using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Helodrace.Tactics;
using Verse;

internal static class TacticalEntryCombatTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        IntVec3 origin = new IntVec3(20, 0, 20);
        int probes = 0;
        float Cover(IntVec3 cell) { probes++; return cell == origin + IntVec3.East * 2 ? .65f : 0f; }
        Check(TacticalEntryCombatPolicy.FindCover(origin, _ => true, _ => true, Cover) == origin + IntVec3.East * 2,
            "A useful nearby connected cover should replace an exposed arrival post.");
        Check(probes == TacticalEntryCombatPolicy.CoverCells, "Open floor must evaluate at most 13 cover cells.");
        Check(TacticalEntryCombatPolicy.FindCover(origin, c => c != origin + IntVec3.East, _ => true, Cover) == origin,
            "A cover two cells away behind a blocking wall cannot be selected through it.");
        Check(TacticalEntryCombatPolicy.FindCover(origin, _ => true, c => c != origin + IntVec3.East * 2, Cover) == origin,
            "An occupied, reserved mouth or foreign-claimed cover post must be excluded.");
        Check(TacticalEntryCombatPolicy.FindCover(origin, _ => true, _ => true,
            c => c == origin + IntVec3.North * 3 ? 1f : 0f) == origin,
            "An attractive distant cover cannot pull an entrant deep into the room.");
        Check(TacticalEntryCombatPolicy.FindCover(origin, _ => true, _ => true, _ => .3f) == origin,
            "Equivalent cover must not cause needless movement or post oscillation.");
        Check(TacticalEntryCombatPolicy.FindCover(origin, c => c == origin, _ => true, _ => 0f) == origin,
            "A tiny room with no spare connected cell must keep its existing usable post.");
        Check(TacticalEntryCombatPolicy.Engaged(280, 279) && !TacticalEntryCombatPolicy.Engaged(280, 280)
            && !TacticalEntryCombatPolicy.Engaged(0, 0), "The quiet window must expire, including a command that never engaged.");
        Check(TacticalEntryCombatPolicy.QuietTicks == 90
            && TacticalEntryCombatPolicy.Engaged(100 + TacticalEntryCombatPolicy.QuietTicks,189)
            && !TacticalEntryCombatPolicy.Engaged(100 + TacticalEntryCombatPolicy.QuietTicks,190),
            "The requested quiet interval must be exactly 90 ticks after the last evidence, without an idle-room delay.");
        Check(TacticalEntryCombatPolicy.ArrivalPhase(TacticalCommandPhase.Enter)
            && TacticalEntryCombatPolicy.ArrivalPhase(TacticalCommandPhase.Clear)
            && !TacticalEntryCombatPolicy.ArrivalPhase(TacticalCommandPhase.Stack)
            && !TacticalEntryCombatPolicy.ArrivalPhase(TacticalCommandPhase.BlastWait),
            "Arrival gunfire must not leak into stack or explosive-safety phases.");

        // Integration: a hot room must return before surveying, consuming a
        // frontier or asking for a new plan. No Unity/game tick loop is required.
        var owner = (MapComponent_TacticalCommands)RuntimeHelpers.GetUninitializedObject(typeof(MapComponent_TacticalCommands));
        var command = new TacticalSquadCommand { Phase = TacticalCommandPhase.Clear, EntryCombatUntil = 280 };
        var plan = new TacticalLocalPlan(); command.Plan = plan;
        command.Frontiers.Add(new TacticalRoomFrontier { Opening = origin, Inward = IntVec3.North });
        typeof(MapComponent_TacticalCommands).GetMethod("AdvanceRoomClear", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(owner, new object[] { command, new List<TacticalMemberCommand>(), 100 });
        Check(command.Due == 130 && command.FrontierCursor == 0 && command.Plan == plan
            && command.Phase == TacticalCommandPhase.Clear, "Ongoing engagement must preserve the current room and defer the next frontier.");
        typeof(MapComponent_TacticalCommands).GetMethod("AdvanceRoomClear", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(owner,new object[]{command,new List<TacticalMemberCommand>(),275});
        Check(command.Due == 280,"Quiet-window expiry must wake the command without an extra 30-tick polling delay.");
        Console.WriteLine("PASS: " + checks + " entry combat quiet-window, connected bounded cover, occupied posts and room-progress checks.");
    }
}
