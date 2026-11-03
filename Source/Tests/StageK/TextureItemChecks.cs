using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private readonly List<(Thing launcher,ThingDef projectile)> itemShots=new();
    private static Checks itemChecks;
    private static void ItemJobEnd(JobDriver __instance,JobCondition condition)
    {
        if(itemChecks!=null&&__instance.job?.def==JobDefOf.DoBill)
            System.IO.File.AppendAllText(System.IO.Path.Combine(itemChecks.output,"checks.txt"),"JOBEND "+__instance.job.bill?.recipe?.defName+" "+condition+" toil="+__instance.CurToilIndex+"\n");
    }
    private static void ItemLaunch(Projectile __instance,Thing launcher)
    {itemChecks?.itemShots.Add((launcher,__instance.def));}
    private Building ItemBuilding(string name,int x,int z)
    {
        var def=Def<ThingDef>(name);var b=(Building)ThingMaker.MakeThing(def,GenStuff.DefaultStuffFor(def));
        b.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(b,playMap.Center+new IntVec3(x,0,z),playMap);return b;
    }
    private int ItemCount(ThingDef def)=>playMap.listerThings.ThingsOfDef(def).Sum(t=>t.stackCount)
        + fixtures.Sum(p=>(p.carryTracker.CarriedThing?.def==def?p.carryTracker.CarriedThing.stackCount:0)
            +p.inventory.innerContainer.Where(t=>t.def==def).Sum(t=>t.stackCount));
    private void ItemSupply(ThingDef def,int count,IntVec3 cell)
    {
        while(count>0){var t=ThingMaker.MakeThing(def,GenStuff.DefaultStuffFor(def));t.stackCount=Math.Min(count,def.stackLimit);count-=t.stackCount;GenPlace.TryPlaceThing(t,cell,playMap,ThingPlaceMode.Near);}
    }
    private IEnumerable<int> ItemBill(Pawn worker,Building_WorkTable table,string name)
    {
        var recipe=Def<RecipeDef>(name);
        if(recipe.researchPrerequisite!=null)Find.ResearchManager.FinishProject(recipe.researchPrerequisite,doCompletionDialog:false);
        Check(table.def.AllRecipes.Contains(recipe),"Recipe appears on the actual worktable: "+name);
        var bill=(Bill_Production)recipe.MakeNewBill();bill.repeatMode=BillRepeatModeDefOf.RepeatCount;bill.repeatCount=1;bill.SetStoreMode(BillStoreModeDefOf.BestStockpile,null);table.BillStack.AddBill(bill);
        var costs=recipe.ingredients.Select(i=>(def:i.filter.AnyAllowedDef,count:i.CountRequiredOfFor(i.filter.AnyAllowedDef,recipe,bill))).ToList();
        foreach(var c in costs)
        {
            // Quicktest maps can contain inaccessible vanilla stock elsewhere. Supply
            // external feedstocks locally; reuse actual outputs for the new chain.
            bool chained=c.def.defName=="HD_Alumina"||c.def.defName=="HD_AluminiumAlloy"||c.def.defName=="HD_Silicon";
            int needed=chained?Math.Max(0,c.count-ItemCount(c.def)):c.count;
            if(needed>0)ItemSupply(c.def,needed,table.InteractionCell+new IntVec3(1,0,-1));
        }
        var before=costs.ToDictionary(c=>c.def,c=>ItemCount(c.def));var products=recipe.products.ToDictionary(p=>p.thingDef,p=>ItemCount(p.thingDef));
        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);worker.stances.CancelBusyStanceSoft();
        var givers=DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(g=>typeof(WorkGiver_DoBill).IsAssignableFrom(g.giverClass)&&g.fixedBillGiverDefs?.Contains(table.def)==true).ToList();
        System.IO.File.AppendAllText(System.IO.Path.Combine(output,"checks.txt"),"BILL "+name+" work="+recipe.requiredGiverWorkType+" givers="+string.Join(",",givers.Select(g=>g.defName+":"+g.workType))+" filters="+string.Join(",",costs.Select(c=>c.def.defName+":"+bill.ingredientFilter.Allows(c.def)+":"+recipe.fixedIngredientFilter.Allows(c.def)))+" should="+bill.ShouldDoNow()+" available="+recipe.AvailableNow+" skill="+recipe.PawnSatisfiesSkillRequirements(worker)+" area="+string.Join(",",table.InteractionCell.GetThingList(playMap).Select(t=>t.def.defName))+" worker="+worker.Position+" costs="+string.Join(",",costs.Select(c=>c.def.defName+":"+ItemCount(c.def)))+"\n");
        var chosen=new List<ThingCount>();var missing=new List<IngredientCount>();var args=new object[]{bill,worker,table,chosen,missing};var ingredientsFound=(bool)AccessTools.Method(typeof(WorkGiver_DoBill),"TryFindBestBillIngredients").Invoke(null,args);
        System.IO.File.AppendAllText(System.IO.Path.Combine(output,"checks.txt"),"INGREDIENTS "+name+" found="+ingredientsFound+" chosen="+string.Join(",",chosen.Select(t=>t.Thing.def.defName+":"+t.Count))+" missing="+string.Join(",",missing.Select(i=>i.filter.Summary))+"\n");
        Job job=null;foreach(var g in givers){job=((WorkGiver_DoBill)g.Worker).JobOnThing(worker,table,true);if(job!=null)break;}
        Check(job?.def==JobDefOf.DoBill&&worker.jobs.TryTakeOrderedJob(job),"Native work giver starts production: "+name+"; job="+job?.def+"; reason="+JobFailReason.Reason);
        int elapsed=0;while(bill.repeatCount>0&&elapsed<60000)
        {
            yield return 120;elapsed+=120;
            if(elapsed%4800==0)System.IO.File.AppendAllText(System.IO.Path.Combine(output,"checks.txt"),"TRACE "+name+" ticks="+elapsed+" job="+worker.CurJobDef+" driver="+worker.jobs.curDriver?.CurToilIndex+" bound="+(bill as Bill_ProductionWithUft)?.BoundUft?.workLeft+" carry="+worker.carryTracker.CarriedThing+" power="+table.GetComp<CompPowerTrader>()?.PowerOn+" available="+recipe.Worker.AvailableOnNow(table)+" materials="+string.Join(",",costs.Select(c=>c.def.defName+":"+ItemCount(c.def)))+"\n");
        }
        Check(bill.repeatCount==0,"Native bill completes: "+name+"; current="+worker.CurJobDef);
        foreach(var c in costs)Check(ItemCount(c.def)==before[c.def]-c.count,"Exact material consumption: "+name+"/"+c.def.defName);
        foreach(var p in recipe.products)Check(ItemCount(p.thingDef)==products[p.thingDef]+p.count,"Exact physical output: "+name);
        int finishing=0;while((worker.CurJobDef==JobDefOf.DoBill||worker.CurJobDef==JobDefOf.HaulToCell)&&finishing<6000){yield return 120;finishing+=120;}
        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);table.BillStack.Delete(bill);
    }
    private void ItemBurst(Building_TurretGun turret,string[] expected)
    {
        var verb=turret.AttackVerb;var fuel=turret.GetComp<CompRefuelable>();float before=fuel.Fuel;
        int start=itemShots.Count;Check(verb.TryStartCastOn(turret.Position+new IntVec3(0,0,18),canHitNonTargetPawns:false,preventFriendlyFire:true),"Native firing starts: "+turret.def.defName);
        for(int i=0;i<300&&verb.Bursting;i++)verb.VerbTick();
        var shots=itemShots.Skip(start).Where(s=>s.launcher==turret).Select(s=>s.projectile.defName).ToArray();
        Check(shots.SequenceEqual(expected),"Native launched projectile sequence: "+turret.def.defName+"; actual="+string.Join(",",shots));
        Check(Math.Abs(before-fuel.Fuel-expected.Length)<.001f,"Exactly one replenishment unit is consumed per shot: "+turret.def.defName);
        verb.Reset();
    }
    private IEnumerable<int> TextureItemChecks()
    {
        playMap=Find.CurrentMap;DebugSettings.enableStoryteller=false;DebugSettings.enableRandomMentalStates=false;DebugSettings.noAnimals=true;
        foreach(var pawn in playMap.mapPawns.AllPawnsSpawned.ToList())pawn.DeSpawn();
        foreach(var cell in CellRect.CenteredOn(playMap.Center,35))
        {
            foreach(var t in cell.GetThingList(playMap).ToList())if(t.Spawned&&t.def.destroyable)t.Destroy();
            playMap.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);playMap.roofGrid.SetRoof(cell,null);playMap.fogGrid.Unfog(cell);playMap.areaManager.Home[cell]=true;
        }
        var h=new Harmony("helodrace.tests.texture-items");h.Patch(AccessTools.PropertyGetter(typeof(MapTemperature),nameof(MapTemperature.OutdoorTemp)),prefix:new HarmonyMethod(typeof(Checks),nameof(Climate)));
        h.Patch(AccessTools.Method(typeof(JobDriver),nameof(JobDriver.Cleanup)),prefix:new HarmonyMethod(typeof(Checks),nameof(ItemJobEnd)));
        h.Patch(AccessTools.GetDeclaredMethods(typeof(Projectile)).Single(m=>m.Name==nameof(Projectile.Launch)&&m.GetParameters().Length==8),postfix:new HarmonyMethod(typeof(Checks),nameof(ItemLaunch)));itemChecks=this;
        playMap.weatherManager.curWeather=playMap.weatherManager.lastWeather=WeatherDefOf.Clear;playMap.skyManager.ForceSetCurSkyGlow(1);
        var stockpile=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,playMap.zoneManager);playMap.zoneManager.RegisterZone(stockpile);stockpile.settings.filter.SetAllowAll(null);
        foreach(var cell in new CellRect(playMap.Center.x+22,playMap.Center.z-14,8,8))if(cell.GetZone(playMap)==null)stockpile.AddCell(cell);
        var worker=PawnGenerator.GeneratePawn(new PawnGenerationRequest(Def<PawnKindDef>("HD_RescuerInBlack"),Faction.OfPlayer,forceGenerateNewPawn:true,mustBeCapableOfViolence:true,canGeneratePawnRelations:false,fixedBiologicalAge:25,fixedChronologicalAge:25){ForceNoBackstory=true});
        worker.apparel.DestroyAll();worker.equipment.DestroyAllEquipment();worker.health.RemoveAllHediffs();
        GenSpawn.Spawn(worker,playMap.Center+new IntVec3(0,0,-8),playMap);fixtures.Add(worker);worker.workSettings.EnableAndInitialize();worker.skills.GetSkill(SkillDefOf.Crafting).Level=16;
        worker.skills.GetSkill(SkillDefOf.Construction).Level=16;worker.needs.joy.CurLevel=1;worker.skills.GetSkill(SkillDefOf.Melee).Level=20;
        foreach(var work in DefDatabase<WorkTypeDef>.AllDefsListForReading)worker.workSettings.SetPriority(work,0);
        worker.workSettings.SetPriority(WorkTypeDefOf.Crafting,1);worker.workSettings.SetPriority(WorkTypeDefOf.Smithing,1);
        for(int hour=0;hour<24;hour++)worker.timetable.SetAssignment(hour,TimeAssignmentDefOf.Work);
        var stirred=(Building_WorkTable)ItemBuilding("HD_StirredTankReactor",-12,3);
        var furnace=(Building_WorkTable)ItemBuilding("HD_SAFurnace",-4,3);
        var fab=(Building_WorkTable)ItemBuilding("HD_SemiconductorFab",4,3);
        var machining=(Building_WorkTable)ItemBuilding("TableMachining",12,3);
        for(int x=-30;x<=24;x++){ItemBuilding("PowerConduit",x,0);ItemBuilding("PowerConduit",x,-1);}
        for(int x=-30;x<=18;x+=3)ItemBuilding("VanometricPowerCell",x,-3);
        for(int x=-16;x<=16;x++){var wall=ThingMaker.MakeThing(ThingDefOf.Wall,Def<ThingDef>("BlocksGranite"));GenSpawn.Spawn(wall,playMap.Center+new IntVec3(x,0,7),playMap);}
        foreach(var table in new[]{stirred,furnace,fab,machining})foreach(var cell in table.OccupiedRect())playMap.roofGrid.SetRoof(cell,RoofDefOf.RoofConstructed);
        playMap.powerNetManager.UpdatePowerNetsAndConnections_First();yield return 1200;
        foreach(var t in new[]{stirred,furnace,fab,machining})Check(t.GetComp<CompPowerTrader>().PowerOn,"Real power grid powers "+t.def.defName);
        foreach(string n in new[]{"HD_Alumina","HD_AluminiumAlloy","HD_Silicon"})
        {
            var d=Def<ThingDef>(n);Check(d.BaseMarketValue<ThingDefOf.ComponentIndustrial.BaseMarketValue,"Intermediate price remains below vanilla component: "+n);
            var t=ThingMaker.MakeThing(d);Check(t.Graphic.MatSingle!=BaseContent.BadMat&&d.uiIcon!=BaseContent.BadTex,"Actual resource graphics resolve: "+n);
            Check(DefDatabase<TraderKindDef>.AllDefsListForReading.Count(k=>k.stockGenerators.OfType<StockGenerator_SingleDef>().Any(g=>Traverse.Create(g).Field("thingDef").GetValue<ThingDef>()==d))>=3,"Three existing suppliers stock "+n);
        }
        foreach(var tuple in new[]{(stirred,"HD_ExtractAlumina"),(furnace,"HD_SmeltSilicon"),(furnace,"HD_RefineAluminiumAlloy"),(fab,"HD_FabricateSolidStateComponents")})
            foreach(int ticks in ItemBill(worker,tuple.Item1,tuple.Item2))yield return ticks;
        Check(Def<RecipeDef>("HD_ExtractAlumina").ingredients.Any(i=>i.filter.Allows(Def<ThingDef>("BlocksSlate"))),"Alumina uses existing slate blocks without a new ore");
        Check(Def<RecipeDef>("HD_SmeltSilicon").ingredients.Any(i=>i.filter.Allows(Def<ThingDef>("BlocksSandstone"))),"Silicon uses existing sandstone blocks without a new ore");
        foreach(string name in new[]{"Make_HD_ExpandableBaton","Make_HD_Apparel_ShieldIronHideRiot","Make_HD_Apparel_ShieldIronHideIV"})
            foreach(int ticks in ItemBill(worker,machining,name))yield return ticks;
        var baton=playMap.listerThings.ThingsOfDef(Def<ThingDef>("HD_ExpandableBaton")).OfType<ThingWithComps>().Single();
        worker.equipment.AddEquipment(baton.SplitOff(1) as ThingWithComps);
        Check(worker.equipment.Primary.def.defName=="HD_ExpandableBaton"&&worker.equipment.Primary.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS)>0,"Crafted baton equips with native melee attacks");
        Check(baton.def.uiIcon!=BaseContent.BadTex&&baton.Graphic.MatSingle!=BaseContent.BadMat,"Collapsed inventory and extended held baton graphics load");
        var riot=(Apparel)playMap.listerThings.ThingsOfDef(Def<ThingDef>("HD_Apparel_ShieldIronHideRiot")).Single();riot.DeSpawn();worker.apparel.Wear(riot);
        var shield=riot.GetComp<CompDirectionalBallisticShield>();Check(shield.Wearer==worker&&shield.IsWeaponAllowed(baton.def)&&!shield.IsWeaponAllowed(Def<ThingDef>("Gun_AssaultRifle")),"Riot shield permits baton and restricts rifles");
        foreach(var rotation in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})
        {var graphic=GraphicDatabase.Get<Graphic_Multi>(riot.def.apparel.wornGraphicPath,ShaderDatabase.Transparent,riot.def.graphicData.drawSize,riot.DrawColor);Check(graphic.MatAt(rotation)!=BaseContent.BadMat,"Riot shield worn direction resolves: "+rotation);}
        worker.Rotation=Rot4.North;int hp=riot.HitPoints;
        var blow=new DamageInfo(DamageDefOf.Blunt,8,.05f,180,worker);shield.PostPreApplyDamage(ref blow,out bool blocked);
        Check(blocked&&riot.HitPoints<hp,"Frontal blunt blow is absorbed and wears the riot shield");
        blow=new DamageInfo(DamageDefOf.Blunt,8,.05f,0,worker);shield.PostPreApplyDamage(ref blow,out blocked);Check(!blocked,"Rear melee bypasses riot shield");
        blow=new DamageInfo(DamageDefOf.Bullet,8,.42f,180,worker);shield.PostPreApplyDamage(ref blow,out blocked);Check(!blocked,"Rifle penetration bypasses light riot protection");
        blow=new DamageInfo(DamageDefOf.Blunt,8,.05f,180);shield.PostPreApplyDamage(ref blow,out blocked);Check(!blocked,"Environmental blunt damage cannot be blocked as a melee attack");
        worker.apparel.Remove(riot);var iv=(Apparel)ThingMaker.MakeThing(Def<ThingDef>("HD_Apparel_ShieldIronHideIV"));worker.apparel.Wear(iv);
        blow=new DamageInfo(DamageDefOf.Blunt,8,.05f,180,worker);iv.GetComp<CompDirectionalBallisticShield>().PostPreApplyDamage(ref blow,out blocked);Check(!blocked,"Existing ballistic shield remains ranged-only");worker.apparel.Remove(iv);
        var turret=(Building_TurretGun)ItemBuilding("HD_Building_M2HB",-15,15);turret.GetComp<CompRefuelable>().Refuel(100);
        var belt=Enumerable.Range(0,10).Select(i=>i%5==4?"HD_Bullet_M2HB_M20_Proj":"HD_Bullet_M2HB_Proj").ToArray();
        ItemBurst(turret,belt);ItemBurst(turret,belt);
        Check(turret.GetComp<CompRefuelable>().Props.fuelFilter.Allows(ThingDefOf.Steel),"M2HB retains Steel replenishment");
        var crows=(Building_Crows)ItemBuilding("HD_Building_CrowsII",0,15);var console=(Building_CrowsComputer)ItemBuilding("HD_CrowsComputer",8,15);
        for(int z=1;z<=16;z++)ItemBuilding("PowerConduit",0,z);for(int x=1;x<=8;x++)ItemBuilding("PowerConduit",x,14);
        var mount=Def<CrowsWeaponDef>("HD_CrowsWeapon_M2HB");Check(crows.RequestWeapon(mount),"Empty CROWS selects M2HB mount");
        foreach(var cost in mount.costList){var source=new ThingOwner<Thing>();var t=ThingMaker.MakeThing(cost.thingDef);t.stackCount=cost.count;source.TryAdd(t);Check(crows.AcceptMountingMaterial(source,t,cost.count)==cost.count,"Native mounting material accepted: "+cost.thingDef.defName);}
        Check(crows.AddMountingWork(mount.workToInstall),"Native CROWS installation finishes with the mixed-feed gun");crows.GetComp<CompRefuelable>().Refuel(100);console.Connect(crows);
        playMap.powerNetManager.UpdatePowerNetsAndConnections_First();yield return 1200;
        Check(!crows.AttackVerb.Available(),"Powered but unmanned CROWS cannot fire");
        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);worker.Position=console.InteractionCell;worker.Notify_Teleported();
        Check(worker.jobs.TryTakeOrderedJob(JobMaker.MakeJob(CrowsDefOf.HD_OperateCrows,console,crows)),"Actual remote-control operation job starts");yield return 60;
        Check(console.Operator==worker&&crows.CanFireNow,"Actual operator at powered console enables CROWS");ItemBurst(crows,belt);
        var next=((Verb_Crows)crows.AttackVerb).Projectile;float fuel=crows.GetComp<CompRefuelable>().Fuel;console.Disconnect();
        Check(!(bool)AccessTools.Method(typeof(Verb_Crows),"TryCastShot").Invoke(crows.AttackVerb,null),"Disconnect blocks an actual attempted CROWS shot");
        Check(((Verb_Crows)crows.AttackVerb).Projectile==next&&crows.GetComp<CompRefuelable>().Fuel==fuel,"Failed remote shot changes neither feed position nor Steel");
        foreach(string n in new[]{"HD_Gun_CrowsII_M240B","HD_Gun_CrowsII_M249Weapon","HD_Gun_CrowsII_MK19Weapon"})
        {var d=Def<ThingDef>(n);Check(d.GetModExtension<MixedMagazineExtension>()==null&&!d.Verbs[0].defaultProjectile.defName.StartsWith("HD_Bullet_M2HB"),"Other CROWS calibers retain native single-projectile feeds: "+n);}
        var m167=Def<ThingDef>("HD_Gun_M167VADS_TurretGun");Check(m167.GetModExtension<MixedMagazineExtension>().projectileSequence.Select(p=>p.defName).SequenceEqual(new[]{"HD_Projectile_M167_20mmHEI","HD_Projectile_M167_20mmHEI","HD_Projectile_M167_20mmHEI","HD_Projectile_M167_20mmHEIT","HD_Projectile_M167_20mmAPI"}),"Existing M167 sequence remains unchanged");
        Check(Def<ThingDef>("HD_Projectile_M167_20mmAPI").thingClass==typeof(Projectile_ArmorPiercingIncendiary),"M167 still resolves its shared incendiary impact class");
        foreach(string n in new[]{"HD_Bullet_M2HB_Proj","HD_Bullet_M2HB_M20_Proj"})
        {var d=Def<ThingDef>(n);Check(d.category==ThingCategory.Projectile&&d.thingClass==typeof(Projectile_ArmorPiercingIncendiary)&&!d.IsWeapon&&d.recipeMaker==null,"Mixed round is a projectile, not a craftable ammunition item: "+n);Check(ThingMaker.MakeThing(d).Graphic.MatSingle!=BaseContent.BadMat,"Mixed projectile texture loads: "+n);}
        Check(Def<ThingDef>("HD_Bullet_M2HB_CAS_Proj").projectile.speed==240&&Def<ThingDef>("HD_Bullet_M2HB_CAS_Proj").projectile.flyOverhead,"Aircraft support bullet is unchanged");
        itemChecks=null;h.UnpatchAll(h.Id);
    }
}
