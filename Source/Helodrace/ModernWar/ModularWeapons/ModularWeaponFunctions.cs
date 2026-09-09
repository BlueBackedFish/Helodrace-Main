using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class ModularWeaponFunctionStatus
    {
        public readonly List<ThingDef> missingRequiredParts = new List<ThingDef>();
        public readonly List<ThingDef> missingFunctionalParts = new List<ThingDef>();
        public bool semiAutomaticOnly;
        public bool singleRoundCapacity;

        public bool CanFire => missingRequiredParts.Count == 0;

        public string MissingRequiredLabels => string.Join(
            ", ",
            missingRequiredParts.Select(def => def.LabelCap.ToString()).ToArray());

        public string MissingFunctionalLabels => string.Join(
            ", ",
            missingFunctionalParts.Select(def => def.LabelCap.ToString()).ToArray());
    }

    public static class ModularWeaponFunctionResolver
    {
        private const int MaxTreeDepth = 32;

        public static ModularWeaponFunctionStatus Resolve(CompModularWeaponNode root)
        {
            ModularWeaponFunctionStatus result = new ModularWeaponFunctionStatus();
            HashSet<ThingDef> required = new HashSet<ThingDef>();
            HashSet<ThingDef> functional = new HashSet<ThingDef>();
            Visit(root?.AssemblyRoot, result, required, functional, 0);
            return result;
        }

        private static void Visit(
            CompModularWeaponNode node,
            ModularWeaponFunctionStatus result,
            HashSet<ThingDef> required,
            HashSet<ThingDef> functional,
            int depth)
        {
            if (node == null || depth > MaxTreeDepth) return;

            if (!node.Props.defaultAttachments.NullOrEmpty())
            {
                for (int i = 0; i < node.Props.defaultAttachments.Count; i++)
                {
                    ModularDefaultAttachment expected = node.Props.defaultAttachments[i];
                    if (expected?.part == null || HasChildOnSocket(node, expected.socketId))
                        continue;

                    CompProperties_ModularWeaponNode expectedProps = expected.part
                        .GetCompProperties<CompProperties_ModularWeaponNode>();
                    if (expectedProps?.partCategory == ModularWeaponPartCategory.Required)
                    {
                        if (required.Add(expected.part))
                            result.missingRequiredParts.Add(expected.part);
                    }
                    else if (expectedProps?.partCategory
                        == ModularWeaponPartCategory.Functional)
                    {
                        if (functional.Add(expected.part))
                            result.missingFunctionalParts.Add(expected.part);
                        ApplyMissingFunctions(expectedProps, result);
                    }
                }
            }

            for (int i = 0; i < node.ChildCount; i++)
            {
                Visit(
                    node.ChildAt(i)?.TryGetComp<CompModularWeaponNode>(),
                    result,
                    required,
                    functional,
                    depth + 1);
            }
        }

        private static bool HasChildOnSocket(CompModularWeaponNode node, string socketId)
        {
            if (node == null || socketId.NullOrEmpty()) return false;
            for (int i = 0; i < node.ChildCount; i++)
                if (node.SocketIdAt(i) == socketId) return true;
            return false;
        }

        private static void ApplyMissingFunctions(
            CompProperties_ModularWeaponNode props,
            ModularWeaponFunctionStatus result)
        {
            if (props?.missingFunctions.NullOrEmpty() != false) return;
            for (int i = 0; i < props.missingFunctions.Count; i++)
            {
                switch (props.missingFunctions[i])
                {
                    case ModularWeaponMissingFunction.SemiAutomaticOnly:
                        result.semiAutomaticOnly = true;
                        break;
                    case ModularWeaponMissingFunction.SingleRoundCapacity:
                        result.singleRoundCapacity = true;
                        break;
                }
            }
        }
    }

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
