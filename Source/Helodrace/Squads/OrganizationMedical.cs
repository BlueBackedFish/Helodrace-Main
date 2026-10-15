using System;
using System.Linq;
using RimWorld;
using Verse;
using Helodrace.ModernWar;
using Helodrace.Tactical;

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

        public static bool AdvancedSlot(RoleAssignment assignment) => assignment.commandRole==null
            && assignment.combatRole?.combatFunction=="Rifle"
            && assignment.pawn!=null && CompSledgehammerBreach.WornBy(assignment.pawn)==null
            && assignment.pawn.inventory?.innerContainer.Any(t=>t.def.defName=="HD_C4_Charge")!=true
            && assignment.pawn.equipment?.Primary?.TryGetComp<CompPowerCutterBreach>()==null;

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
            HediffDef training = DefDatabase<HediffDef>.GetNamed("HD_TCCCTraining_LOW");
            if (!Helodrace.Tactical.TacticalTraining.HasTcccLow(pawn)) pawn.health.AddHediff(training);
        }

        internal static void Equip(CombatOrganization organization)
        {
            ThingDef bagDef = DefDatabase<ThingDef>.GetNamed(BagDefName);
            foreach (CombatGroup root in organization.rootGroups)
            foreach (CombatGroup squad in root.DescendantsAndSelf.Where(g => g.formation.unitLevel ==
                (organization.faction.def.defName=="HD_HelodCivilHighFaction"?"Team":"Squad")))
            {
                bool high=organization.faction.def.defName=="HD_HelodCivilHighFaction";
                RoleAssignment selected = Select(squad, a => (!high || AdvancedSlot(a)) && a.pawn?.apparel != null
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
                if(high)
                {
                    selected.medicalDuty=true;
                    pawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed("HD_TCCCTraining_HIGH"));
                    EquipAdvancedPouch(pawn);
                }
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

        private static void EquipAdvancedPouch(Pawn pawn)
        {
            var armor=pawn.apparel.WornApparel.Select(a=>a.TryGetComp<CompModularArmor>())
                .FirstOrDefault(a=>a?.Props.armorTags?.Contains("HD_IOTVGen4")==true);
            if(armor==null) return;
            var panel=DefDatabase<ModularArmorPalsPanelDef>.GetNamed("HD_IOTV_PALS_Lower");
            foreach(var part in armor.InstalledParts.Where(p=>p.IsPalsMounted && p.palsPanel==panel).ToArray())
            {
                Thing replaced=part.InstalledItem;
                if(!armor.RemovePalsPart(part)) return;
                if(replaced!=null && pawn.inventory.innerContainer.Contains(replaced))
                { pawn.inventory.innerContainer.Remove(replaced); replaced.Destroy(); }
            }
            var partDef=DefDatabase<ModularArmorPartDef>.GetNamed("HD_IOTVPart_MediL");
            Thing item=ThingMaker.MakeThing(partDef.RequiredThingDef);
            if(armor.InstallPalsPart(partDef,panel,0,0,item)==null) { item.Destroy(); return; }
            var pouch=item.TryGetComp<CompMedicalPouch>();
            ThingDef agent=DefDatabase<ThingDef>.GetNamed("HD_TCCC_HemostaticAgent");
            for(int i=0;i<pouch.HemostaticCapacity;i++)
            {
                Thing dose=ThingMaker.MakeThing(agent);
                if(!pawn.inventory.innerContainer.TryAdd(dose)) { dose.Destroy(); break; }
                Thing stock=pawn.inventory.innerContainer.First(t=>t.def==agent);
                if(!pouch.TryLoad(pawn,stock)) break;
            }
        }
    }
}

