using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public static class HamRadioUtility
    {
        public static bool IsHamFaction(ICommunicable target)
            => target is Faction faction && HamRadioRoutingRules.IsHamFaction(faction.def?.defName);

        public static FloatMenuOption Option(Building_CommsConsole console, Pawn pawn, Faction faction)
        {
            string label = "HD_HAM_Use".Translate(faction.Name);
            if (!console.CanUseCommsNow)
                return new FloatMenuOption(label + ": " + "HD_HAM_NoSignal".Translate(), null);
            if (!pawn.CanReserveAndReach(console, PathEndMode.InteractionCell, Danger.Deadly))
                return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return new FloatMenuOption(label + ": " + "HD_Telegraph_OperatorUnavailable".Translate(), null);
            return new FloatMenuOption(label, () => GiveUseJob(console, pawn, faction));
        }

        public static void GiveUseJob(Building_CommsConsole console, Pawn pawn, Faction faction)
        {
            if (!IsHamFaction(faction) || faction.defeated) return;
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_UseHamRadio"), console);
            job.commTarget = faction;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public static IEnumerable<Toil> UseToils(JobDriver driver, TargetIndex target)
        {
            driver.FailOnDespawnedNullOrForbidden(target);
            driver.FailOn(() => !IsHamFaction(driver.job.commTarget) || ((Faction)driver.job.commTarget).defeated);
            driver.FailOn(() => !(driver.job.GetTarget(target).Thing is Building_CommsConsole console) || !console.CanUseCommsNow);
            yield return Toils_Goto.GotoThing(target, PathEndMode.InteractionCell);
            yield return Toils_General.WaitWith(target, 180, true);
            yield return new Toil
            {
                initAction = () => driver.pawn.Map.GetComponent<MapComponent_HamRadio>().Receive(driver.pawn, driver.job.commTarget as Faction),
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    // The console and its target list stay vanilla. Only these two factions use HAM.
    [HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GiveUseCommsJob))]
    public static class Patch_HamRadioJob
    {
        [HarmonyPrefix]
        public static bool Prefix(Building_CommsConsole __instance, Pawn negotiator, ICommunicable target)
        {
            if (!HamRadioUtility.IsHamFaction(target)) return true;
            HamRadioUtility.GiveUseJob(__instance, negotiator, (Faction)target);
            return false;
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.CommFloatMenuOption))]
    public static class Patch_HamFactionCommsOption
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, Building_CommsConsole console, Pawn negotiator, ref FloatMenuOption __result)
        {
            if (!HamRadioUtility.IsHamFaction(__instance)) return true;
            __result = HamRadioUtility.Option(console, negotiator, __instance);
            return false;
        }
    }

    // Also covers direct calls and vanilla comms jobs already queued for LOW/HIGH.
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryOpenComms))]
    public static class Patch_HamFactionCommsDialog
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, Pawn negotiator)
        {
            if (!HamRadioUtility.IsHamFaction(__instance)) return true;
            negotiator?.Map?.GetComponent<MapComponent_HamRadio>().Receive(negotiator, __instance);
            return false;
        }
    }
}
