using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class MapComponent_HamRadio : MapComponent
    {
        private readonly HamRadioSchedule schedule = new HamRadioSchedule();
        public MapComponent_HamRadio(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref schedule.LastCheckedDay, "hamLastQuestCheckDay", -1);
        }

        public void Receive(Pawn operatorPawn, Faction faction)
        {
            if (operatorPawn == null || operatorPawn.Map != map || !HamRadioUtility.IsHamFaction(faction) || faction.defeated) return;
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
        private Building_CommsConsole Radio => job.GetTarget(TargetIndex.A).Thing as Building_CommsConsole;
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(Radio, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            return HamRadioUtility.UseToils(this, TargetIndex.A);
        }
    }
}
