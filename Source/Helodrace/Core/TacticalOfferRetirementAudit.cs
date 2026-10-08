using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Helodrace.Squads;
using Helodrace.Tactics;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public bool r7OneSidedOfferSeen, r7RetiredPeerClean, r7OfferSurvivorComplete;
        [DataMember] public string[] r7OfferRetirementEvents;
    }
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool OfferRetirementFixture => result.fixtureCase == "r7-offer-retirement";
        private TacticalSquadCommand retiredOfferPeer, survivingOfferer;
        private readonly List<string> offerRetirementEvents = new List<string>();
        private void OfferRetirementEvent(string message)
        {
            offerRetirementEvents.Add((GenTicks.TicksGame - started) + ":" + message);
            result.r7OfferRetirementEvents = offerRetirementEvents.ToArray();
            Log.Message("R7 offer retirement " + offerRetirementEvents.Last());
        }
        private void ApplyOfferRetirementDrill()
        {
            if (TacticalEngineSelection.Kind != TacticalEngineKind.New || result.units != 2)
                throw new InvalidOperationException("Offer retirement requires two native new-engine squads.");
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            GameComponent_TacticalCommands scheduler = Current.Game.GetComponent<GameComponent_TacticalCommands>();
            if (retiredOfferPeer == null)
            {
                TacticalMessage offer = PendingPackets.FirstOrDefault(m => m.Kind == TacticalMessageKind.Offer
                    && m.From.Link.Peer == m.To && m.To.Link.Peer != m.From);
                if (offer == null) return;
                survivingOfferer = offer.From; retiredOfferPeer = offer.To;
                result.r7OneSidedOfferSeen = true; result.caseTriggered = true;
                OfferRetirementEvent("real undelivered offer with incoming-only peer; receiver exits via WorldPawns");
                foreach (TacticalMemberCommand member in retiredOfferPeer.Members)
                    if (member.Pawn.Spawned) member.Pawn.ExitMap(false, Rot4.West);
                result.r7WorldOrganizationsCleared = retiredOfferPeer.Members.All(m => OrganizationAPI.GetGroup(m.Pawn) == null
                    && m.Pawn.TryGetComp<PawnOrganizationComponent>()?.organizationId == null);
            }
            if (!result.r7RetiredPeerClean && !service.Commands.Contains(retiredOfferPeer))
            {
                result.r7RetiredPeerClean = scheduler.ScheduledCount == 1 && service.Commands.Count() == 1
                    && survivingOfferer.Link.Peer == null && survivingOfferer.Link.Cooperation.Stage == TacticalAgreementStage.Aborted
                    && !PendingPackets.Any(m => m.From == retiredOfferPeer || m.To == retiredOfferPeer)
                    && retiredOfferPeer.Members.All(m => !service.TryProfileContext(m.Pawn, out _, out _));
                if (!result.r7RetiredPeerClean) throw new InvalidOperationException("Retired one-sided offer left peer/packet/ownership state.");
                OfferRetirementEvent("receiver removed; sender peer/agreement, packets and retired pawn ownership cleaned");
            }
            if (!result.r7OfferSurvivorComplete && result.r7RetiredPeerClean
                && survivingOfferer.Phase == TacticalCommandPhase.Complete
                && survivingOfferer.Members.All(m => m.EntryAssignmentDone))
            {
                result.r7OfferSurvivorComplete = true;
                OfferRetirementEvent("remaining actual squad completed its original CQB mission");
            }
        }
    }
}
