using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Helodrace.ModernWar;
using RimWorld;
using Verse;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private static bool VanillaEquipmentComponent(ThingDef def) =>
        def.defName=="ComponentIndustrial"||def.defName=="ComponentSpacer";

    private IEnumerable<int> EquipmentMaterialChecks(Pawn worker, Building_WorkTable machining)
    {
        machining=ItemClothingBench(worker);yield return 1200;
        GenCommandLine.TryGetCommandLineArg("hdEquipmentMaterialsData",out string path);
        using var data=JsonDocument.Parse(File.ReadAllText(path));
        foreach(var row in data.RootElement.EnumerateArray())
        {
            if(row.GetProperty("abstract").GetBoolean())continue;
            string name=row.GetProperty("defName").GetString();
            var expected=row.GetProperty("after").EnumerateObject()
                .Select(p=>(material:p.Name,amount:p.Value.GetInt32())).ToArray();
            if(row.GetProperty("kind").GetString()=="RecipeDef")
            {
                var recipe=Def<RecipeDef>(name);
                CheckEquipmentRecipe(recipe,expected);
                continue;
            }
            var def=Def<ThingDef>(name);
            Check(def.costList.Count==expected.Length&&expected.All(e=>def.costList.Any(c=>c.thingDef.defName==e.material&&c.count==e.amount)),"Resolved equipment/construction cost uses the exact dedicated materials: "+name);
            if(def.category==ThingCategory.Building)continue;
            var make=DefDatabase<RecipeDef>.GetNamedSilentFail("Make_"+name);
            if(row.GetProperty("recipeDisabled").GetBoolean())
                Check(make==null,"Assembly-only weapon stays without an automatic direct recipe: "+name);
            else
            {
                Check(make!=null,"Equipment has its generated production recipe: "+name);
                if(make!=null)CheckEquipmentRecipe(make,expected);
            }
        }

        var ownThings=DefDatabase<ThingDef>.AllDefsListForReading.Where(d=>d.defName.StartsWith("HD_")).ToList();
        var remaining=ownThings.Where(d=>d.costList?.Any(c=>VanillaEquipmentComponent(c.thingDef))==true).Select(d=>d.defName);
        Check(!remaining.Any(),"No resolved Helod equipment or building cost uses vanilla components: "+string.Join(",",remaining));
        foreach(var shield in ownThings.Where(d=>d.apparel!=null&&d.defName.Contains("Shield")))
            Check(shield.costList.All(c=>c.thingDef!=ThingDefOf.Plasteel),"Shield has no plasteel: "+shield.defName);

        var forbiddenRecipes=DefDatabase<RecipeDef>.AllDefsListForReading
            .Where(r=>r.defName.StartsWith("HD_")||r.ProducedThingDef?.defName.StartsWith("HD_")==true)
            .Where(r=>r.ProducedThingDef?.defName!="HD_GeneralMachinePart")
            .Where(r=>r.ingredients.Any(i=>i.filter.AllowedThingDefs.Any(VanillaEquipmentComponent)))
            .Select(r=>r.defName);
        Check(!forbiddenRecipes.Any(),"All authored and generated Helod production recipes avoid vanilla component inputs, including inherited gun families: "+string.Join(",",forbiddenRecipes));

        var bench=(Building_WorkTable)ItemBuilding("HD_BasicWorkbench",20,-9);
        var gunsmith=(Building_WorkTable)ItemBuilding("HD_GunSmithTable",12,-16);
        var machine=ItemBuilding("HD_MultiTaskMachine",18,-6);
        for(int z=-2;z>=-16;z--)ItemBuilding("PowerConduit",12,z);
        for(int z=-2;z>=-6;z--)ItemBuilding("PowerConduit",18,z);
        foreach(var cell in machine.OccupiedRect())playMap.roofGrid.SetRoof(cell,RoofDefOf.RoofConstructed);
        playMap.powerNetManager.UpdatePowerNetsAndConnections_First();yield return 1200;
        var facilities=bench.GetComp<CompAffectedByFacilities>();
        Check(machine.GetComp<CompPowerTrader>().PowerOn&&facilities.LinkedFacilitiesListForReading.Contains(machine)&&facilities.IsFacilityActive(machine),"Real powered multitask machine supplies the basic bench's lathe and milling requirements");
        foreach(string name in new[]{"HD_Apparel_ShieldDefenTechIIIA","HD_Apparel_ShieldIronHideIV","HD_Apparel_IBTVAssault","HD_ArmorPlate_SAPI","HD_ModularPart_GPNVG18_Part"})
            foreach(int ticks in ItemBill(worker,machining,"Make_"+name))yield return ticks;
        foreach(string name in new[]{"Make_HD_Apparel_SCR300_Radio","Make_HD_Gun_M4A1_Weapon","Make_HD_Gun_MP5_Weapon","HD_Make_Gun_M1903A4_Weapon","HD_Make_Gun_M60E3_Weapon"})
            foreach(int ticks in ItemBill(worker,bench,name,180000))yield return ticks;
        foreach(int ticks in ItemBill(worker,gunsmith,"Make_HD_ModularWeaponPartsBox"))yield return ticks;
        Check(playMap.listerThings.ThingsOfDef(Def<ThingDef>("HD_Apparel_IBTVAssault")).All(t=>t.TryGetComp<CompModularArmor>().InstalledParts.Count==0),"IBTV crafted with dedicated components remains an empty carrier");
    }

    private void CheckEquipmentRecipe(RecipeDef recipe,(string material,int amount)[] expected)
    {
        Check(recipe.ingredients.Count==expected.Length&&expected.All(e=>recipe.ingredients.Any(i=>i.filter.AllowedDefCount==1&&i.filter.AnyAllowedDef.defName==e.material&&i.GetBaseCount()==e.amount)),"Resolved production inputs match the dedicated material quantities: "+recipe.defName);
        var bill=(Bill_Production)recipe.MakeNewBill();
        // Fixed ingredients are accepted by their own IngredientCount filter;
        // the global fixedIngredientFilter need not also include them in 1.7.
        Check(expected.All(e=>recipe.defaultIngredientFilter.Allows(Def<ThingDef>(e.material))&&bill.ingredientFilter.Allows(Def<ThingDef>(e.material))&&bill.IsFixedOrAllowedIngredient(Def<ThingDef>(e.material))),"Fresh bill permits every dedicated input: "+recipe.defName);
    }
}
