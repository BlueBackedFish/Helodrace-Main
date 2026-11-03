using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace;

public sealed class ScenPart_ResearchTechBaseline : ScenPart
{
    public override string Summary(Scenario scen) => "HD_ResearchTechBaseline_Summary".Translate();
}

// Preserve the actual faction's industrial identity. Only research uses the
// native penalty for a Neolithic starting colony (Medieval 1.5, Industrial+ 2).
// Native progress, apparent-cost UI and explicit completion stay in agreement.
[HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.CostFactor))]
public static class Patch_ResearchTechBaseline
{
    [HarmonyPostfix]
    public static void Postfix(ResearchProjectDef __instance, TechLevel researcherTechLevel, ref float __result)
    {
        // Also prevents recursion when asking the engine for its tribal factor.
        if (researcherTechLevel == TechLevel.Neolithic || Current.Game == null) return;
        TechLevel playerTech = Faction.OfPlayerSilentFail?.def.techLevel ?? TechLevel.Industrial;
        if (researcherTechLevel != playerTech || Find.Scenario == null) return;
        foreach (ScenPart part in Find.Scenario.AllParts)
        {
            if (part is not ScenPart_ResearchTechBaseline) continue;
            __result = System.MathF.Max(__result, __instance.CostFactor(TechLevel.Neolithic));
            return;
        }
    }
}
