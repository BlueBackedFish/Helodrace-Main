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
    public enum TacticalPrecisionPart
    {
        None,
        Head,
        Chest,
        Arm,
        Leg
    }

    public static class TacticalPrecisionFireUtility
    {
        private const string CqbTrainingDefName = "HD_CQBTraining";
        private const float ChestWarmupMultiplier = 1f;
        private const float ArmLegWarmupMultiplier = 1.35f;
        private const float HeadWarmupMultiplier = 1.75f;

        private sealed class ShotState
        {
            public Pawn shooter;
            public Pawn target;
            public BodyPartRecord part;
        }

        private static readonly Dictionary<int, TacticalPrecisionPart> selectedParts =
            new Dictionary<int, TacticalPrecisionPart>();
        private static readonly ConditionalWeakTable<Projectile, ShotState> shotStates =
            new ConditionalWeakTable<Projectile, ShotState>();
        [ThreadStatic] private static ShotState impact;

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        public static bool CanUse(Pawn pawn)
        {
            return pawn?.Map != null
                && !pawn.Dead
                && !pawn.Downed
                && HasCQBTraining(pawn);
        }

        public static TacticalPrecisionPart SelectedPart(Pawn pawn)
        {
            return pawn != null
                && selectedParts.TryGetValue(pawn.thingIDNumber, out TacticalPrecisionPart part)
                ? part
                : TacticalPrecisionPart.None;
        }

        public static void ShowSelectionMenu(Pawn pawn)
        {
            if (!CanUse(pawn))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                OptionFor(pawn, TacticalPrecisionPart.Head, "HD_TacticalPrecisionFire_Head"),
                OptionFor(pawn, TacticalPrecisionPart.Chest, "HD_TacticalPrecisionFire_Chest"),
                OptionFor(pawn, TacticalPrecisionPart.Arm, "HD_TacticalPrecisionFire_Arm"),
                OptionFor(pawn, TacticalPrecisionPart.Leg, "HD_TacticalPrecisionFire_Leg"),
                OptionFor(pawn, TacticalPrecisionPart.None, "HD_TacticalPrecisionFire_Clear")
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static FloatMenuOption OptionFor(
            Pawn pawn,
            TacticalPrecisionPart part,
            string labelKey)
        {
            return new FloatMenuOption(
                labelKey.Translate().ToString(),
                () => SelectPart(pawn, part));
        }

        private static void SelectPart(Pawn pawn, TacticalPrecisionPart part)
        {
            if (pawn == null)
            {
                return;
            }

            if (part == TacticalPrecisionPart.None)
            {
                selectedParts.Remove(pawn.thingIDNumber);
            }
            else
            {
                selectedParts[pawn.thingIDNumber] = part;
            }

            Messages.Message(
                "HD_TacticalPrecisionFire_Selected".Translate(
                    LabelFor(part)),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public static string LabelFor(TacticalPrecisionPart part)
        {
            switch (part)
            {
                case TacticalPrecisionPart.Head:
                    return "HD_TacticalPrecisionFire_Head".Translate().ToString();
                case TacticalPrecisionPart.Chest:
                    return "HD_TacticalPrecisionFire_Chest".Translate().ToString();
                case TacticalPrecisionPart.Arm:
                    return "HD_TacticalPrecisionFire_Arm".Translate().ToString();
                case TacticalPrecisionPart.Leg:
                    return "HD_TacticalPrecisionFire_Leg".Translate().ToString();
                default:
                    return "HD_TacticalPrecisionFire_None".Translate().ToString();
            }
        }

        public static bool TryGetRequestedPart(
            Pawn shooter,
            LocalTargetInfo target,
            Verb verb,
            out BodyPartRecord part)
        {
            part = null;
            TacticalPrecisionPart requested = SelectedPart(shooter);
            if (!CanUse(shooter)
                || requested == TacticalPrecisionPart.None
                || !TacticalAimUtility.IsAiming(shooter)
                || shooter.pather?.MovingNow == true
                || !(target.Thing is Pawn victim)
                || !TacticalAimUtility.IsRangedVerb(verb)
                || !IsInFocusArc(shooter, target))
            {
                return false;
            }

            part = FindAvailablePart(victim, requested);
            return part != null;
        }

        public static bool TryGetWarmupMultiplier(Verb verb, out float multiplier)
        {
            multiplier = 1f;
            Pawn shooter = verb?.Caster as Pawn;
            TacticalPrecisionPart requested = SelectedPart(shooter);
            if (requested == TacticalPrecisionPart.None
                || !TryGetRequestedPart(shooter, verb.CurrentTarget, verb, out _))
            {
                return false;
            }

            multiplier = requested == TacticalPrecisionPart.Head
                ? HeadWarmupMultiplier
                : requested == TacticalPrecisionPart.Chest
                    ? ChestWarmupMultiplier
                    : ArmLegWarmupMultiplier;
            return true;
        }

        public static void RegisterShot(Projectile projectile, Thing launcher, LocalTargetInfo intendedTarget)
        {
            Pawn shooter = launcher as Pawn;
            Verb verb = shooter?.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
            if (verb == null || !TryGetRequestedPart(shooter, intendedTarget, verb, out BodyPartRecord part))
            {
                return;
            }

            Pawn target = intendedTarget.Thing as Pawn;
            if (target == null)
            {
                return;
            }

            shotStates.Remove(projectile);
            shotStates.Add(projectile, new ShotState
            {
                shooter = shooter,
                target = target,
                part = part
            });
        }

        public static object BeginImpact(Projectile projectile)
        {
            object previous = impact;
            shotStates.TryGetValue(projectile, out impact);
            return previous;
        }

        public static void EndImpact(Projectile projectile, object previous)
        {
            if (projectile.Destroyed) shotStates.Remove(projectile);
            impact = previous as ShotState;
        }

        public static void ApplyRequestedPart(Pawn victim, ref DamageInfo dinfo)
        {
            ShotState state = impact;
            if (state == null || victim == null || state.target != victim
                || dinfo.Instigator != state.shooter
                || state.part == null)
            {
                return;
            }

            if (victim.health?.hediffSet?.GetNotMissingParts().Contains(state.part) == true)
            {
                dinfo.SetHitPart(state.part);
            }
        }

        private static bool IsInFocusArc(Pawn pawn, LocalTargetInfo target)
        {
            Hediff_TacticalAim aim = TacticalAimUtility.Get(pawn);
            if (aim == null
                || !TacticalAimUtility.TryGetTargetAngle(pawn, target, out float targetAngle))
            {
                return false;
            }

            return Mathf.Abs(Mathf.DeltaAngle(aim.FocusAngle, targetAngle))
                <= Hediff_TacticalAim.FocusHalfAngle;
        }

        public static BodyPartRecord FindAvailablePart(
            Pawn victim,
            TacticalPrecisionPart requested)
        {
            if (victim?.health?.hediffSet == null)
            {
                return null;
            }

            string defName = requested == TacticalPrecisionPart.Head
                ? "Head"
                : requested == TacticalPrecisionPart.Chest
                    ? "Torso"
                    : requested == TacticalPrecisionPart.Arm
                        ? "Arm"
                        : "Leg";

            return victim.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(part => part.def?.defName == defName);
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
    public static class Patch_Pawn_GetGizmos_TacticalPrecisionFire
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (__instance?.Faction != Faction.OfPlayer
                || !TacticalPrecisionFireUtility.CanUse(__instance))
            {
                return;
            }

            TacticalPrecisionPart selected = TacticalPrecisionFireUtility.SelectedPart(__instance);
            __result = __result.Concat(new[]
            {
                new Command_Action
                {
                    defaultLabel = "HD_TacticalPrecisionFire_Command".Translate().ToString(),
                    defaultDesc = "HD_TacticalPrecisionFire_CommandDesc".Translate(
                        TacticalPrecisionFireUtility.LabelFor(selected)).ToString(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false)
                        ?? BaseContent.BadTex,
                    action = () => TacticalPrecisionFireUtility.ShowSelectionMenu(__instance)
                }
            });
        }
    }

    [HarmonyPatch(typeof(Verb), "get_WarmupTime")]
    public static class Patch_Verb_WarmupTime_TacticalPrecisionFire
    {
        public static void Postfix(Verb __instance, ref float __result)
        {
            if (TacticalPrecisionFireUtility.TryGetWarmupMultiplier(
                __instance,
                out float multiplier))
            {
                __result *= multiplier;
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[] {
        typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
        typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
    public static class Patch_VerbLaunchProjectile_TryCastShot_TacticalPrecisionFire
    {
        public static void Postfix(Projectile __instance, Thing launcher, LocalTargetInfo intendedTarget)
        {
            TacticalPrecisionFireUtility.RegisterShot(__instance, launcher, intendedTarget);
        }
    }

    [HarmonyPatch]
    public static class Patch_ProjectileImpact_TacticalPrecisionFire
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Projectile), "ImpactSomething");
            yield return AccessTools.Method(typeof(Projectile), "CheckForFreeIntercept");
        }
        public static void Prefix(Projectile __instance, out object __state)
            => __state = TacticalPrecisionFireUtility.BeginImpact(__instance);
        public static void Finalizer(Projectile __instance, object __state)
            => TacticalPrecisionFireUtility.EndImpact(__instance, __state);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_Pawn_PreApplyDamage_TacticalPrecisionFire
    {
        public static void Prefix(Pawn __instance, ref DamageInfo dinfo)
        {
            TacticalPrecisionFireUtility.ApplyRequestedPart(__instance, ref dinfo);
        }
    }
}
