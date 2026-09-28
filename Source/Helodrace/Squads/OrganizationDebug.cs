using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace.Squads
{
    public static class OrganizationDebug
    {
        private static string PawnName(Pawn pawn) => pawn?.LabelShortCap ?? "-";
        public static string DescribePawn(Pawn pawn)
        {
            CombatGroup group = OrganizationAPI.GetGroup(pawn);
            if (group == null) return "No combat organization.";
            RoleAssignment role = group.roleAssignments.First(assignment => assignment.pawn == pawn);
            var text = new StringBuilder();
            text.AppendLine("Organization: " + group.Organization.id);
            text.AppendLine("Unit: " + group.name + " [" + group.id + "]");
            text.AppendLine("Parent: " + (group.Parent?.name ?? "-"));
            text.AppendLine("Combat role: " + role.combatRole.LabelCap);
            text.AppendLine("Command role: " + (role.commandRole?.LabelCap.ToString() ?? "-"));
            var acting = group.Organization.AllGroups.Where(value => value.actingCommander == pawn
                && value.EffectiveCommander == pawn).Select(value => value.name + ": " + value.formation.commanderRole.LabelCap);
            text.AppendLine("Acting role: " + string.Join(", ", acting));
            text.AppendLine("Commander: " + PawnName(group.EffectiveCommander));
            text.AppendLine("State: " + group.commandState + "; efficiency: " + group.CommandEfficiency.ToString("P0"));
            text.AppendLine("Parent command available: " + group.ParentCommandAvailable);
            text.AppendLine("Succession: " + string.Join(" → ", group.successionList.Select(PawnName)));
            return text.ToString().TrimEnd();
        }

        public static string Describe(CombatOrganization organization)
        {
            var text = new StringBuilder();
            text.AppendLine(organization.id + " / " + organization.doctrine.LabelCap);
            text.AppendLine("Initial points: " + organization.initialRaidPoints.ToString("0.##"));
            text.AppendLine("Formation points: " + organization.formationPointsSpent.ToString("0.##"));
            text.AppendLine("Support points: " + organization.GetSupportPoints().ToString("0.##"));
            text.AppendLine("Point overrun: " + organization.PointOverrun.ToString("0.##"));
            foreach (CombatGroup root in organization.rootGroups) DescribeGroup(text, root, 0);
            return text.ToString();
        }

        private static void DescribeGroup(StringBuilder text, CombatGroup group, int depth)
        {
            string indent = new string(' ', depth * 2);
            text.AppendLine(indent + group.name + " [" + group.id + "] " + group.commandState);
            text.AppendLine(indent + "Commander: " + PawnName(group.EffectiveCommander)
                + "; efficiency: " + group.CommandEfficiency.ToString("P0"));
            foreach (RoleAssignment assignment in group.roleAssignments)
                text.AppendLine(indent + "  " + PawnName(assignment.pawn) + ": " + assignment.combatRole.LabelCap
                    + (assignment.commandRole == null ? "" : " / " + assignment.commandRole.LabelCap.ToString()));
            text.AppendLine(indent + "Succession: " + string.Join(" → ", group.successionList.Select(PawnName)));
            foreach (CombatGroup child in group.children) DescribeGroup(text, child, depth + 1);
        }

        [DebugAction("Helodrace/Organization", "Inspect selected pawn organization",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void InspectSelected()
        {
            Pawn pawn = Find.Selector.SingleSelectedThing as Pawn;
            CombatOrganization organization = OrganizationAPI.GetOrganization(pawn);
            Find.WindowStack.Add(new Dialog_MessageBox(organization == null
                ? "Select an organized raid pawn." : DescribePawn(pawn) + "\n\n" + Describe(organization)));
        }

        [DebugAction("Helodrace/Organization", "List combat organizations",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ListOrganizations()
        {
            Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n\n",
                OrganizationAPI.Registry.Organizations.Select(Describe))));
        }

        [DebugAction("Helodrace/Organization", "Preview Great War complete formations",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Preview()
        {
            DoctrineDef doctrine = DefDatabase<DoctrineDef>.GetNamed("HD_Doctrine_GreatWar");
            var text = new StringBuilder();
            foreach (float points in new[] { 100f, 250f, 500f, 1000f, 1400f, 3000f, 4000f })
            {
                FormationPlan plan = FormationPlanner.Plan(points, doctrine);
                text.AppendLine(points + "pt → " + string.Join(", ", plan.roots.Select(root => root.LabelCap.ToString())));
                text.AppendLine("  Personnel=" + plan.Personnel + ", spent=" + plan.formationPointsSpent
                    + ", support=" + plan.SupportPointsRemaining + ", overrun=" + plan.PointOverrun);
            }
            Find.WindowStack.Add(new Dialog_MessageBox(text.ToString()));
        }

        [DebugAction("Helodrace/Organization", "Spawn Great War organization (1400pt)",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnExample()
        {
            Map map = Find.CurrentMap;
            Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named("HD_HelodCivilLowFaction"));
            if (faction == null) return;
            var parms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat, faction = faction, tile = map.Tile,
                points = 1400f, raidStrategy = DefDatabase<RaidStrategyDef>.GetNamed("ImmediateAttack")
            };
            var pawns = OrganizationGenerator.Generate(parms, out var organization);
            if (pawns.Count == 0) return;
            IntVec3 origin = CellFinder.RandomEdgeCell(map);
            foreach (Pawn pawn in pawns)
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(origin, map, 12), map);
            // The normal game's Lord handles combat. Organization never issues tactical duties.
            LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction), map, pawns);
            Find.WindowStack.Add(new Dialog_MessageBox(Describe(organization)));
        }
    }
}
