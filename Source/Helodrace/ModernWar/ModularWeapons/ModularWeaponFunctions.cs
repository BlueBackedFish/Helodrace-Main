using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.ModernWar
{
    public static class ModularWeaponPartCategoryUtility
    {
        public static string Label(this ModularWeaponPartCategory category)
        {
            switch (category)
            {
                case ModularWeaponPartCategory.Required:
                    return "HD_ModularWeapon_CategoryRequired".Translate();
                case ModularWeaponPartCategory.Functional:
                    return "HD_ModularWeapon_CategoryFunctional".Translate();
                default:
                    return "HD_ModularWeapon_CategoryOptional".Translate();
            }
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[]
    {
        typeof(LocalTargetInfo),
        typeof(LocalTargetInfo),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool)
    })]
    public static class Patch_Verb_ModularWeaponRequiredParts
    {
        private const int MessageCooldownTicks = 120;
        private static readonly Dictionary<int, int> nextMessageTick =
            new Dictionary<int, int>();

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            CompModularWeaponNode root = __instance?.EquipmentSource
                ?.TryGetComp<CompModularWeaponNode>();
            if (root?.Props.isAssemblyRoot != true) return true;

            ModularWeaponFunctionStatus status = root.FunctionStatus;
            if (status.CanFire) return true;

            __result = false;
            NotifyMissingParts(__instance, root, status);
            return false;
        }

        private static void NotifyMissingParts(
            Verb verb,
            CompModularWeaponNode root,
            ModularWeaponFunctionStatus status)
        {
            Pawn pawn = verb?.caster as Pawn;
            if (pawn?.Faction != Faction.OfPlayer) return;

            int id = root?.parent?.thingIDNumber ?? -1;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (id >= 0 && nextMessageTick.TryGetValue(id, out int next) && now < next)
                return;
            if (id >= 0) nextMessageTick[id] = now + MessageCooldownTicks;

            Messages.Message(
                "HD_ModularWeapon_MissingRequiredParts".Translate(
                    root.parent.LabelCap,
                    status.MissingRequiredLabels),
                pawn,
                MessageTypeDefOf.RejectInput,
                false);
        }
    }
}
