using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private readonly TacticalPassageAdmissions<Pawn, IntVec3> passageTraffic = new TacticalPassageAdmissions<Pawn, IntVec3>();
        private readonly TacticalQueuePositions<Pawn, IntVec3> ingressGoals = new TacticalQueuePositions<Pawn, IntVec3>();
        private readonly TacticalQueuePositions<Pawn, IntVec3> ingressYields = new TacticalQueuePositions<Pawn, IntVec3>();
        private readonly HashSet<IntVec3> transitMouths = new HashSet<IntVec3>();
        internal bool TransitMouth(IntVec3 cell) => transitMouths.Contains(cell);
        internal int PassageWaiting => passageTraffic.Count;
        private bool FreeIngressClaim(Pawn pawn, IntVec3 cell) => ingressGoals.Available(pawn, cell) && ingressYields.Available(pawn, cell);
        private void ReleaseIngressTraffic(Pawn pawn)
        { passageTraffic.Release(pawn); ingressGoals.Release(pawn); ingressYields.Release(pawn); }
        private void RememberTransitMouth(RaidTacticalPlan plan)
        {
            if (!plan.BreachCell.IsValid) return;
            transitMouths.Add(plan.BreachCell);
            RaidStructureSnapshot structure = StructureFor(map, plan);
            if (structure != null && structure.RoomArea(structure.RoomAt(plan.BreachInside)) > 1)
                transitMouths.Add(plan.BreachInside);
        }
        private void PrunePassageTraffic(int tick)
        {
            var active = new HashSet<Pawn>();
            foreach (ExecutionState state in states.Values)
            {
                if (state.ActivePlan != null && state.CrossingBreach.IsValid) RememberTransitMouth(state.ActivePlan);
                foreach (RaidExteriorIngress ingress in state.ExteriorIngress)
                    if (ingress.Active && !ingress.Complete && ingress.Pawn?.Spawned == true && ingress.Pawn.Map == map
                        && !ingress.Pawn.Dead && !ingress.Pawn.Downed
                        && MapComponent_RaidTacticalOrders.For(ingress.Pawn)?.Reactive != true)
                    {
                        active.Add(ingress.Pawn);
                        transitMouths.Add(ingress.Opening);
                        if (!ingress.SingleCellRoom) transitMouths.Add(ingress.Inside);
                        if (ingress.Destination.IsValid && !ingressGoals.TryGet(ingress.Pawn, out _))
                            ingressGoals.Assign(ingress.Pawn, ingress.Destination);
                        if (ingress.Yielding && ingress.YieldCell.IsValid && !ingressYields.TryGet(ingress.Pawn, out _))
                            ingressYields.Assign(ingress.Pawn, ingress.YieldCell);
                    }
            }
            passageTraffic.Prune(tick, active.Contains);
            ingressGoals.Prune(active.Contains); ingressYields.Prune(active.Contains);
        }
        private bool CanEnterPassage(Pawn pawn, RaidExteriorIngress ingress) =>
            (pawn.Position == ingress.Opening || ingress.Entered || passageTraffic.First(pawn, ingress.Opening))
            && IngressOpeningAvailable(pawn, ingress, cell => map.pawnDestinationReservationManager.CanReserve(cell, pawn));
    }
}
