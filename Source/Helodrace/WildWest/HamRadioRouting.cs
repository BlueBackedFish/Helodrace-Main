using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public static class HamRadioUtility
    {
        public static bool IsHam(Building_CommsConsole console)
            => console != null && HamRadioRoutingRules.IsHamConsole(console.def?.defName, console is Building_HamRadio);

        public static bool IsLow(ICommunicable target)
            => target is Faction faction && HamRadioRoutingRules.BlocksVanillaFaction(faction.def?.defName);

        public static IEnumerable<FloatMenuOption> Options(Building_CommsConsole console, Pawn pawn)
        {
            string label = "HD_HAM_Use".Translate();
            if (!console.CanUseCommsNow)
            {
                yield return new FloatMenuOption(label + ": " + "HD_HAM_NoSignal".Translate(), null);
                yield break;
            }
            if (!pawn.CanReserveAndReach(console, PathEndMode.InteractionCell, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
                yield break;
            }
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
            {
                yield return new FloatMenuOption(label + ": " + "HD_Telegraph_OperatorUnavailable".Translate(), null);
                yield break;
            }
            yield return new FloatMenuOption(label, () => GiveUseJob(console, pawn));
        }

        public static void GiveUseJob(Building_CommsConsole console, Pawn pawn)
            => pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_UseHamRadio"), console), JobTag.Misc);

        public static IEnumerable<Toil> UseToils(JobDriver driver, TargetIndex target)
        {
            driver.FailOnDespawnedNullOrForbidden(target);
            driver.FailOn(() => !(driver.job.GetTarget(target).Thing is Building_CommsConsole console) || !console.CanUseCommsNow);
            yield return Toils_Goto.GotoThing(target, PathEndMode.InteractionCell);
            yield return Toils_General.WaitWith(target, 180, true);
            yield return new Toil
            {
                initAction = () => driver.pawn.Map.GetComponent<MapComponent_HamRadio>().Receive(driver.pawn),
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    // Route the base methods too: loaded consoles and other XML patches can keep
    // Building_CommsConsole even when our ThingDef names the HAM subclass.
    [HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GetFloatMenuOptions))]
    public static class Patch_HamRadioMenu
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        public static void Postfix(Building_CommsConsole __instance, Pawn myPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            if (HamRadioUtility.IsHam(__instance)) __result = HamRadioUtility.Options(__instance, myPawn);
        }
    }

    [HarmonyPatch(typeof(Building_CommsConsole), "GetCommTargets")]
    public static class Patch_HamRadioTargets
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        public static void Postfix(Building_CommsConsole __instance, ref IEnumerable<ICommunicable> __result)
        {
            __result = HamRadioUtility.IsHam(__instance) ? Enumerable.Empty<ICommunicable>()
                : __result.Where(target => !HamRadioUtility.IsLow(target));
        }
    }

    [HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GiveUseCommsJob))]
    public static class Patch_HamRadioJob
    {
        [HarmonyPrefix]
        public static bool Prefix(Building_CommsConsole __instance, Pawn negotiator, ICommunicable target)
        {
            if (HamRadioUtility.IsHam(__instance))
            {
                HamRadioUtility.GiveUseJob(__instance, negotiator);
                return false;
            }
            if (!HamRadioUtility.IsLow(target)) return true;
            Messages.Message("HD_HAM_DirectLowUnavailable".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }
    }

    [HarmonyPatch(typeof(JobDriver_UseCommsConsole), "MakeNewToils")]
    public static class Patch_HamRadioExistingJob
    {
        [HarmonyPrefix]
        public static bool Prefix(JobDriver_UseCommsConsole __instance, ref IEnumerable<Toil> __result)
        {
            foreach (TargetIndex index in new[] { TargetIndex.A, TargetIndex.B })
                if (HamRadioUtility.IsHam(__instance.job.GetTarget(index).Thing as Building_CommsConsole))
                {
                    __result = HamRadioUtility.UseToils(__instance, index);
                    return false;
                }
            return true;
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.CommFloatMenuOption))]
    public static class Patch_LowFactionVanillaCommsOption
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, ref FloatMenuOption __result)
        {
            if (!HamRadioUtility.IsLow(__instance)) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.TryOpenComms))]
    public static class Patch_LowFactionVanillaCommsDialog
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance)
        {
            if (!HamRadioUtility.IsLow(__instance)) return true;
            Messages.Message("HD_HAM_DirectLowUnavailable".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }
    }
}
