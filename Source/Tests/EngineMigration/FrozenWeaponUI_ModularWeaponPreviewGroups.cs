// Frozen from 586d610f before owned weapon editor geometry conversion.
using Helodrace.ModernWar;
using System.Collections.Generic;
using System.Linq;

namespace Helodrace.Tests.FrozenWeaponUI
{
    internal enum ModularWeaponPreviewGroupKind { Assembly, MainWeapon, AllSights, Sight, AttachedWeapon }

    internal sealed class ModularWeaponPreviewGroup
    {
        public string Key;
        public ModularWeaponPreviewGroupKind Kind;
        public ModularRenderNode Root;
        public ModularRenderNode WeaponRoot;
        public int SightIndex;
        public readonly HashSet<int> PartIds = new HashSet<int>();
        public bool Contains(ModularRenderNode node) => node != null && PartIds.Contains(node.thing.thingIDNumber);
    }

    internal static class ModularWeaponPreviewGroups
    {
        public static List<ModularWeaponPreviewGroup> Build(List<ModularRenderNode> snapshot,
            IReadOnlyList<ModularSightGroupStatus> sights)
        {
            var result = new List<ModularWeaponPreviewGroup>();
            ModularRenderNode root = snapshot.FirstOrDefault(node => node.parentComp == null);
            var all = new ModularWeaponPreviewGroup
                { Key = "all", Kind = ModularWeaponPreviewGroupKind.Assembly, Root = root, WeaponRoot = root };
            all.PartIds.UnionWith(snapshot.Select(node => node.thing.thingIDNumber));
            result.Add(all);
            if (root == null) return result;

            var byComp = snapshot.ToDictionary(node => node.comp);
            var byId = snapshot.ToDictionary(node => node.thing.thingIDNumber);
            var owners = new Dictionary<int, ModularRenderNode>();
            foreach (ModularRenderNode node in snapshot)
            {
                ModularRenderNode current = node;
                while (current.parentComp != null
                    && !current.Props.isWeaponSubassembly && !current.Props.isAssemblyRoot)
                    current = byComp[current.parentComp];
                owners.Add(node.thing.thingIDNumber, current);
            }
            var weapons = snapshot.Where(node => node != root
                && (node.Props.isWeaponSubassembly || node.Props.isAssemblyRoot))
                .OrderBy(node => node.depth).ThenBy(node => node.thing.thingIDNumber).ToList();
            if (weapons.Count > 0)
                result.Add(WeaponGroup(root, owners, snapshot, "main", ModularWeaponPreviewGroupKind.MainWeapon));

            var allSights = new ModularWeaponPreviewGroup
                { Key = "sights", Kind = ModularWeaponPreviewGroupKind.AllSights };
            var sightGroups = new List<ModularWeaponPreviewGroup>();
            foreach (var sight in sights)
            {
                // A future attached gun's optics must not merge into the rifle's view
                // merely because their axes happen to share the same height.
                var members = sight.members.Where(part => byId.ContainsKey(part.thingIDNumber))
                    .Select(part => byId[part.thingIDNumber]);
                foreach (var partition in members.GroupBy(node => owners[node.thing.thingIDNumber]))
                {
                    var ids = new HashSet<int>(partition.Select(node => node.thing.thingIDNumber));
                    ModularRenderNode primary = partition.FirstOrDefault(node => node.thing == sight.primary)
                        ?? partition.First();
                    var group = new ModularWeaponPreviewGroup
                    {
                        Key = "sight:" + partition.Key.thing.thingIDNumber + ":" + ids.Min(),
                        Kind = ModularWeaponPreviewGroupKind.Sight,
                        Root = primary,
                        WeaponRoot = partition.Key,
                        SightIndex = sight.index
                    };
                    // Retain child covers and other components belonging to the optic,
                    // while stopping at independently grouped weapon subassemblies.
                    foreach (var node in snapshot)
                    {
                        if (owners[node.thing.thingIDNumber] != partition.Key) continue;
                        ModularRenderNode current = node;
                        while (current != null)
                        {
                            if (ids.Contains(current.thing.thingIDNumber))
                            {
                                group.PartIds.Add(node.thing.thingIDNumber);
                                break;
                            }
                            current = current.parentComp == null ? null : byComp[current.parentComp];
                        }
                    }
                    sightGroups.Add(group);
                    allSights.PartIds.UnionWith(group.PartIds);
                    if (allSights.Root == null) allSights.Root = primary;
                }
            }
            if (allSights.PartIds.Count > 0)
            {
                result.Add(allSights);
                result.AddRange(sightGroups);
            }
            foreach (var weapon in weapons)
                result.Add(WeaponGroup(weapon, owners, snapshot, "weapon:" + weapon.thing.thingIDNumber,
                    ModularWeaponPreviewGroupKind.AttachedWeapon));
            return result;
        }

        private static ModularWeaponPreviewGroup WeaponGroup(ModularRenderNode root,
            Dictionary<int, ModularRenderNode> owners, List<ModularRenderNode> snapshot,
            string key, ModularWeaponPreviewGroupKind kind)
        {
            var group = new ModularWeaponPreviewGroup { Key = key, Kind = kind, Root = root, WeaponRoot = root };
            group.PartIds.UnionWith(snapshot.Where(node => owners[node.thing.thingIDNumber] == root)
                .Select(node => node.thing.thingIDNumber));
            return group;
        }
    }
}
