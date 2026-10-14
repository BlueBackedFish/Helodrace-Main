using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public enum TacticalFieldPostState { Unplaced, Moving, Fixed, Uncovered }
    public sealed partial class TacticalMemberCommand
    {
        public TacticalFieldPostState FieldPostState;
        public IntVec3 FieldPost = IntVec3.Invalid;
        public Thing FieldCover;
        public bool FieldHadCover;
        public int FieldCoverCheckAt;
        public int FieldPlacementFailures;
        internal void ResetFieldPost()
        {
            FieldPostState = TacticalFieldPostState.Unplaced; FieldPost = IntVec3.Invalid;
            FieldCover = null; FieldHadCover = false; FieldCoverCheckAt = 0; FieldDecisionAt = 0; FieldPlacementFailures = 0;
            FieldSupportCheckAt = 0; FieldSupportUntil = -1;
            FieldArrivalCell = IntVec3.Invalid; FieldArrivalCheckAt = 0; FieldArrivalCovered = false;
        }
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private void CaptureFieldPost(TacticalMemberCommand member, TacticalFieldResponse field, int tick)
        {
            Pawn pawn = member.Pawn;
            if (!FieldCoverAt(byPawn[pawn], pawn.Position, field.Focus))
            { RejectFieldPost(member, tick); return; }
            member.FieldPost = pawn.Position;
            Thing best = null; float score = 0;
            foreach (IntVec3 side in GenAdj.AdjacentCells)
            {
                IntVec3 cell = pawn.Position + side;
                if (!cell.InBounds(map)) continue;
                Thing cover = cell.GetCover(map);
                float block = cover?.BaseBlockChance() ?? 0;
                block *= TacticalFieldPolicy.CoverWeight(side, field.Focus - pawn.Position);
                if (block > score) { best = cover; score = block; }
            }
            member.FieldCover = best; member.FieldHadCover = best != null;
            member.FieldPostState = TacticalFieldPostState.Fixed;
            member.FieldPlacementFailures = 0;
            member.FieldCoverCheckAt = tick + 90 + pawn.thingIDNumber % 30;
            member.FieldDecisionAt = int.MaxValue;
        }
        private static void RejectFieldPost(TacticalMemberCommand member, int tick)
        {
            member.FieldPostState = TacticalFieldPostState.Uncovered;
            member.FieldPost = IntVec3.Invalid; member.FieldCover = null; member.FieldHadCover = false;
            member.FieldPlacementFailures++;
            member.FieldDecisionAt = tick + (member.FieldPlacementFailures < 2 ? 90 : 900);
        }
        private void RefreshFieldPost(TacticalMemberCommand member, TacticalFieldResponse field, int tick)
        {
            if (member.FieldPostState == TacticalFieldPostState.Moving)
            {
                if (member.Pawn.Position == member.FieldPost) CaptureFieldPost(member, field, tick);
                else if (member.Job == null || member.Pawn.CurJob != member.Job) RejectFieldPost(member, tick);
                return;
            }
            if (tick < member.FieldCoverCheckAt || member.FieldPostState == TacticalFieldPostState.Unplaced) return;
            member.FieldCoverCheckAt = tick + 90 + member.Pawn.thingIDNumber % 30;
            if (member.FieldHadCover && (member.FieldCover == null || member.FieldCover.Destroyed))
            { IntVec3 origin = member.FieldPost; member.ResetFieldPost(); member.FieldPost = origin; return; }
            // Loss of firing LOS/target does not invalidate an intact cover.
            if (member.FieldPostState == TacticalFieldPostState.Fixed && member.FieldPost.IsValid
                && !FieldCoverAt(byPawn[member.Pawn], member.FieldPost, field.Focus))
            { member.ResetFieldPost(); return; }
            if (member.FieldPost.IsValid && !member.FieldPost.Standable(map)) RejectFieldPost(member, tick);
        }
        private Job FixedFieldPostJob(TacticalMemberCommand member, TacticalSquadCommand command, int tick)
        {
            if (member.FieldPostState != TacticalFieldPostState.Fixed) return null;
            if (member.Pawn.Position != member.FieldPost)
            {
                var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (member.FieldPost.Standable(map) && budget.TryPath(tick))
                {
                    Job returning = JobMaker.MakeJob(JobDefOf.Goto, member.FieldPost);
                    returning.expiryInterval = 600; returning.checkOverrideOnExpire = false; return returning;
                }
            }
            Job hold = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            hold.expiryInterval = 600 + member.Pawn.thingIDNumber % 90; hold.checkOverrideOnExpire = false;
            return hold;
        }
    }
}
