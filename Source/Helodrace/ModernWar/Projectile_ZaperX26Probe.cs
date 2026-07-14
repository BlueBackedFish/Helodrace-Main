using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace.ModernWar
{
    public sealed class CompProperties_ZaperX26 : CompProperties
    {
        public CompProperties_ZaperX26()
        {
            compClass = typeof(CompZaperX26);
        }
    }

    /// <summary>
    /// Pairs probe impacts from the same X26. A lone hit expires without
    /// shocking the target, while two hits on the same pawn start the tether.
    /// </summary>
    public sealed class CompZaperX26 : ThingComp
    {
        private const int ProbePairWindowTicks = 12;
        private const int ShockDurationTicks = 300;

        private Pawn pendingTarget;
        private int pendingProbeExpiresAt;
        private ZaperX26TetherEffect activeTether;

        public bool HasActiveTether => activeTether != null && !activeTether.Destroyed && activeTether.Spawned;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref pendingTarget, "zaperPendingTarget");
            Scribe_Values.Look(ref pendingProbeExpiresAt, "zaperPendingProbeExpiresAt");
            Scribe_References.Look(ref activeTether, "zaperActiveTether");
        }

        public void RegisterProbeHit(Pawn launcher, Pawn target, Map map)
        {
            if (launcher == null || target == null || map == null || target.Map != map)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (pendingTarget == target && currentTick <= pendingProbeExpiresAt)
            {
                pendingTarget = null;
                pendingProbeExpiresAt = 0;
                ZaperX26TetherEffect.StartOrRefresh(this, launcher, target, map, ShockDurationTicks);
                return;
            }

            pendingTarget = target;
            pendingProbeExpiresAt = currentTick + ProbePairWindowTicks;
        }

        public void SetActiveTether(ZaperX26TetherEffect tether)
        {
            activeTether = tether;
        }

        public void ClearActiveTether(ZaperX26TetherEffect tether)
        {
            if (activeTether == tether)
            {
                activeTether = null;
            }
        }
    }

    /// <summary>
    /// Prevents this weapon from beginning another attack while it is powering
    /// an active tether. TryCastShot is also guarded for AI and queued casts.
    /// </summary>
    public sealed class Verb_ShootZaperX26 : Verb_Shoot
    {
        private bool TetherActive => EquipmentSource?.TryGetComp<CompZaperX26>()?.HasActiveTether == true;

        public override bool Available()
        {
            return !TetherActive && base.Available();
        }

        protected override bool TryCastShot()
        {
            if (TetherActive || !base.TryCastShot())
            {
                return false;
            }

            Pawn caster = CasterPawn;
            SoundDef mortarLaunch = DefDatabase<SoundDef>.GetNamedSilentFail("Mortar_LaunchA");
            if (caster?.Map != null && mortarLaunch != null)
            {
                SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(caster), MaintenanceType.None);
                soundInfo.volumeFactor = 0.45f;
                SoundStarter.PlayOneShot(mortarLaunch, soundInfo);
            }

            return true;
        }
    }

    /// <summary>
    /// A short-range X26 probe that keeps its deployment wire visually attached
    /// to the pawn that fired it for the duration of its flight.
    /// </summary>
    public sealed class Projectile_ZaperX26Probe : Projectile
    {
        private const float WireWidth = 0.022f;

        private static Material wireMaterial;

        internal static Material WireMaterial => wireMaterial ??
            (wireMaterial = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.30f, 0.28f, 0.24f)));

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map impactMap = Map;
            Pawn firingPawn = Launcher as Pawn;
            Pawn hitPawn = !blockedByShield ? hitThing as Pawn : null;
            CompZaperX26 zaperComp = equipment?.TryGetComp<CompZaperX26>();
            float impactAngle = ExactRotation.eulerAngles.y;
            Thing intendedThing = intendedTarget.Thing;
            ThingDef sourceWeaponDef = equipmentDef;

            base.Impact(hitThing, blockedByShield);

            if (hitPawn != null && firingPawn != null && zaperComp != null)
            {
                bool instigatorGuilty = !firingPawn.Drafted;
                DamageInfo probeDamage = new DamageInfo(
                    DamageDefOf.Bullet,
                    DamageAmount,
                    ArmorPenetration,
                    impactAngle,
                    firingPawn,
                    weapon: sourceWeaponDef,
                    intendedTarget: intendedThing,
                    instigatorGuilty: instigatorGuilty);
                DamageWorker.DamageResult result = hitPawn.TakeDamage(probeDamage);

                // Armor blocks the electrical circuit when no gunshot wound is
                // produced. Such a probe does not count toward the required pair.
                if (result.totalDamageDealt > 0f && result.wounded)
                {
                    zaperComp.RegisterProbeHit(firingPawn, hitPawn, impactMap);
                }
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            DrawFlightWire(drawLoc);
            base.DrawAt(drawLoc, flip);
        }

        private void DrawFlightWire(Vector3 probePosition)
        {
            if (!(Launcher is Pawn pawn) || !pawn.Spawned || pawn.Map != Map)
            {
                return;
            }

            DrawSaggingWire(pawn.DrawPos, probePosition, 0f, WireWidth);
        }

        internal static void DrawSaggingWire(Vector3 start, Vector3 end, float lateralOffset, float width)
        {
            Vector3 direction = end - start;
            Vector3 lateral = direction.sqrMagnitude > 0.001f
                ? new Vector3(-direction.z, 0f, direction.x).normalized * lateralOffset
                : Vector3.zero;
            start += lateral;
            end += lateral;

            float altitude = AltitudeLayer.MoteOverhead.AltitudeFor();
            start.y = altitude;
            end.y = altitude;
            Vector3 midpoint = Vector3.Lerp(start, end, 0.5f);
            midpoint.z -= Mathf.Min(0.12f, Vector3.Distance(start, end) * 0.014f);

            GenDraw.DrawLineBetween(start, midpoint, WireMaterial, width);
            GenDraw.DrawLineBetween(midpoint, end, WireMaterial, width);
        }
    }

    /// <summary>
    /// Persistent paired wires and shock loop after both probes have embedded.
    /// The effect owns the temporary hediff so visuals, sound, and penalties end
    /// on exactly the same tick.
    /// </summary>
    public sealed class ZaperX26TetherEffect : Thing
    {
        private const float MaximumConnectedDistance = 11f;
        private const float ActiveWireWidth = 0.026f;

        private Pawn launcher;
        private Pawn target;
        private ThingWithComps sourceWeapon;
        private int ticksLeft;
        private Hediff shockHediff;
        private Sustainer shockSustainer;

        public static void StartOrRefresh(CompZaperX26 sourceComp, Pawn launcher, Pawn target, Map map, int durationTicks)
        {
            ThingDef effectDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_ZaperX26_TetherEffect");
            if (effectDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_ZaperX26_TetherEffect ThingDef is missing.", 26260001);
                return;
            }

            List<Thing> existingEffects = map.listerThings.ThingsOfDef(effectDef);
            for (int i = 0; i < existingEffects.Count; i++)
            {
                if (existingEffects[i] is ZaperX26TetherEffect existing && existing.target == target)
                {
                    existing.Initialize(sourceComp, launcher, target, durationTicks);
                    return;
                }
            }

            ZaperX26TetherEffect effect = ThingMaker.MakeThing(effectDef) as ZaperX26TetherEffect;
            if (effect == null)
            {
                return;
            }

            GenSpawn.Spawn(effect, target.Position, map);
            effect.Initialize(sourceComp, launcher, target, durationTicks);
        }

        public void Initialize(CompZaperX26 sourceComp, Pawn newLauncher, Pawn newTarget, int durationTicks)
        {
            sourceWeapon?.TryGetComp<CompZaperX26>()?.ClearActiveTether(this);
            sourceWeapon = sourceComp?.parent;
            launcher = newLauncher;
            target = newTarget;
            ticksLeft = Mathf.Max(ticksLeft, durationTicks);
            sourceComp?.SetActiveTether(this);
            EnsureShockHediff();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref launcher, "zaperLauncher");
            Scribe_References.Look(ref target, "zaperTarget");
            Scribe_References.Look(ref sourceWeapon, "zaperSourceWeapon");
            Scribe_Values.Look(ref ticksLeft, "zaperTicksLeft");
        }

        protected override void Tick()
        {
            base.Tick();

            if (!ConnectionIsValid())
            {
                Destroy();
                return;
            }

            EnsureShockHediff();
            MaintainShockSound();
            ticksLeft--;
            if (ticksLeft <= 0)
            {
                Destroy();
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (!ConnectionIsValid())
            {
                return;
            }

            Vector3 start = launcher.DrawPos;
            Vector3 end = target.DrawPos;
            Projectile_ZaperX26Probe.DrawSaggingWire(start, end, -0.045f, ActiveWireWidth);
            Projectile_ZaperX26Probe.DrawSaggingWire(start, end, 0.045f, ActiveWireWidth);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            EndShockSound();
            RemoveShockHediff();
            sourceWeapon?.TryGetComp<CompZaperX26>()?.ClearActiveTether(this);
            base.Destroy(mode);
        }

        private bool ConnectionIsValid()
        {
            return ticksLeft > 0
                && launcher != null
                && target != null
                && !launcher.Destroyed
                && !target.Destroyed
                && launcher.Spawned
                && target.Spawned
                && launcher.Map == Map
                && target.Map == Map
                && launcher.Position.DistanceTo(target.Position) <= MaximumConnectedDistance;
        }

        private void EnsureShockHediff()
        {
            if (target?.health == null)
            {
                return;
            }

            HediffDef shockDef = DefDatabase<HediffDef>.GetNamedSilentFail("HD_ZaperX26_Shock");
            if (shockDef == null)
            {
                return;
            }

            if (shockHediff == null || shockHediff.pawn != target)
            {
                shockHediff = target.health.hediffSet.GetFirstHediffOfDef(shockDef);
            }

            if (shockHediff == null)
            {
                shockHediff = target.health.AddHediff(shockDef);
            }
        }

        private void RemoveShockHediff()
        {
            if (shockHediff != null && target?.health?.hediffSet?.hediffs.Contains(shockHediff) == true)
            {
                target.health.RemoveHediff(shockHediff);
            }

            shockHediff = null;
        }

        private void MaintainShockSound()
        {
            if (shockSustainer == null || shockSustainer.Ended)
            {
                SoundDef loopDef = DefDatabase<SoundDef>.GetNamedSilentFail("HD_ZaperX26_ShockLoop");
                if (loopDef != null)
                {
                    SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(target), MaintenanceType.PerTick);
                    soundInfo.volumeFactor = 7f;
                    shockSustainer = SoundStarter.TrySpawnSustainer(loopDef, soundInfo);
                }
            }

            shockSustainer?.Maintain();
        }

        private void EndShockSound()
        {
            if (shockSustainer != null && !shockSustainer.Ended)
            {
                shockSustainer.End();
            }

            shockSustainer = null;
        }
    }

    /// <summary>
    /// The ZAPER's Blunt tool is its contact-electrode strike. Apply a separate
    /// high-pain pulse only after that melee attack actually deals damage.
    /// Ranged probes use Bullet damage and therefore never enter this path.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_Pawn_PostApplyDamage_ZaperX26Contact
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (__instance == null
                || __instance.Dead
                || totalDamageDealt <= 0f
                || dinfo.Def != DamageDefOf.Blunt
                || dinfo.Weapon?.defName != "HD_Gun_ZaperX26")
            {
                return;
            }

            SoundDef contactSound = DefDatabase<SoundDef>.GetNamedSilentFail("HD_ZaperX26_Contact");
            if (__instance.Map != null && contactSound != null)
            {
                contactSound.PlayOneShot(new TargetInfo(__instance.Position, __instance.Map));
            }

            HediffDef contactShockDef = DefDatabase<HediffDef>.GetNamedSilentFail("HD_ZaperX26_ContactShock");
            if (contactShockDef == null)
            {
                return;
            }

            Hediff existing = __instance.health.hediffSet.GetFirstHediffOfDef(contactShockDef);
            if (existing != null)
            {
                __instance.health.RemoveHediff(existing);
            }

            __instance.health.AddHediff(contactShockDef);
        }
    }
}
