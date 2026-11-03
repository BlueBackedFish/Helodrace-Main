using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using Helodrace;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Analysis.T232;

// Analysis-only module; no product patch alters aim, collision, armor or injuries.
public sealed class DamageBench : GameComponent
{
    private static DamageBench active;
    private static Projectile_ShotgunPellet impacting;
    private readonly HashSet<int> hits = new(), damaging = new();
    private readonly Dictionary<int,int> damageCalls = new();
    private float damage;
    private bool started, finished;
    private Map map;
    private Pawn shooter, victim;
    private string output;
    private int caseIndex, repeat, samples = 128;
    private ThingWithComps weapon;
    private Verb verb;
    private CompSharpshooterWeapon mode;
    private readonly List<Case> cases = new();
    private readonly List<object> specs = new();
    private sealed record Case(string weapon, bool alt, int armor, int distance);
    private static T Def<T>(string name) where T:Verse.Def => DefDatabase<T>.GetNamed(name);
    private static readonly string[][] Armor = { Array.Empty<string>(),
        new[]{"Apparel_FlakVest","Apparel_SimpleHelmet"},
        new[]{"Apparel_PowerArmor","Apparel_PowerArmorHelmet"} };
    public DamageBench(Game game)
    {
        if(GenCommandLine.TryGetCommandLineArg("hdT232",out string folder))
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder,"constructor.txt"),"Analysis component constructed\n");
        }
    }
    public override void GameComponentTick() { if(!started && !finished) GameComponentUpdate(); }
    private void Status(string value) => File.AppendAllText(Path.Combine(output,"status.txt"),value+"\n");
    private static string Csv(params object[] cells) => string.Join(",",cells.Select(v=>
        "\""+(v is IFormattable f?f.ToString(null,CultureInfo.InvariantCulture):v?.ToString()??"").Replace("\"","\"\"")+"\""));
    private static void ImpactPrefix(Projectile_ShotgunPellet __instance,Thing hitThing,bool blockedByShield,ref Projectile_ShotgunPellet __state)
    {
        __state=impacting;impacting=__instance;
        if(active?.victim!=null && !blockedByShield && (hitThing==active.victim || hitThing is Corpse c && c.InnerPawn==active.victim)) active.hits.Add(__instance.thingIDNumber);
    }
    private static Exception ImpactFinalizer(Exception __exception,Projectile_ShotgunPellet __state) {impacting=__state;return __exception;}
    private static void DamagePostfix(Thing __instance,DamageWorker.DamageResult __result)
    {
        if(active?.victim!=__instance || impacting==null)return;
        active.damage+=__result.totalDamageDealt;
        if(__result.totalDamageDealt<=0)return;
        int id=impacting.thingIDNumber;active.damaging.Add(id);active.damageCalls[id]=active.damageCalls.GetValueOrDefault(id)+1;
    }
    private static void Body(Pawn p) {if(p.story?.bodyType==null && p.story!=null)p.story.bodyType=BodyTypeDefOf.Male;}
    private Pawn Generate(IntVec3 cell,int seed)
    {
        Pawn pawn;
        Rand.PushState(seed);
        try { pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,null,
            forceGenerateNewPawn:true,mustBeCapableOfViolence:true,canGeneratePawnRelations:false,
            fixedBiologicalAge:25,fixedChronologicalAge:25,allowAddictions:false,
            forcedXenotype:XenotypeDefOf.Baseliner,fixedGender:Gender.Male){ForceNoBackstory=true}); }
        finally {Rand.PopState();}
        Body(pawn);pawn.apparel.DestroyAll();pawn.equipment.DestroyAllEquipment();pawn.health.RemoveAllHediffs();
        pawn.story?.traits.allTraits.Clear();pawn.ideo?.SetIdeo(null);
        GenSpawn.Spawn(pawn,cell,map);
        var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=999999;pawn.jobs.StartJob(wait,JobCondition.InterruptForced);
        return pawn;
    }
    public override void GameComponentUpdate()
    {
        if(finished || Current.ProgramState!=ProgramState.Playing || Find.CurrentMap==null
            || !GenCommandLine.TryGetCommandLineArg("hdT232",out output))return;
        try
        {
            Find.TickManager.Pause();
            if(!started) {Setup();started=true;return;}
            if(caseIndex==cases.Count)
            {
                File.WriteAllText(Path.Combine(output,"specs.json"),JsonSerializer.Serialize(specs,new JsonSerializerOptions{WriteIndented=true}));
                Status("COMPLETE");finished=true;Waylith.EngineApplication.Instance.Quit(0);return;
            }
            RunOne();
        }
        catch(Exception e) {finished=true;Status("FAIL "+e);Log.Error("T2-32 chart benchmark: "+e);Waylith.EngineApplication.Instance.Quit(1);}
    }
    private void Setup()
    {
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"status.txt"),"T2-32 actual native single-shell benchmark\n");
        if(GenCommandLine.TryGetCommandLineArg("hdT232Samples",out string count))samples=int.Parse(count);
        map=Find.CurrentMap;DebugSettings.enableStoryteller=false;DebugSettings.noAnimals=true;DebugSettings.enableRandomMentalStates=false;
        foreach(var pawn in map.mapPawns.AllPawnsSpawned.ToList())pawn.DeSpawn();
        foreach(var cell in CellRect.CenteredOn(map.Center,24))
        {
            foreach(var thing in cell.GetThingList(map).ToList())if(thing.Spawned && thing.def.destroyable)thing.Destroy();
            map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.roofGrid.SetRoof(cell,null);map.fogGrid.Unfog(cell);
        }
        map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
        shooter=Generate(map.Center+new IntVec3(0,0,-12),70012);shooter.SetFaction(Faction.OfPlayer);
        shooter.genes.AddGene(Def<GeneDef>("HD_Gene_Sharpshooter"),true);
        if(shooter.drafter!=null)shooter.drafter.Drafted=true;
        foreach(string gun in new[]{"HD_Gun_WinM1887_Weapon","HD_Gun_M1897_Weapon"})
            foreach(bool alt in new[]{false,true})foreach(int armor in new[]{0,1,2})foreach(int distance in new[]{3,7,15})cases.Add(new(gun,alt,armor,distance));
        File.WriteAllText(Path.Combine(output,"shots.csv"),"case,weapon,alt,armor,distance,repeat,seed,hits,damaging_pellets,damage,down_or_dead,dead,vital_injury,duplicate_damage_pellets,launched_pellets,damage_budget\n");
        var harmony=new Harmony("helodrace.analysis.t232");
        harmony.Patch(AccessTools.Method(typeof(Projectile_ShotgunPellet),"Impact"),prefix:new HarmonyMethod(typeof(DamageBench),nameof(ImpactPrefix)),finalizer:new HarmonyMethod(typeof(DamageBench),nameof(ImpactFinalizer)));
        harmony.Patch(AccessTools.Method(typeof(Thing),nameof(Thing.TakeDamage)),postfix:new HarmonyMethod(typeof(DamageBench),nameof(DamagePostfix)));
        active=this;Status("Samples per valid condition: "+samples);
    }
    private void RunOne()
    {
        var c=cases[caseIndex];
        if(victim!=null) {if(victim.Corpse!=null)victim.Corpse.Destroy();else if(!victim.Destroyed)victim.Destroy();victim=null;}
        if(repeat==0)
        {
            shooter.equipment.DestroyAllEquipment();weapon=(ThingWithComps)ThingMaker.MakeThing(Def<ThingDef>(c.weapon));
            weapon.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal,ArtGenerationContext.Colony);shooter.equipment.AddEquipment(weapon);
            mode=weapon.GetComp<CompSharpshooterWeapon>();
            if(!mode.TrySetMode(c.alt))throw new InvalidOperationException("Mode switch failed");
            verb=mode.SelectedVerb;verb.caster=shooter;
        }
        // Long sweeps must not accumulate wandering, wounds or unrelated hive/animal interference.
        foreach(var other in map.mapPawns.AllPawnsSpawned.Where(p=>p!=shooter).ToList())other.DeSpawn();
        shooter.pather.StopDead();shooter.Position=map.Center+new IntVec3(0,0,-12);
        shooter.health.RemoveAllHediffs();
        var hold=JobMaker.MakeJob(JobDefOf.Wait);hold.expiryInterval=999999;
        shooter.jobs.StartJob(hold,JobCondition.InterruptForced);
        victim=Generate(shooter.Position+new IntVec3(0,0,c.distance),70013);
        victim.pather.StopDead();
        foreach(string name in Armor[c.armor])
        {
            var def=Def<ThingDef>(name);var apparel=(Apparel)ThingMaker.MakeThing(def,GenStuff.DefaultStuffFor(def));
            apparel.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal,ArtGenerationContext.Colony);victim.apparel.Wear(apparel,false);
        }
        shooter.skills.GetSkill(SkillDefOf.Shooting).Level=10;
        foreach(var pawn in new[]{shooter,victim})
        {
            foreach(var need in pawn.needs.AllNeeds)need.CurLevel=need.MaxLevel;
            foreach(var stat in DefDatabase<StatDef>.AllDefsListForReading)stat.Worker.ClearCacheForThing(pawn);
        }
        map.weatherManager.curWeather=map.weatherManager.lastWeather=WeatherDefOf.Clear;map.skyManager.ForceSetCurSkyGlow(1);
        shooter.Rotation=Rot4.North;victim.Rotation=Rot4.South;
        AccessTools.FieldRefAccess<Verb,LocalTargetInfo>("currentTarget")(verb)=victim;
        bool available=c.distance<=verb.verbProps.range;
        if(repeat==0)
        {
            var props=(VerbProperties_Shotgun)verb.verbProps;
            float warmup=(float)AccessTools.PropertyGetter(typeof(Verb),"WarmupTime").Invoke(verb,null);
            float cooldown=verb.verbProps.AdjustedCooldown(verb,shooter);
            var projectile=verb.verbProps.defaultProjectile;
            var settings=projectile.GetModExtension<ShotgunProjectileExtension>();
            specs.Add(new{index=caseIndex,c.weapon,c.alt,c.armor,c.distance,available,range=props.range,
                warmup_seconds=warmup,cooldown_seconds=cooldown,cycle_seconds=warmup+cooldown,
                aim_chance=ShotReport.HitReportFor(shooter,verb,victim).AimOnTargetChance_StandardTarget,
                pellets=props.pelletCount,spread_sigma_degrees=props.spreadAngle,projectile=projectile.defName,
                base_damage=projectile.projectile.GetDamageAmount(weapon),base_AP=projectile.projectile.GetArmorPenetration(weapon),
                energy_half_distance=settings?.energyHalfDistance,
                armor_items=victim.apparel.WornApparel.Select(a=>new{name=a.def.defName,sharp=a.GetStatValue(StatDefOf.ArmorRating_Sharp),blunt=a.GetStatValue(StatDefOf.ArmorRating_Blunt),stuff=a.Stuff?.defName}).ToArray(),
                shooter_skill=10,incoming_damage_factor=victim.GetStatValue(StatDefOf.IncomingDamageFactor),body_size=victim.BodySize,shooter_accuracy=shooter.GetStatValue(StatDefOf.ShootingAccuracyPawn)});
            Status("CASE "+caseIndex+" "+c+" available="+available);
        }
        if(!available) {caseIndex++;repeat=0;return;}
        damage=0;hits.Clear();damaging.Clear();damageCalls.Clear();
        int seed=90000+repeat;Rand.PushState(seed);
        try
        {
            if(!(bool)AccessTools.Method(typeof(Verb_ShootShotgun),"TryCastShot").Invoke(verb,null))throw new InvalidOperationException("Shot failed: case="+caseIndex+" repeat="+repeat+" shooter="+shooter.Position+" alive="+!shooter.Dead+" spawned="+shooter.Spawned+" down="+shooter.Downed+" target="+victim.Position+" targetAlive="+!victim.Dead+" canHit="+verb.CanHitTarget(victim)+" range="+verb.verbProps.range+" mode="+mode.altModeActive);
            var launched=map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile_ShotgunPellet>().Where(p=>p.Launcher==shooter).ToArray();
            if(launched.Length!=9)throw new InvalidOperationException("Expected nine pellets, got "+launched.Length);
            float budget=launched.Sum(p=>p.DamageAmount)*victim.GetStatValue(StatDefOf.IncomingDamageFactor);
            int tick=0;
            while(launched.Any(p=>!p.Destroyed) && tick++<240)Find.TickManager.DoSingleTick();
            if(launched.Any(p=>!p.Destroyed))throw new InvalidOperationException("Projectile flight timed out");
            bool vital=victim.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(i=>new[]{"Brain","Heart","Lung","Liver","Kidney"}.Contains(i.Part?.def.defName));
            File.AppendAllText(Path.Combine(output,"shots.csv"),Csv(caseIndex,c.weapon,c.alt,c.armor,c.distance,repeat,seed,hits.Count,damaging.Count,damage,
                victim.Downed||victim.Dead,victim.Dead,vital,damageCalls.Count(p=>p.Value>1),launched.Length,budget)+"\n");
        }
        finally {Rand.PopState();}
        repeat++;
        if(repeat==samples) {Status("DONE case="+caseIndex+" samples="+samples);caseIndex++;repeat=0;}
    }
}
