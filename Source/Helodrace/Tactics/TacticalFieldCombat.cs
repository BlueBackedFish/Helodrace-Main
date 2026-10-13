using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed partial class MapComponent_TacticalCommands
    {
        private int FieldDepth(TacticalSquadCommand command, Pawn pawn)
        {
            var field = command.FieldResponse;
            return field.High ? TacticalFieldPolicy.HighDepth
                : field.LowFlank && field.FireGroup.Contains(command.Members.FindIndex(m => m.Pawn == pawn))
                    ? TacticalFieldDoctrine.LowFlankDepth : TacticalFieldDoctrine.LowMainDepth;
        }
        private List<IntVec3> FieldShelters(TacticalSquadCommand command, Pawn pawn, out bool ready)
        {
            var field = command.FieldResponse;
            if (field.High || field.LowFlank && field.FireGroup.Contains(command.Members.FindIndex(m => m.Pawn == pawn)))
            {
                int index = command.Members.FindIndex(m => m.Pawn == pawn);
                var area = field.Areas[field.High ? field.Groups[index] : 1];
                ready = area.Prepared; return area.Cells;
            }
            ready = field.LowCoverReady; return field.LowCoverCells;
        }
        internal bool FieldNeedsShelter(TacticalSquadCommand command, Pawn pawn)
        {
            var cells = FieldShelters(command, pawn, out bool ready);
            return ready && cells.Count > 0 && (!FieldLocusContains(command, pawn, pawn.Position)
                || !FieldCoverAt(command, pawn.Position, command.FieldResponse.Focus));
        }
        internal bool TryFieldShelter(TacticalSquadCommand command, Pawn pawn, out IntVec3 destination)
        {
            destination = IntVec3.Invalid;
            if (!FieldNeedsShelter(command, pawn)) return false;
            var cells = FieldShelters(command, pawn, out _);
            int start = (int)((uint)pawn.thingIDNumber % (uint)cells.Count);
            var best = new List<IntVec3>(3); var distances = new List<int>(3);
            for (int visit = 0; visit < Math.Min(24, cells.Count); visit++)
            {
                IntVec3 cell = cells[(start + visit) % cells.Count];
                if (!FieldLocusContains(command, pawn, cell) || !FieldCombatFloor(command, cell)
                    || !FieldCoverAt(command, cell, command.FieldResponse.Focus) || cell.GetFirstPawn(map) != null
                    || !map.pawnDestinationReservationManager.CanReserve(cell, pawn, false)) continue;
                int distance = cell.DistanceToSquared(pawn.Position), place = 0;
                while (place < distances.Count && distances[place] <= distance) place++;
                if (place >= 3) continue;
                best.Insert(place, cell); distances.Insert(place, distance);
                if (best.Count > 3) { best.RemoveAt(3); distances.RemoveAt(3); }
            }
            foreach (IntVec3 cell in best)
                if (map.reachability.CanReach(pawn.Position, cell, PathEndMode.OnCell, TraverseParms.For(pawn, Danger.Some)))
                { destination = cell; return true; }
            return false;
        }
        private void PrepareFieldCover(TacticalSquadCommand command, TacticalFieldResponse field, int tick)
        {
            int first = field.High ? 0 : 1, last = field.High ? 3 : 2;
            for (int group = first; group < last; group++)
            {
                var area = field.Areas[group];
                if (area.Prepared && tick - area.Started >= TacticalFieldPolicy.CoverRefresh)
                    field.Areas[group] = area = new TacticalFireArea { Started = tick };
                if (area.Prepared) continue;
                var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (!budget.TryPlan(tick)) return;
                long started = Stopwatch.GetTimestamp();
                try
                {
                    int width = field.High ? TacticalFieldPolicy.HighWidth : TacticalFieldDoctrine.LowFlankRadius;
                    int depth = field.High ? TacticalFieldPolicy.HighDepth : TacticalFieldDoctrine.LowFlankDepth;
                    int side = width * 2 + 1, limit = side * side;
                    IntVec3 centre = field.FirePoints[group];
                    for (int count = 0; count < TacticalFieldPolicy.CoverBatch && area.Cursor < limit
                        && area.Cells.Count < TacticalFieldPolicy.CoverLimit; count++)
                    {
                        int cursor = area.Cursor++;
                        IntVec3 cell = centre + new IntVec3(cursor % side - width, 0, cursor / side - width);
                        if (!TacticalFieldDoctrine.WithinEllipse(centre, cell, field.Forward, width, depth) || !FieldFloor(command, cell)) continue;
                        if (!area.Seed.IsValid || cell.DistanceToSquared(centre) < area.Seed.DistanceToSquared(centre)) area.Seed = cell;
                        if (FieldCoverAt(command, cell, field.Focus)) area.Cells.Add(cell);
                    }
                    area.Prepared = area.Cursor >= limit || area.Cells.Count == TacticalFieldPolicy.CoverLimit;
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
                return;
            }
        }
        private void AdvanceFieldCombatActors(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick)
        {
            command.Due = Math.Min(command.Due, tick + 10);
            int issued = 0;
            for (int visit = 0; visit < active.Count && issued < 2; visit++)
            {
                if (field.NativeCursor >= active.Count) field.NativeCursor = 0;
                var member = active[field.NativeCursor++]; var pawn = member.Pawn;
                if (!FieldCombatContext(pawn, out _)) continue;
                bool native = pawn.CurJob == member.Job && (pawn.CurJobDef == JobDefOf.Wait_Combat
                    || pawn.CurJobDef == JobDefOf.Goto || pawn.CurJobDef == JobDefOf.AttackMelee);
                if (native)
                {
                    if (pawn.CurJobDef == JobDefOf.Wait_Combat && tick < member.FieldDecisionAt) continue;
                    FieldLocus(command, pawn, out IntVec3 locus, out int width);
                    bool inside = TacticalFieldDoctrine.WithinEllipse(locus, pawn.Position, field.Forward, width + 2, FieldDepth(command, pawn) + 2);
                    if (inside && FieldCombatFloor(command, pawn.Position)
                        && FieldLocusContains(command, pawn, pawn.CurJobDef == JobDefOf.Goto ? pawn.CurJob.targetA.Cell : pawn.Position)
                        && !(pawn.CurJobDef == JobDefOf.Wait_Combat && FieldNeedsShelter(command, pawn))) continue;
                }
                if (!CanIssue(member)) continue;
                Job job = FieldNativeJob(pawn, tick, controller: true);
                if (job != null && Issue(member, job)) issued++;
            }
        }
    }
}
