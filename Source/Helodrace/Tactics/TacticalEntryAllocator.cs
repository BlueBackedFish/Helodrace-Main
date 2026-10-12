using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    // Assign at most two member/guard seats in one main-thread slice. The plan
    // retains its raw interior seats until the completed result is published.
    public sealed class TacticalEntryAllocator
    {
        private struct Guard { internal IntVec3 Cell; internal int Side, Distance; internal bool Covered; }
        private readonly TacticalLocalPlan plan;
        private readonly IList<int> eligible;
        private readonly Func<IntVec3,bool> covered;
        private readonly IntVec3[] positions, inside;
        private readonly bool[] entering;
        private readonly HashSet<IntVec3> used = new HashSet<IntVec3>();
        private readonly List<Guard> guards = new List<Guard>();
        private int cursor, stage, nextSide;
        public bool Complete { get; private set; }
        public bool Valid { get; private set; } = true;
        public TacticalEntryAllocator(TacticalLocalPlan plan, IList<int> eligible, Func<IntVec3,bool> covered = null)
        {
            this.plan = plan; this.eligible = eligible; this.covered = covered;
            positions = plan.Stack.ToArray(); entering = new bool[positions.Length];
            inside = new IntVec3[Math.Min(plan.Positions.Count, eligible.Count)];
            for (int i=0; i<inside.Length; i++) inside[i] = plan.Positions[i];
            var seen = new HashSet<int>();
            foreach (int index in eligible)
                if (index < 0 || index >= positions.Length || !seen.Add(index)) { Complete=true; Valid=false; return; }
            if (inside.Length == 0) { Complete=true; Valid=false; }
            nextSide = plan.StackSide < 0 ? -1 : 1;
        }
        public bool Step()
        {
            if (Complete) return true;
            int remaining = TacticalCadence.MovementBatch;
            if (stage == 0)
            {
                while (cursor < inside.Length && remaining-- > 0)
                {
                    if (!used.Add(inside[cursor])) return Fail();
                    int member = eligible[cursor]; positions[member] = inside[cursor]; entering[member] = true; cursor++;
                }
                if (cursor == inside.Length) { cursor=0; stage=inside.Length==positions.Length ? 3 : 1; }
                return false;
            }
            if (stage == 1)
            {
                IntVec3 tangent = new IntVec3(-plan.Inward.z,0,plan.Inward.x);
                while (cursor < plan.Stack.Count && remaining-- > 0)
                {
                    IntVec3 cell = plan.Stack[cursor++];
                    if (!used.Add(cell)) continue;
                    int side = Math.Sign((cell.x-plan.Opening.x)*tangent.x+(cell.z-plan.Opening.z)*tangent.z);
                    guards.Add(new Guard { Cell=cell,Side=side,Covered=side!=0 && covered?.Invoke(cell)==true,
                        Distance=cell.DistanceToSquared(plan.Opening) });
                }
                if (cursor == plan.Stack.Count)
                {
                    if (guards.Count < positions.Length-inside.Length) return Fail();
                    guards.Sort((a,b) => { int compare=b.Covered.CompareTo(a.Covered);
                        if(compare==0)compare=a.Distance.CompareTo(b.Distance);
                        if(compare==0)compare=a.Cell.x.CompareTo(b.Cell.x);
                        return compare==0 ? a.Cell.z.CompareTo(b.Cell.z) : compare; });
                    stage=2; cursor=0;
                }
                return false;
            }
            if (stage == 2)
            {
                while (cursor < positions.Length && remaining > 0)
                {
                    int member=cursor++; if(entering[member])continue;
                    bool cover=guards[0].Covered;
                    int selected=guards.FindIndex(g=>g.Covered==cover && g.Side==nextSide);
                    if(selected<0)selected=guards.FindIndex(g=>g.Covered==cover && g.Side==-nextSide);
                    if(selected<0)selected=0;
                    Guard guard=guards[selected];guards.RemoveAt(selected);positions[member]=guard.Cell;
                    if(guard.Side!=0)nextSide=-guard.Side; remaining--;
                }
                if(cursor==positions.Length)stage=3;
                return false;
            }
            used.Clear(); foreach(IntVec3 cell in positions)if(!used.Add(cell))return Fail();
            plan.Positions.Clear();plan.Positions.AddRange(positions);plan.RetainedOutside.Clear();plan.EntryOrder.Clear();
            for(int i=0;i<inside.Length;i++)plan.EntryOrder.Add(eligible[i]);
            for(int i=0;i<positions.Length;i++)
                if(!entering[i]) {plan.RetainedOutside.Add(i);plan.EntryOrder.Add(i);}
            Complete=true;return true;
        }
        private bool Fail() { Complete=true;Valid=false;return true; }
    }
}
