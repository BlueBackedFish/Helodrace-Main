using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class RaidSecurityMovementTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var leader = new Pawn(); var tail = new Pawn();
        var plan = new RaidTacticalPlan();
        plan.MovementNodes.Add(new RaidMovementNode { Id = 0 }); plan.MovementNodes.Add(new RaidMovementNode { Id = 1 });
        var state = new MapComponent_RaidTacticalExecution.ExecutionState { ActivePlan = plan, Phase = RaidExecutionPhase.Assemble };
        var leadingProgress = new RaidNodeMemberProgress { Pawn = leader, Completed = 1 };
        state.NodeMembers.Add(leadingProgress); state.NodeMembers.Add(new RaidNodeMemberProgress { Pawn = tail, Completed = -1 });
        var connection = AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "ConnectionFor");
        object Connection(Pawn pawn) => connection.Invoke(null, new object[] { state, pawn });
        Check(Connection(leader) == null && Connection(tail) == plan.MovementNodes[0],
            "Security that finished approach loses connection costs even while a tail member is still approaching.");
        leadingProgress.Completed = 0;
        Check(Connection(leader) == plan.MovementNodes[1], "Path costs select the member's next connection rather than the whole route.");
        state.ApproachComplete = true;
        Check(Connection(tail) == null, "Completed unit approaches do not retain node portal restrictions.");
        state.ApproachComplete = false; state.Phase = RaidExecutionPhase.Breach;
        Check(Connection(tail) == null, "Stack/security positioning after approach uses normal local path costs.");

        const int width = 21, height = 17;
        var assembly = typeof(RaidStructureSnapshot).Assembly;
        var inputType = assembly.GetType("Helodrace.TacticalGeometryInput");
        var rawType = assembly.GetType("Helodrace.TacticalRawCell"); var flagsType = assembly.GetType("Helodrace.TacticalRawFlags");
        object input = Activator.CreateInstance(inputType, new object[] { width, height });
        var cells = (Array)AccessTools.Field(inputType, "Cells").GetValue(input);
        var floor = new HashSet<IntVec3>();
        for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                int room = z >= 7 && z <= 10 ? x >= 6 && x <= 8 ? 1 : x >= 10 && x <= 14 ? 2 : 0 : 0;
                bool oldDoor = x == 5 && z == 9;
                bool indoorDoor = x == 9 && z == 9;
                if (oldDoor) room = 9;
                if (indoorDoor) room = 10;
                bool wall = x == 5 || x == 9;
                object cell = Activator.CreateInstance(rawType);
                AccessTools.Field(rawType, "Room").SetValue(cell, room);
                AccessTools.Field(rawType, "Flags").SetValue(cell, Enum.Parse(flagsType,
                    oldDoor || indoorDoor ? "WallLine, Edifice, Door" : wall ? "WallLine, Edifice" : "Standable"));
                cells.SetValue(cell, z * width + x);
                if (room > 0) floor.Add(new IntVec3(x, 0, z));
            }
        var innerDoor = new IntVec3(9, 0, 9);
        floor.Remove(innerDoor);
        object geometry = AccessTools.Method(assembly.GetType("Helodrace.TacticalGeometry"), "Calculate")
            .Invoke(null, new object[] { input, CancellationToken.None });
        var version = (TacticalStructureVersion)Activator.CreateInstance(typeof(TacticalStructureVersion),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { (object)73, geometry }, null);
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)); map.info = new MapInfo { Size = new IntVec3(width, 1, height) };
        map.cellIndices = new CellIndices(width, height);
        var snapshot = (RaidStructureSnapshot)Activator.CreateInstance(typeof(RaidStructureSnapshot),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { map, version, "Followers" }, null);
        var opening = new IntVec3(5, 0, 8); var inside = new IntVec3(6, 0, 8);
        var passage = new IntVec3(9, 0, 8); var target = new IntVec3(12, 0, 8);
        var method = AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "ConnectedIngressCells");
        HashSet<IntVec3> Connected() => (HashSet<IntVec3>)method.Invoke(null, new object[] {
            map, snapshot, opening, inside, new Func<IntVec3, bool>(cell => floor.Contains(cell)) });
        Check(!Connected().Contains(target), "A closed interior wall cannot be mistaken for a route through the first room.");
        floor.Add(passage);
        Check(Connected().Contains(target), "A new opening between frozen indoor rooms permits a follower's final target without a first-room stop.");
        var oldEntry = new IntVec3(5, 0, 9); floor.Add(oldEntry); floor.Add(opening);
        Check(!Connected().Contains(oldEntry) && !Connected().Contains(opening),
            "Indoor transit never escapes via the original exterior entrance or loops back through the selected mouth.");
        floor.Remove(inside);
        Check(Connected().Count == 0, "An obstructed interior mouth cannot authorize a disconnected onward route.");
        floor.Add(inside);
        floor.Remove(passage);
        Check(!Connected().Contains(target), "Closing the local connection removes the later room from the live transit graph.");
        floor.Add(innerDoor);
        Check(Connected().Contains(innerDoor) && Connected().Contains(target),
            "A passable interior door with its own room ID remains a valid connection between indoor rooms.");
        var route = new List<IntVec3> { new IntVec3(4, 0, 8), opening, inside, new IntVec3(7, 0, 8) };
        var forward = AccessTools.Method(assembly.GetType("Helodrace.RaidNodeRoute"), "ForwardPortalCenter");
        var guide = (IntVec3)forward.Invoke(null, new object[] { route, 1 });
        Check(guide == inside && snapshot.RoomAt(guide) == 1 && snapshot.RoomAt(opening) == 0,
            "A transit guide beyond a demolished wall belongs to the destination room, never the frozen room-zero opening.");
        Check((IntVec3)forward.Invoke(null, new object[] { route, route.Count - 1 }) == route[route.Count - 1],
            "A terminal transit guide does not read beyond the planned path.");

        // Use the native ThingGrid to exercise collision admission independently
        // of Unity's path jobs; reservation permission is supplied explicitly.
        map.thingGrid = new ThingGrid(map);
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
        var queued = new Pawn { thingIDNumber = 41001 }; var blocker = new Pawn { thingIDNumber = 41002 };
        foreach (Pawn value in new[] { queued, blocker })
        {
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(value, (sbyte)0);
            value.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(value.health, PawnHealthState.Mobile);
        }
        var admission = AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "IngressOpeningAvailable");
        var waitingIngress = new RaidExteriorIngress { Pawn = queued, Opening = opening, Inside = inside, InsideRoom = 1 };
        Game previousGame = Current.Game;
        Current.Game = game;
        try
        {
            AccessTools.Field(typeof(Thing), "positionInt").SetValue(queued, route[0]);
            bool Available(bool reservationFree = true) => (bool)admission.Invoke(null, new object[] {
                queued, waitingIngress, new Func<IntVec3, bool>(_ => reservationFree) });
            Check(Available(), "A free opening admits the waiting security pawn.");
            opening.GetThingList(map).Add(blocker);
            Check(!Available(), "An actual pawn in the opening blocks admission even without a destination reservation.");
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(blocker.health, PawnHealthState.Down);
            Check(Available(), "A downed breacher does not permanently lock passage through the opening.");
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(blocker.health, PawnHealthState.Mobile);
            opening.GetThingList(map).Clear(); inside.GetThingList(map).Add(blocker);
            Check(!Available(), "An occupied inside mouth prevents security from piling up in the preceding tile.");
            waitingIngress.Entered = true;
            Check(Available(false), "A pawn already admitted keeps clearing the mouth even when another pawn reserves it.");
            waitingIngress.Entered = false;
            AccessTools.Field(typeof(Thing), "positionInt").SetValue(queued, opening);
            Check(Available(false), "A pawn already in the opening is not bounced back to an outside waiting cell.");
            AccessTools.Field(typeof(Thing), "positionInt").SetValue(queued, route[0]);
            inside.GetThingList(map).Clear();
            Check(!Available(false), "A reserved opening blocks a new follower before physical overlap occurs.");
            Check(Available(), "Releasing occupation and reservation immediately restores admission.");
        }
        finally { Current.Game = previousGame; }
        Console.WriteLine($"PASS: {checks} personal security movement and bounded indoor follower connection checks.");
    }
}
