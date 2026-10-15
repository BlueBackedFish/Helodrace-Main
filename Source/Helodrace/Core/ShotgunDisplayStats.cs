using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    internal static class ShotgunDisplayStats
    {
        internal static IEnumerable<int> SpreadFactors(ThingDef weapon)
        {
            if (weapon == null) return Enumerable.Empty<int>();
            return weapon.Verbs.OfType<VerbProperties_Shotgun>()
                .Where(props => props.verbClass != null
                    && typeof(Verb_ShootShotgun).IsAssignableFrom(props.verbClass))
                .Select(props => Math.Max(0, props.spreadFactor))
                .Distinct().OrderBy(factor => factor);
        }
    }

    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    public static class Patch_ThingDef_ShotgunDisplayStats
    {
        public static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> __result, ThingDef __instance)
        {
            foreach (StatDrawEntry entry in __result) yield return entry;
            int[] factors = ShotgunDisplayStats.SpreadFactors(__instance).ToArray();
            if (factors.Length == 0) yield break;

            string value = string.Join(", ", factors.Select(factor => factor.ToString()).ToArray());
            yield return new StatDrawEntry(
                StatCategoryDefOf.Weapon_Ranged,
                "HD_Shotgun_SpreadFactor".Translate().Resolve(),
                value,
                "HD_Shotgun_SpreadFactor_Desc".Translate(value).Resolve(),
                5460);
        }
    }
}
