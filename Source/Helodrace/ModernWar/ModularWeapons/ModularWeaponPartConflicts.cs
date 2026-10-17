using System.Collections.Generic;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class ModularWeaponPartConflict
    {
        public readonly ThingDef Part;
        public readonly ThingDef Other;

        internal ModularWeaponPartConflict(ThingDef part, ThingDef other)
        {
            Part = part;
            Other = other;
        }

        public string Reason => "HD_ModularWeapon_RejectPartConflict".Translate(
            Part.LabelCap, Other.LabelCap);
    }

    public static class ModularWeaponPartConflicts
    {
        public static bool Conflicts(ThingDef first, ThingDef second)
        {
            if (first == null || second == null) return false;
            return first.GetCompProperties<CompProperties_ModularWeaponNode>()
                    ?.incompatibleParts?.Contains(second) == true
                || second.GetCompProperties<CompProperties_ModularWeaponNode>()
                    ?.incompatibleParts?.Contains(first) == true;
        }

        // Check the incoming subtree both internally and against the entire destination,
        // including ancestors and siblings. Unrelated existing conflicts do not prevent repairs.
        public static ModularWeaponPartConflict Find(
            CompModularWeaponNode destination, ThingDef incomingDef,
            CompModularWeaponNode incomingTree = null, Thing ignoredSubtree = null)
        {
            var installed = new List<ThingDef>();
            Collect(destination?.AssemblyRoot, ignoredSubtree, installed,
                new HashSet<CompModularWeaponNode>());
            var incoming = new List<ThingDef>();
            if (incomingTree == null) incoming.Add(incomingDef);
            else Collect(incomingTree, null, incoming, new HashSet<CompModularWeaponNode>());

            for (int i = 0; i < incoming.Count; i++)
            {
                foreach (ThingDef other in installed)
                    if (Conflicts(incoming[i], other))
                        return new ModularWeaponPartConflict(incoming[i], other);
                for (int j = 0; j < i; j++)
                    if (Conflicts(incoming[i], incoming[j]))
                        return new ModularWeaponPartConflict(incoming[i], incoming[j]);
            }
            return null;
        }

        private static void Collect(CompModularWeaponNode node, Thing ignored,
            List<ThingDef> result, HashSet<CompModularWeaponNode> visited)
        {
            if (node == null || node.parent == ignored || !visited.Add(node)) return;
            result.Add(node.parent.def);
            for (int i = 0; i < node.ChildCount; i++)
                Collect(node.ChildAt(i)?.TryGetComp<CompModularWeaponNode>(), ignored, result, visited);
        }
    }
}
