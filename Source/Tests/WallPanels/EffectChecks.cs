using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Panels;
using Helodrace.Ballistics;
using RimWorld;
using RimWorld.Planet;
using Verse;
using System.Xml.Linq;

internal static class EffectChecks
{
    private sealed class FixtureAutoPowered : CompAutoPowered { public override bool WantsToBeOn=>true; }
    public static void CheckDefinitions(Action<bool,string> check,string root)
    {
        foreach(string path in new[]{"Buildings/WallPanels.xml","WallPanels/PolymerSmoke.xml","WallPanels/PolymerSmokeHediff.xml"}.Select(p=>Path.Combine(root,"Defs",p)))
            foreach(XElement def in XDocument.Load(path).Root!.Elements())
            {
                Type type=def.Name.LocalName switch {"ThingDef"=>typeof(ThingDef),"HediffDef"=>typeof(HediffDef),"RecipeDef"=>typeof(RecipeDef),_=>typeof(PanelRules).Assembly.GetType(def.Name.LocalName)!};
                check(type!=null,"Missing XML def class "+def.Name);
                foreach(XElement field in def.Elements())
                    check(AccessTools.Field(type,field.Name.LocalName)!=null||AccessTools.Property(type,field.Name.LocalName)!=null,"Unknown engine XML field: "+def.Name+"/"+field.Name);
                foreach(XAttribute cls in def.DescendantsAndSelf().Attributes("Class"))
                    check(typeof(PanelRules).Assembly.GetType(cls.Value)!=null,"Unresolved effect XML class "+cls.Value);
            }
        var english=XDocument.Load(Path.Combine(root,"Languages/English/Keyed/WallPanelEffects.xml")).Root!.Elements().ToDictionary(n=>n.Name.LocalName,n=>n.Value);
        var korean=XDocument.Load(Path.Combine(root,"Languages/Korean (한국어)/Keyed/WallPanelEffects.xml")).Root!.Elements().ToDictionary(n=>n.Name.LocalName,n=>n.Value);
        check(english.Keys.Order().SequenceEqual(korean.Keys.Order()),"Effect translation key mismatch");
        foreach(string key in english.Keys)
            check(System.Text.RegularExpressions.Regex.Matches(english[key],@"\{\d+\}").Select(m=>m.Value).Order().SequenceEqual(System.Text.RegularExpressions.Regex.Matches(korean[key],@"\{\d+\}").Select(m=>m.Value).Order()),"Effect translation argument mismatch: "+key);
    }
    private sealed class BlackoutCondition : GameCondition
    {
        public override bool ElectricityDisabled => true;
    }
    static T Raw<T>() where T:class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static void Set(object obj,string field,object value) => AccessTools.Field(obj.GetType(),field).SetValue(obj,value);
    static void State(Thing thing,IntVec3 pos,int id)
    {
        Set(thing,"positionInt",pos);Set(thing,"mapIndexOrState",(sbyte)0);thing.thingIDNumber=id;thing.stackCount=1;
    }
    public static void Run(Action<bool,string> check)
    {
        CheckArmor(check);
        float remaining=1,total=0;
        for(int i=0;i<100;i++)total+=CompPolymerCore.Consume(ref remaining,250,1800);
        check(MathF.Abs(total-1)<1e-6f&&remaining==0,"Burning produced renewable foam fuel");
        check(CompPolymerCore.Consume(ref remaining,9999,1800)==0,"Spent core produced more smoke");
        var fixture=new Harmony("Helodrace.PanelFixture.MapApplicability");
        fixture.Patch(AccessTools.Method(typeof(GameCondition),nameof(GameCondition.CanApplyOnMap)),
            prefix:new HarmonyMethod(typeof(EffectChecks),nameof(FixtureCondition)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(Map),nameof(Map.Biome)),prefix:new HarmonyMethod(typeof(EffectChecks),nameof(FixtureBiome)));
        try { CheckMap(check); CheckCoreSave(check); }
        finally { fixture.UnpatchAll(fixture.Id); }
    }
    public static void RunPatched(Action<bool,string> check)
    {
        var fixture=new Harmony("Helodrace.PanelFixture.MapApplicability");
        fixture.Patch(AccessTools.Method(typeof(GameCondition),nameof(GameCondition.CanApplyOnMap)),prefix:new HarmonyMethod(typeof(EffectChecks),nameof(FixtureCondition)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(Map),nameof(Map.Biome)),prefix:new HarmonyMethod(typeof(EffectChecks),nameof(FixtureBiome)));
        try {CheckMap(check,true);}
        finally {fixture.UnpatchAll(fixture.Id);}
    }
    static bool FixtureBiome(Map __instance,ref BiomeDef __result)
    {
        if(__instance.generatorDef?.defName!="PanelFixture")return true;
        __result=new BiomeDef{constantOutdoorTemperature=40};return false;
    }
    static void CheckCoreSave(Action<bool,string> check)
    {
        var core=new CompPolymerCore();Set(core,"remaining",.35f);
        string path=Path.Combine(Path.GetTempPath(),"helod-panel-core-"+Guid.NewGuid()+".xml");
        try
        {
            Scribe.saver.InitSaving(path,"core");core.PostExposeData();Scribe.saver.FinalizeSaving();
            var loaded=new CompPolymerCore();Scribe.loader.InitLoading(path);loaded.PostExposeData();Scribe.loader.ForceStop();
            check(MathF.Abs(loaded.FractionRemaining-.35f)<1e-6f,"Native save/load replenished burned foam");
        }
        finally{Scribe.ForceStop();if(File.Exists(path))File.Delete(path);}
        var source=Raw<ThingWithComps>();source.stackCount=6;Set(source,"comps",new List<ThingComp>{core});core.parent=source;
        var piece=Raw<ThingWithComps>();piece.stackCount=4;var split=new CompPolymerCore{parent=piece};Set(piece,"comps",new List<ThingComp>{split});
        core.PostSplitOff(piece);
        check(MathF.Abs((6*core.FractionRemaining+4*split.FractionRemaining)-3.5f)<1e-6f,"Splitting replenished foam fuel");
        var fresh=Raw<ThingWithComps>();fresh.stackCount=4;var freshCore=new CompPolymerCore{parent=fresh};Set(fresh,"comps",new List<ThingComp>{freshCore});
        core.PreAbsorbStack(fresh,4);
        check(MathF.Abs(core.FractionRemaining*10-(6*.35f+4))<1e-5f,"Merging stacks created or lost foam fuel");
    }
    // Fixture conditions apply to this constructed map; do not bootstrap the
    // installed user's ModsConfig/DLC filter from a console test.
    static bool FixtureCondition(GameCondition __instance,ref bool __result)
    {
        if(__instance is not BlackoutCondition)return true;
        __result=true;return false;
    }
    static void CheckArmor(Action<bool,string> check)
    {
        var mat=new KineticMaterial("RHA test",ArmorMaterialFamily.Metal,7850,new[]{1680f,1395,1245,1060},null,null,.06f,.2f,1f/300,60,4);
        int normalBounces=0,grazingBounces=0;
        for(ulong seed=1;seed<=500;seed++)
        {
            foreach(float degrees in new[]{0f,75f})
            {
                float rad=degrees*MathF.PI/180;
                Vector3 velocity=new(MathF.Sin(rad)*900,0,MathF.Cos(rad)*900);
                var round=PenetratorState.Projectile(PenetrationMechanism.KineticBullet,.008f,velocity,0,.00762f);
                var result=PanelCombat.ResolveArmor(round,PenetratorClass.Soft,mat,.008f,1,-Vector3.UnitZ,seed);
                check(result.output.KineticEnergyJ<=round.KineticEnergyJ+.01f,"Panel created projectile energy");
                check(result.absorbedJ>=0,"Negative panel energy absorption");
                if(result.ricochet)
                {
                    if(degrees==0)normalBounces++;else grazingBounces++;
                    check(result.stopped&&result.output.Velocity.Z<0,"Ricochet returned into its wall");
                }
            }
        }
        check(normalBounces==0&&grazingBounces>400,"RHA ricochet did not distinguish normal and grazing hits");
        var fast=PenetratorState.Projectile(PenetrationMechanism.KineticBullet,.04f,Vector3.UnitZ*1800,0,.0127f);
        var perforated=PanelCombat.ResolveArmor(fast,PenetratorClass.Tungsten,mat,.008f,1,-Vector3.UnitZ,321);
        check(!perforated.stopped&&perforated.output.Speed<fast.Speed,"Fast AP impact did not leave reduced residual velocity");
        check(PanelCombat.TraceFace(new Vector3(-2,0,-.1f),Vector3.Normalize(new Vector3(1,0,.2f)),new IntVec3(0,0,0),out Rot4 face,out Vector3 p)&&face==Rot4.West&&MathF.Abs(p.X)<1e-5f,"Actual segment selected the wrong wall face");
    }
    static void CheckMap(Action<bool,string> check,bool patched=false)
    {
        // Real game classes and grids, populated directly without map generation,
        // UI or GPU. This is a deterministic engine fixture, not a played save.
        Game game=Raw<Game>();Set(game,"maps",new List<Map>());Current.Game=game;
        var world=Raw<World>();world.gameConditionManager=new GameConditionManager(world);Set(game,"worldInt",world);
        Map map=Raw<Map>();map.info=new MapInfo{Size=new IntVec3(9,1,9)};map.cellIndices=new CellIndices(9,9);
        game.Maps.Add(map);
        map.edificeGrid=new EdificeGrid(map);map.thingGrid=new ThingGrid(map);map.regionGrid=new RegionGrid(map);
        map.regionDirtyer=new RegionDirtyer(map);map.regionAndRoomUpdater=new RegionAndRoomUpdater(map);
        Set(map.regionAndRoomUpdater,"initialized",true);
        map.roofGrid=new RoofGrid(map);map.gameConditionManager=new GameConditionManager(map);
        map.generatorDef=new MapGeneratorDef{defName="PanelFixture"};
        map.mapTemperature=new MapTemperature(map);
        map.events=new MapEvents(map);
        var panels=new MapComponent_WallPanels(map);var shielding=new MapComponent_PanelShielding(map);
        map.components=new List<MapComponent>{panels,shielding};
        IntVec3 center=new(4,0,4);
        Room room=Raw<Room>();District district=Raw<District>();Region region=Raw<Region>();
        Set(room,"districts",new List<District>{district});Set(room,"cachedCellCount",1);Set(room,"cachedOpenRoofCount",0);
        Set(district,"mapIndex",(sbyte)0);Set(district,"roomInt",room);Set(district,"regions",new List<Region>{region});
        Set(region,"mapIndex",(sbyte)0);Set(region,"districtInt",district);Set(region,"type",RegionType.Normal);
        Set(region,"valid",true);Set(region,"extentsClose",new CellRect(4,4,1,1));Set(region,"extentsLimit",new CellRect(4,4,1,1));
        Set(region,"cachedCellCount",1);
        map.regionGrid.DirectGrid[map.cellIndices.CellToIndex(center)]=region;
        var roof=new RoofDef{defName="FixtureRoof"};var roofs=(RoofDef[])AccessTools.Field(typeof(RoofGrid),"roofGrid").GetValue(map.roofGrid)!;
        roofs[map.cellIndices.CellToIndex(center)]=roof;
        RoomTempTracker tracker=Raw<RoomTempTracker>();Set(tracker,"room",room);Set(tracker,"temperatureInt",20f);Set(tracker,"equalizeCells",new List<IntVec3>());
        Set(room,"tempTracker",tracker);
        int id=1;
        Building Host(IntVec3 pos,bool door=false)
        {
            Building b=door?Raw<Building_Door>():Raw<Building>();
            b.def=new ThingDef{defName="FixtureHost",thingClass=door?typeof(Building_Door):typeof(Building),fillPercent=1,building=new BuildingProperties{isWall=!door,isEdifice=true},passability=Traversability.Impassable};
            State(b,pos,id++);Set(b,"rotationInt",Rot4.North);
            map.edificeGrid.InnerArray[map.cellIndices.CellToIndex(pos)]=b;map.thingGrid.ThingsListAt(pos).Add(b);
            return b;
        }
        Building_WallPanel Panel(Building host,Rot4 face,PanelSlot slot,float r=0)
        {
            var panel=Raw<Building_WallPanel>();
            panel.def=new ThingDef{defName="FixturePanel",thingClass=typeof(Building_WallPanel),drawDamagedOverlay=false,building=new BuildingProperties{isEdifice=false},
                modExtensions=new List<DefModExtension>{new WallPanelProperties{slot=slot,thermalResistance=r,electromagneticShielding=slot==PanelSlot.Shielding,flameMultiplier=.35f,ignitionMultiplier=.1f,aramid=slot==PanelSlot.Reinforcement}}};
            State(panel,host.Position,id++);Set(panel,"rotationInt",face);Set(panel,"hitPointsInt",250);
            var anchor=new CompPanelAnchor{parent=panel,props=new CompProperties_PanelAnchor(),host=host,anchored=true};
            Set(panel,"comps",new List<ThingComp>{anchor});
            map.thingGrid.ThingsListAt(panel.Position).Add(panel);panels.Register(panel);return panel;
        }
        Building[] walls=new Building[4];
        for(int d=0;d<4;d++)walls[d]=Host(center+GenAdj.CardinalDirections[d],d==2);
        Building north=walls[0];
        var reads=(List<IntVec3>)AccessTools.Field(typeof(RoomTempTracker),"equalizeCells").GetValue(tracker)!;reads.Add(new IntVec3(4,0,6));
        MethodInfo wallHeat=AccessTools.Method(typeof(RoomTempTracker),"WallEqualizationTempChangePerInterval");
        float bareHeat=patched?(float)wallHeat.Invoke(tracker,null)!:0;
        var lining=Panel(north,Rot4.South,PanelSlot.Insulation,3);
        check(MathF.Abs(PanelThermal.PathFactor(room,new IntVec3(4,0,6),map)-.25f)<.0001f,"North wall did not insulate its own path");
        check(PanelThermal.PathFactor(room,new IntVec3(6,0,4),map)==1,"North panel insulated an unrelated east path");
        Panel(north,Rot4.North,PanelSlot.Insulation,3);
        check(MathF.Abs(PanelThermal.PathFactor(room,new IntVec3(4,0,6),map)-1f/7)<.0001f,"Opposing insulation resistances did not combine");
        if(patched)check(MathF.Abs((float)wallHeat.Invoke(tracker,null)!-bareHeat/7)<1e-5f,"Patched engine wall heat did not use its per-path resistance");
        check(CompPolymerCore.EmissionCells(lining).SequenceEqual(new[]{center}),"Attached foam smoke crossed an enclosed wall");
        var smokeCells=CompPolymerCore.EmissionCells(north);
        check(smokeCells.Contains(center)&&smokeCells.Contains(new IntVec3(4,0,6))&&smokeCells.Distinct().Count()==smokeCells.Count,"Wall smoke did not reach both exposed spaces exactly once");
        check(PanelSurface.FireFactor(north,true,center)<.36f&&PanelSurface.FireFactor(north,true,new IntVec3(5,0,5))==1,"Fire protection leaked to an uncovered face");
        check(!shielding.Report(room).Protected,"Bare room was shielded");
        for(int d=0;d<4;d++)Panel(walls[d],new Rot4((d+2)%4),PanelSlot.Shielding);
        check(shielding.Report(room).Protected,"Fully lined roofed room was not shielded");
        var door=(Building_Door)walls[2];Set(door,"openInt",true);shielding.Invalidate();
        check(!shielding.Report(room).Protected,"Open lined door retained immunity");
        Set(door,"openInt",false);shielding.Invalidate();check(shielding.Report(room).Protected,"Closing lined door did not restore eligibility");
        roofs[map.cellIndices.CellToIndex(center)]=null;shielding.Invalidate();check(!shielding.Report(room).Protected,"Roof hole retained immunity");
        roofs[map.cellIndices.CellToIndex(center)]=roof;shielding.Invalidate();
        Thing device=Raw<Building>();device.def=new ThingDef{thingClass=typeof(Building),size=new IntVec2(1,1)};State(device,center,id++);
        check(shielding.Protects(device),"Equipment in the protected room was not eligible");
        if(patched)
        {
            map.roofGrid.RemoveRoofUnsafe(map.cellIndices.CellToIndex(center));
            check(!shielding.Report(room).Protected,"Native roof removal left a protected cache entry");
            check(shielding.Report(room).roofHoles==1,"Shield report hid its missing roof");
            roofs[map.cellIndices.CellToIndex(center)]=roof;shielding.Invalidate();
        }
        var solar=new BlackoutCondition{def=new GameConditionDef{defName="SolarFlare"}};
        map.gameConditionManager.ActiveConditions.Add(solar);
        check(PanelShielding.SolarActive(map)&&!PanelShielding.SolarBlocked(device),"Protected equipment was blocked by solar flare");
        var outside=Raw<Building>();outside.def=device.def;State(outside,new IntVec3(1,0,1),id++);
        check(PanelShielding.SolarBlocked(outside),"Immunity leaked to outside equipment on the same map");
        map.gameConditionManager.ActiveConditions.Add(new BlackoutCondition{def=new GameConditionDef{defName="OtherBlackout"}});
        check(PanelShielding.OtherBlackout(map),"Shielding suppressed a non-solar blackout");
        if(patched)
        {
            map.gameConditionManager.ActiveConditions.RemoveAt(1);
            CheckPower(check,map,(Building)device,outside);
        }
        var aramid=Panel(north,Rot4.South,PanelSlot.Reinforcement);
        var damage=new DamageInfo(new DamageDef{defName="FixtureDamage"},50,0,0);
        PanelCombat.Resolve(north,ref damage);
        check(damage.Amount==0&&aramid.HitPoints==200,"Aramid did not absorb a front hit as independent HP");
        damage=new DamageInfo(new DamageDef{defName="FixtureDamage"},50,0,90);
        PanelCombat.Resolve(north,ref damage);
        check(damage.Amount==50&&aramid.HitPoints==200,"Aramid protected an uncovered side");
    }
    static void CheckPower(Action<bool,string> check,Map map,Building inside,Building outside)
    {
        var props=new CompProperties_Power{compClass=typeof(CompPowerTrader)};
        var protectedPower=new CompPowerTrader{parent=inside,props=props};var exposedPower=new CompPowerTrader{parent=outside,props=props};
        inside.def.comps=new List<CompProperties>{props};outside.def=new ThingDef{thingClass=typeof(Building),comps=new List<CompProperties>{props}};
        Set(inside,"comps",new List<ThingComp>{protectedPower});Set(outside,"comps",new List<ThingComp>{exposedPower});
        Set(protectedPower,"powerOnInt",true);Set(protectedPower,"powerOutputInt",100f);
        Set(exposedPower,"powerOnInt",false);Set(exposedPower,"powerOutputInt",100f);
        check(protectedPower.PowerOn&&protectedPower.PowerOutput==100,"Local solar protection did not preserve eligible generation");
        check(!exposedPower.PowerOn&&exposedPower.PowerOutput==0,"Outside generator fed a shielded network");
        var batteryProps=new CompProperties_Battery{storedEnergyMax=100,efficiency=1};
        var a=new CompPowerBattery{parent=inside,props=batteryProps};var b=new CompPowerBattery{parent=outside,props=batteryProps};
        Set(a,"storedEnergy",10f);Set(b,"storedEnergy",10f);
        check(a.StoredEnergy==10&&b.StoredEnergy==0,"Unshielded battery was available during solar flare");
        b.DrawPower(5);check((float)AccessTools.Field(typeof(CompPowerBattery),"storedEnergy").GetValue(b)! == 10,"Solar blackout consumed an unavailable battery");
        a.DrawPower(5);check(a.StoredEnergy==5,"Protected battery did not supply stored energy");
        check(map.gameConditionManager.ElectricityDisabled(map),"Map-wide solar flare was removed");
        PowerNet net=Raw<PowerNet>();var manager=Raw<PowerNetManager>();Set(manager,"map",map);Set(net,"powerNetManager",manager);
        Set(net,"powerComps",new List<CompPowerTrader>{protectedPower,exposedPower});Set(net,"batteryComps",new List<CompPowerBattery>{a,b});Set(net,"givingBats",new List<CompPowerBattery>());
        net.PowerNetTick();
        check(protectedPower.PowerOn&&a.StoredEnergy>5,"Actual network tick did not run a protected generator and battery");
        check((float)AccessTools.Field(typeof(CompPowerBattery),"storedEnergy").GetValue(b)! == 10,"Protected network charged an outside battery");
        check(map.gameConditionManager.ElectricityDisabled(map),"Local exception changed the global map query");
        var facility=new CompFacilityInactiveWhenElectricityDisabled{parent=inside};
        check((bool)AccessTools.PropertyGetter(typeof(CompFacilityInactiveWhenElectricityDisabled),"CanBeActive").Invoke(facility,null)!,"Protected electrical facility retained the map-wide blackout");
        Set(net,"hasPowerSource",true);Set(net,"transmitters",new List<CompPower>{protectedPower,a});Set(protectedPower,"transNet",net);Set(exposedPower,"transNet",net);
        var auto=new FixtureAutoPowered{parent=inside};Set(auto,"compPowerCached",protectedPower);
        var outsideAuto=new FixtureAutoPowered{parent=outside};Set(outsideAuto,"compPowerCached",exposedPower);
        MethodInfo appears=AccessTools.PropertyGetter(typeof(CompAutoPowered),"AppearsPowered");
        check((bool)appears.Invoke(auto,null)!,"Protected automatic power still saw solar blackout");
        check(!(bool)appears.Invoke(outsideAuto,null)!,"Automatic-power immunity escaped to an outside device");
        map.gameConditionManager.ActiveConditions.Add(new BlackoutCondition{def=new GameConditionDef{defName="OtherBlackout"}});
        check(!(bool)appears.Invoke(auto,null)!,"Local device exception suppressed a non-solar blackout");
    }
}
