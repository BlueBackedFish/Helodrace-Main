using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Helodrace.Squads;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    // Controlled native integration fixtures, enabled only by the isolated audit.
    // The normal execution, orders, PathFinder jobs and pawn path follower run.
    internal sealed class RaidMovementFunctionalAudit
    {
        private readonly Map map;
        private readonly List<Pawn> pawns;
        private readonly Pawn owner;
        private readonly string output;
        private readonly int started;
        private int stage, stageStarted, targetRoom;
        private RaidTacticalPlan active;
        private MapComponent_RaidTacticalExecution.ExecutionState state;
        private RaidStructureSnapshot structure;
        private Building_Door hiddenDoor;
        private readonly HashSet<Pawn> startedMoving = new HashSet<Pawn>();
        private readonly Dictionary<Pawn, IntVec3> stagePositions = new Dictionary<Pawn, IntVec3>();
        private bool hiddenDoorSeen, wrongPortal;
        internal RaidMovementFunctionalAudit(Map map, List<Pawn> pawns, Pawn owner, string output)
        { this.map = map; this.pawns = pawns; this.owner = owner; this.output = output; started = GenTicks.TicksGame; }
        internal bool Update()
        {
            int tick = GenTicks.TicksGame;
            if (stage == 0)
            {
                var unit = RaidTacticalUnit.ForPawn(pawns[0]);
                state = map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(unit.Id);
                if (state?.ActivePlan?.Success != true)
                {
                    if (tick - started > 1800) throw new InvalidOperationException("Functional fixture has no active plan.");
                    return false;
                }
                structure = map.GetComponent<MapComponent_RaidTacticalPlans>().GetStructure(unit.OrganizationId);
                owner.DeSpawn();
                hiddenDoor = (Building_Door)new IntVec3(120, 0, 130).GetEdifice(map);
                Begin(1);
            }
            foreach (Pawn pawn in pawns)
            {
                if (pawn.Position != stagePositions[pawn] || pawn.CurJobDef == JobDefOf.Goto
                    || MapComponent_RaidTacticalOrders.For(pawn)?.Movement.StartedTick >= stageStarted) startedMoving.Add(pawn);
                if (pawn.Position == hiddenDoor.Position) wrongPortal = true;
                if (RaidObservationSight.CanSeeCell(map, pawn.Position, hiddenDoor.Position, RaidContactMemory.Radius,
                        (a, b) => GenSight.LineOfSight(a, b, map, true))) hiddenDoorSeen = true;
            }
            // Update can skip any particular tick at accelerated game speeds.
            // Maintain the fixture's smoke every sampled frame, not only when
            // the last tick happens to be divisible by ten.
            if (stage == 5)
                HelodGasStore.AddGas(new IntVec3(120, 0, 112), map, HelodGasDefOf.HD_HCSmokeGrid, 1f);
            if (!ReferenceEquals(map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(state.UnitId), state))
                throw new InvalidOperationException("Functional fixture execution state was replaced.");
            if (stage == 3 && tick % 30 == 0)
            {
                if (hiddenDoor.Open) AccessTools.Method(typeof(Building_Door), "DoorTryClose").Invoke(hiddenDoor, null);
                else Open(hiddenDoor);
                if (!ReferenceEquals(state.ActivePlan, active))
                    throw new InvalidOperationException("An unseen opposite door replaced the committed movement plan.");
            }
            bool arrived = pawns.All(pawn => structure.RoomAt(pawn.Position) == targetRoom
                && active.Assignments.Any(assignment => assignment.Pawn == pawn
                    && pawn.Position.DistanceToSquared(assignment.Position) <= 2));
            if (!arrived)
            {
                if (tick - stageStarted > 1800)
                    throw new InvalidOperationException("Security functional stage " + stage + " stalled: "
                        + string.Join(";", pawns.Select(pawn => pawn.Position + "/" + pawn.CurJobDef?.defName
                            + "/" + MapComponent_RaidTacticalOrders.For(pawn)?.Movement.BlockReason
                            + "/edifice=" + pawn.Position.GetEdifice(map)?.def.defName
                            + "/frozenStandable=" + structure.Version.Geometry.Input.Cells[map.cellIndices.CellToIndex(pawn.Position)]
                                .Has(TacticalRawFlags.Standable))));
                return false;
            }
            bool movedRequired = stage == 4 ? startedMoving.Contains(pawns.Last()) : startedMoving.Count == pawns.Count;
            if (!movedRequired || wrongPortal || stage == 3 && hiddenDoorSeen
                || stage == 4 && state.ContactGuards.Count != 0
                || stage == 5 && !RaidSmokeUtility.CoveringSmokeAt(map, new IntVec3(120, 0, 112)))
                throw new InvalidOperationException("Invalid functional arrival: started=" + startedMoving.Count
                    + " wrongPortal=" + wrongPortal + " hiddenDoorSeen=" + hiddenDoorSeen
                    + " HC density=" + HelodGasStore.DensityAt(new IntVec3(120, 0, 112), map, HelodGasDefOf.HD_HCSmokeGrid));
            File.AppendAllText(output, "{\"functionalStage\":" + stage + ",\"passed\":true,\"pawns\":"
                + pawns.Count + ",\"arrivalTicks\":" + (tick - stageStarted) + "}\n");
            if (stage == 5) { File.AppendAllText(output, "{\"complete\":true}\n"); return true; }
            Begin(stage + 1);
            return false;
        }
        private static void Open(Building_Door door) => AccessTools.Method(typeof(Building_Door), "DoorOpen")
            .Invoke(door, new object[] { 100000 });
        private void Begin(int next)
        {
            stage = next; stageStarted = GenTicks.TicksGame; startedMoving.Clear(); wrongPortal = hiddenDoorSeen = false;
            int z = stage == 1 ? 116 : 112;
            IntVec3 portal = new IntVec3(120, 0, z), destination = new IntVec3(126, 0, z);
            var door = (Building_Door)new IntVec3(120, 0, 116).GetEdifice(map);
            foreach (Pawn pawn in pawns)
            {
                map.GetComponent<MapComponent_RaidTacticalOrders>().Forget(pawn);
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                pawn.pather.StopDead();
                int i = pawns.IndexOf(pawn);
                pawn.Position = new IntVec3(110 + i % 4, 0, z - 1 + i / 4);
                pawn.Notify_Teleported(endCurrentJob: false);
            }
            if (stage == 1) Open(door);
            else
            {
                AccessTools.Method(typeof(Building_Door), "DoorTryClose").Invoke(door, null);
                state.CqbKnowledge.RememberDirect(door.Position, new RaidKnownCqbCell
                    { Building = door.thingIDNumber, Portal = true, Usable = false }, stageStarted, structure.Version.Id, state.UnitId);
                portal.GetEdifice(map)?.Destroy(DestroyMode.Vanish);
            }
            state.CqbKnowledge.RememberDirect(portal, new RaidKnownCqbCell
                { Building = stage == 1 ? door.thingIDNumber : 0, Portal = true, Usable = true }, stageStarted, structure.Version.Id, state.UnitId);
            active = state.ActivePlan;
            active.IsDefensive = false; active.CqbIntent = RaidCqbIntent.None;
            active.Selected = new RaidTacticalOption { Maneuver = RaidTacticalManeuver.Regroup };
            state.Maneuver = RaidTacticalManeuver.Regroup;
            active.Start = pawns[0].Position;
            active.Entry = destination; active.PlannedBreach = null; active.BreachCell = active.BreachInside = IntVec3.Invalid;
            active.CoordinationDelayTicks = 10000;
            active.Assignments = pawns.Select((pawn, i) => new RaidTacticalAssignment { Pawn = pawn,
                GroupId = RaidTacticalUnit.ForPawn(pawn).GroupId, Task = RaidTacticalTask.Security,
                Position = new IntVec3(125 + i % 4, 0, z - 1 + i / 4) }).ToList();
            active.SafeStackCells = active.Assignments.Select(assignment => assignment.Position).ToList();
            active.ApproachPath = new List<IntVec3> { destination };
            targetRoom = structure.RoomAt(destination);
            active.MovementNodes = new List<RaidMovementNode> { new RaidMovementNode { Id = 0,
                StructureVersion = structure.Version.Id, Center = destination, Purpose = RaidMovementNodePurpose.Gather,
                AllowedRooms = new List<int> { targetRoom }, AllowedPortals = new List<IntVec3> { portal },
                GuidanceCells = active.SafeStackCells.ToList() } };
            state.Phase = RaidExecutionPhase.Assemble; state.PhaseStarted = state.ApproachProgressTick = stageStarted;
            state.CurrentNode = 0; state.ApproachComplete = false; state.ReadySince = -1;
            state.NodeMembers.Clear(); state.Indices.InvalidateNodes(); state.ContactGuards.Clear();
            state.ExteriorIngress.Clear(); state.Reactions.Clear(); state.Crossings.Clear();
            state.ExternalSupportAttempted = true; state.DefenseUntil = 0; state.ApproachSmokeActive = false;
            if (stage == 4)
            {
                foreach (RaidTacticalAssignment assignment in active.Assignments.Take(pawns.Count - 1))
                {
                    assignment.Pawn.Position = assignment.Position;
                    assignment.Pawn.Notify_Teleported(endCurrentJob: false);
                }
                state.ApproachComplete = true; state.CurrentNode = 1;
                state.NodeMembers = pawns.Select(pawn => new RaidNodeMemberProgress { Pawn = pawn, Completed = 0 }).ToList();
                state.Indices.InvalidateNodes();
                Pawn guard = pawns.Last();
                state.ContactGuards.Add(new RaidContactGuard { Pawn = guard, Until = stageStarted + 600,
                    Position = guard.Position, EnemyId = -1 });
                // No remembered threat remains: the normal CQB response releases
                // this guard after the rest of the approach is already complete.
            }
            if (stage == 5)
                HelodGasStore.AddGas(portal, map, HelodGasDefOf.HD_HCSmokeGrid, 1f);
            stagePositions.Clear();
            foreach (Pawn pawn in pawns) stagePositions[pawn] = pawn.Position;
            if (structure.RoomAt(pawns.Last().Position) == targetRoom)
                throw new InvalidOperationException("Functional fixture must start its joining pawn in the previous room.");
        }
    }
}
