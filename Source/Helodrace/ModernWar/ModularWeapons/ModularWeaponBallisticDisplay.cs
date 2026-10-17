using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.ModernWar
{
    // Vanilla's info card reads the root verb's default projectile. Replace those
    // two entries for instances so installed ammo/barrels match the modding panel.
    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    public static class Patch_ThingDef_ModularBallisticStats
    {
        public static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> __result, StatRequest req)
        {
            CompModularWeaponNode modular = req.Thing?.TryGetComp<CompModularWeaponNode>();
            ModularWeaponBallisticProfile profile = modular?.Props.isAssemblyRoot == true
                && !ModsConfig.IsActive("ceteam.combatextended") ? modular.ConvertedStats.Ballistics : null;
            if (profile == null)
            {
                foreach (StatDrawEntry entry in __result) yield return entry;
                yield break;
            }
            string damageLabel = "Damage".Translate().CapitalizeFirst();
            string penetrationLabel = "ArmorPenetration".Translate().CapitalizeFirst();
            foreach (StatDrawEntry entry in __result)
            {
                if (entry.category == StatCategoryDefOf.Weapon_Ranged
                    && entry.stat == null && (entry.LabelCap == damageLabel || entry.LabelCap == penetrationLabel))
                    continue;
                yield return entry;
            }
            string report = profile.Projectile.LabelCap + "\n" + "HD_ModularWeapon_BallisticsHint".Translate();
            yield return new StatDrawEntry(StatCategoryDefOf.Weapon_Ranged, damageLabel,
                profile.DamageAt(0, req.Thing.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier)).ToString(), report, 5500);
            yield return new StatDrawEntry(StatCategoryDefOf.Weapon_Ranged, penetrationLabel,
                profile.PenetrationAt(0, req.Thing.GetStatValue(StatDefOf.RangedWeapon_ArmorPenetrationMultiplier)).ToStringPercent(), report, 5400);
            yield return new StatDrawEntry(StatCategoryDefOf.Weapon_Ranged, "HD_ModularWeapon_FragmentationChance".Translate(),
                profile.FragmentationChanceAt().ToStringPercent(), report, 5399);
        }
    }
}
