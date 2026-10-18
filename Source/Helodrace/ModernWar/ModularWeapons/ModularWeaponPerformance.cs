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
            if (__instance is Verb_ShootModularUnderbarrel) return;
            CompModularWeaponNode comp = __instance?.EquipmentSource
                ?.TryGetComp<CompModularWeaponNode>();
            if (comp?.Props.isAssemblyRoot != true) return;
            __result = comp.EffectiveBurstIntervalTicks;
        }
    }

    [HarmonyPatch(typeof(Verb), "get_BurstShotCount")]
    public static class Patch_Verb_BurstShotCount_ModularWeapon
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Verb __instance, ref int __result)
        {
            if (__instance is Verb_ShootModularUnderbarrel) return;
            CompModularWeaponNode comp = __instance?.EquipmentSource
                ?.TryGetComp<CompModularWeaponNode>();
            if (comp?.Props.isAssemblyRoot != true) return;
            __result = comp.ApplyBurstShotCount(__result);
        }
    }

    // Patch after vanilla's projectile verb has applied the equipment multiplier.
    // Patching the base getter clamps too early and a faster build can then
    // multiply that clamped value below the mechanical minimum again.
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "get_WarmupTime")]
    public static class Patch_Verb_WarmupTime_ModularWeapon
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Verb __instance, ref float __result)
        {
            if (__instance is Verb_ShootModularUnderbarrel) return;
            CompModularWeaponNode comp = __instance?.EquipmentSource
                ?.TryGetComp<CompModularWeaponNode>();
            if (comp?.Props.isAssemblyRoot != true) return;
            if (!ModsConfig.IsActive("ceteam.combatextended") && __instance.CurrentTarget.IsValid
                && __instance.Caster?.Spawned == true)
            {
                var groups = comp.SightGroups;
                float representativeCost = 1.12f;
                foreach (var group in groups)
                    if (group.isActive && group.performance != null)
                        representativeCost = group.performance.Warmup;
                float distance = __instance.Caster.Position.DistanceTo(__instance.CurrentTarget.Cell);
                __result *= ModularWeaponSightPerformance.WarmupAtDistance(groups, distance) / representativeCost;
            }
            comp.ApplyMechanicalRpmFloor(ref __result);
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
            if (comp?.Props.isAssemblyRoot == true)
                comp.ApplyFireDelayToCooldown(ref __result);
            AmmoPouchUtility.ApplyCooldown(thing, ref __result);
        }
    }
}
