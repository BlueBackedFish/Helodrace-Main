using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Helodrace;

internal static class Program
{
    private static int checks;
    private static void CheckPassageAdmissions()
    {
        var traffic = new TacticalPassageAdmissions<int, int>();
        Check(traffic.Request(1, 7, 0) && !traffic.Request(2, 7, 0), "One physical mouth admits only its first waiting actor.");
        Check(traffic.Request(1, 7, 30) && traffic.Count == 2, "Repeated requests neither duplicate nor reorder existing traffic.");
        Check(traffic.Request(3, 8, 30), "Independent openings do not share an admission bottleneck.");
        traffic.Release(1);
        Check(traffic.First(2, 7), "Clearing the mouth immediately promotes the next actor.");
        traffic.Request(2, 8, 40);
        Check(!traffic.First(2, 8) && traffic.Count == 2, "Changing a passage relinquishes the old token and joins the new tail.");
        traffic.Prune(50, actor => actor != 3);
        Check(traffic.First(2, 8) && traffic.Count == 1, "A casualty or withdrawn task cannot permanently hold admission.");
        traffic.Prune(131, _ => true);
        Check(traffic.Count == 0, "A task which no longer requests entry loses its stale traffic ticket.");
        for (int actor = 0; actor < 200; actor++) traffic.Request(actor, 9, 150);
        for (int actor = 0; actor < 200; actor++)
        { Check(traffic.First(actor, 9), "A large waiting cohort retains FIFO fairness without cycling leaders."); traffic.Release(actor); }
        Check(traffic.Count == 0, "Admission completion leaves no retained traffic references.");
        traffic.Request(1, 7, 200); traffic.Clear(); Check(traffic.Count == 0, "Map teardown clears derived passage traffic.");
    }
    private static void CheckObservationServices()
    {
        var due = new TacticalDueQueue<string>();
        due.Schedule("map1/unit1", 10); due.Schedule("map2/unit2", 10); due.Schedule("future", 40);
        Check(!due.TryTake(9, out _, out _) && due.Count == 3, "Early polls do not remove future execution work.");
        Check(due.TryTake(10, out string head, out int deadline) && head == "map1/unit1" && deadline == 10,
            "Equal deadlines preserve insertion order across maps.");
        due.Schedule(head, 20);
        Check(due.TryTake(11, out head, out deadline) && head == "map2/unit2" && deadline == 10,
            "Rescheduling an active unit cannot overtake a waiting sibling map.");
        due.Schedule("future", 12); due.Schedule("future", 13);
        Check(due.Count == 2 && due.OldestDelay(16) == 3 && due.TryTake(16, out head, out _) && head == "future",
            "Deadline replacement removes old entries and reports deferred work age.");
        due.Remove("map1/unit1"); due.Remove("map1/unit1");
        Check(due.Count == 0 && !due.TryTake(100, out _, out _), "Cancelled owners do not leave stale queue nodes.");
        var burst = new TacticalDueQueue<int>();
        for (int i = 0; i < 200; i++) burst.Schedule(i, 0);
        var serviced = new System.Collections.Generic.HashSet<int>();
        for (int tick = 0; tick < 50; tick++)
            for (int i = 0; i < 8 && burst.TryTake(tick, out int id, out _); i++)
            { serviced.Add(id); burst.Schedule(id, tick + 10); }
        Check(serviced.Count == 200 && burst.Count == 200,
            "Overdue cold-start work survives bounded ticks without starvation from recurring updates.");
        burst.Clear(); Check(burst.Count == 0, "Runtime queues can be rebuilt after load without stale work.");
        var soldiers = Enumerable.Range(0, 13).ToList();
        var observed = new System.Collections.Generic.HashSet<int>(); int observerCursor = 0;
        for (int i = 0; i < 13; i++)
        {
            var selected = TacticalObserverRotation.Select(soldiers, id => id == 0 || id == 8, ref observerCursor, 4);
            Check(selected.Count == 4 && selected.Distinct().Count() == 4 && selected.Contains(0) && selected.Contains(8),
                "Commander and explicit opening sensor retain observation priority within the bounded scan.");
            foreach (int id in selected) observed.Add(id);
        }
        Check(observed.Count == 13, "Ordinary fireteam members are not permanently starved of personal observations.");
        observerCursor = 0;
        Check(TacticalObserverRotation.Select(new int[] { 4 }, _ => false, ref observerCursor, 4).SequenceEqual(new[] { 4 })
            && TacticalObserverRotation.Select(new int[0], _ => false, ref observerCursor, 4).Count == 0,
            "Small or empty squads do not duplicate observers or access an invalid cursor.");
        var slices = new TacticalSliceQueue<string>();
        var trace = new System.Collections.Generic.List<string>();
        slices.Add("map1/long"); slices.Add("map2/short"); slices.Add("map1/long");
        int remaining = 4;
        int count = slices.Run(10, () => remaining-- > 0, id => { trace.Add(id); return id.EndsWith("long"); });
        Check(count == 4 && trace.SequenceEqual(new[] { "map1/long", "map2/short", "map1/long", "map1/long" }),
            "Suspended long plans yield to another map, while duplicate submission cannot multiply work.");
        Check(slices.Count == 1, "Finished requests leave the queue; suspended requests retain their continuation.");
        slices.Add("cancelled"); slices.Remove("cancelled"); trace.Clear();
        Check(slices.Run(1, () => true, id => { trace.Add(id); return true; }) == 1 && trace[0] == "map1/long",
            "Cancelled and replaced work never executes a stale continuation.");
        Check(slices.Run(10, () => false, _ => throw new Exception("Paused work executed")) == 0 && slices.Count == 1,
            "A paused or spent global allowance neither executes nor loses pending work.");
        Check(slices.Run(3, () => true, _ => true) == 3 && slices.Count == 1,
            "Even tiny continuations cannot exceed the frame slice count limit.");
        slices.Remove("map1/long"); slices.Remove("map1/long");
        Check(slices.Count == 0 && slices.Run(3, () => true, _ => true) == 0,
            "Removal is idempotent and an empty scheduler performs no work.");
        var leases = new TacticalOpeningLeases<int>();
        bool Nearby(int a, int b) => Math.Abs(a - b) <= 12;
        Check(leases.Acquire("front", 10, Nearby) && !leases.Acquire("tail", 10, Nearby),
            "Teams cannot simultaneously claim the same opening workspace");
        Check(!leases.Acquire("third", 15, Nearby) && leases.Acquire("independent", 90, Nearby),
            "Overlapping formations queue while independent entrances proceed");
        leases.Release("front");
        Check(leases.Acquire("tail", 10, Nearby) && !leases.Acquire("third", 15, Nearby),
            "A released workspace goes to the oldest waiting team");
        leases.Prune(owner => owner != "tail");
        Check(leases.Acquire("third", 15, Nearby), "A destroyed unit does not retain an opening lease");
        Check(leases.Acquire("third", 150, Nearby) && leases.Acquire("new", 15, Nearby),
            "Replacing an entrance frees the old workspace instead of retaining a stale lease");
        var cpu = new TacticalCpuSamples(4);
        for (int sample = 1; sample <= 8; sample++) cpu.Add(sample);
        Check(cpu.Percentiles().SequenceEqual(new[] { 6.0, 8.0, 8.0 }) && cpu.TotalSamples == 8
            && cpu.TotalMilliseconds == 36 && cpu.Maximum == 8,
            "CPU percentiles use bounded recent samples while counts and maximum cover the full profile");
        var index = new TacticalSpatialIndex<int>();
        var random = new Random(7231);
        var positions = Enumerable.Range(0, 400).Select(id => new { Id = id, X = random.Next(600), Z = random.Next(600) }).ToArray();
        foreach (var position in positions) index.Add(position.Id, position.X, position.Z);
        foreach (var source in positions.Take(50))
        {
            var candidates = index.Query(source.X, source.Z, 90).ToHashSet();
            Check(positions.Where(value => (value.X - source.X) * (value.X - source.X)
                + (value.Z - source.Z) * (value.Z - source.Z) <= 8100).All(value => candidates.Contains(value.Id)),
                "Spatial broad phase cannot lose an in-range pawn at map edges or chunk boundaries");
        }
        index.Clear(); Check(!index.Query(0, 0, 600).Any(), "Spatial rebuild discards departed pawn entries");
        var budget = new TacticalServiceBudget(96, 10, 32);
        Check(budget.Grant("A", 0, 96) == 96 && budget.Grant("B", 0, 96) == 0,
            "A burst cannot exceed the shared observation window");
        Check(budget.Grant("C", 10, 96) == 0 && budget.Grant("B", 10, 96) == 96,
            "An older pending unit is served before newly arriving observations");
        Check(budget.Grant("C", 20, 96) == 96, "Repeated contention does not starve the queued unit");
        budget.Grant("D", 20, 96);
        Check(budget.Grant("E", 90, 96) == 96, "A departed queued unit cannot indefinitely block observation");
    }
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
        Check(!TacticalNodeProgress.AllowsStep(-1, 0, false, false, room => room == 1 || room == 2)
            && TacticalNodeProgress.AllowsStep(-1, 0, false, true, room => room == 1 || room == 2),
            "A connector source uses exact portal permission instead of granting escape to unrelated room-zero tiles");
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
    private static void CheckMaskKeys()
    {
        var input = Open(8, 7);
        for (int i = 0; i < input.Cells.Length; i++)
        {
            input.Cells[i].Room = i % 4;
            if (i % 9 == 0) input.Cells[i].Flags |= TacticalRawFlags.Door | TacticalRawFlags.WallLine;
        }
        var geometry = TacticalGeometry.Calculate(input, CancellationToken.None);
        var random = new Random(73);
        for (int iteration = 0; iteration < 512; iteration++)
        {
            bool Flag() => random.Next(2) == 1;
            var source = new TacticalMovementMaskInput {
                Width = 8, Height = 7, Structure = iteration % 5 == 0 ? null : geometry,
                Reactive = Flag(), ExteriorOnly = Flag(), InitialRoom = random.Next(4), ExcludedRoom = random.Next(-1, 4),
                SelectedOpeningOnly = Flag(), BreachIndex = random.Next(-1, 56),
                RestrictRooms = Flag(), AllowedRooms = new[] { 3, 1, 1 },
                RestrictPortals = Flag(), AllowedPortals = new[] { 9, 0, 9, 18 },
                RestrictCells = Flag(), AllowedCells = Enumerable.Range(0, 30).Reverse().ToArray(),
                Fight = Flag(), FightX = random.Next(8), FightZ = random.Next(7), FightRoom = random.Next(4),
                FightRadius = random.Next(1, 10), LeashX = 4, LeashZ = 3, LeashRadius = random.Next(-1, 8)
            };
            var key = new TacticalMovementMaskKey(source);
            Check(TacticalMovementMask.Calculate(source, CancellationToken.None)
                    .SequenceEqual(TacticalMovementMask.Calculate(key.Input, CancellationToken.None)),
                "Effective-key normalization preserves complete movement permissions across combined policy modes");
        }
        var zeroA = new TacticalMovementMaskKey(new TacticalMovementMaskInput {
            Width = 8, Height = 7, Structure = geometry, InitialRoom = 99, BreachIndex = 15 });
        var zeroB = new TacticalMovementMaskKey(new TacticalMovementMaskInput {
            Width = 8, Height = 7, InitialRoom = 2, BreachIndex = 25 });
        Check(zeroA.Equals(zeroB) && zeroA.GetHashCode() == zeroB.GetHashCode(),
            "Unrestricted movement shares one zero-cost grid regardless of irrelevant room, breach or plan version");
        int[] originalRooms = { 3, 1, 1 };
        var frozen = new TacticalMovementMaskKey(new TacticalMovementMaskInput {
            Width = 8, Height = 7, Structure = geometry, RestrictRooms = true, AllowedRooms = originalRooms });
        var equivalent = new TacticalMovementMaskKey(new TacticalMovementMaskInput {
            Width = 8, Height = 7, Structure = geometry, RestrictRooms = true, AllowedRooms = new[] { 1, 3 } });
        Check(frozen.Equals(equivalent) && frozen.GetHashCode() == equivalent.GetHashCode(),
            "Identical room permissions coalesce across list order, duplicates and owner identity");
        originalRooms[0] = 2;
        Check(frozen.Equals(equivalent) && frozen.Input.AllowedRooms.SequenceEqual(new[] { 1, 3 }),
            "Editing the source arrays cannot change a published cache key or worker input");
        var different = new TacticalMovementMaskKey(new TacticalMovementMaskInput {
            Width = 8, Height = 7, Structure = geometry, RestrictRooms = true, AllowedRooms = new[] { 1, 2 } });
        Check(!frozen.Equals(different), "Different known permissions remain isolated even for the same static map");
    }

    private static void CheckPreparationQueue()
    {
        var cadence = new TacticalPreparationQueue<object, object>();
        object cadenceFirst = new object(), cadenceSecond = new object(), cadenceWaiter = new object();
        Check(!cadence.TryBeginService(20), "An empty preparation queue never begins work");
        cadence.Add(cadenceFirst, 20); cadence.WaitFor(cadenceWaiter, cadenceFirst);
        Check(cadence.TryBeginService(20) && !cadence.TryBeginService(20), "Tick and update share one preparation pass per frame");
        cadence.Add(cadenceFirst, 20);
        Check(!cadence.TryBeginService(20), "A duplicate request cannot reopen the same-frame budget");
        cadence.Add(cadenceSecond, 20);
        Check(cadence.TryBeginService(20) && !cadence.TryBeginService(20), "New work may start immediately even later in the same frame");
        Check(cadence.TryBeginService(21), "An incomplete worker is reviewed again next frame");
        Check(cadence.Complete(cadenceFirst).SequenceEqual(new[] { cadenceWaiter }), "Frame gating preserves preparation completion notifications");
        cadence.Complete(cadenceSecond);
        Check(!cadence.TryBeginService(22), "A drained queue returns to zero service passes");
        cadence.Clear(); cadence.Add(cadenceFirst, 21);
        Check(cadence.TryBeginService(21), "Clear resets frame state for a new raid");
        var lease = new TacticalNativeLease<object>();
        var memory = new TacticalNativeBudget(100);
        Check(memory.Fits(100), "A native allocation fitting exactly within budget is admitted");
        memory.Allocated(80);
        Check(memory.Fits(20) && !memory.Fits(21), "Native pressure defers normal publication instead of disposing live readers");
        memory.Allocated(30);
        Check(memory.Bytes == 110 && memory.Peak == 110 && !memory.Fits(0),
            "Emergency publication may temporarily exceed the target and is reflected in memory accounting");
        memory.Freed(80);
        Check(memory.Bytes == 30 && memory.Fits(70) && memory.Peak == 110,
            "Safe retirement restores capacity while preserving the measured peak");
        object request = new object();
        Check(lease.CanRetire, "An unused native cache entry may be retired");
        lease.Acquire(request); lease.Acquire(request);
        Check(!lease.CanRetire && lease.Requests == 1, "Created/queued requests retain their grid, with duplicate acquisition coalesced");
        Check(!TacticalNativeBudget.Retirable(true, lease.CanRetire, 0, 100), "Memory pressure cannot retire a queued request's grid");
        lease.BeginRead(); lease.Release(request);
        Check(!lease.CanRetire && lease.Requests == 0, "Cancelling a running request does not permit disposal while its grid job still reads");
        Check(!TacticalNativeBudget.Retirable(true, lease.CanRetire, 0, 100), "Memory pressure cannot retire a cancelled request's running grid");
        lease.CompleteReads();
        Check(lease.CanRetire, "The native completion barrier permits retirement after all requests release");
        Check(TacticalNativeBudget.Retirable(true, lease.CanRetire, 0, 100)
            && !TacticalNativeBudget.Retirable(true, lease.CanRetire, 99, 100)
            && !TacticalNativeBudget.Retirable(false, lease.CanRetire, 0, 100),
            "Retirement needs a completed, unleased, idle grid and preserves just-requested or unpublished entries");
        lease.Acquire(request); lease.BeginRead(); lease.CompleteReads();
        Check(!lease.CanRetire, "A completion barrier cannot retire a grid still retained by another pending request");
        lease.Release(request);
        Check(lease.CanRetire, "Final request resolution releases the completed grid");
        var queue = new TacticalPreparationQueue<object, object>();
        object old = new object(), speculative = new object(), later = new object();
        object a = new object(), b = new object(), c = new object();
        queue.Add(speculative, 0); queue.Add(old, 10); queue.Add(later, 20);
        queue.WaitFor(a, old); queue.WaitFor(b, later);
        Check(queue.HasWaiters(old) && !queue.HasWaiters(speculative),
            "Only obsolete speculative tasks may be cancelled; an old actual mover remains protected");
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
        CheckPassageAdmissions();
        CheckMaskKeys();
        try
        {
            CheckQueuePositions();
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
                object[] args = { blocked, CancellationToken.None, null, false };
                Check((bool)start.Invoke(null, args), "The shared scheduler accepts an idle slot");
                var blockedTask = (System.Threading.Tasks.Task<int>)args[2];
                Check(entered.Wait(TimeSpan.FromSeconds(10)), "The controlled worker starts off the caller thread");
                try
                {
                    Check(!TacticalGeometryWorker.TryStart(divided, CancellationToken.None, out _),
                        "Static geometry cannot occupy the movement worker's reserved capacity");
                    object[] movementArgs = { blocked, CancellationToken.None, null, true };
                    Check((bool)start.Invoke(null, movementArgs), "A movement calculation can run alongside static geometry");
                    var movementTask = (System.Threading.Tasks.Task<int>)movementArgs[2];
                    Check(!TacticalGeometryWorker.TryStart(maskInput, CancellationToken.None, out _),
                        "The shared pool cannot enqueue an unbounded third calculation");
                    release.Set();
                    Check(movementTask.Wait(TimeSpan.FromSeconds(10)), "Both bounded workers finish after release");
                    TacticalGeometryWorker.Release(movementTask);
                }
                finally { release.Set(); }
                Check(blockedTask.Wait(TimeSpan.FromSeconds(10)) && blockedTask.Result != callerThread,
                    "Default task scheduling runs the pure calculation on a different thread");
                TacticalGeometryWorker.Release(blockedTask);
            }
            CheckMovementNodes();
            CheckObservationServices();
            var reactionCadence = new TacticalReactionCadence();
            Check(reactionCadence.Due(0, 0, 0, false), "Initial reaction review is due.");
            reactionCadence.Record(0, 0, 0, false);
            Check(!reactionCadence.Due(10, 0, 0, false) && !reactionCadence.Due(20, 0, 0, false)
                && reactionCadence.Due(30, 0, 0, false), "Quiet reaction reviews have a bounded 30-tick interval.");
            Check(reactionCadence.Due(10, 1, 0, false), "A received or observed contact bypasses quiet cadence.");
            Check(reactionCadence.Due(10, 0, 1, false), "A phase transition triggers a fresh tactical reaction.");
            Check(reactionCadence.Due(10, 0, 0, true), "Active smoke or contact guards retain rapid reaction checks.");
            reactionCadence.Record(10, 1, 1, true);
            Check(!reactionCadence.Due(10, 1, 1, true) && reactionCadence.Due(20, 1, 1, false),
                "Same-tick evaluation is coalesced but an earlier active result is revalidated.");
            reactionCadence.Invalidate();
            Check(reactionCadence.Due(10, 1, 1, false), "Urgent casualties bypass a quiet deadline.");
            Console.WriteLine($"PASS: {checks} tactical geometry, movement node, immutable input, codec and worker checks.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void CheckQueuePositions()
    {
        var queue = new TacticalQueuePositions<int, int>();
        Check(queue.Assign(1, 10) && queue.Assign(2, 20), "Waiting destinations have individual stable owners.");
        Check(!queue.Assign(3, 10) && queue.Available(1, 10) && !queue.Available(2, 10),
            "Neither a sibling nor another squad can claim an occupied waiting destination.");
        Check(!queue.Assign(1, 20) && queue.TryGet(1, out int retained) && retained == 10,
            "A failed retarget keeps the existing claim until explicitly released.");
        Check(queue.Assign(1, 30) && queue.Available(3, 10) && !queue.Available(3, 30),
            "Changing a waiting destination releases its old reverse-index entry.");
        queue.Release(3);
        Check(!queue.Available(3, 30), "A non-owner cannot release another pawn's waiting tile.");
        queue.Prune(actor => actor == 2);
        Check(queue.Count == 1 && queue.Available(3, 30) && !queue.TryGet(1, out _),
            "Casualty/removal cleanup drops both directions without releasing surviving actors.");
        queue.Release(2); queue.Release(2);
        Check(queue.Count == 0 && queue.Available(1, 20), "Releasing admission or cancelling a queue is idempotent.");
    }
}
