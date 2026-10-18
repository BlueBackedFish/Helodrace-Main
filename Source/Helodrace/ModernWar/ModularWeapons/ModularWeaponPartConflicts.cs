using System.Collections.Generic;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class ModularWeaponPartConflict
    {
        public readonly ThingDef Part;
        public readonly ThingDef Other;
        public readonly ThingDef Condition;

        internal ModularWeaponPartConflict(ThingDef part, ThingDef other, ThingDef condition = null)
        {
            Part = part;
            Other = other;
            Condition = condition;
        }

        public string Reason => Condition == null
            ? "HD_ModularWeapon_RejectPartConflict".Translate(Part.LabelCap, Other.LabelCap)
            : "HD_ModularWeapon_RejectConditionalPartConflict".Translate(
                Part.LabelCap, Other.LabelCap, Condition.LabelCap);
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
            // Adding the condition part (e.g. swapping a barrel) can activate a rule
            // on two already installed parts, so inspect the complete proposed assembly.
            var combined = new HashSet<ThingDef>(installed);
            combined.UnionWith(incoming);
            var added = new HashSet<ThingDef>(incoming);
            foreach (ThingDef owner in combined)
            {
                var rules = owner?.GetCompProperties<CompProperties_ModularWeaponNode>()
                    ?.conditionalIncompatibleParts;
                if (rules == null) continue;
                foreach (ModularWeaponConditionalPartConflict rule in rules)
                {
                    if (rule?.part == null || !combined.Contains(rule.part) || rule.whenAnyParts == null) continue;
                    foreach (ThingDef condition in rule.whenAnyParts)
                        if (condition != null && combined.Contains(condition)
                            && (added.Contains(owner) || added.Contains(rule.part) || added.Contains(condition)))
                            return new ModularWeaponPartConflict(owner, rule.part, condition);
                }
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
