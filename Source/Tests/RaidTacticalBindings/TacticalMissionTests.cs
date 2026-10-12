using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalMissionTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var opening = new IntVec3(0, 0, 4); var inside = opening + IntVec3.East;
        bool Floor(IntVec3 c) => c.x >= 1 && c.x <= 8 && c.z >= 1 && c.z <= 8;
        TacticalStructureClass Finish(TacticalStructureProbe probe, Func<IntVec3, bool> floor, Func<IntVec3, bool> evidence)
        {
            for (int tick = 0; tick < 30; tick++)
            {
                int allowance = TacticalStructureProbe.Slice;
                var result = probe.Step(floor, evidence, ref allowance);
                Check(allowance >= 0, "Structure work must respect the shared slice.");
                if (result != TacticalStructureClass.Pending) return result;
            }
            throw new Exception("Structure classification failed to terminate under a cell cap.");
        }
        var neutral = new TacticalStructureProbe(inside, opening);
        int left = 1;
        Check(neutral.Step(Floor, _ => false, ref left) == TacticalStructureClass.Pending && left == 0,
            "A neutral ruin must yield rather than classify a partial scan as cleared.");
        Check(Finish(neutral, Floor, _ => false) == TacticalStructureClass.Incidental,
            "An unrelated enclosed neutral ruin is not a mission target.");
        var goal = new IntVec3(7, 0, 7);
        Check(Finish(new TacticalStructureProbe(inside, opening), Floor, c => c == goal) == TacticalStructureClass.Target,
            "An explicitly supplied goal inside a reused neutral ruin qualifies that local structure.");
        Check(Finish(new TacticalStructureProbe(inside, opening), Floor, c => c == new IntVec3(12, 0, 4)) == TacticalStructureClass.Incidental,
            "A nearby disconnected owned structure must not qualify a ruin by distance.");
        Check(Finish(new TacticalStructureProbe(new IntVec3(30, 0, 30), opening), _ => false, _ => true) == TacticalStructureClass.Incidental,
            "Outdoor space must not flood to a remote settlement anchor.");
        Check(Finish(new TacticalStructureProbe(inside, opening), _ => true, _ => false) == TacticalStructureClass.Incidental,
            "An unbounded floor predicate must still terminate at the local cell cap.");
        CheckProbeResume(Check);
        var command = new TacticalSquadCommand { GoalSecured = true, Goal = goal };
        command.Frontiers.Add(new TacticalRoomFrontier { Opening = opening, Inward = IntVec3.East, Excluded = true });
        Check(!((IntVec3)AccessTools.Method(typeof(MapComponent_TacticalCommands), "PendingGoal").Invoke(null, new object[] { command })).IsValid,
            "A secured bed and excluded frontier must not restart the original mission.");
        command.Mission.NextGoal = new IntVec3(40, 0, 40);
        Check((IntVec3)AccessTools.Method(typeof(MapComponent_TacticalCommands), "PendingGoal").Invoke(null, new object[] { command }) == command.Mission.NextGoal,
            "An independently found next building must override the original already secured bed destination.");
        var facade = new TacticalFacadeSurvey(new IntVec3(50, 0, 50));
        int samples = 0;
        bool local = true;
        while (facade.Next(out IntVec3 cell))
        {
            local &= Math.Abs(cell.x - 50) <= 24 && Math.Abs(cell.z - 50) <= 24;
            if (++samples > 384) throw new Exception("Facade survey exceeded its fixed ray cap.");
        }
        Check(local, "Facade sampling must stay local rather than traverse the map.");
        Check(samples == 384 && facade.Finished, "Sixteen finite sight rays must terminate without a map scan.");
        var survey = new TacticalRoomScan(inside, opening, Floor,
            c => c.x == 9 && c.z >= 1 && c.z <= 8, null, c => c.x == 10 && c.z >= 1 && c.z <= 8);
        while (!survey.Step()) { }
        Check(survey.Frontiers.Exists(f => f.Outdoors && f.Inside.x == 10)
            && !survey.Cells.Contains(new IntVec3(10, 0, 4)),
            "An outdoor exit must be a known frontier, never an infinite outdoor room or already secured yard.");
        var saved = new TacticalAssaultMission { Stage = TacticalMissionStage.Blocked, NextGoal = goal,
            EmptySurveys = 1, SearchAt = 700, FailedPlans = 3, ForgetFailuresAt = 1300 };
        saved.Traversed.Add(opening); saved.FailedDestinations.Add(inside);
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-Mission-" + Guid.NewGuid() + ".xml");
        try
        {
            Scribe.saver.InitSaving(path, "mission"); saved.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalAssaultMission();
            Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            Check(loaded.Stage == saved.Stage && loaded.NextGoal == goal && loaded.SearchAt == 700
                && loaded.EmptySurveys == 1 && loaded.FailedPlans == 3 && loaded.ForgetFailuresAt == 1300
                && loaded.Traversed.Contains(opening) && loaded.FailedDestinations.Contains(inside),
                "Native Scribe must retain mission stage, pending destination, failure latency and actual outdoor passages.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine("PASS: " + checks + " bounded structure target, reused ruin, disconnected evidence and excluded-goal checks.");
    }
    private static void CheckProbeResume(Action<bool,string> check)
    {
        var service = new MapComponent_TacticalCommands(null);
        var goal = new IntVec3(7,0,7);
        AccessTools.Field(service.GetType(), "explicitGoal").SetValue(service, goal);
        var command = new TacticalSquadCommand { Owner = service, Goal = goal };
        var prepare = AccessTools.Method(service.GetType(), "PrepareStructureTargets");
        prepare.Invoke(service,new object[]{command,0});
        var regions = (Dictionary<Tuple<IntVec3,IntVec3,int>,TacticalSecuredRegionProbe>)
            AccessTools.Field(command.GetType(), "SecuredRegionProbes").GetValue(command);
        var structures = (Dictionary<IntVec3,TacticalStructureProbe>)
            AccessTools.Field(command.GetType(), "StructureProbes").GetValue(command);
        var opening = new IntVec3(0,0,4);
        var key = Tuple.Create(opening,IntVec3.East,1);
        var region = new TacticalSecuredRegionProbe(opening,IntVec3.East,false);
        var structure = new TacticalStructureProbe(opening+IntVec3.East,opening);
        regions[key] = region; structures[opening] = structure;
        int regionReads = 0, structureReads = 0;
        for (int tick = 1; tick <= 9; tick++)
        {
            service.SharedSecured.Record(new TacticalSecuredReport { Opening = new IntVec3(100+tick,0,100), SeenTick=tick });
            prepare.Invoke(service,new object[]{command,tick*601});
            if (region.Result == TacticalStructureClass.Pending)
            {
                check(regions.TryGetValue(key,out var kept) && ReferenceEquals(kept,region),
                    "Unrelated room reports and expiry must retain the unfinished secured-region queue.");
                int allowance = TacticalSecuredRegionProbe.Slice;
                region.Step(_ => { regionReads++; return true; },_ => false,ref allowance);
            }
            if (structure.Result == TacticalStructureClass.Pending)
            {
                check(structures.TryGetValue(opening,out var kept) && ReferenceEquals(kept,structure),
                    "Structure expiry must retain unfinished target classification despite long scheduling delays.");
                int allowance = TacticalStructureProbe.Slice;
                structure.Step(_ => { structureReads++; return true; },_ => false,ref allowance);
            }
        }
        check(region.Result == TacticalStructureClass.Incidental && regionReads == TacticalSecuredRegionProbe.CellLimit
            && structure.Result == TacticalStructureClass.Incidental && structureReads == TacticalStructureProbe.CellLimit,
            "Delayed slices must finish once within their fixed total cell caps rather than restarting forever.");
        check(!regions.ContainsKey(key) && !structures.ContainsKey(opening), "Finished answers must still expire normally.");
        var target = new TacticalStructureProbe(opening+IntVec3.East,opening);
        structures[opening] = target;
        for (int tick = 10; tick < 18 && target.Result == TacticalStructureClass.Pending; tick++)
        {
            prepare.Invoke(service,new object[]{command,tick*601});
            check(ReferenceEquals(structures[opening],target),"Expiry must also preserve pending positive objective evidence.");
            int allowance = TacticalStructureProbe.Slice;
            target.Step(c => c.x>=1 && c.x<=8 && c.z>=1 && c.z<=8,c => c==goal,ref allowance);
        }
        check(target.Result == TacticalStructureClass.Target,"Delayed classification must still recognize the actual goal in the next room.");
        structures[opening] = new TacticalStructureProbe(opening+IntVec3.East,opening);
        command.Goal = new IntVec3(30,0,30); prepare.Invoke(service,new object[]{command,7000});
        check(structures.Count == 0,"An actual objective change must discard unfinished evidence for the old goal.");
        var oldGame = Current.Game;
        try
        {
            var game = (Game)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Game));
            game.components = new List<GameComponent>{new GameComponent_TacticalCommands(game)}; Current.Game = game;
            command.PlanRetryAt = 200;
            AccessTools.Method(service.GetType(),"AdvanceRoomClear").Invoke(service,
                new object[]{command,new List<TacticalMemberCommand>(),100});
            check(command.Due == 200 && game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.TryPlan(100),
                "A waiting clear command must not consume another squad's plan slice or refresh classification.");
        }
        finally { Current.Game = oldGame; }
    }
}
