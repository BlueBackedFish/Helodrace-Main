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
}
