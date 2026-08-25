using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.ModernWar
{
    [HarmonyPatch(typeof(Verb), "get_TicksBetweenBurstShots")]
    public static class Patch_Verb_TicksBetweenBurstShots_ModularWeapon
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Verb __instance, ref int __result)
        {
            CompModularWeaponNode comp = __instance?.EquipmentSource
                ?.TryGetComp<CompModularWeaponNode>();
            if (comp?.Props.isAssemblyRoot != true) return;
            __result = comp.EffectiveBurstIntervalTicks;
        }
    }

    [HarmonyPatch(typeof(StatExtension), nameof(StatExtension.GetStatValue))]
    public static class Patch_StatExtension_ModularWeaponFireDelay
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (stat != StatDefOf.RangedWeapon_Cooldown) return;
            CompModularWeaponNode comp = thing?.TryGetComp<CompModularWeaponNode>();
            if (comp?.Props.isAssemblyRoot != true) return;
            comp.ApplyFireDelayToCooldown(ref __result);
        }
    }
}
