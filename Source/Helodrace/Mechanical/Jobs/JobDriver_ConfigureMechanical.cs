using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace Helodrace
{
    public class JobDriver_ConfigureMechanical : JobDriver
    {
        private const int ConfigureTicks = 60; // 1 second of work

        protected Thing Machine => job.targetA.Thing;
        protected CompMechanicalEmitter EmitterComp => Machine.TryGetComp<CompMechanicalEmitter>();
        protected CompMechanicalUser UserComp => Machine.TryGetComp<CompMechanicalUser>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Machine, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            Toil configureToil = new Toil();
            configureToil.initAction = delegate
            {
                pawn.pather.StopDead();
            };
            configureToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Machine);
            };
            configureToil.defaultCompleteMode = ToilCompleteMode.Delay;
            configureToil.defaultDuration = ConfigureTicks;
            configureToil.WithProgressBarToilDelay(TargetIndex.A);
            configureToil.activeSkill = () => SkillDefOf.Crafting;

            yield return configureToil;

            yield return new Toil
            {
                initAction = delegate
                {
                    if (EmitterComp != null)
                    {
                        EmitterComp.ApplyPendingTargetRPM();
                    }
                    if (UserComp != null)
                    {
                        UserComp.ApplyPendingGearRatio();
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    public class WorkGiver_ConfigureMechanical : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t.Faction != pawn.Faction) return false;
            
            var emitter = t.TryGetComp<CompMechanicalEmitter>();
            var user = t.TryGetComp<CompMechanicalUser>();

            if (emitter != null && emitter.WantsConfiguration)
            {
                if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;
                return true;
            }

            if (user != null && user.WantsConfiguration)
            {
                if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;
                return true;
            }

            return false;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_ConfigureMechanical"), t);
        }
    }
}