using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Helodrace.Squads;
using RimWorld;
using Verse;

internal static class Program
{
    private static int checks;
    private static int nextPawn;
    private static readonly SkillDef Shooting = new SkillDef { defName = "Shooting" };
    private static readonly RoleDef Rifle = new RoleDef
        { defName = "Rifle", combatFunction = "Rifle", preferredSkill = Shooting };
    private static readonly RoleDef Leader = CommandRole("Leader", "Squad", 40);
    private static readonly RoleDef Deputy = CommandRole("Deputy", "Squad", 30);
    private static readonly RoleDef TeamLeader = CommandRole("TeamLeader", "Team", 20);

    private static RoleDef CommandRole(string name, string level, int authority) => new RoleDef
    {
        defName = name, isCommandRole = true, commandAuthority = authority,
        canCommandUnitLevels = new List<string> { level }, preferredSkill = Shooting
    };
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    private static void Near(float expected, float actual, string message) =>
        Check(Math.Abs(expected - actual) < 0.001f, message + ": " + actual);
    private static Pawn Pawn(Faction faction, int skill = 5) => new Pawn
    {
        thingIDNumber = ++nextPawn, Faction = faction, skills = new Skills { level = skill }
    };
    private static RoleAssignment Assignment(Pawn pawn, RoleDef command = null, int order = -1) =>
        new RoleAssignment
        {
            pawn = pawn, combatRole = Rifle, commandRole = command, explicitSuccessionOrder = order,
            commandQualifications = new List<RoleDef> { Leader }
        };
    private static DoctrineDef Doctrine(params FormationDef[] formations) => new DoctrineDef
    {
        defName = "TestDoctrine", availableFormations = formations.ToList(),
        formationPointTolerance = 0.1f, commandLossDelayTicks = 90,
        commandRecoveryTicks = 300, actingCommandEfficiency = 0.7f
    };
    private static FormationDef Single(float cost) => new FormationDef
    {
        defName = "Sample", unitLevel = "Squad", commanderRole = Leader,
        minimumPersonnel = 1, idealPersonnel = 1, maximumPersonnel = 1,
        requiredRoles = new List<FormationRoleSlot>
        {
            new FormationRoleSlot
            {
                pawnKind = new PawnKindDef { combatPower = cost }, combatRole = Rifle, commandRole = Leader
            }
        }
    };
    private static CombatGroup Group(FormationDef formation, string id, params RoleAssignment[] assignments) =>
        new CombatGroup { id = id, name = id, formation = formation, roleAssignments = assignments.ToList() };
    private static CombatOrganization Organization(Faction faction, params CombatGroup[] roots)
    {
        var organization = new CombatOrganization
        { id = "Raid_184", faction = faction, doctrine = Doctrine(), rootGroups = roots.ToList() };
        organization.RestoreTreeLinks();
        foreach (var root in roots) root.InitializeCommand();
        return organization;
    }

    private static void TestPlanning()
    {
        var squad = Single(520);
        var doctrine = Doctrine(squad);
        var plan = FormationPlanner.Plan(500, doctrine);
        Check(plan.roots.Count == 1, "500pt must allow one 520pt unit");
        Near(20, plan.PointOverrun, "Single budget overrun");
        Near(0, plan.SupportPointsRemaining, "Overrun cannot create negative support");
        plan = FormationPlanner.Plan(1400, doctrine);
        Check(plan.roots.Count == 2, "1400pt cannot buy 1560pt with 10% tolerance");
        Near(360, plan.SupportPointsRemaining, "Unused points are support points");
        plan = FormationPlanner.Plan(1300, doctrine);
        Check(plan.roots.Count == 2, "MVP example must produce two full units");
        Near(260, plan.SupportPointsRemaining, "MVP reserve");
        Check(FormationPlanner.Plan(472, doctrine).roots.Count == 0, "No partial unit below tolerance");
        Check(FormationPlanner.Plan(0, doctrine).Personnel == 0, "No zero-budget pawns");
        foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            bool threw = false;
            try { FormationPlanner.Plan(bad, doctrine); } catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "Invalid budget must be rejected");
        }
        var invalid = Single(520);
        invalid.idealPersonnel = 2;
        Check(invalid.ConfigErrors().Any(), "Personnel mismatches are invalid");
        Check(FormationPlanner.Plan(1000, Doctrine(invalid)).Personnel == 0, "No invalid incomplete formation");
        var invalidGrenade = Single(520);
        invalidGrenade.requiredRoles[0].grenadeLoadout.Add(null);
        Check(invalidGrenade.ConfigErrors().Any(), "Unresolved grenade loadouts are invalid");
        var cyclic = Single(520);
        cyclic.childFormations.Add(new ChildFormationSlot { formation = cyclic });
        Check(cyclic.ConfigErrors().Any(), "Cyclic formation must be rejected");

        var fireteam = Single(60);
        fireteam.defName = "Fireteam";
        fireteam.unitLevel = "Team";
        fireteam.commanderRole = TeamLeader;
        fireteam.requiredRoles[0].commandRole = TeamLeader;
        fireteam.requiredRoles[0].count = 4;
        fireteam.minimumPersonnel = fireteam.idealPersonnel = fireteam.maximumPersonnel = 4;
        var modernSquad = Single(60);
        modernSquad.defName = "ModernSquad";
        modernSquad.childFormations.Add(new ChildFormationSlot { formation = fireteam, count = 2 });
        modernSquad.minimumPersonnel = modernSquad.idealPersonnel = modernSquad.maximumPersonnel = 9;
        Check(!modernSquad.ConfigErrors().Any(), "A modern nine-person squad is data driven");
        Near(540, modernSquad.FormationCost, "Nested formation cost");
        var platoon = Single(60);
        platoon.childFormations.Add(new ChildFormationSlot { formation = modernSquad, count = 3 });
        platoon.minimumPersonnel = platoon.idealPersonnel = platoon.maximumPersonnel = 28;
        Near(1680, platoon.FormationCost, "Arbitrary depth cost");
        Check(platoon.StandardPersonnel == 28, "Arbitrary depth personnel");
    }

    private static void TestSuccession()
    {
        var faction = new Faction();
        var original = Pawn(faction, 3);
        var deputy = Pawn(faction, 1);
        var expert = Pawn(faction, 20);
        var group = Group(Single(80), "Squad_1", Assignment(original, Leader, 0),
            Assignment(deputy, Deputy, 1), Assignment(expert));
        var organization = Organization(faction, group);
        Check(group.commander == original, "Original commander assigned from command role");
        original.Downed = true;
        group.UpdateCommand(10);
        Check(group.commandState == CommandState.CommanderLost, "Commander loss is detected");
        Check(organization.GlobalCommandState == CommandState.CommanderLost, "Organization exposes actual loss state");
        group.UpdateCommand(85);
        Check(group.commandState == CommandState.SuccessionPending && group.EffectiveCommander == null,
            "Confusion interval must delay succession");
        group.UpdateCommand(100);
        Check(group.actingCommander == deputy, "Explicit deputy outranks highest skilled pawn");
        Check(group.commandState == CommandState.ActingCommander, "Acting state after succession");
        Near(0.7f, group.CommandEfficiency, "Reduced temporary command efficiency");
        Check(group.roleAssignments[1].commandRole == Deputy && group.roleAssignments[1].combatRole == Rifle,
            "Succession must preserve original combat and command roles");
        group.UpdateCommand(400);
        Check(group.commandState == CommandState.CommandRestored, "Command recovers after 300 ticks");
        group.UpdateCommand(415);
        Near(1, group.CommandEfficiency, "Acting commander can reach normal efficiency");
        deputy.Dead = true;
        group.UpdateCommand(430);
        Check(group.EffectiveCommander == null, "Second commander loss resets command");
        group.UpdateCommand(519);
        Check(group.actingCommander == null, "Second loss gets its own delay");
        group.UpdateCommand(520);
        Check(group.actingCommander == expert, "Dynamic fallback succeeds after explicit successors are lost");
        original.Downed = false;
        group.UpdateCommand(535);
        Check(group.EffectiveCommander == original && group.actingCommander == null,
            "Returning original commander reclaims command");

        original.IsPrisoner = true;
        group.UpdateCommand(550);
        group.UpdateCommand(640);
        Check(group.actingCommander == expert, "Captured commander cannot retain command");
        expert.Faction = new Faction();
        group.UpdateCommand(655);
        group.UpdateCommand(745);
        Check(group.EffectiveCommander == null, "Enemy/recruited pawns cannot succeed");
    }

    private static void TestHierarchyAndPersistence()
    {
        var faction = new Faction();
        var squadLeader = Pawn(faction);
        var alphaLeader = Pawn(faction);
        var alphaRifleman = Pawn(faction);
        var bravoLeader = Pawn(faction);
        var team = Single(80);
        team.unitLevel = "Team"; team.commanderRole = TeamLeader;
        team.requiredRoles[0].commandRole = TeamLeader;
        var alpha = Group(team, "Alpha", Assignment(alphaLeader, TeamLeader, 0), Assignment(alphaRifleman));
        var bravo = Group(team, "Bravo", Assignment(bravoLeader, TeamLeader, 0));
        alpha.parentSuccessionPriority = 1;
        bravo.parentSuccessionPriority = 2;
        var squad = Group(Single(80), "Squad", Assignment(squadLeader, Leader, 0));
        squad.children.Add(alpha); squad.children.Add(bravo);
        var organization = Organization(faction, squad);
        organization.SetBudget(new FormationPlan { initialRaidPoints = 1300, formationPointsSpent = 1040 });
        squadLeader.Dead = true;
        squad.UpdateCommand(100);
        Check(alpha.EffectiveCommander == alphaLeader && bravo.EffectiveCommander == bravoLeader,
            "Lower units continue while parent command is lost");
        Check(!alpha.ParentCommandAvailable, "Parent command availability tracks disruption");
        squad.UpdateCommand(190);
        Check(squad.actingCommander == alphaLeader, "Qualified subordinate commander takes parent command");
        Check(alpha.roleAssignments[0].commandRole == TeamLeader && alpha.roleAssignments[0].combatRole == Rifle,
            "Parent acting command cannot overwrite original team/combat role");
        Check(alpha.EffectiveCommander == alphaLeader, "Subordinate can retain original command concurrently");
        Check(alpha.ParentCommandAvailable, "Parent command is restored by acting commander");
        alphaLeader.Downed = true;
        squad.UpdateCommand(205);
        squad.UpdateCommand(295);
        Check(alpha.actingCommander == alphaRifleman, "Local successor selected within Alpha");
        Check(alpha.actingCommander != bravoLeader, "Local succession cannot borrow sibling commander");
        Check(squad.actingCommander == alphaRifleman, "Hierarchical succession follows current child command");

        Check(organization.TrySpendSupportPoints(100), "Spend reserve");
        var loaded = Scribe.RoundTrip(organization);
        var loadedSquad = loaded.rootGroups.Single();
        var loadedAlpha = loadedSquad.children[0];
        Check(!ReferenceEquals(loadedSquad, squad), "Groups were deeply recreated");
        Check(loadedAlpha.Parent == loadedSquad && loadedAlpha.Organization == loaded,
            "Parent and organization links restored after loading");
        Check(loadedAlpha.parentGroupId == "Squad" && loadedAlpha.id == "Alpha" && loaded.id == "Raid_184",
            "Permanent IDs survive loading");
        Check(loadedAlpha.parentSuccessionPriority == 1, "Explicit child succession order survives loading");
        Check(loadedSquad.actingCommander == alphaRifleman && loadedAlpha.actingCommander == alphaRifleman,
            "Nested acting commanders survive loading");
        Check(loadedSquad.commandState == squad.commandState && loadedSquad.actingStartedTick == 295,
            "Command state and recovery clock survive loading");
        Check(loadedAlpha.roleAssignments[0].commandRole == TeamLeader, "Original role survives loading");
        Near(160, loaded.GetSupportPoints(), "Spent support budget survives loading");
        loadedSquad.UpdateCommand(595);
        Near(1, loadedSquad.CommandEfficiency, "Command recovery resumes after loading");
        loadedAlpha.roleAssignments[1].pawn = null;
        loadedAlpha.actingCommander = null;
        loadedAlpha.UpdateCommand(610);
        loadedAlpha.UpdateCommand(700);
        Check(loadedAlpha.EffectiveCommander == null, "Missing saved pawn references are safe");
    }

    private static void TestQualificationAndSupport()
    {
        var faction = new Faction();
        var original = Pawn(faction);
        var skilled = Pawn(faction, 20);
        var qualified = Pawn(faction, 4);
        var group = Group(Single(80), "Qualifications", Assignment(original, Leader, 0),
            Assignment(skilled), Assignment(qualified));
        group.roleAssignments[1].commandQualifications.Clear();
        var organization = Organization(faction, group);
        original.Dead = true;
        group.UpdateCommand(0); group.UpdateCommand(90);
        Check(group.actingCommander == qualified, "Qualified pawn outranks unqualified experience");
        organization.SetBudget(new FormationPlan { initialRaidPoints = 1400, formationPointsSpent = 1040 });
        Check(!organization.TrySpendSupportPoints(361), "Cannot overspend reserve");
        foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity })
            Check(!organization.TrySpendSupportPoints(bad), "Invalid support spend rejected");
        Check(organization.TrySpendSupportPoints(160), "Valid reserve spend");
        Near(200, organization.GetSupportPoints(), "Reserve deducted");
        organization.RefundSupportPoints(1000);
        Near(360, organization.GetSupportPoints(), "Refund cannot mint reserve beyond original budget");
        organization.RefundSupportPoints(float.NaN);
        Near(360, organization.GetSupportPoints(), "Invalid refund rejected");
    }

    private static void TestRaidMemberDeparture()
    {
        var faction = new Faction();
        var leader = Pawn(faction);
        var deputy = Pawn(faction);
        var member = Pawn(faction);
        var group = Group(Single(80), "DepartingSquad", Assignment(leader, Leader, 0),
            Assignment(deputy, Deputy, 1), Assignment(member));
        var organization = Organization(faction, group);
        Check(organization.RemoveMember(leader), "Departing commander must be removed");
        Check(!organization.AllMembers.Contains(leader), "Departed pawn cannot remain in membership");
        Check(group.commander == null && group.actingCommander == null, "Departed commander reference must clear");
        Check(!group.successionList.Contains(leader), "Departed pawn cannot inherit command later");
        group.UpdateCommand(0);
        group.UpdateCommand(90);
        Check(group.actingCommander == deputy, "Remaining squad can succeed after retreat");
        Check(!organization.RemoveMember(leader), "Repeated exit/world transition must be harmless");
        Check(organization.RemoveMember(deputy) && organization.RemoveMember(member),
            "Remaining members may leave independently");
        Check(!organization.AllMembers.Any(), "Empty raid organization has no active members");
        var loaded = Scribe.RoundTrip(organization);
        Check(!loaded.AllMembers.Any() && loaded.rootGroups[0].successionList.Count == 0,
            "Departed pawn references must not reappear after loading");
    }

    // Read the actual XML definitions, including inherited pawn costs and role metadata.
    private static Dictionary<string, Def> LoadDefinitions(string root)
    {
        var nodes = new[] { "Defs/Helod/Pawns/PawnKinds.xml", "Defs/Helod/Pawns/PawnKinds_GreatWar.xml",
            "Defs/Organization/Organization_GreatWar.xml", "Defs/Organization/Organization_Modern.xml" }
            .SelectMany(file => XDocument.Load(Path.Combine(root, file)).Root.Elements()).ToList();
        var templates = nodes.Where(node => node.Attribute("Name") != null)
            .ToDictionary(node => (string)node.Attribute("Name"));
        XElement Merged(XElement node)
        {
            var parent = (string)node.Attribute("ParentName");
            var merged = parent == null ? new XElement(node.Name) : Merged(templates[parent]);
            foreach (var child in node.Elements())
            {
                merged.Elements(child.Name).Remove();
                merged.Add(new XElement(child));
            }
            return merged;
        }
        var defs = new Dictionary<string, Def> { ["Shooting"] = Shooting };
        foreach (string file in Directory.GetFiles(Path.Combine(root, "Defs/ModernWar/ModularPresets"), "*.xml"))
            foreach (XElement preset in XDocument.Load(file).Root.Elements("Helodrace.ModernWar.ModularWeaponPresetDef"))
            {
                string name = (string)preset.Element("defName");
                defs.Add(name, new Helodrace.ModernWar.ModularWeaponPresetDef { defName = name });
            }
        var grenadeNames = new[] { "Defs/GreatWar/Items/Grenades_GreatWar.xml",
                "Defs/ColdWar/Items/Grenades_ColdWar.xml",
                "Defs/ModernWar/Items/Grenades_ModernWar.xml" }
            .SelectMany(file => XDocument.Load(Path.Combine(root, file)).Root.Elements("ThingDef"))
            .Select(node => (string)node.Element("defName"))
            .Where(name => name?.StartsWith("HD_Grenade_") == true).ToHashSet();
        foreach (string grenadeName in grenadeNames)
            defs.Add(grenadeName, new ThingDef { defName = grenadeName });
        defs.Add("HD_C4_Charge", new ThingDef { defName = "HD_C4_Charge" });
        defs.Add("HD_M81Igniter", new ThingDef { defName = "HD_M81Igniter" });
        defs.Add("HD_40mmM381HE_Round", new ThingDef { defName = "HD_40mmM381HE_Round" });
        defs.Add("HD_Apparel_ZaperX26_Device", new ThingDef
            { defName = "HD_Apparel_ZaperX26_Device", IsApparel = true });
        defs.Add("HD_Apparel_GW_Sledgehammer", new ThingDef
            { defName = "HD_Apparel_GW_Sledgehammer", IsApparel = true });
        foreach (var node in nodes.Where(node => node.Element("defName") != null))
        {
            Def def = node.Name.LocalName switch
            {
                "PawnKindDef" => new PawnKindDef(),
                "Helodrace.Squads.RoleDef" => new RoleDef(),
                "Helodrace.Squads.FormationDef" => new FormationDef(),
                "Helodrace.Squads.DoctrineDef" => new DoctrineDef(),
                _ => throw new Exception("Unexpected definition " + node.Name)
            };
            defs.Add((string)node.Element("defName"), def);
        }
        object Value(Type type, XElement element)
        {
            if (type == typeof(string)) return element.Value;
            if (type == typeof(int)) return int.Parse(element.Value, CultureInfo.InvariantCulture);
            if (type == typeof(float)) return float.Parse(element.Value, CultureInfo.InvariantCulture);
            if (type == typeof(bool)) return bool.Parse(element.Value);
            if (typeof(Def).IsAssignableFrom(type)) return defs[element.Value];
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type);
                foreach (var li in element.Elements("li")) list.Add(Value(type.GetGenericArguments()[0], li));
                return list;
            }
            var result = Activator.CreateInstance(type);
            Fill(result, element);
            return result;
        }
        void Fill(object target, XElement element)
        {
            foreach (var child in element.Elements())
            {
                var field = target.GetType().GetField(child.Name.LocalName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null) field.SetValue(target, Value(field.FieldType, child));
            }
        }
        foreach (var node in nodes.Where(node => node.Element("defName") != null))
            Fill(defs[(string)node.Element("defName")], Merged(node));
        return defs;
    }

    private static void TestActualDefinitions(string root)
    {
        var defs = LoadDefinitions(root);
        var usedGrenades = new HashSet<string>();
        foreach (var formation in defs.Values.OfType<FormationDef>()
            .Where(formation => formation.defName.StartsWith("HD_Formation_GW_")))
        {
            Check(!formation.ConfigErrors().Any(), formation.defName + " config: "
                + string.Join(", ", formation.ConfigErrors()));
            foreach (var slot in formation.Slots)
            {
                Check(slot.pawnKind.defName.StartsWith("HD_GW_"), "Only Great War PawnKinds may spawn");
                Check(slot.grenadeLoadout.Count == 1, "Each Great War raid member carries one grenade");
                foreach (ThingDef grenade in slot.grenadeLoadout)
                {
                    Check(grenade.defName == "HD_Grenade_MKII" || grenade.defName == "HD_Grenade_MKIII"
                        || grenade.defName == "HD_Grenade_M8_Item", "Only designated Great War grenades may spawn");
                    usedGrenades.Add(grenade.defName);
                }
            }
        }
        Check(usedGrenades.SetEquals(new[] { "HD_Grenade_MKII", "HD_Grenade_MKIII", "HD_Grenade_M8_Item" }),
            "Raid formations distribute fragmentation, offensive and smoke grenades");
        foreach (string name in new[] { "HD_Formation_GW_Patrol",
            "HD_Formation_GW_RifleSquad" })
            Check(((FormationDef)defs[name]).Slots.Sum(slot => slot.count
                * slot.apparelLoadout.Count(item => item.defName
                    == "HD_Apparel_GW_Sledgehammer")) == 1,
                name + " has one sledgehammer bearer without replacing a firearm");
        XElement hammer = XDocument.Load(Path.Combine(root,
            "Defs/GreatWar/Items/Sledgehammer_GreatWar.xml")).Root
            .Elements("ThingDef").Single(node =>
                (string)node.Element("defName") == "HD_Apparel_GW_Sledgehammer");
        Check((string)hammer.Attribute("ParentName") == "ApparelNoQualityBase"
            && hammer.Element("tools") == null && hammer.Element("verbs") == null
            && hammer.Element("equipmentType") == null
            && hammer.Element("weaponTags") == null
            && (string)hammer.Descendants("texPath").First()
                == "Weapons/GreatWar/HD_Sledgehammer",
            "Sledgehammer is wearable breaching gear using the supplied texture, not a weapon");
        Check(hammer.Descendants("li").Any(node =>
                (string)node.Attribute("Class")
                    == "Helodrace.CompProperties_SledgehammerBreach")
            && File.Exists(Path.Combine(root,
                "Textures/Weapons/GreatWar/HD_Sledgehammer.png"))
            && File.Exists(Path.Combine(root,
                "Textures/Skill/HD_BreachSledgeHammer.png")),
            "Sledgehammer breach component and both supplied textures exist");
        Check(XDocument.Load(Path.Combine(root, "Defs/GreatWar/Sledgehammer_Breach.xml"))
                .Root.Elements("JobDef").Any(node =>
                    (string)node.Element("defName") == "HD_SledgehammerBreach"
                    && (string)node.Element("driverClass")
                        == "Helodrace.JobDriver_SledgehammerBreach"),
            "Sledgehammer work job is defined");
        Check(XDocument.Load(Path.Combine(root, "Defs/GreatWar/Sledgehammer_Breach.xml"))
            .Root.Elements("JobDef").Any(node =>
                (string)node.Element("defName") == "HD_RecoverSledgehammer"
                && (string)node.Element("driverClass") == "Helodrace.JobDriver_RecoverSledgehammer"
                && (string)node.Element("forceCompleteBeforeNextJob") == "true"),
            "Sledgehammer recovery is a protected movement and equipment job");
        Check(XDocument.Load(Path.Combine(root,
                "Defs/Helod/Race/HelodRaceSettings.xml")).Descendants("apparelList")
                .Elements("li").Any(node => node.Value == "HD_Apparel_GW_Sledgehammer"),
            "Helod apparel rules permit raid bearers to wear the sledgehammer");
        using (var deployedDll = File.OpenRead(Path.Combine(root,
                   "Assemblies/Helodrace.dll")))
        using (var assembly = new PEReader(deployedDll))
        {
            MetadataReader metadata = assembly.GetMetadataReader();
            HashSet<string> types = metadata.TypeDefinitions.Select(handle =>
                metadata.GetString(metadata.GetTypeDefinition(handle).Name)).ToHashSet();
            Check(types.Contains("CompProperties_SledgehammerBreach")
                && types.Contains("CompSledgehammerBreach")
                && types.Contains("JobDriver_SledgehammerBreach"),
                "Deployed game assembly includes the sledgehammer component and job");
        }
        XElement rifleKind = XDocument.Load(Path.Combine(root,
                "Defs/Helod/Pawns/PawnKinds_GreatWar.xml")).Root.Elements("PawnKindDef")
            .First(node => (string)node.Element("defName") == "HD_GW_HelodRifleman");
        Check((bool?)rifleKind.Element("canBeSapper") == true,
            "Great War riflemen can fill the sapper duty without a separate role PawnKind");
        XElement lowFaction = XDocument.Load(Path.Combine(root,
                "Defs/Factions/Factions_Helod.xml")).Root.Elements("FactionDef")
            .First(node => (string)node.Element("defName") == "HD_HelodCivilLowFaction");
        Check(lowFaction.Element("disallowedRaidStrategies")?.Elements("li")
                .Any(node => node.Value == "ImmediateAttackSappers") != true,
            "Great War faction allows sapper raids");
        var doctrine = (DoctrineDef)defs["HD_Doctrine_GreatWar"];
        Check(!doctrine.ConfigErrors().Any(), "Actual doctrine is valid");
        Near(510, ((FormationDef)defs["HD_Formation_GW_Patrol"]).FormationCost, "Actual patrol cost");
        Near(1000, ((FormationDef)defs["HD_Formation_GW_RifleSquad"]).FormationCost, "Actual squad cost");
        Near(3885, ((FormationDef)defs["HD_Formation_GW_RiflePlatoon"]).FormationCost, "Actual reinforced platoon cost");
        Check(((FormationDef)defs["HD_Formation_GW_RiflePlatoon"]).StandardPersonnel == 45, "Platoon is complete");
        var modern = (DoctrineDef)defs["HD_Doctrine_Modern"];
        Check(!modern.ConfigErrors().Any(), "Modern doctrine is valid");
        foreach (FormationDef formation in defs.Values.OfType<FormationDef>()
            .Where(formation => formation.defName.StartsWith("HD_Formation_MW_")))
        {
            Check(!formation.ConfigErrors().Any(), formation.defName + " config: "
                + string.Join(", ", formation.ConfigErrors()));
            Check(formation.Slots.All(slot => slot.pawnKind.defName == "HD_MW_HelodRifleman"),
                "Modern formations use modern equipment");
        }
        Check(((FormationDef)defs["HD_Formation_MW_Fireteam"]).StandardPersonnel == 4,
            "Modern fireteam is complete");
        Near(660, ((FormationDef)defs["HD_Formation_MW_Fireteam"]).FormationCost,
            "Modern fireteam cost");
        Check(((FormationDef)defs["HD_Formation_MW_RifleSquad"]).StandardPersonnel == 13,
            "Modern USMC-style squad has three four-person fireteams and a leader");
        Near(2275, ((FormationDef)defs["HD_Formation_MW_RifleSquad"]).FormationCost,
            "Modern squad cost");
        var modernLeader = ((FormationDef)defs["HD_Formation_MW_RifleSquad"]).requiredRoles[0];
        Check(modernLeader.grenadeLoadout.Count(item => item.defName == "HD_C4_Charge") == 3
            && modernLeader.grenadeLoadout.Any(item => item.defName == "HD_M81Igniter"),
            "Modern squad leader carries enough C4 and a shock-tube igniter");
        Check(((FormationDef)defs["HD_Formation_MW_RiflePlatoon"]).StandardPersonnel == 43,
            "Modern platoon has three thirteen-person squads and four headquarters personnel");
        Near(7445, ((FormationDef)defs["HD_Formation_MW_RiflePlatoon"]).FormationCost,
            "Modern platoon cost");
        Check(((FormationDef)defs["HD_Formation_MW_Fireteam"]).requiredRoles[0]
                .apparelLoadout.Single().defName == "HD_Apparel_ZaperX26_Device"
            && ((FormationDef)defs["HD_Formation_MW_RifleSquad"]).requiredRoles[0]
                .apparelLoadout.Single().defName == "HD_Apparel_ZaperX26_Device",
            "Modern team and squad leaders carry a ZAPER X26");
        XElement highFaction = XDocument.Load(Path.Combine(root, "Defs/Factions/Factions_Helod.xml"))
            .Root.Elements("FactionDef").First(node => (string)node.Element("defName")
                == "HD_HelodCivilHighFaction");
        Check((string)highFaction.Descendants("doctrine").FirstOrDefault()
            == "HD_Doctrine_Modern", "High faction uses the modern organization doctrine");
        Check(highFaction.Descendants("HD_MW_HelodRifleman").Any(),
            "High faction has a modern combat pawn pool");
        foreach (string name in new[] { "HD_HelodCivilLowFaction",
            "HD_HelodCivilHighFaction" })
        {
            XElement faction = XDocument.Load(Path.Combine(root,
                "Defs/Factions/Factions_Helod.xml")).Root.Elements("FactionDef")
                .First(node => (string)node.Element("defName") == name);
            string[] bannedArrivals = faction.Element("arrivalModeBlacklist")
                ?.Elements("li").Select(node => node.Value).ToArray()
                ?? Array.Empty<string>();
            Check(new[] { "CenterDrop", "EdgeDrop", "RandomDrop",
                "SpecificDropDebug" }.All(bannedArrivals.Contains),
                name + " forbids drop-pod raid arrivals");
        }
        var modernTeam = (FormationDef)defs["HD_Formation_MW_Fireteam"];
        Check(modern.movementNodeSpan == 16 && modern.movementGuidanceRadius == 3
            && modern.movementPortalRadius == 2 && modern.movementArrivalRefreshTicks == 60
            && modern.movementDestinationRetryTicks == 120,
            "Doctrine provides bounded movement node defaults independently of PawnKinds");
        modern.movementPortalRadius = 7;
        Check(modern.ConfigErrors().Any(error => error.Contains("Movement node")),
            "Invalid arrival sizes are rejected rather than turning local refresh into map-wide work");
        modern.movementPortalRadius = 2;
        Check(modernTeam.requiredRoles[2].apparelLoadout.Single().defName == "HD_Apparel_GW_Sledgehammer"
            && ((FormationDef)defs["HD_Formation_MW_RifleSquad"]).childFormations.Single().count == 3,
            "Each High fireteam assistant carries a backup hammer, giving three per squad");
        XElement highKind = XDocument.Load(Path.Combine(root, "Defs/Helod/Pawns/PawnKinds.xml"))
            .Root.Elements("PawnKindDef").Single(node => (string)node.Element("defName") == "HD_MW_HelodRifleman");
        Check(highKind.Element("apparelRequired").Elements("li").Select(node => node.Value).ToHashSet()
            .SetEquals(new[] { "HD_Apparel_UCPBlouse", "HD_Apparel_UCPPants", "HD_Apparel_IBTVAssault", "HD_Apparel_FASTMT" }),
            "Every High soldier has the same explicit uniform, vest and helmet");
        Check(modernTeam.requiredRoles[0].weaponPreset?.defName == "HD_WeaponPreset_M16A4_UBGL"
            && modernTeam.requiredRoles[1].weaponPreset?.defName == "HD_WeaponPreset_M249_USMC"
            && modernTeam.requiredRoles.Skip(2).All(slot => slot.weaponPreset == null),
            "High specialists override their weapon presets while riflemen retain the common PawnKind loadout");
        Check(modernTeam.requiredRoles[0].inventoryLoadout.Count == 6
            && modernTeam.requiredRoles[0].inventoryLoadout.All(item => item.defName == "HD_40mmM381HE_Round"),
            "Each launcher operator receives six compatible HE rounds without a temporary tablet");
        Check(new[] { modernTeam, (FormationDef)defs["HD_Formation_MW_RifleSquad"], (FormationDef)defs["HD_Formation_MW_RiflePlatoon"] }
            .SelectMany(formation => formation.Slots).All(slot => !slot.inventoryLoadout.Any(item => item.defName == "HD_MilitaryTablet")),
            "High team, squad and platoon billets never issue temporary tablets");
        Check(((DoctrineDef)defs["HD_Doctrine_Modern"]).tacticalRadio && !doctrine.tacticalRadio,
            "Actual High/LOW doctrines select equipment-based radio and physical contact respectively");
        XElement tablet = XDocument.Load(Path.Combine(root, "Defs/ModernWar/Items/Apparel_ModernWar.xml"))
            .Root.Elements("ThingDef").Single(node => (string)node.Element("defName") == "HD_MilitaryTablet");
        Check(!tablet.Element("comps").Elements("li").Any(node => (string)node.Attribute("Class") == "Helodrace.CompProperties_TacticalRadio"),
            "The temporary tablet does not provide tactical radio hardware");
        XElement radioModule = XDocument.Load(Path.Combine(root, "Defs/ModernWar/Items/ModularArmorPartItems.xml"))
            .Root.Elements("ThingDef").Single(node => (string)node.Element("defName") == "HD_ModularPart_WalkieTalkie");
        Check(radioModule.Element("comps").Elements("li").Any(node => (string)node.Attribute("Class") == "Helodrace.CompProperties_TacticalRadio"
            && (string)node.Element("network") == "ModernInfantry" && (int?)node.Element("range") == 300),
            "The armor radio module contains the actual compatible radio hardware");
        XElement armorPreset = XDocument.Load(Path.Combine(root, "Defs/ModernWar/ModularLoadoutPresets.xml"))
            .Root.Elements("Helodrace.ModernWar.ModularArmorPresetDef").Single(node => (string)node.Element("defName") == "HD_ArmorPreset_IBTV_Rifleman");
        Check(armorPreset.Element("palsParts").Elements("li").Any(node => (string)node.Element("part") == "HD_IOTVPart_WalkieTalkie")
            && highKind.Descendants("armorPresets").Elements("li").Any(node => node.Value == "HD_ArmorPreset_IBTV_Rifleman"),
            "Every High rifleman receives the existing armor preset with an installed radio module");
        Check(modernTeam.requiredRoles.Select(slot => slot.combatRole.defName).SequenceEqual(new[] {
            "HD_Role_Grenadier", "HD_Role_AutomaticRifleman", "HD_Role_AssistantAutomaticRifleman", "HD_Role_Rifleman" }),
            "One PawnKind supplies the USMC leader/grenadier, automatic rifleman, assistant and rifleman billets");
        Check(modernTeam.requiredRoles.Select(slot => slot.explicitSuccessionOrder).SequenceEqual(new[] { 0, 1, 2, 3 }),
            "The automatic rifleman succeeds the team leader before the assistant and rifleman");
        Check(FormationPlanner.Plan(600f, modern).Personnel == 0,
            "High raids below the complete squad budget cannot spawn independent fireteams");
        Check(modern.availableFormations.All(formation => formation.unitLevel != "Team"),
            "High fireteams are subdivisions, never independently selectable raid units");
        foreach (float points in Enumerable.Range(0, 400).Select(index => index * 47f))
        {
            var highPlan = FormationPlanner.Plan(points, modern);
            Check(highPlan.roots.All(formation => formation.unitLevel == "Squad" || formation.unitLevel == "Platoon"),
                "Every High raid root is a complete squad or platoon");
            Check(highPlan.Personnel == 0 || highPlan.Personnel >= 13,
                "High never fields fewer than thirteen personnel");
        }
        Check(FormationPlanner.Plan(2100f, modern).Personnel == 13,
            "A mid-sized high-faction raid fields one complete squad");
        Check(FormationPlanner.Plan(6800f, modern).Personnel == 43,
            "A large high-faction raid fields one complete platoon");
        foreach (float points in Enumerable.Range(0, 400).Select(index => index * 47f))
        {
            var plan = FormationPlanner.Plan(points, doctrine);
            Check(plan.formationPointsSpent <= points * 1.1f + 0.001f, "Total tolerance boundary");
            Near(Math.Max(0, points - plan.formationPointsSpent), plan.SupportPointsRemaining, "Reserve conservation");
            Check(plan.roots.All(formation => formation.StandardPersonnel == formation.idealPersonnel),
                "Every chosen unit is complete");
        }
        foreach (float points in new[] { 500f, 1000f, 1300f, 1400f, 3000f, 4000f })
        {
            var plan = FormationPlanner.Plan(points, doctrine);
            Console.WriteLine($"{points}pt: personnel={plan.Personnel}, spent={plan.formationPointsSpent}, "
                + $"support={plan.SupportPointsRemaining}, overrun={plan.PointOverrun}");
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            TestPlanning(); TestSuccession(); TestHierarchyAndPersistence(); TestQualificationAndSupport();
            TestRaidMemberDeparture();
            TestActualDefinitions(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
            Console.WriteLine("PASS: " + checks + " assertions (production logic, actual XML, game boundary stubs).");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
