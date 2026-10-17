using System.Collections.Generic;
using System.Linq;
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

            if (!node.Props.sockets.NullOrEmpty())
            {
                for (int i = 0; i < node.Props.sockets.Count; i++)
                {
                    ModularAttachmentSocket socket = node.Props.sockets[i];
                    ThingDef expected = socket?.expectedPart;
                    if (expected == null || HasChildOnSocket(node, socket.id))
                        continue;

                    CompProperties_ModularWeaponNode expectedProps = expected
                        .GetCompProperties<CompProperties_ModularWeaponNode>();
                    if (expectedProps?.partCategory == ModularWeaponPartCategory.Required)
                    {
                        if (required.Add(expected))
                            result.missingRequiredParts.Add(expected);
                    }
                    else if (expectedProps?.partCategory
                        == ModularWeaponPartCategory.Functional)
                    {
                        if (functional.Add(expected))
                            result.missingFunctionalParts.Add(expected);
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
}
