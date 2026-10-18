using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Helodrace.ModernWar;
using Verse;

internal static class PartConflictTests
{
    private static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static FieldInfo FieldOfType(Type type, Type fieldType)
    {
        for (; type != null; type = type.BaseType)
        {
            var field = type.GetFields(Fields | BindingFlags.DeclaredOnly).FirstOrDefault(f => f.FieldType == fieldType);
            if (field != null) return field;
        }
        throw new Exception("Missing fixture field " + fieldType);
    }
    internal static CompModularWeaponNode Node(string name, ThingDef def = null)
    {
        var thing = Bare<ThingWithComps>();
        thing.def = def ?? Bare<ThingDef>(); thing.def.defName = name; thing.def.label = name;
        thing.def.comps ??= new List<CompProperties>();
        var props = thing.def.GetCompProperties<CompProperties_ModularWeaponNode>() ?? new CompProperties_ModularWeaponNode();
        thing.def.comps = new List<CompProperties> { props };
        var comp = new CompModularWeaponNode { parent = thing, props = props };
        FieldOfType(typeof(ThingWithComps), typeof(List<ThingComp>)).SetValue(thing, new List<ThingComp> { comp });
        return comp;
    }
    // Seed a tree without graphics, map initialization, ThingMaker or game stat workers.
    internal static void Add(CompModularWeaponNode parent, CompModularWeaponNode child)
    {
        var owner = parent.GetDirectlyHeldThings();
        var field = FieldOfType(owner.GetType(), typeof(List<Thing>));
        var list = (List<Thing>)field.GetValue(owner);
        list.Add(child.parent);
        typeof(Thing).GetField("holdingOwner", Fields).SetValue(child.parent, owner);
    }
    internal static void Run(Action<bool, string> check)
    {
        var root = Node("root"); var stock = Node("wood"); var barrel = Node("shortBarrel");
        stock.Props.incompatibleParts.Add(barrel.parent.def);
        check(ModularWeaponPartConflicts.Conflicts(stock.parent.def, barrel.parent.def), "One-sided XML declaration blocks the pair");
        check(ModularWeaponPartConflicts.Conflicts(barrel.parent.def, stock.parent.def), "Part conflicts are symmetric");
        check(!ModularWeaponPartConflicts.Conflicts(null, stock.parent.def), "Missing defs are safe");
        Add(root, stock);
        var conflict = ModularWeaponPartConflicts.Find(root, barrel.parent.def, barrel);
        check(conflict?.Part == barrel.parent.def && conflict.Other == stock.parent.def, "Sibling conflict identifies both actual definitions");
        check(root.ChildCount == 1 && barrel.parent.ParentHolder == null, "Conflict checks never attach, detach or destroy parts");
        check(ModularWeaponPartConflicts.Find(root, barrel.parent.def, barrel, stock.parent) == null, "Replacement excludes the old part");

        var grip = Node("grip"); var ebrStock = Node("ebrStock"); Add(grip, ebrStock);
        stock.Props.incompatibleParts.Add(ebrStock.parent.def);
        check(ModularWeaponPartConflicts.Find(root, grip.parent.def, grip)?.Other == stock.parent.def, "Preassembled incoming descendants cannot bypass conflicts");
        var receiver = Node("receiver"); Add(root, receiver);
        check(ModularWeaponPartConflicts.Find(receiver, grip.parent.def, grip)?.Other == stock.parent.def, "Nested attachment checks other branches of the weapon");
        stock.Props.incompatibleParts.Add(receiver.parent.def);
        check(ModularWeaponPartConflicts.Find(receiver, Node("safe").parent.def) == null, "An unrelated old conflict does not block repairs");
        receiver.Props.incompatibleParts.Add(grip.parent.def);
        check(ModularWeaponPartConflicts.Find(receiver, grip.parent.def)?.Other == receiver.parent.def, "Ancestor conflict is enforced");

        var subtree = Node("subtree"); var nested = Node("nested"); Add(subtree, nested); Add(root, subtree);
        nested.Props.incompatibleParts.Add(barrel.parent.def);
        check(ModularWeaponPartConflicts.Find(root, barrel.parent.def, barrel, stock.parent) != null, "Ignoring one replaced branch preserves conflicts in other branches");
        var clean = Node("clean");
        check(ModularWeaponPartConflicts.Find(subtree, barrel.parent.def, barrel, subtree.parent) != null, "Excluding a replaced subtree still checks surrounding siblings");
        check(ModularWeaponPartConflicts.Find(clean, barrel.parent.def, barrel) == null, "Identical part candidates are allowed on separate weapons");
        var replacement = Node("replacement"); nested.Props.incompatibleParts.Add(replacement.parent.def);
        check(ModularWeaponPartConflicts.Find(root, replacement.parent.def) != null, "Installed descendants participate in compatibility checks");
        check(ModularWeaponPartConflicts.Find(root, replacement.parent.def, ignoredSubtree: subtree.parent) == null,
            "Replacement excludes all descendants of the removed branch, not just its root");

        var reverseRoot = Node("reverseRoot"); var installedBarrel = Node("shortBarrel", barrel.parent.def); Add(reverseRoot, installedBarrel);
        check(ModularWeaponPartConflicts.Find(reverseRoot, stock.parent.def, Node("wood", stock.parent.def)) != null,
            "Reversing actual installation order still rejects the same pair");

        var invalid = Node("invalid"); var a = Node("a"); var b = Node("b");
        a.Props.incompatibleParts.Add(b.parent.def); Add(invalid, a); Add(invalid, b);
        check(ModularWeaponPartConflicts.Find(clean, invalid.parent.def, invalid) != null, "Incoming internally invalid assemblies are rejected");
        check(ModularWeaponPartConflicts.Find(root, clean.parent.def, clean, root.parent) == null, "Whole-weapon preset replacement excludes the entire old tree");

        var conditionalRoot = Node("conditionalRoot"); var sight = Node("sight"); var guard = Node("guard");
        var shortTube = Node("shortTube"); var longTube = Node("longTube");
        var rule = new ModularWeaponConditionalPartConflict {part=guard.parent.def,whenAnyParts=new List<ThingDef>{shortTube.parent.def}};
        sight.Props.conditionalIncompatibleParts.Add(rule);
        Add(conditionalRoot, sight); Add(conditionalRoot, guard); Add(conditionalRoot, shortTube);
        check(ModularWeaponPartConflicts.Find(conditionalRoot, Node("unrelated").parent.def) == null,
            "An unrelated candidate must not be blocked by an existing conditional conflict");
        check(ModularWeaponPartConflicts.Find(conditionalRoot, longTube.parent.def, longTube, shortTube.parent) == null,
            "Replacing the condition barrel with a compatible barrel repairs the assembly");
        var newSight = Node("replacementSight", sight.parent.def);
        check(ModularWeaponPartConflicts.Find(conditionalRoot, newSight.parent.def, newSight, sight.parent)?.Condition == shortTube.parent.def,
            "Replacing one part with the same conflicting part must still be rejected");
        var conditionalTree = Node("conditionalTree");
        Add(conditionalTree, Node("sight", sight.parent.def));
        Add(conditionalTree, Node("guard", guard.parent.def));
        var nestedBarrel = Node("shortTube", shortTube.parent.def); Add(conditionalTree, nestedBarrel);
        check(ModularWeaponPartConflicts.Find(clean, conditionalTree.parent.def, conditionalTree)?.Condition == shortTube.parent.def,
            "An internally invalid incoming subtree cannot bypass conditional conflicts");
        check(ModularWeaponPartConflicts.Find(conditionalRoot, longTube.parent.def, longTube, conditionalRoot.parent) == null,
            "Whole-weapon replacement excludes all obsolete condition parts");
        check(!rule.ConfigErrors(sight.parent.def).Any(), "A valid conditional rule has no configuration errors");
        check(new ModularWeaponConditionalPartConflict().ConfigErrors(sight.parent.def).Count() == 2,
            "Missing conditional targets and empty condition lists are reported");
        rule.whenAnyParts.Add(shortTube.parent.def);
        check(rule.ConfigErrors(sight.parent.def).Any(), "Duplicate conditions are reported");
        rule.whenAnyParts = new List<ThingDef>{sight.parent.def, guard.parent.def, null};
        check(rule.ConfigErrors(sight.parent.def).Count() == 3, "Self, target and null conditions are invalid");
        Console.WriteLine("PASS: production symmetric conflicts, full subtree traversal, nonmutation and replacement exclusions.");
    }
}
