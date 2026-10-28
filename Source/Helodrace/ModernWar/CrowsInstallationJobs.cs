using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class WorkGiver_InstallCrowsWeapon : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(CrowsDefOf.HD_Building_CrowsII);
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false) => JobOnThing(pawn, thing, forced) != null;
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Building_Crows turret) || !turret.InstallationPending || thing.Faction != pawn.Faction
                || thing.IsForbidden(pawn) || thing.IsBurning() || !pawn.CanReserveAndReach(thing, PathEndMode.Touch, Danger.Some)
                || pawn.skills.GetSkill(SkillDefOf.Construction).Level < 6) return null;
            if (turret.MaterialsReady) return JobMaker.MakeJob(CrowsDefOf.HD_InstallCrowsWeapon, turret);
            foreach (ThingDefCountClass cost in turret.RequestedWeapon.costList)
            {
                int missing = turret.MissingMaterial(cost.thingDef);
                if (missing <= 0) continue;
                Thing material = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(cost.thingDef),
                    PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                    t => !t.IsForbidden(pawn) && pawn.CanReserve(t));
                if (material == null) continue;
                Job job = JobMaker.MakeJob(CrowsDefOf.HD_HaulCrowsMountMaterial, turret, material);
                job.count = System.Math.Min(missing, material.stackCount);
                return job;
            }
            return null;
        }
    }

    public sealed class JobDriver_HaulCrowsMountMaterial : JobDriver
    {
        private Building_Crows Turret => TargetThingA as Building_Crows;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
            && pawn.Reserve(job.targetB, job, 1, job.count, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOn(() => Turret?.InstallationPending != true || Turret.Faction != pawn.Faction);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                Thing material = pawn.carryTracker.CarriedThing;
                if (material != null) Turret.AcceptMountingMaterial(pawn.carryTracker.innerContainer, material, material.stackCount);
                // A player can change the selection during the delivery. Put
                // any now-unneeded remainder back on the map instead of leaving
                // the builder carrying it into the next construction job.
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing remainder);
            });
        }
    }

    public sealed class JobDriver_InstallCrowsWeapon : JobDriver
    {
        private Building_Crows Turret => TargetThingA as Building_Crows;
        private CrowsWeaponDef selection;
        public override void ExposeData() { base.ExposeData(); Scribe_Defs.Look(ref selection, "crowsWeaponSelection"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => Turret?.MaterialsReady != true || Turret.Faction != pawn.Faction
                || selection != null && Turret.RequestedWeapon != selection);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil install = ToilMaker.MakeToil("InstallCrowsWeapon");
            install.initAction = () => selection = Turret.RequestedWeapon;
            install.defaultCompleteMode = ToilCompleteMode.Never;
            install.tickIntervalAction = delta =>
            {
                pawn.rotationTracker.FaceTarget(Turret);
                if (Turret.AddMountingWork(pawn.GetStatValue(StatDefOf.ConstructionSpeed) * delta)) EndJobWith(JobCondition.Succeeded);
            };
            yield return install.WithProgressBar(TargetIndex.A, () => Turret.InstallationProgress);
        }
    }
}
