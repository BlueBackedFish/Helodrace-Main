using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse.AI;
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
        Check(command.Due == 110 && command.FrontierCursor == 0 && command.Plan == plan
            && command.Phase == TacticalCommandPhase.Clear, "Ongoing engagement must preserve the current room and defer the next frontier.");
        bool held = (bool)AccessTools.Method(owner.GetType(), "HoldRoomCombat")
            .Invoke(owner,new object[]{command,new List<TacticalMemberCommand>(),275});
        Check(!held && command.RoomWatchHandledUntil == 280,
            "The quiet tail must permit the rest of the squad to plan instead of holding everyone for 90 ticks.");
        CheckCommittedIngress(Check,origin);
        Console.WriteLine("PASS: " + checks + " entry combat quiet-window, connected bounded cover, occupied posts and room-progress checks.");
    }
    private static void CheckCommittedIngress(Action<bool,string> check,IntVec3 origin)
    {
        var owner=new MapComponent_TacticalCommands(null);
        var command=new TacticalSquadCommand {Phase=TacticalCommandPhase.Enter,
            Plan=new TacticalLocalPlan {Opening=origin,Inward=IntVec3.East},
            ContactResponse=new TacticalContactResponse {Rear=true,DirectAttack=true},
            ContactRestoring=true,FocusFire=new TacticalFocusFire {Started=90},
            FieldResponse=new TacticalFieldResponse {Started=80},MedicalCare=new TacticalMedicalCare(),
            OpeningAction=new TacticalOpeningAction {Launched=true,EffectsCleared=true,Enemy=origin+IntVec3.East*3}};
        var pawn=new Pawn {Position=origin-IntVec3.East};
        pawn.jobs=(Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker));
        var definition=(JobDef)RuntimeHelpers.GetUninitializedObject(typeof(JobDef));definition.defName="HD_NewTacticalIngress";
        var job=new Job {def=definition,targetA=pawn.Position};
        AccessTools.Field(typeof(Pawn_JobTracker),"curJob").SetValue(pawn.jobs,job);
        pawn.jobs.curDriver=new JobDriver_TacticalIngress();
        var member=new TacticalMemberCommand {Pawn=pawn,Job=job,Crossed=true,Passed=true};command.Members.Add(member);
        IntVec3 contactPost=origin+IntVec3.North*5,fieldPost=origin+IntVec3.South*5;
        command.ContactResponse.Occupied.Add(contactPost);
        var claims=(Dictionary<IntVec3,TacticalSquadCommand>)AccessTools.Field(owner.GetType(),"claims").GetValue(owner);
        claims[contactPost]=claims[command.Plan.Outside]=command;
        command.Contacts.Memory.Remember(12,origin+IntVec3.East*3,origin+IntVec3.East*3,false,true,90);
        var action=command.OpeningAction;
        check(TacticalEntryCombatPolicy.CommittedIngress(command),"A real portal ingress must retain execution ownership during combat.");
        AccessTools.Method(owner.GetType(),"PrepareCommittedIngress").Invoke(owner,new object[]{command,100});
        check(command.ContactResponse==null&&command.FocusFire==null&&command.FieldResponse==null&&!command.ContactRestoring
            &&command.MedicalCare.CancelRequested,"Squad reaction, identification/restoration and medical overlays cannot stop queued entrants.");
        check(member.Job==job&&pawn.CurJob==job&&member.Crossed&&member.Passed&&command.OpeningAction==action
            &&command.Contacts.Memory.Entries.Count==1,"Removing competing controllers must preserve live ingress, actual crossing, grenade safety and observed knowledge.");
        check(!claims.ContainsKey(contactPost)&&!claims.ContainsKey(fieldPost)&&claims.ContainsKey(command.Plan.Outside),
            "Only obsolete reaction footprints are released, not the actual ingress mouth.");
        command.Plan.Direct=true;
        check(!TacticalEntryCombatPolicy.CommittedIngress(command),"Ordinary field movement remains eligible for tactical reactions.");
        command.Plan.Direct=false;command.Phase=TacticalCommandPhase.Clear;
        check(!TacticalEntryCombatPolicy.CommittedIngress(command),"After all entrants arrive, normal combat/room responses resume.");
    }
}
