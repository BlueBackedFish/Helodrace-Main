using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalCqbPursuitTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
        var pursuit=new TacticalCqbPursuit();
        var sight=new IntVec3(6,0,5);
        var targetDoor=new TacticalRoomFrontier {Opening=new IntVec3(4,0,5),Inward=IntVec3.East};
        var unrelated=new TacticalRoomFrontier {Opening=new IntVec3(1,0,1),Inward=IntVec3.North};
        pursuit.Observe(7,sight,100);
        var room=new HashSet<IntVec3>{targetDoor.Inside,sight,new IntVec3(7,0,5)};
        AccessTools.Method(typeof(TacticalCqbPursuit),"CompleteRoom").Invoke(pursuit,new object[]{room,false});
        Check(pursuit.Priority(targetDoor,105)==0 && pursuit.Priority(unrelated,105)==2,
            "An observed room must outrank another goal-directed room, not only the exact observed door tile.");
        int version=pursuit.Version;
        pursuit.Observe(9,new IntVec3(7,0,5),110);
        Check(pursuit.Version==version && pursuit.RoomReady && pursuit.Target==new IntVec3(7,0,5),
            "An actually reobserved enemy in the same room reuses its footprint instead of rescanning it.");
        pursuit.Observe(10,new IntVec3(99,0,99),109);
        Check(pursuit.EnemyId==9,"An older shared report cannot replace newer room knowledge.");
        var adjacent=new TacticalRoomFrontier {Opening=new IntVec3(8,0,5),Inward=IntVec3.East};
        var outdoors=new TacticalRoomFrontier {Opening=new IntVec3(6,0,4),Inward=IntVec3.South,Outdoors=true};
        pursuit.RecordEntered(room,new[]{adjacent,outdoors},115);
        Check(pursuit.Entered && pursuit.Priority(adjacent,116)==1 && pursuit.Priority(targetDoor,116)==2
            && pursuit.Priority(outdoors,116)==2,"After entering the sighted room, its known indoor exits take priority, not a return breach or outdoor exit.");
        var further=new TacticalRoomFrontier {Opening=new IntVec3(12,0,5),Inward=IntVec3.East};
        pursuit.RecordEntered(new HashSet<IntVec3>{new IntVec3(10,0,5)},new[]{further},120);
        Check(pursuit.Priority(further,121)==2 && pursuit.Priority(adjacent,121)==1,
            "A single sighting only gives one-hop room priority; it cannot infer an unseen enemy's next room indefinitely.");
        pursuit.Expire(110+TacticalContactMemory.RetentionTicks);
        Check(!pursuit.Target.IsValid && pursuit.Room.Count==0 && pursuit.AdjacentDoors.Count==0 && pursuit.Priority(adjacent,1910)==2,
            "Expired sightings release pursuit priority and stale room state.");
        pursuit.Observe(9,new IntVec3(10,0,5),1920);
        Check(!pursuit.RoomReady && !pursuit.Entered && pursuit.AdjacentDoors.Count==0,
            "A new observation in another room starts a new bounded lead.");
        Check(!TacticalCqbPursuit.PreferSample(2,50,7,50,7)
            && TacticalCqbPursuit.PreferSample(7,50,2,50,7)
            && TacticalCqbPursuit.PreferSample(2,51,7,50,7),
            "Simultaneous sightings keep a stable identity, while genuinely newer sightings may replace it.");
        Console.WriteLine($"PASS: {checks} observed-room priority, one-hop pursuit, reused snapshots, stale reports and stable sample selection checks.");
    }
}
