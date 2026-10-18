using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.ModernWar
{
    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    public static class Patch_ThingDef_ModularAccuracyStats
    {
        public static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> __result, StatRequest req)
        {
            var weapon = req.Thing?.TryGetComp<CompModularWeaponNode>();
            bool enabled = weapon?.Props.isAssemblyRoot == true && !ModsConfig.IsActive("ceteam.combatextended");
            foreach (var entry in __result)
            {
                if (enabled && entry.category == StatCategoryDefOf.Weapon_Ranged && entry.stat == null
                    && entry.LabelCap == "RangedWarmupTime".Translate().CapitalizeFirst()) continue;
                yield return entry;
            }
            if (!enabled) yield break;
            var stats = weapon.ConvertedStats;
            var bands = new[] { stats.Touch, stats.Short, stats.Medium, stats.Long };
            var statDefs = new[] { StatDefOf.AccuracyTouch, StatDefOf.AccuracyShort, StatDefOf.AccuracyMedium, StatDefOf.AccuracyLong };
            string report = "";
            for (int i = 0; i < bands.Length; i++)
                report += (i == 0 ? "" : "\n") + statDefs[i].LabelCap + ": " + Dialog_ModularWeapon.AccuracyReport(bands[i]);
            yield return new StatDrawEntry(StatCategoryDefOf.Weapon_Ranged,
                "HD_ModularWeapon_AccuracyModel".Translate(), "", report, 5398);
            yield return new StatDrawEntry(StatCategoryDefOf.Weapon_Ranged,
                "HD_ModularWeapon_WarmupSeconds".Translate(), Dialog_ModularWeapon.PreviewWarmupSeconds(weapon).ToString("0.###") + " s",
                "HD_ModularWeapon_TimingHint".Translate(), 5397);
            if (stats.HasStock)
                yield return new StatDrawEntry(StatCategoryDefOf.Weapon_Ranged,
                    "HD_ModularWeapon_StockSupport".Translate(), stats.StockSupport.ToStringPercent(),
                    "HD_ModularWeapon_StockExtension".Translate() + ": " + stats.StockExtension.ToStringPercent()
                    + "\n" + "HD_ModularWeapon_HandlingLength".Translate() + ": " + stats.HandlingLengthMeters.ToString("0.###") + " m", 5396);
        }
    }
}
