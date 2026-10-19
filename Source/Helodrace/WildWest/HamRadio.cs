using RimWorld;
using Verse;

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

        public string TryReceiveMission(Pawn operatorPawn, Faction faction)
        {
            if (operatorPawn == null || operatorPawn.Map != map || !map.IsPlayerHome
                || !HamRadioUtility.IsHamFaction(faction) || faction.defeated)
                return "HD_HAM_NoMission".Translate();
            int day = Find.TickManager.TicksAbs / GenDate.TicksPerDay;
            if (!schedule.CanCheck(day)) return "HD_HAM_AlreadyChecked".Translate();
            if (schedule.CheckDay(day, Rand.Value))
            {
                IncidentDef incident = IncidentDefOf.GiveQuest_Random;
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(incident.category, map);
                parms.forced = true;
                if (incident.Worker.TryExecute(parms))
                {
                    Messages.Message("HD_HAM_MissionReceived".Translate(), operatorPawn, MessageTypeDefOf.PositiveEvent);
                    return "HD_HAM_MissionReceived".Translate();
                }
            }
            return "HD_HAM_NoMission".Translate();
        }
    }
}
