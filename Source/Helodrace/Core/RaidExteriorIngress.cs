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
        public bool Direct, Waiting, SingleCellRoom;
        public bool Yielding;
        public IntVec3 YieldCell = IntVec3.Invalid;
        public int YieldSearchAfter;
        public int SearchAfter;
        public IntVec3 Destination = IntVec3.Invalid;
        public IntVec3 Requested = IntVec3.Invalid;
        public IntVec3 MovementDestination => Yielding ? YieldCell : Waiting || Entered ? Destination : Opening;

        public void ObservePosition(IntVec3 position, int room)
        {
            if (Complete) return;
            if (position == Opening) { Entered = true; return; }
            if (!Entered) return;
            IntVec3 inward = Inside - Opening;
            int depth = (position.x - Opening.x) * inward.x + (position.z - Opening.z) * inward.z;
            if (Direct && room > 0 && depth >= 1 && (position != Inside || SingleCellRoom))
            {
                Complete = true; Active = false;
                return;
            }
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
            Scribe_Values.Look(ref Direct, "direct"); Scribe_Values.Look(ref Waiting, "waiting");
            Scribe_Values.Look(ref SingleCellRoom, "singleCellRoom"); Scribe_Values.Look(ref SearchAfter, "searchAfter");
            Scribe_Values.Look(ref Yielding, "yielding");
            Scribe_Values.Look(ref YieldCell, "yieldCell", IntVec3.Invalid);
            Scribe_Values.Look(ref YieldSearchAfter, "yieldSearchAfter");
            Scribe_Values.Look(ref Destination, "destination", IntVec3.Invalid);
            Scribe_Values.Look(ref Requested, "requested", IntVec3.Invalid);
        }
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private readonly Dictionary<string, Tuple<int, HashSet<IntVec3>, int>> ingressConnections =
            new Dictionary<string, Tuple<int, HashSet<IntVec3>, int>>();
        private int ingressConnectionRevision;

        internal HashSet<IntVec3> InteriorIngressCells(Pawn pawn, RaidStructureSnapshot structure,
            RaidExteriorIngress ingress, out int revision)
        {
            int tick = GenTicks.TicksGame;
            string key = $"{structure.Version.Id}:{ingress.Opening}:{ingress.Inside}:{pawn.Faction?.GetUniqueLoadID()}";
            if (!ingressConnections.TryGetValue(key, out var cached) || tick - cached.Item1 >= 60)
            {
                var connected = ConnectedIngressCells(map, structure, ingress.Opening, ingress.Inside,
                    cell => cell.GetEdifice(map) is Building_Door door ? door.Open || door.PawnCanOpen(pawn) : cell.Standable(map));
                if (ingressConnections.Count >= 64)
                    ingressConnections.Remove(ingressConnections.OrderBy(value => value.Value.Item1).First().Key);
                int nextRevision = cached != null && connected.SetEquals(cached.Item2)
                    ? cached.Item3 : ++ingressConnectionRevision;
                ingressConnections[key] = cached = Tuple.Create(tick, connected, nextRevision);
            }
            revision = cached.Item3;
            return cached.Item2;
        }

        internal static HashSet<IntVec3> ConnectedIngressCells(Map map, RaidStructureSnapshot structure,
            IntVec3 opening, IntVec3 inside, Func<IntVec3, bool> passable)
        {
            bool Interior(IntVec3 cell)
            {
                if (!cell.InBounds(map) || cell == opening) return false;
                int index = map.cellIndices.CellToIndex(cell);
                TacticalRawCell raw = structure.Version.Geometry.Input.Cells[index];
                if (structure.Version.Geometry.Cells[index].ExteriorAccess) return false;
                // Newly opened interior walls keep room 0 in the frozen layout.
                // Doors may have their own positive room ID. Both sides still
                // have to be indoors, so a former exterior door is not an escape route.
                if (raw.Has(TacticalRawFlags.WallLine) || raw.Has(TacticalRawFlags.Door))
                    return structure.RoomAt(cell + IntVec3.North) > 0 && structure.RoomAt(cell + IntVec3.South) > 0
                        || structure.RoomAt(cell + IntVec3.East) > 0 && structure.RoomAt(cell + IntVec3.West) > 0;
                return raw.Room > 0;
            }
            return RaidFormationTopology.Connected(
                GenRadial.RadialCellsAround(inside, 14f, true).Where(cell => Interior(cell) && passable(cell)),
                inside, cell => GenAdj.CardinalDirections.Select(offset => cell + offset), _ => true);
        }

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
            if (structure.RoomAt(requested) <= 0) { ingress.Active = false; ingress.Yielding = false; return false; }
            ingress.Active = true;
            ingress.Requested = requested;
            var peers = states.Values.SelectMany(value => value.ExteriorIngress)
                .Where(value => value != ingress && value.Active && !value.Complete && value.Pawn?.Spawned == true
                    && value.Pawn.Map == map && !value.Pawn.Dead && !value.Pawn.Downed)
                .SelectMany(value => value.Yielding ? new[] { value.Destination, value.YieldCell } : new[] { value.Destination })
                .Where(cell => cell.IsValid).ToList();
            int tick = GenTicks.TicksGame;
            HashSet<IntVec3> interior = InteriorIngressCells(pawn, structure, ingress, out _);
            ingress.SingleCellRoom = structure.RoomArea(ingress.InsideRoom) == 1;
            bool singleCellRoom = ingress.SingleCellRoom;
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
            bool Free(IntVec3 cell) => cell.InBounds(map) && !peers.Contains(cell)
                && !cell.GetThingList(map).OfType<Pawn>().Any(other => other != pawn && other.pather?.Moving != true)
                && map.pawnDestinationReservationManager.CanReserve(cell, pawn);
            bool stable = !ingress.Waiting && interior.Contains(ingress.Destination)
                && ingress.Destination.Standable(map)
                && !state.ActivePlan.AvoidedTrapCells.Contains(ingress.Destination);
            if (!stable || tick >= ingress.SearchAfter && !Free(ingress.Destination))
            {
                if (ingress.Waiting && tick < ingress.SearchAfter)
                {
                    ingress.Yielding = false;
                    destination = ingress.Destination;
                    return true;
                }
                ingress.SearchAfter = tick + 60;
                // A follower may pass through the first room without reserving a stop there.
                // This route is proved by a bounded indoor flood, not global reachability
                // that might secretly go back outside through an old entrance.
                ingress.Direct = interior.Contains(requested) && requested != ingress.Inside
                    && Free(requested) && !state.ActivePlan.AvoidedTrapCells.Contains(requested);
                ingress.Waiting = false;
                if (ingress.Direct) ingress.Destination = requested;
                else
                {
                    // If the final target is beyond the bounded graph, use a free
                    // local joining cell, reusing the same proved indoor connection.
                    var connected = new HashSet<IntVec3>(interior.Where(cell =>
                        cell.DistanceToSquared(ingress.Inside) <= 36 && structure.RoomAt(cell) == ingress.InsideRoom
                        && !state.ActivePlan.AvoidedTrapCells.Contains(cell)));
                    singleCellRoom = connected.Count == 1;
                    ingress.Destination = GenRadial.RadialCellsAround(ingress.Inside, 5f, true)
                        .Concat(requested.DistanceToSquared(ingress.Opening) <= 49
                            ? new[] { requested } : Array.Empty<IntVec3>()).Distinct()
                        .Where(cell => connected.Contains(cell) && Valid(cell))
                        .OrderBy(cell => cell == requested ? -100 : cell.DistanceToSquared(ingress.Inside))
                        .Take(24).DefaultIfEmpty(IntVec3.Invalid).First();
                }
                if (!ingress.Destination.IsValid)
                {
                    // No legal standing slot is not permission to hold the opening itself.
                    ingress.Waiting = true;
                    IntVec3 outward = ingress.Opening - ingress.Inside;
                    ingress.Destination = GenRadial.RadialCellsAround(ingress.Opening, 8f, true)
                        .Where(cell => cell.InBounds(map) && structure.RoomAt(cell) == 0 && cell.Standable(map)
                            && cell != ingress.Opening && cell != ingress.Inside
                            && (cell.x - ingress.Opening.x) * outward.x + (cell.z - ingress.Opening.z) * outward.z >= 1
                            && !state.ActivePlan.AvoidedTrapCells.Contains(cell) && Free(cell))
                        .OrderBy(cell => pawn.Position.DistanceToSquared(cell))
                        .Take(24)
                        .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                        .DefaultIfEmpty(IntVec3.Invalid).First();
                }
                MapComponent_RaidTacticalTrace.Record(pawn,
                    $"Exterior join via {ingress.Opening}: {ingress.Destination}; direct={ingress.Direct} waiting={ingress.Waiting}");
            }
            UpdateIngressYield(pawn, structure, state.ActivePlan, ingress, peers, tick);
            // A blocked opening waits for recovery, never silently switches to an old entrance.
            destination = ingress.Destination.IsValid ? ingress.MovementDestination : IntVec3.Invalid;
            return true;
        }

        private void UpdateIngressYield(Pawn pawn, RaidStructureSnapshot structure, RaidTacticalPlan plan,
            RaidExteriorIngress ingress, List<IntVec3> peers, int tick)
        {
            bool available = IngressOpeningAvailable(pawn, ingress,
                cell => map.pawnDestinationReservationManager.CanReserve(cell, pawn));
            ingress.Yielding = !ingress.Waiting && !available;
            if (!ingress.Yielding) return;
            IntVec3 outward = ingress.Opening - ingress.Inside;
            bool Free(IntVec3 cell) => cell.InBounds(map) && structure.RoomAt(cell) == 0 && cell.Standable(map)
                && cell.DistanceToSquared(ingress.Opening) >= 4
                && (cell.x - ingress.Opening.x) * outward.x + (cell.z - ingress.Opening.z) * outward.z >= 1
                && RaidNodeRoute.WalkLine(map, ingress.Opening + outward, cell)
                && !plan.AvoidedTrapCells.Contains(cell) && !peers.Contains(cell)
                && !FormationOccupied(pawn, cell) && map.pawnDestinationReservationManager.CanReserve(cell, pawn);
            if (Free(ingress.YieldCell)) return;
            if (tick < ingress.YieldSearchAfter) { ingress.YieldCell = IntVec3.Invalid; return; }
            ingress.YieldSearchAfter = tick + 60;
            ingress.YieldCell = GenRadial.RadialCellsAround(ingress.Opening, 8f, true)
                .Where(Free).OrderBy(cell => pawn.Position.DistanceToSquared(cell)).Take(24)
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .DefaultIfEmpty(IntVec3.Invalid).First();
            MapComponent_RaidTacticalTrace.Record(pawn, $"Opening occupied: yield at {ingress.YieldCell} for {ingress.Opening}");
        }

        internal static bool IngressOpeningAvailable(Pawn pawn, RaidExteriorIngress ingress,
            Func<IntVec3, bool> canReserve) => RaidBreachTraversal.OpeningAvailable(
                pawn.Position == ingress.Opening, ingress.Entered,
                IngressMouthOccupied(pawn, ingress.Opening) || IngressMouthOccupied(pawn, ingress.Inside),
                !canReserve(ingress.Opening));

        private static bool IngressMouthOccupied(Pawn pawn, IntVec3 cell) => !cell.InBounds(pawn.Map)
            || cell.GetThingList(pawn.Map).OfType<Pawn>().Any(other => other != pawn && other.Spawned
                && !other.Dead && !other.Downed);

        internal bool ContinueExteriorIngress(Pawn pawn, RaidPawnOrder order)
        {
            RaidExteriorIngress ingress = ActiveExteriorIngress(pawn);
            if (order.Reactive || ingress == null || !ingress.Requested.IsValid) return false;
            if (!RedirectExteriorIngress(pawn, ingress.Requested, out IntVec3 next)) return false;
            IntVec3 destination = next.IsValid ? next : pawn.Position;
            if (order.Destination != destination) order.RetryAfter = 0;
            order.Destination = destination;
            order.Room = StructureFor(map, StateFor(order.UnitId).ActivePlan).RoomAt(order.Destination);
            return order.Destination != pawn.Position;
        }

        internal bool AllowsExteriorIngressStep(Pawn pawn, IntVec3 next)
        {
            RaidExteriorIngress ingress = ActiveExteriorIngress(pawn);
            if (ingress == null || !next.InBounds(map)) return true;
            var unit = Helodrace.Squads.RaidTacticalUnit.ForPawn(pawn);
            RaidStructureSnapshot structure = StructureFor(map, StateFor(unit.Id).ActivePlan);
            if (next == ingress.Opening && !ingress.Entered)
                return IngressOpeningAvailable(pawn, ingress,
                    candidate => map.pawnDestinationReservationManager.CanReserve(candidate, pawn));
            TacticalCellData cell = structure.CachedAt(next);
            int room = structure.RoomAt(next);
            if (ingress.Entered && !ingress.Waiting)
                return next == ingress.Opening || InteriorIngressCells(pawn, structure, ingress, out _).Contains(next);
            return RaidBreachTraversal.AllowsCommittedIngressStep(next == ingress.Opening, false,
                structure.RoomAt(pawn.Position), room, ingress.InsideRoom, cell.WallLine, cell.ExteriorAccess);
        }
    }
}
