using System;
using System.Collections.Generic;
using Helodrace.ModernWar;

internal static class FunctionTests
{
    public static void Run()
    {
        var barrel = Expected("barrel", ModularWeaponPartCategory.Required);
        var gas = Expected("gas", ModularWeaponPartCategory.Functional,
            ModularWeaponMissingFunction.SemiAutomaticOnly);
        var magazine = Expected("magazine", ModularWeaponPartCategory.Functional,
            ModularWeaponMissingFunction.SingleRoundCapacity);
        var root = new CompModularWeaponNode();
        root.Props.sockets.Add(new ModularAttachmentSocket { id = "barrel", expectedPart = barrel });
        root.Props.sockets.Add(new ModularAttachmentSocket { id = "gas", expectedPart = gas });
        root.Props.sockets.Add(new ModularAttachmentSocket { id = "magazine", expectedPart = magazine });
        var missing = ModularWeaponFunctionResolver.Resolve(root);
        Check(!missing.CanFire && missing.missingRequiredParts.Count == 1,
            "A bare assembly must remain blocked by missing firing-critical parts.");
        Check(missing.semiAutomaticOnly && missing.singleRoundCapacity
            && missing.missingFunctionalParts.Count == 2,
            "Missing functional parts retain their fire-mode and capacity penalties.");
        Add(root, "barrel", new CompModularWeaponNode());
        var degraded = ModularWeaponFunctionResolver.Resolve(root);
        Check(degraded.CanFire && degraded.semiAutomaticOnly && degraded.singleRoundCapacity,
            "A compatible replacement satisfies the socket without matching the reference part.");
        var gasNode = new CompModularWeaponNode();
        Add(root, "gas", gasNode);
        Add(root, "magazine", new CompModularWeaponNode());
        var complete = ModularWeaponFunctionResolver.Resolve(root);
        Check(complete.CanFire && !complete.semiAutomaticOnly && !complete.singleRoundCapacity,
            "Installing functional replacements clears the penalties.");
        gasNode.Props.sockets.Add(new ModularAttachmentSocket { id = "inner", expectedPart = barrel });
        for (int i = 0; i < 3; i++)
        {
            Check(!ModularWeaponFunctionResolver.Resolve(root).CanFire && gasNode.ChildCount == 0,
                "Repeatedly inspecting a missing nested part must not create or restore it.");
        }
        Check(ModularWeaponFunctionResolver.Resolve(null).CanFire,
            "A missing assembly must be safe to inspect.");
        Console.WriteLine("PASS: missing-part socket requirements, compatible replacements, functional penalties and no automatic children.");
    }

    private static ThingDef Expected(string label, ModularWeaponPartCategory category,
        params ModularWeaponMissingFunction[] effects)
        => new ThingDef { LabelCap = label, Props = new CompProperties_ModularWeaponNode
            { partCategory = category, missingFunctions = new List<ModularWeaponMissingFunction>(effects) } };

    private static void Add(CompModularWeaponNode parent, string socket, CompModularWeaponNode child)
    {
        parent.Children.Add(new Thing { Comp = child });
        parent.SocketIds.Add(socket);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

namespace Verse
{
    public static class TestCollectionExtensions
    {
        public static bool NullOrEmpty<T>(this ICollection<T> value) => value == null || value.Count == 0;
        public static bool NullOrEmpty(this string value) => string.IsNullOrEmpty(value);
    }
}
