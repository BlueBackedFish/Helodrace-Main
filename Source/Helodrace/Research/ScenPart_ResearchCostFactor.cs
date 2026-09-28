using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    public class ScenPart_ResearchCostFactor : ScenPart
    {
        public ResearchProjectDef project;
        public float factor = 1f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref project, "project");
            Scribe_Values.Look(ref factor, "factor", 1f);
        }

        public override string Summary(Scenario scen)
        {
            if (project == null)
            {
                return null;
            }

            return $"{project.LabelCap} research cost x{factor}";
        }

        public override bool HasNullDefs()
        {
            return base.HasNullDefs() || project == null;
        }
    }

    public static class ResearchCostFactorUtility
    {
        public static float ScenarioFactorFor(ResearchProjectDef project)
        {
            if (project == null || Current.Game == null)
            {
                return 1f;
            }

            Scenario scenario = Find.Scenario;
            if (scenario == null)
            {
                return 1f;
            }

            float factor = 1f;
            foreach (ScenPart part in scenario.AllParts)
            {
                ScenPart_ResearchCostFactor researchCostFactor = part as ScenPart_ResearchCostFactor;
                if (researchCostFactor?.project == project && researchCostFactor.factor > 0f)
                {
                    factor *= researchCostFactor.factor;
                }
            }

            return factor;
        }
    }

    [HarmonyPatch(typeof(ResearchProjectDef), "get_Cost")]
    public static class Patch_ResearchProjectDef_Cost
    {
        [HarmonyPostfix]
        public static void Postfix(ResearchProjectDef __instance, ref float __result)
        {
            __result *= ResearchCostFactorUtility.ScenarioFactorFor(__instance);
        }
    }

    /// <summary>
    /// ResearchManager.FinishProject writes the unmodified base cost into its
    /// progress dictionary. A scenario multiplier applied through get_Cost then
    /// makes that supposedly finished project incomplete again. Normalize only
    /// explicit completion calls; ordinary research still has to earn the full
    /// scenario-adjusted cost.
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    public static class Patch_ResearchManager_ScenarioCostFinish
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        public static void Postfix(
            ResearchProjectDef proj,
            Dictionary<ResearchProjectDef, float> ___progress)
        {
            EnsureFinishedProgress(proj, ___progress);
        }

        internal static void EnsureFinishedProgress(
            ResearchProjectDef project,
            Dictionary<ResearchProjectDef, float> progress)
        {
            if (project == null || progress == null
                || ResearchCostFactorUtility.ScenarioFactorFor(project) == 1f)
            {
                return;
            }

            float required = project.Cost;
            if (!progress.TryGetValue(project, out float current) || current < required)
            {
                progress[project] = required;
            }
        }
    }

    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.DebugSetAllProjectsFinished))]
    public static class Patch_ResearchManager_ScenarioCostDebugFinishAll
    {
        [HarmonyPostfix]
        public static void Postfix(Dictionary<ResearchProjectDef, float> ___progress)
        {
            List<ResearchProjectDef> projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < projects.Count; i++)
            {
                Patch_ResearchManager_ScenarioCostFinish.EnsureFinishedProgress(
                    projects[i], ___progress);
            }
        }
    }
}
