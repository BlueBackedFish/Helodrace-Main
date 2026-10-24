using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class JobDriver_ListenRadio : JobDriver
    {
        protected Building_Radio Radio => TargetThingA as Building_Radio;
        protected virtual bool CanContinue => Radio?.CanUseNow == true
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Hearing);

        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
                && pawn.ReserveSittableOrSpot(job.targetB.Cell, job, errorOnFailed);

        protected virtual void StartRadio() { }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => !CanContinue);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil listen = ToilMaker.MakeToil("RadioRecreation");
            listen.initAction = StartRadio;
            listen.defaultCompleteMode = ToilCompleteMode.Delay;
            listen.defaultDuration = job.def.joyDuration;
            listen.handlingFacing = true;
            listen.tickIntervalAction = delta =>
            {
                pawn.rotationTracker.FaceTarget(Radio);
                pawn.GainComfortFromCellIfPossible(delta);
                JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.EndJob, 1f, Radio);
            };
            listen.AddFinishAction(() => JoyUtility.TryGainRecRoomThought(pawn));
            yield return listen;
        }
    }

    public sealed class JobDriver_ChatOnRadio : JobDriver_ListenRadio
    {
        private bool conversationStarted;

        // Clicking the channel during automatic chatter must still open its window.
        public override bool? IsSameJobAs(Job other) => other.playerForced ? false : (bool?)null;

        protected override bool CanContinue => base.CanContinue
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
            && Building_Radio.IsChannelAvailable(job.commTarget as Faction);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref conversationStarted, "radioConversationStarted", false);
        }

        protected override void StartRadio()
        {
            if (conversationStarted) return;
            conversationStarted = true;
            Faction channel = (Faction)job.commTarget;
            if (job.playerForced)
            {
                // The existing HAM patch supplies chatter inside the vanilla negotiation window.
                channel.TryOpenComms(pawn);
            }
            else
            {
                // Automatic recreation must not interrupt play with dialogue windows.
                Radio.Map.GetComponent<MapComponent_HamRadio>().Chat(pawn, channel);
            }
        }
    }
}
