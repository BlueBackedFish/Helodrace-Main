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
        var leader = new Pawn { thingIDNumber = 31001 }; var tail = new Pawn { thingIDNumber = 31002 };
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

        var withdrawn = new Pawn { thingIDNumber = 31003 }; var departed = new Pawn { thingIDNumber = 31004 };
        var soldierDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        soldierDef.defName = "AuditSoldier";
        foreach (Pawn value in new[] { leader, tail, withdrawn, departed })
            value.def = soldierDef;
        plan.Assignments.Add(new RaidTacticalAssignment { Pawn = leader, Task = RaidTacticalTask.Entry });
        plan.Assignments.Add(new RaidTacticalAssignment { Pawn = tail, Task = RaidTacticalTask.Security });
        plan.Assignments.Add(new RaidTacticalAssignment { Pawn = withdrawn, Task = RaidTacticalTask.Withdraw });
        plan.Assignments.Add(new RaidTacticalAssignment { Pawn = departed, Task = RaidTacticalTask.Entry });
        state.ContactGuards.Add(new RaidContactGuard { Pawn = tail, Until = 600 });
        var cohortMethod = AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "ApproachAssignments");
        List<RaidTacticalAssignment> Cohort(int tick) => (List<RaidTacticalAssignment>)cohortMethod.Invoke(null,
            new object[] { new List<Pawn> { leader, tail, withdrawn }, plan, state, tick });
        Check(Cohort(599).Count == 1 && Cohort(599)[0].Pawn == leader,
            "Active security guards do not block final approach; withdrawn and departed members are excluded.");
        Check(Cohort(600).Count == 2, "An expired contact guard rejoins the same movement/readiness cohort.");
        state.ContactGuards.Clear();
        Check(Cohort(100).Count == 2 && state.NodeMembers[1].Completed == -1,
            "Releasing a guard resumes its personal progress without pretending that it already crossed the route.");
        var guardLookup = AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "ContactGuardFor",
            new[] { typeof(MapComponent_RaidTacticalExecution.ExecutionState), typeof(Pawn), typeof(int) });
        Check(guardLookup.Invoke(null, new object[] { state, tail, 100 }) == null,
            "Released security guards stop owning subsequent movement orders immediately.");
        state.Phase = RaidExecutionPhase.Assemble; state.ApproachComplete = true; state.CurrentNode = 2;
        leadingProgress.Completed = 1;
        AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "ResumeReleasedGuardApproach")
            .Invoke(null, new object[] { state, plan, tail });
        Check(!state.ApproachComplete && state.CurrentNode == 1 && leadingProgress.Completed == 1
            && state.NodeMembers[1].Completed == 0 && Connection(tail) == plan.MovementNodes[1],
            "A released guard restores only its final personal join even after the lead completed; old origin nodes stay skipped.");
        var diagnostic = new RaidMovementDiagnostics();
        diagnostic.Request(RaidOrderKind.Move, new IntVec3(3, 0, 4), RaidMoveController.Formation, 100, true);
        diagnostic.Block(RaidMoveBlockReason.GridPreparing, 105);
        diagnostic.Block(RaidMoveBlockReason.GridPreparing, 120);
        Check(diagnostic.BlockedSince == 105 && diagnostic.StartedTick == -1,
            "Repeated grid waits preserve wait age and do not claim that a Goto has started.");
        diagnostic.Started(130);
        Check(diagnostic.LastStartLatency == 30 && diagnostic.StartedTick == 130 && diagnostic.BlockedSince == -1,
            "Actual Goto start records request latency and clears the wait reason.");
        diagnostic.Request(RaidOrderKind.Move, new IntVec3(3, 0, 4), RaidMoveController.Formation, 140, false);
        diagnostic.Started(150);
        Check(diagnostic.RequestedTick == 100 && diagnostic.StartedTick == 130,
            "An unchanged directive or a continuation does not restart its movement latency measurement.");

        var movementAreaType = typeof(RaidMovementDiagnostics).Assembly.GetType("Helodrace.RaidMovementArea");
        object retainedArea = RuntimeHelpers.GetUninitializedObject(movementAreaType);
        var leaseField = AccessTools.Field(movementAreaType, "Lease");
        object nativeLease = Activator.CreateInstance(leaseField.FieldType);
        leaseField.SetValue(retainedArea, nativeLease);
        var nativeRequest = (PathRequest)RuntimeHelpers.GetUninitializedObject(typeof(PathRequest));
        nativeRequest.customizer = (PathRequest.IPathGridCustomizer)retainedArea;
        bool CanRetire() => (bool)AccessTools.Property(leaseField.FieldType, "CanRetire").GetValue(nativeLease);
        Patch_RaidMovementArea_Request.Postfix(nativeRequest, null);
        Check(!CanRetire(), "The actual game PathRequest creation hook acquires its movement-grid lease.");
        AccessTools.Method(leaseField.FieldType, "BeginRead").Invoke(nativeLease, null);
        nativeRequest.Dispose(); Patch_RaidMovementArea_RequestCancelled.Postfix(nativeRequest);
        Check(!CanRetire(), "The actual cancellation hook releases the request but preserves the running native read.");
        AccessTools.Method(leaseField.FieldType, "CompleteReads").Invoke(nativeLease, null);
        Check(CanRetire(), "Native retirement becomes possible only after the completion barrier.");
        Patch_RaidMovementArea_Request.Postfix(nativeRequest, null);
        nativeRequest.Resolve(null); Patch_RaidMovementArea_RequestResolved.Postfix(nativeRequest);
        Check(CanRetire(), "The real request resolution hook releases completed request ownership.");

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

        var knownJoin = AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "KnownIndoorJoin");
        var rear = new IntVec3(7, 0, 8);
        RaidMovementNode Join(Func<IntVec3, bool> known) => (RaidMovementNode)knownJoin.Invoke(null, new object[] {
            map, snapshot, rear, target, new HashSet<int> { 1, 2 }, known, new HashSet<IntVec3>() });
        Check(Join(_ => false) == null, "A rear member cannot join through a wall or an unobserved open doorway.");
        var throughGap = Join(cell => cell == passage);
        Check(throughGap != null && throughGap.AllowedPortals.Contains(passage)
            && !throughGap.AllowedRooms.Contains(0), "The known demolished wall joins rooms without authorizing all exterior room-zero cells.");
        var joinCells = (HashSet<IntVec3>)AccessTools.Field(typeof(RaidMovementNode), "RestrictedCells").GetValue(throughGap);
        Check(joinCells.Contains(rear) && joinCells.Contains(passage) && joinCells.Contains(target)
            && !joinCells.Contains(opening) && !joinCells.Contains(oldEntry) && !joinCells.Contains(new IntVec3(4, 0, 8)),
            "The personal join corridor contains a connected indoor route and excludes both exterior entrances.");
        var throughDoor = Join(cell => cell == innerDoor);
        Check(throughDoor != null && throughDoor.AllowedPortals.Contains(innerDoor)
            && !throughDoor.AllowedRooms.Contains(10), "A door's own frozen room number is allowed at that portal, not as an entire room.");
        var routeConnection = new RaidMovementNode { Center = target };
        var captureConnection = AccessTools.Method(assembly.GetType("Helodrace.RaidNodeRoute"), "CaptureConnection");
        captureConnection.Invoke(null, new object[] { map, snapshot, routeConnection, new[] { rear, passage, target } });
        Check(routeConnection.AllowedRooms.Contains(1) && routeConnection.AllowedRooms.Contains(2)
            && !routeConnection.AllowedRooms.Contains(0) && routeConnection.AllowedPortals.Contains(passage),
            "Ordinary indoor nodes also authorize the exact demolished wall without authorizing all outside cells.");
        var effectiveRoom = AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "ConnectionRoom");
        int EffectiveRoom(IntVec3 cell, RaidMovementNode node) => (int)effectiveRoom.Invoke(null, new object[] { map, snapshot, cell, node });
        Check(EffectiveRoom(passage, routeConnection) == -1 && EffectiveRoom(innerDoor, throughDoor) == -1,
            "Standing on a frozen gap or door cannot turn its room ID into unrestricted escape-room permission.");
        Check(EffectiveRoom(rear, routeConnection) == 1
            && EffectiveRoom(new IntVec3(4, 0, 8), new RaidMovementNode { AllowedRooms = new List<int> { 0 } }) == 0,
            "Real room floors and genuine exterior approaches retain their normal source-room permissions.");
        state.Phase = RaidExecutionPhase.Assemble;
        AccessTools.Field(typeof(RaidNodeMemberProgress), "JoinConnection").SetValue(state.NodeMembers[1], throughDoor);
        Check(Connection(tail) == throughDoor, "Physical admission and path costs use the same personal known-passage join.");
        AccessTools.Field(typeof(RaidNodeMemberProgress), "JoinConnection").SetValue(state.NodeMembers[1], null);

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
