using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public static class ModularWeaponRangeUtility
    {
        public static float PreviewPrimaryRange(Thing weapon)
        {
            VerbProperties primary = null;
            if (weapon?.def?.Verbs == null) return 0f;
            foreach (VerbProperties verb in weapon.def.Verbs)
            {
                if (verb.verbClass == typeof(Verb_ShootModularUnderbarrel)) continue;
                if (primary == null || verb.isPrimary) primary = verb;
                if (verb.isPrimary) break;
            }
            float range = (primary?.range ?? 0f) * weapon.GetStatValue(StatDefOf.RangedWeapon_RangeMultiplier);
            return ClampWeather(range, weapon.MapHeld);
        }

        internal static float ClampWeather(float range, Map map)
        {
            float cap = map?.weatherManager?.CurWeatherMaxRangeCap ?? -1f;
            return cap >= 0f ? Mathf.Min(range, cap) : range;
        }
    }

    // Vanilla's projectile verb already consumes RangedWeapon_RangeMultiplier.
    // Its multiplication happens AFTER the weather cap; clamp once more so a
    // scope cannot bypass weather. The launcher must not inherit rifle modding.
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "get_EffectiveRange")]
    public static class Patch_Verb_LaunchProjectile_ModularRange
    {
        public static void Postfix(Verb_LaunchProjectile __instance, ref float __result)
        {
            Thing equipment = __instance?.EquipmentSource;
            CompModularWeaponNode comp = equipment?.TryGetComp<CompModularWeaponNode>();
            if (comp?.Props.isAssemblyRoot != true) return;
            if (__instance is Verb_ShootModularUnderbarrel)
                __result /= Mathf.Max(0.01f, comp.ConvertedStats.RangeFactor);
            if (equipment.TryGetComp<CompUniqueWeapon>()?.IgnoreAccuracyMaluses != true)
            {
                Map map = __instance.Caster?.MapHeld;
                float cap = map?.weatherManager?.CurWeatherMaxRangeCap ?? -1f;
                if (cap < 0f) return;
                // Undo vanilla's pre-multiplier cap before applying the final cap.
                // Otherwise a 0.9x build would incorrectly turn a 10-cell fog cap
                // into 9 cells even when its uncapped range is well above 10.
                float original = __instance.verbProps.rangeStat == null
                    ? __instance.verbProps.range
                    : __instance.Caster.GetStatValue(__instance.verbProps.rangeStat);
                if (cap > 0f && original > cap) __result *= original / cap;
                __result = Mathf.Min(__result, cap);
            }
        }
    }
}
