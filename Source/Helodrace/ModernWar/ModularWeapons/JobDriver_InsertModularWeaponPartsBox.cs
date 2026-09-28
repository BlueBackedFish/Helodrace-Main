using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public sealed class JobDriver_InsertModularWeaponPartsBox : JobDriver
    {
        private const TargetIndex BoxInd = TargetIndex.A;
        private const TargetIndex BenchInd = TargetIndex.B;

        private Thing Box => job.GetTarget(BoxInd).Thing;
        private Thing Bench => job.GetTarget(BenchInd).Thing;
        private CompModularWeaponWorkbench BenchComp =>
            Bench?.TryGetComp<CompModularWeaponWorkbench>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (Box?.Spawned != true || Bench?.Spawned != true
                || BenchComp == null || BenchComp.InstalledPartsBox != null)
                return false;

            job.count = 1;
            return pawn.Reserve(Box, job, 1, 1, null, errorOnFailed)
                && pawn.Reserve(Bench, job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(BoxInd);
            this.FailOnDespawnedNullOrForbidden(BenchInd);
            this.FailOn(() => BenchComp == null || BenchComp.InstalledPartsBox != null);
            this.FailOn(() => Box != null && !Box.Spawned
                && pawn.carryTracker?.CarriedThing != Box
                && BenchComp?.InstalledPartsBox?.parent != Box);

            yield return Toils_Goto.GotoThing(BoxInd, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(BoxInd, false, true, false);
            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);
            yield return Toils_General.Do(InstallCarriedBox);
        }

        private void InstallCarriedBox()
        {
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried == null || carried != Box || BenchComp == null)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            Thing box = pawn.carryTracker.innerContainer.Take(carried, 1);
            if (box != null && BenchComp.TryInstallPartsBox(box)) return;

            if (box != null
                && pawn.carryTracker.innerContainer.TryAdd(box, false))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (box != null && pawn.Spawned)
                GenPlace.TryPlaceThing(box, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            EndJobWith(JobCondition.Incompletable);
        }
    }
}
