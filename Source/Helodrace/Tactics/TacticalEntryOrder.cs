using System;
using System.Collections.Generic;
using System.Linq;
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
            var ranked = candidates.OrderBy(c => c.Priority).ThenBy(c => c.Distance).ThenBy(c => c.Index).ToList();
            int leader = ranked.FindIndex(c => c.Leader);
            if (leader >= 0)
            {
                TacticalEntryCandidate commander = ranked[leader]; ranked.RemoveAt(leader);
                ranked.Insert(ranked.Count / 2, commander);
            }
            return ranked.Select(c => c.Index).ToList();
        }
        internal static List<int> For(TacticalSquadCommand command, IList<TacticalMemberCommand> active, TacticalLocalPlan plan)
        {
            Pawn leader = command.Link.Unit?.Commander;
            return Build(active.Select(m =>
            {
                int index = command.Members.IndexOf(m);
                IntVec3 position = plan.Stack.Count == command.Members.Count ? plan.Stack[index] : m.Pawn.Position;
                return new TacticalEntryCandidate { Index = index, Distance = position.DistanceToSquared(plan.Outside),
                    Priority = m.Pawn == command.Breacher ? -1 : Priority(m.CombatFunction), Leader = leader != null && m.Pawn == leader };
            }).Where(c => plan.StackMembers.Count == 0 || plan.StackMembers.Contains(c.Index)));
        }
    }
}
