using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    internal static class ProductionFacilityDisplay
    {
        // Cache recipe identity, not translated text or map-dependent availability.
        private static readonly Dictionary<ThingDef, List<RecipeDef>> recipes = new();

        private static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> __result, ThingDef __instance)
        {
            foreach (var entry in __result) yield return entry;
            if (!__instance.defName.StartsWith("HD_")) yield break;
            var capability = __instance.GetModExtension<FacilityCapabilityExtension>();
            if (capability != null && capability.capabilities.Count > 0)
                yield return new StatDrawEntry(StatCategoryDefOf.Basics,
                    "HD_Production_FacilityFields".Translate().Resolve(),
                    string.Join(", ", capability.capabilities.OrderBy(c => c.field.order).Select(c => c.Label)),
                    "HD_Production_FacilityFieldsDesc".Translate().Resolve(), 998);
            if (!recipes.TryGetValue(__instance, out var production))
            {
                production = DefDatabase<RecipeDef>.AllDefsListForReading
                    .Where(r => r.ProducedThingDef == __instance && r.AllRecipeUsers.Any())
                    .OrderBy(r => r.defName).ToList();
                recipes.Add(__instance, production);
            }
            if (production.Count == 0) yield break;
            string value = string.Join(" / ", production.Select(LinkedFacilityRecipes.RequiredFacilitiesText).Distinct());
            string report = "HD_Production_RequiredFacilitiesDesc".Translate().Resolve()
                + "\n\n" + string.Join("\n", production.Select(r => r.LabelCap + ": "
                    + LinkedFacilityRecipes.RequiredFacilitiesText(r) + " ("
                    + string.Join(", ", r.AllRecipeUsers.Select(t => t.LabelCap.ToString())) + ")"));
            yield return new StatDrawEntry(StatCategoryDefOf.Basics,
                "HD_Production_RequiredFacilities".Translate().Resolve(), value, report, 999);
        }
    }
}
