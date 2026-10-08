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
                        + command.Members.Count(member => member.Entered)
                        + " outside guards=" + command.Plan?.RetainedOutside.Count
                        + " assignments arrived=" + command.Members.Count(member => member.EntryAssignmentDone)
                        + " cooperation=" + command.Link.Cooperation.Stage + " channel=" + command.Link.LastChannel
                        + " liaison=" + command.Link.Liaison?.thingIDNumber + " peer=" + command.Link.Peer?.Id
                        + " area=" + command.Link.Cooperation.Agenda?.Area(command.Id)
                        + " ready=" + command.Link.Cooperation.LocalReady + "/" + command.Link.Cooperation.PeerReady
                        + " peer report age=" + (command.Link.Cooperation.PeerStatusAt < 0 ? -1
                            : GenTicks.TicksGame - command.Link.Cooperation.PeerStatusAt)
                        + " contact response=" + (command.ContactResponse == null ? "none" : command.ContactResponse.First.ToString())
                        + " field=" + command.FieldResponse?.Stage + " motion=" + command.FieldResponse?.Motion
                        + " focus=" + command.FieldResponse?.Focus + " moving team=" + command.FieldResponse?.MovingTeam
                        + " contacts=[" + string.Join(";", command.Contacts.Memory.Entries.Select(contact => contact.EnemyId
                            + "@" + contact.Position + " age=" + (GenTicks.TicksGame - contact.SeenTick)
                            + (contact.Door ? " door-area=" + contact.Area : ""))) + "]"))));
        }
    }
}
