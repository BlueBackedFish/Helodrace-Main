using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System;
using Helodrace.ModernWar;
using RimWorld;
using Verse;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private void WorktableCatalog()
    {
        var rows=DefDatabase<RecipeDef>.AllDefsListForReading
            .Where(r=>r.ProducedThingDef?.defName.StartsWith("HD_")==true)
            .Select(r=>new {
                recipe=r.defName,product=r.ProducedThingDef.defName,
                users=r.AllRecipeUsers.Select(u=>u.defName).Distinct().OrderBy(n=>n).ToArray(),
                weapon=r.ProducedThingDef.IsWeapon,
                apparel=r.ProducedThingDef.apparel!=null,
                weaponPart=r.ProducedThingDef.GetCompProperties<CompProperties_ModularWeaponNode>()!=null,
                armorPlate=r.ProducedThingDef.GetCompProperties<CompProperties_ArmorPlateSet>()!=null,
                workType=r.requiredGiverWorkType?.defName
            }).OrderBy(r=>r.recipe).ToArray();
        File.WriteAllText(Path.Combine(output,"catalog.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Check(rows.Length>100,"Captured all resolved Helod production worktables before migration: "+rows.Length);
    }

    private Building_WorkTable ItemClothingBench(Pawn worker)
    {
        worker.workSettings.SetPriority(Def<WorkTypeDef>("Tailoring"),1);
        var bench=(Building_WorkTable)ItemBuilding("HD_ClothingBench",-12,-10);
        var press=ItemBuilding("HD_ElectricHydraulicPress",-12,-6);
        for(int z=-2;z>=-6;z--)ItemBuilding("PowerConduit",-12,z);
        foreach(var cell in press.OccupiedRect())playMap.roofGrid.SetRoof(cell,RoofDefOf.RoofConstructed);
        playMap.powerNetManager.UpdatePowerNetsAndConnections_First();
        return bench;
    }

    private IEnumerable<int> WorktableChecks(Pawn worker)
    {
        GenCommandLine.TryGetCommandLineArg("hdWorktableData",out string folder);
        using var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"routing.json")));
        foreach(var row in data.RootElement.EnumerateArray())
        {
            var recipe=Def<RecipeDef>(row.GetProperty("recipe").GetString());
            var expected=row.GetProperty("after").EnumerateArray().Select(x=>x.GetString()).OrderBy(x=>x).ToArray();
            Check(recipe.AllRecipeUsers.Select(t=>t.defName).Distinct().OrderBy(n=>n).SequenceEqual(expected),"Exact dedicated and retained basic worktables: "+recipe.defName);
            Check(recipe.recipeUsers.Select(t=>t.defName).Distinct().Count()==recipe.recipeUsers.Count,"No duplicate recipe users: "+recipe.defName);
            foreach(string table in expected)Check(Def<ThingDef>(table).AllRecipes.Contains(recipe),"Actual worktable catalog contains "+recipe.defName+" on "+table);
            var product=recipe.ProducedThingDef;
            var entry=product.SpecialDisplayStats(StatRequest.For(product,null)).Single(e=>e.LabelCap=="HD_Production_RequiredFacilities".Translate().Resolve());
            bool press=row.GetProperty("requiresPress").GetBoolean();
            Check(!string.IsNullOrEmpty(entry.ValueString)&&(!press||entry.ValueString.Contains(Def<Helodrace.FacilityFieldDef>("HD_Pressing").LabelCap)),"Separate information entry matches production requirements: "+product.defName);
        }
        using var before=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"before.json")));
        foreach(var row in before.RootElement.EnumerateArray().Where(r=>r.GetProperty("users").EnumerateArray().Any(x=>x.GetString()=="HD_BasicWorkbench")))
            Check(Def<RecipeDef>(row.GetProperty("recipe").GetString()).AllRecipeUsers.Any(t=>t.defName=="HD_BasicWorkbench"),"Every previous basic-workbench recipe remains: "+row.GetProperty("recipe").GetString());

        var clothing=ItemClothingBench(worker);yield return 1200;
        var pressThing=(Building_WorkTable)playMap.listerThings.ThingsOfDef(Def<ThingDef>("HD_ElectricHydraulicPress")).Single();
        var sapi=Def<RecipeDef>("Make_HD_ArmorPlate_SAPI");
        Check(sapi.Worker.AvailableOnNow(clothing),"Powered electric hydraulic press unlocks armor production");
        pressThing.GetComp<CompFlickable>().SwitchIsOn=false;
        Check(!sapi.Worker.AvailableOnNow(clothing),"Switching off the press blocks new armor jobs");
        Check(Def<RecipeDef>("Make_HD_Apparel_UCPPants").Worker.AvailableOnNow(clothing),"Ordinary tailoring needs no armor press");
        pressThing.GetComp<CompFlickable>().SwitchIsOn=true;
        pressThing.GetComp<CompPowerTrader>().PowerOn=false;
        Check(!sapi.Worker.AvailableOnNow(clothing),"Loss of press electricity blocks armor production");
        playMap.powerNetManager.UpdatePowerNetsAndConnections_First();yield return 600;
        bool interrupted=false,load=false;
        foreach(int ticks in ItemBill(worker,clothing,sapi.defName))
        {
            var power=pressThing.GetComp<Helodrace.CompIndustrialWorktablePower>();
            if(power.IsWorking&&!interrupted)
            {
                Check(Math.Abs(pressThing.GetComp<CompPowerTrader>().PowerOutput+1800)<.01,"Armor work drives the press at its real 1800W active load");load=true;
                pressThing.GetComp<CompFlickable>().SwitchIsOn=false;yield return 120;
                Check(worker.CurJobDef!=JobDefOf.DoBill&&!clothing.BillStack.Bills.Single().ShouldDoNow(),"Press loss interrupts an actual armor job and stops its bill");
                pressThing.GetComp<CompFlickable>().SwitchIsOn=true;yield return 600;interrupted=true;
            }
            yield return ticks;
        }
        Check(interrupted&&load,"Actual armor production exercised press load, interruption and recovery");
        foreach(string name in new[]{"HD_Apparel_IBTVAssault","HD_Apparel_FASTMT","HD_Apparel_GreatWarM1Helmet","HD_Apparel_ShieldIronHideRiot"})
            foreach(int ticks in ItemBill(worker,clothing,"Make_"+name))yield return ticks;

        // The retained early armor recipe uses the same facility rule at a basic bench.
        clothing.DeSpawn();var basic=(Building_WorkTable)ItemBuilding("HD_BasicWorkbench",-12,-10);yield return 600;
        foreach(int ticks in ItemBill(worker,basic,"Make_HD_Apparel_M1952AFlakJacket"))yield return ticks;
        basic.Destroy(DestroyMode.Vanish);GenSpawn.Spawn(clothing,playMap.Center+new IntVec3(-12,0,-10),playMap);yield return 600;
        pressThing.DeSpawn();Check(!sapi.Worker.AvailableOnNow(clothing),"Removing a press revokes armor production access");
        var mechanicalPress=ItemBuilding("HD_LineShaftHydraulicPress",-12,-6);
        var motor=ItemBuilding("HD_EarlyIndustrialLargeMotor",-18,-6);
        for(int x=-16;x<=-14;x++)ItemBuilding("HD_LineShaft",x,-6);
        for(int x=-30;x<=-18;x+=3){ItemBuilding("VanometricPowerCell",x,-12);ItemBuilding("VanometricPowerCell",x,-15);}
        for(int z=-2;z>=-15;z--)ItemBuilding("PowerConduit",-30,z);
        for(int x=-30;x<=-12;x++){ItemBuilding("PowerConduit",x,-12);ItemBuilding("PowerConduit",x,-15);}
        for(int z=-2;z>=-6;z--)ItemBuilding("PowerConduit",-18,z);
        playMap.powerNetManager.UpdatePowerNetsAndConnections_First();yield return 1200;
        var user=mechanicalPress.GetComp<Helodrace.CompMechanicalUser>();
        var helmet=Def<RecipeDef>("Make_HD_Apparel_GreatWarM1Helmet");
        Check(user.HasPower&&user.RealRPM>=user.Props.minimalRPM&&helmet.Worker.AvailableOnNow(clothing),"Real powered shaft network makes the early press meet pressing Lv.1");
        Check(!sapi.Worker.AvailableOnNow(clothing),"Pressing Lv.1 press does not meet pressing Lv.2 modern armor");
        motor.GetComp<CompFlickable>().SwitchIsOn=false;yield return 600;
        Check(!helmet.Worker.AvailableOnNow(clothing),"Mechanical motor shutdown disables the early press");
        motor.GetComp<CompFlickable>().SwitchIsOn=true;yield return 1200;
        bool mechanicalLoad=false;
        foreach(int ticks in ItemBill(worker,clothing,"Make_HD_Apparel_GreatWarM1Helmet"))
        {if(user.IsWorking&&Math.Abs(user.GridPowerDemanded-450)<.01)mechanicalLoad=true;yield return ticks;}
        Check(mechanicalLoad,"Armor work drives the early press at its real 450W shaft load");

        var gunBench=(Building_WorkTable)ItemBuilding("HD_GunSmithTable",20,-9);
        var treadle=ItemBuilding("HD_TreadleLathe",23,-12);yield return 60;
        string Links()=>string.Join(",",gunBench.GetComp<CompAffectedByFacilities>().LinkedFacilitiesListForReading.Select(t=>t.def.defName));
        Check(Def<RecipeDef>("HD_Make_Gun_ColtSAA_Weapon").Worker.AvailableOnNow(gunBench),"Turning Lv.1 lathe meets breech-loading revolver requirements; linked="+Links());
        Check(!Def<RecipeDef>("HD_Make_Gun_Henry_Weapon").Worker.AvailableOnNow(gunBench),"Turning Lv.1 alone does not meet turning Lv.2 + milling Lv.1");
        var turret=ItemBuilding("HD_LineShaftTurretLathe",16,-12);var mill=ItemBuilding("HD_LineShaftMillingMachine",19,-12);yield return 60;
        Check(Def<RecipeDef>("HD_Make_Gun_Henry_Weapon").Worker.AvailableOnNow(gunBench),"Separate turret lathe and mill together meet turning Lv.2 + milling Lv.1; linked="+Links());
        Check(!Def<RecipeDef>("Make_HD_Gun_M16A1_Weapon").Worker.AvailableOnNow(gunBench),"Line-shaft machines do not meet NC-level rifle requirements");
        treadle.Destroy(DestroyMode.Vanish);turret.Destroy(DestroyMode.Vanish);mill.Destroy(DestroyMode.Vanish);
        var tools=ItemBuilding("HD_MultiTaskMachine",18,-5);
        for(int z=-2;z>=-5;z--)ItemBuilding("PowerConduit",18,z);
        playMap.powerNetManager.UpdatePowerNetsAndConnections_First();yield return 1200;
        foreach(string name in new[]{"Make_HD_ExpandableBaton","Make_HD_Gun_P320_Weapon","Make_HD_Gun_MP5_Weapon"})
            foreach(int ticks in ItemBill(worker,gunBench,name,180000))yield return ticks;
    }
}
