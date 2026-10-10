using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace.Tactics
{
    // Only explicit value/reference fields are serialized. No runtime reflection,
    // map-wide cache or duplicate Pawn Job object is used in the tick path.
    public sealed partial class TacticalMemberCommand : IExposable
    {
        public int SavedJobId = -1;
        public void ExposeData()
        {
            Scribe_Values.Look(ref RetryTick, "retryTick");
            Scribe_Values.Look(ref LastProgressTick, "lastProgressTick");
            Scribe_Values.Look(ref LastPosition, "lastPosition");
            Scribe_Values.Look(ref Parking, "parking");
            Scribe_Values.Look(ref Passed, "passed");
            Scribe_Values.Look(ref Crossed, "crossed");
            Scribe_Values.Look(ref Entered, "entered");
            Scribe_Values.Look(ref EverEntered, "everEntered");
            Scribe_Values.Look(ref EntryAssignmentDone, "entryAssignmentDone");
            Scribe_Values.Look(ref Rear, "rear");
            Scribe_Values.Look(ref Fireteam, "fireteam");
            Scribe_Values.Look(ref AutomaticWeapon, "automaticWeapon");
            Scribe_Values.Look(ref LastCareAt, "lastCareAt");
            Scribe_Values.Look(ref MedicalRejoinPending, "medicalRejoinPending");
            Scribe_References.Look(ref Pawn, "pawn");
            if (Scribe.mode == LoadSaveMode.Saving) SavedJobId = Job?.loadID ?? -1;
            Scribe_Values.Look(ref SavedJobId, "ownedJobId", -1);
        }
    }

    public sealed partial class TacticalSquadCommand : IExposable
    {
        internal bool SurveyPending;
        private int savedPlanIndex = -1;
        internal Pawn SavedMedicalCandidate;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref Phase, "phase");
            Scribe_Values.Look(ref Goal, "goal");
            Scribe_Values.Look(ref Due, "due");
            Scribe_Values.Look(ref PhaseStarted, "phaseStarted");
            Scribe_Values.Look(ref Failures, "failures");
            Scribe_Values.Look(ref PlanRetryAt, "planRetryAt");
            Scribe_Values.Look(ref HadConnectedStack, "hadConnectedStack");
            Scribe_Values.Look(ref Defensive, "defensive");
            Scribe_Values.Look(ref DefenseRestoring, "defenseRestoring");
            Scribe_Values.Look(ref DefenseAnchor, "defenseAnchor", IntVec3.Invalid);
            Scribe_References.Look(ref SupportCaller, "supportCaller");
            Scribe_Values.Look(ref SupportAim, "supportAim", IntVec3.Invalid);
            Scribe_Values.Look(ref SupportUntil, "supportUntil");
            Scribe_Values.Look(ref SupportCheckAt, "supportCheckAt");
            Scribe_Values.Look(ref SupportStrikeActive, "supportStrikeActive");
            Scribe_Values.Look(ref BarrierHitPoints, "barrierHitPoints");
            Scribe_Values.Look(ref ReturnCursor, "returnCursor");
            Scribe_Values.Look(ref ReleaseAfterReturn, "releaseAfterReturn");
            Scribe_Values.Look(ref ReplanAfterReturn, "replanAfterReturn");
            Scribe_Values.Look(ref DeferredWork, "deferredWork");
            Scribe_Values.Look(ref ContactRestoring, "contactRestoring");
            Scribe_Values.Look(ref ContactHandledAt, "contactHandledAt");
            Scribe_Values.Look(ref ContactCloseAt, "contactCloseAt");
            Scribe_Values.Look(ref ReplanAfterSupport, "replanAfterSupport");
            Scribe_Values.Look(ref GoalSecured, "goalSecured");
            Scribe_Values.Look(ref FrontierBusy, "frontierBusy");
            Scribe_Values.Look(ref FrontierCursor, "frontierCursor");
            Scribe_Values.Look(ref RoomRecoveryUntil, "roomRecoveryUntil");
            Scribe_Values.Look(ref RecoveryRetryAt, "recoveryRetryAt");
            Scribe_Values.Look(ref RecoveryCandidateCursor, "recoveryCandidateCursor");
            Scribe_Values.Look(ref MedicalCursor, "medicalCursor");
            Scribe_Values.Look(ref MedicalWindowUntil, "medicalWindowUntil");
            Scribe_Values.Look(ref NextMedicalCheck, "nextMedicalCheck");
            Scribe_Values.Look(ref MedicalCandidateScore, "medicalCandidateScore");
            Scribe_Values.Look(ref LastPlanFailure, "lastPlanFailure");
            Scribe_References.Look(ref RaidLord, "raidLord");
            Scribe_References.Look(ref Breacher, "breacher");
            Scribe_Deep.Look(ref Link, "link");
            Scribe_Deep.Look(ref Plan, "plan");
            Scribe_Deep.Look(ref OpeningAction, "openingAction");
            Scribe_Deep.Look(ref ContactResponse, "contactResponse");
            Scribe_Deep.Look(ref FieldResponse, "fieldResponse");
            Scribe_Deep.Look(ref MedicalCare, "medicalCare");
            Scribe_Deep.Look(ref Contacts, "contacts");
            Scribe_Deep.Look(ref ChargeAction, "chargeAction");
            Scribe_Deep.Look(ref RecoveryFrontier, "recoveryFrontier");
            Scribe_Collections.Look(ref Members, "members", LookMode.Deep);
            Scribe_Collections.Look(ref SecuredCells, "securedCells", LookMode.Value);
            Scribe_Collections.Look(ref SecuredPlans, "securedPlans", LookMode.Deep);
            Scribe_Collections.Look(ref Frontiers, "frontiers", LookMode.Deep);
            Scribe_Collections.Look(ref FrontierKeys, "frontierKeys", LookMode.Value);
            Scribe_Collections.Look(ref BreachTools, "breachTools", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                SurveyPending = RoomScan != null; savedPlanIndex = SecuredPlans.IndexOf(Plan);
                SavedMedicalCandidate = MedicalCandidate?.Pawn;
            }
            Scribe_Values.Look(ref SurveyPending, "surveyPending");
            Scribe_Values.Look(ref savedPlanIndex, "currentSecuredPlanIndex", -1);
            Scribe_References.Look(ref SavedMedicalCandidate, "medicalCandidate");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && savedPlanIndex >= 0 && savedPlanIndex < SecuredPlans.Count)
                Plan = SecuredPlans[savedPlanIndex];
        }
    }

    public sealed partial class TacticalLocalPlan : IExposable
    {
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (Barrier?.Destroyed == true) Barrier = null;
            }
            Scribe_Values.Look(ref Opening, "opening");
            Scribe_Values.Look(ref Inward, "inward");
            Scribe_Values.Look(ref EntryLane, "entryLane");
            Scribe_Values.Look(ref Direct, "direct");
            Scribe_Values.Look(ref ExistingOpening, "existingOpening");
            Scribe_Values.Look(ref HammerFallback, "hammerFallback");
            Scribe_References.Look(ref Barrier, "barrier");
            Scribe_Collections.Look(ref Stack, "stack", LookMode.Value);
            Scribe_Collections.Look(ref Positions, "positions", LookMode.Value);
            Scribe_Collections.Look(ref RetainedOutside, "retainedOutside", LookMode.Value);
            Scribe_Collections.Look(ref Interior, "interior", LookMode.Value);
        }
    }

    public sealed partial class TacticalRoomFrontier : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Opening, "opening");
            Scribe_Values.Look(ref Inward, "inward");
            Scribe_Values.Look(ref Preference, "preference");
        }
    }

    public sealed partial class TacticalOpeningAction : IExposable
    {
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (Projectile?.Destroyed == true) Projectile = null;
            }
            Scribe_Values.Look(ref ObservationPosition, "observationPosition");
            Scribe_Values.Look(ref Source, "source");
            Scribe_Values.Look(ref Enemy, "enemy");
            Scribe_Values.Look(ref Target, "target");
            Scribe_Values.Look(ref ReturnPosition, "returnPosition");
            Scribe_Values.Look(ref EnemyId, "enemyId");
            Scribe_Values.Look(ref Started, "started");
            Scribe_Values.Look(ref SupportStarted, "supportStarted");
            Scribe_Values.Look(ref SettledAt, "settledAt");
            Scribe_Values.Look(ref ObservationDone, "observationDone");
            Scribe_Values.Look(ref ObservationIssued, "observationIssued");
            Scribe_Values.Look(ref StackPrepared, "stackPrepared");
            Scribe_Values.Look(ref ThrowIssued, "throwIssued");
            Scribe_Values.Look(ref Launched, "launched");
            Scribe_Values.Look(ref Returned, "returned");
            Scribe_Values.Look(ref EffectsCleared, "effectsCleared");
            Scribe_Values.Look(ref EntryAdjusted, "entryAdjusted");
            Scribe_Values.Look(ref Outdoors, "outdoors");
            Scribe_Values.Look(ref RoomCells, "roomCells");
            Scribe_References.Look(ref Observer, "observer");
            Scribe_References.Look(ref Thrower, "thrower");
            Scribe_References.Look(ref Projectile, "projectile");
            Scribe_Defs.Look(ref ProjectileDef, "projectileDef");
        }
    }

    public sealed partial class TacticalChargeAction : IExposable
    {
        internal Thing SavedCharge;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Deadline, "deadline");
            Scribe_Values.Look(ref SettledAt, "settledAt");
            Scribe_Values.Look(ref Withdrawing, "withdrawing");
            Scribe_Values.Look(ref Detonated, "detonated");
            Scribe_Values.Look(ref EffectsCleared, "effectsCleared");
            Scribe_Values.Look(ref Radius, "radius");
            Scribe_References.Look(ref Installer, "installer");
            Scribe_Collections.Look(ref OriginalStack, "originalStack", LookMode.Value);
            Scribe_Collections.Look(ref Withdrawal, "withdrawal", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                SavedCharge = Charge?.parent?.Destroyed == false ? Charge.parent : null;
                if (Charge != null) EffectOperator = Charge.OperatorPawn;
                SavedFragments = SavedFragments.Concat(Charge?.Fragments ?? Enumerable.Empty<Projectile>())
                    .Where(projectile => projectile?.Spawned == true).Distinct().ToList();
            }
            Scribe_References.Look(ref SavedCharge, "installedCharge");
            Scribe_References.Look(ref EffectOperator, "effectOperator");
            Scribe_Collections.Look(ref SavedFragments, "liveFragments", LookMode.Reference);
        }
    }

    public sealed partial class TacticalContact : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref EnemyId, "enemyId");
            Scribe_Values.Look(ref SeenTick, "seenTick");
            Scribe_Values.Look(ref PreviousTick, "previousTick");
            Scribe_Values.Look(ref PreviousPosition, "previousPosition");
            Scribe_Values.Look(ref Position, "position");
            Scribe_Values.Look(ref Area, "area");
            Scribe_Values.Look(ref Door, "door");
            Scribe_Values.Look(ref Origin, "origin");
        }
    }

    public sealed partial class TacticalContactMemory : IExposable
    {
        public void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
        }
    }

    public sealed partial class TacticalContactState : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref NextScan, "nextScan");
            Scribe_Values.Look(ref ObserverCursor, "observerCursor");
            Scribe_Values.Look(ref CandidateCursor, "candidateCursor");
            Scribe_Deep.Look(ref Memory, "memory");
        }
    }

    public sealed partial class TacticalContactResponse : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Started, "started");
            Scribe_Values.Look(ref LastSeen, "lastSeen");
            Scribe_Values.Look(ref Assigned, "assigned");
            Scribe_Values.Look(ref SecuredCount, "securedCount");
            Scribe_Values.Look(ref FirstId, "firstId");
            Scribe_Values.Look(ref SecondId, "secondId");
            Scribe_Values.Look(ref FocusUpdatedAt, "focusUpdatedAt");
            Scribe_Values.Look(ref Phase, "phase");
            Scribe_Values.Look(ref Anchor, "anchor");
            Scribe_Values.Look(ref First, "first");
            Scribe_Values.Look(ref Second, "second");
            Scribe_Values.Look(ref Rear, "rear");
            Scribe_Values.Look(ref Opposed, "opposed");
            Scribe_Values.Look(ref Door, "door");
            Scribe_Collections.Look(ref Posts, "posts", LookMode.Value);
            Scribe_Collections.Look(ref Occupied, "occupied", LookMode.Value);
        }
    }

    public sealed partial class TacticalFieldResponse : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Started, "started");
            Scribe_Values.Look(ref LastSeen, "lastSeen");
            Scribe_Values.Look(ref EnemyId, "enemyId");
            Scribe_Values.Look(ref FocusAt, "focusAt");
            Scribe_Values.Look(ref Assigned, "assigned");
            Scribe_Values.Look(ref MovingTeam, "movingTeam");
            Scribe_Values.Look(ref NextMove, "nextMove");
            Scribe_Values.Look(ref MoveStarted, "moveStarted");
            Scribe_Values.Look(ref SmokeRetryAt, "smokeRetryAt");
            Scribe_Values.Look(ref BoundCursor, "boundCursor");
            Scribe_Values.Look(ref BoundPending, "boundPending");
            Scribe_Values.Look(ref Stage, "stage");
            Scribe_Values.Look(ref Motion, "motion");
            Scribe_Values.Look(ref High, "high");
            Scribe_Values.Look(ref Anchor, "anchor");
            Scribe_Values.Look(ref Focus, "focus");
            Scribe_Values.Look(ref Forward, "forward");
            Scribe_Deep.Look(ref Screen, "screen");
            Scribe_Collections.Look(ref Posts, "posts", LookMode.Value);
            Scribe_Collections.Look(ref Order, "order", LookMode.Value);
            Scribe_Collections.Look(ref FireGroup, "fireGroup", LookMode.Value);
            Scribe_Collections.Look(ref Occupied, "occupied", LookMode.Value);
        }
    }

    public sealed partial class TacticalFieldSmoke : IExposable
    {
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (Projectile?.Destroyed == true) Projectile = null;
                SavedJobId = ActiveJob?.loadID ?? -1;
            }
            Scribe_Values.Look(ref SavedJobId, "activeJobId", -1);
            Scribe_Values.Look(ref Target, "target");
            Scribe_Values.Look(ref ReturnPosition, "returnPosition");
            Scribe_Values.Look(ref Started, "started");
            Scribe_Values.Look(ref Launched, "launched");
            Scribe_Values.Look(ref Returned, "returned");
            Scribe_References.Look(ref Thrower, "thrower");
            Scribe_References.Look(ref Projectile, "projectile");
        }
    }

    public sealed partial class TacticalMedicalCare : IExposable
    {
        internal Pawn SavedPatient, SavedHelper;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Started, "started");
            Scribe_Values.Look(ref Finished, "finished");
            Scribe_Values.Look(ref Successful, "successful");
            Scribe_Values.Look(ref Plasma, "plasma");
            Scribe_Values.Look(ref CancelRequested, "cancelRequested");
            if (Scribe.mode == LoadSaveMode.Saving) { SavedPatient = Patient?.Pawn; SavedHelper = Helper?.Pawn; }
            Scribe_References.Look(ref SavedPatient, "patient"); Scribe_References.Look(ref SavedHelper, "helper");
        }
    }

    public sealed partial class TacticalSquadLink : IExposable
    {
        internal string SavedPeer;
        public void ExposeData()
        {
            Scribe_Values.Look(ref SelectionCursor, "selectionCursor");
            Scribe_Values.Look(ref PeerCursor, "peerCursor");
            Scribe_Values.Look(ref ExchangeAt, "exchangeAt");
            Scribe_Values.Look(ref PartnerExchangeAt, "partnerExchangeAt");
            Scribe_Values.Look(ref PartnerProbeAt, "partnerProbeAt");
            Scribe_Values.Look(ref OpportunityUntil, "opportunityUntil");
            Scribe_Values.Look(ref IdentifyUntil, "identifyUntil");
            Scribe_Values.Look(ref IdentifyStarted, "identifyStarted");
            Scribe_Values.Look(ref ResetPlan, "resetPlan");
            Scribe_Values.Look(ref IdentificationHolding, "identificationHolding");
            Scribe_Values.Look(ref LastChannel, "lastChannel");
            Scribe_References.Look(ref Liaison, "liaison");
            Scribe_Deep.Look(ref Cooperation, "cooperation");
            Scribe_Collections.Look(ref KnownPortals, "knownPortals", LookMode.Value);
            Scribe_Collections.Look(ref Identified, "identified", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.Saving) SavedPeer = Peer?.Id;
            Scribe_Values.Look(ref SavedPeer, "peerId");
        }
    }

    public sealed partial class TacticalCooperationAgenda : IExposable
    {
        public TacticalCooperationAgenda() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref First, "first");
            Scribe_Values.Look(ref Second, "second");
            Scribe_Values.Look(ref Goal, "goal");
            Scribe_Values.Look(ref Forward, "forward");
            Scribe_Values.Look(ref StartAt, "startAt");
            Scribe_Values.Look(ref Deadline, "deadline");
        }
    }

    public sealed partial class TacticalCooperationState : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Stage, "stage");
            Scribe_Values.Look(ref LocalReady, "localReady");
            Scribe_Values.Look(ref PeerReady, "peerReady");
            Scribe_Values.Look(ref PeerFinished, "peerFinished");
            Scribe_Values.Look(ref PeerGoalSecured, "peerGoalSecured");
            Scribe_Values.Look(ref PeerStatusAt, "peerStatusAt");
            Scribe_Values.Look(ref ConfirmedStart, "confirmedStart");
            Scribe_Values.Look(ref NegotiationStarted, "negotiationStarted");
            Scribe_Values.Look(ref PeerOpening, "peerOpening");
            Scribe_Deep.Look(ref Agenda, "agenda");
        }
    }

    public sealed partial class TacticalMessage : IExposable
    {
        internal string SavedFrom, SavedTo;
        internal Map SavedMap;
        public void ExposeData()
        {
            Scribe_Values.Look(ref FromPawn, "fromPawn");
            Scribe_Values.Look(ref ToPawn, "toPawn");
            Scribe_Values.Look(ref Sent, "sent");
            Scribe_Values.Look(ref Due, "due");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Channel, "channel");
            Scribe_Values.Look(ref Ready, "ready");
            Scribe_Values.Look(ref Finished, "finished");
            Scribe_Values.Look(ref GoalSecured, "goalSecured");
            Scribe_Values.Look(ref PortalUsable, "portalUsable");
            Scribe_Values.Look(ref Opening, "opening");
            Scribe_Values.Look(ref StartAt, "startAt");
            Scribe_Deep.Look(ref Agenda, "agenda");
            Scribe_Deep.Look(ref Contact, "contact");
            if (Scribe.mode == LoadSaveMode.Saving) { SavedFrom = From?.Id; SavedTo = To?.Id; SavedMap = From?.Owner.map; }
            Scribe_Values.Look(ref SavedFrom, "fromId"); Scribe_Values.Look(ref SavedTo, "toId");
            Scribe_References.Look(ref SavedMap, "map");
        }
    }

}
