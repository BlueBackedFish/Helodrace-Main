using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Squads
{
    public static class OrganizationMedical
    {
        public const string BagDefName = "HD_Apparel_GreatWarMedibag";
        public static RoleAssignment Select(CombatGroup squad, Func<RoleAssignment, bool> eligible) =>
            squad.AllAssignments.Where(a => a.commandRole == null && eligible(a))
                .OrderBy(a => a.combatRole?.combatFunction == "Rifle" ? 0 : 1)
                .ThenBy(a => a.combatRole?.combatFunction == "HAR" || a.combatRole?.combatFunction == "MachineGun" ? 1 : 0)
                .FirstOrDefault();

        public static bool HasDuty(Pawn pawn) => OrganizationAPI.GetGroup(pawn)?.roleAssignments
            .Any(a => a.pawn == pawn && a.medicalDuty) == true;

        public static void Assign(Pawn pawn)
        {
            CombatGroup group = OrganizationAPI.GetGroup(pawn);
            RoleAssignment assignment = group?.roleAssignments.Find(a => a.pawn == pawn);
            if (assignment != null) assignment.medicalDuty = true;
            AddTraining(pawn);
        }

        private static void AddTraining(Pawn pawn)
        {
            HediffDef training = DefDatabase<HediffDef>.GetNamed("HD_TCCCTraining");
            if (!pawn.health.hediffSet.HasHediff(training)) pawn.health.AddHediff(training);
        }

        internal static void Equip(CombatOrganization organization)
        {
            ThingDef bagDef = DefDatabase<ThingDef>.GetNamed(BagDefName);
            foreach (CombatGroup root in organization.rootGroups)
            foreach (CombatGroup squad in root.DescendantsAndSelf.Where(g => g.formation.unitLevel == "Squad"))
            {
                RoleAssignment selected = Select(squad, a => a.pawn?.apparel != null
                    && ApparelUtility.HasPartsToWear(a.pawn, bagDef)
                    && a.pawn.apparel.WornApparel.All(item => ApparelUtility.CanWearTogether(item.def, bagDef, a.pawn.RaceProps.body)));
                if (selected == null)
                {
                    // Optional support equipment must not destroy an otherwise
                    // valid whole raid when another mod blocks every wearer.
                    Log.Warning("Helodrace: no compatible medic slot in " + squad.formation.defName + "; raid kept, medical bag skipped.");
                    continue;
                }
                Pawn pawn = selected.pawn;
                var bag = (Apparel)ThingMaker.MakeThing(bagDef, GenStuff.DefaultStuffFor(bagDef));
                pawn.apparel.Wear(bag, false);
                if (bag.Wearer != pawn)
                {
                    bag.Destroy(DestroyMode.Vanish);
                    Log.Warning("Helodrace: medic bag could not be worn in " + squad.formation.defName + "; raid kept, medical bag skipped.");
                    continue;
                }
                selected.medicalDuty = true; AddTraining(pawn);
                CompMedibag comp = bag.TryGetComp<CompMedibag>();
                ThingDef supplyDef = comp.Props.supplyDef ?? ThingDefOf.MedicineHerbal;
                Thing supplies = ThingMaker.MakeThing(supplyDef); supplies.stackCount = 5;
                if (!pawn.inventory.innerContainer.TryAdd(supplies)) supplies.Destroy(DestroyMode.Vanish);
                else
                {
                    Thing actual = pawn.inventory.innerContainer.FirstOrDefault(item => item.def == supplyDef);
                    while (comp.TryLoadInventorySupply(actual)) { }
                }
            }
        }
    }
}

