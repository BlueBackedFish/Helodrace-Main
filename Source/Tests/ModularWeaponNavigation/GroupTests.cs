using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.ModernWar;

internal static class GroupTests
{
    public static void Run()
    {
        var rifle = Node(1, "rifle");
        rifle.Props.isAssemblyRoot = true;
        var upper = Node(2, "upper", rifle);
        var front = Node(3, "front sight", upper);
        var rear = Node(4, "rear sight", upper);
        var optic = Node(5, "optic", upper);
        var magnifier = Node(6, "magnifier", upper);
        var cover = Node(7, "optic cover", optic);
        var launcher = Node(8, "M203A2", upper);
        launcher.Props.isWeaponSubassembly = true;
        var barrel = Node(9, "launcher barrel", launcher);
        var trigger = Node(10, "launcher trigger", launcher);
        var launcherSight = Node(11, "launcher optic", launcher);
        var nestedGun = Node(12, "future nested weapon", launcher);
        nestedGun.Props.isWeaponSubassembly = true;
        var nestedBarrel = Node(13, "nested barrel", nestedGun);
        var mislabeled = Node(14, "M203A2 replica panel", upper);
        var snapshot = new List<ModularRenderNode>
            { cover, trigger, optic, launcher, rifle, rear, magnifier, launcherSight,
                nestedGun, front, nestedBarrel, barrel, upper, mislabeled };
        var ironGroup = new ModularSightGroupStatus { index = 0 };
        ironGroup.members.AddRange(new[] { front.thing, rear.thing });
        var opticalGroup = new ModularSightGroupStatus { index = 1, primary = optic.thing };
        // Even if combat currently groups axes at the same height, independent
        // weapons must have independently selectable optical views.
        opticalGroup.members.AddRange(new[] { optic.thing, magnifier.thing, launcherSight.thing });
        var sights = new[] { ironGroup, opticalGroup };
        var groups = ModularWeaponPreviewGroups.Build(snapshot, sights);
        CheckIds(groups.Single(group => group.Key == "main"), 1, 2, 3, 4, 5, 6, 7, 14);
        CheckIds(groups.Single(group => group.Key == "weapon:8"), 8, 9, 10, 11);
        CheckIds(groups.Single(group => group.Key == "weapon:12"), 12, 13);
        CheckIds(groups.Single(group => group.Key == "sights"), 3, 4, 5, 6, 7, 11);
        CheckIds(groups.Single(group => group.Key == "sight:1:3"), 3, 4);
        CheckIds(groups.Single(group => group.Key == "sight:1:5"), 5, 6, 7);
        CheckIds(groups.Single(group => group.Key == "sight:8:11"), 11);
        if (groups.Count(group => group.Kind == ModularWeaponPreviewGroupKind.AttachedWeapon) != 2)
            throw new Exception("Subweapon groups must use declared ownership rather than part names.");
        if (groups.Single(group => group.Key == "sight:1:5").Root != optic)
            throw new Exception("Selecting an optical group must select its primary sight.");
        snapshot.Remove(launcherSight);
        var updated = ModularWeaponPreviewGroups.Build(snapshot, sights);
        if (updated.Any(group => group.Key == "sight:8:11")
            || updated.Any(group => group.PartIds.Contains(11)))
            throw new Exception("Removed parts must not survive in group views.");
        var plain = ModularWeaponPreviewGroups.Build(new List<ModularRenderNode> { rifle },
            Array.Empty<ModularSightGroupStatus>());
        if (plain.Count != 1 || plain[0].Key != "all")
            throw new Exception("Assemblies without optics or attached weapons must have no empty group options.");
        var empty = ModularWeaponPreviewGroups.Build(new List<ModularRenderNode>(), sights);
        if (empty.Count != 1 || empty[0].PartIds.Count != 0)
            throw new Exception("An empty snapshot must retain a safe whole-assembly fallback.");
        Console.WriteLine("PASS: main/attached/nested weapon groups, actual sight membership, optic children, shared axes, removal and empty groups.");
    }

    private static void CheckIds(ModularWeaponPreviewGroup group, params int[] ids)
    {
        if (!group.PartIds.SetEquals(ids))
            throw new Exception("Incorrect members for " + group.Key + ": " + string.Join(",", group.PartIds));
    }

    private static ModularRenderNode Node(int id, string label, ModularRenderNode parent = null)
        => new ModularRenderNode
        {
            thing = new Thing { thingIDNumber = id, LabelCap = label }, comp = new CompModularWeaponNode(),
            parentComp = parent?.comp, depth = (parent?.depth ?? -1) + 1
        };
}
