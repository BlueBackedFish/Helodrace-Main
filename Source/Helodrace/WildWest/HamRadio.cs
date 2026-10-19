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
            Scribe_Values.Look(ref schedule.PreparedDay, "hamRumorDay", -1);
            Scribe_Values.Look(ref schedule.HasRumor, "hamHasRumor", false);
            Scribe_Values.Look(ref schedule.RumorConsumed, "hamRumorConsumed", false);
        }

        private int CurrentDay => (int)((Find.TickManager.TicksAbs
            + GenDate.LocalTicksOffsetFromLongitude(Find.WorldGrid.LongLatOf(map.Tile).x)) / GenDate.TicksPerDay);

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            PrepareToday();
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 == 0) PrepareToday();
        }

        private void PrepareToday()
        {
            if (!map.IsPlayerHome) return;
            int day = CurrentDay;
            if (day > schedule.PreparedDay) schedule.PrepareDay(day, Find.World.info.Seed, map.uniqueID);
        }

        public string Chat(Pawn operatorPawn, Faction faction)
        {
            string chatter = HamRadioUtility.RandomChatter();
            if (operatorPawn == null || operatorPawn.Map != map || !map.IsPlayerHome
                || !HamRadioUtility.IsHamFaction(faction) || faction.defeated)
                return chatter;
            PrepareToday();
            // Consume before generation so an unavailable quest cannot be retried
            // by repeatedly choosing chatter or switching LOW/HIGH contacts.
            if (schedule.TryConsumeRumor(CurrentDay))
            {
                IncidentDef incident = IncidentDefOf.GiveQuest_Random;
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(incident.category, map);
                parms.forced = true;
                if (incident.Worker.TryExecute(parms))
                {
                    Messages.Message("HD_HAM_MissionReceived".Translate(), operatorPawn, MessageTypeDefOf.PositiveEvent);
                    return chatter + "\n\n" + "HD_HAM_Rumor".Translate();
                }
            }
            return chatter;
        }
    }
}
