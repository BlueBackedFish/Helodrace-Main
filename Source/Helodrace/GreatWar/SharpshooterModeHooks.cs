using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Helodrace
{
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.EquipmentTrackerTick))]
    public static class Patch_SharpshooterEquippedTick
    {
        public static void Postfix(Pawn_EquipmentTracker __instance) => __instance.Primary?
            .TryGetComp<CompSharpshooterWeapon>()?.TickEquipped(__instance.pawn, GenTicks.TicksGame);
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class Patch_SharpshooterMovementStart
    {
        public static void Postfix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            if (__instance.Moving) ___pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>()?.NotifyMovementStarted(___pawn);
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StopDead))]
    public static class Patch_SharpshooterMovementStop
    {
        public static void Postfix(Pawn ___pawn) => ___pawn.equipment?.Primary?
            .TryGetComp<CompSharpshooterWeapon>()?.TickEquipped(___pawn, GenTicks.TicksGame);
    }
    // Gate both overloads: mobile-fire skills can handle the short overload
    // without ever entering vanilla's overload with a destination.
    [HarmonyPatch]
    public static class Patch_SharpshooterPreparationCast
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in typeof(Verb).GetMethods())
                if (method.Name == nameof(Verb.TryStartCastOn)) yield return method;
        }
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            if (__instance.IsMeleeAttack) return true;
            var comp = __instance.EquipmentSource?.TryGetComp<CompSharpshooterWeapon>();
            if (comp == null || !comp.IsModeVerb(__instance) || comp.ReadyToFire) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo),
        typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_SharpshooterCurrentVerbCast
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Verb __instance, LocalTargetInfo castTarg, LocalTargetInfo destTarg,
            bool surpriseAttack, bool canHitNonTargetPawns, bool preventFriendlyFire, bool nonInterruptingSelfCast, ref bool __result)
        {
            if (__instance.IsMeleeAttack) return true;
            var comp = __instance.EquipmentSource?.TryGetComp<CompSharpshooterWeapon>();
            if (comp == null || !comp.IsModeVerb(__instance) || !comp.CanUseSharpshooterMode) return true;
            if (!comp.ReadyToFire) { __result = false; return false; }
            Verb selected = comp.SelectedVerb;
            if (selected == null || selected == __instance) return true;
            // Attack commands can retain a Verb reference across a manual
            // switch. Reacquire the selected verb instead of firing old mode.
            __result = selected.TryStartCastOn(castTarg, destTarg, surpriseAttack, canHitNonTargetPawns,
                preventFriendlyFire, nonInterruptingSelfCast);
            return false;
        }
    }
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo),
        typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_SharpshooterCurrentVerbShortCast
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Verb __instance, LocalTargetInfo castTarg,
            bool surpriseAttack, bool canHitNonTargetPawns, bool preventFriendlyFire, bool nonInterruptingSelfCast, ref bool __result)
        {
            if (__instance.IsMeleeAttack) return true;
            var comp=__instance.EquipmentSource?.TryGetComp<CompSharpshooterWeapon>();
            if(comp==null || !comp.IsModeVerb(__instance) || !comp.CanUseSharpshooterMode) return true;
            if(!comp.ReadyToFire) { __result=false; return false; }
            Verb selected=comp.SelectedVerb;
            if(selected==null || selected==__instance) return true;
            __result=selected.TryStartCastOn(castTarg,surpriseAttack,canHitNonTargetPawns,preventFriendlyFire,nonInterruptingSelfCast);
            return false;
        }
    }
    [HarmonyPatch(typeof(Verb), nameof(Verb.Available))]
    public static class Patch_SharpshooterBurstPreparation
    {
        public static void Postfix(Verb __instance, ref bool __result)
        {
            // The native failed-burst path preserves the final weapon cooldown
            // and callbacks. Outside bursts leave AI weapon discovery intact.
            if (!__result || !__instance.Bursting || __instance.IsMeleeAttack) return;
            var comp = __instance.EquipmentSource?.TryGetComp<CompSharpshooterWeapon>();
            if (comp != null && comp.IsModeVerb(__instance) && !comp.ReadyToFire) __result=false;
        }
    }
}
