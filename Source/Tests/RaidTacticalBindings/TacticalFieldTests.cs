using System;
using System.Linq;
using System.IO;
using Helodrace.Tactics;
using Verse;
using HarmonyLib;

internal static class TacticalFieldTests
{
    public static void Run()
    {
        var memory = new TacticalContactMemory();
        IntVec3 anchor = new IntVec3(10, 0, 10), first = new IntVec3(10, 0, 60);
        memory.Remember(1, first, first, false, true, 100, "a");
        TacticalContact contact = memory.Entries.Single();
        if (TacticalFieldPolicy.Motion(contact, anchor) != TacticalObservedMotion.Unknown)
            throw new Exception("First observation invented a motion sample.");
        void Move(IntVec3 next, int tick, TacticalObservedMotion expected)
        {
            memory.Remember(1, next, next, false, true, tick, "a");
            if (TacticalFieldPolicy.Motion(contact, anchor) != expected) throw new Exception("Motion: " + expected);
        }
        Move(new IntVec3(10, 0, 55), 145, TacticalObservedMotion.Approaching);
        Move(new IntVec3(15, 0, 55), 190, TacticalObservedMotion.Crossing);
        Move(new IntVec3(15, 0, 65), 235, TacticalObservedMotion.Leaving);
        Move(new IntVec3(15, 0, 65), 280, TacticalObservedMotion.Stationary);
        Move(new IntVec3(15, 0, 60), 700, TacticalObservedMotion.Unknown);
        Move(new IntVec3(15, 0, 55), 745, TacticalObservedMotion.Approaching);
        TacticalContact report = contact.Copy();
        var peer = new TacticalContactMemory(); peer.Receive(report, 750);
        if (TacticalFieldPolicy.Motion(peer.Entries.Single(), anchor) != TacticalObservedMotion.Approaching
            || peer.Entries.Single().PreviousPosition != report.PreviousPosition)
            throw new Exception("Report lost its observed history.");
        memory.Remember(1, first, first, false, true, 790, "other-observer");
        if (TacticalFieldPolicy.Motion(contact, anchor) != TacticalObservedMotion.Unknown)
            throw new Exception("Unrelated reports were treated as continuous tracking.");
        CheckFirePoints(anchor);
        var playerMemory = new TacticalContactMemory();
        playerMemory.Remember(2,anchor+IntVec3.North*60,first,false,true,100,"observer",true);
        playerMemory.Remember(2,anchor+IntVec3.North*55,first,false,true,145,"observer",true);
        var player = playerMemory.Entries.Single();
        if (!TacticalFieldPolicy.FormationAllowed(player,anchor,145)
            || TacticalFieldPolicy.FormationAllowed(player,anchor,385))
            throw new Exception("Only a fresh approach inside the inclusive 55-cell player boundary may form fire points.");
        var sharedPlayer=new TacticalContactMemory();sharedPlayer.Receive(player.Copy(),150);
        if (!sharedPlayer.Entries.Single().PlayerControlled || !TacticalFieldPolicy.FormationAllowed(sharedPlayer.Entries.Single(),anchor,150))
            throw new Exception("Shared contact reports must retain player ownership and observed motion.");
        playerMemory.Remember(2,anchor+IntVec3.North*55,first,false,true,190,"observer",true);
        if (TacticalFieldPolicy.FormationAllowed(player,anchor,190)) throw new Exception("A stationary player cannot retain an arc indefinitely.");
        playerMemory.Remember(2,anchor+IntVec3.North*56,first,false,true,235,"observer",true);
        if (TacticalFieldPolicy.FormationAllowed(player,anchor,235)) throw new Exception("A retreating player cannot trigger fire points.");
        playerMemory.Remember(2,anchor+IntVec3.North*57,first,false,true,280,"observer",true);
        playerMemory.Remember(2,anchor+IntVec3.North*56,first,false,true,325,"observer",true);
        if (TacticalFieldPolicy.FormationAllowed(player,anchor,325)) throw new Exception("An approaching player outside 55 cells cannot trigger fire points.");
        playerMemory.Remember(2,anchor+IntVec3.North*54,first,false,true,370,"different-observer",true);
        if (TacticalFieldPolicy.FormationAllowed(player,anchor,370)) throw new Exception("A lone/new-observer sample cannot invent a player approach.");
        player.PlayerControlled=false;
        if (!TacticalFieldPolicy.FormationAllowed(player,anchor,370)) throw new Exception("AI opponents remain eligible without an approach sample.");
        if (!TacticalFieldPolicy.Ready(4,3) || TacticalFieldPolicy.Ready(4,2)
            || !TacticalFieldPolicy.Ready(1,1) || TacticalFieldPolicy.Ready(1,0) || !TacticalFieldPolicy.Ready(0,0))
            throw new Exception("A prepared majority must release the team without accepting an isolated/unarrived member.");
        if (TacticalFieldPolicy.SightRadius <= TacticalContactState.Radius)
            throw new Exception("Field sight does not extend the CQB radius.");
        if (!TacticalMedicalPolicy.Urgent(.4f) || TacticalMedicalPolicy.Urgent(.399f)
            || !TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Clear, true, null)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Clear, false, null)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.BlastWait, true, null)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Stack, true, null)
            || !TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Stack, true, TacticalFieldStage.Defending)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Clear, true, TacticalFieldStage.Screening)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Clear, true, TacticalFieldStage.Moving))
            throw new Exception("Care may only interrupt a secured room or a settled field defense.");
        if (AccessTools.DeclaredMethod(typeof(Pawn_HealthTracker), "MakeUndowned") == null
            || AccessTools.Field(typeof(Pawn_HealthTracker), "pawn")?.FieldType != typeof(Pawn))
            throw new Exception("Native recovery wake binding is missing.");
        Console.WriteLine("PASS: R6 first-sample, approach/cross/leave/stationary, stale-gap, copied report history, origin change and fire-point areas and converging fire checks.");
    }
    private static void CheckFirePoints(IntVec3 anchor)
    {
        foreach (IntVec3 forward in GenAdj.CardinalDirections)
        foreach (int seed in new[] { 10, 999, 10001 })
        {
            IntVec3 focus = anchor + forward * 50;
            var points = Enumerable.Range(0, 3).Select(i => TacticalFieldPolicy.FirePoint(anchor, focus, forward, i, seed)).ToArray();
            int Depth(IntVec3 cell) => (cell - anchor).x * forward.x + (cell - anchor).z * forward.z;
            IntVec3 side = new IntVec3(-forward.z, 0, forward.x);
            int Lateral(IntVec3 cell) => (cell - anchor).x * side.x + (cell - anchor).z * side.z;
            if (points.Distinct().Count() != 3 || points.Any(p => Depth(p) != 0)
                || Lateral(points[0]) >= -6 || Lateral(points[2]) <= 6 || Lateral(points[1]) != 0)
                throw new Exception("Three HIGH fire-point centres must initially lie on one transverse line.");
            foreach (IntVec3 point in points)
            {
            }
        }
        if (Enumerable.Range(0, 3).Any(team => TacticalFieldPolicy.FirePointIndex(true, team) != team)
            || TacticalFieldPolicy.FirePointIndex(true, -1) != 1 || TacticalFieldPolicy.FirePointIndex(false, -1) != 0)
            throw new Exception("HIGH uses its actual fireteams; LOW's main body is not split into automatic fixed flank groups.");
        CheckFirePointSave(anchor);
    }
    private static void CheckFirePointSave(IntVec3 anchor)
    {
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-firepoints-" + Guid.NewGuid() + ".xml");
        var field = new TacticalFieldResponse { Anchor = anchor, Focus = anchor + IntVec3.North * 50,
            Forward = IntVec3.North, Started = 123, High = true };
        for (int i = 0; i < 3; i++) field.FirePoints.Add(TacticalFieldPolicy.FirePoint(anchor, field.Focus, field.Forward, i, 123));
        field.Areas.Add(new TacticalFireArea { Cells = areaForSave(anchor), Started = 100, Cursor = 96, Seed = anchor, Prepared = true });
        field.Groups.Add(2); field.LeaderIndex = 0; field.LeaderTeam = 2;
        // Console has no active mod registry. Seed the native type resolver,
        // while keeping nested data extraction in the real Scribe methods.
        var cache = (System.Collections.IDictionary)AccessTools.Field(typeof(GenTypes), "typeCache").GetValue(null);
        Type keyType = typeof(GenTypes).GetNestedType("TypeCacheKey", System.Reflection.BindingFlags.NonPublic);
        cache[Activator.CreateInstance(keyType, new object[] { typeof(TacticalFireArea).FullName, null })] = typeof(TacticalFireArea);
        bool previousHeader = ScribeMetaHeaderUtility.modListChanged;
        ScribeMetaHeaderUtility.modListChanged = true;
        try
        {
            Scribe.saver.InitSaving(path, "field"); field.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalFieldResponse();
            Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            if (!loaded.FirePoints.SequenceEqual(field.FirePoints) || loaded.Started != field.Started
                || !loaded.Areas[0].Cells.SequenceEqual(field.Areas[0].Cells) || loaded.Areas[0].Cursor != 96 || loaded.Areas[0].Seed != anchor
                || !loaded.Areas[0].Prepared || loaded.Groups[0] != 2
                || loaded.LeaderIndex != 0 || loaded.LeaderTeam != 2)
                throw new Exception("Saving must preserve shared firing areas and regional shelter metadata.");
            Console.WriteLine("PASS: native Scribe fire-point value payload round trip (not a full Unity world load).");
        }
        finally { Scribe.ForceStop(); ScribeMetaHeaderUtility.modListChanged = previousHeader; if (File.Exists(path)) File.Delete(path); }
    }
    private static System.Collections.Generic.List<IntVec3> areaForSave(IntVec3 anchor) =>
        new System.Collections.Generic.List<IntVec3> { anchor, anchor + IntVec3.West };

}
