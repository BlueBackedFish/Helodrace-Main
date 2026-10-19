using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class Building_HamRadio : Building_CommsConsole
    {
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn)
        {
            // Keep component-specific interactions, including Biotech boss calls.
            foreach (ThingComp comp in AllComps)
                foreach (FloatMenuOption option in comp.CompFloatMenuOptions(pawn)) yield return option;

            string label = "HD_HAM_Use".Translate();
            if (!CanUseCommsNow)
            {
                yield return new FloatMenuOption(label + ": " + "HD_HAM_NoSignal".Translate(), null);
                yield break;
            }
            if (!pawn.CanReserveAndReach(this, PathEndMode.InteractionCell, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
                yield break;
            }
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
            {
                yield return new FloatMenuOption(label + ": " + "HD_Telegraph_OperatorUnavailable".Translate(), null);
                yield break;
            }
            yield return new FloatMenuOption(label, () => pawn.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_UseHamRadio"), this), JobTag.Misc));
        }
    }

    public sealed class MapComponent_HamRadio : MapComponent
    {
        private readonly HamRadioSchedule schedule = new HamRadioSchedule();
        public MapComponent_HamRadio(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref schedule.LastCheckedDay, "hamLastQuestCheckDay", -1);
        }

        public void Receive(Pawn operatorPawn)
        {
            int day = Find.TickManager.TicksAbs / GenDate.TicksPerDay;
            if (map.IsPlayerHome && schedule.CanCheck(day) && schedule.CheckDay(day, Rand.Value))
            {
                IncidentDef incident = IncidentDefOf.GiveQuest_Random;
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(incident.category, map);
                parms.forced = true;
                if (incident.Worker.TryExecute(parms))
                {
                    Messages.Message("HD_HAM_MissionReceived".Translate(), operatorPawn, MessageTypeDefOf.PositiveEvent);
                    return;
                }
            }
            Messages.Message(("HD_HAM_Chatter" + Rand.RangeInclusive(1, 6)).Translate(),
                operatorPawn, MessageTypeDefOf.NeutralEvent);
        }
    }

    public sealed class JobDriver_UseHamRadio : JobDriver
    {
        private Building_HamRadio Radio => job.GetTarget(TargetIndex.A).Thing as Building_HamRadio;
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(Radio, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Radio == null || !Radio.CanUseCommsNow);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_General.WaitWith(TargetIndex.A, 180, true);
            yield return new Toil
            {
                initAction = () => pawn.Map.GetComponent<MapComponent_HamRadio>().Receive(pawn),
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
