using System;
using Helodrace.ModernWar;

internal static class ChildTransferTests
{
    public static void Run()
    {
        var oldBarrel = Owner("muzzle");
        var newBarrel = Owner("muzzle");
        var muzzle = Part("muzzle");
        muzzle.Comp.Props.sockets.Add(new ModularAttachmentSocket { id = "suppressor" });
        var suppressor = Part("suppressor");
        Attach(muzzle.Comp, suppressor, "suppressor");
        Attach(oldBarrel, muzzle, "muzzle");
        Check(ModularWeaponChildTransfer.Move(oldBarrel, newBarrel).Count == 0
            && oldBarrel.ChildCount == 0 && newBarrel.ChildAt(0) == muzzle
            && muzzle.Comp.ChildAt(0) == suppressor,
            "A barrel swap must retain the same muzzle and its entire suppressor subtree.");

        var missing = Owner("other");
        var removed = ModularWeaponChildTransfer.Move(newBarrel, missing);
        Check(removed.Count == 1 && removed[0] == muzzle && muzzle.Comp.ChildAt(0) == suppressor,
            "Unmatched sockets return intact child assemblies for storage.");

        var previous = Owner("rail", true);
        var shorter = Owner("rail", true);
        var first = Part("rail"); var second = Part("rail");
        Attach(previous, first, "rail", -0.04f); Attach(previous, second, "rail", 0.04f);
        shorter.PlacementAllowed = (thing, socket, offset) => shorter.ChildCount == 0;
        removed = ModularWeaponChildTransfer.Move(previous, shorter);
        Check(removed.Count == 2 && removed.Contains(first) && removed.Contains(second)
            && shorter.ChildCount == 0 && previous.ChildCount == 0,
            "A rail that cannot hold the complete group must retain none of the transferred parts.");

        previous = Owner("rail", true); shorter = Owner("rail", true);
        Attach(previous, first, "rail", 0.2f);
        shorter.PlacementAllowed = (thing, socket, offset) => Math.Abs(offset) < 0.05f;
        shorter.FreeOffset = 0.03f;
        Check(ModularWeaponChildTransfer.Move(previous, shorter).Count == 0
            && shorter.ChildAt(0) == first && shorter.RailOffsetAt(0) == 0.03f,
            "A shorter rail may reuse a valid free position before declaring insufficient space.");

        previous = Owner("muzzle"); var target = Owner("muzzle");
        muzzle.Comp.Props.mounts[0].SocketId = "old-interface";
        muzzle.Comp.Props.mounts.Add(new ModularAttachmentMount { id = "replacement", SocketId = "muzzle" });
        // Model an old mount which was compatible before the new interface was selected.
        previous.Children.Add(muzzle); previous.SocketIds.Add("muzzle");
        previous.MountIds.Add("mount"); previous.Offsets.Add(0f);
        Check(ModularWeaponChildTransfer.Move(previous, target).Count == 0
            && target.MountIdAt(0) == "replacement", "A matching socket can select another compatible child mount.");
        Console.WriteLine("PASS: barrel/muzzle subtree retention, unmatched returns, atomic rail transfer, shorter rails and alternate mounts.");
    }

    private static CompModularWeaponNode Owner(string socket, bool rail = false)
    {
        var node = new CompModularWeaponNode();
        node.Props.sockets.Add(new ModularAttachmentSocket { id = socket, isRail = rail });
        return node;
    }
    private static Thing Part(string socket)
    {
        var thing = new Thing { Comp = new CompModularWeaponNode() };
        thing.Comp.Props.mounts.Add(new ModularAttachmentMount { id = "mount", SocketId = socket });
        return thing;
    }
    private static void Attach(CompModularWeaponNode parent, Thing child, string socket, float offset = 0f)
    {
        if (!parent.TryAttach(child, socket, "mount", offset, out _)) throw new Exception("Invalid test fixture.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
