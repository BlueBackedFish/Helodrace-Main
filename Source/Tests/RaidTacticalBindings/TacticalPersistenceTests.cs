using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using Helodrace;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Helodrace.Tactics;

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
        CheckFieldSmokeJobIdentity();
    }

    private static void CheckFieldSmokeJobIdentity()
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.jobs = (Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker));
        Job Make(int id)
        {
            var job = (Job)RuntimeHelpers.GetUninitializedObject(typeof(Job));
            job.loadID = id; return job;
        }
        var throwing = Make(30); var guard = Make(31); var restoredThrow = Make(30);
        var smoke = new TacticalFieldSmoke { Thrower = pawn, Job = throwing, Launched = true, Returned = true };
        var current = AccessTools.Field(typeof(Pawn_JobTracker), "curJob");
        var active = AccessTools.PropertyGetter(typeof(TacticalFieldSmoke), "ActiveJob");
        var savedId = AccessTools.Field(typeof(TacticalFieldSmoke), "SavedJobId");
        var restore = AccessTools.Method(typeof(TacticalFieldSmoke), "RestoreJob");
        Job Active() => (Job)active.Invoke(smoke, null);
        current.SetValue(pawn.jobs, throwing);
        if (Active() != throwing) throw new Exception("A still-current native throw/return Job must retain its identity.");
        savedId.SetValue(smoke, throwing.loadID);
        current.SetValue(pawn.jobs, restoredThrow);
        restore.Invoke(smoke, new object[] { restoredThrow });
        if (smoke.Job != restoredThrow || Active() != restoredThrow)
            throw new Exception("Restore must bind the native restored instance with the saved smoke Job identity.");
        current.SetValue(pawn.jobs, guard);
        if (Active() != null) throw new Exception("Completed smoke must not claim the subsequent guard Job as active.");
        restore.Invoke(smoke, new object[] { guard });
        if (smoke.Job != null) throw new Exception("A member's different guard Job must never bind to the smoke action.");
        restore.Invoke(smoke, new object[] { restoredThrow });
        if (smoke.Job != null) throw new Exception("An inactive native Job must not bind even with a matching saved ID.");
        savedId.SetValue(smoke, -1);
        current.SetValue(pawn.jobs, restoredThrow);
        restore.Invoke(smoke, new object[] { restoredThrow });
        if (smoke.Job != null) throw new Exception("A completed screen with no active saved Job must remain unbound.");
        current.SetValue(pawn.jobs, null);
        if (Active() != null) throw new Exception("Two absent Jobs must not imply an active smoke action.");
        Console.WriteLine("PASS: active smoke native Job rebinding; completed smoke rejects guard/stale/unsaved Jobs and absent-job equality.");
    }
}
