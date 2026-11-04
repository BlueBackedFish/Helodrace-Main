using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private static List<(float x,float z)> supportDrawScales;
    // Actual game DrawMesh boundary. All measurements use .NET vectors.
    private static void SupportRecompile(){}
    private static bool SupportDraw(UnityEngine.Matrix4x4 __1)
    {
        if(supportDrawScales==null)return true;
        supportDrawScales.Add((new Vector3(__1.m00,__1.m10,__1.m20).Length(),
            new Vector3(__1.m02,__1.m12,__1.m22).Length()));
        return false;
    }

    private IEnumerable<int> SupportBuild(Pawn worker,string name,int x,int z)
    {
        var def=Def<ThingDef>(name);
        Check(def.designationCategory?.defName=="HD_HelodTechnology_Build"&&def.blueprintDef!=null&&def.frameDef!=null,
            "Support weapon has a real construction menu, blueprint and frame: "+name);
        Check(new Designator_Build(def).Visible,"Researched support weapon appears in construction menu: "+name);
        var pos=playMap.Center+new IntVec3(x,0,z);
        var costs=def.CostList;
        foreach(var cost in costs)ItemSupply(cost.thingDef,cost.count,pos+new IntVec3(2,0,-2));
        var before=costs.ToDictionary(c=>c.thingDef,c=>ItemCount(c.thingDef));
        var blueprint=GenConstruct.PlaceBlueprintForBuild(def,pos,playMap,Rot4.North,Faction.OfPlayer,null);
        var givers=DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(d=>
            d.giverClass==typeof(WorkGiver_ConstructDeliverResourcesToBlueprints)||
            d.giverClass==typeof(WorkGiver_ConstructDeliverResourcesToFrames)||
            d.giverClass==typeof(WorkGiver_ConstructFinishFrames)).Select(d=>(WorkGiver_Scanner)d.Worker).ToList();
        int ticks=0,jobs=0;
        while(!playMap.listerThings.ThingsOfDef(def).Any(t=>t.Position==pos)&&ticks<120000)
        {
            if(worker.CurJob?.playerForced!=true)
            {
                worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
                var target=pos.GetThingList(playMap).FirstOrDefault(t=>t is Blueprint_Build||t is Frame);
                if(target!=null)
                    foreach(var giver in givers)
                    {
                        var job=giver.JobOnThing(worker,target,true);
                        if(job!=null&&worker.jobs.TryTakeOrderedJob(job)){jobs++;break;}
                    }
            }
            yield return 120;ticks+=120;
        }
        Check(blueprint.Destroyed&&playMap.listerThings.ThingsOfDef(def).Count(t=>t.Position==pos)==1&&jobs>0,
            "Native hauling and frame work construct exactly one "+name+"; jobs="+jobs+" ticks="+ticks);
        foreach(var cost in costs)Check(ItemCount(cost.thingDef)==before[cost.thingDef]-cost.count,
            "Exact construction consumption: "+name+"/"+cost.thingDef.defName);
        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
    }

    private IEnumerable<int> SupportWeaponChecks(Pawn worker)
    {
        foreach(var research in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            Find.ResearchManager.FinishProject(research,doCompletionDialog:false);
        foreach(var work in DefDatabase<WorkTypeDef>.AllDefsListForReading)worker.workSettings.SetPriority(work,0);
        worker.workSettings.SetPriority(WorkTypeDefOf.Construction,1);
        foreach(int ticks in SupportBuild(worker,"HD_Building_M167VADS",4,-16))yield return ticks;
        foreach(int ticks in SupportBuild(worker,"HD_Building_M220TOW",-5,-16))yield return ticks;
        var tow=(Building_TurretGun)playMap.listerThings.ThingsOfDef(Def<ThingDef>("HD_Building_M220TOW")).Single();
        var graphic=tow.GetComp<CompTowLauncherGraphic>();
        Check(((CompProperties_TowLauncherGraphic)graphic.props).drawSize==2f&&tow.def.building.turretTopDrawSize==1.3f,
            "TOW mount is 1.3 tiles while upper launcher remains 2 tiles");
        var loader=tow.GunCompEq.parent.GetComp<CompChangeableProjectile>();
        var harmony=new Harmony("helodrace.tests.support-construction");
        harmony.Patch(AccessTools.Method(typeof(UnityEngine.Graphics),"DrawMesh",new[]{typeof(UnityEngine.Mesh),
            typeof(UnityEngine.Matrix4x4),typeof(UnityEngine.Material),typeof(int)}),prefix:new HarmonyMethod(typeof(Checks),nameof(SupportDraw)));
        // These callers may already inline the engine shim before the probe is installed.
        // Re-JIT their original bodies after patching DrawMesh so the probe observes them.
        foreach(var method in new[]{AccessTools.Method(typeof(TurretTop),nameof(TurretTop.DrawTurret)),
            AccessTools.Method(typeof(CompTowLauncherGraphic),"DrawLayer"),
            AccessTools.Method(typeof(CompTowLauncherGraphic),nameof(CompTowLauncherGraphic.PostDraw))})
            harmony.Patch(method,postfix:new HarmonyMethod(typeof(Checks),nameof(SupportRecompile)));
        try
        {
            foreach(bool loaded in new[]{false,true})
            {
                loader.loadedCount=0;
                if(loaded)loader.LoadShell(Def<ThingDef>("HD_BGM71A_Round"),1);
                foreach(float angle in new[]{0f,45f,90f,180f,270f})
                {
                    tow.Top.CurRotation=angle;
                    supportDrawScales=new();tow.Top.DrawTurret(tow.DrawPos,default,0);
                    Check(supportDrawScales.Count==1&&supportDrawScales.All(v=>Math.Abs(v.x-1.3f)<.0001f&&Math.Abs(v.z-1.3f)<.0001f),
                        "Actual lower mount draw uses 1.3 without distortion; loaded="+loaded+" angle="+angle+" calls="+string.Join(";",supportDrawScales));
                    supportDrawScales.Clear();graphic.PostDraw();
                    Check(supportDrawScales.Count==(loaded?4:2)&&supportDrawScales.All(v=>Math.Abs(v.x-2f)<.0001f&&Math.Abs(v.z-2f)<.0001f),
                        "Upper launcher and missile layers retain scale and load/unload count; loaded="+loaded+" angle="+angle+" calls="+string.Join(";",supportDrawScales));
                    supportDrawScales=null;
                }
            }
        }
        finally{supportDrawScales=null;harmony.UnpatchAll(harmony.Id);}
    }
}
