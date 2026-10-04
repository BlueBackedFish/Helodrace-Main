using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Helodrace;

internal static class Program
{
    private static int checks;
    private static void CheckMovementNodes()
    {
        Check(TacticalNodeProgress.Endpoint(9, i => i != 0, i => i, _ => true) == 1,
            "A reserved shared center uses a nearby endpoint without a retry wait");
        Check(TacticalNodeProgress.Endpoint(9, _ => true, i => i == 0 ? 1000 : i, _ => true) == 1,
            "Pending peer movement avoids same-tick endpoint collisions");
        Check(TacticalNodeProgress.Endpoint(9, _ => true, i => i, i => i > 2) == 3,
            "Unreachable nearby endpoints do not stop all followers");
        Check(TacticalNodeProgress.Endpoint(9, _ => false, i => i, _ => true) == -1,
            "A genuinely full passage does not ignore reservations");
        int calls = 0;
        Check(TacticalNodeProgress.Endpoint(100, _ => true, i => i, _ => { calls++; return false; }) == -1
            && calls == 24, "Endpoint reachability work is bounded");
        Check(TacticalNodeProgress.Select(0, _ => false, (_, _) => true).Count == 0,
            "An absent route cannot fabricate a movement node");
        var open = TacticalNodeProgress.Select(65, _ => false, (_, _) => true);
        Check(open.SequenceEqual(new[] { 0, 16, 32, 48, 64 }),
            "Open field routes use sparse waypoints rather than one order per tile");
        var portals = TacticalNodeProgress.Select(20, index => index == 7 || index == 8,
            (_, _) => true);
        Check(portals.Contains(7) && portals.Contains(8), "Required portal transitions cannot be skipped");
        var corner = TacticalNodeProgress.Select(12, _ => false, (from, to) => !(from < 5 && to > 5));
        Check(corner.Contains(5), "A blocked diagonal cannot connect nodes through a corner wall");
        Check(TacticalNodeProgress.Advance(false, 13, 1), "A lead member advances without a transit quorum");
        Check(!TacticalNodeProgress.Advance(false, 13, 0), "No false physical progress");
        Check(!TacticalNodeProgress.Advance(true, 13, 12), "Final staging still waits for active members");
        Check(TacticalNodeProgress.Advance(true, 13, 13), "Final handoff completes");
        Check(TacticalNodeProgress.Advance(true, 0, 0), "Unavailable members do not block movement");
        Check(TacticalNodeProgress.OutsideSince(false, 10, 80) == -1, "Small detours clear deviation timing");
        Check(!TacticalNodeProgress.NeedsCorrection(10, 99, 90), "Brief detours do not trigger correction");
        Check(TacticalNodeProgress.NeedsCorrection(10, 100, 90), "Persistent deviation triggers a forward join");
        Check(TacticalNodeProgress.ForwardJoin(1, 6, _ => false, _ => true) == 5,
            "A tail member joins the forward aim instead of revisiting ordinary nodes");
        Check(TacticalNodeProgress.ForwardJoin(1, 6, i => i == 3, _ => true) == 1,
            "Forward joins cannot skip a required opening");
        Check(TacticalNodeProgress.ForwardJoin(1, 6, _ => false, _ => false) == 1,
            "A wall blocks false forward progress");
        Check(TacticalNodeProgress.ForwardJoin(5, 2, _ => false, _ => true) == 5,
            "A shared aim never pulls a member back to passed connections");
        Check(TacticalNodeProgress.Arrive(3, 5, 8, _ => false) == 3,
            "Temporary evasion cannot reset already completed nodes");
        Check(TacticalNodeProgress.Arrive(1, 5, 8, index => index != 2) == 1,
            "A late member cannot jump over an unvisited required node");
        Check(TacticalNodeProgress.Arrive(1, 5, 8, index => index <= 3) == 3,
            "A tail member follows its own unfinished sequence without moving the lead backwards");
        Check(TacticalNodeProgress.Arrive(7, 99, 8, _ => true) == 7,
            "Completed routes stay complete after restore or a repeated update");
        Check(!TacticalNodeProgress.WalkLine(0, 0, 2, 2, (x, z) => !(x == 1 && z == 0)),
            "A diagonal shoulder wall prevents a corner shortcut");
        Check(!TacticalNodeProgress.WalkLine(0, 0, 6, 0, (x, z) => x != 3),
            "A solid wall prevents a false straight node connection");
        Check(TacticalNodeProgress.WalkLine(0, 0, 6, 0, (_, _) => true),
            "A physically opened connection is usable without restoring a route leash");
        Check(TacticalNodeProgress.Advance(false, 13, 1), "Passage capacity is not a transit quorum");
        Check(!TacticalNodeProgress.AllowsStep(1, 0, true, false, room => room == 0 || room == 1),
            "A physically reachable but unselected door is rejected at the crossing");
        Check(TacticalNodeProgress.AllowsStep(1, 0, true, true, room => room == 0 || room == 1),
            "The selected doorway is accepted");
        Check(TacticalNodeProgress.AllowsStep(3, 0, true, false, room => room == 0 || room == 1),
            "An evaded pawn can exit its off-route room to rejoin the valid connection");
        Check(!TacticalNodeProgress.AllowsStep(1, 3, false, false, room => room == 0 || room == 1),
            "A new hole into an unselected room is not silently used as a shortcut");
        Check(TacticalNodeProgress.AllowsStep(1, 3, true, true, room => room == 2),
            "A selected known portal may have its own frozen room ID outside the floor-room list");
        Check(TacticalNodeProgress.AllowsStep(1, 0, false, true, room => room == 2),
            "A selected demolished wall is permitted at that exact connection cell");
        Check(!TacticalNodeProgress.AllowsStep(1, 0, false, false, room => room == 2),
            "Permission for a selected gap does not permit other exterior cells");
    }
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    private static TacticalGeometryInput Open(int width, int height)
    {
        var input = new TacticalGeometryInput(width, height);
        for (int i = 0; i < input.Cells.Length; i++)
            input.Cells[i].Flags = TacticalRawFlags.Standable | TacticalRawFlags.Outside | TacticalRawFlags.Unroofed;
        return input;
    }
    private static void Wall(TacticalGeometryInput input, int x, int z)
    {
        input.Cells[z * input.Width + x] = new TacticalRawCell {
            Flags = TacticalRawFlags.WallLine | TacticalRawFlags.Edifice, StructureId = z * input.Width + x + 1
        };
    }
    private static void CheckPreparationQueue()
    {
        var lease = new TacticalNativeLease<object>();
        object request = new object();
        Check(lease.CanRetire, "An unused native cache entry may be retired");
        lease.Acquire(request); lease.Acquire(request);
        Check(!lease.CanRetire && lease.Requests == 1, "Created/queued requests retain their grid, with duplicate acquisition coalesced");
        lease.BeginRead(); lease.Release(request);
        Check(!lease.CanRetire && lease.Requests == 0, "Cancelling a running request does not permit disposal while its grid job still reads");
        lease.CompleteReads();
        Check(lease.CanRetire, "The native completion barrier permits retirement after all requests release");
        lease.Acquire(request); lease.BeginRead(); lease.CompleteReads();
        Check(!lease.CanRetire, "A completion barrier cannot retire a grid still retained by another pending request");
        lease.Release(request);
        Check(lease.CanRetire, "Final request resolution releases the completed grid");
        var queue = new TacticalPreparationQueue<object, object>();
        object old = new object(), speculative = new object(), later = new object();
        object a = new object(), b = new object(), c = new object();
        queue.Add(speculative, 0); queue.Add(old, 10); queue.Add(later, 20);
        queue.WaitFor(a, old); queue.WaitFor(b, later);
        queue.WaitFor(a, old); queue.Add(old, 200);
        Check(queue.Count == 3 && queue.WaiterCount == 2 && queue.ServiceOrder(1).Single() == old,
            "Actual movement waits precede speculative preparation, with repeated requests preserving FIFO age");
        queue.WaitFor(c, old); queue.WaitFor(a, later);
        Check(queue.Complete(old).SequenceEqual(new[] { c }), "Ready notifications go only to current waiters, not entire plans or superseded orders");
        Check(queue.WaiterCount == 2 && queue.OldestWaitAge(100) == 80, "Switching a waiter does not lose another pawn's pending task");
        queue.Forget(b); queue.Forget(a);
        Check(queue.Complete(later).Length == 0 && queue.WaiterCount == 0, "Released orders and reactive detours stop receiving preparation notifications");
        queue.Clear();
        object first = null;
        for (int i = 0; i < 1000; i++)
        {
            object task = new object();
            if (i == 0) first = task;
            queue.Add(task, i); queue.WaitFor(new object(), task);
        }
        Check(queue.Count == 1000 && queue.PeakCount == 1000 && queue.ServiceOrder(64).Count() == 64
            && queue.ServiceOrder(64).First() == first, "A burst above 64 requests limits service per pass without cancelling old movers");
        queue.Complete(first);
        Check(queue.Count == 999 && queue.WaiterCount == 999, "FIFO completion releases just the completed task and its waiters");
        queue.Clear();
        Check(queue.Count == 0 && queue.WaiterCount == 0 && !queue.ServiceOrder(64).Any(), "Map teardown drops all pending queue ownership");
    }

    private static int Main()
    {
        CheckPreparationQueue();
        try
        {
            var divided = Open(7, 5);
            for (int z = 0; z < 5; z++) Wall(divided, 3, z);
            int doorIndex = 2 * 7 + 3;
            divided.Cells[doorIndex].Flags |= TacticalRawFlags.Door;
            for (int z = 0; z < 5; z++)
                for (int x = 4; x < 7; x++)
                    divided.Cells[z * 7 + x] = new TacticalRawCell {
                        Flags = TacticalRawFlags.Standable, Room = 1
                    };
            divided.Cells[18].Flags |= TacticalRawFlags.Anchor;
            string inputBefore = string.Join(",", divided.Cells.Select(cell => $"{cell.Flags}:{cell.Room}:{cell.StructureId}"));
            TacticalGeometryResult geometry = TacticalGeometry.Calculate(divided, CancellationToken.None);
            Check(geometry.RoomAreas[1] == 15, "Room area is counted in the existing background geometry pass");
            var areaInput = Open(6, 4);
            foreach (int index in Enumerable.Range(0, 20)) areaInput.Cells[index].Room = 2;
            areaInput.Cells[0].Flags = TacticalRawFlags.Edifice; // Furniture still occupies floor area.
            areaInput.Cells[1].Flags = TacticalRawFlags.WallLine;
            areaInput.Cells[2].Flags = TacticalRawFlags.Door;
            TacticalGeometryResult areaGeometry = TacticalGeometry.Calculate(areaInput, CancellationToken.None);
            Check(areaGeometry.RoomAreas[2] == 18 && !areaGeometry.RoomAreas.ContainsKey(0),
                "Room floor area includes furniture but excludes wall, door and outdoor cells");
            Check(TacticalGeometryCodec.Unpack(TacticalGeometryCodec.Pack(areaGeometry), 6, 4).RoomAreas[2] == 18,
                "Restored fixed-room area is rebuilt while decoding, without a tactical full-map scan");
            Check(geometry.ComponentCount == 2, "A closed dividing door leaves two structural components");
            Check(geometry.Components[doorIndex] == 0, "Door cells are joined through live state, not permanently flooded");
            Check(geometry.Components[14] != geometry.Components[20], "Opposite door sides stay independent");
            Check(geometry.Cells[doorIndex].ExteriorAccess, "The door between indoor and outdoor flags is exterior access");
            Check(geometry.Cells[doorIndex].Structures.HasFlag(TacticalStructureKind.Door), "Door structure classification survives capture");
            Check(geometry.Cells[doorIndex].DoorThreat == 12 && geometry.Cells[16].DoorThreat == 8,
                "Door and adjacent geometry scores preserve the previous rules");
            Check(geometry.Doors.SequenceEqual(new[] { doorIndex }), "Only actual doors enter the door overlay list");
            Check(geometry.Breaches.Length == 5, "Every original wall/door remains a breach target");
            Check(geometry.Anchors.SequenceEqual(new[] { 18 }), "Indoor objective anchors are value indices");
            var closedGraph = new TacticalNavigationGraph(geometry, Array.Empty<int>());
            Check(!closedGraph.Connected(14, 20), "A closed inaccessible door cannot join cached regions");
            var openGraph = new TacticalNavigationGraph(geometry, new[] { doorIndex });
            Check(openGraph.Connected(14, 20) && openGraph.Connected(doorIndex, 14),
                "An open or authorized door joins both regions and its own cell");
            Check(!closedGraph.Connected(14, 20), "A new door overlay cannot mutate another plan's closed graph");
            var maskInput = new TacticalMovementMaskInput {
                Width = 7, Height = 5, Structure = geometry,
                Reactive = true, InitialRoom = 1
            };
            ushort[] reactionMask = TacticalMovementMask.Calculate(maskInput, CancellationToken.None);
            Check(reactionMask[20] == 0 && reactionMask[14] == ushort.MaxValue,
                "An emergency mask allows the original room and forbids other rooms");
            maskInput.Reactive = false;
            maskInput.ExteriorOnly = true;
            maskInput.InitialRoom = 0;
            ushort[] exteriorMask = TacticalMovementMask.Calculate(maskInput, CancellationToken.None);
            Check(exteriorMask[16] == 0 && exteriorMask[0] == 0,
                "Waypoint movement leaves walkable detours at vanilla cost instead of pulling pawns back to a route line");
            Check(exteriorMask[18] == ushort.MaxValue,
                "A corridor overlapping the interior cannot override exterior staging restrictions");
            maskInput.RestrictPortals = true;
            Check(TacticalMovementMask.Calculate(maskInput, CancellationToken.None)[doorIndex] == ushort.MaxValue,
                "Node approach discourages every unselected door instead of allowing a detour through a different entry");
            maskInput.AllowedPortals = new[] { doorIndex };
            maskInput.ExteriorOnly = false;
            Check(TacticalMovementMask.Calculate(maskInput, CancellationToken.None)[doorIndex] == 0,
                "Only the planned portal retains vanilla passage cost");
            maskInput.RestrictPortals = false;
            maskInput.ExteriorOnly = false;
            maskInput.SelectedOpeningOnly = true;
            maskInput.BreachIndex = doorIndex;
            ushort[] crossingMask = TacticalMovementMask.Calculate(maskInput, CancellationToken.None);
            Check(crossingMask[doorIndex] != ushort.MaxValue && crossingMask[3] == ushort.MaxValue,
                "Only the selected wall opening can be used while crossing");
            maskInput.SelectedOpeningOnly = false;
            var ingressInput = Open(7, 5);
            for (int z = 0; z < 5; z++) Wall(ingressInput, 3, z);
            for (int z = 0; z < 5; z++)
                for (int x = 4; x < 7; x++) ingressInput.Cells[z * 7 + x].Room = 1;
            ingressInput.Cells[doorIndex].Flags |= TacticalRawFlags.Door;
            ingressInput.Cells[doorIndex].Room = 9;
            ingressInput.Cells[0].Room = 2;
            var ingressMask = TacticalMovementMask.Calculate(new TacticalMovementMaskInput {
                Width = 7, Height = 5, Structure = TacticalGeometry.Calculate(ingressInput, CancellationToken.None),
                ExteriorOnly = true, InitialRoom = 1, SelectedOpeningOnly = true, BreachIndex = doorIndex
            }, CancellationToken.None);
            Check(ingressMask[doorIndex] == 0 && ingressMask[18] == 0 && ingressMask[14] == 0,
                "Committed ingress joins exterior, doorway room and original interior after a new room plan");
            Check(ingressMask[3] == ushort.MaxValue && ingressMask[0] == ushort.MaxValue,
                "Old entrances and unrelated rooms stay excluded during follower ingress");
            foreach (int currentRoom in new[] { 0, 1, 2 })
            {
                var connectionMask = TacticalMovementMask.Calculate(new TacticalMovementMaskInput {
                    Width = 6, Height = 4, Structure = areaGeometry, InitialRoom = currentRoom,
                    RestrictRooms = true, AllowedRooms = new[] { 0, 1 }, RestrictPortals = true,
                    AllowedPortals = Array.Empty<int>()
                }, CancellationToken.None);
                for (int i = 0; i < connectionMask.Length; i++)
                    Check((connectionMask[i] == 0) == TacticalNodeProgress.AllowsStep(currentRoom,
                        areaInput.Cells[i].Room, areaInput.Cells[i].Has(TacticalRawFlags.Door), false,
                        room => room == 0 || room == 1),
                        "Personal connection path costs and physical entry permission agree, including escape from an off-route room");
            }
            var indoorTransit = TacticalMovementMask.Calculate(new TacticalMovementMaskInput {
                Width = 7, Height = 5, Structure = geometry, RestrictCells = true,
                AllowedCells = new[] { 18, 19, 20 }, BreachIndex = doorIndex
            }, CancellationToken.None);
            Check(indoorTransit[18] == 0 && indoorTransit[doorIndex] == 0 && indoorTransit[14] == ushort.MaxValue,
                "An admitted follower uses a proved indoor connection without reopening unrelated exterior paths");
            var joinInput = Open(4, 1);
            joinInput.Cells[0].Room = 1; joinInput.Cells[1].Room = 3;
            joinInput.Cells[1].Flags |= TacticalRawFlags.Door;
            joinInput.Cells[2].Room = 0; joinInput.Cells[3].Room = 2;
            var joinMask = TacticalMovementMask.Calculate(new TacticalMovementMaskInput {
                Width = 4, Height = 1, Structure = TacticalGeometry.Calculate(joinInput, CancellationToken.None),
                InitialRoom = 1, RestrictRooms = true, AllowedRooms = new[] { 2 }, RestrictPortals = true,
                AllowedPortals = new[] { 1, 2 }, RestrictCells = true, AllowedCells = new[] { 0, 1, 2, 3 }
            }, CancellationToken.None);
            Check(joinMask.All(cost => cost == 0), "Known portal exceptions connect a prior room through door and demolished-wall IDs");
            var confinedJoin = TacticalMovementMask.Calculate(new TacticalMovementMaskInput {
                Width = 4, Height = 1, Structure = TacticalGeometry.Calculate(joinInput, CancellationToken.None),
                InitialRoom = 1, RestrictRooms = true, AllowedRooms = new[] { 2 }, RestrictPortals = true,
                AllowedPortals = new[] { 1 }, RestrictCells = true, AllowedCells = new[] { 0, 1, 3 }, BreachIndex = 2
            }, CancellationToken.None);
            Check(confinedJoin[2] == ushort.MaxValue, "An unrelated planned breach cannot override a personal join corridor");
            maskInput.ExcludedRoom = 1;
            Check(TacticalMovementMask.Calculate(maskInput, CancellationToken.None)[18] == ushort.MaxValue,
                "Support masks prevent entry into the grenade target room");
            maskInput.ExcludedRoom = 0;
            maskInput.Fight = true;
            maskInput.FightX = 5; maskInput.FightZ = 2; maskInput.FightRadius = 2;
            maskInput.FightRoom = 1;
            ushort[] fightMask = TacticalMovementMask.Calculate(maskInput, CancellationToken.None);
            Check(fightMask[19] != ushort.MaxValue && fightMask[16] == ushort.MaxValue && fightMask[0] == ushort.MaxValue,
                "Fight masks enforce both activity radius and original room");
            maskInput.LeashX = 5; maskInput.LeashZ = 2; maskInput.LeashRadius = 1;
            Check(TacticalMovementMask.Calculate(maskInput, CancellationToken.None)[5] == ushort.MaxValue,
                "A defense leash remains effective inside the fight radius");
            Check(geometry.ComponentCount == 2 && geometry.Components[14] != geometry.Components[20],
                "Live door overlays do not overwrite frozen structural component IDs");
            var breachedGraph = new TacticalNavigationGraph(geometry, new[] { 3 });
            Check(breachedGraph.Connected(0, 6), "A destroyed wall connects original structural regions");
            var doubleWall = Open(8, 4);
            for (int z = 0; z < 4; z++) { Wall(doubleWall, 3, z); Wall(doubleWall, 4, z); }
            TacticalGeometryResult doubleGeometry = TacticalGeometry.Calculate(doubleWall, CancellationToken.None);
            Check(!new TacticalNavigationGraph(doubleGeometry, new[] { 11 }).Connected(8, 15),
                "Breaking only half of a double wall does not open the room");
            Check(new TacticalNavigationGraph(doubleGeometry, new[] { 11, 12 }).Connected(8, 15),
                "Consecutive destroyed wall cells join through one another");
            Check(inputBefore == string.Join(",", divided.Cells.Select(cell => $"{cell.Flags}:{cell.Room}:{cell.StructureId}")),
                "Calculation does not mutate the captured input");

            var gap = Open(7, 5);
            Wall(gap, 3, 1); Wall(gap, 3, 3);
            gap.Cells[16].Flags &= ~TacticalRawFlags.Unroofed;
            TacticalGeometryResult opening = TacticalGeometry.Calculate(gap, CancellationToken.None);
            Check(opening.Cells[17].Structures.HasFlag(TacticalStructureKind.Opening), "An isolated wall gap is classified as an opening");
            Check(opening.Cells[17].ExteriorAccess, "Roof difference marks exterior access even in a merged vanilla room");
            Check(opening.Cells[17].WallThreat == 6, "An isolated passage retains its geometry score");
            var corridor = Open(7, 5);
            for (int x = 0; x < 7; x++) { Wall(corridor, x, 1); Wall(corridor, x, 3); }
            TacticalGeometryResult hallway = TacticalGeometry.Calculate(corridor, CancellationToken.None);
            Check(hallway.Cells[17].Structures.HasFlag(TacticalStructureKind.Corridor), "A continuing hallway is not an isolated breach opening");
            Check(!hallway.Cells[17].Structures.HasFlag(TacticalStructureKind.Opening), "Neighbor lookahead preserves corridor continuity");

            var wrap = new TacticalGeometryInput(3, 2);
            wrap.Cells[2].Flags = TacticalRawFlags.Standable;
            wrap.Cells[3].Flags = TacticalRawFlags.Standable;
            TacticalGeometryResult edges = TacticalGeometry.Calculate(wrap, CancellationToken.None);
            Check(edges.ComponentCount == 2, "Flat indexing never connects opposite row edges");
            Check(!new TacticalNavigationGraph(edges, new[] { -1, 6 }).Connected(2, 3),
                "Invalid overlay cells cannot wrap graph connections across row edges");
            var furniture = Open(3, 3);
            furniture.Cells[7].Flags = TacticalRawFlags.Edifice;
            furniture.Cells[5].Flags = TacticalRawFlags.Edifice;
            TacticalGeometryResult corner = TacticalGeometry.Calculate(furniture, CancellationToken.None);
            Check(corner.Cells[4].Structures.HasFlag(TacticalStructureKind.Corner), "Impassable furniture keeps the legacy corner classification");
            Check(furniture.Cells.All(cell => !cell.Has(TacticalRawFlags.WallLine)), "Furniture is not turned into a physical breach wall");

            for (int seed = 0; seed < 40; seed++)
            {
                var random = new Random(seed);
                var input = Open(19, 13);
                for (int i = 0; i < input.Cells.Length; i++)
                {
                    if (random.Next(4) == 0) Wall(input, i % 19, i / 19);
                    if (random.Next(15) == 0) input.Cells[i].Flags |= TacticalRawFlags.Door;
                    input.Cells[i].Room = i % 3;
                }
                TacticalGeometryResult first = TacticalGeometry.Calculate(input, CancellationToken.None);
                TacticalGeometryResult second = TacticalGeometry.Calculate(input, CancellationToken.None);
                Check(first.Components.SequenceEqual(second.Components), "Component numbering is deterministic");
                string packed = TacticalGeometryCodec.Pack(first);
                Check(packed == TacticalGeometryCodec.Pack(second), "Pure geometry output is reproducible");
                TacticalGeometryResult restored = TacticalGeometryCodec.Unpack(packed, 19, 13);
                Check(packed == TacticalGeometryCodec.Pack(restored), "Shared version codec roundtrip preserves all values");
                Check(first.Breaches.SequenceEqual(restored.Breaches) && first.Doors.SequenceEqual(restored.Doors)
                    && first.Anchors.SequenceEqual(restored.Anchors), "Target lists reconstruct from the shared saved cells");
            }
            bool invalid = false;
            try { TacticalGeometryCodec.Unpack(TacticalGeometryCodec.Pack(geometry), 8, 5); }
            catch (InvalidDataException) { invalid = true; }
            Check(invalid, "A version from a different map size is rejected");
            invalid = false;
            try { TacticalGeometryCodec.Unpack("invalid base64", 7, 5); }
            catch (FormatException) { invalid = true; }
            Check(invalid, "Corrupt saved geometry is rejected");

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool canceled = false;
                try { TacticalGeometry.Calculate(divided, cancellation.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled, "An obsolete map request can cancel pure calculation before publication");
            }
            Check(TacticalGeometryWorker.TryStart(divided, CancellationToken.None, out var task), "A worker accepts value-only input");
            Check(task.Wait(TimeSpan.FromSeconds(10)), "Worker completion has a bounded test timeout");
            Check(TacticalGeometryCodec.Pack(task.Result) == TacticalGeometryCodec.Pack(geometry), "Background calculation equals the synchronous reference");
            TacticalGeometryWorker.Release(task);
            Check(TacticalGeometryWorker.TryStart(wrap, CancellationToken.None, out var next), "A completed worker slot is reusable by another map");
            Check(next.Wait(TimeSpan.FromSeconds(10)) && next.Result.ComponentCount == 2, "A reused slot does not return the previous map's result");
            TacticalGeometryWorker.Release(next);
            Check(TacticalGeometryWorker.TryStart(maskInput, CancellationToken.None, out var maskTask),
                "Movement masks use the same bounded worker slot as geometry");
            Check(maskTask.Wait(TimeSpan.FromSeconds(10))
                && maskTask.Result.SequenceEqual(TacticalMovementMask.Calculate(maskInput, CancellationToken.None)),
                "Background movement cost calculation preserves synchronous results");
            TacticalGeometryWorker.Release(maskTask);
            // Hold a controlled value-only job in the production scheduler to
            // test its concurrency bound without timing a large calculation.
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                int callerThread = Environment.CurrentManagedThreadId;
                Func<int> blocked = () => {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                    return Environment.CurrentManagedThreadId;
                };
                var start = typeof(TacticalGeometryWorker).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method => method.Name == "TryStart" && method.IsGenericMethodDefinition)
                    .MakeGenericMethod(typeof(int));
                object[] args = { blocked, CancellationToken.None, null };
                Check((bool)start.Invoke(null, args), "The shared scheduler accepts an idle slot");
                var blockedTask = (System.Threading.Tasks.Task<int>)args[2];
                Check(entered.Wait(TimeSpan.FromSeconds(10)), "The controlled worker starts off the caller thread");
                try
                {
                    Check(!TacticalGeometryWorker.TryStart(divided, CancellationToken.None, out _),
                        "A busy shared worker rejects another map's geometry without enqueueing an unbounded task");
                    Check(!TacticalGeometryWorker.TryStart(maskInput, CancellationToken.None, out _),
                        "Movement masks cannot add a second concurrent worker while geometry owns the slot");
                }
                finally { release.Set(); }
                Check(blockedTask.Wait(TimeSpan.FromSeconds(10)) && blockedTask.Result != callerThread,
                    "Default task scheduling runs the pure calculation on a different thread");
                TacticalGeometryWorker.Release(blockedTask);
            }
            CheckMovementNodes();
            Console.WriteLine($"PASS: {checks} tactical geometry, movement node, immutable input, codec and worker checks.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
