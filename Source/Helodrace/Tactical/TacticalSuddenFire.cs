using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace.Tactical
{
    public static class TacticalSuddenFireUtility
    {
        public const float Range = 5f;
        public const int ShotCount = 3;
        private const float LargePawnSizeThreshold = 1.5f;
        private const int MaximumPawnSizeBonus = 2;
        private const float ShotgunRangeBonus = 2f;
        private const float HeavyShotgunRangeBonus = 3f;
        private const int CooldownTicks = 60 * 60;
        private const string CoinTossSoundDefName = "HD_CoinToss";
        private const int StateLifetimeTicks = 240;
        private const int ReportLifetimeTicks = 2;

        private sealed class State
        {
            public Verb verb;
            public int startedTick;
            public int shotsFired;
        }

        private struct ReportState
        {
            public int tick;
        }

        private static readonly Dictionary<int, State> states =
            new Dictionary<int, State>();
        private static readonly Dictionary<int, ReportState> suddenReports =
            new Dictionary<int, ReportState>();
        private static readonly Dictionary<int, int> cooldownEndTicks =
            new Dictionary<int, int>();

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        public static bool IsActive(Verb verb)
        {
            if (verb == null || !states.TryGetValue(verb.GetHashCode(), out State state))
            {
                return false;
            }

            int now = CurrentTick;
            if (now - state.startedTick > StateLifetimeTicks)
            {
                states.Remove(verb.GetHashCode());
                return false;
            }

            return state.verb == verb;
        }

        public static bool IsValidTarget(Pawn pawn, LocalTargetInfo target)
        {
            if (!TacticalAimUtility.HasCQBTraining(pawn)
                || pawn?.Map == null
                || !target.IsValid
                || target.Thing == pawn)
            {
                return false;
            }

            Verb verb = pawn.equipment?.Primary
                ?.GetComp<CompEquippable>()?.PrimaryVerb;
            if (!TacticalAimUtility.IsRangedVerb(verb)
                || verb.Bursting)
            {
                return false;
            }

            float distance = DistanceToTarget(pawn, target);
            if (distance > MaximumRange(pawn, verb) || distance < 0.5f)
            {
                return false;
            }

            return verb.CanHitTarget(target);
        }

        public static float MaximumRange(Pawn pawn, Verb verb)
        {
            float range = Range + PawnSizeRangeBonus(pawn);
            return range + ShotgunRangeBonusFor(verb);
        }

        private static float PawnSizeRangeBonus(Pawn pawn)
        {
            float bodySize = pawn?.BodySize ?? 0f;
            if (bodySize < LargePawnSizeThreshold)
            {
                return 0f;
            }

            int sizeSteps = Mathf.FloorToInt(bodySize - LargePawnSizeThreshold) + 1;
            return Mathf.Clamp(sizeSteps, 1, MaximumPawnSizeBonus);
        }

        private static float ShotgunRangeBonusFor(Verb verb)
        {
            if (!(verb is Verb_ShootShotgun))
            {
                return 0f;
            }

            VerbProperties_Shotgun shotgunProps = verb.verbProps as VerbProperties_Shotgun;
            int pelletCount = shotgunProps?.pelletCount ?? 1;

            // M79 buckshot changes pellet count at runtime rather than through
            // VerbProperties, so include its active ammo mode as well.
            CompM79Launcher m79 = verb.EquipmentSource?.TryGetComp<CompM79Launcher>();
            if (m79?.BuckshotSelected == true)
            {
                pelletCount = Mathf.Max(pelletCount, 20);
            }

            if (pelletCount <= 1)
            {
                return 0f;
            }

            return pelletCount >= 12 ? HeavyShotgunRangeBonus : ShotgunRangeBonus;
        }

        private static float DistanceToTarget(Pawn pawn, LocalTargetInfo target)
        {
            if (!target.HasThing)
            {
                return pawn.Position.DistanceTo(target.Cell);
            }

            float closestDistance = float.MaxValue;
            foreach (IntVec3 cell in target.Thing.OccupiedRect().Cells)
            {
                closestDistance = Mathf.Min(
                    closestDistance,
                    pawn.Position.DistanceTo(cell));
            }

            return closestDistance;
        }

        public static bool IsValidTarget(Pawn pawn, TargetInfo target)
        {
            LocalTargetInfo local = target.HasThing
                ? new LocalTargetInfo(target.Thing)
                : new LocalTargetInfo(target.Cell);
            return IsValidTarget(pawn, local);
        }

        public static bool CanUse(Pawn pawn)
        {
            return HasAccess(pawn) && !IsOnCooldown(pawn);
        }

        public static bool HasAccess(Pawn pawn)
        {
            Verb verb = pawn?.equipment?.Primary
                ?.GetComp<CompEquippable>()?.PrimaryVerb;
            return TacticalAimUtility.HasCQBTraining(pawn)
                && pawn?.Map != null
                && TacticalAimUtility.IsRangedVerb(verb)
                && !verb.Bursting;
        }

        public static bool IsOnCooldown(Pawn pawn)
        {
            if (pawn == null || !cooldownEndTicks.TryGetValue(
                pawn.thingIDNumber,
                out int cooldownEndTick))
            {
                return false;
            }

            if (CurrentTick >= cooldownEndTick)
            {
                cooldownEndTicks.Remove(pawn.thingIDNumber);
                return false;
            }

            return true;
        }

        public static int TicksRemaining(Pawn pawn)
        {
            if (!IsOnCooldown(pawn)
                || !cooldownEndTicks.TryGetValue(pawn.thingIDNumber, out int endTick))
            {
                return 0;
            }

            return Mathf.Max(0, endTick - CurrentTick);
        }

        public static void BeginTargeting(Pawn pawn)
        {
            if (!CanUse(pawn))
            {
                return;
            }

            Map map = pawn.Map;
            Verb verb = pawn.equipment?.Primary
                ?.GetComp<CompEquippable>()?.PrimaryVerb;
            float range = MaximumRange(pawn, verb);
            System.Action<LocalTargetInfo> drawPreview = target =>
            {
                GenDraw.DrawRadiusRing(pawn.Position, range, Color.white);
                if (target.IsValid && target.Cell.InBounds(map))
                {
                    GenDraw.DrawRadiusRing(
                        target.Cell,
                        0.55f,
                        IsValidTarget(pawn, target)
                            ? Color.yellow
                            : Color.red);
                }
            };

            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetPawns = true,
                    canTargetBuildings = true,
                    canTargetLocations = false,
                    validator = target => target.Cell.InBounds(map)
                        && IsValidTarget(pawn, target)
                },
                target => Start(pawn, target),
                drawPreview);

            Helodrace.MapComponent_PersistentTargetingOverlay.Set(map, drawPreview);
        }

        public static void Start(Pawn pawn, LocalTargetInfo target)
        {
            if (!CanUse(pawn))
            {
                if (IsOnCooldown(pawn))
                {
                    Messages.Message(
                        "HD_TacticalSuddenFire_Cooldown".Translate(
                            TicksRemaining(pawn).ToStringTicksToPeriod()),
                        pawn,
                        MessageTypeDefOf.RejectInput,
                        false);
                }
                return;
            }

            if (!IsValidTarget(pawn, target))
            {
                Messages.Message(
                    "HD_TacticalSuddenFire_InvalidTarget".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            Verb verb = pawn.equipment.Primary.GetComp<CompEquippable>()?.PrimaryVerb;
            if (verb == null)
            {
                return;
            }

            states[verb.GetHashCode()] = new State
            {
                verb = verb,
                startedTick = CurrentTick,
                shotsFired = 0
            };

            bool started = verb.TryStartCastOn(
                target,
                true,
                true,
                false,
                false);
            if (!started)
            {
                states.Remove(verb.GetHashCode());
                return;
            }

            cooldownEndTicks[pawn.thingIDNumber] = CurrentTick + CooldownTicks;
            DefDatabase<SoundDef>.GetNamedSilentFail(CoinTossSoundDefName)
                ?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
        }

        public static int BurstCount(Verb verb)
        {
            return IsActive(verb) ? ShotCount : 0;
        }

        public static void NotifyShot(Verb verb)
        {
            if (!states.TryGetValue(verb?.GetHashCode() ?? -1, out State state)
                || state.verb != verb)
            {
                return;
            }

            state.shotsFired++;
            if (state.shotsFired >= ShotCount)
            {
                states.Remove(verb.GetHashCode());
            }
        }

        public static void RegisterReport(ShotReport report)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (!suddenReports.ContainsKey(report.GetHashCode()))
            {
                suddenReports[report.GetHashCode()] = new ReportState { tick = tick };
            }

            if (suddenReports.Count > 256)
            {
                List<int> expired = suddenReports
                    .Where(pair => tick - pair.Value.tick > ReportLifetimeTicks)
                    .Select(pair => pair.Key)
                    .ToList();
                for (int i = 0; i < expired.Count; i++)
                {
                    suddenReports.Remove(expired[i]);
                }
            }
        }

        public static bool IsSuddenReport(ShotReport report)
        {
            if (!suddenReports.TryGetValue(report.GetHashCode(), out ReportState state))
            {
                return false;
            }

            return (Find.TickManager?.TicksGame ?? 0) - state.tick
                <= ReportLifetimeTicks;
        }

        public static Command CreateCommand(Pawn pawn)
        {
            bool onCooldown = IsOnCooldown(pawn);
            return new Command_Action
            {
                defaultLabel = "HD_TacticalSuddenFire_Command".Translate().ToString(),
                defaultDesc = "HD_TacticalSuddenFire_CommandDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Skill/HD_CQB_SuddenFire", false)
                    ?? BaseContent.BadTex,
                Disabled = onCooldown,
                disabledReason = onCooldown
                    ? "HD_TacticalSuddenFire_Cooldown".Translate(
                        TicksRemaining(pawn).ToStringTicksToPeriod()).ToString()
                    : null,
                action = () => BeginTargeting(pawn)
            };
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalSuddenFire
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (__instance?.Faction != Faction.OfPlayer
                || __instance.Dead
                || !TacticalSuddenFireUtility.HasAccess(__instance))
            {
                return;
            }

            __result = __result.Concat(new[]
            {
                TacticalSuddenFireUtility.CreateCommand(__instance)
            });
        }
    }

    [HarmonyPatch(typeof(Verb), "get_WarmupTime")]
    public static class Patch_Verb_WarmupTime_TacticalSuddenFire
    {
        public static void Postfix(Verb __instance, ref float __result)
        {
            if (TacticalSuddenFireUtility.IsActive(__instance))
            {
                __result = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(Verb), "get_BurstShotCount")]
    public static class Patch_Verb_BurstShotCount_TacticalSuddenFire
    {
        public static void Postfix(Verb __instance, ref int __result)
        {
            int count = TacticalSuddenFireUtility.BurstCount(__instance);
            if (count > 0)
            {
                __result = count;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_Shoot), "get_ShotsPerBurst")]
    public static class Patch_Verb_ShotsPerBurst_TacticalSuddenFire
    {
        public static void Postfix(Verb_Shoot __instance, ref int __result)
        {
            int count = TacticalSuddenFireUtility.BurstCount(__instance);
            if (count > 0)
            {
                __result = count;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot_TacticalSuddenFire
    {
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (__result)
            {
                TacticalSuddenFireUtility.NotifyShot(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_ShotReport_HitReportFor_TacticalSuddenFire
    {
        public static void Postfix(
            Thing caster,
            Verb verb,
            LocalTargetInfo target,
            ref ShotReport __result)
        {
            if (TacticalSuddenFireUtility.IsActive(verb))
            {
                TacticalSuddenFireUtility.RegisterReport(__result);
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), "get_AimOnTargetChance_StandardTarget")]
    public static class Patch_ShotReport_AimChance_TacticalSuddenFire
    {
        public static void Postfix(ShotReport __instance, ref float __result)
        {
            if (TacticalSuddenFireUtility.IsSuddenReport(__instance))
            {
                __result = 1f;
            }
        }
    }
}
