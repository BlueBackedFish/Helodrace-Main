using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public sealed class JobDriver_TacticalSlide : JobDriver
    {
        public bool Released { get; private set; }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil slide = new Toil
            {
                handlingFacing = true,
                initAction = delegate
                {
                    pawn.pather.StopDead();
                    TacticalHighSpeedMovementUtility.InitializeSlide(pawn, job.targetA.Cell);
                },
                defaultCompleteMode = ToilCompleteMode.Never
            };
            slide.tickAction = delegate
            {
                TacticalHighSpeedMovementUtility.SlideAdvanceResult result =
                    TacticalHighSpeedMovementUtility.AdvanceSlide(pawn);
                if (result == TacticalHighSpeedMovementUtility.SlideAdvanceResult.Completed)
                {
                    Released = true;
                    TacticalHighSpeedMovementUtility.Cancel(pawn);
                    EndJobWith(JobCondition.Succeeded);
                }
                else if (result == TacticalHighSpeedMovementUtility.SlideAdvanceResult.Blocked)
                {
                    Released = true;
                    TacticalHighSpeedMovementUtility.SwitchToNormalMovement(pawn);
                    if (pawn.jobs.curDriver == this)
                        EndJobWith(JobCondition.Incompletable);
                }
            };

            yield return slide;
        }
    }
}
