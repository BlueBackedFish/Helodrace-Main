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
                        + " goal=" + command.Goal + " opening=" + command.Plan?.Opening + " depth=" + command.Plan?.Depth
                        + " inside=" + command.Plan?.Inside + " barrier=" + command.Plan?.Barrier?.Position + " connected=" + command.HadConnectedStack
                        + " stack side=" + command.Plan?.StackSide
                        + " stack party=" + (command.Plan?.StackMembers.Count > 0 ? command.Plan.StackMembers.Count : command.Members.Count)
                        + " entry order=" + (command.Plan == null ? "none" : string.Join(",", command.Plan.EntryOrder))
                        + " furniture=" + command.FurnitureAction?.Target?.Position
                        + " pending furniture=" + command.Plan?.PendingFurniture?.Position
                        + " clearing pawn=" + command.FurnitureAction?.Worker?.thingIDNumber
                        + " observed=" + command.OpeningAction?.Enemy + " throw=" + command.OpeningAction?.Launched
                        + " opening outdoors=" + command.OpeningAction?.Outdoors
                        + " C4=" + (command.ChargeAction == null ? "none" : command.ChargeAction.Detonated ? "effects"
                            : command.ChargeAction.Charge == null ? "install" : command.ChargeAction.Withdrawing ? "trigger wait" : "withdraw")
                        + " C4 deadline=" + command.ChargeAction?.Deadline + " hammer fallback=" + command.Plan?.HammerFallback
                        + " returned=" + command.OpeningAction?.Returned + " safe=" + command.OpeningAction?.EffectsCleared
                        + " passed=" + command.Members.Count(member => member.Passed) + " entered="
                        + command.Members.Count(member => member.Entered)
                        + " outside guards=" + command.Plan?.RetainedOutside.Count
                        + " assignments arrived=" + command.Members.Count(member => member.EntryAssignmentDone)
                        + " cooperation=" + command.Link.Cooperation.Stage + " channel=" + command.Link.LastChannel
                        + " liaison=" + command.Link.Liaison?.thingIDNumber + " peer=" + command.Link.Peer?.Id
                        + " area=" + command.Link.Cooperation.Agenda?.Area(command.Id)
                        + " mission=" + command.Mission.Stage + " next=" + command.Mission.NextGoal
                        + " traversed=" + command.Mission.Traversed.Count + " blocked=" + command.Mission.FailedPlans
                        + " secured own/reported=" + command.SecuredCells.Count + "/" + command.SecuredKnowledge.Received.Count
                        + " recheck=" + command.SecuredKnowledge.Received.Count(r => r.Recheck)
                        + " ready=" + command.Link.Cooperation.LocalReady + "/" + command.Link.Cooperation.PeerReady
                        + " peer report age=" + (command.Link.Cooperation.PeerStatusAt < 0 ? -1
                            : GenTicks.TicksGame - command.Link.Cooperation.PeerStatusAt)
                        + " contact response=" + (command.ContactResponse == null ? "none" : command.ContactResponse.First.ToString())
                        + " field=" + command.FieldResponse?.Stage + " motion=" + command.FieldResponse?.Motion
                        + " focus=" + command.FieldResponse?.Focus + " moving team=" + command.FieldResponse?.MovingTeam
                        + " focus fire=" + command.FocusFire?.Stage + " target=" + command.FocusFire?.TargetId
                        + " melee threats=" + command.Contacts.Melee.Entries.Count
                        + " treatment=" + command.MedicalCare?.Job?.def.defName
                        + " patient=" + command.MedicalCare?.Patient.Pawn.thingIDNumber
                        + " medic=" + command.MedicalCare?.Helper.Pawn.thingIDNumber
                        + " contacts=[" + string.Join(";", command.Contacts.Memory.Entries.Select(contact => contact.EnemyId
                            + "@" + contact.Position + " age=" + (GenTicks.TicksGame - contact.SeenTick)
                            + (contact.Door ? " door-area=" + contact.Area : ""))) + "]"))));
        }
    }
}
