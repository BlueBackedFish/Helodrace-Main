using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    internal static class AntiAirRoundRegistry
    {
        private sealed class RoundState
        {
            public Projectile round;
            public CompMannedAntiAir controller;
            public Thing threat;
            public Vector3 previousRoundPosition;
            public Vector3 previousThreatPosition;
            public bool attemptedIntercept;
        }

        private static readonly Dictionary<int, RoundState> Rounds =
            new Dictionary<int, RoundState>();

        public static void Mark(Projectile projectile, CompMannedAntiAir controller)
        {
            Thing threat = controller?.SelectedThreat;
            if (projectile != null && threat != null)
            {
                controller.ConfigureMissTrajectory(projectile);
                Rounds[projectile.thingIDNumber] = new RoundState
                {
                    round = projectile,
                    controller = controller,
                    threat = threat,
                    previousRoundPosition = projectile.ExactPosition,
                    previousThreatPosition = ThreatPosition(threat)
                };
            }
        }

        public static void Tick(Projectile projectile)
        {
            if (projectile == null
                || !Rounds.TryGetValue(projectile.thingIDNumber, out RoundState state))
            {
                return;
            }

            if (projectile.Destroyed || !projectile.Spawned)
            {
                Rounds.Remove(projectile.thingIDNumber);
                return;
            }

            Thing threat = state.threat;
            CompMannedAntiAir controller = state.controller;
            if (state.attemptedIntercept
                || controller == null
                || !controller.IsThreat(threat))
            {
                state.attemptedIntercept = true;
                return;
            }

            Vector3 currentRoundPosition = projectile.ExactPosition;
            Vector3 currentThreatPosition = ThreatPosition(threat);
            Vector3 relativeStart = state.previousRoundPosition - state.previousThreatPosition;
            Vector3 relativeEnd = currentRoundPosition - currentThreatPosition;
            relativeStart.y = 0f;
            relativeEnd.y = 0f;

            state.previousRoundPosition = currentRoundPosition;
            state.previousThreatPosition = currentThreatPosition;

            float collisionRadius = Mathf.Max(0.1f, controller.PropsForPatches.interceptCollisionRadius);
            if (DistanceToOriginSquared(relativeStart, relativeEnd)
                > collisionRadius * collisionRadius)
            {
                return;
            }

            // Each physical round gets one collision opportunity. A miss keeps
            // flying to the end of its trajectory and resolves its normal ground
            // impact instead of disappearing at the lead point.
            state.attemptedIntercept = true;
            if (controller.TryResolveAntiAirImpact(projectile, threat))
            {
                Rounds.Remove(projectile.thingIDNumber);
            }
        }

        public static bool Contains(Projectile projectile)
        {
            return projectile != null && Rounds.ContainsKey(projectile.thingIDNumber);
        }

        public static void Remove(Projectile projectile)
        {
            if (projectile != null)
            {
                Rounds.Remove(projectile.thingIDNumber);
            }
        }

        private static Vector3 ThreatPosition(Thing threat)
        {
            return threat is Projectile projectile
                ? projectile.ExactPosition
                : threat?.DrawPos ?? Vector3.zero;
        }

        private static float DistanceToOriginSquared(Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.0001f)
            {
                return start.sqrMagnitude;
            }

            float fraction = Mathf.Clamp01(-Vector3.Dot(start, segment) / lengthSquared);
            return (start + segment * fraction).sqrMagnitude;
        }
    }

    /// <summary>
    /// Def-configurable fire-control model for a manned anti-aircraft turret.
    /// Skill weights deliberately live on the building comp so later weapons
    /// can model optical sights, manual directors, radar control, and other
    /// technology without adding more Harmony patches.
    /// </summary>
    public class CompProperties_MannedAntiAir : CompProperties
    {
        public float projectileInterceptChance = 0.075f;
        public float dropPodInterceptChance = 0.025f;
        public int antiAirWarmupTicks = 12;
        public float interceptCollisionRadius = 1.25f;
        public float missTrajectoryRangeFactor = 2f;
        public float shootingSkillWeight = 1f;
        public float intellectualSkillWeight;
        public bool interceptOverheadProjectiles = true;
        public bool interceptDirectProjectiles;
        public bool interceptDropPods = true;
        public float dropPodCrashExplosionRadius = 2.2f;
        public int dropPodCrashDamage = 28;

        public CompProperties_MannedAntiAir()
        {
            compClass = typeof(CompMannedAntiAir);
        }
    }

    public class CompMannedAntiAir : ThingComp
    {
        private static readonly FieldInfo ProjectileTicksToImpactField =
            AccessTools.Field(typeof(Projectile), "ticksToImpact");
        private static readonly FieldInfo ProjectileDestinationField =
            AccessTools.Field(typeof(Projectile), "destination");
        private static readonly FieldInfo ProjectileOriginField =
            AccessTools.Field(typeof(Projectile), "origin");
        private static readonly FieldInfo ProjectileLifetimeField =
            AccessTools.Field(typeof(Projectile), "lifetime");
        private static readonly FieldInfo SkyfallerTicksToImpactField =
            AccessTools.Field(typeof(Skyfaller), "ticksToImpact");
        private static readonly MethodInfo DropPodImpactMethod =
            AccessTools.Method(typeof(DropPodIncoming), "Impact");
        private static readonly MethodInfo ResetForcedTargetMethod =
            AccessTools.Method(typeof(Building_TurretGun), "ResetForcedTarget");
        private static readonly MethodInfo ResetCurrentTargetMethod =
            AccessTools.Method(typeof(Building_TurretGun), "ResetCurrentTarget");
        private static readonly MethodInfo TryStartShootSomethingMethod =
            AccessTools.Method(typeof(Building_TurretGun), "TryStartShootSomething");
        private static readonly MethodInfo BeginBurstMethod =
            AccessTools.Method(typeof(Building_TurretGun), "BeginBurst");
        private static readonly FieldInfo BurstCooldownTicksLeftField =
            AccessTools.Field(typeof(Building_TurretGun), "burstCooldownTicksLeft");
        private static readonly FieldInfo BurstWarmupTicksLeftField =
            AccessTools.Field(typeof(Building_TurretGun), "burstWarmupTicksLeft");
        private static readonly FieldInfo CurrentTargetIntField =
            AccessTools.Field(typeof(Building_TurretGun), "currentTargetInt");

        private bool antiAirMode;
        private Thing selectedThreat;
        private static HashSet<ThingDef> fragmentProjectileDefs;

        private CompProperties_MannedAntiAir Props =>
            (CompProperties_MannedAntiAir)props;

        public bool AntiAirMode => antiAirMode;
        public CompProperties_MannedAntiAir PropsForPatches => Props;
        public Thing SelectedThreat => selectedThreat;

        public Pawn Gunner
        {
            get
            {
                Pawn pawn = parent.TryGetComp<CompMannable>()?.ManningPawn;
                return pawn != null
                    && pawn.Spawned
                    && !pawn.Dead
                    && !pawn.Downed
                    && pawn.Map == parent.Map
                    ? pawn
                    : null;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref antiAirMode, "antiAirMode", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                selectedThreat = null;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            Building_TurretGun turret = parent as Building_TurretGun;
            if (!antiAirMode
                || Gunner == null
                || turret == null
                || turret.AttackVerb?.WarmingUp == true
                || turret.AttackVerb?.Bursting == true)
            {
                return;
            }

            int cooldownTicks = BurstCooldownTicksLeftField != null
                ? (int)BurstCooldownTicksLeftField.GetValue(turret)
                : 0;
            int warmupTicks = BurstWarmupTicksLeftField != null
                ? (int)BurstWarmupTicksLeftField.GetValue(turret)
                : 0;
            if (warmupTicks > 0)
            {
                // Building_TurretGun's pre-burst spin-up is separate from
                // Verb.WarmingUp. Reacquiring here would reset this counter on
                // every tick and make the gun track forever without firing.
                return;
            }

            if (cooldownTicks <= 0)
            {
                if (CanContinueLockedBurst
                    && turret.AttackVerb?.Available() == true
                    && CurrentTargetIntField != null
                    && BeginBurstMethod != null)
                {
                    CurrentTargetIntField.SetValue(
                        turret,
                        new LocalTargetInfo(selectedThreat));
                    BeginBurstMethod.Invoke(turret, null);
                    turret.Top?.TurretTopTick();
                    return;
                }

                Thing threat = SelectThreat();
                if (threat == null || turret.AttackVerb?.Available() != true)
                {
                    return;
                }

                // Keep only Anti-Air: CIWS's direct lock-on pattern. The M167
                // uses its own short spin-up phase so the barrels visibly begin
                // rotating as soon as the target is acquired.
                if (CurrentTargetIntField != null && BurstWarmupTicksLeftField != null)
                {
                    CurrentTargetIntField.SetValue(turret, new LocalTargetInfo(threat));
                    BurstWarmupTicksLeftField.SetValue(
                        turret,
                        Mathf.Max(1, Props.antiAirWarmupTicks));
                    turret.Top?.TurretTopTick();
                }
                else
                {
                    // Retain a safe fallback if a future RimWorld update renames
                    // the private target field.
                    TryStartShootSomethingMethod?.Invoke(turret, new object[] { true });
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            yield return new Command_Toggle
            {
                defaultLabel = "HD_AntiAir_Mode_Label".Translate(),
                defaultDesc = "HD_AntiAir_Mode_Desc".Translate(),
                icon = TexCommand.FireAtWill,
                isActive = () => antiAirMode,
                toggleAction = ToggleAntiAirMode
            };
        }

        public override string CompInspectStringExtra()
        {
            string status = antiAirMode
                ? "HD_AntiAir_StatusOn".Translate()
                : "HD_AntiAir_StatusOff".Translate();
            string result = "HD_AntiAir_InspectMode".Translate(status);

            if (!antiAirMode)
            {
                return result;
            }

            Pawn gunner = Gunner;
            if (gunner == null)
            {
                return result + "\n" + "HD_AntiAir_NoGunner".Translate();
            }

            float skillFactor = OperatorSkillFactor(gunner);
            float shootingWeight = Mathf.Max(0f, Props.shootingSkillWeight);
            float intellectualWeight = Mathf.Max(0f, Props.intellectualSkillWeight);
            float totalWeight = shootingWeight + intellectualWeight;
            string weights = "HD_AntiAir_OperatorFactors".Translate(
                (totalWeight > 0f ? shootingWeight / totalWeight : 0f).ToStringPercent(),
                (totalWeight > 0f ? intellectualWeight / totalWeight : 0f).ToStringPercent());
            string chances = "HD_AntiAir_Chance".Translate(
                Mathf.Clamp01(Props.projectileInterceptChance * skillFactor).ToStringPercent(),
                Mathf.Clamp01(Props.dropPodInterceptChance * skillFactor).ToStringPercent());
            return result + "\n" + weights + "\n" + chances;
        }

        public Thing SelectThreat()
        {
            selectedThreat = null;
            if (!antiAirMode || Gunner == null || parent.Map == null)
            {
                return null;
            }

            float range = (parent as Building_TurretGun)?.AttackVerb?.EffectiveRange ?? 0f;
            float minimumRange = (parent as Building_TurretGun)?.AttackVerb?.verbProps?.minRange ?? 0f;
            float rangeSquared = range * range;
            float minimumRangeSquared = minimumRange * minimumRange;

            selectedThreat = parent.Map.listerThings.AllThings
                .Where(IsThreat)
                .Where(thing =>
                {
                    float distanceSquared = parent.Position.DistanceToSquared(thing.Position);
                    return distanceSquared <= rangeSquared && distanceSquared >= minimumRangeSquared;
                })
                .OrderByDescending(ThreatPriority)
                .FirstOrDefault();
            return selectedThreat;
        }

        public bool CanContinueLockedBurst =>
            antiAirMode
            && Gunner != null
            && selectedThreat != null
            && CurrentLeadTarget().IsValid;

        public LocalTargetInfo CurrentLeadTarget()
        {
            if (!IsThreat(selectedThreat) || parent.Map == null)
            {
                return LocalTargetInfo.Invalid;
            }

            Building_TurretGun turret = parent as Building_TurretGun;
            float effectiveRange = turret?.AttackVerb?.EffectiveRange ?? 0f;
            float minimumRange = turret?.AttackVerb?.verbProps?.minRange ?? 0f;
            float currentDistanceSquared = parent.Position.DistanceToSquared(selectedThreat.Position);
            if (currentDistanceSquared > effectiveRange * effectiveRange
                || currentDistanceSquared < minimumRange * minimumRange)
            {
                return LocalTargetInfo.Invalid;
            }

            Vector3 aimPosition = selectedThreat.DrawPos;
            if (selectedThreat is Projectile projectile)
            {
                aimPosition = ProjectileLeadPosition(projectile);
            }

            Vector3 muzzlePosition = parent.DrawPos;
            Vector3 offset = aimPosition - muzzlePosition;
            offset.y = 0f;
            float maximumRange = Mathf.Max(
                0.5f,
                effectiveRange - 0.25f);
            if (offset.sqrMagnitude <= 0.0001f)
            {
                return LocalTargetInfo.Invalid;
            }

            // Aim through the calculated intercept point to the edge of the
            // weapon's range. A missed round therefore continues beyond the
            // airborne target and makes its normal impact on the ground.
            aimPosition = muzzlePosition + offset.normalized * maximumRange;

            IntVec3 aimCell = aimPosition.ToIntVec3();
            aimCell.x = Mathf.Clamp(aimCell.x, 0, parent.Map.Size.x - 1);
            aimCell.z = Mathf.Clamp(aimCell.z, 0, parent.Map.Size.z - 1);
            return new LocalTargetInfo(aimCell);
        }

        public bool IsThreat(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != parent.Map)
            {
                return false;
            }

            if (thing is Projectile projectile)
            {
                if (AntiAirRoundRegistry.Contains(projectile))
                {
                    return false;
                }

                if (IsFragmentProjectile(projectile.def))
                {
                    return false;
                }

                bool permittedTrajectory = projectile.def?.projectile?.flyOverhead == true
                    ? Props.interceptOverheadProjectiles
                    : Props.interceptDirectProjectiles;
                return permittedTrajectory;
            }

            return Props.interceptDropPods
                && thing is DropPodIncoming;
        }

        public void ConfigureMissTrajectory(Projectile projectile)
        {
            if (projectile == null
                || ProjectileOriginField == null
                || ProjectileDestinationField == null
                || ProjectileTicksToImpactField == null)
            {
                return;
            }

            Vector3 origin = (Vector3)ProjectileOriginField.GetValue(projectile);
            Vector3 destination = (Vector3)ProjectileDestinationField.GetValue(projectile);
            Vector3 direction = destination - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            float baseRange = (parent as Building_TurretGun)?.AttackVerb?.EffectiveRange ?? 0f;
            float travelDistance = Mathf.Max(
                1f,
                baseRange * Mathf.Max(1f, Props.missTrajectoryRangeFactor));
            Vector3 extendedDestination = origin + direction.normalized * travelDistance;
            extendedDestination.y = destination.y;
            ProjectileDestinationField.SetValue(projectile, extendedDestination);

            float speed = Mathf.Max(
                0.001f,
                projectile.def?.projectile?.SpeedTilesPerTick ?? 0.001f);
            int flightTicks = Mathf.Max(1, Mathf.CeilToInt(travelDistance / speed));
            ProjectileTicksToImpactField.SetValue(projectile, flightTicks);
            ProjectileLifetimeField?.SetValue(projectile, flightTicks);
        }

        public bool TryResolveAntiAirImpact(Projectile interceptor, Thing threat)
        {
            Pawn gunner = Gunner;
            if (!antiAirMode
                || interceptor == null
                || interceptor.Destroyed
                || gunner == null
                || !IsThreat(threat))
            {
                return false;
            }

            bool dropPod = threat is DropPodIncoming;
            float baseChance = dropPod
                ? Props.dropPodInterceptChance
                : Props.projectileInterceptChance;
            if (!Rand.Chance(Mathf.Clamp01(baseChance * OperatorSkillFactor(gunner))))
            {
                return false;
            }

            Map map = threat.Map;
            IntVec3 interceptCell = threat.Position;

            if (dropPod)
            {
                InterceptDropPod((DropPodIncoming)threat, gunner);
            }
            else
            {
                InterceptProjectile((Projectile)threat);
            }

            selectedThreat = null;
            if (interceptor is Projectile_Explosive)
            {
                if (map != null && interceptCell.InBounds(map))
                {
                    interceptor.Position = interceptCell;
                }

                // Invoke the concrete projectile's Explode override so HEI and
                // HEI-T retain their configured blast, fragments, and ignition.
                AccessTools.Method(interceptor.GetType(), "Explode")?.Invoke(interceptor, null);
            }
            else
            {
                // API has no airburst: both the incoming threat and the striking
                // armor-piercing round simply vanish at the interception point.
                interceptor.Destroy(DestroyMode.Vanish);
                if (map != null && interceptCell.InBounds(map))
                {
                    FleckMaker.Static(interceptCell, map, FleckDefOf.ExplosionFlash, 0.55f);
                    FleckMaker.ThrowMicroSparks(interceptCell.ToVector3Shifted(), map);
                }
            }

            return true;
        }

        private void ToggleAntiAirMode()
        {
            antiAirMode = !antiAirMode;
            selectedThreat = null;
            if (parent is Building_TurretGun turret)
            {
                ResetForcedTargetMethod?.Invoke(turret, null);
                ResetCurrentTargetMethod?.Invoke(turret, null);
            }
        }

        private float OperatorSkillFactor(Pawn pawn)
        {
            float shootingWeight = Mathf.Max(0f, Props.shootingSkillWeight);
            float intellectualWeight = Mathf.Max(0f, Props.intellectualSkillWeight);
            float totalWeight = shootingWeight + intellectualWeight;
            if (pawn == null || totalWeight <= 0f)
            {
                return 1f;
            }

            int shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
            int intellectual = pawn.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            float skillFactor = (shootingWeight * SkillLevelFactor(shooting)
                + intellectualWeight * SkillLevelFactor(intellectual)) / totalWeight;

            if (shootingWeight > 0f)
            {
                float condition = Mathf.Clamp(
                    pawn.GetStatValue(StatDefOf.ShootingAccuracyPawn),
                    0.75f,
                    1.15f);
                skillFactor *= Mathf.Lerp(1f, condition, shootingWeight / totalWeight);
            }

            return Mathf.Clamp(skillFactor, 0.25f, 2f);
        }

        private Vector3 ProjectileLeadPosition(Projectile target)
        {
            Vector3 currentPosition = target.ExactPosition;
            int remainingTicks = ProjectileTicksToImpactField != null
                ? Mathf.Max(1, (int)ProjectileTicksToImpactField.GetValue(target))
                : 1;
            Vector3 destination = ProjectileDestinationField != null
                ? (Vector3)ProjectileDestinationField.GetValue(target)
                : currentPosition;
            Vector3 targetVelocity = (destination - currentPosition) / remainingTicks;
            targetVelocity.y = 0f;

            ThingDef outgoingProjectile =
                ((parent as Building_TurretGun)?.AttackVerb as Verb_LaunchProjectile)?.Projectile;
            float outgoingSpeed = Mathf.Max(
                0.01f,
                outgoingProjectile?.projectile?.SpeedTilesPerTick ?? 1f);
            Vector3 muzzlePosition = parent.DrawPos;
            Vector3 predictedPosition = currentPosition;
            float leadTicks = 0f;

            // A few fixed-point iterations are sufficient because RimWorld
            // projectiles move linearly in the horizontal map plane.
            for (int iteration = 0; iteration < 4; iteration++)
            {
                Vector3 difference = predictedPosition - muzzlePosition;
                difference.y = 0f;
                leadTicks = Mathf.Min(difference.magnitude / outgoingSpeed, remainingTicks);
                predictedPosition = currentPosition + targetVelocity * leadTicks;
            }

            return predictedPosition;
        }

        private static float SkillLevelFactor(int level)
        {
            return 0.5f + Mathf.Clamp(level, 0, 20) * 0.05f;
        }

        private static bool IsFragmentProjectile(ThingDef projectileDef)
        {
            if (projectileDef == null)
            {
                return false;
            }

            if (fragmentProjectileDefs == null)
            {
                fragmentProjectileDefs = new HashSet<ThingDef>();
                foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    ThingDef fragmentDef = thingDef
                        .GetModExtension<ModernWar.FragmentationGrenadeExtension>()
                        ?.fragmentProjectile;
                    if (fragmentDef != null)
                    {
                        fragmentProjectileDefs.Add(fragmentDef);
                    }
                }
            }

            return fragmentProjectileDefs.Contains(projectileDef)
                || projectileDef.defName?.IndexOf(
                    "fragment",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private float ThreatPriority(Thing thing)
        {
            if (thing is DropPodIncoming skyfaller)
            {
                int ticks = SkyfallerTicksToImpactField != null
                    ? (int)SkyfallerTicksToImpactField.GetValue(skyfaller)
                    : 0;
                return 100000f - ticks;
            }

            if (thing is Projectile projectile)
            {
                int ticks = ProjectileTicksToImpactField != null
                    ? (int)ProjectileTicksToImpactField.GetValue(projectile)
                    : 0;
                return 50000f - ticks;
            }

            return 0f;
        }

        private static void InterceptProjectile(Projectile projectile)
        {
            Map map = projectile.Map;
            IntVec3 cell = projectile.Position;
            projectile.Destroy(DestroyMode.Vanish);
            if (map != null && cell.InBounds(map))
            {
                FleckMaker.Static(cell, map, FleckDefOf.ExplosionFlash, 1.2f);
                FleckMaker.ThrowMicroSparks(cell.ToVector3Shifted(), map);
            }
        }

        private void InterceptDropPod(DropPodIncoming dropPod, Pawn gunner)
        {
            Map map = dropPod.Map;
            IntVec3 cell = dropPod.Position;
            try
            {
                // Force the pod to disgorge its contents before applying crash
                // damage. Destroying the skyfaller directly can silently delete
                // the pawns it owns.
                DropPodImpactMethod?.Invoke(dropPod, null);
                if (map != null && cell.InBounds(map))
                {
                    GenExplosion.DoExplosion(
                        cell,
                        map,
                        Props.dropPodCrashExplosionRadius,
                        DamageDefOf.Bomb,
                        gunner,
                        Props.dropPodCrashDamage,
                        armorPenetration: 0.2f);
                }
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "Helodrace: failed to resolve an intercepted drop pod. " + exception,
                    10523041);
            }
        }
    }

    [HarmonyPatch(typeof(Building_TurretGun), "TryFindNewTarget")]
    public static class Patch_BuildingTurretGun_TryFindNewTarget_AntiAir
    {
        public static bool Prefix(Building_TurretGun __instance, ref LocalTargetInfo __result)
        {
            CompMannedAntiAir antiAir = __instance?.TryGetComp<CompMannedAntiAir>();
            if (antiAir?.AntiAirMode != true)
            {
                return true;
            }

            Thing threat = antiAir.SelectThreat();
            __result = threat != null
                ? new LocalTargetInfo(threat)
                : LocalTargetInfo.Invalid;
            return false;
        }
    }

    [HarmonyPatch(typeof(Building_TurretGun), "TryStartShootSomething")]
    public static class Patch_BuildingTurretGun_TryStartShootSomething_AntiAir
    {
        public static bool Prefix(
            Building_TurretGun __instance,
            ref LocalTargetInfo ___currentTargetInt,
            ref LocalTargetInfo ___forcedTarget,
            ref int ___burstWarmupTicksLeft)
        {
            CompMannedAntiAir antiAir = __instance?.TryGetComp<CompMannedAntiAir>();
            if (antiAir?.AntiAirMode != true)
            {
                return true;
            }

            Thing lockedThreat = ___currentTargetInt.Thing;
            if (___burstWarmupTicksLeft > 0
                && lockedThreat != null
                && lockedThreat == antiAir.SelectedThreat
                && antiAir.CurrentLeadTarget().IsValid)
            {
                // Building_TurretGun performs its periodic acquisition check
                // even while the burst warmup is counting down. Preserve the
                // existing lock so that check cannot restart the spin-up timer.
                return false;
            }

            ___forcedTarget = LocalTargetInfo.Invalid;
            Thing threat = antiAir.SelectThreat();
            ___currentTargetInt = threat != null
                ? new LocalTargetInfo(threat)
                : LocalTargetInfo.Invalid;
            if (threat != null)
            {
                ___burstWarmupTicksLeft = Mathf.Max(
                    1,
                    antiAir.PropsForPatches.antiAirWarmupTicks);
                __instance.Top?.TurretTopTick();
            }

            // The AA comp owns acquisition and spin-up. Do not let vanilla
            // overwrite the airborne target with a ground-combat search.
            return false;
        }
    }

    [HarmonyPatch(typeof(Building_TurretGun), "BeginBurst")]
    public static class Patch_BuildingTurretGun_BeginBurst_AntiAir
    {
        public static bool Prefix(
            Building_TurretGun __instance,
            ref LocalTargetInfo ___currentTargetInt)
        {
            CompMannedAntiAir antiAir = __instance?.TryGetComp<CompMannedAntiAir>();
            if (antiAir?.AntiAirMode != true)
            {
                return true;
            }

            return antiAir.CurrentLeadTarget().IsValid;
        }
    }

    [HarmonyPatch(typeof(Building_TurretGun), "BurstComplete")]
    public static class Patch_BuildingTurretGun_BurstComplete_AntiAir
    {
        public static void Postfix(
            Building_TurretGun __instance,
            ref int ___burstCooldownTicksLeft)
        {
            CompMannedAntiAir antiAir = __instance?.TryGetComp<CompMannedAntiAir>();
            if (antiAir?.CanContinueLockedBurst == true)
            {
                // Continue engaging the same airborne target without the
                // normal between-burst pause. CompTick starts the next burst
                // immediately and does not repeat the initial barrel spin-up.
                ___burstCooldownTicksLeft = 0;
            }
        }
    }

    [HarmonyPatch(typeof(Building_TurretGun), "IsValidTarget")]
    public static class Patch_BuildingTurretGun_IsValidTarget_AntiAir
    {
        public static void Postfix(Building_TurretGun __instance, Thing t, ref bool __result)
        {
            CompMannedAntiAir antiAir = __instance?.TryGetComp<CompMannedAntiAir>();
            if (antiAir?.AntiAirMode == true && antiAir.IsThreat(t))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot_AntiAir
    {
        public struct AntiAirShotState
        {
            public bool targetReplaced;
            public LocalTargetInfo originalTarget;
        }

        private static readonly FieldInfo CurrentTargetField =
            AccessTools.Field(typeof(Verb), "currentTarget");

        public static bool Prefix(
            Verb_LaunchProjectile __instance,
            ref AntiAirShotState __state)
        {
            if (!(__instance?.Caster is Building_TurretGun turret))
            {
                return true;
            }

            CompMannedAntiAir antiAir = turret.TryGetComp<CompMannedAntiAir>();
            if (antiAir?.AntiAirMode != true)
            {
                return true;
            }

            __state.targetReplaced = true;
            __state.originalTarget = __instance.CurrentTarget;
            LocalTargetInfo leadTarget = antiAir.CurrentLeadTarget();
            if (!leadTarget.IsValid)
            {
                return false;
            }

            CurrentTargetField?.SetValue(__instance, leadTarget);
            return true;
        }

        public static void Postfix(
            Verb_LaunchProjectile __instance,
            bool __result,
            AntiAirShotState __state)
        {
            if (__state.targetReplaced && CurrentTargetField != null)
            {
                CurrentTargetField.SetValue(__instance, __state.originalTarget);
            }
        }
    }

    [HarmonyPatch(typeof(Verb), "TryFindShootLineFromTo")]
    public static class Patch_Verb_TryFindShootLineFromTo_AntiAir
    {
        public static bool Prefix(
            Verb __instance,
            IntVec3 root,
            LocalTargetInfo targ,
            ref ShootLine resultingLine,
            ref bool __result)
        {
            if (!(__instance?.Caster is Building_TurretGun turret))
            {
                return true;
            }

            CompMannedAntiAir antiAir = turret.TryGetComp<CompMannedAntiAir>();
            if (antiAir?.AntiAirMode != true)
            {
                return true;
            }

            LocalTargetInfo fireTarget = antiAir.CurrentLeadTarget();
            float distanceSquared = root.DistanceToSquared(fireTarget.Cell);
            float maximumRange = __instance.EffectiveRange;
            float minimumRange = __instance.verbProps?.minRange ?? 0f;
            resultingLine = new ShootLine(root, fireTarget.Cell);
            __result = fireTarget.IsValid
                && fireTarget.Cell.InBounds(turret.Map)
                && distanceSquared <= maximumRange * maximumRange
                && distanceSquared >= minimumRange * minimumRange;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(Projectile),
        nameof(Projectile.Launch),
        new[]
        {
            typeof(Thing),
            typeof(Vector3),
            typeof(LocalTargetInfo),
            typeof(LocalTargetInfo),
            typeof(ProjectileHitFlags),
            typeof(bool),
            typeof(Thing),
            typeof(ThingDef)
        })]
    public static class Patch_Projectile_Launch_MarkAntiAirRound
    {
        public static void Postfix(
            Projectile __instance,
            Thing launcher,
            Thing equipment)
        {
            CompMannedAntiAir antiAir = launcher?.TryGetComp<CompMannedAntiAir>()
                ?? equipment?.TryGetComp<CompMannedAntiAir>();
            if (antiAir?.AntiAirMode == true)
            {
                AntiAirRoundRegistry.Mark(__instance, antiAir);
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), "Tick")]
    public static class Patch_Projectile_Tick_AntiAirRound
    {
        public static void Postfix(Projectile __instance)
        {
            AntiAirRoundRegistry.Tick(__instance);
        }
    }

    [HarmonyPatch(typeof(Projectile), "CheckForFreeIntercept")]
    public static class Patch_Projectile_CheckForFreeIntercept_AntiAirRound
    {
        public static bool Prefix(Projectile __instance, ref bool __result)
        {
            if (!AntiAirRoundRegistry.Contains(__instance))
            {
                return true;
            }

            // AA rounds use a mortar-like flight lane: walls, doors, pawns,
            // and cover cannot catch them between muzzle and destination.
            // CheckForFreeInterceptBetween still runs its separate projectile-
            // interceptor pass, and ImpactSomething still resolves the final
            // ground impact when the doubled-range destination is inside map.
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Projectile_Explosive), "Impact")]
    public static class Patch_ProjectileExplosive_Impact_AntiAirRound
    {
        public static bool Prefix(Projectile_Explosive __instance)
        {
            return AllowGroundImpact(__instance);
        }

        private static bool AllowGroundImpact(Projectile projectile)
        {
            AntiAirRoundRegistry.Remove(projectile);
            return true;
        }
    }

    [HarmonyPatch(typeof(Projectile_20mmArmorPiercingIncendiary), "Impact")]
    public static class Patch_Projectile20mmApi_Impact_AntiAirRound
    {
        public static bool Prefix(Projectile_20mmArmorPiercingIncendiary __instance)
        {
            AntiAirRoundRegistry.Remove(__instance);
            return true;
        }
    }
}
