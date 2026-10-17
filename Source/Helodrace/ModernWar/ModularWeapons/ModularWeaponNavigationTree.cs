using System;
using System.Collections.Generic;
using System.Linq;

namespace Helodrace.ModernWar
{
    // Rendering snapshots are layer-sorted, so navigation must reconstruct ownership.
    internal sealed class ModularWeaponNavigationTree
    {
        internal sealed class Entry
        {
            public ModularRenderNode Node;
            public ModularAttachmentSocket Socket;
            public Entry Parent;
            public readonly List<Entry> Children = new List<Entry>();
            public int Depth;
            public int DescendantCount;
            public bool MatchesSearch;
            public bool IsEmpty => Node == null;
            public ModularRenderNode Owner => IsEmpty ? Parent.Node : Node;
        }

        private readonly List<Entry> roots = new List<Entry>();
        private readonly Dictionary<CompModularWeaponNode, Entry> entries =
            new Dictionary<CompModularWeaponNode, Entry>();

        public ModularWeaponNavigationTree(
            List<ModularRenderNode> snapshot,
            Predicate<ModularRenderNode> hidePart,
            Predicate<ModularAttachmentSocket> hideSocket)
        {
            foreach (ModularRenderNode node in snapshot)
                if (!hidePart(node)) entries.Add(node.comp, new Entry { Node = node });

            foreach (Entry entry in entries.Values)
            {
                Entry parent;
                if (entry.Node.parentComp == null)
                    roots.Add(entry);
                else if (entries.TryGetValue(entry.Node.parentComp, out parent))
                {
                    entry.Parent = parent;
                    entry.Socket = parent.Node.Props.SocketNamed(entry.Node.parentSocketId);
                    parent.Children.Add(entry);
                }
            }

            foreach (Entry entry in entries.Values)
            {
                entry.Children.Sort((a, b) =>
                    a.Node.parentChildIndex.CompareTo(b.Node.parentChildIndex));
                foreach (ModularAttachmentSocket socket in entry.Node.Props.sockets)
                {
                    if (hideSocket(socket) || entry.Children.Any(child => child.Socket == socket))
                        continue;
                    // Keep missing firing-critical parts visible even though no Thing exists.
                    bool required = socket.required || entry.Node.Props.defaultAttachments.Any(
                        attachment => attachment.socketId == socket.id
                            && attachment.part?.GetCompProperties<CompProperties_ModularWeaponNode>()
                                ?.partCategory == ModularWeaponPartCategory.Required);
                    if (required)
                        entry.Children.Add(new Entry { Parent = entry, Socket = socket });
                }
            }
            foreach (Entry entry in roots) SetDepthAndCount(entry, 0);
        }

        public List<Entry> VisibleRows(HashSet<int> collapsed, string search)
        {
            string query = (search ?? string.Empty).Trim();
            var included = new HashSet<Entry>();
            foreach (Entry entry in entries.Values)
            {
                entry.MatchesSearch = query.Length > 0 && Matches(entry, query);
                if (entry.MatchesSearch) IncludeAncestors(entry, included);
                foreach (Entry empty in entry.Children.Where(child => child.IsEmpty))
                {
                    empty.MatchesSearch = query.Length > 0 && Matches(empty, query);
                    if (empty.MatchesSearch) IncludeAncestors(empty, included);
                }
            }
            var result = new List<Entry>();
            foreach (Entry entry in roots)
                AppendVisible(entry, collapsed, query.Length > 0 ? included : null, result);
            return result;
        }

        public void Reveal(ModularRenderNode node, HashSet<int> collapsed)
        {
            Entry entry;
            if (node == null || !entries.TryGetValue(node.comp, out entry)) return;
            for (Entry parent = entry.Parent; parent != null; parent = parent.Parent)
                collapsed.Remove(parent.Node.thing.thingIDNumber);
        }

        public void Focus(ModularRenderNode node, HashSet<int> collapsed)
        {
            collapsed.Clear();
            foreach (Entry entry in entries.Values)
                if (entry.Children.Count > 0) collapsed.Add(entry.Node.thing.thingIDNumber);
            Reveal(node, collapsed);
            if (node != null) collapsed.Remove(node.thing.thingIDNumber);
        }

        public string Breadcrumb(ModularRenderNode node)
        {
            Entry entry;
            if (node == null || !entries.TryGetValue(node.comp, out entry)) return string.Empty;
            var labels = new List<string>();
            for (; entry != null; entry = entry.Parent)
                labels.Add(entry.Node.thing.LabelCap.ToString());
            labels.Reverse();
            return string.Join("  ›  ", labels.ToArray());
        }

        private static int SetDepthAndCount(Entry entry, int depth)
        {
            entry.Depth = depth;
            foreach (Entry child in entry.Children)
                entry.DescendantCount += SetDepthAndCount(child, depth + 1);
            return entry.DescendantCount + (entry.IsEmpty ? 0 : 1);
        }

        private static bool Matches(Entry entry, string query)
        {
            return (entry.Node?.thing.LabelCap.ToString() ?? string.Empty)
                    .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (entry.Socket?.Label ?? string.Empty)
                    .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void IncludeAncestors(Entry entry, HashSet<Entry> included)
        {
            for (; entry != null; entry = entry.Parent) included.Add(entry);
        }

        private static void AppendVisible(Entry entry, HashSet<int> collapsed,
            HashSet<Entry> included, List<Entry> result)
        {
            if (included != null && !included.Contains(entry)) return;
            result.Add(entry);
            if (entry.IsEmpty || (included == null
                && collapsed.Contains(entry.Node.thing.thingIDNumber))) return;
            foreach (Entry child in entry.Children)
                AppendVisible(child, collapsed, included, result);
        }
    }
}
