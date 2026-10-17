using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.ModernWar;

internal static class Program
{
    private static int assertions;

    private static void Main()
    {
        CameraTests.Run();
        ImageBoundsTests.Run();
        GroupTests.Run();
        StatMotionTests.Run();
        SocketLayoutTests.Run();
        FunctionTests.Run();
        var lower = Part(1, "하부 리시버");
        var upper = Part(2, "상부 리시버", lower, "upper", "상부 결합부", 0);
        var barrel = Part(3, "10.3인치 총열", upper, "barrel", "총열 결합부", 0);
        var muzzle = Part(4, "소염기", barrel, "muzzle", "총구", 0);
        var handguard = Part(5, "RIS II", upper, "handguard", "핸드가드", 1);
        var light = Part(6, "전술 조명", handguard, "side", "우측 레일", 0);
        var stock = Part(7, "개머리판", lower, "stock", "스톡 결합부", 1);
        var secondLight = Part(8, "전술 조명", handguard, "side", "우측 레일", 1);
        // Simulate draw-layer order, which is neither preorder nor root-first.
        var snapshot = new List<ModularRenderNode>
            { light, muzzle, stock, upper, secondLight, lower, handguard, barrel };
        var tree = Tree(snapshot);
        var collapsed = new HashSet<int>();
        CheckIds(tree.VisibleRows(collapsed, ""), 1, 2, 3, 4, 5, 6, 8, 7);
        var rows = tree.VisibleRows(collapsed, "");
        Check(rows[0].DescendantCount == 7, "Descendant totals must span all levels.");
        Check(rows.Single(row => row.Node == light).Depth == 3,
            "Depth must follow ownership rather than snapshot order.");
        Check(rows.Single(row => row.Node == light).Socket.Label == "우측 레일",
            "A part must retain its actual mounting socket.");
        Check(tree.Breadcrumb(light) == "하부 리시버  ›  상부 리시버  ›  RIS II  ›  전술 조명",
            "Breadcrumb must include every ancestor.");

        collapsed.Add(upper.thing.thingIDNumber);
        CheckIds(tree.VisibleRows(collapsed, ""), 1, 2, 7);
        CheckIds(tree.VisibleRows(collapsed, "  조명  "), 1, 2, 5, 6, 8);
        Check(tree.VisibleRows(collapsed, "조명").Count(row => row.MatchesSearch) == 2,
            "Same-name parts must remain separate selectable instances.");
        CheckIds(tree.VisibleRows(collapsed, "우측 레일"), 1, 2, 5, 6, 8);
        CheckIds(tree.VisibleRows(collapsed, "ris ii"), 1, 2, 5);
        Check(tree.VisibleRows(collapsed, "not present").Count == 0,
            "Unmatched searches must not display unrelated branches.");
        var scope = new HashSet<int> { light.thing.thingIDNumber, secondLight.thing.thingIDNumber };
        CheckIds(tree.VisibleRows(new HashSet<int>(), "", scope), 1, 2, 5, 6, 8);
        Check(tree.VisibleRows(new HashSet<int>(), "", scope).Count(row => row.IsContext) == 3,
            "Group views must distinguish contextual ancestors from actual group members.");
        Check(tree.VisibleRows(collapsed, "총열", scope).Count == 0,
            "Scoped searches must not include unrelated parts.");
        Check(tree.VisibleRows(collapsed, "리시버", scope).Count == 0,
            "Scoped searches must not match contextual ancestors.");
        CheckIds(tree.VisibleRows(collapsed, "", scope), 1, 2);
        CheckIds(tree.VisibleRows(collapsed, "조명", scope), 1, 2, 5, 6, 8);
        CheckIds(tree.VisibleRows(collapsed, ""), 1, 2, 7);
        tree.Reveal(light, collapsed);
        CheckIds(tree.VisibleRows(collapsed, ""), 1, 2, 3, 4, 5, 6, 8, 7);
        tree.Focus(light, collapsed);
        CheckIds(tree.VisibleRows(collapsed, ""), 1, 2, 3, 5, 6, 8, 7);
        Check(!collapsed.Contains(lower.thing.thingIDNumber)
            && !collapsed.Contains(upper.thing.thingIDNumber)
            && !collapsed.Contains(handguard.thing.thingIDNumber),
            "Focusing a deep part must keep all ancestors expanded.");

        var boltSocket = new ModularAttachmentSocket { id = "bolt", Label = "볼트 결합부" };
        upper.Props.sockets.Add(boltSocket);
        boltSocket.expectedPart = new ThingDef { Props = new CompProperties_ModularWeaponNode
            { partCategory = ModularWeaponPartCategory.Required } };
        var mandatory = new ModularAttachmentSocket
            { id = "mandatory", Label = "필수 결합부", required = true };
        lower.Props.sockets.Add(mandatory);
        lower.Props.sockets.Add(new ModularAttachmentSocket { id = "optional", Label = "빈 레일" });
        tree = Tree(snapshot);
        rows = tree.VisibleRows(new HashSet<int>(), "");
        Check(rows.Count(row => row.IsEmpty) == 2,
            "Both required sockets and missing Required-category expectations need warning rows.");
        Check(rows[0].DescendantCount == 7, "Vacancies must not inflate attached part totals.");
        var empty = rows.Single(row => row.IsEmpty && row.Socket == boltSocket);
        Check(empty.Owner == upper && empty.Depth == 2,
            "A missing part must target its real parent and socket for installation.");
        CheckIds(tree.VisibleRows(collapsed, "볼트 결합부").Where(row => !row.IsEmpty).ToList(), 1, 2);
        Check(tree.VisibleRows(collapsed, "볼트 결합부").Last().IsEmpty,
            "Searching a missing socket must expose its warning through collapsed ancestors.");

        var hidden = Tree(snapshot, node => node == light || node == secondLight,
            socket => socket == boltSocket);
        rows = hidden.VisibleRows(new HashSet<int>(), "");
        Check(!rows.Any(row => row.Node == light || row.Node == secondLight || row.Socket == boltSocket),
            "CE-hidden parts and sockets must not leak into navigation or search.");
        Check(hidden.VisibleRows(new HashSet<int>(), "조명").Count == 0,
            "Hidden parts must not be searchable.");
        snapshot.Remove(muzzle);
        var rebuilt = Tree(snapshot);
        Check(!rebuilt.VisibleRows(new HashSet<int>(), "").Any(row => row.Node == muzzle),
            "Rebuilding after removal must discard stale instances.");
        Check(Tree(new List<ModularRenderNode>()).VisibleRows(collapsed, "").Count == 0,
            "Empty snapshots must be safe.");
        Console.WriteLine($"PASS: {assertions} navigation checks (layer order, deep paths, collapse/reveal, search, duplicate names, missing required parts, hidden parts and removal).");
    }

    private static ModularWeaponNavigationTree Tree(List<ModularRenderNode> snapshot,
        Predicate<ModularRenderNode> hidePart = null,
        Predicate<ModularAttachmentSocket> hideSocket = null)
        => new ModularWeaponNavigationTree(snapshot, hidePart ?? (_ => false), hideSocket ?? (_ => false));

    private static ModularRenderNode Part(int id, string label, ModularRenderNode parent = null,
        string socketId = null, string socketLabel = null, int index = 0)
    {
        if (parent != null && parent.Props.SocketNamed(socketId) == null)
            parent.Props.sockets.Add(new ModularAttachmentSocket { id = socketId, Label = socketLabel });
        return new ModularRenderNode
        {
            thing = new Thing { thingIDNumber = id, LabelCap = label },
            comp = new CompModularWeaponNode(),
            parentComp = parent?.comp,
            parentSocketId = socketId,
            parentChildIndex = index
        };
    }

    private static void CheckIds(List<ModularWeaponNavigationTree.Entry> rows, params int[] ids)
        => Check(rows.Select(row => row.Node?.thing.thingIDNumber ?? -1).SequenceEqual(ids),
            "Unexpected tree order: " + string.Join(", ", rows.Select(row => row.Node?.thing.thingIDNumber ?? -1)));

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
}

// Only engine data containers are stubbed; traversal and search use production code.
namespace Helodrace.ModernWar
{
    public enum ModularWeaponPartCategory { Required, Functional, Optional }
    public enum ModularWeaponMissingFunction { SemiAutomaticOnly, SingleRoundCapacity }
    public sealed class Thing
    {
        public int thingIDNumber;
        public string LabelCap;
        public CompModularWeaponNode Comp;
        public T TryGetComp<T>() where T : class => Comp as T;
    }
    public sealed class ModularSightGroupStatus
    {
        public int index;
        public Thing primary;
        public readonly List<Thing> members = new List<Thing>();
    }
    public sealed class ThingDef
    {
        public string LabelCap;
        public CompProperties_ModularWeaponNode Props;
        public T GetCompProperties<T>() where T : class => Props as T;
    }
    public sealed class ModularAttachmentSocket { public string id; public string Label; public bool required; public ThingDef expectedPart; }
    public sealed class CompProperties_ModularWeaponNode
    {
        public ModularWeaponPartCategory partCategory = ModularWeaponPartCategory.Optional;
        public bool isAssemblyRoot, isWeaponSubassembly;
        public List<ModularWeaponMissingFunction> missingFunctions = new List<ModularWeaponMissingFunction>();
        public List<ModularAttachmentSocket> sockets = new List<ModularAttachmentSocket>();
        public ModularAttachmentSocket SocketNamed(string id) => sockets.FirstOrDefault(socket => socket.id == id);
    }
    public sealed class CompModularWeaponNode
    {
        public CompProperties_ModularWeaponNode Props = new CompProperties_ModularWeaponNode();
        public CompModularWeaponNode AssemblyRoot => this;
        public readonly List<Thing> Children = new List<Thing>();
        public readonly List<string> SocketIds = new List<string>();
        public int ChildCount => Children.Count;
        public Thing ChildAt(int index) => Children[index];
        public string SocketIdAt(int index) => SocketIds[index];
    }
    public sealed class ModularRenderNode
    {
        public Thing thing;
        public CompModularWeaponNode comp, parentComp;
        public string parentSocketId;
        public int parentChildIndex;
        public int depth;
        public CompProperties_ModularWeaponNode Props => comp.Props;
    }
}
