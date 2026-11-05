using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using RimWorld;
using Verse;

namespace Helodrace.Tests.StageI;

public sealed partial class Checks
{
    private void ResearchChecks()
    {
        var policy=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"research-policy.json"))).RootElement;
        var changes=policy.GetProperty("changes").EnumerateArray().Select(row=>(project:Def<ResearchProjectDef>(row.GetProperty("name").GetString()),
            vanilla:row.GetProperty("source").GetString()=="ludeon.rimworld", before:row.GetProperty("base_before").GetSingle(), after:row.GetProperty("base_after").GetSingle())).ToList();
        Check(!ScenPart_ResearchTechBaseline.In(Find.Scenario), "Research checks start outside a Helod scenario");
        foreach(var row in changes)
            Check(row.project.baseCost==(row.vanilla?row.before:row.after), "Non-Helod scenario keeps base-game budget and Helod tree budget: "+row.project.defName);
        var electricity=Def<ResearchProjectDef>("Electricity");
        Check(electricity.CostFactor(TechLevel.Industrial)==1, "Non-Helod scenario retains native industrial cost factor");
        Current.Game.Scenario=Def<ScenarioDef>("HD_Scenario_WWTech").scenario;
        foreach(var row in changes)
            Check(row.project.baseCost==row.after, "Helod scenario research base budget: "+row.project.defName);
        Check(Def<FactionDef>("HD_HelodPlayerColony").techLevel==TechLevel.Industrial, "Research penalty preserves actual faction industrial identity");
        foreach(var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p=>p.baseCost>0))
            Check(project.CostFactor(Faction.OfPlayer.def.techLevel)==project.CostFactor(TechLevel.Neolithic), "Native tribal tech penalty: "+project.defName);
        Check(electricity.Cost==11100 && electricity.CostApparent==22200, "UI apparent cost includes tech penalty exactly once");
        Check(Def<ResearchProjectDef>("HelodHighElectronics").Cost==1500 && Def<ResearchProjectDef>("HelodHighElectronics").CostApparent==3000, "Helod milestone has its own budget and the same tribal factor");
        var manager=Find.ResearchManager;
        manager.ResetAllProgress(); DebugSettings.fastResearch=false; Find.Storyteller.difficulty.researchSpeedFactor=1;
        manager.SetCurrentProject(electricity); manager.ResearchPerformed(200,worker);
        Check(Math.Abs(manager.GetProgress(electricity)-.825f)<.001 && !electricity.IsFinished, "Normal research earns half industrial progress under native tribal cost factor");
        manager.ResearchPerformed((electricity.Cost-manager.GetProgress(electricity)+.1f)*2/.00825f,worker);
        Check(electricity.IsFinished && manager.GetProgress(electricity)==electricity.baseCost && electricity.ProgressApparent==electricity.CostApparent, "Normal completion and full UI bar agree without overfilling raw progress");
        Check(Def<ResearchProjectDef>("HelodHighElectronics").IsFinished && !Def<ResearchProjectDef>("HelodTransistor").IsFinished, "Normal linked electricity completes its counterpart without skipping transistors");
        manager.ResetAllProgress();
        var micro=Def<ResearchProjectDef>("MicroelectronicsBasics"); manager.FinishProject(micro,doCompletionDialog:false);
        Check(micro.IsFinished && micro.ProgressReal==micro.baseCost && micro.ProgressApparent==micro.CostApparent && Def<ResearchProjectDef>("HelodTransistor").IsFinished, "Explicit completion follows native thresholds and synchronizes both research trees");
        manager.DebugSetAllProjectsFinished();
        Check(DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p=>p.baseCost>0).All(p=>p.IsFinished && p.ProgressApparent==p.CostApparent), "Instant finish-all completes every research point project and fills apparent-cost bars");
        Write("UI opening the actual research tab for one update before save/load");
    }
}
