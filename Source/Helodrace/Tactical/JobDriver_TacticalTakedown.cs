using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public sealed class JobDriver_TacticalTakedown : JobDriver
    {
        private Pawn Victim => job.targetA.Thing as Pawn;
        private BodyPartRecord SelectedPart => Victim != null && job.count >= 0
            && job.count < Victim.RaceProps.body.AllParts.Count
                ? Victim.RaceProps.body.AllParts[job.count] : null;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !TacticalTakedownUtility.CanContinueApproach(pawn, Victim, SelectedPart));
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            // Vanilla pathing ends the job if the route becomes unreachable.
            Toil approach = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            approach.AddPreTickAction(() =>
            {
                if (pawn.stances?.curStance?.StanceBusy == true
                    || (!pawn.pather.Moving && !pawn.CanReachImmediate(Victim, PathEndMode.Touch)))
                    EndJobWith(JobCondition.Incompletable);
            });
            yield return approach;
            Toil strike = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            strike.initAction = () =>
            {
                pawn.pather.StopDead();
                bool succeeded = TacticalTakedownUtility.CompleteTakedown(pawn, Victim, SelectedPart,
                    job.def.defName == "HD_TacticalSubdue"
                        ? TacticalTakedownMode.Subdue : TacticalTakedownMode.InstantKill);
                if (!succeeded) EndJobWith(JobCondition.Incompletable);
            };
            yield return strike;
        }
    }
}
