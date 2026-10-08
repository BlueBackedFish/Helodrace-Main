using System;
using System.Linq;
using Helodrace.Tactics;
using Verse;

internal static class TacticalContactTests
{
    internal static void Run()
    {
        var memory = new TacticalContactMemory();
        var seen = new IntVec3(3, 0, 5); var area = seen + IntVec3.North;
        memory.Remember(1, seen, area, true, 100);
        memory.Expire(200);
        TacticalContact first = memory.Entries.Single();
        if (first.Position != seen || first.Area != area || !first.Door || first.SeenTick != 100)
            throw new Exception("Unobserved time must not refresh a contact or its position.");
        if (!TacticalContactMemory.Fresh(first, 339) || TacticalContactMemory.Fresh(first, 340))
            throw new Exception("Contact freshness boundary failed.");
        memory.Remember(1, seen + IntVec3.East, area, false, 500);
        if (memory.Entries.Count != 1 || first.Position != seen + IntVec3.East || first.Door || first.SeenTick != 500)
            throw new Exception("A new actual sighting must update one identity.");
        for (int id = 2; id <= 9; id++) memory.Remember(id, seen, area, false, 500 + id);
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
        a.Contacts.Memory.Remember(1, seen, area, true, 0); a.SecuredCells.Add(seen);
        if (b.Contacts.Memory.Entries.Count != 0) throw new Exception("Unconnected squads must not share knowledge.");
        a.Contacts.Memory.Expire(1800);
        if (!a.SecuredCells.Contains(seen)) throw new Exception("Danger expiry must not erase physical room history.");
        Console.WriteLine("PASS: R4 bounded value-only memory, fresh/retention boundaries, door area, rear/opposed threats, isolated squad knowledge and secured history.");
    }
}
