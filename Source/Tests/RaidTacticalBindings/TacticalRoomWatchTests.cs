using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;
using Verse.AI;

internal static class TacticalRoomWatchTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); definition.defName = "watchFixture";
        var roster = Enumerable.Range(0, 13).Select(i => new TacticalMemberCommand
            { Pawn = new Pawn { thingIDNumber = 1000 + i, def = definition }, Fireteam = i == 12 ? -1 : i / 4 }).ToList();
        var commander = roster[12].Pawn;
        roster[0].Medic = true;
        var low = TacticalRoomWatchPolicy.Select(roster, false, false, commander, roster[1].Pawn);
        Check(low.Count == 2 && !low.Contains(roster[0]) && !low.Contains(roster[1]) && !low.Contains(roster[12]),
            "LOW must leave two ordinary members, preserving medic, breacher and commander for the next action.");
        Check(TacticalRoomWatchPolicy.Select(roster.Take(1).ToList(), false, false, null, null).Count == 0,
            "A lone LOW member cannot watch while leaving nobody to proceed.");
        var high = TacticalRoomWatchPolicy.Select(roster, true, false, commander, roster[1].Pawn);
        Check(high.Count == 4 && high.All(m => m.Fireteam == 2) && !high.Contains(roster[12]),
            "HIGH must keep the trailing whole fireteam so leading entry teams can proceed without its release barrier.");
        Check(TacticalRoomWatchPolicy.Select(roster.Take(4).ToList(), true, true, commander, null).Count == 4,
            "A HIGH child task may watch with its own team while siblings continue.");
        Check(TacticalRoomWatchPolicy.Select(roster.Take(4).ToList(), true, false, commander, null).Count == 0,
            "An unsplit HIGH force with only one surviving team cannot leave that entire force behind.");
        Check(TacticalRoomWatchPolicy.Live(190, 129) && !TacticalRoomWatchPolicy.Live(190, 130)
            && TacticalEntryCombatPolicy.Engaged(190, 189),
            "Live combat holds everybody briefly; the remaining 90-tick quiet tail is selective.");

        var owner = new MapComponent_TacticalCommands(null);
        var command = new TacticalSquadCommand { Id = "watch", Phase = TacticalCommandPhase.Clear,
            EntryCombatUntil = 190, Plan = new TacticalLocalPlan() };
        var member = roster[2];
        member.Pawn.Position = new IntVec3(10, 0, 10);
        member.Pawn.jobs = (Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker));
        member.Job = new Job();
        AccessTools.Field(typeof(Pawn_JobTracker), "curJob").SetValue(member.Pawn.jobs, member.Job);
        command.Members.Add(member);
        member.RoomWatchUntil = 190; member.RoomWatchCell = member.Pawn.Position;
        var owners = (Dictionary<Pawn,TacticalSquadCommand>)AccessTools.Field(owner.GetType(), "byPawn").GetValue(owner);
        owners[member.Pawn] = command;
        bool Watching(int tick) => (bool)AccessTools.Method(owner.GetType(), "RoomWatching").Invoke(owner, new object[]{member, tick});
        bool Hold(int tick) => (bool)AccessTools.Method(owner.GetType(), "HoldRoomCombat")
            .Invoke(owner, new object[]{command, new List<TacticalMemberCommand>(), tick});
        Check(!Hold(130) && command.RoomWatchHandledUntil == 190 && member.RoomWatchUntil == 190,
            "An existing watch must permit progression and cannot be duplicated on repeated quiet decisions.");
        Check(!Hold(140) && member.RoomWatchUntil == 190, "Polling must not lengthen or restart the watch.");
        command.Plan = new TacticalLocalPlan(); command.Phase = TacticalCommandPhase.Stack;
        member.CqbOrder = new TacticalCqbOrder { Owner = command.Id, Version = command.PlanVersion,
            Destination = new IntVec3(20, 0, 20) };
        Check(Watching(150) && member.CqbOrder.Destination != member.RoomWatchCell,
            "The watch must survive the next plan while retaining the new movement intent for release.");
        Check(!Watching(190), "The watch releases exactly at its quiet deadline without another squad timer.");
        AccessTools.Method(owner.GetType(), "EntryCombatEvidence").Invoke(null, new object[]{command,member,160});
        Check(member.RoomWatchUntil == 250 && command.EntryCombatUntil == 190,
            "A watcher's live engagement extends only its own watch, not the moving squad's quiet deadline.");
        command.Phase = TacticalCommandPhase.Returning;
        Check(!Watching(170) && member.RoomWatchUntil == 0, "Retreat must cancel the watch immediately.");
        command.Phase = TacticalCommandPhase.Stack; member.RoomWatchUntil = 250;
        command.ChargeAction = new TacticalChargeAction();
        Check(!Watching(180) && member.RoomWatchUntil == 0, "C4 safety withdrawal must override the watch.");
        command.ChargeAction = null; member.RoomWatchUntil = 250; command.FieldResponse = new TacticalFieldResponse();
        Check(!Watching(180), "Outdoor field response must not inherit an indoor rear watch.");
        command.FieldResponse = null; member.RoomWatchUntil = 250;
        member.Pawn.Position += IntVec3.East;
        Check(!Watching(180), "A displaced or evading guard cannot be forced back onto the old watch cell.");
        Console.WriteLine($"PASS: {checks} selective LOW/HIGH indoor watch, continuing intent, expiry and safety checks.");
    }
}
