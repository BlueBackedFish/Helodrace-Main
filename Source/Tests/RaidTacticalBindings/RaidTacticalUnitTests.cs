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
        organization.SetBudget(new FormationPlan { initialRaidPoints = 100, formationPointsSpent = 40 });
        Check(ReferenceEquals(units[1].Organization, units[2].Organization)
            && units[1].Organization.TrySpendSupportPoints(40)
            && !units[2].Organization.TrySpendSupportPoints(30)
            && units[2].Organization.GetSupportPoints() == 20,
            "Splitting execution must never duplicate or independently refund the real organization's support budget.");
        CheckRuntimeOwnership(organization, units[1], units[2], highUnit, alpha);
        Console.WriteLine($"PASS: {checks} tactical unit partition, memory, casualty, command, persistence and High affiliation checks");
    }

    private static void CheckRuntimeOwnership(CombatOrganization organization, RaidTacticalUnit first,
        RaidTacticalUnit second, RaidTacticalUnit highUnit, CombatGroup alpha)
    {
        Game previousGame = Current.Game;
        LoadSaveMode previousMode = Scribe.mode;
        XmlNode previousXml = Scribe.loader.curXmlParent;
        IExposable previousParent = Scribe.loader.curParent;
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        game.components = new List<GameComponent>();
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
        var registry = new GameComponent_CombatOrganizations(game);
        game.components.Add(registry);
        AccessTools.Field(typeof(GameComponent_CombatOrganizations), "organizations").SetValue(registry,
            new List<CombatOrganization> { organization, highUnit.Organization });
        var byPawn = (Dictionary<Pawn, CombatGroup>)AccessTools.Field(typeof(GameComponent_CombatOrganizations), "byPawn").GetValue(registry);
        foreach (CombatGroup group in organization.AllGroups.Concat(highUnit.Groups))
            foreach (Pawn pawn in group.Members)
            {
                byPawn[pawn] = group;
                AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)0);
            }
        var execution = new MapComponent_RaidTacticalExecution(map);
        var plans = new MapComponent_RaidTacticalPlans(map);
        AccessTools.Field(typeof(Map), "components").SetValue(map, new List<MapComponent> { execution, plans });
        Current.Game = game;
        try
        {
            var a = new MapComponent_RaidTacticalExecution.ExecutionState { OrganizationId = organization.id,
                UnitId = first.Id, GroupId = first.GroupId, Phase = RaidExecutionPhase.Breach };
            var b = new MapComponent_RaidTacticalExecution.ExecutionState { OrganizationId = organization.id,
                UnitId = second.Id, GroupId = second.GroupId, Phase = RaidExecutionPhase.Assemble };
            var cell = new IntVec3(5, 0, 6);
            a.Contacts.Observe(7, "Enemy", cell, 12, 2, 100, IntVec3.Invalid, true, 25);
            a.ClearedRoomCells.Add(cell);
            a.RoomSecurity.Observe(12, cell, 100);
            a.DoorStateSignature = "11:1";
            a.ActivePlan = new RaidTacticalPlan { OrganizationId = organization.id, UnitId = first.Id, GroupId = first.GroupId };
            b.ActivePlan = new RaidTacticalPlan { OrganizationId = organization.id, UnitId = second.Id, GroupId = second.GroupId };
            AccessTools.Field(typeof(MapComponent_RaidTacticalExecution), "savedStates").SetValue(execution,
                new List<MapComponent_RaidTacticalExecution.ExecutionState> { a, b });
            Scribe.mode = LoadSaveMode.PostLoadInit;
            // Exercise the actual state dictionary reconstruction, not a mock keyed by unit ID.
            execution.ExposeData();
            Scribe.mode = previousMode;
            object State(string id) => AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "StateFor").Invoke(execution, new object[] { id });
            Check(State(first.Id) == a && State(second.Id) == b && State(organization.id) == null,
                "Loading two units of one organization must not overwrite either state or recreate an organization-wide alias.");
            Check(b.Contacts.Entries.Count == 0 && b.ClearedRoomCells.Count == 0 && b.RoomSecurity.For(12) == null
                && b.DoorStateSignature == null && b.Phase == RaidExecutionPhase.Assemble,
                "Observed enemies, cleared rooms, door knowledge and execution phase must remain local to the observing squad.");
            Check(RaidTacticalUnit.ForPawn(first.Members.First()).Id == first.Id
                && RaidTacticalUnit.ForPawn(alpha.Members.First()).Id == highUnit.Id,
                "Pawn lookup must resolve the command unit while retaining the original Team membership.");
            var pending = (Dictionary<string, HashSet<Pawn>>)AccessTools.Field(typeof(MapComponent_RaidTacticalExecution), "pendingCasualties").GetValue(execution);
            Pawn casualty = first.Members.First();
            var component = new PawnOrganizationComponent { parent = casualty,
                organizationId = organization.id, groupId = first.GroupId };
            component.Notify_Killed(map);
            component.Notify_Killed(map);
            Check(pending.Count == 1 && pending[first.Id].Count == 1 && !pending.ContainsKey(second.Id),
                "Repeated death notifications must coalesce for the affected unit without scheduling its sibling.");
            var decisionPlans = (Dictionary<string, RaidTacticalPlan>)AccessTools.Field(typeof(MapComponent_RaidTacticalPlans), "plans").GetValue(plans);
            decisionPlans[first.Id] = a.ActivePlan;
            decisionPlans[second.Id] = b.ActivePlan;
            plans.InvalidateDecision(first.Id);
            Check(!decisionPlans.ContainsKey(first.Id) && decisionPlans[second.Id] == b.ActivePlan,
                "Invalidating one unit's decision must not erase the sibling's cached plan.");
            Check(typeof(RaidPawnOrder).GetField("UnitId") != null && typeof(RaidPawnOrder).GetField("GroupId") != null,
                "Durable pawn directives need both a command owner and original subgroup affiliation.");
            CheckCommandOwnership(game, map, execution, first, second, a, b);
            CheckPlanningBudget(game, plans);
            CheckIdentityLoading(first, alpha.id);
        }
        finally
        {
            Scribe.mode = previousMode;
            Scribe.loader.curXmlParent = previousXml;
            Scribe.loader.curParent = previousParent;
            Current.Game = previousGame;
        }
    }

    private static void CheckCommandOwnership(Game game, Map map, MapComponent_RaidTacticalExecution execution,
        RaidTacticalUnit first, RaidTacticalUnit second, MapComponent_RaidTacticalExecution.ExecutionState a,
        MapComponent_RaidTacticalExecution.ExecutionState b)
    {
        a.ActivePlan.Selected = b.ActivePlan.Selected = new RaidTacticalOption { Maneuver = RaidTacticalManeuver.CoordinatedEntry };
        a.ActivePlan.Assignments = first.Members.Select(pawn => new RaidTacticalAssignment { Pawn = pawn }).ToList();
        b.ActivePlan.Assignments = second.Members.Select(pawn => new RaidTacticalAssignment { Pawn = pawn }).ToList();
        Pawn actor = first.Members.First();
        bool Owns(string id, MapComponent_RaidTacticalExecution.ExecutionState state, Pawn pawn) =>
            (bool)AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "OwnsAssignment").Invoke(null, new object[] { id, state, pawn });
        Check(Owns(first.Id, a, actor) && Owns(second.Id, b, second.Members.First()), "Each squad must own its assigned pawns independently.");
        a.ActivePlan.UnitId = second.Id;
        Check(!Owns(first.Id, a, actor), "A plan belonging to a different unit must not acquire this pawn's command authority.");
        a.ActivePlan.UnitId = first.Id;
        var orders = new MapComponent_RaidTacticalOrders(map);
        var directives = (Dictionary<Pawn, RaidPawnOrder>)AccessTools.Field(typeof(MapComponent_RaidTacticalOrders), "orders").GetValue(orders);
        var order = new RaidPawnOrder { Pawn = actor, OrganizationId = first.OrganizationId, UnitId = first.Id, GroupId = first.GroupId };
        directives[actor] = order;
        object Directive() => AccessTools.Method(typeof(MapComponent_RaidTacticalOrders), "Get").Invoke(orders, new object[] { actor });
        Check((bool)AccessTools.Method(typeof(RaidPawnOrder), "OwnedBy").Invoke(order, new object[] { first }),
            "A directive must match the pawn's actual command unit and assigned group.");
        order.UnitId = second.Id;
        Check(Directive() == null, "Matching organization identity alone must not authorize a sibling squad's directive.");
        order.UnitId = first.Id;
        byPawnTransfer(actor, second.Group);
        Check(Directive() == null, "Changing a pawn's unit membership must invalidate its previous unit's directive.");
        byPawnTransfer(actor, first.Group);
        Check(typeof(Lord).GetMethod("RemovePawns", new[] { typeof(List<Pawn>) }) != null
            && typeof(LordMaker).GetMethod("MakeNewLord", new[] { typeof(Faction), typeof(LordJob), typeof(Map), typeof(IEnumerable<Pawn>) }) != null,
            "Unit withdrawal must use the installed game's real Lord transfer API; Unity Lord behavior requires in-game validation.");
        void byPawnTransfer(Pawn pawn, CombatGroup group) =>
            ((Dictionary<Pawn, CombatGroup>)AccessTools.Field(typeof(GameComponent_CombatOrganizations), "byPawn")
                .GetValue(OrganizationAPI.Registry))[pawn] = group;
    }

    private static void CheckPlanningBudget(Game game, MapComponent_RaidTacticalPlans plans)
    {
        game.tickManager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
        var organization = new CombatOrganization { id = "Queue", rootGroups = new List<CombatGroup>
            { Group("Q1", "Squad", 0), Group("Q2", "Squad", 0) } };
        organization.RestoreTreeLinks();
        var units = RaidTacticalUnit.ForOrganization(organization).ToList();
        // No active members means a cheap, real failed plan; the scheduler still
        // needs to give the second unit a turn if the first keeps invalidating.
        ((Dictionary<string, RaidStructureSnapshot>)AccessTools.Field(typeof(MapComponent_RaidTacticalPlans), "structures")
            .GetValue(plans))[organization.id] = new RaidStructureSnapshot { OrganizationId = organization.id };
        RaidTacticalPlan first = plans.GetPlan(units[0]);
        Check(first?.UnitId == units[0].Id && first.OrganizationId == organization.id,
            "Planning must write separate unit and shared geometry identities.");
        Check(plans.GetPlan(units[1]) == null, "The common planning budget must still limit expensive decisions to one per tick.");
        AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(game.tickManager, 1);
        plans.InvalidateDecision(units[0].Id);
        Check(plans.GetPlan(units[0]) == null && plans.GetPlan(units[1])?.UnitId == units[1].Id,
            "An unstable first unit must not starve a queued sibling's initial plan.");
    }

    private static void CheckIdentityLoading(RaidTacticalUnit unit, string memberGroup)
    {
        var document = new XmlDocument();
        document.LoadXml($"<root><organizationId>{unit.OrganizationId}</organizationId><organization>{unit.OrganizationId}</organization>"
            + $"<unitId>{unit.Id}</unitId><groupId>{memberGroup}</groupId></root>");
        Scribe.loader.curXmlParent = document.DocumentElement;
        Scribe.mode = LoadSaveMode.LoadingVars;
        var plan = new RaidTacticalPlan();
        Scribe.loader.curParent = plan;
        plan.ExposeData();
        Check(plan.UnitId == unit.Id && plan.OrganizationId == unit.OrganizationId && plan.GroupId == memberGroup,
            "Native Scribe loading must preserve plan ownership separately from geometry organization identity.");
        var state = new MapComponent_RaidTacticalExecution.ExecutionState();
        Scribe.loader.curParent = state;
        state.ExposeData();
        Check(state.UnitId == unit.Id && state.OrganizationId == unit.OrganizationId && state.GroupId == memberGroup,
            "Native Scribe loading must preserve the execution state's unit key.");
        var order = new RaidPawnOrder();
        Scribe.loader.curParent = order;
        order.ExposeData();
        Check(order.UnitId == unit.Id && order.OrganizationId == unit.OrganizationId && order.GroupId == memberGroup,
            "Native Scribe loading must preserve a directive's owner and fireteam membership.");
        var assignment = new RaidTacticalAssignment();
        Scribe.loader.curParent = assignment;
        assignment.ExposeData();
        Check(assignment.GroupId == memberGroup, "Native assignment loading must retain the High fireteam's actual GroupId.");
    }
}
