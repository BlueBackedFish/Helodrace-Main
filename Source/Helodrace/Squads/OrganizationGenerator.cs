using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Squads
{
    public static class OrganizationGenerator
    {
        private sealed class PlannedMember
        {
            public CombatGroup group;
            public FormationRoleSlot slot;
            public RoleAssignment assignment;
        }

        public static bool AppliesTo(PawnGroupMakerParms parms)
        {
            return parms != null && parms.groupKind == PawnGroupKindDefOf.Combat
                && parms.raidStrategy != null && !parms.inhabitants
                && parms.faction?.def.GetModExtension<FactionOrganizationExtension>()?.doctrine != null;
        }

        public static DoctrineDef DoctrineFor(PawnGroupMakerParms parms) => parms.faction.def
            .GetModExtension<FactionOrganizationExtension>().doctrine;

        public static IEnumerable<PawnKindDef> KindsFor(FormationDef formation)
        {
            foreach (FormationRoleSlot slot in formation.Slots)
                for (int i = 0; i < slot.count; i++) yield return slot.pawnKind;
            foreach (ChildFormationSlot child in formation.childFormations)
                for (int i = 0; i < child.count; i++)
                    foreach (PawnKindDef kind in KindsFor(child.formation)) yield return kind;
        }

        public static List<Pawn> Generate(PawnGroupMakerParms parms, out CombatOrganization organization)
        {
            DoctrineDef doctrine = DoctrineFor(parms);
            FormationPlan plan = FormationPlanner.Plan(parms.points, doctrine);
            var pawns = new List<Pawn>();
            organization = null;
            if (plan.roots.Count == 0) return pawns;
            GameComponent_CombatOrganizations registry = OrganizationAPI.Registry;
            if (registry == null) throw new InvalidOperationException("No combat organization registry.");
            var result = new CombatOrganization { id = registry.AllocateId(), faction = parms.faction, doctrine = doctrine };
            result.SetBudget(plan);
            var members = new List<PlannedMember>();
            int sequence = 0;
            foreach (FormationDef root in plan.roots)
                result.rootGroups.Add(PlanGroup(result, null, root, members, ref sequence));
            result.RestoreTreeLinks();

            if (parms.seed.HasValue) Rand.PushState(parms.seed.Value);
            try
            {
                // The complete tree and role slots exist BEFORE generating any pawns.
                foreach (PlannedMember member in members)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(member.slot.pawnKind,
                        parms.faction, PawnGenerationContext.NonPlayer, parms.tile,
                        forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                        mustBeCapableOfViolence: true, forceAddFreeWarmLayerIfNeeded: false,
                        allowFood: parms.raidStrategy.pawnsCanBringFood, inhabitant: false,
                        fixedIdeo: parms.ideo,
                        developmentalStages: parms.raidAgeRestriction?.developmentStage ?? DevelopmentalStage.Adult,
                        biologicalAgeRange: parms.raidAgeRestriction?.ageRange));
                    pawns.Add(pawn);
                    if (pawn.kindDef.defName == "HD_MW_HelodRifleman")
                    {
                        // Vanilla can add unrelated utility apparel and inventory gear.
                        // Keep the explicit uniform/armor; slot equipment is added below.
                        foreach (Apparel apparel in pawn.apparel.WornApparel.ToList())
                            if (!pawn.kindDef.apparelRequired.Contains(apparel.def))
                            {
                                pawn.apparel.Remove(apparel);
                                apparel.Destroy(DestroyMode.Vanish);
                            }
                        foreach (Thing item in pawn.inventory.innerContainer.ToList())
                            if (item.def.ingestible == null)
                            {
                                pawn.inventory.innerContainer.Remove(item);
                                item.Destroy(DestroyMode.Vanish);
                            }
                    }
                    if (member.slot.weaponPreset != null)
                        Helodrace.ModernWar.Patch_PawnGenerator_ModularLoadout.ApplyWeapon(pawn,
                            new Helodrace.ModernWar.ModularPawnKindLoadout
                            { weaponPreset = member.slot.weaponPreset });
                    if (parms.faction?.def?.defName == "HD_HelodCivilHighFaction")
                        foreach (string training in new[] { "HD_CQBTraining", "HD_TCCCTraining" })
                        {
                            HediffDef trainingDef = DefDatabase<HediffDef>.GetNamedSilentFail(training);
                            if (trainingDef != null && !pawn.health.hediffSet.HasHediff(trainingDef))
                                pawn.health.AddHediff(HediffMaker.MakeHediff(trainingDef, pawn));
                        }
                    foreach (ThingDef grenadeDef in member.slot.grenadeLoadout.Concat(member.slot.inventoryLoadout))
                    {
                        Thing grenade = ThingMaker.MakeThing(grenadeDef);
                        if (pawn.inventory?.innerContainer == null
                            || !pawn.inventory.innerContainer.TryAdd(grenade))
                            grenade.Destroy(DestroyMode.Vanish);
                    }
                    foreach (ThingDef apparelDef in member.slot.apparelLoadout)
                    {
                        Apparel apparel = ThingMaker.MakeThing(apparelDef) as Apparel;
                        if (apparel == null) continue;
                        if (pawn.apparel != null)
                            pawn.apparel.Wear(apparel, false);
                        else apparel.Destroy(DestroyMode.Vanish);
                    }
                    member.assignment.pawn = pawn;
                }
                EnsureSmokeCarriers(result);
                foreach (CombatGroup root in result.rootGroups) root.InitializeCommand();
                if (parms.forceOneDowned && pawns.Count > 0)
                    HealthUtility.DamageUntilDowned(pawns[0]);
                registry.Register(result);
                organization = result;
                return pawns;
            }
            catch
            {
                foreach (Pawn pawn in pawns) pawn.Destroy(DestroyMode.Vanish);
                throw;
            }
            finally
            {
                if (parms.seed.HasValue) Rand.PopState();
            }
        }

        private static void EnsureSmokeCarriers(CombatOrganization organization)
        {
            ThingDef smoke = DefDatabase<ThingDef>.GetNamed("HD_Grenade_M8_Item");
            foreach (CombatGroup root in organization.rootGroups)
            {
                List<Pawn> members = root.AllMembers.ToList();
                foreach (Pawn pawn in members.OrderBy(member =>
                    member.inventory.innerContainer.Any(item => item.def == smoke) ? 0 : 1)
                    .Take(RaidSmokePolicy.CarrierCount(members.Count)))
                {
                    int count = pawn.inventory.innerContainer.Where(item => item.def == smoke)
                        .Sum(item => item.stackCount);
                    if (count >= RaidSmokePolicy.GrenadesPerCarrier) continue;
                    Thing stock = ThingMaker.MakeThing(smoke);
                    stock.stackCount = RaidSmokePolicy.GrenadesPerCarrier - count;
                    if (!pawn.inventory.innerContainer.TryAdd(stock)) stock.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static CombatGroup PlanGroup(CombatOrganization organization, CombatGroup parent,
            FormationDef formation, List<PlannedMember> members, ref int sequence)
        {
            var group = new CombatGroup
            {
                id = organization.id + "_Group_" + ++sequence,
                name = formation.LabelCap + " " + sequence,
                formation = formation, Parent = parent, Organization = organization,
                parentGroupId = parent?.id
            };
            foreach (FormationRoleSlot slot in formation.Slots)
                for (int i = 0; i < slot.count; i++)
                {
                    var assignment = new RoleAssignment
                    {
                        combatRole = slot.combatRole, commandRole = slot.commandRole,
                        explicitSuccessionOrder = slot.explicitSuccessionOrder < 0 ? -1
                            : slot.explicitSuccessionOrder + i,
                        rankPriority = slot.rankPriority,
                        commandQualifications = new List<RoleDef>(slot.commandQualifications)
                    };
                    if (slot.commandRole != null && !assignment.commandQualifications.Contains(slot.commandRole))
                        assignment.commandQualifications.Add(slot.commandRole);
                    group.roleAssignments.Add(assignment);
                    members.Add(new PlannedMember { group = group, slot = slot, assignment = assignment });
                }
            foreach (ChildFormationSlot child in formation.childFormations)
                for (int i = 0; i < child.count; i++)
                {
                    CombatGroup childGroup = PlanGroup(organization, group, child.formation, members, ref sequence);
                    childGroup.parentSuccessionPriority = child.successionPriority + i;
                    group.children.Add(childGroup);
                }
            return group;
        }
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.GeneratePawns))]
    public static class Patch_PawnGroupMaker_Organization
    {
        public static bool Prefix(PawnGroupMakerParms parms, ref IEnumerable<Pawn> __result)
        {
            if (!OrganizationGenerator.AppliesTo(parms)) return true;
            __result = OrganizationGenerator.Generate(parms, out _);
            return false;
        }
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.GeneratePawnKindsExample))]
    public static class Patch_PawnGroupMaker_OrganizationPreview
    {
        public static bool Prefix(PawnGroupMakerParms parms, ref IEnumerable<PawnKindDef> __result)
        {
            if (!OrganizationGenerator.AppliesTo(parms)) return true;
            FormationPlan plan = FormationPlanner.Plan(parms.points, OrganizationGenerator.DoctrineFor(parms));
            __result = plan.roots.SelectMany(OrganizationGenerator.KindsFor).ToList();
            return false;
        }
    }
}
