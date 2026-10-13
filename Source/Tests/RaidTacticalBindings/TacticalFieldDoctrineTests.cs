using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;

internal static class TacticalFieldDoctrineTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        var anchor = new IntVec3(80, 0, 80);
        foreach (var forward in GenAdj.CardinalDirections)
        {
            var points = Enumerable.Range(0, 3).Select(i => TacticalFieldDoctrine.LinePoint(anchor, forward, i, 20)).ToArray();
            int Depth(IntVec3 cell) => (cell - anchor).x * forward.x + (cell - anchor).z * forward.z;
            Check(points.All(p => Depth(p) == 0) && points[1] == anchor && points.Distinct().Count() == 3,
                "A HIGH squad starts with three fire areas on a transverse line in all four orientations.");
            foreach (int seed in new[] { 0, 1, 2 })
            {
                var left = TacticalFieldDoctrine.LinePoint(anchor, forward, 0, seed);
                var right = TacticalFieldDoctrine.LinePoint(anchor, forward, 2, seed);
                int oldSpacing = 7 + seed;
                Check(left.DistanceToSquared(anchor) >= 4 * oldSpacing * oldSpacing
                    && left.DistanceToSquared(anchor) <= 9 * oldSpacing * oldSpacing
                    && left - anchor == anchor - right,
                    "HIGH's lateral spacing is two to three times the previous distance and stays symmetric.");
            }
            foreach (int side in new[] { -1, 1 })
                Check(TacticalFieldDoctrine.Within(anchor, TacticalFieldDoctrine.FlankPoint(anchor, forward, side, false), TacticalFieldDoctrine.LowRadius)
                    && TacticalFieldDoctrine.Within(anchor, TacticalFieldDoctrine.FlankPoint(anchor, forward, side, true), TacticalFieldDoctrine.LowRadius),
                    "Both LOW flank legs stay inside the shared leash.");
        }
        Check(TacticalFieldDoctrine.FlankSize(5) == 0 && TacticalFieldDoctrine.FlankSize(6) == 3
            && TacticalFieldDoctrine.FlankSize(12) == 4 && TacticalFieldDoctrine.FlankSize(40) == 4,
            "A temporary LOW automatic group contains three or four and leaves a viable main body.");
        Check(TacticalFieldDoctrine.FlankOpportunity(900, 900, false, false, 12, true, 3)
            && !TacticalFieldDoctrine.FlankOpportunity(899, 900, false, false, 12, true, 3)
            && !TacticalFieldDoctrine.FlankOpportunity(900, 900, false, false, 12, false, 3)
            && !TacticalFieldDoctrine.FlankOpportunity(900, 900, true, false, 12, true, 3)
            && !TacticalFieldDoctrine.FlankOpportunity(900, 900, false, true, 12, true, 3)
            && !TacticalFieldDoctrine.FlankOpportunity(900, 900, false, false, 12, true, 4),
            "Flanking needs an automatic weapon, strength, timing/opportunity and no defensive/support hold.");
        Check(TacticalFieldDoctrine.Within(anchor, anchor + IntVec3.North * 24, 24)
            && !TacticalFieldDoctrine.Within(anchor, anchor + IntVec3.North * 25, 24), "LOW's shared radius has an inclusive boundary.");
        var reloadDef = (JobDef)RuntimeHelpers.GetUninitializedObject(typeof(JobDef)); reloadDef.defName = "HD_ReloadM79FromInventory";
        Check(TacticalFieldDoctrine.EquipmentWork(new Job { def = reloadDef }) && !TacticalFieldDoctrine.EquipmentWork(null),
            "Field repositioning must not repeatedly cancel a live weapon reload.");
        Check(!TacticalFieldDoctrine.RetreatDue(599, 0, 600, 590, 0, false)
            && TacticalFieldDoctrine.RetreatDue(600, 0, 600, 590, 0, false)
            && !TacticalFieldDoctrine.RetreatDue(600, 0, 600, 100, 0, false)
            && !TacticalFieldDoctrine.RetreatDue(600, 0, 600, 601, 0, false)
            && !TacticalFieldDoctrine.RetreatDue(600, 0, 600, 590, 6, false),
            "Delayed, actual recent fighting advances one finite retreat step; idle/future sightings cannot withdraw a squad.");
        Check(TacticalFieldDoctrine.RetreatDepth(6, 1, 3) == 12 && TacticalFieldDoctrine.RetreatDepth(6, 0, 3) == 6,
            "Solo HIGH's centre withdraws twice as much as its wings.");
        Check(TacticalFieldDoctrine.RetreatDepth(12, 4, 9) == 24 && TacticalFieldDoctrine.RetreatDepth(12, 0, 9) == 6
            && TacticalFieldDoctrine.RetreatDepth(12, 8, 9) == 6, "Multiple squads amplify centre withdrawal and keep symmetric shallower flanks.");
        CheckHighExecution(Check, anchor);
        CheckAngledLine(Check, anchor);
        CheckLowDefense(Check, anchor);
        CheckRadio(Check, anchor);
        CheckNativeBindings(Check);
        CheckSave(Check, anchor);
        Console.WriteLine("PASS: " + checks + " LOW bounded vanilla combat/flank, HIGH line/withdrawal, radio membership, native bindings and state checks.");
    }
    private static TacticalFieldFormation Solo(string id, IntVec3 anchor, int tick) => new TacticalFieldFormation
        { Coordinator = id, Origin = anchor, Forward = IntVec3.North, Started = 0,
            Members = new List<string> { id }, Seen = new Dictionary<string, int> { [id] = tick } };
    private static void CheckAngledLine(Action<bool, string> check, IntVec3 anchor)
    {
        foreach (var direction in new[] { new IntVec3(40, 0, 40), new IntVec3(60, 0, 25), new IntVec3(-25, 0, 60),
            new IntVec3(-60, 0, -25), new IntVec3(25, 0, -60) })
        {
            var forward = TacticalFieldDoctrine.Facing(anchor, anchor + direction);
            double length = Math.Sqrt(direction.LengthHorizontalSquared);
            double Depth(IntVec3 cell) => ((cell.x - anchor.x) * direction.x + (cell.z - anchor.z) * direction.z) / length;
            var left = TacticalFieldDoctrine.LinePoint(anchor, forward, 0, 0);
            var right = TacticalFieldDoctrine.LinePoint(anchor, forward, 2, 0);
            check(Math.Abs(Depth(left)) <= .71 && left - anchor == anchor - right
                && left.DistanceToSquared(anchor) >= 17 * 17 && left.DistanceToSquared(anchor) <= 19 * 19,
                "Angled enemy approaches produce perpendicular, symmetric fire points with only tile-rounding error.");
            var retreat = TacticalFieldDoctrine.Offset(forward, 0, -12);
            check(Math.Abs(Depth(anchor + retreat) + 12) <= .71 && retreat.LengthHorizontalSquared <= 13 * 13,
                "Retreat remains twelve cells along the enemy axis, independent of the direction vector length.");
            var formation = new TacticalFieldFormation { Origin = anchor, Forward = forward, Members = new List<string> { "A", "B" } };
            check(formation.Members.SelectMany(id => Enumerable.Range(0, 3).Select(p => formation.Point(id, p)))
                .All(p => Math.Abs(Depth(p)) <= .71), "Radio formations use the same perpendicular enemy axis.");
            var next = anchor + new IntVec3(20, 0, 20);
            var safe = TacticalFieldDoctrine.WithoutAdvance(anchor, next, forward);
            check(Depth(safe) <= 0 && (Depth(next) > 0 || safe == next),
                "Membership changes cannot advance toward an angled enemy or overcorrect by vector magnitude.");
        }
    }
    private static void CheckLowDefense(Action<bool, string> check, IntVec3 anchor)
    {
        foreach (var forward in GenAdj.CardinalDirections)
        {
            var side = new IntVec3(-forward.z, 0, forward.x);
            check(TacticalFieldDoctrine.WithinEllipse(anchor, anchor + side * 25, forward, TacticalFieldDoctrine.LowMainRadius, TacticalFieldDoctrine.LowMainDepth)
                && TacticalFieldDoctrine.WithinEllipse(anchor, anchor + forward * 12, forward, TacticalFieldDoctrine.LowMainRadius, TacticalFieldDoctrine.LowMainDepth)
                && !TacticalFieldDoctrine.WithinEllipse(anchor, anchor + forward * 13, forward, TacticalFieldDoctrine.LowMainRadius, TacticalFieldDoctrine.LowMainDepth)
                && !TacticalFieldDoctrine.WithinEllipse(anchor, anchor + side * 25 + forward, forward, TacticalFieldDoctrine.LowMainRadius, TacticalFieldDoctrine.LowMainDepth)
                && TacticalFieldDoctrine.WithinEllipse(anchor, anchor + side * 25, forward, TacticalFieldDoctrine.LowRadius, TacticalFieldDoctrine.LowDepth),
                "LOW's main zone is wide across the enemy direction and shallow along it, not a circle.");
        }
        var angle = new IntVec3(60, 0, 25);
        check(TacticalFieldDoctrine.WithinEllipse(anchor, anchor + TacticalFieldDoctrine.Offset(angle, 22, 0), angle,
                TacticalFieldDoctrine.LowMainRadius, TacticalFieldDoctrine.LowMainDepth)
            && !TacticalFieldDoctrine.WithinEllipse(anchor, anchor + TacticalFieldDoctrine.Offset(angle, 0, 16), angle,
                TacticalFieldDoctrine.LowMainRadius, TacticalFieldDoctrine.LowMainDepth),
            "The ellipse follows the actual angled enemy direction.");
        check(TacticalFieldDoctrine.WithinEllipse(anchor, anchor, IntVec3.Zero, 14, 8), "A zero facing does not divide by zero.");
        check(TacticalLowDefenseSearch.CoverCell(anchor, 0) == anchor + new IntVec3(-25, 0, -25)
            && TacticalLowDefenseSearch.CoverCell(anchor, TacticalLowDefenseSearch.CoverGridCount - 1) == anchor + new IntVec3(25, 0, 25),
            "Shared cover sampling spans the enlarged ellipse instead of retaining the old fourteen-cell grid.");
        foreach (int side in new[] { -1, 1 })
            check(TacticalFieldDoctrine.WithinEllipse(anchor, TacticalFieldDoctrine.FlankPoint(anchor, angle, side, true), angle,
                TacticalFieldDoctrine.LowRadius, TacticalFieldDoctrine.LowDepth),
                "LOW's existing temporary flank destination remains inside the rotated shared ellipse.");
        var search = new TacticalLowDefenseSearch { Origin = anchor, Forward = IntVec3.North, Needed = 6, CurrentCover = 0 };
        var choices = new[] { anchor + IntVec3.South * 20, anchor + IntVec3.East * 20,
            anchor + IntVec3.West * 20, anchor + IntVec3.North * 20 };
        int calls = 0, worst = 0, slices = 0;
        while (!search.Scanned && slices++ < 60)
        {
            int before = calls;
            search.Advance(cell => { calls++; if (cell.DistanceToSquared(anchor) > 2500) throw new Exception("Defense probe exceeded fifty cells.");
                return choices.Contains(cell) ? 6 : 0; });
            worst = Math.Max(worst, calls - before);
        }
        check(search.Scanned && worst <= 8 && calls > 250 && calls < 350, "A fifty-cell search completes through bounded eight-candidate slices.");
        check(search.Candidates.Count == 3 && !search.Candidates.Contains(choices[3]),
            "Equal-cover rear/lateral points beat advancing toward the threat; only three shared finalists remain.");
        var empty = new TacticalLowDefenseSearch { Origin = anchor, Forward = IntVec3.North, Needed = 6, CurrentCover = 3 };
        for (int slice = 0; slice < 60 && !empty.Scanned; slice++) empty.Advance(_ => 3);
        check(empty.Scanned && empty.Candidates.Count == 0, "Equally poor cover cannot cause endless relocation between points.");
        check(TacticalLowDefenseSearch.RequiredCover(12) == 6 && TacticalLowDefenseSearch.RequiredCover(3) == 2
            && TacticalLowDefenseSearch.RequiredCover(40) == 6, "Cover adequacy scales with the squad but stays bounded for large raids.");
    }
    private static void CheckHighExecution(Action<bool, string> check, IntVec3 anchor)
    {
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        var owner = new MapComponent_TacticalCommands(map);
        var field = new TacticalFieldResponse { High = true, Stage = TacticalFieldStage.Defending, Anchor = anchor,
            Forward = IntVec3.North, Started = 0, NextMove = 600, LastEngaged = 590, AppliedStep = 0 };
        for (int i = 0; i < 3; i++) { var p = TacticalFieldDoctrine.LinePoint(anchor, field.Forward, i, 0);
            field.FirePoints.Add(p); field.RetreatBases.Add(p); field.Areas.Add(new TacticalFireArea()); }
        var command = new TacticalSquadCommand { Id = "B", FieldResponse = field };
        var method = AccessTools.Method(owner.GetType(), "AdvanceHighFieldRetreat");
        method.Invoke(owner, new object[] { command, field, 599 });
        check(field.RetreatStep == 0 && !field.BoundPending, "The real HIGH controller must preserve its initial line before the fighting delay.");
        method.Invoke(owner, new object[] { command, field, 600 });
        check(field.RetreatStep == 1 && field.FirePoints[1] == anchor + IntVec3.South * 2
            && field.FirePoints[0] == field.RetreatBases[0] + IntVec3.South
            && field.MovingGroups == 7 && field.BoundPending, "Actual retreat changes three shared areas, with a deeper centre and incremental assignment.");
        method.Invoke(owner, new object[] { command, field, 1200 });
        check(field.RetreatStep == 1, "Unfinished assignment cannot start a second calculation/movement wave.");
        field.BoundPending = false; field.Stage = TacticalFieldStage.Defending; field.LastEngaged = 1190;
        var group = Solo("A", anchor, 1190); group.Members.Add("B"); group.Seen["B"] = 1190;
        field.Formation = group;
        method.Invoke(owner, new object[] { command, field, 1200 });
        check(group.Step == 0, "A follower squad must await a delivered coordinator step rather than independently advancing the shared clock.");
        group.Step = 2; field.BoundPending = false;
        method.Invoke(owner, new object[] { command, field, 1201 });
        check(field.AppliedStep == 2 && field.AppliedGroup == "A|B", "A delivered group step reaches the actual HIGH controller.");
    }
    private static void CheckRadio(Action<bool, string> check, IntVec3 anchor)
    {
        var a = Solo("A", anchor, 100); var b = Solo("B", anchor + IntVec3.East * 8, 100); var c = Solo("C", anchor, 100);
        check(a.Members.Count == 1 && b.Members.Count == 1, "Uncommunicated HIGH squads must remain independent.");
        check(b.Receive(a.Copy(), "B", "A", 100, 120) && b.Coordinator == "A" && b.Members.SequenceEqual(new[] { "A", "B" }),
            "Only a received radio snapshot establishes shared origin and membership.");
        b.Seen["B"] = 120;
        check(a.Receive(b.Copy(), "A", "B", 120, 140) && a.Members.Count == 2, "Return delivery makes the coordinator aware of the follower.");
        a.Step = 4; a.Seen["A"] = 140;
        check(c.Receive(a.Copy(), "C", "A", 140, 160) && c.Members.Count == 3 && c.Coordinator == "A" && c.Step == 4,
            "A third communicated squad joins the same finite layout and clock.");
        var all = new TacticalFieldFormation { Coordinator = "A", Origin = anchor, Forward = IntVec3.North, Step = 0,
            Members = new List<string> { "A", "B", "C" } };
        var line = all.Members.SelectMany(id => Enumerable.Range(0, 3).Select(p => all.Point(id, p))).ToArray();
        check(line.Distinct().Count() == 9 && line.All(p => p.z == anchor.z), "Three squads start as nine distinct aligned fire points.");
        foreach (int count in new[] { 2, 3, 4 })
        foreach (var forward in GenAdj.CardinalDirections)
        {
            var wide = new TacticalFieldFormation { Origin = anchor, Forward = forward,
                Members = Enumerable.Range(0, count).Select(i => i.ToString()).ToList() };
            var centres = wide.Members.SelectMany(id => Enumerable.Range(0, 3).Select(p => wide.Point(id, p))).ToArray();
            check(centres.Zip(centres.Skip(1), (a, b) => a.DistanceToSquared(b)).All(d => d == 225),
                "Radio formations retain a uniform 15-cell gap for odd and even squad counts in every orientation.");
        }
        all.Step = 12;
        check(all.Point("B", 1).z == anchor.z - 24 && all.Point("A", 0).z == anchor.z - 6
            && all.Point("C", 2).z == anchor.z - 6, "The global central fireteam withdraws much deeper than the outside teams.");
        check(!b.Receive(a.Copy(), "B", "A", 50, 160), "Older packets cannot roll back newer formation delivery.");
        check(!b.Receive(a.Copy(), "B", "A", 140, 740), "Expired radio packets cannot create a formation.");
        var invalid = a.Copy(); invalid.LastEngaged = 1000;
        check(!b.Receive(invalid, "B", "A", 140, 160), "Future engagement evidence is not a valid withdrawal order.");
        check(c.Expire("C", 740) && !c.Members.Contains("A"), "Dead/disconnected members expire from their original observation dates, without immortal relays.");
        check(!RaidTacticalRadioUtility.SignalAvailable(null), "A missing map cannot authorize a radio channel.");
    }
    private static void CheckNativeBindings(Action<bool, string> check)
    {
        check(typeof(JobGiver_AIFightEnemies).IsAssignableFrom(typeof(TacticalLowFieldFight)), "LOW must actually inherit native fighting/cover selection.");
        RuntimeHelpers.RunClassConstructor(typeof(Patch_NewTactical_LowFieldJobs).TypeHandle);
        check(AccessTools.Field(typeof(Patch_NewTactical_LowFieldJobs), "ConstantJobs").GetValue(null) != null,
            "The installed native constant-tree delegate must bind successfully to retain safety priority.");
        foreach (Type patch in new[] { typeof(Patch_NewTactical_LowFieldJobs), typeof(Patch_NewTactical_AdoptLowFieldJob) })
        {
            HarmonyMethod info = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patch));
            var target = AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
            check(target != null, "Installed game job hook missing.");
            foreach (var hook in patch.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Prefix" || m.Name == "Postfix"))
                foreach (var p in hook.GetParameters())
                {
                    Type actual = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                    Type expected = p.Name == "__instance" ? target.DeclaringType : p.Name == "__result" ? target.ReturnType
                        : p.Name.StartsWith("___") ? AccessTools.Field(target.DeclaringType, p.Name.Substring(3))?.FieldType
                        : target.GetParameters().FirstOrDefault(x => x.Name == p.Name)?.ParameterType;
                    if (expected?.IsByRef == true) expected = expected.GetElementType();
                    check(expected != null && actual == expected, "Native field hook type mismatch: " + p.Name);
                }
        }
        check(AccessTools.Method(typeof(GameConditionManager), "ElectricityDisabled", new[] { typeof(Map) }) != null,
            "Solar-flare radio checks must bind the installed game condition API.");
        var owner = new MapComponent_TacticalCommands(null);
        var pawn = new Pawn { jobs = (Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker)) };
        var candidate = new Job();
        var pending = new Dictionary<Pawn, Job>(ReferenceEqualityComparer.Instance);
        AccessTools.Field(owner.GetType(), "pendingLowJobs").SetValue(owner, pending);
        pending[pawn] = candidate;
        AccessTools.Method(owner.GetType(), "AdoptLowNative").Invoke(owner, new object[] { pawn, candidate });
        check(pending.Count == 0 && pawn.CurJob == null,
            "An unsuccessful native start must drop the pending candidate without adopting another driver's job.");
    }
    private static void CheckSave(Action<bool, string> check, IntVec3 anchor)
    {
        var cache = (System.Collections.IDictionary)AccessTools.Field(typeof(GenTypes), "typeCache").GetValue(null);
        var key = typeof(GenTypes).GetNestedType("TypeCacheKey", BindingFlags.NonPublic);
        foreach (Type t in new[] { typeof(TacticalFieldFormation), typeof(TacticalFireArea), typeof(TacticalLowDefenseSearch) })
            cache[Activator.CreateInstance(key, new object[] { t.FullName, null })] = t;
        var field = new TacticalFieldResponse { High = true, LowFlank = true, LowFlankAttack = true, NextFlank = 900,
            Formation = Solo("A", anchor, 100), RetreatStep = 3, AppliedStep = 3, MovingGroups = 7, LastEngaged = 99 };
        field.Forward = field.Formation.Forward = new IntVec3(60, 0, 25);
        field.LowCoverCells.Add(anchor); field.LowCoverReady = true; field.LowCoverCheckAt = 1800;
        field.LowDefenseSearch = new TacticalLowDefenseSearch { Origin = anchor, Forward = field.Forward, Cursor = 104, NextSlice = 110 };
        field.LowDefenseSearch.Candidates.Add(anchor + IntVec3.South * 20); field.LowDefenseSearch.Scores.Add(55);
        field.RetreatBases.Add(anchor);
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-field-doctrine-" + Guid.NewGuid() + ".xml");
        bool previous = ScribeMetaHeaderUtility.modListChanged; ScribeMetaHeaderUtility.modListChanged = true;
        try
        {
            Scribe.saver.InitSaving(path, "field"); field.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalFieldResponse(); Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            check(loaded.Formation.Coordinator == "A" && loaded.Formation.Seen["A"] == 100 && loaded.RetreatBases[0] == anchor
                && loaded.RetreatStep == 3 && loaded.MovingGroups == 7 && loaded.LowFlankAttack && loaded.NextFlank == 900
                && loaded.Forward == field.Forward && loaded.Formation.Forward == field.Forward,
                "Native Scribe must retain group reports, retreat progress and the active LOW temporary party.");
            check(loaded.LowCoverReady && loaded.LowCoverCells.Single() == anchor && loaded.LowCoverCheckAt == 1800
                && loaded.LowDefenseSearch.Cursor == 104 && loaded.LowDefenseSearch.NextSlice == 110
                && loaded.LowDefenseSearch.Candidates.Single() == anchor + IntVec3.South * 20 && loaded.LowDefenseSearch.Scores.Single() == 55,
                "Incremental defense searches and shared shelter candidates resume from saved value state.");
        }
        finally { Scribe.ForceStop(); ScribeMetaHeaderUtility.modListChanged = previous; if (File.Exists(path)) File.Delete(path); }
    }
}
