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
        if (TacticalFieldPolicy.SightRadius <= TacticalContactState.Radius)
            throw new Exception("Field sight does not extend the CQB radius.");
        if (TacticalFieldPolicy.NextMover(-1,7,true)!=0 || TacticalFieldPolicy.NextMover(0,7,true)!=1
            || TacticalFieldPolicy.NextMover(2,7,true)!=0 || TacticalFieldPolicy.NextMover(0,5,true)!=2
            || TacticalFieldPolicy.NextMover(2,4,true)!=2 || TacticalFieldPolicy.NextMover(-1,0,false)!=-3
            || TacticalFieldPolicy.NextMover(-1,0,true)!=-2 || TacticalFieldPolicy.NextMover(-2,0,true)!=-3)
            throw new Exception("Absent child teams or reserves must not create a frozen/empty movement wave.");
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
            if (points.Distinct().Count() != 3 || Depth(points[1]) >= -3 || Depth(points[0]) < 4 || Depth(points[2]) < 4
                || Lateral(points[0]) >= -6 || Lateral(points[2]) <= 6 || Lateral(points[1]) != 0)
                throw new Exception("Three fire-point centers must have advanced lateral wings and a recessed base.");
            foreach (IntVec3 point in points)
            {
                var scattered = Enumerable.Range(1, 20).Select(id => TacticalFieldPolicy.Scatter(point, id, seed)).ToArray();
                if (scattered.Any(cell => Math.Abs(cell.x - point.x) > 2 || Math.Abs(cell.z - point.z) > 2)
                    || scattered.Select(Depth).Distinct().Count() < 2 || scattered.Select(Lateral).Distinct().Count() < 2)
                    throw new Exception("Posts must scatter through an area, not form an exact depth row or curve.");
                IntVec3 moved = TacticalFieldPolicy.AdvanceFirePoint(point, forward, focus);
                if (moved != point + forward * 5) throw new Exception("A moving group advances one shared area.");
            }
            IntVec3 near = focus - forward * 4;
            if (TacticalFieldPolicy.AdvanceFirePoint(near, forward, focus) != near + forward
                || TacticalFieldPolicy.AdvanceFirePoint(focus - forward * 2, forward, focus) != focus - forward * 2)
                throw new Exception("A firing area must not advance through the observed enemy.");
        }
        if (Enumerable.Range(0, 3).Any(team => TacticalFieldPolicy.FirePointIndex(true, team, false, 10) != team)
            || TacticalFieldPolicy.FirePointIndex(true, -1, false, 10) != 1
            || TacticalFieldPolicy.FirePointIndex(false, -1, true, 0) != 1
            || TacticalFieldPolicy.FirePointIndex(false, -1, false, 4) != 0
            || TacticalFieldPolicy.FirePointIndex(false, -1, false, 5) != 2)
            throw new Exception("High uses physical teams; LOW places its core between two flank groups.");
        if (TacticalFieldPolicy.FirePostScore(true, 0, 8, 100, 1) <= TacticalFieldPolicy.FirePostScore(false, 1, 0, 0, 1))
            throw new Exception("A covered firing position must not lose to a blind holding position.");
        if (TacticalFieldPolicy.Scatter(anchor, 10, 100) == TacticalFieldPolicy.Scatter(anchor, 10, 200))
            throw new Exception("Independent encounters must allow different local positions.");
        CheckFirePointSave(anchor);
    }
    private static void CheckFirePointSave(IntVec3 anchor)
    {
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-firepoints-" + Guid.NewGuid() + ".xml");
        var field = new TacticalFieldResponse { Anchor = anchor, Focus = anchor + IntVec3.North * 50,
            Forward = IntVec3.North, Started = 123, High = true };
        for (int i = 0; i < 3; i++) field.FirePoints.Add(TacticalFieldPolicy.FirePoint(anchor, field.Focus, field.Forward, i, 123));
        try
        {
            Scribe.saver.InitSaving(path, "field"); field.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalFieldResponse();
            Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            if (!loaded.FirePoints.SequenceEqual(field.FirePoints) || loaded.Started != field.Started
                || TacticalFieldPolicy.Scatter(loaded.FirePoints[0], 10, loaded.Started)
                    != TacticalFieldPolicy.Scatter(field.FirePoints[0], 10, field.Started))
                throw new Exception("Saving must preserve shared firing areas and stable post scatter.");
            Console.WriteLine("PASS: native Scribe fire-point value payload round trip (not a full Unity world load).");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
    }

}
