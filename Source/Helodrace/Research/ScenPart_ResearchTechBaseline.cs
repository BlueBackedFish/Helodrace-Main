using System.Collections.Generic;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace;

// Marks a Helod scenario. Its research rules (the medieval tech penalty and the
// linked base-game budgets) apply only while the current scenario has this part.
public sealed class ScenPart_ResearchTechBaseline : ScenPart
{
    public override string Summary(Scenario scen) => "HD_ResearchTechBaseline_Summary".Translate();

    public static bool In(Scenario scenario)
    {
        if (scenario == null) return false;
        foreach (ScenPart part in scenario.AllParts)
            if (part is ScenPart_ResearchTechBaseline) return true;
        return false;
    }
}

// Preserve the actual faction's industrial identity. Only research uses the
// native penalty for a Medieval colony (Industrial and later 1.5; the engine caps the gap at Industrial).
// Native progress, apparent-cost UI and explicit completion stay in agreement.
[HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.CostFactor))]
public static class Patch_ResearchTechBaseline
{
    [HarmonyPostfix]
    public static void Postfix(ResearchProjectDef __instance, TechLevel researcherTechLevel, ref float __result)
    {
        // Also prevents recursion when asking the engine for its medieval factor.
        if (researcherTechLevel <= TechLevel.Medieval || Current.Game == null) return;
        TechLevel playerTech = Faction.OfPlayerSilentFail?.def.techLevel ?? TechLevel.Industrial;
        if (researcherTechLevel != playerTech || !ScenPart_ResearchTechBaseline.In(Find.Scenario)) return;
        __result = System.MathF.Max(__result, __instance.CostFactor(TechLevel.Medieval));
    }
}

// Base-game research budgets for Helod scenarios. XML: <ProjectDefName>baseCost</ProjectDefName>.
public sealed class HelodScenarioResearchBudgetDef : Def
{
    public List<ResearchBudget> baseCosts = new List<ResearchBudget>();

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        foreach (ResearchBudget budget in baseCosts)
            if (budget.project == null || budget.baseCost <= 0) yield return "Research budget needs a project and a positive base cost";
    }
}

public sealed class ResearchBudget
{
    public ResearchProjectDef project;
    public float baseCost;

    public void LoadDataFromXmlCustom(XmlNode xmlRoot)
    {
        DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(this, nameof(project), xmlRoot.Name);
        baseCost = ParseHelper.FromString<float>(xmlRoot.FirstChild.Value);
    }
}

// baseCost is shared def state, so it follows whichever scenario the current game uses:
// Helod budgets in a Helod scenario, the costs loaded from XML everywhere else.
public static class HelodScenarioResearchBudgets
{
    private static readonly Dictionary<ResearchProjectDef, float> loadedCosts = new Dictionary<ResearchProjectDef, float>();

    public static void Apply(Scenario scenario)
    {
        bool helod = ScenPart_ResearchTechBaseline.In(scenario);
        foreach (HelodScenarioResearchBudgetDef def in DefDatabase<HelodScenarioResearchBudgetDef>.AllDefsListForReading)
            foreach (ResearchBudget budget in def.baseCosts)
            {
                ResearchProjectDef project = budget.project;
                if (project == null) continue;
                if (!loadedCosts.TryGetValue(project, out float loaded)) loadedCosts[project] = loaded = project.baseCost;
                project.baseCost = helod ? budget.baseCost : loaded;
            }
    }
}

// New games, the tutorial and scenario previews assign the scenario through this setter.
[HarmonyPatch(typeof(Game), nameof(Game.Scenario), MethodType.Setter)]
public static class Patch_GameScenarioResearchBudgets
{
    [HarmonyPostfix]
    public static void Postfix(Game __instance) => HelodScenarioResearchBudgets.Apply(__instance.Scenario);
}

// Saved games read the scenario field directly, before maps and research state are used.
[HarmonyPatch(typeof(Game), "ExposeSmallComponents")]
public static class Patch_GameLoadResearchBudgets
{
    [HarmonyPostfix]
    public static void Postfix(Game __instance)
    {
        if (Scribe.mode == LoadSaveMode.LoadingVars) HelodScenarioResearchBudgets.Apply(__instance.Scenario);
    }
}
