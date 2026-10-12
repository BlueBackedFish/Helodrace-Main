using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    public struct TacticalEntryCandidate
    {
        public int Index, Priority, Distance;
        public bool Leader;
    }
    public static class TacticalEntryOrder
    {
        public static int Priority(string function) => function == "HAR" || function == "MachineGun" ? 1
            : function == "Sniper" || function == "Bazooka" || function == "RocketSupply" ? 2
            : function == "Medic" ? 3 : 0;

        public static List<int> Build(IEnumerable<TacticalEntryCandidate> candidates)
        {
            var ranked = new List<TacticalEntryCandidate>();
            foreach (TacticalEntryCandidate candidate in candidates) ranked.Add(candidate);
            ranked.Sort((a,b) => { int compare = a.Priority.CompareTo(b.Priority);
                if (compare == 0) compare = a.Distance.CompareTo(b.Distance);
                return compare == 0 ? a.Index.CompareTo(b.Index) : compare; });
            int leader = ranked.FindIndex(c => c.Leader);
            if (leader >= 0)
            {
                TacticalEntryCandidate commander = ranked[leader]; ranked.RemoveAt(leader);
                ranked.Insert(ranked.Count / 2, commander);
            }
            var result = new List<int>(ranked.Count);
            foreach (TacticalEntryCandidate candidate in ranked) result.Add(candidate.Index);
            return result;
        }
        internal static List<int> For(TacticalSquadCommand command, IList<TacticalMemberCommand> active, TacticalLocalPlan plan)
        {
            Pawn leader = command.Link.Unit?.Commander;
            var candidates = new List<TacticalEntryCandidate>(active.Count);
            foreach (TacticalMemberCommand m in active)
            {
                int index = command.Members.IndexOf(m);
                if (plan.StackMembers.Count > 0 && !plan.StackMembers.Contains(index)) continue;
                IntVec3 position = plan.Stack.Count == command.Members.Count ? plan.Stack[index] : m.Pawn.Position;
                candidates.Add(new TacticalEntryCandidate { Index = index, Distance = position.DistanceToSquared(plan.Outside),
                    Priority = m.Pawn == command.Breacher ? -1 : Priority(m.CombatFunction), Leader = leader != null && m.Pawn == leader });
            }
            return Build(candidates);
        }
    }
}
