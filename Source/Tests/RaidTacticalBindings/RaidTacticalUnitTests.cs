using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using Helodrace;
using Helodrace.Squads;
using RimWorld;
using Verse;
using Verse.AI.Group;

internal static class RaidTacticalUnitTests
{
    internal static string BindPawn(Game game, Pawn pawn, string groupId)
    {
        if (pawn.def == null)
        {
            pawn.def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            pawn.def.defName = "UnitTestPawn";
        }
        var group = Group(groupId, "Squad", 0);
        group.roleAssignments.Add(new RoleAssignment { pawn = pawn });
        var organization = new CombatOrganization { id = "Tests", rootGroups = new List<CombatGroup> { group } };
        organization.RestoreTreeLinks();
        var registry = new GameComponent_CombatOrganizations(game);
        game.components = new List<GameComponent> { registry };
        AccessTools.Field(typeof(GameComponent_CombatOrganizations), "organizations").SetValue(registry,
            new List<CombatOrganization> { organization });
        ((Dictionary<Pawn, CombatGroup>)AccessTools.Field(typeof(GameComponent_CombatOrganizations), "byPawn")
            .GetValue(registry))[pawn] = group;
        return RaidTacticalUnit.ForGroup(group).Id;
    }

    private static int checks;
    private static int nextPawnId = 20000;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static CombatGroup Group(string id, string level, int personnel, params CombatGroup[] children)
    {
        var definition = (FormationDef)RuntimeHelpers.GetUninitializedObject(typeof(FormationDef));
        definition.unitLevel = level;
        definition.requiredRoles = new List<FormationRoleSlot> { new FormationRoleSlot { count = personnel } };
        definition.optionalRoles = new List<FormationRoleSlot>();
        definition.childFormations = children.Select(child => new ChildFormationSlot { formation = child.formation }).ToList();
        var pawnDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        pawnDef.defName = "UnitTestPawn";
        var group = new CombatGroup { id = id, name = id, formation = definition, children = children.ToList() };
        for (int i = 0; i < personnel; i++)
            group.roleAssignments.Add(new RoleAssignment { pawn = new Pawn { def = pawnDef, thingIDNumber = nextPawnId++ } });
        return group;
    }

    public static void Run()
    {
        var squadA = Group("A", "Squad", 4);
        var squadB = Group("B", "Squad", 4);
        var mg = Group("MG", "Team", 2);
        var hq = Group("HQ", "Platoon", 2, squadA, squadB, mg);
        var organization = new CombatOrganization { id = "Low", rootGroups = new List<CombatGroup> { hq } };
        organization.RestoreTreeLinks();
        var units = RaidTacticalUnit.ForOrganization(organization).ToList();
        Check(units.Count == 4, "Headquarters, two squads and the independent machine gun team need separate owners.");
        Check(units.SelectMany(unit => unit.Members).Count() == organization.AllMembers.Count(),
            "Every pawn must belong to exactly one execution unit, including headquarters personnel.");
        Check(units.SelectMany(unit => unit.Members).Distinct().Count() == organization.AllMembers.Count(),
            "Unit membership must cover the complete roster.");
        Check(units[0].Members.Count() == 2 && units[0].Groups.Count() == 1 && !units[0].IncludesChildren,
            "Platoon headquarters must not own its child squads' directives or observations.");
        Check(units[1].StandardPersonnel == 4 && units[0].StandardPersonnel == 2,
            "Casualty calculations must use unit strength, not the whole platoon.");
        Pawn departed = squadA.Members.First();
        string stableId = units[1].Id;
        organization.RemoveMember(departed);
        Check(RaidTacticalUnit.ForOrganization(organization).Single(unit => unit.Group == squadA).Id == stableId
            && units[1].StandardPersonnel == 4, "Casualties must not change unit identity or shrink its original strength.");
        Check(RaidTacticalUnit.ForGroup(squadB).Id != stableId, "Sibling squads need distinct state keys.");

        var alpha = Group("Alpha", "Team", 4);
        var bravo = Group("Bravo", "Team", 4);
        var charlie = Group("Charlie", "Team", 4);
        var highSquad = Group("HighSquad", "Squad", 1, alpha, bravo, charlie);
        var high = new CombatOrganization { id = "High", rootGroups = new List<CombatGroup> { highSquad } };
        high.RestoreTreeLinks();
        var highUnit = RaidTacticalUnit.ForOrganization(high).Single();
        Check(highUnit.Members.Count() == 13 && highUnit.Groups.Count() == 4,
            "High's three Team-level fireteams must remain under their squad's single plan.");
        Check(RaidTacticalUnit.ForGroup(alpha).Id == highUnit.Id && RaidTacticalUnit.ForGroup(bravo).Id == highUnit.Id
            && RaidTacticalUnit.ForGroup(charlie).Id == highUnit.Id,
            "Fireteam ancestry, not its name, selects the command owner.");
        Check(alpha.Parent == highSquad && bravo.parentGroupId == highSquad.id && charlie.Parent == highSquad
            && highUnit.StandardPersonnel == 13,
            "Subdivision affiliation and full squad personnel must remain intact.");
        Check(ReferenceEquals(RaidTacticalUnit.ForGroup(alpha), RaidTacticalUnit.ForGroup(bravo))
            && ReferenceEquals(RaidTacticalUnit.ForGroup(highSquad), highUnit),
            "Group and pawn lookups reuse the canonical squad view instead of allocating new wrappers and IDs.");
        high.RestoreTreeLinks();
        var restoredHigh = RaidTacticalUnit.ForGroup(alpha);
        Check(!ReferenceEquals(restoredHigh, highUnit) && restoredHigh.Id == highUnit.Id
            && ReferenceEquals(restoredHigh, RaidTacticalUnit.ForGroup(bravo)),
            "Restoring the authoritative organization tree invalidates cached ownership consistently.");
        organization.SetBudget(new FormationPlan { initialRaidPoints = 100, formationPointsSpent = 40 });
        Check(ReferenceEquals(units[1].Organization, units[2].Organization)
            && units[1].Organization.TrySpendSupportPoints(40)
            && !units[2].Organization.TrySpendSupportPoints(30)
            && units[2].Organization.GetSupportPoints() == 20,
            "Splitting execution must never duplicate or independently refund the real organization's support budget.");
        Console.WriteLine($"PASS: {checks} real organization partition, canonical squad views, shared budgets and High affiliation checks");
    }
}
