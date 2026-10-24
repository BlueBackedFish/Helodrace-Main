using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class JobDriver_ListenRadio : JobDriver_WatchTelevision
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !(TargetThingA is Building_Radio radio) || !radio.CanUseNow
                || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Hearing));
            foreach (Toil toil in base.MakeNewToils()) yield return toil;
        }
    }

    public sealed class JobDriver_ChatOnRadio : JobDriver
    {
        private bool conversationStarted;
        private Building_Radio Radio => TargetThingA as Building_Radio;
        private bool CanContinue => Radio?.CanUseNow == true
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Hearing)
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
            && Building_Radio.IsChannelAvailable(job.commTarget as Faction);

        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
                && pawn.ReserveSittableOrSpot(job.targetB.Cell, job, errorOnFailed);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref conversationStarted, "radioConversationStarted", false);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => !CanContinue);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil chat = ToilMaker.MakeToil("RadioChat");
            chat.initAction = () =>
            {
                if (conversationStarted) return;
                conversationStarted = true;
                Radio.Map.GetComponent<MapComponent_HamRadio>().Chat(pawn, (Faction)job.commTarget);
            };
            chat.defaultCompleteMode = ToilCompleteMode.Delay;
            chat.defaultDuration = job.def.joyDuration;
            chat.handlingFacing = true;
            chat.tickIntervalAction = delta =>
            {
                pawn.rotationTracker.FaceTarget(Radio);
                pawn.GainComfortFromCellIfPossible(delta);
                JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.EndJob, 1f, Radio);
            };
            chat.AddFinishAction(() => JoyUtility.TryGainRecRoomThought(pawn));
            yield return chat;
        }
    }
}
