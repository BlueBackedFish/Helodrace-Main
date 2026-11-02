using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    // These machines can be refueled through a service port while another pawn
    // reserves the normal work position. Fuel itself retains normal reservations.
    public class CompProperties_IndustrialRefuelable : CompProperties_Refuelable
    {
        public CompProperties_IndustrialRefuelable()
        {
            // The game only preserves the player's auto-refuel choice on load
            // when the component exposes this toggle.
            showAllowAutoRefuelToggle = true;
        }
    }

    internal static class IndustrialRefueling
    {
        internal static ReservationLayerDef Layer => DefDatabase<ReservationLayerDef>.GetNamed("HD_IndustrialRefueling");
        internal static bool Handles(Thing thing)
            => thing?.TryGetComp<CompRefuelable>()?.Props is CompProperties_IndustrialRefuelable;

        internal static bool CanRefuel(Pawn pawn, Thing machine, bool forced, out Thing fuel)
        {
            fuel = null;
            if (!Handles(machine) || !machine.Spawned || machine.IsForbidden(pawn) || machine.Fogged()
                || machine.Faction != pawn.Faction) return false;
            CompRefuelable comp = machine.TryGetComp<CompRefuelable>();
            if (comp.IsFull || !comp.Props.allowRefuelIfNotEmpty && comp.Fuel > 0f
                || !forced && (!comp.allowAutoRefuel || !comp.ShouldAutoRefuelNow)
                || !pawn.CanReserve(machine.Position, 1, -1, Layer, forced)) return false;
            ThingFilter filter = comp.Props.fuelFilter;
            fuel = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, filter.BestThingRequest,
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                candidate => filter.Allows(candidate) && !candidate.IsForbidden(pawn) && pawn.CanReserve(candidate));
            if (fuel == null) JobFailReason.Is("NoFuelToRefuel".Translate(filter.Summary));
            return fuel != null;
        }

        internal static Job MakeJob(Pawn pawn, Thing machine, bool forced)
            => CanRefuel(pawn, machine, forced, out Thing fuel)
                ? JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_RefuelIndustrialMachine"), machine, fuel)
                : null;
    }

    public class WorkGiver_RefuelIndustrialMachine : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Refuelable);
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
            => IndustrialRefueling.CanRefuel(pawn, t, forced, out _);
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
            => IndustrialRefueling.MakeJob(pawn, t, forced);
    }

    public class JobDriver_RefuelIndustrialMachine : JobDriver_Refuel
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => IndustrialRefueling.Handles(job.targetA.Thing)
                // 1.7 also checks a building's ordinary interaction-cell owner
                // across reservation layers. Reserve the service cell itself
                // on our layer so the maker's work position stays independent.
                && pawn.Reserve(job.targetA.Cell, job, 1, -1, IndustrialRefueling.Layer, errorOnFailed)
                && pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);
    }

    // Both automatic hauling and the refuel job offered by an empty bill giver
    // use the same reservation layer; two refuelers cannot race one another.
    [HarmonyPatch(typeof(WorkGiver_Refuel), nameof(WorkGiver_Refuel.HasJobOnThing))]
    internal static class Patch_IndustrialRefueling_VanillaWorkGiver
    {
        private static bool Prefix(Thing t, ref bool __result)
        {
            if (!IndustrialRefueling.Handles(t)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    internal static class Patch_IndustrialRefueling_BillWorkGiver
    {
        private static void Postfix(Pawn pawn, Thing thing, bool forced, ref Job __result)
        {
            if (__result?.def == JobDefOf.Refuel && IndustrialRefueling.Handles(thing))
                __result = IndustrialRefueling.MakeJob(pawn, thing, forced);
        }
    }
}
