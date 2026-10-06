using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Squads;
using RimWorld;
using Verse;

internal static class RaidSpawnGenerationTests
{
    internal static void Run()
    {
        var binding = AccessTools.Field(typeof(DefOfHelper), "bindingNow");
        bool previousBinding = (bool)binding.GetValue(null);
        binding.SetValue(null, true);
        PawnGroupKindDef previousCombat = PawnGroupKindDefOf.Combat;
        PawnGroupKindDefOf.Combat = new PawnGroupKindDef { defName = "Combat" };
        try
        {
            int checks = 0;
            void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
            var soldier = new PawnKindDef { combatPower = 155f };
            var rifle = new RoleDef();
            var command = new RoleDef { isCommandRole = true, canCommandUnitLevels = new List<string> { "Team", "Squad" } };
            var team = new FormationDef { defName = "SpawnTeam", unitLevel = "Team", commanderRole = command,
                minimumPersonnel = 4, idealPersonnel = 4, maximumPersonnel = 4,
                requiredRoles = new List<FormationRoleSlot> {
                    new FormationRoleSlot { pawnKind = soldier, combatRole = rifle, commandRole = command, equipmentPointCost = 40 },
                    new FormationRoleSlot { pawnKind = soldier, combatRole = rifle, count = 3 } } };
            var squad = new FormationDef { defName = "SpawnSquad", unitLevel = "Squad", commanderRole = command,
                minimumPersonnel = 13, idealPersonnel = 13, maximumPersonnel = 13,
                requiredRoles = new List<FormationRoleSlot> {
                    new FormationRoleSlot { pawnKind = soldier, combatRole = rifle, commandRole = command, equipmentPointCost = 140 } },
                childFormations = new List<ChildFormationSlot> { new ChildFormationSlot { formation = team, count = 3 } } };
            var doctrine = new DoctrineDef { availableFormations = new List<FormationDef> { squad } };
            var faction = new Faction { def = new FactionDef { modExtensions = new List<DefModExtension> {
                new FactionOrganizationExtension { doctrine = doctrine } } } };
            float minimum = -1;
            Check(!Patch_RaidStrategy_OrganizationMinimum.Prefix(faction, PawnGroupKindDefOf.Combat, ref minimum)
                && Math.Abs(minimum - squad.FormationCost / 1.1f) < 0.01f,
                "Native raid minimum includes the full 13-person squad, all three teams and slot equipment.");
            Check(FormationPlanner.Plan(minimum * 1.05f, doctrine).Personnel == 13,
                "The native incident's 1.05 floor produces exactly one complete High squad, never an empty raid or single team.");
            var smaller = new FormationDef { defName = "SpawnSmall", unitLevel = "Team", commanderRole = command,
                minimumPersonnel = 7, idealPersonnel = 7, maximumPersonnel = 7,
                requiredRoles = new List<FormationRoleSlot> {
                    new FormationRoleSlot { pawnKind = new PawnKindDef { combatPower = 80 }, combatRole = rifle,
                        commandRole = command, count = 7 } } };
            doctrine.availableFormations.Add(smaller);
            Check(!Patch_RaidStrategy_OrganizationMinimum.Prefix(faction, PawnGroupKindDefOf.Combat, ref minimum)
                && Math.Abs(minimum - smaller.FormationCost / 1.1f) < 0.01f,
                "Low doctrine chooses the cheapest valid complete formation as its floor.");
            float unchanged = 123;
            Check(Patch_RaidStrategy_OrganizationMinimum.Prefix(faction, new PawnGroupKindDef(), ref unchanged) && unchanged == 123,
                "Trade, visitors and other noncombat groups keep their native minimum.");
            Check(Patch_RaidStrategy_OrganizationMinimum.Prefix(new Faction { def = new FactionDef() }, PawnGroupKindDefOf.Combat, ref unchanged),
                "Factions without organization doctrine keep their native minimum.");
            doctrine.availableFormations.Clear();
            Check(Patch_RaidStrategy_OrganizationMinimum.Prefix(faction, PawnGroupKindDefOf.Combat, ref unchanged),
                "An empty doctrine does not replace native behavior with an invalid floor.");
            Console.WriteLine($"PASS: {checks} actual raid minimum and complete-formation generation checks.");
        }
        finally
        {
            PawnGroupKindDefOf.Combat = previousCombat;
            binding.SetValue(null, previousBinding);
        }
    }
}
