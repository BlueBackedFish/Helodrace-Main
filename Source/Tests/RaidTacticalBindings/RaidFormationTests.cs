using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class RaidFormationTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(17, 1, 17) };
        map.cellIndices = new CellIndices(map);
        map.thingGrid = new ThingGrid(map);
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
        var pawn = new Pawn { thingIDNumber = 40001 };
        var other = new Pawn { thingIDNumber = 40002 };
        var pawnDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        pawnDef.defName = "FormationTestPawn";
        pawn.def = other.def = pawnDef;
        var cell = new IntVec3(8, 0, 8);
        var near = cell + IntVec3.North;
        foreach (Pawn value in new[] { pawn, other })
        {
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(value, (sbyte)0);
            AccessTools.Field(typeof(Thing), "positionInt").SetValue(value, cell);
            value.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(value.health, PawnHealthState.Mobile);
        }
        var method = AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "AtStagingPosition");
        var plan = new RaidTacticalPlan { BreachCell = cell + IntVec3.East * 2 };
        plan.SafeStackCells.Add(cell); plan.SafeStackCells.Add(near);
        plan.SafeSupportCells.Add(cell); plan.SafeSupportCells.Add(near);
        var assignment = new RaidTacticalAssignment { Pawn = pawn, Position = near };
        Game previous = Current.Game;
        Current.Game = game;
        try
        {
            cell.GetThingList(map).Add(pawn);
            bool Ready() => (bool)method.Invoke(null, new object[] { assignment, plan });
            foreach (RaidTacticalTask task in new[] { RaidTacticalTask.Entry, RaidTacticalTask.Security, RaidTacticalTask.FireSupport })
            {
                assignment.Task = task; assignment.Position = near;
                Check(!Ready(), "Being in another safe tile must not complete the assigned stack/security position.");
                assignment.Position = cell;
                Check(Ready(), "A pawn alone on its own assigned tile is ready.");
                cell.GetThingList(map).Add(other);
                Check(!Ready(), "Two physically overlapping pawns must not count as a ready formation.");
                cell.GetThingList(map).Remove(other);
                Check(Ready(), "Readiness resumes once the transient occupant clears the tile.");
            }
            plan.BreachCell = IntVec3.Invalid;
            assignment.Position = near;
            Check(!Ready(), "Regroup security also needs its own tile rather than arrival tolerance.");
            assignment.Position = cell;
            Check(Ready(), "Non-breach regroup retains normal readiness at the assigned tile.");
            assignment.Position = IntVec3.Invalid;
            Check(!Ready(), "No valid safe slot cannot be treated as a completed formation.");
            var execution = new MapComponent_RaidTacticalExecution(map);
            var states = (Dictionary<string, MapComponent_RaidTacticalExecution.ExecutionState>)AccessTools.Field(
                typeof(MapComponent_RaidTacticalExecution), "states").GetValue(execution);
            plan.Assignments.Add(new RaidTacticalAssignment { Pawn = pawn, Task = RaidTacticalTask.Entry, Position = cell });
            plan.Assignments.Add(new RaidTacticalAssignment { Pawn = other, Task = RaidTacticalTask.Security, Position = near });
            var state = new MapComponent_RaidTacticalExecution.ExecutionState { UnitId = "test", ActivePlan = plan };
            states.Add("test", state);
            void Index()
            {
                AccessTools.Field(typeof(MapComponent_RaidTacticalExecution), "formationIndexTick").SetValue(execution, -1);
                AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "IndexFormations").Invoke(execution, null);
            }
            var indexed = (Dictionary<IntVec3, List<RaidTacticalAssignment>>)AccessTools.Field(
                typeof(MapComponent_RaidTacticalExecution), "formationIndex").GetValue(execution);
            Index(); Check(indexed.Count == 2, "Active stack/security claims are indexed together.");
            state.Phase = RaidExecutionPhase.CrossBreach;
            Index(); Check(indexed.Count == 1 && indexed.ContainsKey(near), "Crossing entry positions no longer block following formations.");
            AccessTools.Field(state.GetType(), "SharedOpeningWait").SetValue(state, true);
            Index(); Check(indexed.Count == 0, "Queued teams do not retain claims on the leading team's stack workspace.");
            AccessTools.Field(state.GetType(), "SharedOpeningWait").SetValue(state, false);
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(other.health, PawnHealthState.Down);
            Index(); Check(indexed.Count == 0, "A downed security member releases its future formation claim.");
        }
        finally { Current.Game = previous; }
        Console.WriteLine($"PASS: {checks} native formation tile occupancy and staging readiness checks.");
    }
}
