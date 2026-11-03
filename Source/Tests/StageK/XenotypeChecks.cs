using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private static readonly string[] Lineages={"HD_MackenzieValleyHelod","HD_TexasHelod","HD_ArcticHelod","HD_EasternHelod","HD_MexicanHelod"};
    private static Pawn temperaturePawn;
    private static float lineageTemperature;
    private static bool LineageTemperature(Thing __instance,ref float __result)
    {if(__instance!=temperaturePawn)return true;__result=lineageTemperature;return false;}
    private Pawn LineagePawn(string name,Gender gender)
        =>PawnGenerator.GeneratePawn(new PawnGenerationRequest(Def<PawnKindDef>("HD_WW_HelodColonist"),Faction.OfPlayer,
            forceGenerateNewPawn:true,canGeneratePawnRelations:false,allowAddictions:false,
            fixedBiologicalAge:25,fixedChronologicalAge:25,fixedGender:gender,forcedXenotype:Def<XenotypeDef>(name),
            forceNoIdeo:true,forceNoBackstory:true,forceNoGear:true));
    private static float Stat(Pawn pawn,StatDef stat)=>stat.Worker.GetValue(StatRequest.For(pawn));
    private IEnumerable<int> XenotypeLifeChecks()
    {
        GeneAudit();playMap=Find.CurrentMap;DebugSettings.enableStoryteller=false;
        foreach(var other in playMap.mapPawns.AllPawnsSpawned.ToList())other.DeSpawn();
        foreach(var cell in CellRect.CenteredOn(playMap.Center,28))
        {
            foreach(var thing in cell.GetThingList(playMap).ToList())if(thing.Spawned&&thing.def.destroyable)thing.Destroy();
            playMap.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);playMap.roofGrid.SetRoof(cell,null);playMap.fogGrid.Unfog(cell);
        }
        var males=new List<Pawn>();var females=new List<Pawn>();var summaries=new List<object>();
        var positions=new[]{new IntVec3(-15,0,-15),new IntVec3(15,0,-15),new IntVec3(-15,0,15),new IntVec3(15,0,15),IntVec3.Zero};
        var expectedMetabolism=new[]{-2,0,-1,2,-3};
        for(int i=0;i<Lineages.Length;i++)
        {
            string name=Lineages[i];var xeno=Def<XenotypeDef>(name);var pawn=LineagePawn(name,Gender.Male);var mother=LineagePawn(name,Gender.Female);
            males.Add(pawn);females.Add(mother);GenSpawn.Spawn(pawn,playMap.Center+positions[i],playMap);fixtures.Add(pawn);
            Check(HelodRace.IsHelod(pawn)&&pawn.genes.Xenotype==xeno&&xeno.inheritable,"Explicit lineage creates the intended heritable Helod: "+name);
            File.WriteAllText(Path.Combine(output,name+"-generated.json"),JsonSerializer.Serialize(new{expected=xeno.genes.Select(g=>g.defName),endogenes=pawn.genes.Endogenes.Select(g=>g.def.defName),xenogenes=pawn.genes.Xenogenes.Select(g=>g.def.defName)},new JsonSerializerOptions{WriteIndented=true}));
            Check(pawn.genes.Xenogenes.Count==0&&pawn.genes.Endogenes.Count==xeno.genes.Count,"Lineage genes are endogenes without accidental extra genes: "+name+" endogenes="+string.Join(",",pawn.genes.Endogenes.Select(g=>g.def.defName))+" xenogenes="+string.Join(",",pawn.genes.Xenogenes.Select(g=>g.def.defName)));
            Check(pawn.genes.HasActiveGene(Def<GeneDef>("HD_Gene_BTXDependency"))&&pawn.genes.HasActiveGene(Def<GeneDef>("HD_Gene_Sharpshooter")),"Shared BTX and sharpshooter genes remain active: "+name);
            Check(xeno.genes.Distinct().Count()==xeno.genes.Count&&!xeno.genes.Any(a=>xeno.genes.Any(b=>a!=b&&a.ConflictsWith(b))),"Lineage has no duplicate or conflicting genes: "+name);
            int metabolism=xeno.genes.Sum(g=>g.biostatMet);
            Check(metabolism==expectedMetabolism[i]&&metabolism>=-5&&metabolism<=5,"Loaded native metabolic budget is valid: "+name+" = "+metabolism);
            summaries.Add(new{name,metabolism,foodFactorFromMetabolism=GeneTuning.MetabolismToFoodConsumptionFactorCurve.Evaluate(metabolism),
                genes=xeno.genes.Select(g=>g.defName),aptitudes=pawn.skills.skills.ToDictionary(s=>s.def.defName,s=>s.Aptitude)});
        }
        File.WriteAllText(Path.Combine(output,"lineages.json"),JsonSerializer.Serialize(summaries,new JsonSerializerOptions{WriteIndented=true}));
        Check(males[0].skills.GetSkill(SkillDefOf.Medicine).Aptitude==4&&males[0].skills.GetSkill(SkillDefOf.Social).Aptitude==8,"Mackenzie supplies strong medicine and remarkable leadership");
        Check(males[1].skills.GetSkill(SkillDefOf.Plants).Aptitude==4&&males[1].skills.GetSkill(SkillDefOf.Animals).Aptitude==4,"Texas supplies farming and animal handling");
        Check(males[2].skills.GetSkill(SkillDefOf.Shooting).Aptitude==8,"Arctic specializes in shooting aptitude");
        Check(males[3].skills.GetSkill(SkillDefOf.Intellectual).Aptitude==4&&males[3].skills.GetSkill(SkillDefOf.Crafting).Aptitude==4,"Eastern supplies research and crafting");
        Check(males[4].skills.GetSkill(SkillDefOf.Shooting).Aptitude==4&&males[4].skills.GetSkill(SkillDefOf.Melee).Aptitude==4,"Mexican mercenary specializes in ranged and melee combat");
        Check(males[4].genes.HasActiveGene(Def<GeneDef>("HD_Gene_AmphetamineDependency"))&&males.Take(4).All(p=>!p.genes.HasActiveGene(Def<GeneDef>("HD_Gene_AmphetamineDependency"))),"Only the mercenary lineage needs the additional stimulant supply");
        var shared=Def<GeneDef>("HD_Gene_Sharpshooter");
        Check(shared.biostatArc==0&&shared.statOffsets.Single(s=>s.stat==StatDefOf.ShootingAccuracyPawn).value==1.5f&&shared.statFactors.Single().value==.9f,"Common sharpshooter combat values are unchanged and archite-free inheritance is enabled");
        var scenario=Def<ScenarioDef>("HD_Scenario_WWTech").scenario;
        var config=scenario.AllParts.OfType<ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes>().Single();
        var configField=AccessTools.Field(config.GetType(),"xenotypeCounts");
        var counts=(System.Collections.IEnumerable)configField.GetValue(config);var starters=new List<string>();
        foreach(var item in counts){var type=item.GetType();var x=(XenotypeDef)AccessTools.Field(type,"xenotype").GetValue(item);starters.Add(x.defName);Check((int)AccessTools.Field(type,"count").GetValue(item)==1&&(bool)AccessTools.Field(type,"requiredAtStart").GetValue(item),"Native starting lineup requires one "+x.defName);}
        Check(starters.OrderBy(x=>x).SequenceEqual(Lineages.Take(4).OrderBy(x=>x)),"Native scenario retains the four complementary starting lineages");
        yield return 1;
        foreach(int ticks in LineageConditions(males))yield return ticks;
        LineageWeights();
        for(int father=0;father<5;father++)for(int mother=0;mother<5;mother++)
        {
            var inherited=PregnancyUtility.GetInheritedGenes(males[father],females[mother],out bool success);
            Check(success&&inherited.Contains(Def<GeneDef>("HD_Gene_BTXDependency"))&&inherited.Contains(shared),"Native inheritance retains shared identity for "+father+"/"+mother+" success="+success+" genes="+string.Join(",",inherited.Select(g=>g.defName)));
        }
        var babyGenes=PregnancyUtility.GetInheritedGenes(males[1],females[3]);
        var newborn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(Def<PawnKindDef>("HD_WW_HelodColonist"),Faction.OfPlayer,
            forceGenerateNewPawn:true,allowDowned:true,canGeneratePawnRelations:false,fixedBiologicalAge:0,fixedChronologicalAge:0,
            fixedGender:Gender.Female,forcedEndogenes:babyGenes,developmentalStages:DevelopmentalStage.Newborn,
            forceNoGear:true,forceNoBackstory:true,forceNoIdeo:true));
        var actualGenes=newborn.genes.Endogenes.Select(g=>g.def).ToHashSet();
        File.WriteAllText(Path.Combine(output,"newborn-genes.json"),JsonSerializer.Serialize(new{inherited=babyGenes.Select(g=>g.defName),actual=actualGenes.Select(g=>g.defName),xenotype=newborn.genes.Xenotype.defName},new JsonSerializerOptions{WriteIndented=true}));
        Check(babyGenes.All(actualGenes.Contains)&&actualGenes.Except(babyGenes).All(g=>g==newborn.genes.GetHairColorGene())&&newborn.genes.Xenotype==XenotypeDefOf.Baseliner,"Actual mixed-parent newborn preserves inherited genes without receiving an unrelated adult lineage; native hair completion is allowed");
        newborn.Destroy();foreach(var p in males.Concat(females))p.Destroy();
    }
    private IEnumerable<int> LineageConditions(List<Pawn> pawns)
    {
        var arctic=pawns[2];temperaturePawn=arctic;var h=new Harmony("helodrace.tests.stagek.lineage-temperature");
        h.Patch(AccessTools.PropertyGetter(typeof(Thing),nameof(Thing.AmbientTemperature)),prefix:new HarmonyMethod(typeof(Checks),nameof(LineageTemperature)));
        lineageTemperature=11;float ordinary=Stat(arctic,StatDefOf.AimingDelayFactor);
        lineageTemperature=10;Check(Math.Abs(Stat(arctic,StatDefOf.AimingDelayFactor)/ordinary-.85f)<.001f,"Arctic cold-field aiming activates at ten degrees");
        playMap.roofGrid.SetRoof(arctic.Position,RoofDefOf.RoofConstructed);Check(Math.Abs(Stat(arctic,StatDefOf.AimingDelayFactor)-ordinary)<.001f,"Arctic cold-field aiming does not apply under a roof");
        playMap.roofGrid.SetRoof(arctic.Position,null);lineageTemperature=10.01f;Check(Math.Abs(Stat(arctic,StatDefOf.AimingDelayFactor)-ordinary)<.001f,"Arctic aiming bonus stops above ten degrees");
        h.UnpatchAll(h.Id);temperaturePawn=null;
        var mercenary=pawns[4];var friend=pawns[1];var far=friend.Position;
        float dodge=Stat(mercenary,StatDefOf.MeleeDodgeChance),shock=Stat(mercenary,StatDefOf.PainShockThreshold);
        float rawDodge=StatDefOf.MeleeDodgeChance.Worker.GetValue(StatRequest.For(mercenary),false);
        friend.Position=mercenary.Position+new IntVec3(8,0,0);
        Check(new ConditionalStatAffecter_HelodComrades().Applies(StatRequest.For(mercenary))&&Math.Abs(StatDefOf.MeleeDodgeChance.Worker.GetValue(StatRequest.For(mercenary),false)-rawDodge-5)<.001f&&Math.Abs(Stat(mercenary,StatDefOf.PainShockThreshold)-shock-.1f)<.001f,"Mercenary cohesion adds five native dodge points and ten percent pain-shock threshold at eight cells");
        friend.Position+=new IntVec3(0,0,1);Check(Math.Abs(Stat(mercenary,StatDefOf.MeleeDodgeChance)-dodge)<.001f,"Mercenary cohesion stops outside its eight-cell radius");
        friend.Position=mercenary.Position+IntVec3.East;friend.SetFaction(Faction.OfAncientsHostile);
        Check(Math.Abs(Stat(mercenary,StatDefOf.MeleeDodgeChance)-dodge)<.001f,"A hostile Helod does not grant mercenary cohesion");
        friend.SetFaction(Faction.OfPlayer);var anesthesia=friend.health.AddHediff(HediffDefOf.Anesthetic);
        Check(friend.Downed&&Math.Abs(Stat(mercenary,StatDefOf.MeleeDodgeChance)-dodge)<.001f,"A downed allied Helod does not grant mercenary cohesion");
        friend.health.RemoveHediff(anesthesia);friend.Position=far;
        var human=Player(mercenary.Position+IntVec3.West);Check(!HelodRace.IsHelod(human)&&Math.Abs(Stat(mercenary,StatDefOf.MeleeDodgeChance)-dodge)<.001f,"A human ally does not accidentally grant Helod-only cohesion");human.Destroy();
        yield return 1;
    }
    private void LineageWeights()
    {
        var profiles=new[]{("HD_WW_HelodColonist",new[]{.30f,.30f,.10f,.25f,.05f}),
            ("HD_MW_HelodRifleman",new[]{.25f,.10f,.20f,.05f,.40f}),
            ("HD_WW_HelodUpperClass",new[]{.20f,.15f,.05f,.55f,.05f}),
            ("HD_GW_HelodSniper",new[]{.15f,.15f,.55f,.05f,.10f})};
        foreach(var (name,expected) in profiles)
        {
            var kind=Def<PawnKindDef>(name);var choices=PawnGenerator.XenotypesAvailableFor(kind,Faction.OfPlayer.def,Faction.OfPlayer);
            for(int i=0;i<5;i++)Check(choices.TryGetValue(Def<XenotypeDef>(Lineages[i]),out float weight)&&Math.Abs(weight-expected[i])<.001,"Native role weight is correct: "+name+"/"+Lineages[i]);
            var sampled=new int[5];Rand.PushState(270113);
            try{for(int i=0;i<10000;i++)sampled[Array.IndexOf(Lineages,HelodXenotypeSelection.Choose(kind,Faction.OfPlayer).defName)]++;}finally{Rand.PopState();}
            for(int i=0;i<5;i++)Check(Math.Abs(sampled[i]/10000f-expected[i])<.02,"Seeded native selector follows role weight: "+name+"/"+Lineages[i]);
            var generated=PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind,Faction.OfPlayer,forceGenerateNewPawn:true,forceNoGear:true,canGeneratePawnRelations:false));
            Check(HelodRace.IsHelod(generated)&&Lineages.Contains(generated.genes.Xenotype.defName),"Actual unforced role generation stays within Helod lineages: "+name);generated.Destroy();
        }
    }
    private void GeneAudit()
    {
        var genes=DefDatabase<GeneDef>.AllDefsListForReading.Select(g=>new{
            name=g.defName,complexity=g.biostatCpx,metabolism=g.biostatMet,archites=g.biostatArc,
            exclusions=g.exclusionTags,
            offsets=g.statOffsets?.Select(s=>new{name=s.stat.defName,value=s.value}),
            factors=g.statFactors?.Select(s=>new{name=s.stat.defName,value=s.value})});
        File.WriteAllText(Path.Combine(output,"genes.json"),JsonSerializer.Serialize(genes,new JsonSerializerOptions{WriteIndented=true}));
        Check(true,"Exported actual loaded native genes and biostats for redesign budgeting");
    }
}
