using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalStackSecurityTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition,string message) { checks++; if (!condition) throw new Exception(message); }
        foreach (IntVec3 forward in GenAdj.CardinalDirections)
        {
            var plan = new TacticalLocalPlan { Opening=new IntVec3(20,0,20), Inward=forward };
            var side = new IntVec3(-forward.z,0,forward.x);
            plan.Stack.AddRange(new[] { plan.Outside+side,plan.Outside-side,
                plan.Outside+side*3,plan.Outside-side*3,plan.Outside-forward*3+side });
            TacticalStackSecurity.Assign(plan);
            Check(plan.StackDirections[0] == IntVec3.Zero && plan.StackDirections.Contains(side)
                && plan.StackDirections.Contains(-side) && plan.StackDirections.Contains(-forward),
                "Preserve the inner entry watch and divide the outer wings/rear in all rotations.");
            Check(plan.StackDirections[2] == side && plan.StackDirections[3] == -side
                && TacticalStackSecurity.Face(plan,2,plan.Stack[2]) == plan.Stack[2]+side*4,
                "Outer wing members must look away from the mouth from their assigned posts.");
        }
        var partial = new TacticalLocalPlan { Opening=new IntVec3(20,0,20),Inward=IntVec3.East };
        partial.Stack.AddRange(new[]{new IntVec3(19,0,19),new IntVec3(19,0,21)});
        var used = new HashSet<IntVec3>(partial.Stack);
        var reads = new Dictionary<IntVec3,int>();
        bool Walk(IntVec3 cell)
        {
            reads.TryGetValue(cell,out int count); reads[cell]=count+1;
            return cell.x >= 12 && cell.x <= 19 && cell.z >= 18 && cell.z <= 22
                || cell.x >= 8 && cell.x <= 12 && cell.z == 18;
        }
        var follow = new TacticalApproachGuards(partial.Outside,6,Walk,c=>!used.Contains(c));
        int slices = 0;
        while (!follow.Complete && slices++ < 20)
        {
            int before=reads.Values.Sum();follow.Step();
            Check(reads.Values.Sum()-before <= TacticalApproachGuards.ProbesPerStep,"Follow-seat discovery must bound live map probes per slice.");
        }
        Check(follow.Valid && follow.Posts.Distinct().Count()==6 && reads.Values.All(n=>n==1)
            && follow.Posts.All(c=>c.DistanceToSquared(partial.Outside)<=9 && c.x<partial.Opening.x),
            "Surplus pawns follow to nearby connected approach floor, not old distant or far-side posts.");
        var eligible = new[]{6,2,4,1,0,3,5,7};
        Check(TacticalEntryAllocation.BindApproach(partial,8,eligible,follow.Posts)
            && partial.StackMembers.SequenceEqual(new[]{6,2}) && partial.Stack.Distinct().Count()==8,
            "A partial stack must bind assault participants separately from nearby guards.");
        IntVec3 guardPost=partial.Stack[0];
        partial.Positions.AddRange(Enumerable.Range(1,4).Select(i=>new IntVec3(20+i,0,20)));
        Check(TacticalEntryAllocation.Allocate(partial,eligible) && partial.RetainedOutside.Count==6
            && partial.RetainedOutside.SetEquals(Enumerable.Range(0,8).Except(new[]{6,2}))
            && partial.Positions[0]==guardPost && partial.Positions.Distinct().Count()==8,
            "Even with spare room seats, only stack participants enter and follow guards retain their nearby posts.");
        Check(!TacticalEntryAllocation.Promote(partial,0,6),"A non-stacked guard cannot be silently promoted into the assault.");
        Check(!TacticalStackSecurity.ReadyToEnter(partial,6),"A scheduled entrant must physically reach its stack before ingress.");
        TacticalStackSecurity.Arrived(partial,6,partial.Stack[6]+IntVec3.East);
        TacticalStackSecurity.Arrived(partial,0,partial.Stack[0]);
        Check(partial.StackedMembers.Count==0,"A wrong cell or a follow guard cannot become a stacked assault member.");
        TacticalStackSecurity.Arrived(partial,6,partial.Stack[6]);
        Check(TacticalStackSecurity.ReadyToEnter(partial,6) && !TacticalStackSecurity.ReadyToEnter(partial,2),
            "Only an actual stack arrival admits a portal entrant, including after a timeout.");
        TacticalStackSecurity.Assign(partial);
        Check(partial.StackDirections.Where((_,i)=>!partial.StackMembers.Contains(i)).Distinct().Count()==3,
            "Follow guards must divide side and rear watch directions.");
        var command=new TacticalSquadCommand {Plan=partial};
        for(int i=0;i<8;i++) command.Members.Add(new TacticalMemberCommand {Pawn=new Pawn()});
        var order=(List<int>)AccessTools.Method(typeof(TacticalEntryOrder),"For")
            .Invoke(null,new object[]{command,command.Members,partial});
        Check(order.Count==2 && order.All(partial.StackMembers.Contains),"Live role ordering must also exclude follow guards.");
        var capped = new TacticalApproachGuards(IntVec3.Zero,1,_=>true,_=>false);
        int capSlices=0;
        while(!capped.Step() && capSlices++<20) { }
        Check(capped.Complete && !capped.Valid,"A missing guard seat must stop at the probe cap instead of scanning indefinitely.");
        var blocked = new TacticalApproachGuards(IntVec3.Zero,1,c=>c==IntVec3.Zero,_=>true);
        Check(blocked.Step() && !blocked.Valid,"Disconnected space across a wall must never be offered as a follow seat.");
        partial.ReportedPassage=true;
        Check(TacticalStackSecurity.ReadyToEnter(partial,2),"A previously secured passage retains direct movement without a new stack.");
        Console.WriteLine($"PASS: {checks} divided stack watches, bounded connected follow seats and stack-only ingress checks.");
    }
}
