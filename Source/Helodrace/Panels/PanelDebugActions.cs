using LudeonTK;
using RimWorld;
using Verse;

namespace Helodrace.Panels
{
    // Developer-only tools for checking room electromagnetic shielding.
    public static class PanelDebugActions
    {
        private const int FlareTicks = GenDate.TicksPerHour * 6;
        private static GameConditionDef SolarFlare => DefDatabase<GameConditionDef>.GetNamed("SolarFlare");

        [DebugAction("Helodrace/Wall panels", "Start solar flare (6 h)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void StartSolarFlare()
        {
            Map map = Find.CurrentMap;
            GameCondition existing = map.gameConditionManager.GetActiveCondition(SolarFlare);
            if (existing != null)
            {
                existing.TicksLeft = FlareTicks;
                Messages.Message("Solar flare extended to 6 hours.", MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(SolarFlare, FlareTicks));
            Messages.Message("Solar flare started for 6 hours.", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Helodrace/Wall panels", "End solar flare", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void EndSolarFlare()
        {
            GameCondition existing = Find.CurrentMap.gameConditionManager.GetActiveCondition(SolarFlare);
            if (existing == null)
            {
                Messages.Message("No solar flare on this map.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            existing.End();
            Messages.Message("Solar flare ended.", MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
