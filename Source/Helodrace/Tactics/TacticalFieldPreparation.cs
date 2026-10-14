using System;
using System.Collections.Generic;
using System.Diagnostics;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed partial class MapComponent_TacticalCommands
    {
        private void AdvanceHighDefense(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick)
        {
            if (field.Maneuver != null || field.Screen != null) return;
            for (int group = 0; group < 3; group++)
            {
                var area = field.Areas[group];
                if (!area.Prepared || area.RelocationAttempted || area.Cells.Count >= 2) continue;
                var guide = active.Find(m => field.Groups[command.Members.IndexOf(m)] == group);
                if (guide == null || active.Exists(m => field.Groups[command.Members.IndexOf(m)] == group && m.FieldPostState == TacticalFieldPostState.Fixed))
                { area.RelocationAttempted = true; continue; }
                var search = area.RelocationSearch ?? (area.RelocationSearch = new TacticalLowDefenseSearch { Origin=field.FirePoints[group], Focus=field.Focus });
                if (tick < search.NextSlice) return;
                var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (!budget.TryPlan(tick)) return;
                long started = Stopwatch.GetTimestamp();
                try
                {
                    search.NextSlice = tick + 30;
                    for (int visit=0; visit<4 && search.Cursor<24; visit++)
                    {
                        IntVec3 cell = TacticalLowDefenseSearch.Candidate(search.Origin, search.Cursor++);
                        if (!FieldFloor(command, cell)) continue;
                        int cover=0;
                        foreach (var side in GenAdj.CardinalDirections)
                            if (FieldCoverAt(command, cell+side*3, search.Focus)) cover++;
                        if (cover<2) continue;
                        float score=cover*20-(float)Math.Sqrt(cell.DistanceToSquared(search.Origin));
                        int at=0; while(at<search.Scores.Count && search.Scores[at]>=score) at++;
                        if(at>=3) continue;
                        search.Candidates.Insert(at,cell); search.Scores.Insert(at,score);
                        if(search.Candidates.Count>3) { search.Candidates.RemoveAt(3); search.Scores.RemoveAt(3); }
                    }
                    if(search.Cursor<24) return;
                    if(search.Choice<search.Candidates.Count)
                    {
                        IntVec3 destination=search.Candidates[search.Choice++];
                        if(!budget.TryPath(tick)) { search.Choice--; return; }
                        if(!map.reachability.CanReach(guide.Pawn.Position,destination,PathEndMode.OnCell,TraverseParms.For(guide.Pawn,Danger.Some))) return;
                        field.FirePoints[group]=destination;
                        field.Areas[group]=new TacticalFireArea { Started=tick, RelocationAttempted=true };
                        foreach(var member in active)
                            if(field.Groups[command.Members.IndexOf(member)]==group) member.FieldDecisionAt=0;
                        return;
                    }
                    area.RelocationAttempted=true; area.RelocationSearch=null;
                }
                finally { budget.Account(tick,Stopwatch.GetTimestamp()-started); }
                return;
            }
        }
    }
}
