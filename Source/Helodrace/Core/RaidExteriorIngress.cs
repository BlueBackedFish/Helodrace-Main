using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    // A member's established exterior connection survives replacement of room plans.
    // Entered is confirmed from the actual pawn position, not a requested path step.
    public sealed class RaidExteriorIngress : IExposable
    {
        public Pawn Pawn;
        public IntVec3 Opening;
        public IntVec3 Inside;
        public int InsideRoom;
        public bool Entered;
        public bool Complete;
        public bool Active;
        public IntVec3 Destination = IntVec3.Invalid;
        public IntVec3 Requested = IntVec3.Invalid;
        public IntVec3 MovementDestination => Entered ? Destination : Opening;

        public void ObservePosition(IntVec3 position, int room)
        {
            if (Complete) return;
            if (position == Opening) { Entered = true; return; }
            if (!Entered) return;
            IntVec3 inward = Inside - Opening;
            int depth = (position.x - Opening.x) * inward.x + (position.z - Opening.z) * inward.z;
            if (room == InsideRoom && depth >= 1)
            {
                // Keep moving until the mouth is clear; merely stepping inside
                // must not hand control to a staging hold on the next update.
                if (!Destination.IsValid || position == Destination) { Complete = true; Active = false; }
            }
            else Entered = false;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref Opening, "opening");
            Scribe_Values.Look(ref Inside, "inside");
            Scribe_Values.Look(ref InsideRoom, "insideRoom");
            Scribe_Values.Look(ref Entered, "entered");
            Scribe_Values.Look(ref Complete, "complete");
            Scribe_Values.Look(ref Active, "active");
            Scribe_Values.Look(ref Destination, "destination", IntVec3.Invalid);
            Scribe_Values.Look(ref Requested, "requested", IntVec3.Invalid);
        }
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        internal void ObserveExteriorIngressPosition(Pawn pawn)
        {
            var unit = Helodrace.Squads.RaidTacticalUnit.ForPawn(pawn);
            ExecutionState state = unit == null ? null : StateFor(unit.Id);
            RaidExteriorIngress ingress = state?.ExteriorIngress.FirstOrDefault(value => value.Pawn == pawn && !value.Complete);
            RaidStructureSnapshot structure = ingress == null || state.ActivePlan == null ? null : StructureFor(map, state.ActivePlan);
            if (structure != null) ingress.ObservePosition(pawn.Position, structure.RoomAt(pawn.Position));
        }

        private void RememberExteriorIngress(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            if (!plan.BreachCell.IsValid || structure == null || structure.RoomAt(plan.Entry) != 0
                || structure.RoomAt(plan.BreachInside) <= 0) return;
            state.ExteriorIngress.RemoveAll(value => value.Pawn == null || value.Pawn.Dead);
            foreach (Pawn pawn in members.Where(pawn => structure.RoomAt(pawn.Position) == 0))
            {
                RaidExteriorIngress previous = state.ExteriorIngress.FirstOrDefault(value => value.Pawn == pawn);
                if (previous != null && (!previous.Complete || previous.Opening == plan.BreachCell)) continue;
                if (previous != null) state.ExteriorIngress.Remove(previous);
                state.ExteriorIngress.Add(new RaidExteriorIngress { Pawn = pawn, Opening = plan.BreachCell,
                    Inside = plan.BreachInside, InsideRoom = structure.RoomAt(plan.BreachInside) });
            }
        }

        internal RaidExteriorIngress ActiveExteriorIngress(Pawn pawn)
        {
            if (MapComponent_RaidTacticalOrders.For(pawn)?.Reactive == true) return null;
            var unit = Helodrace.Squads.RaidTacticalUnit.ForPawn(pawn);
            ExecutionState state = unit == null ? null : StateFor(unit.Id);
            RaidExteriorIngress ingress = state?.ExteriorIngress.FirstOrDefault(value => value.Pawn == pawn && !value.Complete);
            RaidStructureSnapshot structure = state?.ActivePlan == null ? null : StructureFor(map, state.ActivePlan);
            if (ingress == null || structure == null) return null;
            ingress.ObservePosition(pawn.Position, structure.RoomAt(pawn.Position));
            return ingress.Active && !ingress.Complete ? ingress : null;
        }

        internal bool RedirectExteriorIngress(Pawn pawn, IntVec3 requested, out IntVec3 destination)
        {
            destination = IntVec3.Invalid;
            var unit = Helodrace.Squads.RaidTacticalUnit.ForPawn(pawn);
            ExecutionState state = unit == null ? null : StateFor(unit.Id);
            RaidExteriorIngress ingress = state?.ExteriorIngress.FirstOrDefault(value => value.Pawn == pawn && !value.Complete);
            RaidStructureSnapshot structure = state?.ActivePlan == null ? null : StructureFor(map, state.ActivePlan);
            if (ingress == null || structure == null || !requested.InBounds(map)) return false;
            ingress.ObservePosition(pawn.Position, structure.RoomAt(pawn.Position));
            if (ingress.Complete) return false;
            // Outside security orders remain outside until their normal plan calls them in.
            if (structure.RoomAt(requested) <= 0) { ingress.Active = false; return false; }
            ingress.Active = true;
            ingress.Requested = requested;
            var peers = state.ExteriorIngress.Where(value => value != ingress && value.Active && !value.Complete)
                .Select(value => value.Destination).ToList();
            bool singleCellRoom = structure.RoomArea(ingress.InsideRoom) == 1;
            bool Valid(IntVec3 cell) => cell.InBounds(map) && cell.Standable(map)
                && structure.RoomAt(cell) == ingress.InsideRoom && cell != ingress.Opening
                && RaidBreachTraversal.IsClearance(
                    (cell.x - ingress.Opening.x) * (ingress.Inside.x - ingress.Opening.x)
                        + (cell.z - ingress.Opening.z) * (ingress.Inside.z - ingress.Opening.z),
                    cell == ingress.Inside, singleCellRoom)
                && !state.ActivePlan.AvoidedTrapCells.Contains(cell)
                && !peers.Contains(cell)
                && !cell.GetThingList(map).OfType<Pawn>().Any(other => other != pawn)
                && map.pawnDestinationReservationManager.CanReserve(cell, pawn);
            if (!Valid(ingress.Destination))
            {
                // A side cell can be reachable via the mouth even when a diagonal
                // from the opening clips the wall. Prove local cardinal connectivity.
                var connected = RaidFormationTopology.Connected(
                    GenRadial.RadialCellsAround(ingress.Inside, 6f, true)
                        .Where(cell => cell.InBounds(map) && cell.Standable(map)
                            && structure.RoomAt(cell) == ingress.InsideRoom
                            && !state.ActivePlan.AvoidedTrapCells.Contains(cell)),
                    ingress.Inside, cell => GenAdj.CardinalDirections.Select(offset => cell + offset), _ => true);
                singleCellRoom = connected.Count == 1;
                // Preserve an entry pawn's existing near-opening clearance goal;
                // followers headed to another room first get a local joining goal.
                ingress.Destination = GenRadial.RadialCellsAround(ingress.Inside, 5f, true)
                    .Concat(requested.DistanceToSquared(ingress.Opening) <= 49
                        ? new[] { requested } : Array.Empty<IntVec3>()).Distinct()
                    .Where(cell => connected.Contains(cell) && Valid(cell))
                    .OrderBy(cell => (cell == requested ? -100 : cell.DistanceToSquared(ingress.Inside))
                        + peers.Count(other => other == cell) * 1000)
                    .Take(24).DefaultIfEmpty(IntVec3.Invalid).First();
                MapComponent_RaidTacticalTrace.Record(pawn,
                    $"Exterior join via {ingress.Opening}: {ingress.Destination}");
            }
            // A blocked opening waits for recovery, never silently switches to an old entrance.
            destination = ingress.Destination.IsValid ? ingress.MovementDestination : IntVec3.Invalid;
            return true;
        }

        internal bool ContinueExteriorIngress(Pawn pawn, RaidPawnOrder order)
        {
            RaidExteriorIngress ingress = ActiveExteriorIngress(pawn);
            if (order.Reactive || ingress == null || !ingress.Requested.IsValid) return false;
            if (!RedirectExteriorIngress(pawn, ingress.Requested, out IntVec3 next) || !next.IsValid) return false;
            order.Destination = next;
            order.Room = StructureFor(map, StateFor(order.UnitId).ActivePlan).RoomAt(next);
            order.RetryAfter = 0;
            return next != pawn.Position;
        }

        internal bool IsExteriorTransitGoal(Pawn pawn, IntVec3 destination)
        {
            RaidExteriorIngress ingress = ActiveExteriorIngress(pawn);
            return ingress != null && !ingress.Entered && destination == ingress.Opening;
        }

        internal bool AllowsExteriorIngressStep(Pawn pawn, IntVec3 next)
        {
            RaidExteriorIngress ingress = ActiveExteriorIngress(pawn);
            if (ingress == null || !next.InBounds(map)) return true;
            var unit = Helodrace.Squads.RaidTacticalUnit.ForPawn(pawn);
            RaidStructureSnapshot structure = StructureFor(map, StateFor(unit.Id).ActivePlan);
            TacticalCellData cell = structure.CachedAt(next);
            int room = structure.RoomAt(next);
            return RaidBreachTraversal.AllowsCommittedIngressStep(next == ingress.Opening, ingress.Entered,
                structure.RoomAt(pawn.Position), room, ingress.InsideRoom, cell.WallLine, cell.ExteriorAccess);
        }
    }
}
