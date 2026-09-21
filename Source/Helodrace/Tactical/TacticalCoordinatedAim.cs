using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Tactical
{
    public static class TacticalCoordinatedAimUtility
    {
        private const string CqbTrainingDefName = "HD_CQBTraining";
        private const float PartnerMaximumDistance = 1.5f;
        private const float TargetHalfAngle = 30f;
        private const float FinalAccuracyMultiplier = 1.20f;
        private const int ReportLifetimeTicks = 2;

        private sealed class State
        {
            public Pawn shooter;
            public Pawn partner;
            public Verb verb;
            public IntVec3 shooterOrigin;
            public IntVec3 partnerOrigin;
            public Pawn target;
            public BodyPartRecord part;
            public int shotCount;
            public int expiryTick;
        }

        private struct ReportState
        {
            public int tick;
        }

        private static readonly Dictionary<int, State> states =
            new Dictionary<int, State>();
        private static readonly Dictionary<int, ReportState> reports =
            new Dictionary<int, ReportState>();
        private static readonly FieldInfo FactorFromShooterAndDistField =
            AccessTools.Field(typeof(ShotReport), "factorFromShooterAndDist");
        private static readonly FieldInfo FactorFromEquipmentField =
            AccessTools.Field(typeof(ShotReport), "factorFromEquipment");

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        public static bool HasAccess(Pawn pawn)
        {
            Verb verb = PrimaryVerb(pawn);
            return pawn?.Faction == Faction.OfPlayer
                && pawn.Map != null
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Drafted
                && HasCQBTraining(pawn)
                && TacticalAimUtility.IsRangedVerb(verb)
                && !verb.Bursting
                && !states.ContainsKey(pawn.thingIDNumber);
        }

        public static bool IsActive(Pawn pawn)
        {
            return pawn != null
                && states.TryGetValue(pawn.thingIDNumber, out State state)
                && state.shooter == pawn;
        }

        public static bool IsValidShot(
            Pawn shooter,
            Verb verb,
            LocalTargetInfo target)
        {
            if (!IsActive(shooter)
                || !states.TryGetValue(shooter.thingIDNumber, out State state)
                || state.verb != verb
                || !target.IsValid
                || (state.target != null && target.Thing != state.target)
                || (target.Thing is Pawn victim && (victim.Dead || victim.Downed))
                || target.Thing == shooter
                || target.Thing == state.partner
                || (target.HasThing && target.Thing.Map != shooter.Map))
            {
                return false;
            }

            return IsFormationValid(state)
                && TargetInPartnerArc(state, target);
        }

        public static void BeginTargeting(Pawn pawn)
        {
            if (!HasAccess(pawn))
            {
                return;
            }

            Map map = pawn.Map;
            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetPawns = true,
                    canTargetBuildings = false,
                    canTargetLocations = false,
                    validator = target => CanSelectPartner(
                        pawn,
                        target.Thing as Pawn)
                },
                target => BeginAttackTargeting(pawn, target.Thing as Pawn),
                target => DrawPartnerPreview(pawn, map, target));

            Helodrace.MapComponent_PersistentTargetingOverlay.Set(
                map,
                target => DrawPartnerPreview(pawn, map, target));
        }

        private static void DrawPartnerPreview(
            Pawn pawn,
            Map map,
            LocalTargetInfo target)
        {
            if (pawn?.Map != map)
            {
                return;
            }

            GenDraw.DrawRadiusRing(pawn.Position, PartnerMaximumDistance, Color.white);
            Pawn partner = target.Thing as Pawn;
            if (partner != null && partner.Map == map)
            {
                GenDraw.DrawRadiusRing(
                    partner.Position,
                    0.55f,
                    CanSelectPartner(pawn, partner) ? Color.green : Color.red);
            }
        }

        private static bool CanSelectPartner(Pawn pawn, Pawn partner)
        {
            return pawn != null
                && partner != null
                && partner != pawn
                && partner.Map == pawn.Map
                && partner.Spawned
                && !partner.Dead
                && !partner.Downed
                && AreAdjacent(pawn.Position, partner.Position)
                && GenSight.LineOfSight(
                    pawn.Position,
                    partner.Position,
                    pawn.Map);
        }

        private static bool AreAdjacent(IntVec3 shooter, IntVec3 partner)
        {
            int distance = shooter.DistanceToSquared(partner);
            return distance > 0 && distance <= 2;
        }

        private static BodyPartRecord VitalPart(Pawn shooter, Pawn target)
        {
            TacticalPrecisionPart selected = TacticalPrecisionFireUtility.SelectedPart(shooter);
            if (selected != TacticalPrecisionPart.None)
                return TacticalPrecisionFireUtility.FindAvailablePart(target, selected);
            return target?.health?.hediffSet?.GetNotMissingParts()
                .Where(part => part.def.defName == "Head" || part.def.defName == "Brain"
                    || part.def.defName == "Heart" || part.def.defName == "Neck")
                .OrderBy(part => part.def.defName == "Head" ? 0 : 1).FirstOrDefault();
        }

        private static void BeginAttackTargeting(Pawn pawn, Pawn partner)
        {
            if (!HasAccess(pawn) || !CanSelectPartner(pawn, partner)) return;
            var preview = new State { shooter = pawn, partner = partner, verb = PrimaryVerb(pawn),
                shooterOrigin = pawn.Position, partnerOrigin = partner.Position };
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetPawns = true, canTargetBuildings = false, canTargetLocations = false,
                validator = info => info.Thing is Pawn victim && victim != pawn && victim != partner
                    && !victim.Dead && !victim.Downed && VitalPart(pawn, victim) != null && IsFormationValid(preview)
                    && TargetInPartnerArc(preview, new LocalTargetInfo(victim))
            }, target => Start(pawn, partner, target.Thing as Pawn));
        }

        public static int BurstCount(Verb verb)
        {
            return verb?.Caster is Pawn pawn && states.TryGetValue(pawn.thingIDNumber, out State state)
                && state.verb == verb ? state.shotCount : 0;
        }

        private static void Start(Pawn pawn, Pawn partner, Pawn target)
        {
            Verb verb = PrimaryVerb(pawn);
            if (!HasAccess(pawn)
                || !CanSelectPartner(pawn, partner)
                || verb == null || target == null || target.Dead || target.Downed || VitalPart(pawn, target) == null)
            {
                return;
            }

            int count = TacticalWeaponRules.CoordinatedShots(TacticalWeaponRules.BurstCount(verb));
            State state = new State
            {
                shooter = pawn,
                partner = partner,
                verb = verb,
                shooterOrigin = pawn.Position,
                partnerOrigin = partner.Position,
                target = target,
                part = VitalPart(pawn, target),
                shotCount = count,
                expiryTick = CurrentTick + 3600
            };
            if (!TargetInPartnerArc(state, new LocalTargetInfo(target))) return;
            states[pawn.thingIDNumber] = state;
            if (!verb.TryStartCastOn(new LocalTargetInfo(target), false, false, false, false))
            {
                Cancel(pawn);
                return;
            }
            Messages.Message(
                "HD_TacticalCoordinatedAim_Started".Translate(partner.LabelShort),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public static void NotifyPawnTick(Pawn pawn)
        {
            if (!IsActive(pawn)
                || !states.TryGetValue(pawn.thingIDNumber, out State state))
            {
                return;
            }

            if (!IsFormationValid(state) || state.target == null || state.target.Dead || state.target.Downed
                || !state.target.Spawned || CurrentTick > state.expiryTick
                || (!state.verb.Bursting
                    && !(pawn.stances?.curStance is Stance_Warmup)))
            {
                Cancel(pawn);
            }
        }

        private static bool IsFormationValid(State state)
        {
            return state.shooter != null
                && state.partner != null
                && state.shooter.Spawned
                && state.partner.Spawned
                && !state.shooter.Dead
                && !state.partner.Dead
                && !state.shooter.Downed
                && state.shooter.Drafted
                && PrimaryVerb(state.shooter) == state.verb
                && !state.partner.Downed
                && state.shooter.Map != null
                && state.shooter.Map == state.partner.Map
                && state.shooter.Position == state.shooterOrigin
                && state.partner.Position == state.partnerOrigin
                && CanSelectPartner(state.shooter, state.partner);
        }

        private static bool TargetInPartnerArc(
            State state,
            LocalTargetInfo target)
        {
            if (!target.IsValid
                || !target.Cell.InBounds(state.shooter.Map)
                || state.verb == null
                || !state.verb.CanHitTarget(target))
            {
                return false;
            }

            Vector3 direction = target.HasThing
                ? target.Thing.DrawPos - state.shooter.DrawPos
                : target.Cell.ToVector3Shifted() - state.shooter.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return false;
            }

            float targetAngle = direction.AngleFlat();
            float referenceAngle = (state.partner.DrawPos - state.shooter.DrawPos).AngleFlat();
            return IsWithinArc(referenceAngle, targetAngle)
                && state.shooter.Position.DistanceTo(target.Cell)
                    <= state.verb.verbProps.range
                && GenSight.LineOfSight(
                    state.shooter.Position,
                    target.Cell,
                    state.shooter.Map);
        }

        private static bool IsWithinArc(float referenceAngle, float targetAngle)
        {
            return Math.Abs(TacticalAimUtility.NormalizeAngle(targetAngle - referenceAngle + 180f)
                - 180f) <= TargetHalfAngle;
        }

        public static void RegisterReport(
            Pawn shooter,
            Verb verb,
            LocalTargetInfo target,
            ref ShotReport report)
        {
            if (!IsValidShot(shooter, verb, target))
            {
                return;
            }

            object boxedReport = report;
            if (FactorFromShooterAndDistField != null)
            {
                FactorFromShooterAndDistField.SetValue(boxedReport, 1f);
            }

            if (FactorFromEquipmentField != null)
            {
                FactorFromEquipmentField.SetValue(boxedReport, 1f);
            }

            report = (ShotReport)boxedReport;
            reports[report.GetHashCode()] = new ReportState
            {
                tick = CurrentTick
            };
            PruneReports();
        }

        private static void PruneReports()
        {
            if (reports.Count <= 256)
            {
                return;
            }

            List<int> expired = reports
                .Where(pair => CurrentTick - pair.Value.tick > ReportLifetimeTicks)
                .Select(pair => pair.Key)
                .ToList();
            for (int i = 0; i < expired.Count; i++)
            {
                reports.Remove(expired[i]);
            }
        }

        public static bool IsCoordinatedReport(ShotReport report)
        {
            return reports.TryGetValue(report.GetHashCode(), out ReportState state)
                && CurrentTick - state.tick <= ReportLifetimeTicks;
        }

        public static void Cancel(Pawn pawn)
        {
            if (pawn != null)
            {
                if (states.TryGetValue(pawn.thingIDNumber, out State state))
                {
                    states.Remove(pawn.thingIDNumber);
                    if (state.verb.Bursting || pawn.stances?.curStance is Stance_Warmup)
                    {
                        state.verb.Reset();
                        if (pawn.stances?.curStance is Stance_Busy busy && busy.verb == state.verb)
                            pawn.stances.CancelBusyStanceHard();
                    }
                }
            }
        }

        private sealed class VitalShot
        {
            public Pawn shooter;
            public Pawn target;
            public BodyPartRecord part;
        }
        private static readonly ConditionalWeakTable<Projectile, VitalShot> vitalShots =
            new ConditionalWeakTable<Projectile, VitalShot>();
        [ThreadStatic] private static VitalShot impact;

        public static void RegisterProjectile(Projectile projectile, Thing launcher)
        {
            if (!(launcher is Pawn pawn) || !states.TryGetValue(pawn.thingIDNumber, out State state)) return;
            vitalShots.Remove(projectile);
            vitalShots.Add(projectile, new VitalShot { shooter = pawn, target = state.target, part = state.part });
        }

        public static object BeginImpact(Projectile projectile)
        {
            var previous = impact;
            vitalShots.TryGetValue(projectile, out impact);
            return previous;
        }

        public static void EndImpact(Projectile projectile, object previous)
        {
            if (projectile.Destroyed) vitalShots.Remove(projectile);
            impact = previous as VitalShot;
        }

        public static void ApplyVitalPart(Pawn pawn, ref DamageInfo damage)
        {
            if (impact != null && impact.target == pawn && damage.Instigator == impact.shooter
                && pawn.health.hediffSet.GetNotMissingParts().Contains(impact.part))
                damage.SetHitPart(impact.part);
        }

        public static void DrawActive(Map map)
        {
            foreach (State state in states.Values)
            {
                if (state.shooter?.Map != map || !IsFormationValid(state))
                {
                    continue;
                }

                Vector3 start = state.shooter.DrawPos;
                float referenceAngle = (state.partner.DrawPos - start).AngleFlat();
                Vector3 left = start + Vector3.forward.RotatedBy(
                    referenceAngle - TargetHalfAngle)
                    * state.verb.verbProps.range;
                Vector3 right = start + Vector3.forward.RotatedBy(
                    referenceAngle + TargetHalfAngle)
                    * state.verb.verbProps.range;
                GenDraw.DrawLineBetween(state.shooter.DrawPos, state.partner.DrawPos, SimpleColor.Cyan, 0.08f);
                GenDraw.DrawLineBetween(start, left, SimpleColor.Cyan, 0.04f);
                GenDraw.DrawLineBetween(start, right, SimpleColor.Cyan, 0.04f);
            }
        }

        public static Command CreateCommand(Pawn pawn)
        {
            bool active = IsActive(pawn);
            return new Command_Action
            {
                defaultLabel = (active
                    ? "HD_TacticalCoordinatedAim_Cancel"
                    : "HD_TacticalCoordinatedAim_Command").Translate().ToString(),
                defaultDesc = (active
                    ? "HD_TacticalCoordinatedAim_CancelDesc"
                    : "HD_TacticalCoordinatedAim_CommandDesc").Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false)
                    ?? BaseContent.BadTex,
                action = () =>
                {
                    if (active)
                    {
                        Cancel(pawn);
                    }
                    else
                    {
                        BeginTargeting(pawn);
                    }
                }
            };
        }

        private static Verb PrimaryVerb(Pawn pawn)
        {
            return pawn?.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
        }

        private static bool HasCQBTraining(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(CqbTrainingDefName);
            return pawn?.health?.hediffSet != null
                && def != null
                && pawn.health.hediffSet.HasHediff(def);
        }
    }

    [HarmonyPatch(typeof(Verb), "get_BurstShotCount")]
    public static class Patch_CoordinatedBurstCount
    {
        public static void Postfix(Verb __instance, ref int __result)
        {
            int count = TacticalCoordinatedAimUtility.BurstCount(__instance);
            if (count > 0) __result = count;
        }
    }

    [HarmonyPatch(typeof(Verb_Shoot), "get_ShotsPerBurst")]
    public static class Patch_CoordinatedShotsPerBurst
    {
        public static void Postfix(Verb_Shoot __instance, ref int __result)
        {
            int count = TacticalCoordinatedAimUtility.BurstCount(__instance);
            if (count > 0) __result = count;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[] {
        typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
        typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
    public static class Patch_CoordinatedProjectile
    {
        public static void Postfix(Projectile __instance, Thing launcher)
            => TacticalCoordinatedAimUtility.RegisterProjectile(__instance, launcher);
    }

    [HarmonyPatch]
    public static class Patch_CoordinatedImpact
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Projectile), "ImpactSomething");
            yield return AccessTools.Method(typeof(Projectile), "CheckForFreeIntercept");
        }
        public static void Prefix(Projectile __instance, out object __state)
            => __state = TacticalCoordinatedAimUtility.BeginImpact(__instance);
        public static void Finalizer(Projectile __instance, object __state)
            => TacticalCoordinatedAimUtility.EndImpact(__instance, __state);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_CoordinatedVitalPart
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Pawn __instance, ref DamageInfo dinfo)
            => TacticalCoordinatedAimUtility.ApplyVitalPart(__instance, ref dinfo);
    }

    public sealed class MapComponent_TacticalCoordinatedAim : MapComponent
    {
        public MapComponent_TacticalCoordinatedAim(Map map) : base(map)
        {
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            TacticalCoordinatedAimUtility.DrawActive(map);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalCoordinatedAim
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (__instance?.Faction == Faction.OfPlayer
                && (TacticalCoordinatedAimUtility.HasAccess(__instance)
                    || TacticalCoordinatedAimUtility.IsActive(__instance)))
            {
                __result = __result.Concat(new[]
                {
                    TacticalCoordinatedAimUtility.CreateCommand(__instance)
                });
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_Verb_TryStartCastOn_TacticalCoordinatedAim
    {
        public static IEnumerable<MethodBase> TargetMethods()
            => typeof(Verb).GetMethods().Where(method => method.Name == nameof(Verb.TryStartCastOn));

        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(
            Verb __instance,
            LocalTargetInfo castTarg,
            ref bool canHitNonTargetPawns,
            ref bool ___canHitNonTargetPawnsNow,
            ref bool __result)
        {
            Pawn shooter = __instance?.Caster as Pawn;
            if (!TacticalCoordinatedAimUtility.IsActive(shooter)
                || !TacticalAimUtility.IsRangedVerb(__instance)) return true;
            if (TacticalCoordinatedAimUtility.IsValidShot(
                shooter,
                __instance,
                castTarg))
            {
                ___canHitNonTargetPawnsNow = false;
                canHitNonTargetPawns = false;
                return true;
            }
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Patch_Verb_BurstArc_TacticalCoordinatedAim
    {
        public static bool Prefix(Verb __instance)
        {
            Pawn shooter = __instance.Caster as Pawn;
            if (!TacticalCoordinatedAimUtility.IsActive(shooter)
                || !TacticalAimUtility.IsRangedVerb(__instance)
                || TacticalCoordinatedAimUtility.IsValidShot(shooter, __instance, __instance.CurrentTarget))
                return true;
            __instance.Reset();
            if (shooter.stances?.curStance is Stance_Busy busy && busy.verb == __instance)
                shooter.stances.CancelBusyStanceHard();
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class Patch_Pawn_Tick_TacticalCoordinatedAim
    {
        public static void Postfix(Pawn __instance)
        {
            TacticalCoordinatedAimUtility.NotifyPawnTick(__instance);
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_ShotReport_HitReportFor_TacticalCoordinatedAim
    {
        public static void Postfix(
            Thing caster,
            Verb verb,
            LocalTargetInfo target,
            ref ShotReport __result)
        {
            TacticalCoordinatedAimUtility.RegisterReport(
                caster as Pawn,
                verb,
                target,
                ref __result);
        }
    }

    [HarmonyPatch(typeof(ShotReport), "get_AimOnTargetChance_StandardTarget")]
    public static class Patch_ShotReport_AimChance_TacticalCoordinatedAim
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ShotReport __instance, ref float __result)
        {
            if (TacticalCoordinatedAimUtility.IsCoordinatedReport(__instance))
            {
                __result = Mathf.Clamp01(__result * 1.20f);
            }
        }
    }
}
