using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalFieldSmoke
    {
        public Pawn Thrower;
        public Job Job;
        public Projectile Projectile;
        public IntVec3 Target, ReturnPosition;
        public int Started;
        public bool Launched, Returned;
        internal int SavedJobId = -1;
        // The screen outlives the throw/return Job while its projectile is live.
        // A later guard Job belongs to the member, not to this smoke action.
        internal Job ActiveJob => Job != null && Thrower?.CurJob == Job ? Job : null;
        internal void RestoreJob(Job ownedJob)
        {
            Job = ownedJob != null && ownedJob.loadID == SavedJobId && Thrower?.CurJob == ownedJob
                ? ownedJob : null;
        }
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long FieldSmokePlans, FieldSmokeThrows, FieldSmokeAdvances;

        private bool PlanFieldSmoke(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalFieldResponse field, int tick)
        {
            // A squad segment, independent of the selected carrier's location.
            // Only harmless HC/blind smoke passes SupportGrenade/IsSmoke.
            IntVec3 target = field.Anchor + field.Forward * 8;
            field.SmokeRetryAt = tick + 300;
            if (!target.InBounds(map) || !target.Walkable(map) || target.Roofed(map)) return false;
            TacticalMemberCommand best = null; Thing stock = null; float distance = float.MaxValue;
            foreach (TacticalMemberCommand member in active)
            {
                if (!AtPost(member) || member.Pawn.CurJob?.playerForced == true || tick < member.RetryTick) continue;
                Thing grenade = SupportGrenade(member.Pawn, true);
                if (grenade == null || !InventoryGrenadeUtility.TryFindThrowSourceFrom(member.Pawn, member.Pawn.Position,
                    target, InventoryGrenadeUtility.NormalThrowRange, out _)) continue;
                float candidate = member.Pawn.Position.DistanceToSquared(target);
                if (candidate >= distance) continue;
                best = member; stock = grenade; distance = candidate;
            }
            if (best == null) return false;
            if (!CanIssue(best)) { field.SmokeRetryAt = tick + 1; return false; }
            IntVec3 post = field.Posts[command.Members.IndexOf(best)];
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalThrow"), target, stock, post);
            job.targetQueueA = new List<LocalTargetInfo> { post, field.Focus };
            job.canUseRangedWeapon = false;
            var screen = new TacticalFieldSmoke { Thrower = best.Pawn, Job = job, Target = target,
                ReturnPosition = post, Started = tick };
            if (!Issue(best, job)) return false;
            field.Screen = screen; field.Stage = TacticalFieldStage.Screening; FieldSmokePlans++;
            return true;
        }

        private bool AdvanceFieldSmoke(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalFieldResponse field, int tick)
        {
            TacticalFieldSmoke screen = field.Screen;
            if (screen == null) { field.Stage = TacticalFieldStage.Defending; return false; }
            bool live = screen.Projectile != null && screen.Projectile.Spawned && !screen.Projectile.Destroyed;
            bool working = screen.Thrower?.Spawned == true && !screen.Thrower.Dead && !screen.Thrower.Downed
                && screen.ActiveJob != null;
            if (!screen.Launched && !working || tick - screen.Started >= 600)
            {
                field.Screen = null; field.Stage = TacticalFieldStage.Defending;
                field.NextMove = tick + 180; return false;
            }
            // Do not equate launch/fuse expiry with an established screen.
            if (!screen.Launched || live || working && !screen.Returned || !RaidSmokeUtility.SmokeAt(map, screen.Target)) return false;
            // Each new segment requests a new planned screen; spread-out rear
            // carriers throw at the shared segment, never their own flee point.
            return true;
        }

        private bool FieldSmokeLaunched(Pawn pawn, Job job, Projectile projectile)
        {
            if (projectile == null || !byPawn.TryGetValue(pawn, out TacticalSquadCommand command)) return false;
            TacticalFieldSmoke screen = command.FieldResponse?.Screen;
            if (screen?.Thrower != pawn || screen.Job != job || pawn.CurJob != job) return false;
            screen.Launched = true; screen.Projectile = projectile; FieldSmokeThrows++; Wake(pawn); return true;
        }
        private bool FieldSmokeReturned(Pawn pawn, Job job)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command)) return false;
            TacticalFieldSmoke screen = command.FieldResponse?.Screen;
            if (screen?.Thrower != pawn || screen.Job != job || pawn.CurJob != job || pawn.Position != screen.ReturnPosition) return false;
            screen.Returned = true; Wake(pawn); return true;
        }
    }
}
