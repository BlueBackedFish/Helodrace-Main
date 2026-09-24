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
    public enum TacticalRapidKillPart
    {
        Torso,
        Head
    }

    public static class TacticalRapidKillUtility
    {
        private const string CqbTrainingDefName = "HD_CQBTraining";
        private const float MaximumRange = 4f;
        private const int InitialChestShots = 2;
        private const int ExtraHeadShots = 2;
        private const int MaximumHeadDoubleTaps = 5;
        private const int ShotIntervalTicks = 2;
        private const int HeadTransitionTicks = 4;
        private const int ReportLifetimeTicks = 2;

        private sealed class State
        {
            public Pawn shooter;
            public Pawn target;
            public Verb verb;
            public TacticalRapidKillPart currentPart;
            public BodyPartRecord currentBodyPart;
            public int chestShotsCompleted;
            public int extraHeadShotsRemaining;
            public int headShotsCompleted;
            public int pendingChestProjectiles;
            public int lastChestShotTick;
            public bool launchedShot;
            public bool extraRetryScheduled;
            public bool currentShotIsExtra;
            public bool waitingForShot;
            public bool chestPenetrated;
            public int nextActionTick;
        }

        private struct ReportState
        {
            public int tick;
        }

        private static readonly Dictionary<int, State> states =
            new Dictionary<int, State>();
        private sealed class ProjectileShot
        {
            public State state;
            public BodyPartRecord part;
            public bool chest;
        }

        private static readonly ConditionalWeakTable<Projectile, ProjectileShot> projectileShots =
            new ConditionalWeakTable<Projectile, ProjectileShot>();
        [ThreadStatic] private static ProjectileShot impactingShot;
        private static readonly Dictionary<int, ReportState> reports =
            new Dictionary<int, ReportState>();
        private static readonly FieldInfo FactorFromShooterAndDistField =
            AccessTools.Field(typeof(ShotReport), "factorFromShooterAndDist");
        private static readonly FieldInfo FactorFromEquipmentField =
            AccessTools.Field(typeof(ShotReport), "factorFromEquipment");
        private static readonly FieldInfo FactorFromTargetSizeField =
            AccessTools.Field(typeof(ShotReport), "factorFromTargetSize");
        private static readonly FieldInfo FactorFromWeatherField =
            AccessTools.Field(typeof(ShotReport), "factorFromWeather");

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

        public static bool IsActive(Verb verb)
        {
            Pawn pawn = verb?.Caster as Pawn;
            return pawn != null
                && states.TryGetValue(pawn.thingIDNumber, out State state)
                && state.verb == verb;
        }

        public static bool IsActive(Pawn pawn)
        {
            return pawn != null
                && states.ContainsKey(pawn.thingIDNumber);
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
                    validator = target =>
                    {
                        Pawn victim = target.Thing as Pawn;
                        return victim != null && victim.Map == map;
                    }
                },
                target => Start(pawn, target.Thing as Pawn),
                highlightAction: null,
                targetValidator: target => IsValidTarget(
                    pawn,
                    target.Thing as Pawn),
                caster: pawn,
                onUpdateAction: target => DrawPreview(pawn, target));

            Helodrace.MapComponent_PersistentTargetingOverlay.Set(
                map,
                target => DrawPreview(pawn, target));
        }

        private static void DrawPreview(Pawn pawn, LocalTargetInfo target)
        {
            if (pawn?.Map == null)
            {
                return;
            }

            GenDraw.DrawRadiusRing(pawn.Position, MaximumRange, Color.white);
            Pawn victim = target.Thing as Pawn;
            if (victim != null && victim.Map == pawn.Map)
            {
                GenDraw.DrawRadiusRing(
                    victim.Position,
                    0.55f,
                    IsValidTarget(pawn, victim) ? Color.green : Color.red);
            }
        }

        private static bool IsValidTarget(Pawn pawn, Pawn target)
        {
            Verb verb = PrimaryVerb(pawn);
            return pawn?.Map != null
                && target != null
                && target != pawn
                && target.Map == pawn.Map
                && target.Spawned
                && !target.Dead
                && !target.Downed
                && pawn.Position.DistanceTo(target.Position) <= MaximumRange
                && TacticalAimUtility.IsRangedVerb(verb)
                && verb != null
                && !verb.Bursting
                && verb.CanHitTarget(new LocalTargetInfo(target))
                && FindBodyPart(target, TacticalRapidKillPart.Torso) != null
                && FindBodyPart(target, TacticalRapidKillPart.Head) != null
                && GenSight.LineOfSight(pawn.Position, target.Position, pawn.Map);
        }

        private static void Start(Pawn pawn, Pawn target)
        {
            Verb verb = PrimaryVerb(pawn);
            if (!HasAccess(pawn) || !IsValidTarget(pawn, target))
            {
                return;
            }

            states[pawn.thingIDNumber] = new State
            {
                shooter = pawn,
                target = target,
                verb = verb,
                nextActionTick = CurrentTick
            };
            TryFireNext(pawn, states[pawn.thingIDNumber]);
        }

        public static void NotifyPawnTick(Pawn pawn)
        {
            if (!IsActive(pawn)
                || !states.TryGetValue(pawn.thingIDNumber, out State state))
            {
                return;
            }

            if (!IsValidState(state))
            {
                Cancel(pawn);
                return;
            }

            if (state.waitingForShot
                || CurrentTick < state.nextActionTick
                || state.verb.Bursting
                || pawn.stances?.curStance?.StanceBusy == true)
            {
                return;
            }

            TryFireNext(pawn, state);
        }

        private static bool IsValidState(State state)
        {
            return state?.shooter != null
                && state.target != null
                && state.verb != null
                && state.shooter.Spawned
                && state.target.Spawned
                && !state.shooter.Dead
                && !state.target.Dead
                && !state.shooter.Downed
                && state.shooter.Drafted
                && PrimaryVerb(state.shooter) == state.verb
                && state.shooter.Map == state.target.Map
                && state.shooter.Position.DistanceTo(state.target.Position)
                    <= MaximumRange
                && state.verb.CanHitTarget(new LocalTargetInfo(state.target));
        }

        private static void TryFireNext(Pawn pawn, State state)
        {
            // The base torso/torso/head drill never waits for impact. Only the
            // optional follow-up decision waits for the chest rounds to resolve.
            if (state.headShotsCompleted > 0 && !state.extraRetryScheduled)
            {
                if (state.pendingChestProjectiles > 0
                    && CurrentTick - state.lastChestShotTick < 60) return;
                state.extraRetryScheduled = true;
                state.extraHeadShotsRemaining = state.chestPenetrated
                    ? 0 : ExtraHeadShots * MaximumHeadDoubleTaps;
            }
            TacticalRapidKillPart nextPart;
            bool isExtra;
            if (!TryGetNextPart(state, out nextPart, out isExtra))
            {
                Finish(pawn);
                return;
            }

            BodyPartRecord bodyPart = FindBodyPart(state.target, nextPart);
            if (bodyPart == null)
            {
                Finish(pawn);
                return;
            }

            state.currentPart = nextPart;
            state.currentBodyPart = bodyPart;
            state.currentShotIsExtra = isExtra;
            state.waitingForShot = true;
            state.launchedShot = false;

            if (!state.verb.TryStartCastOn(
                new LocalTargetInfo(state.target),
                true,
                false,
                false,
                false))
            {
                Finish(pawn);
                return;
            }

            state.nextActionTick = Math.Max(state.nextActionTick, CurrentTick + ShotIntervalTicks);
        }

        private static bool TryGetNextPart(
            State state,
            out TacticalRapidKillPart part,
            out bool isExtra)
        {
            isExtra = false;
            if (state.chestShotsCompleted < InitialChestShots)
            {
                part = TacticalRapidKillPart.Torso;
                return true;
            }

            if (state.extraHeadShotsRemaining > 0)
            {
                part = TacticalRapidKillPart.Head;
                isExtra = true;
                return true;
            }

            if (state.headShotsCompleted > 0)
            {
                part = TacticalRapidKillPart.Head;
                return false;
            }

            part = TacticalRapidKillPart.Head;
            return true;
        }

        public static void NotifyShot(Verb verb)
        {
            Pawn pawn = verb?.Caster as Pawn;
            if (pawn == null
                || !states.TryGetValue(pawn.thingIDNumber, out State state)
                || state.verb != verb
                || !state.waitingForShot)
            {
                return;
            }

            state.waitingForShot = false;
            if (!state.launchedShot)
            {
                Finish(pawn);
                return;
            }
            if (state.currentPart == TacticalRapidKillPart.Torso)
            {
                state.chestShotsCompleted++;
                state.lastChestShotTick = CurrentTick;
            }
            else
            {
                state.headShotsCompleted++;
                if (state.currentShotIsExtra) state.extraHeadShotsRemaining--;
            }

            state.nextActionTick = CurrentTick + NextShotDelay(state);
        }

        private static int NextShotDelay(State state)
        {
            return state.currentPart == TacticalRapidKillPart.Torso
                && state.chestShotsCompleted == InitialChestShots
                    ? HeadTransitionTicks : ShotIntervalTicks;
        }

        public static void TrackProjectile(Projectile projectile, Thing launcher)
        {
            if (!(launcher is Pawn shooter)
                || !states.TryGetValue(shooter.thingIDNumber, out State state)
                || !state.waitingForShot) return;
            var shot = new ProjectileShot
            {
                state = state,
                part = state.currentBodyPart,
                chest = state.currentPart == TacticalRapidKillPart.Torso
            };
            projectileShots.Remove(projectile);
            projectileShots.Add(projectile, shot);
            state.launchedShot = true;
            if (shot.chest) state.pendingChestProjectiles++;
        }

        public static object BeginImpact(Projectile projectile)
        {
            object previous = impactingShot;
            projectileShots.TryGetValue(projectile, out impactingShot);
            return previous;
        }

        public static void EndImpact(Projectile projectile, object previous)
        {
            if (projectileShots.TryGetValue(projectile, out ProjectileShot shot))
            {
                if (shot.chest) shot.state.pendingChestProjectiles--;
                projectileShots.Remove(projectile);
            }
            impactingShot = previous as ProjectileShot;
        }

        public static void ShortenCooldown(Stance stance)
        {
            if (stance is Stance_Cooldown cooldown && IsActive(cooldown.verb))
                cooldown.ticksLeft = Math.Min(cooldown.ticksLeft, ShotIntervalTicks);
        }

        public static void ApplyRequestedPart(Pawn victim, ref DamageInfo dinfo)
        {
            ProjectileShot shot = impactingShot;
            if (shot == null || shot.state.target != victim
                || dinfo.Instigator != shot.state.shooter
                || shot.part == null)
            {
                return;
            }

            dinfo.SetHitPart(shot.part);
        }

        public static void NotifyArmorResult(Pawn victim, DamageDef damageDef, float damage)
        {
            ProjectileShot shot = impactingShot;
            if (shot == null || shot.state.target != victim || !shot.chest)
            {
                return;
            }

            if (damage > 0f && damageDef != DamageDefOf.Blunt)
            {
                shot.state.chestPenetrated = true;
            }
        }

        public static void RegisterReport(
            Pawn shooter,
            Verb verb,
            LocalTargetInfo target,
            ref ShotReport report)
        {
            if (!IsActive(verb)
                || shooter == null
                || !states.TryGetValue(shooter.thingIDNumber, out State state)
                || state.target != target.Thing)
            {
                return;
            }

            object boxedReport = report;
            SetFieldToOne(FactorFromShooterAndDistField, boxedReport);
            SetFieldToOne(FactorFromEquipmentField, boxedReport);
            SetFieldToOne(FactorFromTargetSizeField, boxedReport);
            SetFieldToOne(FactorFromWeatherField, boxedReport);
            report = (ShotReport)boxedReport;
            reports[report.GetHashCode()] = new ReportState
            {
                tick = CurrentTick
            };
            PruneReports();
        }

        private static void SetFieldToOne(FieldInfo field, object boxedReport)
        {
            field?.SetValue(boxedReport, 1f);
        }

        public static bool IsRapidKillReport(ShotReport report)
        {
            return reports.TryGetValue(report.GetHashCode(), out ReportState state)
                && CurrentTick - state.tick <= ReportLifetimeTicks;
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

        private static BodyPartRecord FindBodyPart(
            Pawn pawn,
            TacticalRapidKillPart part)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return null;
            }

            string defName = part == TacticalRapidKillPart.Torso
                ? "Torso"
                : "Head";
            return pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(bodyPart => bodyPart.def?.defName == defName);
        }

        public static void Finish(Pawn pawn)
        {
            if (pawn != null)
            {
                states.Remove(pawn.thingIDNumber);
            }
        }

        public static void Cancel(Pawn pawn)
        {
            Finish(pawn);
        }

        public static int BurstCount(Verb verb)
        {
            return IsActive(verb) ? 1 : 0;
        }

        public static Command CreateCommand(Pawn pawn)
        {
            return new Command_Action
            {
                defaultLabel = "HD_TacticalRapidKill_Command".Translate().ToString(),
                defaultDesc = "HD_TacticalRapidKill_CommandDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Skill/HD_CQB_RapidKill", false)
                    ?? BaseContent.BadTex,
                action = () => BeginTargeting(pawn)
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

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalRapidKill
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (__instance?.Faction == Faction.OfPlayer
                && TacticalRapidKillUtility.HasAccess(__instance))
            {
                __result = __result.Concat(new[]
                {
                    TacticalRapidKillUtility.CreateCommand(__instance)
                });
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class Patch_Pawn_Tick_TacticalRapidKill
    {
        public static void Postfix(Pawn __instance)
        {
            TacticalRapidKillUtility.NotifyPawnTick(__instance);
        }
    }

    [HarmonyPatch(typeof(Verb), "get_WarmupTime")]
    public static class Patch_Verb_WarmupTime_TacticalRapidKill
    {
        public static void Postfix(Verb __instance, ref float __result)
        {
            if (TacticalRapidKillUtility.IsActive(__instance))
            {
                __result = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(Verb), "get_BurstShotCount")]
    public static class Patch_Verb_BurstShotCount_TacticalRapidKill
    {
        public static void Postfix(Verb __instance, ref int __result)
        {
            int count = TacticalRapidKillUtility.BurstCount(__instance);
            if (count > 0)
            {
                __result = count;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_Shoot), "get_ShotsPerBurst")]
    public static class Patch_Verb_ShotsPerBurst_TacticalRapidKill
    {
        public static void Postfix(Verb_Shoot __instance, ref int __result)
        {
            int count = TacticalRapidKillUtility.BurstCount(__instance);
            if (count > 0)
            {
                __result = count;
            }
        }
    }

    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot_TacticalRapidKill
    {
        public static void Postfix(Verb __instance)
        {
            TacticalRapidKillUtility.NotifyShot(__instance);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[] {
        typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
        typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
    public static class Patch_Projectile_Launch_TacticalRapidKill
    {
        public static void Postfix(Projectile __instance, Thing launcher)
            => TacticalRapidKillUtility.TrackProjectile(__instance, launcher);
    }

    [HarmonyPatch(typeof(Projectile), "ImpactSomething")]
    public static class Patch_Projectile_Impact_TacticalRapidKill
    {
        public static void Prefix(Projectile __instance, out object __state)
            => __state = TacticalRapidKillUtility.BeginImpact(__instance);
        public static void Finalizer(Projectile __instance, object __state)
            => TacticalRapidKillUtility.EndImpact(__instance, __state);
    }

    [HarmonyPatch(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.SetStance))]
    public static class Patch_SetStance_TacticalRapidKill
    {
        public static void Prefix(Stance newStance)
            => TacticalRapidKillUtility.ShortenCooldown(newStance);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_Pawn_PreApplyDamage_TacticalRapidKill
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Pawn __instance, ref DamageInfo dinfo)
        {
            TacticalRapidKillUtility.ApplyRequestedPart(__instance, ref dinfo);
        }
    }

    [HarmonyPatch(typeof(ArmorUtility), nameof(ArmorUtility.GetPostArmorDamage))]
    public static class Patch_ArmorUtility_TacticalRapidKill
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            Pawn pawn,
            DamageDef damageDef,
            float __result)
        {
            TacticalRapidKillUtility.NotifyArmorResult(pawn, damageDef, __result);
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_ShotReport_HitReportFor_TacticalRapidKill
    {
        public static void Postfix(
            Thing caster,
            Verb verb,
            LocalTargetInfo target,
            ref ShotReport __result)
        {
            TacticalRapidKillUtility.RegisterReport(
                caster as Pawn,
                verb,
                target,
                ref __result);
        }
    }

    [HarmonyPatch(typeof(ShotReport), "get_AimOnTargetChance_StandardTarget")]
    public static class Patch_ShotReport_AimChance_TacticalRapidKill
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ShotReport __instance, ref float __result)
        {
            if (TacticalRapidKillUtility.IsRapidKillReport(__instance))
            {
                __result = Mathf.Clamp01(__result);
            }
        }
    }
}
