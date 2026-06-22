using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace Helodrace
{
    public class JobDriver_LubricateBelt : JobDriver
    {
        private const TargetIndex MachineInd = TargetIndex.A;
        private const TargetIndex SlushInd = TargetIndex.B;

        protected Thing Machine => job.GetTarget(MachineInd).Thing;
        protected Thing Slush => job.GetTarget(SlushInd).Thing;
        protected CompMechanicalUser UserComp => Machine.TryGetComp<CompMechanicalUser>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Machine, job, 1, -1, null, errorOnFailed)) return false;
            if (!pawn.Reserve(Slush, job, 1, 1, null, errorOnFailed)) return false;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(MachineInd);
            this.FailOnDespawnedNullOrForbidden(SlushInd);

            yield return Toils_Goto.GotoThing(SlushInd, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(SlushInd);
            yield return Toils_Haul.StartCarryThing(SlushInd, false, false, false);
            yield return Toils_Goto.GotoThing(MachineInd, PathEndMode.Touch);

            Toil lubricateToil = new Toil();
            lubricateToil.initAction = delegate
            {
                pawn.pather.StopDead();
            };
            lubricateToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Machine);
            };
            lubricateToil.defaultCompleteMode = ToilCompleteMode.Delay;
            lubricateToil.defaultDuration = 180; // 3 seconds
            lubricateToil.WithProgressBarToilDelay(MachineInd);
            lubricateToil.activeSkill = () => SkillDefOf.Crafting;
            yield return lubricateToil;

            yield return new Toil
            {
                initAction = delegate
                {
                    if (UserComp != null && Slush != null)
                    {
                        UserComp.Lubricate(Slush);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    public class WorkGiver_LubricateBelt : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t.Faction != pawn.Faction) return false;
            var user = t.TryGetComp<CompMechanicalUser>();

            if (user != null && user.WantsLubrication)
            {
                if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;

                // Find Lubricant
                Thing slush = GenClosest.ClosestThingReachable(
                    pawn.Position,
                    pawn.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn),
                    9999f,
                    x => !x.IsForbidden(pawn) && pawn.CanReserve(x) && x.TryGetComp<CompLubricant>() != null
                );

                if (slush == null) return false;
                return true;
            }

            return false;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Thing slush = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                x => !x.IsForbidden(pawn) && pawn.CanReserve(x) && x.TryGetComp<CompLubricant>() != null
            );

            if (slush != null)
            {
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_LubricateBelt"), t, slush);
                job.count = 1;
                return job;
            }
            return null;
        }
    }

    public class JobDriver_RepairBelt : JobDriver
    {
        private const TargetIndex MachineInd = TargetIndex.A;
        private const TargetIndex PartsInd = TargetIndex.B;

        protected Thing Machine => job.GetTarget(MachineInd).Thing;
        protected Thing Parts => job.GetTarget(PartsInd).Thing;
        protected CompMechanicalUser UserComp => Machine.TryGetComp<CompMechanicalUser>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Machine, job, 1, -1, null, errorOnFailed)) return false;
            if (Parts != null && !pawn.Reserve(Parts, job, 1, 5, null, errorOnFailed)) return false;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(MachineInd);
            if (Parts != null)
            {
                this.FailOnDespawnedNullOrForbidden(PartsInd);
                yield return Toils_Goto.GotoThing(PartsInd, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(PartsInd);
                yield return Toils_Haul.StartCarryThing(PartsInd, false, false, false);
            }
            
            yield return Toils_Goto.GotoThing(MachineInd, PathEndMode.Touch);

            Toil repairToil = new Toil();
            repairToil.initAction = delegate
            {
                pawn.pather.StopDead();
            };
            repairToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Machine);
            };
            repairToil.defaultCompleteMode = ToilCompleteMode.Delay;
            repairToil.defaultDuration = 300; // 5 seconds
            repairToil.WithProgressBarToilDelay(MachineInd);
            repairToil.activeSkill = () => SkillDefOf.Construction;
            yield return repairToil;

            yield return new Toil
            {
                initAction = delegate
                {
                    if (UserComp != null)
                    {
                        UserComp.RepairBelt(Parts);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    public class WorkGiver_RepairBelt : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t.Faction != pawn.Faction) return false;
            var user = t.TryGetComp<CompMechanicalUser>();

            if (user != null && user.WantsBeltRepair)
            {
                if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;

                // Find 5 Steel
                Thing parts = GenClosest.ClosestThingReachable(
                    pawn.Position,
                    pawn.Map,
                    ThingRequest.ForDef(ThingDefOf.Steel),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn),
                    9999f,
                    x => !x.IsForbidden(pawn) && pawn.CanReserve(x) && x.stackCount >= 5
                );

                if (parts == null) return false;
                return true;
            }

            return false;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Thing parts = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(ThingDefOf.Steel),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                x => !x.IsForbidden(pawn) && pawn.CanReserve(x) && x.stackCount >= 5
            );

            if (parts != null)
            {
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_RepairBelt"), t, parts);
                job.count = 5;
                return job;
            }
            return null;
        }
    }
}