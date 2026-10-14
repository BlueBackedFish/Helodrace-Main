using System;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

internal static class TacticalFieldManeuverTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
        Check(TacticalFieldPolicy.Continue(1000, 500, -1), "Short observation gaps must preserve the field mission.");
        Check(TacticalFieldPolicy.Continue(1000, 0, 900), "Actual recent shots must preserve combat after stale scheduled observation.");
        Check(!TacticalFieldPolicy.Continue(900, 0, -1), "A lost mission must terminate after its bounded grace period.");
        Check(!TacticalFieldPolicy.Prepare(false, false, false, 30, 31 * 31), "Attackers must keep approaching a distant passive observation.");
        Check(TacticalFieldPolicy.Prepare(false, false, false, 30, 30 * 30), "Targets at the weapon boundary allow preparation.");
        Check(TacticalFieldPolicy.Prepare(true, false, false, 30, 10000)
            && TacticalFieldPolicy.Prepare(false, true, false, 30, 10000)
            && TacticalFieldPolicy.Prepare(false, false, true, 30, 10000), "Defense, support and incoming fire retain early responses.");
        Check(AccessTools.Method(typeof(Pawn), nameof(Pawn.Notify_UsedVerb), new[] { typeof(Pawn), typeof(Verb) }) != null,
            "Actual successful-shot notifications must bind the installed native game signature.");
        var service = new MapComponent_TacticalCommands(null);
        var carrier = new TacticalMemberCommand { Pawn = new Pawn { thingIDNumber = 21 }, FieldPostState = TacticalFieldPostState.Fixed,
            FieldHadCover = true, FieldPost = IntVec3.Invalid };
        Thing cover = (Thing)RuntimeHelpers.GetUninitializedObject(typeof(Thing));
        var mapIndex = AccessTools.Field(typeof(Thing), "mapIndexOrState");
        mapIndex.SetValue(cover, (sbyte)0); carrier.FieldCover = cover;
        var refresh = AccessTools.Method(service.GetType(), "RefreshFieldPost");
        var field = new TacticalFieldResponse { Focus = IntVec3.North * 30 };
        refresh.Invoke(service, new object[] { carrier, field, 100 });
        Check(carrier.FieldPostState == TacticalFieldPostState.Fixed, "Intact recorded cover keeps its post through periodic review.");
        field.Focus = IntVec3.East * 30;
        refresh.Invoke(service, new object[] { carrier, field, 300 });
        Check(carrier.FieldPostState == TacticalFieldPostState.Fixed, "A target/direction change must not unlock the anvil position.");
        mapIndex.SetValue(cover, (sbyte)-2);
        refresh.Invoke(service, new object[] { carrier, field, 500 });
        Check(carrier.FieldPostState == TacticalFieldPostState.Unplaced && carrier.FieldCover == null,
            "Destroying the actual cover must request a fresh placement for its user.");
        CheckMovement(Check);
        var rejected = new TacticalMemberCommand { FieldPostState = TacticalFieldPostState.Moving,
            FieldPost = new IntVec3(1,0,1), FieldHadCover = true };
        var reject = AccessTools.Method(typeof(MapComponent_TacticalCommands), "RejectFieldPost");
        reject.Invoke(null, new object[] { rejected, 100 });
        Check(rejected.FieldPostState == TacticalFieldPostState.Uncovered && !rejected.FieldPost.IsValid
            && !rejected.FieldHadCover && rejected.FieldDecisionAt == 190, "Failed placement is not fixed or a compulsory return destination.");
        reject.Invoke(null, new object[] { rejected, 200 });
        Check(rejected.FieldDecisionAt == 1100 && rejected.FieldPlacementFailures == 2,
            "Repeated failed placement backs off instead of scanning every decision slice.");
        var fixedJob = AccessTools.Method(typeof(MapComponent_TacticalCommands), "FixedFieldPostJob");
        Check(fixedJob.Invoke(service, new object[] { rejected, new TacticalSquadCommand(), 200 }) == null,
            "Uncovered placement cannot acquire a permanent fixed-position wait.");
        Console.WriteLine("PASS: " + checks + " field feedback, contact grace and attack approach checks.");
    }
    private static void CheckMovement(Action<bool,string> check)
    {
        var service = new MapComponent_TacticalCommands(null);
        var field = new TacticalFieldResponse { Anchor=new IntVec3(50,0,50), Forward=IntVec3.North };
        for(int i=0;i<3;i++) { field.FirePoints.Add(field.Anchor); field.Areas.Add(new TacticalFireArea()); }
        var command = new TacticalSquadCommand { FieldResponse=field };
        var active = new List<TacticalMemberCommand>();
        for(int i=0;i<6;i++)
        {
            var member = new TacticalMemberCommand { Pawn=new Pawn(),FieldPostState=TacticalFieldPostState.Fixed };
            active.Add(member); command.Members.Add(member); field.Groups.Add(0);
        }
        void Place(int index,IntVec3 cell) => AccessTools.Field(typeof(Thing),"positionInt").SetValue(active[index].Pawn,cell);
        var m = field.Maneuver = new TacticalFieldManeuver { Phase=TacticalManeuverPhase.Moving,Started=2000,PhaseAt=2000,
            Origin=field.Anchor,Party=new List<int>{2,3,4,5},Waypoints=new List<IntVec3>{field.Anchor+IntVec3.East*8,
                field.Anchor+new IntVec3(16,0,4),field.Anchor+new IntVec3(22,0,10)} };
        field.FirePoints[1]=m.Waypoints[0]; foreach(int index in m.Party) { field.FireGroup.Add(index); Place(index,m.Waypoints[0]); }
        var advance=AccessTools.Method(service.GetType(),"AdvanceFieldManeuver");
        void Step(int tick)
        {
            field.Areas[1].Prepared = true; field.Areas[1].Cells.Clear();
            for (int index = 2; index < 6; index++) field.Areas[1].Cells.Add(active[index].Pawn.Position);
            active[0].FieldShotAt=active[1].FieldShotAt=tick;
            advance.Invoke(service,new object[]{command,active,field,tick,false});
        }
        Step(2000); check(m.Half==1 && m.Leg==0,"The first moving half must hand over to its covering half before advancing the leg.");
        Step(2010); check(m.Leg==1 && field.FirePoints[1]==m.Waypoints[1],"Both halves arriving advances the shared waypoint exactly once.");
        for(int i=2;i<5;i++) Place(i,m.Waypoints[1]);
        Step(2020); Step(2030);
        check(m.Leg==1 && m.Half==1,"A late covering soldier receives a bounded opportunity to catch up.");
        Step(2270); check(m.Leg==2 && m.Half==0,"One late soldier must not reset the whole team to the original waypoint.");
        for(int i=2;i<5;i++) Place(i,m.Waypoints[2]);
        Step(2280); Step(2530);
        check(m.Phase==TacticalManeuverPhase.Strike && m.ReadyShots==0,"Arrival must not fabricate actual crossfire evidence.");
        for(int i=2;i<5;i++) active[i].FieldShotAt=2540;
        Step(2540); check(m.ReadyShots==3,"Only actual new firing records confirm the team established crossfire.");
        Step(2890); check(m.Phase==TacticalManeuverPhase.Strike,
            "Confirmed crossfire must not return on the old fixed strike timer.");
        Step(3440); check(m.Phase==TacticalManeuverPhase.Recover && field.FirePoints[1]==field.Anchor,
            "A flank that has lost useful shooting for the bounded grace period may recover.");
        for(int i=2;i<5;i++) Place(i,field.Anchor);
        Step(3450); check(field.Maneuver==null && field.FireGroup.Count==0 && active[0].FieldPostState==TacticalFieldPostState.Fixed,
            "A ready majority completes recovery while preserving the original main body's fixed state.");
    }
}
