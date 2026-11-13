using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.SquadAI
{
    // The squad AI only moves duty focus cells; these job givers and vanilla's
    // JobGiver_AIDefendPoint (HD_SquadHold) do the per-pawn work. The classes
    // stay loadable under every engine because the duties are saved on pawns.
    public static class SquadDuties
    {
        public static DutyDef Hold => DefDatabase<DutyDef>.GetNamed("HD_SquadHold");
        public static DutyDef Move => DefDatabase<DutyDef>.GetNamed("HD_SquadMove");
        public static bool Owned(DutyDef def) => def != null && (def.defName == "HD_SquadHold" || def.defName == "HD_SquadMove");
    }

    // Answers an enemy that is already close instead of walking past it.
    public sealed class JobGiver_SquadCloseThreat : ThinkNode_JobGiver
    {
        public float radius = 7f;
        public override ThinkNode DeepCopy(bool resolve = true)
        {
            var copy = (JobGiver_SquadCloseThreat)base.DeepCopy(resolve);
            copy.radius = radius; return copy;
        }
        protected override Job TryGiveJob(Pawn pawn)
        {
            Thing target = null; float best = radius * radius;
            foreach (IAttackTarget candidate in pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn))
            {
                Thing thing = candidate.Thing;
                if (candidate.ThreatDisabled(pawn) || !SquadContacts.Threat(thing, pawn)) continue;
                float distance = thing.Position.DistanceToSquared(pawn.Position);
                if (distance < best && GenSight.LineOfSightToThing(pawn.Position, thing, pawn.Map, true)) { best = distance; target = thing; }
            }
            if (target == null) return null;
            Verb verb = pawn.TryGetAttackVerb(target, !pawn.IsColonist, false);
            if (verb == null) return null;
            if (verb.IsMeleeAttack)
            {
                Job melee = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                melee.expiryInterval = 120; melee.checkOverrideOnExpire = true; return melee;
            }
            if (!verb.CanHitTarget(target)) return null;
            Job job = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            job.maxNumStaticAttacks = 2; job.endIfCantShootTargetFromCurPos = true;
            job.expiryInterval = 90; job.checkOverrideOnExpire = true;
            return job;
        }
    }

    // Walks back to the post first, so vanilla's shoot-from-here shortcut
    // cannot keep a squadmate far from the spacing the plan gave him.
    public sealed class JobGiver_SquadReturnToPost : ThinkNode_JobGiver
    {
        public float slack = 3f;
        public override ThinkNode DeepCopy(bool resolve = true)
        {
            var copy = (JobGiver_SquadReturnToPost)base.DeepCopy(resolve);
            copy.slack = slack; return copy;
        }
        protected override Job TryGiveJob(Pawn pawn)
        {
            PawnDuty duty = pawn.mindState.duty;
            if (duty == null || !duty.focus.IsValid || pawn.Position.InHorDistOf(duty.focus.Cell, duty.radius + slack)) return null;
            return SquadJobs.Goto(pawn, duty.focus.Cell, duty.locomotion);
        }
    }

    // Moves to the post given by an HD_SquadMove duty.
    public sealed class JobGiver_SquadMove : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            PawnDuty duty = pawn.mindState.duty;
            if (duty == null || !duty.focus.IsValid || pawn.Position.InHorDistOf(duty.focus.Cell, duty.radius)) return null;
            return SquadJobs.Goto(pawn, duty.focus.Cell, duty.locomotion);
        }
    }

    // At the post with nothing to shoot: wait in combat stance there.
    public sealed class JobGiver_SquadHoldPost : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            PawnDuty duty = pawn.mindState.duty;
            if (duty == null || !duty.focus.IsValid) return null;
            if (!pawn.Position.InHorDistOf(duty.focus.Cell, duty.radius))
                return SquadJobs.Goto(pawn, duty.focus.Cell, duty.locomotion);
            Job wait = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            wait.expiryInterval = 120; wait.checkOverrideOnExpire = true;
            return wait;
        }
    }

    public static class SquadJobs
    {
        public static Job Goto(Pawn pawn, IntVec3 cell, LocomotionUrgency urgency)
        {
            if (!pawn.Map.reachability.CanReach(pawn.Position, cell, PathEndMode.OnCell, TraverseParms.For(pawn, Danger.Deadly)))
                return null;
            Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            job.locomotionUrgency = urgency == LocomotionUrgency.None ? LocomotionUrgency.Jog : urgency;
            job.expiryInterval = 180; job.checkOverrideOnExpire = true;
            return job;
        }
    }
}
