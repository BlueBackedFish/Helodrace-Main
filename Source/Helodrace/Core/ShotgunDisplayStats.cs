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
        internal static IEnumerable<float> SpreadAngles(ThingDef weapon)
        {
            if (weapon == null) return Enumerable.Empty<float>();
            return weapon.Verbs.OfType<VerbProperties_Shotgun>()
                .Where(props => props.verbClass != null
                    && typeof(Verb_ShootShotgun).IsAssignableFrom(props.verbClass))
                .Select(props => Math.Max(0f, props.spreadAngle))
                .Distinct().OrderBy(factor => factor);
        }
    }

    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    public static class Patch_ThingDef_ShotgunDisplayStats
    {
        public static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> __result, ThingDef __instance)
        {
            foreach (StatDrawEntry entry in __result) yield return entry;
            float[] angles = ShotgunDisplayStats.SpreadAngles(__instance).ToArray();
            if (angles.Length == 0) yield break;

            string value = string.Join(", ", angles.Select(angle => angle.ToString("0.##") + "°").ToArray());
            yield return new StatDrawEntry(
                StatCategoryDefOf.Weapon_Ranged,
                "HD_Shotgun_SpreadAngle".Translate().Resolve(),
                value,
                "HD_Shotgun_SpreadAngle_Desc".Translate(value).Resolve(),
                5460);
            string diameter = string.Join(", ", angles.Select(angle => ShotgunBallistics.PatternDiameter(angle, 10f).ToString("0.##")).ToArray());
            yield return new StatDrawEntry(StatCategoryDefOf.Weapon_Ranged,
                "HD_Shotgun_PatternDiameter".Translate().Resolve(), diameter,
                "HD_Shotgun_PatternDiameter_Desc".Translate().Resolve(), 5459);
        }
    }
}
