using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Helodrace.Tactics;
using Helodrace.ModernWar;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public bool r7ReloadPacketsPreserved = true, r7ReloadAgreementPreserved = true;
        [DataMember] public bool r7ReloadPacketEndpointsBound = true, r7ReloadPacketsDelivered;
        [DataMember] public string[] r7CommunicationReloadEvents;
        [DataMember] public bool r7ReloadWornEquipmentPreserved = true;
    }

    // Uses the real bounded network and native save loader. Never inserts a
    // packet, agreement, or restored endpoint to make the fixture pass.
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private string reloadNetwork, reloadSquads, reloadEquipment;
        private long reloadDeliveredBaseline;
        private bool checkRestoredEndpoints;
        private List<string> communicationReloadEvents = new List<string>();
        private TacticalCommunications Network => Current.Game.GetComponent<GameComponent_TacticalCommands>().Communications;
        private List<TacticalMessage> PendingPackets => (List<TacticalMessage>)AccessTools
            .Field(typeof(TacticalCommunications), "pending").GetValue(Network);
        private TacticalSquadCommand[] ReloadSquads => map.GetComponent<MapComponent_TacticalCommands>().Commands.OrderBy(c => c.Id).ToArray();

        private void ExposeCommunicationReloadState()
        {
            Scribe_Values.Look(ref reloadNetwork, "reloadNetwork");
            Scribe_Values.Look(ref reloadEquipment, "reloadEquipment");
            Scribe_Values.Look(ref reloadSquads, "reloadSquads");
            Scribe_Values.Look(ref reloadDeliveredBaseline, "reloadDeliveredBaseline");
            Scribe_Values.Look(ref checkRestoredEndpoints, "checkRestoredEndpoints");
            Scribe_Collections.Look(ref communicationReloadEvents, "communicationReloadEvents", LookMode.Value);
            Scribe_Collections.Look(ref cooperationEvents, "drillCooperationEvents", LookMode.Value);
            Scribe_Values.Look(ref splitAt, "drillSplitAt", -1);
            Scribe_Values.Look(ref splitReportA, "drillSplitReportA");
            Scribe_Values.Look(ref splitReportB, "drillSplitReportB");
            Scribe_Values.Look(ref identificationBaseline, "drillIdentificationBaseline");
            Scribe_Values.Look(ref splitClean, "drillSplitClean", true);
            Scribe_Values.Look(ref recontactTriggered, "drillRecontactTriggered");
            Scribe_Values.Look(ref lastLoggedDrops, "drillLastLoggedDrops");
            Scribe_References.Look(ref sharedDoor, "drillSharedDoor");
        }

        private static string Agenda(TacticalCooperationAgenda a) => a == null ? "none" : a.Id + ":" + a.First
            + ":" + a.Second + ":" + a.Goal + ":" + a.Forward + ":" + a.StartAt + ":" + a.Deadline;
        private string PacketState() => Digest(string.Join("|", PendingPackets.Select(p =>
            (p.From?.Id ?? p.SavedFrom) + ":" + (p.To?.Id ?? p.SavedTo)
            + ":" + (p.From?.Owner.map ?? p.SavedMap)?.uniqueID + ":" + p.FromPawn + ":" + p.ToPawn
            + ":" + p.Sent + ":" + p.Due + ":" + p.Kind + ":" + p.Channel + ":" + Agenda(p.Agenda)
            + ":" + p.Ready + ":" + p.Finished + ":" + p.GoalSecured + ":" + p.PortalUsable + ":" + p.Opening + ":" + p.StartAt
            + ":" + p.Contact?.EnemyId + ":" + p.Contact?.Position + ":" + p.Contact?.SeenTick + ":" + p.Contact?.Origin)));
        private static string SquadState(TacticalSquadCommand c)
        {
            TacticalSquadLink l = c.Link; TacticalCooperationState a = l.Cooperation;
            return c.Id + ":" + c.Phase + ":" + c.Plan?.Opening + ":" + c.Goal + ":" + History(c) + ":" + NativeJobs(c)
                + ":" + Contacts(c) + ":" + l.Peer?.Id + ":" + l.Liaison?.thingIDNumber + ":" + l.IdentifyUntil
                + ":" + l.IdentifyStarted + ":" + l.IdentificationHolding + ":" + l.LastChannel
                + ":" + string.Join(";", l.KnownPortals.OrderBy(p => p.x).ThenBy(p => p.z))
                + ":" + string.Join(";", l.Identified.OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value))
                + ":" + Agenda(a.Agenda) + ":" + a.Stage + ":" + a.LocalReady + ":" + a.PeerReady
                + ":" + a.PeerFinished + ":" + a.PeerGoalSecured + ":" + a.PeerStatusAt + ":" + a.ConfirmedStart
                + ":" + a.NegotiationStarted + ":" + a.PeerOpening;
        }
        private string EquipmentState() => string.Join("|", ReloadSquads.Select(c => c.Id + ":" + c.Link.Unit.Organization.doctrine.defName
            + ":" + c.Link.Unit.Organization.doctrine.tacticalRadio + ":" + string.Join(";", c.Members.Select(m =>
                m.Pawn.thingIDNumber + ":radios=" + RaidTacticalRadioUtility.Radios(m.Pawn).Count() + ":parts="
                + string.Join(",", m.Pawn.apparel.WornApparel.SelectMany(a => a.TryGetComp<CompModularArmor>()?.InstalledParts
                    ?? (IReadOnlyList<InstalledModularArmorPart>)new InstalledModularArmorPart[0]).Select(p =>
                        p.part.defName + "/" + p.palsPanel?.defName + "/" + p.palsX + "/" + p.palsY
                        + "/" + p.InstalledItem?.thingIDNumber + "/" + p.InstalledItem?.HitPoints))))));
        private string SquadsState() => Digest(string.Join("|", ReloadSquads.Select(SquadState)));
        private void CommunicationReloadEvent(string message)
        {
            string entry = (GenTicks.TicksGame - started) + ":" + message;
            communicationReloadEvents.Add(entry); result.r7CommunicationReloadEvents = communicationReloadEvents.ToArray();
            Log.Message("R7 communication reload " + entry);
        }
        private void VerifyCommunicationReload()
        {
            result.r7ReloadWornEquipmentPreserved &= EquipmentState() == reloadEquipment;
            if (!result.r7ReloadWornEquipmentPreserved)
                Log.Error("R7 equipment before=" + reloadEquipment + " after=" + EquipmentState());
            result.r7ReloadPacketsPreserved &= PacketState() == reloadNetwork;
            result.r7ReloadAgreementPreserved &= SquadsState() == reloadSquads;
            result.r7ReloadJobsBound &= ReloadSquads.SelectMany(c => c.Members).Where(m => m.SavedJobId >= 0)
                .All(m => m.Job != null && m.Job == m.Pawn.CurJob && m.Job.loadID == m.SavedJobId);
            reloadPending = false; result.r7Reloads++; checkRestoredEndpoints = true;
            CommunicationReloadEvent("loaded checkpoint=" + reloadStep + " pending=" + PendingPackets.Count
                + " packets=" + result.r7ReloadPacketsPreserved + " squads=" + result.r7ReloadAgreementPreserved);
            if (!result.r7ReloadPacketsPreserved || !result.r7ReloadAgreementPreserved || !result.r7ReloadJobsBound || !result.r7ReloadWornEquipmentPreserved)
                throw new InvalidOperationException("R7 native communication reload changed packet/agenda/Job state.");
        }
        private void ApplyCommunicationReload()
        {
            // The real game component binds packet endpoints on its first tick,
            // after every map has completed FinalizeInit. Do not invoke it here.
            if (checkRestoredEndpoints)
            {
                result.r7ReloadPacketEndpointsBound &= PendingPackets.All(p => p.From != null && p.To != null
                    && p.From == p.From.Owner.SavedCommand(p.SavedFrom) && p.To == p.To.Owner.SavedCommand(p.SavedTo));
                checkRestoredEndpoints = false;
                if (!result.r7ReloadPacketEndpointsBound) throw new InvalidOperationException("R7 packet endpoints did not bind on the native first tick.");
            }
            result.r7ReloadPacketsDelivered |= reloadStep >= RequiredReloads && Network.MessagesDelivered > reloadDeliveredBaseline;
            if (reloadStep >= RequiredReloads) return;
            TacticalMessageKind kind = reloadStep == 0 ? TacticalMessageKind.Offer : TacticalMessageKind.Status;
            TacticalMessage packet = PendingPackets.FirstOrDefault(p => p.Kind == kind && p.Due > GenTicks.TicksGame);
            if (packet == null || ReloadSquads.Length != 2
                || reloadStep == 1 && !ReloadSquads.All(c => c.Link.Cooperation.Active)) return;
            reloadNetwork = PacketState(); reloadSquads = SquadsState(); reloadEquipment = EquipmentState(); reloadDeliveredBaseline = Network.MessagesDelivered;
            reloadStep++; reloadPending = true; result.caseTriggered = true;
            CommunicationReloadEvent("saving checkpoint=" + reloadStep + " kind=" + kind + " channel=" + packet.Channel
                + " pending=" + PendingPackets.Count + " from=" + packet.FromPawn + " to=" + packet.ToPawn);
            string save = "R7TacticalCheckpoint" + reloadStep;
            GameDataSaveLoader.SaveGame(save); GameDataSaveLoader.LoadGame(save);
        }
    }
}
