using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RimWorld;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private static bool FixedNoRepair(ref bool __result){__result=false;return false;}

    private static MinifiedThing FixedPacked(Map map,Building building)=>map.listerThings
        .ThingsInGroup(ThingRequestGroup.MinifiedThing).OfType<MinifiedThing>().Single(t=>t.InnerThing==building);

    private void FixedMount(Building_Crows station,CrowsWeaponDef weapon)
    {
        Check(station.RequestWeapon(weapon),"Request CROWS mount: "+weapon.defName);
        foreach(var cost in weapon.costList)
        {
            var input=ThingMaker.MakeThing(cost.thingDef);input.stackCount=cost.count;
            Check(station.MountingMaterials.TryAdd(input),"Stage mounting input: "+cost.thingDef.defName);
        }
        Check(station.AddMountingWork(weapon.workToInstall)&&station.InstalledWeapon==weapon,
            "Complete actual CROWS mounting: "+weapon.defName);
    }

    private IEnumerable<int> FixedMove(Pawn worker,Building_TurretGun building,float expectedMass)
    {
        string name=building.def.defName;
        worker.workSettings.SetPriority(WorkTypeDefOf.Construction,1);
        var fuel=building.GetComp<CompRefuelable>();
        if(fuel!=null){fuel.ConsumeFuel(fuel.Fuel);fuel.Refuel(37);}
        float fuelBefore=fuel?.Fuel??0;
        var loader=building.GunCompEq?.parent.TryGetComp<CompChangeableProjectile>();
        ThingDef shell=loader?.parent.def.building?.fixedStorageSettings?.filter.AnyAllowedDef;
        if(shell!=null)loader.LoadShell(shell,1);
        Thing originalGun=building.GunCompEq?.parent;
        int hp=building.HitPoints=building.MaxHitPoints-17;
        var mount=(building as Building_Crows)?.InstalledWeapon;
        Check(Math.Abs(building.GetStatValue(StatDefOf.Mass,true,0)-expectedMass)<.01f,"Resolved unloaded assembly mass: "+name+" = "+expectedMass);
        var uninstall=new Designator_Uninstall();
        Check(uninstall.CanDesignateThing(building).Accepted,"Uninstall command accepts "+name);
        uninstall.DesignateThing(building);
        var giver=(WorkGiver_Uninstall)DefDatabase<WorkGiverDef>.AllDefsListForReading.Single(d=>d.giverClass==typeof(WorkGiver_Uninstall)).Worker;
        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        var job=giver.JobOnThing(worker,building,true);
        Check(job!=null&&worker.jobs.TryTakeOrderedJob(job),"Native uninstall work starts: "+name);
        int elapsed=0;
        while(building.Spawned&&elapsed<30000){yield return 120;elapsed+=120;}
        Check(!building.Spawned&&!building.Destroyed,"Real work packs the existing building: "+name);
        var packed=FixedPacked(playMap,building);
        Check(Math.Abs(packed.GetStatValue(StatDefOf.Mass,true,0)-expectedMass)<.01f,"Packed caravan mass matches weapon and mount: "+name);
        Check(packed.InnerThing.HitPoints==hp&&building.GunCompEq?.parent==originalGun,"Packing preserves durability and actual turret gun: "+name);
        Check(fuel==null||Math.Abs(fuel.Fuel-fuelBefore)<.001f,"Packing preserves stored ammunition fuel: "+name);
        Check(shell==null||loader.Loaded&&loader.LoadedShell==shell&&loader.loadedCount==1,"Packing preserves loaded shell: "+name);
        if(building is Building_Crows crows)Check(crows.Computer==null&&crows.InstalledWeapon==mount,"Packing disconnects remote operation and preserves CROWS armament");
        var destination=playMap.Center+new IntVec3(10,0,-18);
        var blueprint=GenConstruct.PlaceBlueprintForInstall(packed,destination,playMap,Rot4.East,Faction.OfPlayer);
        var installer=(WorkGiver_ConstructDeliverResourcesToBlueprints)DefDatabase<WorkGiverDef>.AllDefsListForReading
            .First(d=>d.giverClass==typeof(WorkGiver_ConstructDeliverResourcesToBlueprints)).Worker;
        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        job=installer.JobOnThing(worker,blueprint,true);
        Check(job!=null&&worker.jobs.TryTakeOrderedJob(job),"Native packed-item hauling and installation start: "+name);
        elapsed=0;while(!building.Spawned&&elapsed<30000){yield return 120;elapsed+=120;}
        Check(building.Spawned&&building.Position==destination&&building.Rotation==Rot4.East&&blueprint.Destroyed,
            "Reinstallation relocates and rotates the same building: "+name);
        Check(building.HitPoints==hp&&building.GunCompEq?.parent==originalGun,"Reinstallation preserves durability and actual turret gun: "+name+" hp="+building.HitPoints+" expected="+hp+" gun="+building.GunCompEq?.parent.ThingID+" expectedGun="+originalGun?.ThingID);
        Check(fuel==null||Math.Abs(fuel.Fuel-fuelBefore)<.001f,"Reinstallation preserves stored ammunition fuel: "+name);
        Check(shell==null||loader.Loaded&&loader.LoadedShell==shell&&loader.loadedCount==1,"Reinstallation preserves loaded shell: "+name);
        Check(Math.Abs(building.GetStatValue(StatDefOf.Mass,true,0)-expectedMass)<.01f,"Reinstalled mass remains correct: "+name);
        Check(!playMap.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing).OfType<MinifiedThing>().Any(t=>t.InnerThing==building),
            "Reinstallation leaves no duplicate packed weapon: "+name);
        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        building.Destroy(DestroyMode.Vanish);
    }

    private IEnumerable<int> FixedRelocationChecks(Pawn worker)
    {
        var probe=new Harmony("helodrace.tests.fixed-relocation");
        // Keep construction enabled while preventing unrelated automatic durability repair.
        probe.Patch(AccessTools.Method(typeof(WorkGiver_Repair),nameof(WorkGiver_Repair.HasJobOnThing)),
            prefix:new HarmonyMethod(typeof(Checks),nameof(FixedNoRepair)));
        foreach(var work in DefDatabase<WorkTypeDef>.AllDefsListForReading)worker.workSettings.SetPriority(work,0);
        worker.workSettings.SetPriority(WorkTypeDefOf.Construction,1);
        GenCommandLine.TryGetCommandLineArg("hdFixedWeaponData",out string path);
        using var rows=JsonDocument.Parse(File.ReadAllText(path));
        var names=rows.RootElement.EnumerateArray().Select(r=>r.GetProperty("defName").GetString()).ToHashSet();
        Check(names.SetEquals(DefDatabase<ThingDef>.AllDefsListForReading.Where(d=>d.defName.StartsWith("HD_")&&d.building?.turretGunDef!=null).Select(d=>d.defName)),
            "All eleven authored fixed weapon buildings are covered");
        var computer=(Building_CrowsComputer)ItemBuilding("HD_CrowsComputer",-18,-16);
        foreach(var row in rows.RootElement.EnumerateArray())
        {
            string name=row.GetProperty("defName").GetString();
            var def=Def<ThingDef>(name);float mass=row.GetProperty("mass").GetSingle();
            Check(def.Minifiable&&def.thingCategories.Contains(Def<ThingCategoryDef>("BuildingsSecurity")),"Security storage and minification resolve: "+name);
            var building=(Building_TurretGun)ItemBuilding(name,0,-18);
            if(building is Building_Crows station)
            {
                Check(Math.Abs(station.GetStatValue(StatDefOf.Mass,true,0)-mass)<.01f,"Empty CROWS has no mounted weapon mass");
                var weapon=Def<CrowsWeaponDef>("HD_CrowsWeapon_M2HB");FixedMount(station,weapon);mass+=weapon.weaponMass;
                computer.Connect(station);Check(computer.Connected,"CROWS is connected before packing");
            }
            foreach(int ticks in FixedMove(worker,building,mass))yield return ticks;
        }
        foreach(var weapon in DefDatabase<CrowsWeaponDef>.AllDefsListForReading.Where(w=>w.defName!="HD_CrowsWeapon_M2HB"))
        {
            var station=(Building_Crows)ItemBuilding("HD_Building_CrowsII",0,-18);FixedMount(station,weapon);
            foreach(int ticks in FixedMove(worker,station,145.5f+weapon.weaponMass))yield return ticks;
        }
        probe.UnpatchAll(probe.Id);
    }
}
