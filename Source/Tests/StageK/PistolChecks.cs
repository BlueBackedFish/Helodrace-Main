using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private IEnumerable<int> PistolChecks()
    {
        playMap=Find.CurrentMap;DebugSettings.enableStoryteller=false;DebugSettings.enableRandomMentalStates=false;
        var climate=new Harmony("helodrace.tests.stagek.pistol-climate");
        climate.Patch(AccessTools.PropertyGetter(typeof(MapTemperature),nameof(MapTemperature.OutdoorTemp)),prefix:new HarmonyMethod(typeof(Checks),nameof(Climate)));
        foreach(var pawn in playMap.mapPawns.AllPawnsSpawned.ToList())pawn.DeSpawn();
        foreach(var cell in CellRect.CenteredOn(playMap.Center,45))
        {
            foreach(var thing in cell.GetThingList(playMap).ToList())if(thing.Spawned&&thing.def.destroyable)thing.Destroy();
            playMap.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);playMap.roofGrid.SetRoof(cell,null);playMap.fogGrid.Unfog(cell);
        }
        playMap.weatherManager.curWeather=playMap.weatherManager.lastWeather=WeatherDefOf.Clear;
        playMap.skyManager.ForceSetCurSkyGlow(1);
        var rescuer=((IncidentWorker_WandererJoin)Def<IncidentDef>("StrangerInBlackJoin").Worker).GeneratePawn(playMap);
        GenSpawn.Spawn(rescuer,playMap.Center,playMap);fixtures.Add(rescuer);
        var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=999999;rescuer.jobs.StartJob(wait,JobCondition.InterruptForced);
        Check(Def<TraitDef>("HD_FrontierSixgunTraining").degreeDatas[0].label=="gunslinger","English trait name is gunslinger");
        var profiles=new[]{
            ("HD_Gun_ColtSAA_Weapon",25.9f,9.9f,2f,6),
            ("HD_Gun_ColtArmyModel1860_Weapon",22.9f,9.9f,3f,6),
            ("HD_Gun_SWModelThree_Weapon",23.9f,9.9f,1.5f,6),
            ("HD_Gun_M1911_Weapon",20.9f,13.9f,1f,2),
            ("HD_Gun_Glock19Gen5_Weapon",23.9f,18.9f,1f,1),
            ("HD_Gun_P320_Weapon",22f,25f,1f,1),
            ("Gun_Revolver",25.9f,28.9f,1f,1),
            ("Gun_Autopistol",25.9f,28.9f,1f,1),
            ("Gun_MachinePistol",19.9f,22.9f,1f,3),
            ("Gun_Revolver_Unique",25.9f,28.9f,1f,1)};
        foreach(var (name,ordinaryRange,modeRange,cooldownMultiplier,burst) in profiles)
        {
            if(DefDatabase<ThingDef>.GetNamedSilentFail(name)==null){Check(name=="Gun_Revolver_Unique","Only the optional Odyssey revolver may be absent");continue;}
            rescuer.stances.CancelBusyStanceSoft();Gun(rescuer,name);
            var gun=rescuer.equipment.Primary;var comp=gun.GetComp<CompSharpshooterWeapon>();
            Check(comp!=null&&comp.CanUseSharpshooterMode,"Native Helod can use pistol mode: "+name);
            var normal=comp.SelectedVerb;
            Check(Math.Abs(normal.verbProps.range-ordinaryRange)<.001f,"Normal pistol range is unchanged: "+name);
            float normalEffective=normal.EffectiveRange,ordinaryCooldown=gun.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
            Check(comp.TrySetMode(true),"Pistol mode switches on through production API: "+name);
            var alt=comp.SelectedVerb;
            Check(alt==comp.AltVerb&&Math.Abs(alt.verbProps.range-modeRange)<.001f,"Mode base range is extended by three cells: "+name);
            Check(alt.verbProps.burstShotCount==burst,"Pistol mode retains its intended firing pattern: "+name);
            float rangeMultiplier=gun.GetStatValue(StatDefOf.RangedWeapon_RangeMultiplier);
            Check(Math.Abs(alt.EffectiveRange-modeRange*rangeMultiplier)<.01f,"Actual mode range includes current modular/weapon multipliers once: "+name);
            Check(Math.Abs(gun.GetStatValue(StatDefOf.RangedWeapon_Cooldown)/ordinaryCooldown-cooldownMultiplier)<.001f,"Actual mode cooldown has the requested multiplier: "+name);
            float expectedCooldown=ordinaryCooldown*cooldownMultiplier*rescuer.GetStatValue(StatDefOf.RangedCooldownFactor);
            Check(Math.Abs(alt.verbProps.AdjustedCooldown(alt,rescuer)-expectedCooldown)<.001f,"Actual ranged cooldown combines gunslinger and mode: "+name);
            int distance=(int)MathF.Floor(alt.EffectiveRange-.05f);
            var target=rescuer.Position+new IntVec3(distance,0,0);
            Check(distance>(modeRange-3)*rangeMultiplier&&!alt.OutOfRange(rescuer.Position,target,new CellRect(target.x,target.z,1,1)),"Native range test permits a shot beyond the previous mode limit: "+name);
            var outside=rescuer.Position+new IntVec3((int)MathF.Ceiling(alt.EffectiveRange)+2,0,0);
            Check(alt.OutOfRange(rescuer.Position,outside,new CellRect(outside.x,outside.z,1,1)),"Native range test still rejects targets beyond the new limit: "+name);
            int before=(int)rescuer.records.GetValue(RecordDefOf.ShotsFired);
            Check(alt.TryStartCastOn(target,canHitNonTargetPawns:false,preventFriendlyFire:true),"Actual pistol cast starts in the added range: "+name);
            for(int tick=0;tick<900&&(rescuer.records.GetValue(RecordDefOf.ShotsFired)==before||alt.Bursting||rescuer.stances.curStance is Stance_Busy);tick+=30)yield return 30;
            Check(rescuer.records.GetValue(RecordDefOf.ShotsFired)>before&&!alt.Bursting&&!(rescuer.stances.curStance is Stance_Busy),"Actual native firing cycle completes in the extended range: "+name);
            Check(comp.TrySetMode(false)&&comp.SelectedVerb==normal&&Math.Abs(normal.EffectiveRange-normalEffective)<.001f,"Switching back restores unchanged normal fire: "+name);
        }
        rescuer.Destroy();climate.UnpatchAll(climate.Id);
    }
}
