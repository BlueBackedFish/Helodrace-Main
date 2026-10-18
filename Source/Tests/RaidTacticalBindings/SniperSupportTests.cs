using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class SniperSupportTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        const int start = 100;
        var session = new HelodSniperSupportSession(null, HelodSniperSupportDirection.West, start);
        Check(session.CanRequestShot(start) && session.RemainingTicks(start) == 5000
            && session.RemainingActions == 5, "Deployment provides two in-game hours and five actions.");
        Check(session.TryConsumeAction(start) && session.RemainingActions == 4
            && session.RemainingTicks(start + 2500) == 2500 && session.RemainingActions == 4,
            "Requests consume one action; passing time does not reset or consume actions.");
        session = RoundTrip(session);
        Check(session.Direction == HelodSniperSupportDirection.West && session.RemainingActions == 4
            && session.RemainingTicks(start + 2500) == 2500, "Save/load retains direction, deadline and used actions.");
        for (int i = 0; i < 4; i++)
            Check(session.TryConsumeAction(start + 2500), "Each remaining action can be used once.");
        Check(!session.TryConsumeAction(start + 2500) && session.RemainingActions == 0
            && session.IsWithinDuration(start + 2500),
            "A sixth request is rejected, but the fifth pending shot retains its execution window.");
        var expiring = new HelodSniperSupportSession(null, HelodSniperSupportDirection.North, start);
        Check(expiring.CanRequestShot(start + 4999) && !expiring.TryConsumeAction(start + 5000)
            && !expiring.IsWithinDuration(start + 5000) && expiring.RemainingActions == 5
            && expiring.RemainingTicks(start + 6000) == 0,
            "Expiry rejects requests at the exact deadline without spending an action.");

        var size = new IntVec3(11, 1, 17);
        var edgePoints = new[] { new IntVec3(5, 0, 16), new IntVec3(10, 0, 8),
            new IntVec3(5, 0, 0), new IntVec3(0, 0, 8) };
        for (int direction = 0; direction < 4; direction++)
        {
            var candidates = HelodSniperSupportSession.EdgeCells(size, (HelodSniperSupportDirection)direction).ToList();
            Check(candidates.Count == (direction % 2 == 0 ? size.x : size.z)
                && candidates.Distinct().Count() == candidates.Count
                && candidates.All(cell => HelodSniperSupportSession.IsOnEdge(cell, size,
                    (HelodSniperSupportDirection)direction)),
                "Sight search enumerates the entire selected edge exactly once and excludes other directions.");
            for (int point = 0; point < 4; point++)
                Check(HelodSniperSupportSession.IsOnEdge(edgePoints[point], size,
                    (HelodSniperSupportDirection)direction) == (direction == point),
                    "Each cardinal direction uses only its corresponding edge on a rectangular map.");
            foreach (var invalid in new[] { IntVec3.Invalid, new IntVec3(-1, 0, 0),
                new IntVec3(11, 0, 16), new IntVec3(0, 0, 17), new IntVec3(5, 0, 8) })
                Check(!HelodSniperSupportSession.IsOnEdge(invalid, size, (HelodSniperSupportDirection)direction),
                    "Interior and out-of-bounds cells cannot become sniper sources.");
        }
        Console.WriteLine("PASS: " + checks + " sniper duration, action budget, save/load and map direction checks.");
    }

    private static HelodSniperSupportSession RoundTrip(HelodSniperSupportSession session)
    {
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-sniper-" + Guid.NewGuid() + ".xml");
        var cache = (IDictionary)AccessTools.Field(typeof(GenTypes), "typeCache").GetValue(null);
        Type key = typeof(GenTypes).GetNestedType("TypeCacheKey", BindingFlags.NonPublic);
        Type type = typeof(HelodSniperSupportSession);
        cache[Activator.CreateInstance(key, new object[] { type.FullName, null })] = type;
        bool previous = ScribeMetaHeaderUtility.modListChanged;
        ScribeMetaHeaderUtility.modListChanged = true;
        try
        {
            Scribe.saver.InitSaving(path, "sniper");
            Scribe_Deep.Look(ref session, "session");
            Scribe.saver.FinalizeSaving();
            HelodSniperSupportSession loaded = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref loaded, "session");
            Scribe.ExitNode();
            foreach (string field in new[] { "curXmlParent", "curParent", "curPathRelToParent" })
                AccessTools.Field(typeof(ScribeLoader), field).SetValue(Scribe.loader, null);
            Scribe.mode = LoadSaveMode.Inactive;
            var crossRefs = AccessTools.Field(typeof(ScribeLoader), "crossRefs").GetValue(Scribe.loader);
            AccessTools.Method(crossRefs.GetType(), "ResolveAllCrossReferences").Invoke(crossRefs, null);
            var initer = AccessTools.Field(typeof(ScribeLoader), "initer").GetValue(Scribe.loader);
            AccessTools.Method(initer.GetType(), "DoAllPostLoadInits").Invoke(initer, null);
            return loaded;
        }
        finally
        {
            Scribe.mode = LoadSaveMode.Inactive;
            ScribeMetaHeaderUtility.modListChanged = previous;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
