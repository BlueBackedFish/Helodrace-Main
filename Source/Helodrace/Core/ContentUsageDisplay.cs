using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed class ContentUsageExtension : DefModExtension
    {
        public string helpKey;
    }

    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    internal static class ContentUsageDisplay
    {
        private static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> __result, ThingDef __instance)
        {
            foreach (var entry in __result) yield return entry;
            var usage = __instance.GetModExtension<ContentUsageExtension>();
            if (string.IsNullOrEmpty(usage?.helpKey)) yield break;
            yield return new StatDrawEntry(StatCategoryDefOf.Basics,
                "HD_ContentUsage_Title".Translate().Resolve(),
                "HD_ContentUsage_Details".Translate().Resolve(),
                usage.helpKey.Translate().Resolve(), 998);
        }
    }
}
