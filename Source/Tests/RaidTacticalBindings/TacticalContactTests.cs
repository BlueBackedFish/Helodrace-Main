using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Helodrace.Tactics;
using Verse;
using Verse.AI;

internal static class TacticalContactTests
{
    internal static void Run()
    {
        var memory = new TacticalContactMemory();
        var seen = new IntVec3(3, 0, 5); var area = seen + IntVec3.North;
        memory.Remember(1, seen, area, true, true, 100);
        memory.Expire(200);
        TacticalContact first = memory.Entries.Single();
        if (first.Position != seen || first.Area != area || !first.Door || first.SeenTick != 100)
            throw new Exception("Unobserved time must not refresh a contact or its position.");
        if (!TacticalContactMemory.Fresh(first, 339) || TacticalContactMemory.Fresh(first, 340))
            throw new Exception("Contact freshness boundary failed.");
        memory.Remember(1, seen + IntVec3.East, area, false, true, 500);
        if (memory.Entries.Count != 1 || first.Position != seen + IntVec3.East || first.Door || first.SeenTick != 500)
            throw new Exception("A new actual sighting must update one identity.");
        for (int id = 2; id <= 9; id++) memory.Remember(id, seen, area, false, true, 500 + id);
        if (memory.Entries.Count != 8 || memory.Entries.Any(c => c.EnemyId == 1))
            throw new Exception("Contact storage must evict the oldest entry at its fixed cap.");
        memory.Expire(2308);
        if (memory.Entries.Count != 1 || memory.Entries[0].EnemyId != 9)
            throw new Exception("Independent retention expiry failed.");
        memory.Expire(2309);
        if (memory.Entries.Count != 0) throw new Exception("Expired contacts remain.");
        if (!TacticalContactMemory.Rear(IntVec3.Zero, IntVec3.North, IntVec3.South)
            || TacticalContactMemory.Rear(IntVec3.Zero, IntVec3.North, IntVec3.East)
            || !TacticalContactMemory.Opposed(IntVec3.Zero, IntVec3.North, IntVec3.South)
            || TacticalContactMemory.Opposed(IntVec3.Zero, IntVec3.North, IntVec3.East))
            throw new Exception("Rear/corridor direction classification failed.");
        var a = new TacticalSquadCommand(); var b = new TacticalSquadCommand();
        a.Contacts.Memory.Remember(1, seen, area, true, true, 0); a.SecuredCells.Add(seen);
        if (b.Contacts.Memory.Entries.Count != 0) throw new Exception("Unconnected squads must not share knowledge.");
        a.Contacts.Memory.Expire(1800);
        if (!a.SecuredCells.Contains(seen)) throw new Exception("Danger expiry must not erase physical room history.");
        bool Start(bool fresh, bool gathering, bool arrival = false, bool close = false,
            bool rear = false, bool opposed = false, bool door = false) => TacticalContactResponsePolicy.ShouldStart(
                fresh, gathering, false, rear, opposed, door, arrival, close);
        if (!Start(true, TacticalContactResponsePolicy.Gathering(TacticalCommandPhase.Stack, false))
            || !Start(true, TacticalContactResponsePolicy.Gathering(TacticalCommandPhase.Stack, true))
            || !Start(true, TacticalContactResponsePolicy.Gathering(TacticalCommandPhase.Breach, false))
            || !Start(true, TacticalContactResponsePolicy.Gathering(TacticalCommandPhase.Observe, false)))
            throw new Exception("A fresh frontal threat beyond six cells must interrupt stack travel/holding, including gathering at an existing gap.");
        if (Start(false, true) || TacticalContactResponsePolicy.Gathering(TacticalCommandPhase.Observe, true)
            || TacticalContactResponsePolicy.Gathering(TacticalCommandPhase.BlastWait, false)
            || Start(true, false, arrival: true, close: true))
            throw new Exception("Stale threats or completed observation must not spuriously replace opening/arrival combat.");
        if (!Start(true, false, close: true) || !Start(true, false, arrival: true, rear: true)
            || !Start(true, false, arrival: true, opposed: true) || !Start(true, false, arrival: true, door: true))
            throw new Exception("Existing close, rear, opposed and other-door emergency responses must remain available.");
        float hidden = TacticalContactResponsePolicy.PostScore(false, 1f, 0, false);
        if (TacticalContactResponsePolicy.PostScore(true, .2f, 4, true) <= hidden
            || TacticalContactResponsePolicy.PostScore(true, .6f, 1, false)
                <= TacticalContactResponsePolicy.PostScore(true, .2f, 1, false))
            throw new Exception("A connected local firing post must beat passive hiding; equivalent firing posts still prefer cover.");
        var owner = (MapComponent_TacticalCommands)RuntimeHelpers.GetUninitializedObject(typeof(MapComponent_TacticalCommands));
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        var postJob = (Job)RuntimeHelpers.GetUninitializedObject(typeof(Job));
        // Synthetic pawns have no native race/equality state. This seam checks
        // command admission, without constructing a Unity pawn or pathfinder.
        var byPawn = new Dictionary<Pawn, TacticalSquadCommand>(ReferenceEqualityComparer.Instance) { [pawn] = a };
        typeof(MapComponent_TacticalCommands).GetField("byPawn", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, byPawn);
        MethodInfo pause = typeof(MapComponent_TacticalCommands).GetMethod("StackMovementPaused", BindingFlags.Instance | BindingFlags.NonPublic);
        bool Paused() => (bool)pause.Invoke(owner, new object[] { pawn, postJob });
        a.ContactResponse = new TacticalContactResponse();
        if (!Paused()) throw new Exception("An owned post awaiting path budget must not restart gathering during contact response.");
        a.ChargeAction = new TacticalChargeAction();
        if (Paused()) throw new Exception("A queued contact must not block C4 safety/withdrawal movement.");
        a.ChargeAction = null; a.OpeningAction = new TacticalOpeningAction { Launched = true };
        if (Paused()) throw new Exception("A queued contact must not block movement before grenade effects clear.");
        a.OpeningAction.EffectsCleared = true;
        if (!Paused()) throw new Exception("After explosive safety completes, the queued contact pause must be available again.");
        a.OpeningAction = null;
        postJob.playerForced = true;
        if (Paused()) throw new Exception("Contact must not block a player-forced movement job.");
        postJob.playerForced = false; a.ContactResponse = null;
        if (Paused()) throw new Exception("Ending contact must permit the preserved gathering plan to move again.");
        byPawn.Clear();
        if (Paused()) throw new Exception("An unrelated pawn must not inherit another squad's contact pause.");
        Console.WriteLine("PASS: contact movement admission pause/resume, explosive withdrawal, player control and unrelated pawns.");
        Console.WriteLine("PASS: stack travel/holding frontal threats, existing-gap gathering, stale/arrival exclusions, emergency response and firing-post preference.");
        Console.WriteLine("PASS: R4 bounded value-only memory, fresh/retention boundaries, door area, rear/opposed threats, isolated squad knowledge and secured history.");
    }
}
