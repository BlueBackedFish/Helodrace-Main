using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    // One bounded, value-only search per squad. No pawn-pair or whole-map scan.
    public sealed class TacticalLowDefenseSearch : IExposable
    {
        public const int Radius = 50, GridCount = 441, CandidateBatch = 8, CacheBatch = 96, CacheLimit = 64;
        public const int CoverGridSide = TacticalFieldDoctrine.LowMainRadius * 2 + 1;
        public const int CoverGridCount = CoverGridSide * CoverGridSide;
        public static IntVec3 CoverCell(IntVec3 origin, int index) => origin + new IntVec3(
            index % CoverGridSide - TacticalFieldDoctrine.LowMainRadius, 0,
            index / CoverGridSide - TacticalFieldDoctrine.LowMainRadius);
        public IntVec3 Origin, Forward, Focus;
        public int CurrentCover, Needed, Cursor, Choice, CacheCursor, NextSlice;
        public bool Scanned, BuildingCache;
        public List<IntVec3> Candidates = new List<IntVec3>();
        public List<float> Scores = new List<float>();
        public static int RequiredCover(int live) => Math.Min(6, Math.Max(2, (live + 1) / 2));
        public static IntVec3 Candidate(IntVec3 origin, int index) =>
            origin + new IntVec3((index % 21 - 10) * 5, 0, (index / 21 - 10) * 5);
        public void Advance(Func<IntVec3, int> cover)
        {
            for (int visit = 0; visit < CandidateBatch && Cursor < GridCount; visit++)
            {
                IntVec3 cell = Candidate(Origin, Cursor++);
                if (!TacticalFieldDoctrine.Within(Origin, cell, Radius) || cell == Origin) continue;
                int count = cover(cell);
                if (count < Needed && count < Math.Max(2, CurrentCover + 2)) continue;
                double length = Math.Sqrt(Forward.LengthHorizontalSquared);
                double advance = length == 0 ? 0 : ((double)(cell.x - Origin.x) * Forward.x + (cell.z - Origin.z) * Forward.z) / length;
                float score = count * 10 - (float)Math.Sqrt(cell.DistanceToSquared(Origin)) * .2f - (float)Math.Max(0, advance) * .4f;
                int place = 0;
                while (place < Scores.Count && Scores[place] >= score) place++;
                if (place >= 3) continue;
                Candidates.Insert(place, cell); Scores.Insert(place, score);
                if (Candidates.Count > 3) { Candidates.RemoveAt(3); Scores.RemoveAt(3); }
            }
            Scanned = Cursor == GridCount;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Origin, "origin"); Scribe_Values.Look(ref Forward, "forward"); Scribe_Values.Look(ref Focus, "focus");
            Scribe_Values.Look(ref CurrentCover, "currentCover"); Scribe_Values.Look(ref Needed, "needed");
            Scribe_Values.Look(ref Cursor, "cursor"); Scribe_Values.Look(ref Choice, "choice");
            Scribe_Values.Look(ref CacheCursor, "cacheCursor"); Scribe_Values.Look(ref NextSlice, "nextSlice");
            Scribe_Values.Look(ref Scanned, "scanned"); Scribe_Values.Look(ref BuildingCache, "buildingCache");
            Scribe_Collections.Look(ref Candidates, "candidates", LookMode.Value); Scribe_Collections.Look(ref Scores, "scores", LookMode.Value);
        }
    }
    public sealed partial class TacticalFieldResponse
    {
        public TacticalLowDefenseSearch LowDefenseSearch;
        public int LowCoverCheckAt, LowCoverCount, LowRelocations;
        public bool LowCoverReady;
        public List<IntVec3> LowCoverCells = new List<IntVec3>();
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private bool FieldCoverAt(TacticalSquadCommand command, IntVec3 cell, IntVec3 focus)
        {
            if (!FieldFloor(command, cell)) return false;
            float protection = 0;
            foreach (IntVec3 side in GenAdj.AdjacentCells)
            {
                IntVec3 neighbour = cell + side;
                if (!neighbour.InBounds(map)) continue;
                Thing cover = neighbour.GetCover(map);
                if (cover == null) continue;
                float block = cover.BaseBlockChance() * TacticalFieldPolicy.CoverWeight(side, focus - cell);
                int range = focus.DistanceToSquared(neighbour);
                if (range < 4) block *= .3333f; else if (range < 9) block *= .66666f;
                protection += (1 - protection) * block;
                if (protection >= TacticalFieldPolicy.MinimumCover) return true;
            }
            return false;
        }
        private int CountLowCover(TacticalSquadCommand command, IntVec3 centre, IntVec3 focus)
        {
            if (!FieldFloor(command, centre)) return 0;
            IntVec3 forward = TacticalFieldDoctrine.Facing(centre, focus);
            int count = 0;
            for (int side = -2; side <= 2; side++)
                for (int depth = -1; depth <= 1; depth++)
                {
                    IntVec3 cell = centre + TacticalFieldDoctrine.Offset(forward,
                        side * TacticalFieldDoctrine.LowMainRadius / 3.0, depth * TacticalFieldDoctrine.LowMainDepth * .75);
                    if (TacticalFieldDoctrine.WithinEllipse(centre, cell, forward,
                        TacticalFieldDoctrine.LowMainRadius, TacticalFieldDoctrine.LowMainDepth) && FieldCoverAt(command, cell, focus)) count++;
                }
            return count;
        }
        private void AdvanceLowDefense(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick)
        {
            if (field.LowFlank || field.Screen != null || command.MedicalCare != null) return;
            if (active.Exists(m => m.FieldPostState == TacticalFieldPostState.Fixed || m.FieldPostState == TacticalFieldPostState.Uncovered))
            { field.LowDefenseSearch = null; return; }
            var search = field.LowDefenseSearch;
            if ((search == null && tick < field.LowCoverCheckAt) || (search != null && tick < search.NextSlice)) return;
            var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryPlan(tick)) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (search == null)
                {
                    field.LowCoverCount = CountLowCover(command, field.Anchor, field.Focus);
                    int known = 0, required = TacticalLowDefenseSearch.RequiredCover(active.Count);
                    // Reuse confirmed cover before a sparse probe can needlessly move a settled squad again.
                    foreach (IntVec3 cell in field.LowCoverCells)
                        if (FieldCombatFloor(command, cell) && FieldCoverAt(command, cell, field.Focus) && ++known >= required) break;
                    field.LowCoverCount = Math.Max(field.LowCoverCount, known);
                    search = field.LowDefenseSearch = new TacticalLowDefenseSearch { Origin = field.Anchor, Forward = field.Forward,
                        Focus = field.Focus, CurrentCover = field.LowCoverCount, Needed = required,
                        Scanned = field.LowCoverCount >= required, BuildingCache = field.LowCoverCount >= required };
                    field.LowCoverCells.Clear(); field.LowCoverReady = false;
                }
                search.NextSlice = tick + 10;
                if (!search.Scanned) { search.Advance(cell => CountLowCover(command, cell, search.Focus)); return; }
                if (!search.BuildingCache)
                {
                    if (search.Choice < search.Candidates.Count)
                    {
                        IntVec3 destination = search.Candidates[search.Choice++];
                        // At most one shared reachability query per slice, after coarse scoring.
                        if (!FieldFloor(command, destination) || !map.reachability.CanReach(active[active.Count / 2].Pawn.Position,
                            destination, PathEndMode.OnCell, TraverseParms.For(active[active.Count / 2].Pawn, Danger.Some))) return;
                        field.Anchor = destination; field.Forward = TacticalFieldDoctrine.Facing(destination, search.Focus);
                        field.FirePoints[0] = destination; field.LowRelocations++;
                        foreach (var member in command.Members) member.FieldDecisionAt = 0;
                    }
                    // No better reachable point: keep the current centre instead of repeatedly replanning.
                    search.BuildingCache = true;
                }
                for (int visit = 0; visit < TacticalLowDefenseSearch.CacheBatch
                    && search.CacheCursor < TacticalLowDefenseSearch.CoverGridCount
                    && field.LowCoverCells.Count < TacticalLowDefenseSearch.CacheLimit; visit++)
                {
                    IntVec3 cell = TacticalLowDefenseSearch.CoverCell(field.Anchor, search.CacheCursor++);
                    if (TacticalFieldDoctrine.WithinEllipse(field.Anchor, cell, field.Forward,
                            TacticalFieldDoctrine.LowMainRadius, TacticalFieldDoctrine.LowMainDepth)
                        && FieldCoverAt(command, cell, field.Focus)) field.LowCoverCells.Add(cell);
                }
                if (search.CacheCursor < TacticalLowDefenseSearch.CoverGridCount && field.LowCoverCells.Count < TacticalLowDefenseSearch.CacheLimit) return;
                field.LowCoverReady = true; field.LowCoverCheckAt = tick + 1800; field.LowDefenseSearch = null;
                field.LowCoverCount = field.LowCoverCells.Count;
                foreach (var member in command.Members) member.FieldDecisionAt = 0;
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }
    }
}
