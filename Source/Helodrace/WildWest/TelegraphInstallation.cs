using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class FloatMenuOptionProvider_TelegraphInstallation : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn actor = context.FirstSelectedPawn;
            if (actor?.Faction != Faction.OfPlayer || !MapComponent_TelegraphNetwork.IsInstallationTrader(clickedPawn, actor.Map)) return null;
            string label = "HD_Telegraph_RequestInstallation".Translate(TelegraphNetworkRules.InstallationCost);
            string reason = actor.Map.GetComponent<MapComponent_TelegraphNetwork>().InstallationRejection(clickedPawn);
            if (reason != null) return new FloatMenuOption(label + ": " + reason, null);
            if (!actor.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
            if (!actor.health.capacities.CapableOf(PawnCapacityDefOf.Talking) || actor.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
                return new FloatMenuOption(label + ": " + "Incapable".Translate(), null);
            return new FloatMenuOption(label, () => actor.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_RequestTelegraphInstallation"), clickedPawn), JobTag.Misc));
        }
    }

    public sealed class JobDriver_RequestTelegraphInstallation : JobDriver
    {
        private Pawn Trader => job.GetTarget(TargetIndex.A).Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(Trader, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !MapComponent_TelegraphNetwork.IsInstallationTrader(Trader, pawn.Map));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return new Toil
            {
                initAction = () =>
                {
                    Pawn trader = Trader;
                    Pawn negotiator = pawn;
                    MapComponent_TelegraphNetwork network = pawn.Map.GetComponent<MapComponent_TelegraphNetwork>();
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        "HD_Telegraph_InstallationConfirm".Translate(TelegraphNetworkRules.InstallationCost),
                        "Confirm".Translate(), () =>
                        {
                            if (!network.TryRequestInstallation(trader, negotiator, out string reason))
                                Messages.Message(reason, MessageTypeDefOf.RejectInput);
                        }, "CancelButton".Translate(), null));
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
