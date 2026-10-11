using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;
using Verse.AI;

internal static class TacticalFocusTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        TacticalMeleeThreat Threat(int id, int urgency, bool shield, int tick = 100) => new TacticalMeleeThreat {
            EnemyId = id, Position = new IntVec3(12 + id, 0, 10), Urgency = urgency,
            Shielded = shield, SeenTick = tick, Origin = "source" };
        var anchor = new IntVec3(10, 0, 10);
        Check(TacticalFocusPolicy.FollowOffsets.Count == 29 && TacticalFocusPolicy.FollowOffsets[0] == IntVec3.Zero
            && TacticalFocusPolicy.FollowOffsets.Distinct().Count() == 29
            && TacticalFocusPolicy.FollowOffsets.All(cell => cell.LengthHorizontalSquared <= 9),
            "Moving-target reacquisition must probe only a fixed, unique three-cell neighbourhood, starting at the last visible tile.");
        Check(TacticalFocusPolicy.Urgency(false, 1) == 0, "Passive proximity must not create a melee threat.");
        Check(TacticalFocusPolicy.Urgency(true, 9) == 3 && TacticalFocusPolicy.Urgency(true, 100) == 2
            && TacticalFocusPolicy.Urgency(true, 900) == 1 && TacticalFocusPolicy.Urgency(true, 901) == 0,
            "Intent and contact danger boundaries must agree.");
        var plain = Threat(1, 2, false); var shield = Threat(2, 2, true); var imminent = Threat(3, 3, false);
        var candidates = new[] { plain, shield };
        Check(TacticalFocusPolicy.Select(candidates, -1, 0, anchor, 100) == shield,
            "A shield bearer must win among comparably urgent approaching enemies.");
        Check(TacticalFocusPolicy.Select(candidates, plain.EnemyId, 100, anchor, 120) == plain,
            "The brief hold must prevent equal-urgency target thrashing.");
        Check(TacticalFocusPolicy.Select(candidates, plain.EnemyId, 100, anchor, 200) == shield,
            "Shield priority must apply once the brief hold expires.");
        Check(TacticalFocusPolicy.Select(new[] { shield, imminent }, shield.EnemyId, 100, anchor, 101) == imminent,
            "An imminent unshielded attacker must override a distant shield and the hold.");
        shield.Shielded = false;
        Check(TacticalFocusPolicy.Select(new[] { shield }, shield.EnemyId, 100, anchor, 150) == shield,
            "Breaking the shield alone must not end an ongoing melee approach.");
        Check(TacticalFocusPolicy.Select(candidates, -1, 0, anchor, 280) == null,
            "LOS loss must expire the focus without accessing a hidden Pawn.");
        var sender = new TacticalMeleeMemory(); var receiver = new TacticalMeleeMemory();
        Check(sender.Receive(plain, 100) && receiver.Receive(sender.Entries.Single(), 120), "Fresh melee reports must transfer.");
        plain.Urgency = 0;
        Check(sender.Entries.Single().Urgency == 2 && receiver.Entries.Single().Urgency == 2,
            "Report and recipient storage must own detached value snapshots.");
        var retreat = Threat(1, 0, false, 130);
        Check(receiver.Receive(retreat, 130) && !receiver.Dangerous(130), "A visible retreat must retract danger.");
        Check(!receiver.Receive(Threat(1, 3, true, 100), 150) && !receiver.Dangerous(150),
            "An older queued report must not resurrect a visibly resolved attacker.");
        Check(!receiver.Receive(Threat(5, 3, true, 200), 150), "Future-dated reports must be rejected.");
        Check(!receiver.Receive(Threat(5, 3, true, 1), 181), "Expired reports must be rejected.");
        for (int id = 2; id <= 8; id++) sender.Receive(Threat(id, 1, false, 100 + id), 110);
        Check(sender.Entries.Count == TacticalFocusPolicy.CandidateLimit && sender.Find(1) == null,
            "Threat memory must retain a fixed cap and evict old identities.");
        sender.Expire(288); Check(sender.Entries.Count == 0, "No snapshot may survive its freshness window.");
        var ranged = new TacticalContactMemory();
        Check(ranged.Entries.Count == 0 && receiver.Entries.Count == 1,
            "Melee reports must remain separate from ranged observation eligibility.");
        var focusMemory = new TacticalMeleeMemory(); focusMemory.Receive(Threat(20, 2, true, 300), 300);
        var focus = new TacticalFocusFire { Started = 300 };
        Check(focus.Select(focusMemory, anchor, 300) && focus.TargetId == 20 && focus.Stage == TacticalFocusStage.Focused,
            "A fresh threat must begin shared focused execution.");
        focusMemory.Receive(Threat(20, 0, false, 310), 310);
        focus.Select(focusMemory, anchor, 310);
        Check(focus.Stage == TacticalFocusStage.Focused, "A sample must not bypass the bounded decision cadence.");
        focus.Select(focusMemory, anchor, 330);
        Check(focus.Stage == TacticalFocusStage.Returning && focus.TargetId == -1 && !focus.Target.IsValid && focus.ReleaseCursor == 0,
            "Resolved threats must release control instead of retaining a persistent guard target.");
        focusMemory.Receive(Threat(21, 3, false, 360), 360);
        Check(focus.Select(focusMemory, anchor, 360) && focus.TargetId == 21 && focus.Stage == TacticalFocusStage.Focused,
            "A new urgent threat must reacquire control during the handoff.");
        focus.ReleaseCursor = 99; focus.AdmissionCursor = -5;
        focus.Restore(focusMemory, 370, 13);
        Check(focus.Stage == TacticalFocusStage.Focused && focus.NextDecision == 370
            && focus.ReleaseCursor == 13 && focus.AdmissionCursor == 0,
            "Load must keep a valid value target, clamp cursors and schedule a fresh decision.");
        focus.Restore(new TacticalMeleeMemory(), 380, 13);
        Check(focus.Stage == TacticalFocusStage.Returning && focus.TargetId == -1 && focus.NormalStarted == 380,
            "Missing or expired saved targets must safely begin native control return.");
        CheckExecutionOwnership(Check);
        CheckLocalAcquisition(Check);
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-Focus-" + Guid.NewGuid() + ".xml");
        try
        {
            Scribe.saver.InitSaving(path, "threat"); retreat.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalMeleeThreat();
            Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            Check(loaded.EnemyId == 1 && loaded.Urgency == 0 && loaded.SeenTick == 130
                && loaded.Position == retreat.Position && loaded.Origin == "source" && !loaded.Shielded,
                "Native Scribe must retain both threat and explicit retreat report values.");
            foreach (TacticalFocusStage stage in Enum.GetValues<TacticalFocusStage>())
            {
                var saved = new TacticalFocusFire { Stage = stage, Started = 300, TargetId = 21,
                    Target = anchor, SeenTick = 360, NextDecision = 390, SelectedAt = 360,
                    ReleaseCursor = 4, NormalStarted = 380, AdmissionCursor = 8 };
                Scribe.saver.InitSaving(path, "focus"); saved.ExposeData(); Scribe.saver.FinalizeSaving();
                var restored = new TacticalFocusFire();
                Scribe.loader.InitLoading(path); restored.ExposeData(); Scribe.loader.ForceStop();
                Check(restored.Stage == stage && restored.Started == 300 && restored.TargetId == 21
                    && restored.Target == anchor && restored.SeenTick == 360 && restored.NextDecision == 390
                    && restored.SelectedAt == 360 && restored.ReleaseCursor == 4 && restored.NormalStarted == 380
                    && restored.AdmissionCursor == 8, "Native Scribe must preserve each focus ownership phase and its value/cursor payload.");
            }
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine($"PASS: {checks} A1.4 priority, bounded handoff, execution ownership/protection, mission retention and native save payload checks.");
    }

    private static void CheckLocalAcquisition(Action<bool, string> check)
    {
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(9, 1, 9) };
        map.cellIndices = new CellIndices(9, 9); map.thingGrid = new ThingGrid(map);
        var target = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn)); target.thingIDNumber = 42;
        var other = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn)); other.thingIDNumber = 43;
        var anchor = new IntVec3(4, 0, 4); var nearby = anchor + IntVec3.East * 2;
        nearby.GetThingList(map).Add(other); nearby.GetThingList(map).Add(target);
        var find = AccessTools.Method(typeof(MapComponent_TacticalCommands), "LocalFocusCandidate");
        Pawn Find() => (Pawn)find.Invoke(null, new object[] { map, anchor, 42 });
        check(ReferenceEquals(Find(), target), "A target which moved two tiles must be reacquired by ID even with another pawn on that tile.");
        nearby.GetThingList(map).Clear(); (anchor + IntVec3.East * 4).GetThingList(map).Add(target);
        check(Find() == null, "Reacquisition must not follow a target beyond the fixed neighbourhood or query the entire pawn list.");
        anchor.GetThingList(map).Add(other);
        check(Find() == null, "A different pawn on the old observed tile must never substitute for the focus identity.");
    }

    private static void CheckExecutionOwnership(Action<bool, string> check)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        pawn.def.defName = "focus-fixture"; pawn.thingIDNumber = 101;
        pawn.jobs = (Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker));
        var job = (Job)RuntimeHelpers.GetUninitializedObject(typeof(Job));
        job.def = new JobDef { defName = "HD_NewTacticalFocusFire" }; job.count = 22;
        AccessTools.Field(typeof(Pawn_JobTracker), "curJob").SetValue(pawn.jobs, job);
        var member = new TacticalMemberCommand { Pawn = pawn, Job = job };
        var command = new TacticalSquadCommand { FocusFire = new TacticalFocusFire { TargetId = 22 } };
        command.Members.Add(member);
        var owner = (MapComponent_TacticalCommands)RuntimeHelpers.GetUninitializedObject(typeof(MapComponent_TacticalCommands));
        AccessTools.Field(typeof(MapComponent_TacticalCommands), "byPawn").SetValue(owner,
            new Dictionary<Pawn, TacticalSquadCommand> { [pawn] = command });
        var owns = AccessTools.Method(typeof(MapComponent_TacticalCommands), "OwnsFocusTarget");
        bool Owns() => (bool)owns.Invoke(owner, new object[] { pawn, job });
        check(Owns(), "The exact owned focus job must be permitted to shoot the shared target.");
        job.count = 23; check(!Owns(), "An old target job must not fire after a shared target switch.");
        job.count = 22; command.FocusFire.Stage = TacticalFocusStage.Normal;
        check(!Owns(), "Normal combat handoff must disable the old focused driver before budgeted release finishes.");
        command.FocusFire = null; check(!Owns(), "An objective reset must revoke old focused casts.");
        var protect = AccessTools.Method(typeof(MapComponent_TacticalCommands), "ProtectedFocusMember");
        bool Protected() => (bool)protect.Invoke(null, new object[] { command, member });
        job.playerForced = true; check(Protected(), "Player-forced work must retain ownership."); job.playerForced = false;
        job.def.defName = "HD_CASStationaryGuidance"; check(Protected(), "Stationary guidance must retain ownership.");
        job.def.defName = "HD_NewTacticalIngress";
        var ingress = (JobDriver_TacticalIngress)RuntimeHelpers.GetUninitializedObject(typeof(JobDriver_TacticalIngress));
        AccessTools.Field(typeof(Pawn_JobTracker), "curDriver").SetValue(pawn.jobs, ingress);
        check(Protected(), "An unfinished ingress must not be pinned in the opening by focus fire.");
        ingress.AtPost = true; check(!Protected(), "A completed ingress hold must be eligible for focused fire and native handoff.");
        var observe = (JobDriver_TacticalObserve)RuntimeHelpers.GetUninitializedObject(typeof(JobDriver_TacticalObserve));
        job.def.defName = "HD_NewTacticalObserve";
        AccessTools.Field(typeof(Pawn_JobTracker), "curDriver").SetValue(pawn.jobs, observe);
        observe.Peeking = true; check(Protected(), "An active observer must finish withdrawing.");
        observe.Peeking = false; observe.AtPost = true; check(!Protected(), "A returned observer's hold must not keep it protected forever.");
        job.def.defName = "HD_NewTacticalFocusFire";
        AccessTools.Field(typeof(Pawn_JobTracker), "curDriver").SetValue(pawn.jobs, null);
        command.MedicalCare = new TacticalMedicalCare { Helper = member }; check(Protected(), "Medical job ownership must be respected.");
        command.MedicalCare = null;
        command.Phase = TacticalCommandPhase.Enter; command.PhaseStarted = 80;
        command.FocusFire = new TacticalFocusFire { Started = 100, Stage = TacticalFocusStage.Normal };
        command.FieldResponse = new TacticalFieldResponse { Started = 200, NextMove = 180 };
        command.ContactResponse = new TacticalContactResponse { Started = 100 };
        command.Plan = new TacticalLocalPlan(); member.Passed = member.Crossed = true;
        var cell = new IntVec3(4, 0, 5); command.SecuredCells.Add(cell);
        var plan = command.Plan; var field = command.FieldResponse;
        var finish = AccessTools.Method(typeof(MapComponent_TacticalCommands), "FinishFocusPause");
        finish.Invoke(null, new object[] { command, 500 });
        check(command.FocusFire == null && command.PhaseStarted == 480 && field.Started == 600 && field.NextMove == 580
            && command.ContactResponse.Started == 500, "Pause duration must be restored once to mission and retained response clocks.");
        finish.Invoke(null, new object[] { command, 500 });
        check(command.PhaseStarted == 480 && command.Phase == TacticalCommandPhase.Enter && command.Plan == plan
            && command.FieldResponse == field && command.SecuredCells.Contains(cell) && member.Passed && member.Crossed,
            "Native handoff/resume must retain portal history, mission phase and firing areas without double-counting pause time.");
        XElement xml = XDocument.Load("Defs/Organization/NewTacticalJobs.xml").Root.Elements("JobDef")
            .Single(d => (string)d.Element("defName") == "HD_NewTacticalFocusFire");
        check(Type.GetType((string)xml.Element("driverClass") + ", Helodrace") == typeof(JobDriver_TacticalFocusFire)
            && Enum.Parse<CheckJobOverrideOnDamageMode>((string)xml.Element("checkOverrideOnDamage")) == CheckJobOverrideOnDamageMode.Always,
            "The real focus JobDef must bind its installed driver and allow damage overrides.");
    }
}
