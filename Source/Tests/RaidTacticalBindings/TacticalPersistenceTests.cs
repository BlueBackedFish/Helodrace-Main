using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using Helodrace;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class TacticalPersistenceTests
{
    internal static void Run()
    {
        var binding = AccessTools.Field(typeof(DefOfHelper), "bindingNow");
        bool previousBinding = (bool)binding.GetValue(null);
        binding.SetValue(null, true);
        try { RuntimeHelpers.RunClassConstructor(typeof(ThingDefOf).TypeHandle); }
        finally { binding.SetValue(null, previousBinding); }
        string path = Path.Combine("Defs", "ColdWar", "BreachExplosive_ColdWar.xml");
        XElement definition = XDocument.Load(path).Root.Elements("ThingDef")
            .Single(def => (string)def.Element("defName") == "HD_InstalledBreachCharge");
        ThingCategory category = Enum.Parse<ThingCategory>((string)definition.Element("category"));
        // Definition constructors load Unity shaders. This native spawn predicate
        // only needs these explicit fields; the actual XML is also tested in game.
        ThingDef SpawnDef(ThingCategory kind, Type type)
        {
            var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            def.category = kind; def.thingClass = type; def.destroyable = true;
            def.comps = new System.Collections.Generic.List<CompProperties>(); return def;
        }
        var wall = SpawnDef(ThingCategory.Building, typeof(Building));
        wall.building = new BuildingProperties();
        wall.passability = Traversability.Impassable; wall.surfaceType = SurfaceType.None;
        var oldCharge = SpawnDef(ThingCategory.Item, typeof(Thing_InstalledBreachCharge));
        var charge = SpawnDef(category, typeof(Thing_InstalledBreachCharge));
        if (!GenSpawn.SpawningWipes(wall, oldCharge))
            throw new Exception("The installed game's load-conflict regression fixture did not reproduce.");
        if (GenSpawn.SpawningWipes(wall, charge) || GenSpawn.SpawningWipes(charge, wall))
            throw new Exception("Loading/spawning an installed charge must preserve the target wall.");
        Console.WriteLine("PASS: installed native spawn rules reproduce Item/wall load conflict; current charge XML coexists with its wall in both directions.");
    }
}
