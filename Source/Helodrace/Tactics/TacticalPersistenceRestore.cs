using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using Verse;

namespace Helodrace.Tactics
{
    public sealed partial class GameComponent_TacticalCommands
    {
        private bool packetsNeedRestore;
        internal void TrackRestored(TacticalSquadCommand command)
        {
            if (!commands.Contains(command)) commands.Add(command);
        }
        public override void ExposeData()
        {
            Communications.ExposeData();
            Scribe_Values.Look(ref cursor, "tacticalCursor");
            Scribe_Values.Look(ref Advances, "tacticalAdvances");
            Scribe_Values.Look(ref BudgetStops, "tacticalBudgetStops");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) packetsNeedRestore = true;
        }
        private void RestorePackets()
        {
            if (!packetsNeedRestore) return;
            // All map FinalizeInit callbacks run before the first game tick.
            // This only reconnects the globally capped saved message queue.
            packetsNeedRestore = false; Communications.RestorePackets();
        }
    }

    public sealed partial class TacticalCommunications
    {
        internal void ExposeData()
        {
            Scribe_Collections.Look(ref pending, "tacticalPackets", LookMode.Deep);
            Scribe_Values.Look(ref sourceCursor, "tacticalSourceCursor");
            Scribe_Values.Look(ref deliveryCursor, "tacticalDeliveryCursor");
            Scribe_Values.Look(ref nextPair, "tacticalNextPair");
            Scribe_Values.Look(ref MessagesSent, "tacticalMessagesSent");
            Scribe_Values.Look(ref MessagesDelivered, "tacticalMessagesDelivered");
            Scribe_Values.Look(ref MessagesDropped, "tacticalMessagesDropped");
            Scribe_Values.Look(ref PairChecks, "tacticalPairChecks");
            Scribe_Values.Look(ref ReportsReceived, "tacticalReportsReceived");
            Scribe_Values.Look(ref AgreementsConfirmed, "tacticalAgreementsConfirmed");
            Scribe_Values.Look(ref StartMessages, "tacticalStartMessages");
            Scribe_Values.Look(ref Identifications, "tacticalIdentifications");
            Scribe_Values.Look(ref OperatorChanges, "tacticalOperatorChanges");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pending == null) pending = new List<TacticalMessage>();
        }
        internal void RestorePackets()
        {
            foreach (TacticalMessage message in pending)
            {
                MapComponent_TacticalCommands owner = message.SavedMap?.GetComponent<MapComponent_TacticalCommands>();
                message.From = owner?.SavedCommand(message.SavedFrom);
                message.To = owner?.SavedCommand(message.SavedTo);
            }
            MessagesDropped += pending.RemoveAll(m => m.From == null || m.To == null);
        }
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private List<TacticalSquadCommand> savedCommands;
        private List<TacticalLocalPlan> savedOpenings;
        private Dictionary<IntVec3, string> savedClaims, savedLeases;
        public long RestoredCommands;
        internal TacticalSquadCommand SavedCommand(string id) => id != null && squads.TryGetValue(id, out TacticalSquadCommand command)
            ? command : null;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref explicitGoal, "tacticalExplicitGoal", IntVec3.Invalid);
            Scribe_Values.Look(ref automaticGoal, "tacticalAutomaticGoal", IntVec3.Invalid);
            Scribe_Values.Look(ref goalRetry, "tacticalGoalRetry");
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedCommands = squads.Values.Where(c => !c.Terminal).ToList();
                savedOpenings = knownOpenings.ToList();
                savedClaims = claims.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
                savedLeases = leases.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
            }
            Scribe_Collections.Look(ref savedCommands, "tacticalCommands", LookMode.Deep);
            Scribe_Collections.Look(ref savedOpenings, "tacticalKnownOpenings", LookMode.Deep);
            Scribe_Collections.Look(ref savedClaims, "tacticalClaims", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref savedLeases, "tacticalLeases", LookMode.Value, LookMode.Value);
            ExposeCounters();
            if (Scribe.mode == LoadSaveMode.Saving)
            { savedCommands = null; savedOpenings = null; savedClaims = savedLeases = null; }
        }
        public override void FinalizeInit()
        {
            RestoreSavedCommands();
        }
        internal void RestoreSavedCommands()
        {
            if (savedCommands == null) return;
            GameComponent_TacticalCommands scheduler = Current.Game.GetComponent<GameComponent_TacticalCommands>();
            foreach (TacticalSquadCommand command in savedCommands)
            {
                RaidTacticalUnit unit = command.Members.Where(m => m.Pawn != null).Select(m => RaidTacticalUnit.ForPawn(m.Pawn))
                    .FirstOrDefault(u => u?.Id == command.Id);
                if (unit == null || command.Terminal) continue;
                command.Owner = this; command.Link.Unit = unit;
                foreach (TacticalMemberCommand member in command.Members)
                {
                    if (member.Pawn == null) continue;
                    byPawn[member.Pawn] = command;
                    // The native tracker owns and serializes the only Job.
                    // Rebind its saved identity, never clone or restart it.
                    member.Job = member.Pawn.CurJob?.loadID == member.SavedJobId ? member.Pawn.CurJob : null;
                }
                if (command.ContactResponse != null) command.ContactResponse.Plan = command.Plan;
                if (command.FieldResponse?.Screen != null)
                    command.FieldResponse.Screen.Job = command.Members.Find(m => m.Pawn == command.FieldResponse.Screen.Thrower)?.Job;
                if (command.ChargeAction != null)
                {
                    command.ChargeAction.Charge = (command.ChargeAction.SavedCharge as ThingWithComps)?.TryGetComp<CompInstalledBreachCharge>();
                    command.ChargeAction.Installation = command.Members.Find(m => m.Pawn == command.ChargeAction.Installer)?.Job;
                }
                if (command.MedicalCare != null)
                {
                    TacticalMedicalCare care = command.MedicalCare;
                    care.Patient = command.Members.Find(m => m.Pawn == care.SavedPatient);
                    care.Helper = command.Members.Find(m => m.Pawn == care.SavedHelper);
                    if (care.Patient == null || care.Helper == null) command.MedicalCare = null;
                    else care.Job = care.Helper.Job;
                }
                command.MedicalCandidate = command.Members.Find(m => m.Pawn == command.SavedMedicalCandidate);
                squads[command.Id] = command; scheduler.TrackRestored(command); RestoredCommands++;
            }
            foreach (TacticalSquadCommand command in squads.Values)
                command.Link.Peer = SavedCommand(command.Link.SavedPeer);
            RestoreClaims(savedClaims, claims); RestoreClaims(savedLeases, leases);
            if (squads.Count > 0 && savedOpenings != null) knownOpenings.AddRange(savedOpenings);
            savedCommands = null; savedOpenings = null; savedClaims = savedLeases = null;
        }
        private void RestoreClaims(Dictionary<IntVec3, string> saved, Dictionary<IntVec3, TacticalSquadCommand> destination)
        {
            if (saved == null) return;
            foreach (var pair in saved)
                if (pair.Key.InBounds(map) && squads.TryGetValue(pair.Value, out TacticalSquadCommand command))
                    destination[pair.Key] = command;
        }
        private void ExposeCounters()
        {
            Scribe_Values.Look(ref ToolRecoveriesStarted, "ToolRecoveriesStartedCounter");
            Scribe_Values.Look(ref ToolRecoveriesCompleted, "ToolRecoveriesCompletedCounter");
            Scribe_Values.Look(ref CutterJobsStarted, "CutterJobsStartedCounter");
            Scribe_Values.Look(ref ChargesInstalled, "ChargesInstalledCounter");
            Scribe_Values.Look(ref ChargeDetonations, "ChargeDetonationsCounter");
            Scribe_Values.Look(ref ChargeOperatorTransfers, "ChargeOperatorTransfersCounter");
            Scribe_Values.Look(ref ChargeWaits, "ChargeWaitsCounter");
            Scribe_Values.Look(ref JobsIssued, "JobsIssuedCounter");
            Scribe_Values.Look(ref JobFailures, "JobFailuresCounter");
            Scribe_Values.Look(ref PlansAttempted, "PlansAttemptedCounter");
            Scribe_Values.Look(ref PlansBuilt, "PlansBuiltCounter");
            Scribe_Values.Look(ref BusyOpeningFallbacks, "BusyOpeningFallbacksCounter");
            Scribe_Values.Look(ref ContactScans, "ContactScansCounter");
            Scribe_Values.Look(ref ContactCandidates, "ContactCandidatesCounter");
            Scribe_Values.Look(ref ContactsSeen, "ContactsSeenCounter");
            Scribe_Values.Look(ref DoorContactsSeen, "DoorContactsSeenCounter");
            Scribe_Values.Look(ref ContactResponses, "ContactResponsesCounter");
            Scribe_Values.Look(ref RearResponses, "RearResponsesCounter");
            Scribe_Values.Look(ref OpposedResponses, "OpposedResponsesCounter");
            Scribe_Values.Look(ref DoorResponses, "DoorResponsesCounter");
            Scribe_Values.Look(ref ContactResumes, "ContactResumesCounter");
            Scribe_Values.Look(ref ContactGuardJobs, "ContactGuardJobsCounter");
            Scribe_Values.Look(ref ContactPostCandidates, "ContactPostCandidatesCounter");
            Scribe_Values.Look(ref ContactShots, "ContactShotsCounter");
            Scribe_Values.Look(ref FieldShots, "FieldShotsCounter");
            Scribe_Values.Look(ref CooperationWaits, "CooperationWaitsCounter");
            Scribe_Values.Look(ref CooperationStarts, "CooperationStartsCounter");
            Scribe_Values.Look(ref AllocatedAreasSecured, "AllocatedAreasSecuredCounter");
            Scribe_Values.Look(ref IdentificationHolds, "IdentificationHoldsCounter");
            Scribe_Values.Look(ref FieldResponses, "FieldResponsesCounter");
            Scribe_Values.Look(ref FieldResumes, "FieldResumesCounter");
            Scribe_Values.Look(ref FieldPostCandidates, "FieldPostCandidatesCounter");
            Scribe_Values.Look(ref FieldBounds, "FieldBoundsCounter");
            Scribe_Values.Look(ref FieldGuardJobs, "FieldGuardJobsCounter");
            Scribe_Values.Look(ref FieldSmokePlans, "FieldSmokePlansCounter");
            Scribe_Values.Look(ref FieldSmokeThrows, "FieldSmokeThrowsCounter");
            Scribe_Values.Look(ref FieldSmokeAdvances, "FieldSmokeAdvancesCounter");
            Scribe_Values.Look(ref MedicalMemberChecks, "MedicalMemberChecksCounter");
            Scribe_Values.Look(ref MedicalTreatments, "MedicalTreatmentsCounter");
            Scribe_Values.Look(ref MedicalCompleted, "MedicalCompletedCounter");
            Scribe_Values.Look(ref MedicalAborted, "MedicalAbortedCounter");
            Scribe_Values.Look(ref MedicalPlasma, "MedicalPlasmaCounter");
            Scribe_Values.Look(ref MedicalRejoins, "MedicalRejoinsCounter");
            Scribe_Values.Look(ref Observations, "ObservationsCounter");
            Scribe_Values.Look(ref ObservationContacts, "ObservationContactsCounter");
            Scribe_Values.Look(ref SupportThrows, "SupportThrowsCounter");
            Scribe_Values.Look(ref SupportWaits, "SupportWaitsCounter");
            Scribe_Values.Look(ref SupportReturns, "SupportReturnsCounter");
            Scribe_Values.Look(ref UnsafeEntries, "UnsafeEntriesCounter");
            Scribe_Values.Look(ref RoomScanSteps, "RoomScanStepsCounter");
            Scribe_Values.Look(ref RoomsSecured, "RoomsSecuredCounter");
            Scribe_Values.Look(ref RoomPlansAttempted, "RoomPlansAttemptedCounter");
            Scribe_Values.Look(ref RoomToolRecoveryWaits, "RoomToolRecoveryWaitsCounter");
        }
    }
}
