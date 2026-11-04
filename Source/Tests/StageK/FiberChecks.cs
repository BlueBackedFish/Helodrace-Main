using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private IEnumerable<int> FiberChecks(Pawn worker, Building_WorkTable reactor)
    {
        var nylon=Def<RecipeDef>("HD_SpinNylon");var aramid=Def<RecipeDef>("HD_SpinAramid");
        var module=(Building_WorkTable_FiberSpinningModule)ItemBuilding("HD_FiberSpinningModule",-8,10);
        Check(module.def.Size==new IntVec2(1,1),"Spinning module occupies exactly one tile");
        Check(!module.GetComp<CompAffectedByFacilities>().LinkedFacilitiesListForReading.Contains(reactor)
            &&!nylon.Worker.AvailableOnNow(module),"A detached module cannot spin fiber");
        Check(new[]{nylon,aramid}.All(r=>module.def.AllRecipes.Contains(r)&&!reactor.def.AllRecipes.Contains(r)),"Fiber bills belong to the module, not the reactor");
        Check(new[]{nylon,aramid}.All(r=>r.unfinishedThingDef?.defName=="HD_UnfinishedIndustrialBatch"),"Fiber recipes retain real unfinished work and ingredients");
        module.DeSpawn();GenSpawn.Spawn(module,playMap.Center+new IntVec3(-10,0,3),playMap);
        foreach(var cell in module.OccupiedRect())playMap.roofGrid.SetRoof(cell,RoofDefOf.RoofConstructed);
        playMap.powerNetManager.UpdatePowerNetsAndConnections_First();yield return 1200;
        Check(module.GetComp<CompPowerTrader>().PowerOn&&reactor.GetComp<CompPowerTrader>().PowerOn,"Real electrical network powers both machines");
        Check(module.OperationalReactor==reactor&&module.GetComp<CompAffectedByFacilities>().LinkedFacilitiesListForReading.Contains(reactor),"Native adjacency links module to its stirred reactor");
        Find.ResearchManager.FinishProject(Def<ResearchProjectDef>("HelodHaberBosch"),doCompletionDialog:false);
        Check(nylon.Worker.AvailableOnNow(module)&&module.UsableForBillsAfterFueling(),"Powered linked module accepts nylon production");
        module.GetComp<CompFlickable>().SwitchIsOn=false;
        Check(!module.UsableForBillsAfterFueling(),"Module switch off blocks work");module.GetComp<CompFlickable>().SwitchIsOn=true;
        reactor.GetComp<CompFlickable>().SwitchIsOn=false;
        Check(module.OperationalReactor==null&&!nylon.Worker.AvailableOnNow(module)&&!module.UsableForBillsAfterFueling(),"Reactor switch off blocks acquisition and work");reactor.GetComp<CompFlickable>().SwitchIsOn=true;
        reactor.GetComp<CompPowerTrader>().PowerOn=false;
        Check(module.OperationalReactor==null&&!nylon.Worker.AvailableOnNow(module),"Reactor power loss disables the linked facility");reactor.GetComp<CompPowerTrader>().PowerOn=true;
        playMap.powerNetManager.UpdatePowerNetsAndConnections_First();yield return 600;
        Check(module.UsableForBillsAfterFueling()&&module.GetComp<CompPowerTrader>().PowerOn,"Native grid restores module power after switch tests");
        bool interrupted=false,loadObserved=false;
        foreach(int ticks in ItemBill(worker,module,"HD_SpinNylon"))
        {
            var bill=module.BillStack.Bills.OfType<Bill_ProductionWithUft>().Single();
            var uft=bill.BoundUft;
            if(!loadObserved&&uft!=null&&worker.CurJobDef==JobDefOf.DoBill&&uft.workLeft<nylon.workAmount-150)
            {
                Check(module.GetComp<CompIndustrialWorktablePower>().IsWorking&&reactor.GetComp<CompIndustrialWorktablePower>().IsWorking,"Spinning drives active load in both module and reactor");
                Check(Math.Abs(module.GetComp<CompPowerTrader>().PowerOutput+500)<.01&&Math.Abs(reactor.GetComp<CompPowerTrader>().PowerOutput+1200)<.01,"Working demand is 500W plus the existing 1200W reactor");loadObserved=true;
            }
            if(!interrupted&&uft!=null&&uft.workLeft<nylon.workAmount-300)
            {
                reactor.GetComp<CompFlickable>().SwitchIsOn=false;yield return 120;
                Check(worker.CurJobDef!=JobDefOf.DoBill&&!bill.ShouldDoNow(),"Reactor loss interrupts an actual in-progress spinning job");
                float left=uft.workLeft;yield return 240;
                Check(bill.BoundUft==uft&&Math.Abs(uft.workLeft-left)<.001&&uft.ingredients.Sum(t=>t.stackCount)==13,"Interrupted work preserves exact progress and all 13 feedstock units");
                reactor.GetComp<CompFlickable>().SwitchIsOn=true;yield return 600;
                Check(nylon.Worker.AvailableOnNow(module),"Restoring reactor permits the same bill to resume");interrupted=true;
            }
            yield return ticks;
        }
        Check(interrupted&&loadObserved,"Actual nylon bill exercised interruption and coupled power load");
        foreach(int ticks in ItemBill(worker,module,"HD_SpinAramid"))yield return ticks;
        var garment=Def<ThingDef>("Apparel_Tuque");
        foreach(string n in new[]{"HD_Nylon","HD_Aramid"})
        {
            var d=Def<ThingDef>(n);Check(d.BaseMarketValue<ThingDefOf.ComponentIndustrial.BaseMarketValue,"Fiber remains below vanilla component value: "+n);
            Check(d.stuffProps.categories.Any(c=>c.defName=="Fabric")&&GenStuff.AllowedStuffsFor(garment).Contains(d),"New fiber is accepted by native textile crafting: "+n);
            var made=ThingMaker.MakeThing(garment,d);Check(made.Stuff==d&&made.GetStatValue(StatDefOf.MaxHitPoints)>0,"Native garment can be made from "+n);
            foreach(int amount in new[]{1,75}){var stack=ThingMaker.MakeThing(d);stack.stackCount=amount;Check(stack.Graphic.MatAt(Rot4.South,stack)!=BaseContent.BadMat,"Fiber stack graphic loads: "+n+"/"+amount);}
        }
        foreach(var rotation in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})Check(module.Graphic.MatAt(rotation)!=BaseContent.BadMat,"Module directional graphic loads: "+rotation);
        Check(Math.Abs(module.def.graphicData.drawSize.x-1.5f)<.001f,"One-tile module uses the calibrated 1.5-tile canvas");
        reactor.DeSpawn();Check(module.OperationalReactor==null&&!nylon.Worker.AvailableOnNow(module),"Removing reactor removes production access");
        GenSpawn.Spawn(reactor,playMap.Center+new IntVec3(-12,0,3),playMap);playMap.powerNetManager.UpdatePowerNetsAndConnections_First();yield return 1200;
        Check(module.OperationalReactor==reactor&&nylon.Worker.AvailableOnNow(module),"Reinstalling reactor rebuilds its native link");
    }
}
