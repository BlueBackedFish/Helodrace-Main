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
        internal bool FieldCoveredCandidate(TacticalSquadCommand command, Pawn pawn, IntVec3 cell) =>
            FieldCombatFloor(command, cell) && FieldLocusContains(command, pawn, cell)
            && FieldCoverAt(command, cell, command.FieldResponse.Focus);
        private int FieldDepth(TacticalSquadCommand command, Pawn pawn)
        {
            var field = command.FieldResponse;
            if (Hammer(command, pawn)) return field.FlankDepth;
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
            var best = new List<IntVec3>(3); var distances = new List<float>(3);
            for (int visit = 0; visit < Math.Min(24, cells.Count); visit++)
            {
                IntVec3 cell = cells[(start + visit) % cells.Count];
                if (!FieldLocusContains(command, pawn, cell) || !FieldCombatFloor(command, cell)
                    || !FieldCoverAt(command, cell, command.FieldResponse.Focus) || cell.GetFirstPawn(map) != null
                    || !map.pawnDestinationReservationManager.CanReserve(cell, pawn, false)) continue;
                float spacing = FieldSpacingCost(command, pawn, cell, true);
                if (spacing == float.MaxValue) continue;
                float distance = (float)Math.Sqrt(cell.DistanceToSquared(pawn.Position)) + spacing; int place = 0;
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
                if (area.Prepared) continue;
                var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (!budget.TryPlan(tick)) return;
                long started = Stopwatch.GetTimestamp();
                try
                {
                    bool flank = group == 1 && field.Maneuver != null && field.Maneuver.Phase != TacticalManeuverPhase.Planning;
                    int width = field.High && !flank ? TacticalFieldPolicy.HighWidth : field.FlankWidth;
                    int depth = field.High && !flank ? TacticalFieldPolicy.HighDepth : field.FlankDepth;
                    IntVec3 centre = field.FirePoints[group];
                    if (area.CoverPending == null)
                    {
                        area.Cells.Clear(); area.Cursor = 0; area.Seed = centre;
                        area.CoverPending = new Queue<IntVec3>(); area.CoverVisited = new HashSet<IntVec3> { centre };
                        if (FieldFloor(command, centre)) area.CoverPending.Enqueue(centre);
                    }
                    for (int count = 0; count < TacticalFieldPolicy.CoverBatch && area.CoverPending.Count > 0
                        && area.Cells.Count < TacticalFieldPolicy.CoverLimit; count++)
                    {
                        IntVec3 cell = area.CoverPending.Dequeue(); area.Cursor++;
                        if (FieldCoverAt(command, cell, field.Focus)) area.Cells.Add(cell);
                        foreach (IntVec3 direction in GenAdj.CardinalDirections)
                        {
                            IntVec3 next = cell + direction;
                            if (TacticalFieldDoctrine.WithinEllipse(centre, next, field.Forward, width, depth)
                                && area.CoverVisited.Add(next) && FieldFloor(command, next)) area.CoverPending.Enqueue(next);
                        }
                    }
                    area.Prepared = area.CoverPending.Count == 0 || area.Cells.Count == TacticalFieldPolicy.CoverLimit;
                    if (area.Prepared) { area.CoverPending = null; area.CoverVisited = null; }
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
                return;
            }
        }
        private void AdvanceFieldCombatActors(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick)
        {
            command.Due = Math.Min(command.Due, tick + 10);
            PlanFieldGrenade(command, active, field, tick);
            int issued = 0;
            for (int visit = 0; visit < active.Count && issued < 2; visit++)
            {
                if (field.NativeCursor >= active.Count) field.NativeCursor = 0;
                var member = active[field.NativeCursor++]; var pawn = member.Pawn;
                if (!FieldCombatContext(pawn, out _)) continue;
                bool anvil = !field.FireGroup.Contains(command.Members.IndexOf(member));
                if (anvil) RefreshFieldPost(member, field, tick);
                if (PrepareFieldMode(command, member)) issued++;
                bool native = pawn.CurJob == member.Job && (pawn.CurJobDef == JobDefOf.Wait_Combat
                    || pawn.CurJobDef == JobDefOf.Goto || pawn.CurJobDef == JobDefOf.AttackMelee);
                if (native)
                {
                    // A valid destination remains valid while the native path
                    // detours outside the locus. Do not restart it every slice.
                    if (pawn.CurJobDef == JobDefOf.Goto && (!anvil || member.FieldPostState == TacticalFieldPostState.Unplaced
                        || member.FieldPostState == TacticalFieldPostState.Moving || pawn.CurJob.targetA.Cell == member.FieldPost)
                        && FieldCombatFloor(command, pawn.CurJob.targetA.Cell)
                        && FieldLocusContains(command, pawn, pawn.CurJob.targetA.Cell)) continue;
                    if (!anvil && field.Maneuver != null)
                    {
                        if (pawn.CurJobDef == JobDefOf.Goto && TacticalFieldDoctrine.WithinEllipse(field.FirePoints[1], pawn.CurJob.targetA.Cell,
                            field.Forward, field.FlankWidth, field.FlankDepth)) continue;
                        if (pawn.CurJobDef == JobDefOf.Wait_Combat && (FieldAtWaypoint(command,member)
                            || !FieldHammerMoving(command,member) && (tick<member.FieldDecisionAt || FieldCoverAt(command,pawn.Position,field.Focus)))) continue;
                    }
                    if (anvil && member.FieldPostState == TacticalFieldPostState.Fixed)
                    {
                        if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == member.FieldPost) continue;
                        if (pawn.CurJobDef == JobDefOf.Wait_Combat && pawn.Position == member.FieldPost) continue;
                    }
                    if (pawn.CurJobDef == JobDefOf.Wait_Combat && tick < member.FieldDecisionAt) continue;
                    FieldLocus(command, pawn, out IntVec3 locus, out int width);
                    bool inside = TacticalFieldDoctrine.WithinEllipse(locus, pawn.Position, field.Forward, width + 2, FieldDepth(command, pawn) + 2);
                    if (anvil && member.FieldPostState == TacticalFieldPostState.Fixed && inside && FieldCombatFloor(command, pawn.Position)
                        && FieldLocusContains(command, pawn, pawn.CurJobDef == JobDefOf.Goto ? pawn.CurJob.targetA.Cell : pawn.Position)
                        && !(pawn.CurJobDef == JobDefOf.Wait_Combat && FieldNeedsShelter(command, pawn))) continue;
                }
                if (pawn.stances.FullBodyBusy && (anvil && member.FieldPostState == TacticalFieldPostState.Fixed
                    || !anvil && !FieldHammerMoving(command,member))) continue;
                // One job source: the native override invokes FieldNativeJob
                // through the existing safety-first tree adapter. It preserves
                // busy stances and avoids controller/thinker competing starts.
                Job before = pawn.CurJob;
                pawn.jobs.CheckForJobOverride();
                if (pawn.CurJob != before) issued++;
            }
        }
    }
}
