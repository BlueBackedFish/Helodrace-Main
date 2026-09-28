using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public sealed class JobDriver_ModifyModularWeaponAtBench : JobDriver
    {
        private const TargetIndex BenchInd = TargetIndex.A;
        private const TargetIndex WeaponInd = TargetIndex.B;

        private Thing Bench => job.GetTarget(BenchInd).Thing;
        private Thing Weapon => job.GetTarget(WeaponInd).Thing;
        private CompModularWeaponWorkbench BenchComp =>
            Bench?.TryGetComp<CompModularWeaponWorkbench>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return BenchComp?.InstalledPartsBox != null
                && pawn.equipment?.Primary == Weapon
                && pawn.Reserve(Bench, job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(BenchInd);
            this.FailOn(() => BenchComp?.InstalledPartsBox == null
                || pawn.equipment?.Primary != Weapon);

            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);
            yield return new Toil
            {
                initAction = delegate
                {
                    CompModularWeaponNode root = Weapon
                        ?.TryGetComp<CompModularWeaponNode>();
                    CompModularWeaponPartsBox box = BenchComp?.InstalledPartsBox;
                    if (root?.Props.isAssemblyRoot != true || box == null)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    pawn.pather.StopDead();
                    pawn.rotationTracker.FaceTarget(Bench);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Find.WindowStack.Add(new Dialog_ModularWeapon(root,
                        new ModularWeaponWorkshopSession(Bench,
                            new List<CompModularWeaponPartsBox> { box }, pawn)));
                },
                tickAction = delegate
                {
                    if (!Find.WindowStack.IsOpen<Dialog_ModularWeapon>())
                        ReadyForNextToil();
                },
                defaultCompleteMode = ToilCompleteMode.Never,
                handlingFacing = true
            };
        }
    }
}
