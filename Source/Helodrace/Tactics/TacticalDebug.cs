using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalDebug
    {
        [DebugAction("Helodrace/New tactical AI", "Set explicit objective", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SetObjective()
        {
            Find.CurrentMap.GetComponent<MapComponent_TacticalCommands>()?.SetObjective(UI.MouseCell());
        }
        [DebugAction("Helodrace/New tactical AI", "Inspect squad commands", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Inspect()
        {
            MapComponent_TacticalCommands service = Find.CurrentMap.GetComponent<MapComponent_TacticalCommands>();
            Find.WindowStack.Add(new Dialog_MessageBox(service == null ? "Start with -hdTacticalEngine=new."
                : "Jobs=" + service.JobsIssued + " failures=" + service.JobFailures + " plans=" + service.PlansAttempted
                    + "\n" + string.Join("\n", service.Commands.Select(command => command.Id + ": " + command.Phase
                        + " goal=" + command.Goal + " opening=" + command.Plan?.Opening + " connected=" + command.HadConnectedStack
                        + " observed=" + command.OpeningAction?.Enemy + " throw=" + command.OpeningAction?.Launched
                        + " returned=" + command.OpeningAction?.Returned + " safe=" + command.OpeningAction?.EffectsCleared
                        + " passed=" + command.Members.Count(member => member.Passed) + " entered="
                        + command.Members.Count(member => member.Entered)))));
        }
    }
}
