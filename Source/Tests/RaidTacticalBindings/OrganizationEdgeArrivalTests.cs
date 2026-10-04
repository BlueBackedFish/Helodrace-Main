using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Squads;
using Verse;

internal static class OrganizationEdgeArrivalTests
{
    internal static void Run()
    {
        int checks = 0, nextId = 70000;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var owners = new Dictionary<Pawn, CombatGroup>();
        var pawnDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        CombatGroup Group(string id, string level, int count, params CombatGroup[] children)
        {
            var def = (FormationDef)RuntimeHelpers.GetUninitializedObject(typeof(FormationDef)); def.unitLevel = level;
            var group = new CombatGroup { id = id, formation = def, children = children.ToList() };
            for (int i = 0; i < count; i++)
            {
                var pawn = new Pawn { thingIDNumber = ++nextId, def = pawnDef };
                group.roleAssignments.Add(new RoleAssignment { pawn = pawn }); owners[pawn] = group;
            }
            return group;
        }
        var squadA = Group("A", "Squad", 6); var squadB = Group("B", "Squad", 6);
        var hq = Group("HQ", "Platoon", 2, squadA, squadB);
        var low = new CombatOrganization { id = "Low", rootGroups = new List<CombatGroup> { hq } }; low.RestoreTreeLinks();
        var teams = new[] { Group("Alpha", "Team", 4), Group("Bravo", "Team", 4), Group("Charlie", "Team", 4) };
        var highSquad = Group("A", "Squad", 1, teams);
        var high = new CombatOrganization { id = "High", rootGroups = new List<CombatGroup> { highSquad } }; high.RestoreTreeLinks();
        var split = AccessTools.Method(typeof(Patch_EdgeWalkInGroups_Organization).Assembly
            .GetType("Helodrace.Squads.OrganizationEdgeArrival"), "Split");
        List<List<Pawn>> Split(List<Pawn> pawns, out List<Pawn> unorganized)
        {
            Func<Pawn, string> resolve = pawn => owners.TryGetValue(pawn, out CombatGroup group) ? RaidTacticalUnit.ForGroup(group).Id : null;
            object[] args = { pawns, resolve, null };
            var groups = (List<List<Pawn>>)split.Invoke(null, args); unorganized = (List<Pawn>)args[2]; return groups;
        }
        var lowPawns = low.AllMembers.ToList();
        var groups = Split(lowPawns, out var guests);
        Check(groups.Count == 3 && guests.Count == 0, "LOW headquarters and two squads enter as three intact groups.");
        Check(groups.Any(group => group.SequenceEqual(squadA.Members))
            && groups.Any(group => group.SequenceEqual(squadB.Members)), "Sibling squads never mix their personnel.");
        Check(groups.SelectMany(group => group).Distinct().Count() == lowPawns.Count,
            "Every generated pawn is assigned once without roster loss.");
        var highGroups = Split(high.AllMembers.ToList(), out guests);
        Check(highGroups.Count == 1 && highGroups.Single().Count == 13,
            "A High squad remains a 13-person arrival group including all three child fireteams.");
        Check(Split(squadA.Members.Reverse().ToList(), out guests).Single().SequenceEqual(squadA.Members.Reverse()),
            "Input order changes cannot split a squad or shuffle its membership.");
        var extra = new Pawn { thingIDNumber = ++nextId, def = pawnDef };
        var mixed = lowPawns.Concat(high.AllMembers).Append(extra).ToList();
        groups = Split(mixed, out guests);
        Check(groups.Count == 4 && guests.SequenceEqual(new[] { extra }),
            "Mixed raids preserve an unorganized roster for vanilla fallback.");
        Check(groups.Count(group => group.Contains(squadA.Members.First()) || group.Contains(highSquad.Members.First())) == 2,
            "Equal group names in different organizations never merge arrival groups.");
        Check(Split(new List<Pawn> { extra }, out guests).Count == 0 && guests.Count == 1,
            "Completely unorganized raids retain the unmodified vanilla arrival worker.");
        Check(Split(new List<Pawn>(), out guests).Count == 0 && guests.Count == 0,
            "An empty arrival does not create a group or invoke spawning.");
        Check(mixed.Count == lowPawns.Count + 14, "Partitioning leaves the caller's complete raid roster unchanged.");
        Console.WriteLine($"PASS: {checks} organization edge arrival, squad/fireteam integrity and vanilla fallback checks.");
    }
}
