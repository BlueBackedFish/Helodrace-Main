using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Helodrace.ModernWar;
using Helodrace.Tactics;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public int newCooperationCompletedTick = -1;
        [DataMember] public bool newCooperationMilestonePreserved;
        [DataMember] public string[] newCooperationCompletionAgendas, newCooperationCompletionStates;
        [DataMember] public string[] newCooperationExecutionTrace;
    }

    // Scripted physical stimuli only; never insert cooperation or contact state.
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool CooperationFixture => result.fixtureCase == "r5-low-coop" || result.fixtureCase == "r5-shared"
            || result.fixtureCase == "r5-radio-loss";
        private List<string> cooperationEvents = new List<string>();
        private List<string> cooperationExecutionTrace = new List<string>();
        private int nextCooperationExecutionTrace;
        private readonly List<KeyValuePair<InstalledModularArmorPart, Thing>> removedRadios = new List<KeyValuePair<InstalledModularArmorPart, Thing>>();
        private int splitAt = -1, splitReportA, splitReportB, radioStep, radioKilledAt, identificationBaseline;
        private long radioDroppedBaseline;
        private long lastLoggedDrops;
        private Pawn killedRadioOperator;
        private TacticalSquadCommand radioLostCommand;
        private bool splitClean = true, recontactTriggered, blackoutClean = true;
        private bool reportExposed, reportSightClean = true;
        private TacticalSquadCommand reportSource, reportReceiver;
        private int reportExposedAt;
        private Building_Door sharedDoor;
        private void CooperationEvent(string message)
        {
            if (cooperationEvents.Count >= 64) return;
            string entry = (GenTicks.TicksGame - started) + ":" + message;
            cooperationEvents.Add(entry); Log.Message("R5 cooperation drill " + entry);
        }
        private void InitializeCooperationDrill()
        {
            if (result.units != 2) throw new InvalidOperationException("R5 fixture requires exactly two complete squads.");
            if (result.fixtureCase == "r5-shared")
            {
                foreach (Pawn pawn in raiders)
                {
                    foreach (Apparel apparel in pawn.apparel.WornApparel.ToArray())
                        if (apparel.TryGetComp<CompSledgehammerBreach>() != null)
                        { pawn.apparel.Remove(apparel); apparel.Destroy(); }
                    foreach (Thing thing in pawn.inventory.innerContainer.ToArray())
                        if (thing.def == BreachExplosiveUtility.C4Def || (thing as ThingWithComps)?.TryGetComp<CompBreachIgniter>() != null)
                        { pawn.inventory.innerContainer.Remove(thing); thing.Destroy(); }
                }
                sharedDoor = new IntVec3(100,0,108).GetEdifice(map) as Building_Door;
                if (sharedDoor == null) throw new InvalidOperationException("Shared fixture needs sapper-door.");
                AccessTools.Field(typeof(Building_Door), "holdOpenInt").SetValue(sharedDoor, true);
                sharedDoor.StartManualOpenBy(owner);
            }
            if (result.fixtureCase == "r5-radio-loss")
            {
                foreach (Pawn pawn in raiders)
                    foreach (Apparel apparel in pawn.apparel.WornApparel)
                    {
                        CompModularArmor modular = apparel.TryGetComp<CompModularArmor>();
                        if (modular == null) continue;
                        foreach (InstalledModularArmorPart record in modular.InstalledParts)
                            if ((record.InstalledItem as ThingWithComps)?.TryGetComp<CompTacticalRadio>() != null)
                            {
                                Thing item = record.RemoveInstalledItem();
                                removedRadios.Add(new KeyValuePair<InstalledModularArmorPart, Thing>(record,item));
                                // A physical loose radio item in inventory is deliberately insufficient.
                                pawn.inventory.innerContainer.TryAdd(item);
                            }
                    }
                if (removedRadios.Count < 4 || raiders.Any(p => RaidTacticalRadioUtility.Radios(p).Any()))
                    throw new InvalidOperationException("Radio-loss fixture must remove actual worn hardware from both squads.");
            }
        }
        private static bool Voice(TacticalSquadCommand a, TacticalSquadCommand b)
        {
            Pawn x=a.Link.Liaison, y=b.Link.Liaison;
            return x?.Spawned == true && y?.Spawned == true && x.Position.DistanceToSquared(y.Position) <= 64
                && GenSight.LineOfSight(x.Position,y.Position,x.Map,true)
                && !GenSight.PointsOnLineOfSight(x.Position,y.Position).Any(c => RaidSmokeUtility.CoveringSmokeAt(x.Map,c));
        }
        private void TraceCooperationExecution(TacticalSquadCommand[] commands, int tick)
        {
            // Functional drill only. Fixed-input CPU cases never enter this path.
            if (cooperationExecutionTrace.Count == 0 && result.newCooperationExecutionTrace != null)
                cooperationExecutionTrace.AddRange(result.newCooperationExecutionTrace);
            if (tick < nextCooperationExecutionTrace || cooperationExecutionTrace.Count >= 128) return;
            nextCooperationExecutionTrace = tick + 120;
            string entry = (tick - started) + ":" + string.Join(" | ", commands.Select(command =>
                command.Id + " phase=" + command.Phase + "/" + command.Link.Cooperation.Stage
                + " ready=" + command.Link.Cooperation.LocalReady + "/" + command.Link.Cooperation.PeerReady
                + " opening=" + command.Plan?.Opening + " direct=" + command.Plan?.Direct
                + " existing=" + command.Plan?.ExistingOpening + " failures=" + command.Failures
                + " reason=" + command.LastPlanFailure + " retry=" + (command.PlanRetryAt - tick)
                + " goal=" + command.GoalSecured + " secured=" + command.SecuredCells.Count
                + " plans=" + command.SecuredPlans.Count + " scan=" + (command.RoomScan != null)
                + " frontier=" + command.FrontierCursor + "/" + command.Frontiers.Count
                + " unknown=" + command.Frontiers.Count(f => !command.SecuredCells.Contains(f.Inside))
                + " busy=" + command.FrontierBusy + " entered=" + command.Members.Count(m => m.EntryAssignmentDone)
                + " deadline=" + (command.Link.Cooperation.Agenda?.Deadline - tick)));
            cooperationExecutionTrace.Add(entry); result.newCooperationExecutionTrace = cooperationExecutionTrace.ToArray();
            Log.Message("R7 cooperation execution " + entry);
        }
        private void ApplyCooperationDrill()
        {
            if (TacticalEngineSelection.Kind != TacticalEngineKind.New) return;
            int tick = GenTicks.TicksGame;
            var commands = map.GetComponent<MapComponent_TacticalCommands>().Commands.OrderBy(c=>c.Id).ToArray();
            if (commands.Length != 2) return;
            TraceCooperationExecution(commands, tick);
            TacticalSquadCommand a=commands[0], b=commands[1];
            TacticalCommunications network=Current.Game.GetComponent<GameComponent_TacticalCommands>().Communications;
            ObserveCooperationCompletion(commands, tick);
            if (network.MessagesDropped>lastLoggedDrops)
            {
                lastLoggedDrops=network.MessagesDropped;
                CooperationEvent("packet drop " + network.LastDropReason + " A=" + a.Phase + "/" + a.Link.Cooperation.Stage
                    + " B=" + b.Phase + "/" + b.Link.Cooperation.Stage);
            }
            if (result.fixtureCase == "r5-radio-loss")
            {
                if (radioStep == 0)
                {
                    blackoutClean &= network.MessagesDelivered == 0 && !a.Link.Cooperation.Active && !b.Link.Cooperation.Active
                        && !Voice(a,b) && !raiders.Any(p=>RaidTacticalRadioUtility.Radios(p).Any());
                    if (tick-started < 180) return;
                    result.newRadioBlackoutBlocked=blackoutClean;
                    foreach (var removed in removedRadios)
                    {
                        removed.Value.holdingOwner?.Remove(removed.Value);
                        if (!removed.Key.GetDirectlyHeldThings().TryAdd(removed.Value)) throw new InvalidOperationException("Physical radio restore failed.");
                    }
                    result.newRadioRestored=raiders.Count(p=>RaidTacticalRadioUtility.Radios(p).Any()) >= 4;
                    radioStep=1; CooperationEvent("worn radios restored after 180 ticks without delivery");
                }
                if (radioStep == 1)
                {
                    var packets=(List<TacticalMessage>)AccessTools.Field(typeof(TacticalCommunications),"pending").GetValue(network);
                    TacticalMessage packet=packets.FirstOrDefault(p=>p.Kind==TacticalMessageKind.Offer && p.Channel==TacticalChannel.Radio
                        && p.Due>tick && p.From.Link.Liaison?.thingIDNumber==p.FromPawn);
                    if (packet == null) return;
                    killedRadioOperator=packet.From.Link.Liaison; radioLostCommand=packet.From;
                    radioDroppedBaseline=network.EndpointDrops; radioKilledAt=tick;
                    ProtectedRaiders.Remove(killedRadioOperator); killedRadioOperator.Kill(null);
                    radioStep=2; result.caseTriggered=true; CooperationEvent("radio sender killed with undelivered offer " + packet.FromPawn);
                }
                if (radioStep == 2 && tick-radioKilledAt >= 90)
                {
                    result.newRadioDeadPacketDropped=network.EndpointDrops>radioDroppedBaseline
                        && network.LastInvalidSender==killedRadioOperator.thingIDNumber;
                    result.newRadioSuccessor=radioLostCommand.Link.Liaison != null && radioLostCommand.Link.Liaison != killedRadioOperator
                        && !radioLostCommand.Link.Liaison.Dead && RaidTacticalRadioUtility.Radios(radioLostCommand.Link.Liaison).Any();
                    if (result.newRadioDeadPacketDropped && result.newRadioSuccessor)
                    { radioStep=3; CooperationEvent("old sender packet dropped; successor uses actual worn radio"); }
                }
                if (radioStep == 3) ApplyRadioContactReport(a,b,tick);
                return;
            }
            if (result.fixtureCase != "r5-low-coop") return;
            if (a.Link.Cooperation.Active && b.Link.Cooperation.Active && !Voice(a,b))
            {
                if (splitAt<0)
                { splitAt=tick; splitReportA=a.Link.Cooperation.PeerStatusAt; splitReportB=b.Link.Cooperation.PeerStatusAt; CooperationEvent("LOW split; status snapshots frozen"); }
                splitClean &= a.Link.Cooperation.PeerStatusAt==splitReportA && b.Link.Cooperation.PeerStatusAt==splitReportB;
                if (tick-splitAt>=120) result.newCooperationSplitBlocked=splitClean;
            }
            else splitAt=-1;
            if (!recontactTriggered && result.newCooperationSplitBlocked && a.SecuredPlans.Count>0 && b.SecuredPlans.Count>0
                && a.Link.Cooperation.Active && b.Link.Cooperation.Active && a.ChargeAction==null && b.ChargeAction==null
                && a.Phase==TacticalCommandPhase.Stack && b.Phase==TacticalCommandPhase.Stack)
            {
                Pawn liaison=a.Link.Liaison, visiting=b.Link.Liaison;
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 cell=liaison.Position+offset;
                    if (!cell.Standable(map) || !cell.Roofed(map) || cell.GetFirstPawn(map)!=null) continue;
                    visiting.Position=cell; recontactTriggered=true; identificationBaseline=(int)network.Identifications;
                    result.caseTriggered=true; CooperationEvent("actual indoor liaison encounter " + cell); break;
                }
            }
            if (recontactTriggered && network.Identifications>identificationBaseline
                && map.GetComponent<MapComponent_TacticalCommands>().IdentificationHolds>0)
                result.newCooperationIdentification=true;
        }
        private void ApplyRadioContactReport(TacticalSquadCommand a, TacticalSquadCommand b, int tick)
        {
            if (result.newContactReportShared) return;
            bool Sees(TacticalSquadCommand command) => command.Members.Any(m=>m.Pawn.Spawned && !m.Pawn.Dead
                && TacticalContactSight.CanSee(map,m.Pawn.Position,owner,new TacticalContactState()));
            if (!reportExposed && a.Link.Cooperation.Active && b.Link.Cooperation.Active
                && a.Phase==TacticalCommandPhase.Stack && b.Phase==TacticalCommandPhase.Stack
                && a.Members.Count(m=>m.Pawn.Spawned && a.Plan.Stack.Contains(m.Pawn.Position))>=8
                && b.Members.Count(m=>m.Pawn.Spawned && b.Plan.Stack.Contains(m.Pawn.Position))>=8)
            {
                IntVec3 tangent=new IntVec3(-a.Plan.Inward.z,0,a.Plan.Inward.x);
                owner.Position=a.Plan.Outside-tangent*26;
                if (owner.Position.Standable(map) && Sees(a) && !Sees(b))
                {
                    reportSource=a; reportReceiver=b; reportExposed=true; reportExposedAt=tick;
                    CooperationEvent("contact exposed only to source squad " + owner.Position);
                }
                else owner.Position=new IntVec3(155,0,155);
            }
            if (!reportExposed) return;
            reportSightClean &= !Sees(reportReceiver);
            TacticalContact received=reportReceiver.Contacts.Memory.Entries.FirstOrDefault(c=>c.EnemyId==owner.thingIDNumber
                && c.Origin==reportSource.Id && c.SeenTick>=reportExposedAt && c.SeenTick<tick);
            if (received==null) return;
            result.newContactReportShared=true; result.newContactReportWithoutLocalSight=reportSightClean;
            CooperationEvent("radio contact received with original observed tick " + (received.SeenTick-started));
            owner.Position=new IntVec3(155,0,155);
        }
        private IntVec3[] CooperationRegions(TacticalSquadCommand[] commands) => MultiRoomFixture
            ? new[] { new IntVec3(108,0,110), new IntVec3(120,0,110), new IntVec3(120,0,128) }
            : new[] { commands[0].Goal };

        // Audit history, never an input to the AI. A later deadline recovery
        // must not erase an actually completed, jointly covered allocation.
        // Conversely two Finished flags alone do not establish that milestone.
        internal static bool CooperationCompletionReady(TacticalSquadCommand[] commands, IntVec3[] regions, int tick)
        {
            return commands.Length == 2 && commands.All(c => c.Link.Cooperation.Agenda != null
                && c.Link.Cooperation.Stage == TacticalAgreementStage.Finished
                && tick < c.Link.Cooperation.Agenda.Deadline
                && c.Goal == c.Link.Cooperation.Agenda.Goal
                && c.Link.Peer?.Id == c.Link.Cooperation.Agenda.Peer(c.Id)
                && c.SecuredCells.Contains(c.Link.Cooperation.Agenda.Area(c.Id))
                && c.Members.Count > 0 && c.Members.All(m => m.Pawn != null && (m.Pawn.Dead || m.Pawn.Downed
                    || m.EntryAssignmentDone && m.Passed && m.Crossed && m.EverEntered)))
                && Agenda(commands[0].Link.Cooperation.Agenda) == Agenda(commands[1].Link.Cooperation.Agenda)
                && TimedCqbMissionCoverage(commands, regions);
        }

        internal static bool CooperationCompletionPreserved(TacticalEngineAuditResult evidence,
            TacticalSquadCommand[] commands, IntVec3[] regions, int startedAt, int tick)
        {
            if (evidence.newCooperationCompletedTick < 0 || evidence.newCooperationCompletionAgendas?.Length != 2
                || evidence.newCooperationCompletionStates?.Length != 2 || commands.Length != 2
                || commands.Select(c => c.Id).Distinct().Count() != 2
                || startedAt + evidence.newCooperationCompletedTick > tick) return false;
            for (int i = 0; i < commands.Length; i++)
            {
                TacticalSquadCommand command = commands[i];
                TacticalCooperationState agreement = command.Link.Cooperation;
                if (agreement.Agenda == null || evidence.newCooperationCompletionAgendas[i] != Agenda(agreement.Agenda)
                    || !evidence.newCooperationCompletionStates[i].StartsWith(command.Id + ":Finished ", StringComparison.Ordinal)
                    || startedAt + evidence.newCooperationCompletedTick >= agreement.Agenda.Deadline
                    || command.Goal != agreement.Agenda.Goal || command.Phase != TacticalCommandPhase.Complete
                    || agreement.Stage != TacticalAgreementStage.Finished
                        && (agreement.Stage != TacticalAgreementStage.Aborted || tick < agreement.Agenda.Deadline)
                    || !command.SecuredCells.Contains(agreement.Agenda.Area(command.Id))
                    || command.SecuredPlans.Count == 0
                    || command.SecuredPlans.Select(p => p.Opening).Distinct().Count() != command.SecuredPlans.Count)
                    return false;
            }
            return regions.All(cell => commands.Any(c => c.SecuredCells.Contains(cell)))
                && commands.Any(c => c.GoalSecured && c.SecuredCells.Contains(c.Goal));
        }

        private void ObserveCooperationCompletion(TacticalSquadCommand[] commands, int tick)
        {
            if (result.newCooperationCompletedTick >= 0 || !CooperationCompletionReady(commands, CooperationRegions(commands), tick)) return;
            TacticalCommunications network = Current.Game.GetComponent<GameComponent_TacticalCommands>().Communications;
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            if (network.AgreementsConfirmed < 2 || service.CooperationStarts < 2) return;
            result.newCooperationCompletedTick = tick - started;
            result.newCooperationCompletionAgendas = commands.Select(c => Agenda(c.Link.Cooperation.Agenda)).ToArray();
            result.newCooperationCompletionStates = commands.Select(c => c.Id + ":Finished phase=" + c.Phase
                + " ownAreaSecured=True goal=" + c.GoalSecured + " peerFinished=" + c.Link.Cooperation.PeerFinished
                + " peerGoalReported=" + c.Link.Cooperation.PeerGoalSecured
                + " rooms=" + string.Join(",", CooperationRegions(commands).Select(c.SecuredCells.Contains))
                + " secured=" + c.SecuredCells.Count + " entries=" + c.Members.Count(m => m.EntryAssignmentDone && m.EverEntered)
                + " plans=" + string.Join(";", c.SecuredPlans.Select(p => p.Opening))).ToArray();
            CooperationEvent("joint mission completed before deadline; local Finished states and actual room/bed/entry coverage recorded");
        }

        private void FinalCooperationDiagnostics(TacticalSquadCommand[] commands)
        {
            commands = commands.OrderBy(c => c.Id).ToArray();
            TacticalCommunications network=Current.Game.GetComponent<GameComponent_TacticalCommands>().Communications;
            MapComponent_TacticalCommands service=map.GetComponent<MapComponent_TacticalCommands>();
            result.newCommunicationChecks=network.PairChecks; result.newMessagesSent=network.MessagesSent;
            result.newMessagesDelivered=network.MessagesDelivered; result.newMessagesDropped=network.MessagesDropped;
            result.newReportsReceived=network.ReportsReceived; result.newPendingMessages=network.PendingCount;
            result.newAgreementsConfirmed=network.AgreementsConfirmed; result.newOperatorChanges=network.OperatorChanges;
            result.newCooperationStarts=service.CooperationStarts; result.newIdentificationHolds=service.IdentificationHolds;
            result.newCooperationDistinctEntrances=commands.Length==2 && commands.All(c=>c.SecuredPlans.Count>0)
                && commands.Select(c=>c.SecuredPlans[0].Opening).Distinct().Count()==2;
            result.newCooperationOwnedAreas=commands.All(c=>c.Link.Cooperation.Agenda!=null
                && c.SecuredCells.Contains(c.Link.Cooperation.Agenda.Area(c.Id)) && c.SecuredPlans.Count>0);
            result.newSharedEntranceProgress=commands.Length==2 && commands.All(c=>c.SecuredPlans.Count>0
                && c.SecuredPlans[0].Opening==new IntVec3(100,0,108)
                && c.Members.All(m=>m.Passed && m.Crossed && m.EverEntered));
            result.newCooperationMilestonePreserved = CooperationCompletionPreserved(result, commands,
                CooperationRegions(commands), started, GenTicks.TicksGame);
            result.newCooperationComplete=network.AgreementsConfirmed>=2 && service.CooperationStarts>=2
                && result.newCooperationMilestonePreserved
                && (result.fixtureCase=="r5-shared" ? result.newSharedEntranceProgress
                    : result.newCooperationDistinctEntrances && result.newCooperationOwnedAreas)
                && (result.fixtureCase!="r5-low-coop" || result.newCooperationSplitBlocked && result.newCooperationIdentification)
                && (result.fixtureCase!="r5-radio-loss" || result.newRadioBlackoutBlocked && result.newRadioRestored
                    && result.newRadioDeadPacketDropped && result.newRadioSuccessor
                    && result.newContactReportShared && result.newContactReportWithoutLocalSight);
            result.newCooperationEvents=cooperationEvents.ToArray();
            result.newCooperationStates=commands.Select(c=>c.Id+":"+c.Link.Cooperation.Stage+" ready="+c.Link.Cooperation.LocalReady
                +" peerReady="+c.Link.Cooperation.PeerReady+" peerTick="+c.Link.Cooperation.PeerStatusAt
                +" area="+c.Link.Cooperation.Agenda?.Area(c.Id)+" areaSecured="+(c.Link.Cooperation.Agenda!=null
                    && c.SecuredCells.Contains(c.Link.Cooperation.Agenda.Area(c.Id)))+" liaison="+c.Link.Liaison?.thingIDNumber
                +" channel="+c.Link.LastChannel+" identification="+c.Link.IdentifyUntil).ToArray();
        }
    }
}
