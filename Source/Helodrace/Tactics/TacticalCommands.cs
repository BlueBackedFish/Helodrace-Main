using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Helodrace.Squads;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace.Tactics
{
    public enum TacticalCommandPhase { Pending, Stack, Breach, Observe, Support, BlastWait, Enter, Clear, Returning, Complete, Released, Defending }
    public sealed partial class TacticalMemberCommand
    {
        public Pawn Pawn;
        public Job Job;
        public int RetryTick;
        public int LastProgressTick;
        public IntVec3 LastPosition, Parking = IntVec3.Invalid;
        public bool Passed, Crossed, Entered, EverEntered, EntryAssignmentDone, Rear;
        public int Fireteam = -1;
        public bool AutomaticWeapon;
        public string CombatFunction;
        public int LastCareAt = -6000;
        public bool MedicalRejoinPending;
        public bool Medic;
        public int FocusCheckAt;
    }
    public sealed partial class TacticalSquadCommand
    {
        public string Id;
        public TacticalSquadLink Link = new TacticalSquadLink();
        public List<TacticalMemberCommand> Members = new List<TacticalMemberCommand>();
        public MapComponent_TacticalCommands Owner;
        public Lord RaidLord;
        public TacticalCommandPhase Phase;
        public IntVec3 Goal;
        public int Due, PhaseStarted, Failures, PlanRetryAt;
        public int MovementDispatchAt, MovementCursor;
        public bool HadConnectedStack;
        public Pawn Breacher;
        public int BarrierHitPoints = -1;
        public int ReturnCursor;
        public bool ReleaseAfterReturn;
        public bool ReplanAfterReturn;
        public bool DeferredWork;
        public TacticalPlanFailure LastPlanFailure;
        public TacticalOpeningAction OpeningAction;
        public TacticalContactState Contacts = new TacticalContactState();
        public TacticalContactResponse ContactResponse;
        public TacticalFieldResponse FieldResponse;
        public TacticalFocusFire FocusFire;
        public TacticalMedicalCare MedicalCare;
        public TacticalBagRecovery BagRecovery;
        public List<Thing> MedicalBags = new List<Thing>();
        public int BagRecoveryAt;
        public int MedicalCursor, MedicalWindowUntil, NextMedicalCheck;
        public TacticalMemberCommand MedicalCandidate;
        public float MedicalCandidateScore;
        public bool ContactRestoring;
        public int ContactHandledAt = -1, ContactCloseAt = int.MinValue / 2;
        public TacticalChargeAction ChargeAction;
        public bool ReplanAfterSupport;
        public List<Thing> BreachTools = new List<Thing>();
        public int RecoveryRetryAt, RecoveryCandidateCursor;
        public TacticalRoomScan RoomScan;
        public HashSet<IntVec3> SecuredCells = new HashSet<IntVec3>();
        public List<TacticalLocalPlan> SecuredPlans = new List<TacticalLocalPlan>();
        public List<TacticalRoomFrontier> Frontiers = new List<TacticalRoomFrontier>();
        public HashSet<IntVec3> FrontierKeys = new HashSet<IntVec3>();
        public bool GoalSecured, FrontierBusy;
        public int FrontierCursor;
        public TacticalRoomFrontier RecoveryFrontier;
        public int RoomRecoveryUntil;
        public bool Defensive, DefenseRestoring;
        public IntVec3 DefenseAnchor = IntVec3.Invalid;
        public bool Terminal => Phase == TacticalCommandPhase.Released;
    }

    // One fair bounded scheduler for ALL maps. No per-map tactical tick/update.
    [NewTactical]
    public sealed partial class GameComponent_TacticalCommands : GameComponent
    {
        private readonly List<TacticalSquadCommand> commands = new List<TacticalSquadCommand>();
        private int cursor, discoverAt, discoverOrganization;
        private IEnumerator<RaidTacticalUnit> discovery;
        private CombatOrganization discovering;
        private int discoveryRevision;
        public readonly TacticalWorkBudget WorkBudget = new TacticalWorkBudget();
        public readonly TacticalCommunications Communications = new TacticalCommunications();
        public long Advances, BudgetStops;
        public int ScheduledCount => commands.Count;
        public GameComponent_TacticalCommands(Game game) { }
        public static bool IsAssaultPhase(LordJob job, LordToil toil) => job != null
            && (toil is LordToil_AssaultColony || toil is LordToil_AssaultColonySappers || toil is LordToil_AssaultColonyBreaching);
        public static bool IsAssaultLord(Lord lord) => lord != null && IsAssaultPhase(lord.LordJob, lord.CurLordToil);
        public void RegisterLord(Lord lord)
        {
            if (!IsTacticalLord(lord)) return;
            var seen = new HashSet<string>();
            foreach (Pawn pawn in lord.ownedPawns)
            {
                RaidTacticalUnit unit = RaidTacticalUnit.ForPawn(pawn);
                if (unit == null || !seen.Add(unit.Id)) continue;
                MapComponent_TacticalCommands service = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
                if (service == null || !CanControlMission(IsDefensiveLord(lord), unit.Faction.HostileTo(Faction.OfPlayer), service.HasExplicitGoal)) continue;
                TacticalSquadCommand command = service.Register(unit, GenTicks.TicksGame);
                if (command != null) commands.Add(command);
            }
        }
        public override void GameComponentTick()
        {
            RestorePackets();
            int tick = GenTicks.TicksGame;
            DiscoverOne(tick);
            if (commands.Count == 0) return;
            long started = Stopwatch.GetTimestamp();
            PumpCqbEvents(tick);
            Communications.Pump(commands, WorkBudget, tick);
            if (Stopwatch.GetTimestamp() - started > Stopwatch.Frequency * .0015) { BudgetStops++; return; }
            int visited = 0, processed = 0;
            // Four cheap due checks, at most two real squad decisions in a tick.
            while (visited++ < Math.Min(4, commands.Count) && processed < 2)
            {
                if (cursor >= commands.Count) cursor = 0;
                TacticalSquadCommand command = commands[cursor];
                if (command.Terminal || command.Owner.map.Disposed)
                { command.Owner.Retire(command); continue; }
                cursor++;
                if (command.Due > tick) continue;
                command.Owner.Advance(command, tick); Advances++; processed++;
                if (command.Phase == TacticalCommandPhase.Complete && command.RaidLord?.LordJob is LordJob_HelodSweep sweep
                    && !sweep.RetreatRequested && commands.Where(c => c.RaidLord == command.RaidLord).All(c => c.Terminal || c.Phase == TacticalCommandPhase.Complete))
                    command.RaidLord.ReceiveMemo("HelodSweepComplete");
                if (Stopwatch.GetTimestamp() - started > Stopwatch.Frequency * .0015)
                { BudgetStops++; break; }
            }
        }
        internal void Forget(TacticalSquadCommand command)
        {
            int index = commands.IndexOf(command);
            if (index >= 0)
            {
                commands.RemoveAt(index);
                if (index < cursor) cursor--;
            }
            Communications.Forget(command, commands, GenTicks.TicksGame);
        }
        private void DiscoverOne(int tick)
        {
            if (tick < discoverAt) return;
            GameComponent_CombatOrganizations registry = OrganizationAPI.Registry;
            if (registry == null) { discoverAt = tick + TacticalCadence.DecisionTicks; return; }
            if (discovering != null && (registry.GetOrganization(discovering.id) != discovering
                || discovering.StructureRevision != discoveryRevision))
            { discovery?.Dispose(); discovery = null; discovering = null; }
            if (discovery == null)
            {
                if (discoverOrganization >= registry.Organizations.Count)
                { discoverOrganization = 0; discoverAt = tick + TacticalCadence.DecisionTicks; return; }
                discovering = registry.Organizations[discoverOrganization++];
                discoveryRevision = discovering.StructureRevision;
                discovery = RaidTacticalUnit.ForOrganization(discovering).GetEnumerator();
            }
            // A registration sweep is spread over ticks, including after load.
            if (!discovery.MoveNext())
            { discovery.Dispose(); discovery = null; discovering = null; return; }
            RaidTacticalUnit unit = discovery.Current;
            Pawn pawn = unit.Members.FirstOrDefault(member => member.Spawned && !member.Dead);
            if (pawn == null || !IsTacticalLord(pawn.GetLord())) return;
            MapComponent_TacticalCommands service = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
            if (service == null || !CanControlMission(IsDefensiveLord(pawn.GetLord()), unit.Faction.HostileTo(Faction.OfPlayer), service.HasExplicitGoal)) return;
            TacticalSquadCommand command = service.Register(unit, tick);
            if (command != null) commands.Add(command);
        }
    }

    [NewTactical]
    public sealed partial class MapComponent_TacticalCommands : MapComponent
    {
        private readonly Dictionary<string, TacticalSquadCommand> squads = new Dictionary<string, TacticalSquadCommand>();
        private readonly Dictionary<Pawn, TacticalSquadCommand> byPawn = new Dictionary<Pawn, TacticalSquadCommand>();
        private readonly Dictionary<IntVec3, TacticalSquadCommand> claims = new Dictionary<IntVec3, TacticalSquadCommand>();
        private readonly Dictionary<IntVec3, TacticalSquadCommand> leases = new Dictionary<IntVec3, TacticalSquadCommand>();
        private readonly List<TacticalLocalPlan> knownOpenings = new List<TacticalLocalPlan>(8);
        private IntVec3 explicitGoal = IntVec3.Invalid, automaticGoal = IntVec3.Invalid;
        private int goalRetry;
        public long JobsIssued, JobFailures, PlansAttempted, PlansBuilt, BusyOpeningFallbacks;
        public string LastJobFailure;
        public IEnumerable<TacticalSquadCommand> Commands => squads.Values;
        public int OwnedPawnCount => byPawn.Count;
        public int ClaimCount => claims.Count;
        public int LeaseCount => leases.Count;
        public int KnownOpeningCount => knownOpenings.Count;
        public bool HasExplicitGoal => explicitGoal.IsValid;
        public MapComponent_TacticalCommands(Map map) : base(map) { }

        // Profiler-only read: never register or rebuild an organization.
        internal bool TryProfileContext(Pawn pawn, out string id, out TacticalCommandPhase phase)
        {
            if (byPawn.TryGetValue(pawn, out TacticalSquadCommand command))
            { id = command.Id; phase = command.Phase; return true; }
            id = null; phase = default; return false;
        }

        // Public/debug mission input. This also permits an AI-v-AI target.
        public void SetObjective(IntVec3 goal, Faction targetFaction = null)
        {
            if (!goal.InBounds(map)) throw new ArgumentException("Objective is outside this map.");
            explicitGoal = goal;
            explicitTargetFaction = targetFaction ?? goal.GetEdifice(map)?.Faction;
            if (explicitTargetFaction == null)
                foreach (Thing thing in goal.GetThingList(map))
                    if (thing.Faction != null) { explicitTargetFaction = thing.Faction; break; }
            // A different objective does not erase a physically secured portal.
            // ReuseOpening still validates its face, occupancy and live passage.
            foreach (TacticalSquadCommand command in squads.Values.ToArray())
            {
                if (command.Terminal) { squads.Remove(command.Id); continue; }
                if (command.Defensive) continue;
                command.CancelEntryPreview();
                if (command.CqbTeams.Count > 0)
                {
                    Current.Game.GetComponent<GameComponent_TacticalCommands>().Communications.Announce(command, GenTicks.TicksGame, true);
                    command.Link.Cooperation.Abort();
                    command.CqbCancelRequested = command.CqbObjectiveChanged = true; command.Goal = goal;
                    command.Due = GenTicks.TicksGame + 1; continue;
                }
                command.CancelPreparation();
                Current.Game.GetComponent<GameComponent_TacticalCommands>().Communications.Announce(command, GenTicks.TicksGame, true);
                foreach (var member in command.Members) pendingFieldJobs.Remove(member.Pawn);
                // Never erase an already launched grenade's safety state.
                if (command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared
                    || command.ChargeAction?.Detonated == true && !command.ChargeAction.EffectsCleared)
                { command.ReplanAfterSupport = true; command.Goal = goal; command.Due = GenTicks.TicksGame + 1; continue; }
                AbandonCharge(command);
                if (command.MedicalCare != null) command.MedicalCare.CancelRequested = true;
                EndFieldResponse(command, GenTicks.TicksGame);
                FinishFocusPause(command, GenTicks.TicksGame);
                command.ContactResponse = null; command.ContactRestoring = false;
                ReleaseClaims(command); command.Plan = null; command.Goal = goal;
                command.OpeningAction = null;
                command.Mission = new TacticalAssaultMission();
                command.SharedSecuredCursor = 0;
                foreach (TacticalRoomFrontier frontier in command.Frontiers) frontier.Excluded = false;
                command.RoomScan = null; command.GoalSecured = command.SecuredCells.Contains(goal);
                command.RecoveryFrontier = null; command.RoomRecoveryUntil = 0;
                command.Phase = TacticalCommandPhase.Pending; command.Due = GenTicks.TicksGame;
                command.ReturnCursor = 0; command.ReleaseAfterReturn = false;
                command.PlanRetryAt = 0;
                foreach (TacticalMemberCommand member in command.Members)
                {
                    member.Passed = member.Crossed = member.Entered = member.EntryAssignmentDone = false;
                    // Preserve ownership until a replacement job is admitted.
                    member.RetryTick = 0;
                }
            }
        }
        internal TacticalSquadCommand Register(RaidTacticalUnit unit, int tick)
        {
            if (squads.ContainsKey(unit.Id)) return null;
            var command = new TacticalSquadCommand { Id = unit.Id, Owner = this, Due = tick, PhaseStarted = tick };
            command.Link.Unit = unit; command.Goal = Goal(tick);
            command.RaidLord = unit.Members.Select(p => p.GetLord()).FirstOrDefault(GameComponent_TacticalCommands.IsTacticalLord);
            foreach (Pawn pawn in unit.Members)
                if (pawn.Spawned && pawn.Map == map && !pawn.Dead && !byPawn.ContainsKey(pawn))
                {
                    CombatGroup group = OrganizationAPI.GetGroup(pawn);
                    string function = group?.roleAssignments.FirstOrDefault(a => a.pawn == pawn)?.combatRole?.combatFunction;
                    command.Members.Add(new TacticalMemberCommand { Pawn = pawn,
                        Fireteam = unit.Group.children.IndexOf(group),
                        CombatFunction = function,
                        Medic = OrganizationMedical.HasDuty(pawn),
                        AutomaticWeapon = function == "HAR" || function == "MachineGun" });
                    TacticalBreachTools.Remember(command, pawn);
                    RememberMedicalBags(command, pawn);
                    byPawn[pawn] = command;
                }
            if (command.Members.Count == 0) return null;
            for (int i = command.Members.Count * 2 / 3; i < command.Members.Count; i++) command.Members[i].Rear = true;
            if (GameComponent_TacticalCommands.IsDefensiveLord(command.RaidLord)) BeginDefense(command, tick);
            squads.Add(command.Id, command); return command;
        }
        public void Wake(Pawn pawn)
        {
            if (pawn != null && byPawn.TryGetValue(pawn, out TacticalSquadCommand command) && !command.Terminal)
            {
                if (pawn.Dead || pawn.Downed) command.BagRecoveryAt = Math.Min(command.BagRecoveryAt, GenTicks.TicksGame + 1);
                if(pawn.Downed)
                {
                    var root=command.CqbParent??command;
                    root.NextMedicalCheck=Math.Min(root.NextMedicalCheck,GenTicks.TicksGame+1);
                    root.MedicalCursor=0; root.MedicalCandidate=null; root.MedicalCandidateScore=0;
                }
                command.Due = Math.Min(command.Due, GenTicks.TicksGame + 1);
                if (command.CqbParent != null) command.CqbParent.Due = Math.Min(command.CqbParent.Due, GenTicks.TicksGame + 1);
            }
        }
        internal void JobFinished(Pawn pawn, Job job, JobCondition condition)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command)) return;
            TacticalMemberCommand member = command.Members.Find(item => item.Pawn == pawn);
            if (member == null || member.Job != job) return;
            member.Job = null;
            CqbNotice(command,TacticalCqbNotice.Failure,member.CqbExecutionVersion);
            if (job.def.defName == TacticalFurniturePolicy.JobName && command.FurnitureAction?.Worker == pawn
                && condition != JobCondition.Succeeded && condition != JobCondition.InterruptForced)
                command.FurnitureAction.Failed = true;
            bool safetyInterrupt = (condition == JobCondition.InterruptForced || condition == JobCondition.InterruptOptional)
                && TacticalExplosiveEvasionPolicy.Pending(pawn, command);
            if (TacticalCqbProgressPolicy.RealFailure(condition,safetyInterrupt))
            {
                JobFailures++; member.RetryTick = GenTicks.TicksGame + 180;
                LastJobFailure = job.def.defName + ":" + condition + " pawn=" + pawn.thingIDNumber;
            }
            Wake(pawn);
        }
        internal void PassedOpening(Pawn pawn, Job job, IntVec3 at, int version = -1, string ownerId = null)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.Plan == null) return;
            TacticalMemberCommand member = command.Members.Find(item => item.Pawn == pawn);
            if (member?.Job == job && command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared) UnsafeEntries++;
            if (member?.Job == job && command.ChargeAction?.Detonated == true && !command.ChargeAction.EffectsCleared) UnsafeEntries++;
            if (member?.Job == job && (version<0 || version==command.PlanVersion) && (ownerId==null || ownerId==command.Id) && at == command.Plan.Opening) member.Passed = true;
        }
        internal void CrossedInside(Pawn pawn, Job job, int version = -1, string ownerId = null)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command)) return;
            TacticalMemberCommand member = command.Members.Find(item => item.Pawn == pawn);
            if (member?.Job == job && (version<0 || version==command.PlanVersion) && (ownerId==null || ownerId==command.Id))
            { member.Crossed = true; CqbNotice(command,TacticalCqbNotice.Passage,command.PlanVersion); }
        }
        private static bool Available(TacticalMemberCommand member, Map map) => member.Pawn?.Spawned == true
            && member.Pawn.Map == map && !member.Pawn.Dead && !member.Pawn.Downed && !member.Pawn.InMentalState
            && GameComponent_TacticalCommands.IsTacticalPawn(member.Pawn);

        public void Advance(TacticalSquadCommand command, int tick)
        {
            command.DeferredWork = false; command.CqbStepped = false;
            command.CqbWait=TacticalCqbWait.None;
            try { AdvanceCore(command, tick); }
            finally
            {
                if (command.CqbTeams.Count > 0 && !command.CqbStepped && command.CqbPausedAt < 0) command.CqbPausedAt = tick;
                if (command.DeferredWork && !command.Terminal)
                    command.Due = Math.Min(command.Due, tick + 1);
            }
        }
        private void AdvanceCore(TacticalSquadCommand command, int tick)
        {
            if(AdvanceCasualties(command,tick)) return;
            if (command.CqbParent == null && command.RaidLord?.LordJob is LordJob_HelodSweep sweep && sweep.RetreatRequested
                && command.Phase != TacticalCommandPhase.Returning && !command.ReleaseAfterReturn)
            {
                if (command.MedicalCare != null) command.MedicalCare.CancelRequested = true;
                BeginReturn(command, tick, true);
            }
            if (command.Phase != TacticalCommandPhase.Enter && command.Phase != TacticalCommandPhase.Clear) command.CancelEntryPreview();
            if (command.Phase != TacticalCommandPhase.Pending && command.Phase != TacticalCommandPhase.Clear) command.CancelPreparation();
            if (PauseForExplosiveEvasion(command, tick)) return;
            command.FurnitureProbes = TacticalFurniturePolicy.SimulationsPerAdvance;
            if (command.Phase == TacticalCommandPhase.Returning)
            {
                if (SupportEffectsPending(command, tick) || ChargeEffectsPending(command, tick)) { command.Due = tick + 15; return; }
                AbandonCharge(command);
                if (command.ReplanAfterSupport) { ResetAfterSupport(command, tick); return; }
                ReturnMembers(command, tick); return;
            }
            command.Due = tick + TacticalCadence.DecisionInterval(command.Phase);
            RejoinTreatedMembers(command, tick);
            var active = command.Members.Where(member => Available(member, map) && member.Pawn.GetLord() == command.RaidLord).ToList();
            if (command.CqbParent == null && active.Count > 0) RaidSupport.Advance(command, tick);
            if (active.Count == 0)
            {
                if (command.CqbTeams.Count > 0)
                { command.CqbCancelRequested = command.ReleaseAfterReturn = true; AdvanceHighCqb(command, active, tick); }
                else Release(command);
                return;
            }
            if (command.CqbParent != null)
            {
                if (AdvanceCommittedIngress(command, active, tick)) return;
                if (RespondToContacts(command, active, tick) || RestoreContactPosts(command, active, tick)) return;
                if (RefreshSharedPassage(command, tick)) return;
                AdvanceEntryPosts(command, active, tick);
                AdvanceAssault(command, active, tick); return;
            }
            if (command.CqbTeams.Count > 0 && (command.CqbCancelRequested || command.CqbTeams.Any(CqbLiveAction)))
            { AdvanceHighCqb(command, active, tick); return; }
            if (UpdateDefensiveMission(command, tick)) return;
            SyncSharedSecured(command, tick);
            if (command.Phase == TacticalCommandPhase.Complete && command.FocusFire == null
                && !command.Contacts.Melee.Dangerous(tick) && command.ContactResponse?.DirectAttack != true && !ResumeExpiredAllocation(command, tick))
            {
                ScanContacts(command, active, tick);
                if (!command.Contacts.Melee.Dangerous(tick) && command.Contacts.Memory.Entries.Count == 0)
                { command.Due = Math.Min(command.Due, tick + 600); return; }
                command.Phase = TacticalCommandPhase.Clear;
                command.Mission.Stage = TacticalMissionStage.Searching;
                command.Mission.SearchAt = tick; command.Mission.EmptySurveys = 0;
            }
            ScanContacts(command, active, tick);
            command.Due = Math.Min(command.Due, Math.Max(tick + 1, command.Contacts.NextScan));
            if (AdvanceCommittedIngress(command, active, tick)) return;
            if (command.MedicalCare != null && AdvanceMedicalCare(command, active, tick)) return;
            if (command.BagRecovery != null && AdvanceMedicalBagRecovery(command, active, tick)) return;
            if (AdvanceFocusFire(command, active, tick)) return;
            if (AdvanceMedicalBagRecovery(command, active, tick)) return;
            if (AdvanceMedicalCare(command, active, tick)) return;
            if (AdvanceUnderbarrelReload(command,active,tick)) return;
            if (AdvanceFieldResponse(command, active, tick)) return;
            if (RespondToContacts(command, active, tick)) return;
            if (RestoreContactPosts(command, active, tick)) return;
            if (RefreshSharedPassage(command, tick)) return;
            if (command.Defensive) { AdvanceDefenseIdle(command, active, tick); return; }
            if (command.CqbTeams.Count == 0) AdvanceEntryPosts(command, active, tick);
            if (AdvanceCoordination(command, active, tick)) return;
            if (AdvanceHighCqb(command, active, tick)) return;
            AdvanceAssault(command, active, tick);
        }
        private void AdvanceAssault(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            RefreshCqbProgress(command,active);
            if (AwaitPendingFurniture(command, active, tick)) return;
            if (AdvanceFurniture(command, active, tick)) return;
            if (command.Phase == TacticalCommandPhase.Clear)
            { AdvanceRoomClear(command, active, tick); return; }
            if (command.Phase == TacticalCommandPhase.Breach && command.ChargeAction != null)
            { AdvanceCharge(command, active, tick); return; }
            foreach (TacticalMemberCommand member in active)
            {
                if (member.Pawn.Position != member.LastPosition)
                { member.LastPosition = member.Pawn.Position; member.LastProgressTick = tick; }
                if ((member.Job == null && tick < member.RetryTick
                    || member.Pawn.CurJob == member.Job && member.Pawn.jobs.curDriver is TacticalJobDriver stalled
                        && !stalled.AtPost && !(stalled is JobDriver_TacticalBreach) && tick - member.LastProgressTick > 180)
                    && RecoverStalledFurniture(command, active, member)) return;
                if (member.Pawn.CurJob == member.Job && member.Pawn.jobs.curDriver is TacticalJobDriver driver
                    && !driver.AtPost && !(driver is JobDriver_TacticalBreach) && tick - member.LastProgressTick > 1200)
                {
                    // A live but stalled path must also have a bounded retry.
                    // Replace a stalled job directly, without a transient vanilla
                    // think-tree search between cancelling it and issuing a post.
                    if (!CanIssue(member)) continue;
                    IntVec3 post = RetryPost(command, member);
                    if (Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalPost"), post, command.Goal)))
                    { JobFailures++; member.RetryTick = tick + 180; member.LastProgressTick = tick; }
                    continue;
                }
                if (member.Job == null && tick < member.RetryTick && member.Pawn.CurJob?.playerForced != true)
                {
                    // Do not let a failed tactical job briefly acquire vanilla
                    // escort/wander orders while its bounded retry is pending.
                    // Reuse the existing assigned post; no plan is recalculated.
                    if (!CanIssue(member)) continue;
                    IntVec3 post = RetryPost(command, member);
                    Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalPost"), post, command.Goal));
                }
            }
            if (command.Phase == TacticalCommandPhase.Pending)
            {
                if (tick < command.PlanRetryAt)
                {
                    // Deferred parking jobs must not turn a 240-tick plan retry
                    // into a new geometric search every tick.
                    foreach (TacticalMemberCommand member in active) EnsureParking(command, member, tick);
                    command.Due = command.PlanRetryAt; return;
                }
                if (command.CqbParent == null) command.Goal = Goal(tick);
                if (!command.Goal.IsValid) { command.Due = tick + 300; return; }
                TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                IntVec3 planningGoal = PendingGoal(command);
                if (!planningGoal.IsValid)
                {
                    command.Phase = TacticalCommandPhase.Clear; command.FrontierCursor = 0;
                    command.Due = tick + 1; return;
                }
                Pawn leader = PendingPlanner(active, planningGoal);
                Pawn hammer = active.Select(member => member.Pawn).FirstOrDefault(pawn => BreachExplosiveUtility.CanOperate(pawn)
                    && (CompSledgehammerBreach.WornBy(pawn) != null || pawn.equipment?.Primary?.TryGetComp<CompPowerCutterBreach>() != null
                        || BreachExplosiveUtility.CountInInventory(pawn, BreachExplosiveUtility.C4Def) > 0 && TacticalBreachTools.IgniterFor(pawn) != null));
                if (!budget.TryPlan(tick)) { command.Due = tick + 1; return; }
                PrepareStructureTargets(command, tick);
                bool Claimed(IntVec3 cell) => claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner != command;
                long planStarted = Stopwatch.GetTimestamp();
                TacticalLocalPlan plan;
                TacticalPlanFailure failure;
                try
                {
                    command.Breacher = hammer;
                    if (!PreparePendingPlan(command, active, leader, planningGoal, Claimed, out plan, out failure))
                    { command.Due = tick + 1; return; }
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - planStarted); }
                command.LastPlanFailure = failure;
                if (command.FurnitureAction != null) { command.Due = tick + 1; return; }
                if (plan == null)
                {
                    // Try usable passages first. Tool recovery must not gather
                    // a squad that can simply walk through an already clear room.
                    if (hammer == null && tick - command.PhaseStarted < 1200 && RecoverBreachTool(command, active, tick))
                    {
                        foreach (TacticalMemberCommand member in active)
                            if (member.Pawn != command.Breacher) EnsureParking(command, member, tick);
                        command.Due = tick + 30; return;
                    }
                    if ((failure & TacticalPlanFailure.Classification) != 0)
                    {
                        foreach (TacticalMemberCommand member in active) EnsureParking(command, member, tick);
                        command.Due = tick + 1; return;
                    }
                    if (command.Failures >= 3 || hammer == null)
                    {
                        command.Mission.Stage = TacticalMissionStage.Blocked;
                        if (command.Mission.NextGoal.IsValid && command.Mission.FailedDestinations.Count < 32)
                            command.Mission.FailedDestinations.Add(command.Mission.NextGoal);
                        command.Mission.ForgetFailuresAt = tick + 1200;
                        command.Mission.FailedPlans++; command.Mission.NextGoal = IntVec3.Invalid;
                        foreach (TacticalMemberCommand member in active) EnsureParking(command, member, tick);
                        command.Phase = TacticalCommandPhase.Clear; command.FrontierCursor = command.Frontiers.Count;
                        command.Mission.Survey = null; command.Mission.EmptySurveys = 0;
                        command.Mission.SearchAt = tick + 600; command.Due = tick + 600;
                        return;
                    }
                    // Waiting for another squad's physical footprint does not let
                    // vanilla wander through an unrelated entrance in the meantime.
                    foreach (TacticalMemberCommand member in active)
                        EnsureParking(command, member, tick);
                    command.Failures++; command.PlanRetryAt = tick + 240;
                    command.Due = command.PlanRetryAt; return;
                }
                ReleaseClaims(command); command.Plan = plan; PlansBuilt++; command.PhaseStarted = tick;
                command.MovementCursor = 0; command.MovementDispatchAt = tick;
                plan.ReportedPassage = TacticalSharedPassagePolicy.Cleared(command.SecuredKnowledge, plan)
                    && (plan.Direct || OpeningUsable(plan));
                command.Link.Cooperation.LocalReady = false;
                command.OpeningAction = null;
                command.HadConnectedStack = plan.Direct || plan.ReportedPassage || TacticalEntryAllocation.ConnectedStack(plan);
                if (plan.Direct || plan.ReportedPassage)
                {
                    foreach (IntVec3 cell in plan.Positions) claims[cell] = command;
                    command.Phase = TacticalCommandPhase.Enter;
                    if (!plan.Direct) RememberOpening(plan);
                }
                else
                {
                    for (int layer = 0; layer < plan.Depth; layer++)
                    {
                        IntVec3 cell = plan.Opening + plan.Inward * layer;
                        leases[cell] = command; claims[cell] = command;
                    }
                    foreach (IntVec3 cell in plan.Stack.Concat(plan.Positions)) claims[cell] = command;
                    claims[plan.Outside] = command;
                    claims[plan.Inside] = command;
                    if (plan.EntryLane.IsValid) claims[plan.EntryLane] = command;
                    claims[plan.Opening] = command;
                    command.Breacher = hammer;
                    // Already-open doors/gaps skip Stack/Breach, whose normal
                    // transition records the portal. Keep the validated local
                    // footprint for later squads as well; ReuseOpening still
                    // validates live passage/claims against shared geometry.
                    if (plan.ExistingOpening) RememberOpening(plan);
                    command.Phase = plan.ExistingOpening ? TacticalCommandPhase.Observe : TacticalCommandPhase.Stack;
                }
            }
            TacticalLocalPlan current = command.Plan;
            if (command.Phase == TacticalCommandPhase.Stack || command.Phase == TacticalCommandPhase.Breach)
            {
                int ready = 0;
                bool dispatch = tick >= command.MovementDispatchAt;
                int issued = 0, start = command.MovementCursor;
                if (dispatch) command.MovementDispatchAt = tick + TacticalCadence.DecisionTicks;
                for (int i = 0; i < command.Members.Count; i++)
                {
                    TacticalMemberCommand member = command.Members[i]; if (!Available(member, map)) continue;
                    if (command.Phase == TacticalCommandPhase.Breach && member.Pawn == command.Breacher) continue;
                    if (AtPost(member) && member.Pawn.Position == current.Stack[i])
                    { ready++; TacticalStackSecurity.Arrived(current,i,member.Pawn.Position); }
                    if (!StationaryGuidance(member))
                        PublishPostIntent(member,current.Stack[i],TacticalStackSecurity.Face(current,i,current.Stack[i]));
                }
                if (dispatch)
                    for (int n = 0; n < command.Members.Count && issued < TacticalCadence.MovementBatch; n++)
                    {
                        int i = (start + n) % command.Members.Count;
                        TacticalMemberCommand member = command.Members[i];
                        if (!Available(member, map) || command.Phase == TacticalCommandPhase.Breach && member.Pawn == command.Breacher
                            || member.Pawn.CurJob?.playerForced == true || StationaryGuidance(member)
                            || HoldsPost(member, current.Stack[i], false)
                                && member.Job.targetB.Cell == TacticalStackSecurity.Face(current,i,current.Stack[i])) continue;
                        EnsurePost(member, current.Stack[i], TacticalStackSecurity.Face(current,i,current.Stack[i]), tick);
                        command.MovementCursor = (i + 1) % command.Members.Count; issued++;
                    }
                if (OpeningUsable(current))
                {
                    if (command.Phase == TacticalCommandPhase.Stack && !CooperationReady(command, tick)) return;
                    RememberOpening(current); command.Phase = TacticalCommandPhase.Observe; command.PhaseStarted = tick;
                }
                else if (command.Phase == TacticalCommandPhase.Stack)
                {
                    // A lost straggler must not keep the entire squad frozen.
                    if (!CqbBreachReady(command,active)) return;
                    if (!CooperationReady(command, tick)) { command.CqbWait=TacticalCqbWait.Cooperation; return; }
                    command.Phase = TacticalCommandPhase.Breach; command.PhaseStarted = tick;
                }
                if (command.Phase == TacticalCommandPhase.Breach)
                {
                    current.Barrier = TacticalLocalPlanner.NextBarrier(map, current);
                    int hitPoints = current.Barrier?.HitPoints ?? 0;
                    if (hitPoints != command.BarrierHitPoints)
                    { command.BarrierHitPoints = hitPoints; command.PhaseStarted = tick; }
                    if (tick - command.PhaseStarted > 1200)
                    { Release(command); return; }
                    TacticalMemberCommand worker = active.FirstOrDefault(member => member.Pawn == command.Breacher
                        && CanBreachPlan(member.Pawn, current));
                    worker = worker ?? active.FirstOrDefault(member => CanBreachPlan(member.Pawn, current));
                    if (worker == null || !CompSledgehammerBreach.CanOperate(worker.Pawn))
                    {
                        if (!RecoverBreachTool(command, active, tick)) Release(command);
                        else command.Due = Math.Min(command.Due, tick + 30);
                        return;
                    }
                    command.Breacher = worker.Pawn;
                    bool cutter = worker.Pawn.equipment?.Primary?.TryGetComp<CompPowerCutterBreach>() != null
                        && (!(current.Barrier is Building_Door) || CompSledgehammerBreach.WornBy(worker.Pawn) == null
                            || !CompSledgehammerBreach.IsValidTarget(worker.Pawn, current.Barrier));
                    bool hammerDoor = current.Barrier is Building_Door && CompSledgehammerBreach.WornBy(worker.Pawn) != null
                        && CompSledgehammerBreach.IsValidTarget(worker.Pawn, current.Barrier);
                    if (!current.HammerFallback && !cutter && !hammerDoor && TacticalBreachTools.CanCharge(worker.Pawn, current.Barrier))
                    {
                        if (BeginCharge(command, worker, tick)) { AdvanceCharge(command, active, tick); return; }
                        if (CompSledgehammerBreach.WornBy(worker.Pawn) == null) { Release(command); return; }
                    }
                    string breachJob = cutter ? TacticalBreachTools.CutterJob : "HD_NewTacticalBreach";
                    if (worker.Job?.def.defName != breachJob || worker.Pawn.CurJob != worker.Job || worker.Job.targetA.Thing != current.Barrier)
                    {
                        if (tick < worker.RetryTick) return;
                        if (!CanIssue(worker)) return;
                        Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(breachJob), current.Barrier,
                            current.Barrier.Position - current.Inward, cutter ? worker.Pawn.equipment.Primary : CompSledgehammerBreach.WornBy(worker.Pawn).parent);
                        if (Issue(worker, job) && cutter) { CutterJobsStarted++; command.PhaseStarted = tick; }
                    }
                    else if (worker.Pawn.jobs.curDriver is JobDriver_TacticalCut cutting && cutting.CuttingActive)
                        command.PhaseStarted = tick;
                    command.Due = Math.Min(command.Due, tick + 30);
                    return;
                }
            }
            if (command.Phase == TacticalCommandPhase.Observe || command.Phase == TacticalCommandPhase.Support
                || command.Phase == TacticalCommandPhase.BlastWait)
            {
                if (command.Phase == TacticalCommandPhase.Observe && current.ReportedPassage
                    && TacticalSharedPassagePolicy.Cleared(command.SecuredKnowledge, current) && OpeningUsable(current)
                    && command.OpeningAction == null && command.ChargeAction == null)
                    command.Phase = TacticalCommandPhase.Enter;
                if (command.Phase == TacticalCommandPhase.Observe && command.OpeningAction == null
                    && !CooperationReady(command, tick)) return;
                if (command.Phase != TacticalCommandPhase.Enter) AdvanceOpeningAction(command, active, tick);
                if (command.Phase != TacticalCommandPhase.Enter) return;
            }
            if (command.Phase != TacticalCommandPhase.Enter) return;
            AvoidObservedOccupiedPost(command);
            if (!current.Direct && !OpeningUsable(current))
            {
                if (current.ReportedPassage)
                {
                    // Destination-only transit posts cannot become a breach
                    // formation when somebody closes/blocks the shared passage.
                    BeginReturn(command, tick, false, replan: true); return;
                }
                // Local obstruction, not global topology refresh. Keep the
                // selected entrance and retry the breach, with bounded latency.
                current.Barrier = TacticalLocalPlanner.NextBarrier(map, current);
                command.Phase = TacticalCommandPhase.Breach; command.Due = tick + 180; return;
            }
            if (current.RetainedOutside.Count > 0 && active.All(m => current.RetainedOutside.Contains(command.Members.IndexOf(m))))
            {
                int vacant = command.Members.FindIndex(m => !Available(m, map)
                    && !current.RetainedOutside.Contains(command.Members.IndexOf(m)));
                if (vacant >= 0)
                {
                    TacticalMemberCommand replacement = active.Find(m => current.StackMembers.Count == 0
                        || current.StackMembers.Contains(command.Members.IndexOf(m)));
                    if (replacement == null) { BeginReturn(command,tick,false,replan:true); return; }
                    if (TacticalEntryAllocation.Promote(current, command.Members.IndexOf(replacement), vacant))
                    {
                        replacement.EntryAssignmentDone = false;
                        TacticalMemberCommand lost = command.Members[vacant];
                        lost.Passed = lost.Crossed = lost.Entered = lost.EntryAssignmentDone = false;
                    }
                }
            }
            bool dispatchEntry = tick >= command.MovementDispatchAt;
            int entryIssues = 0;
            if (dispatchEntry) command.MovementDispatchAt = tick + TacticalCadence.DecisionTicks;
            for (int ordinal = 0; ordinal < command.Members.Count; ordinal++)
            {
                int i = current.EntryOrder.Count == command.Members.Count ? current.EntryOrder[ordinal] : ordinal;
                TacticalMemberCommand member = command.Members[i]; if (!Available(member, map)) continue;
                if (current.RetainedOutside.Contains(i))
                {
                    PublishPostIntent(member,current.Positions[i],TacticalStackSecurity.Face(current,i,current.Positions[i]),true);
                    member.EntryAssignmentDone = AtPost(member) && member.Pawn.Position == current.Positions[i];
                    IntVec3 face = TacticalStackSecurity.Face(current,i,current.Positions[i]);
                    if ((ContinuingActor(member) || dispatchEntry && entryIssues < TacticalCadence.MovementBatch)
                        && (!HoldsPost(member,current.Positions[i],true) || member.Job.targetB.Cell != face))
                    { EnsurePost(member,current.Positions[i],face,tick,entryCombat:true); entryIssues++; }
                    continue;
                }
                if (member.Pawn.jobs.curDriver is TacticalJobDriver driver && member.Pawn.CurJob == member.Job && driver.AtPost)
                    member.Entered |= member.Pawn.Position == current.Positions[i] && (current.Direct || member.Crossed && member.Passed);
                member.EverEntered |= member.Entered;
                member.EntryAssignmentDone = member.Entered && member.Pawn.CurJob == member.Job
                    && member.Pawn.jobs.curDriver is TacticalJobDriver arrival && arrival.AtPost
                    && member.Pawn.Position == current.Positions[i];
                if (member.Entered) continue;
                if (!TacticalStackSecurity.ReadyToEnter(current,i))
                {
                    if (AtPost(member)) TacticalStackSecurity.Arrived(current,i,member.Pawn.Position);
                    if (!TacticalStackSecurity.ReadyToEnter(current,i))
                    {
                        PublishPostIntent(member,current.Stack[i],TacticalStackSecurity.Face(current,i,current.Stack[i]));
                        if ((ContinuingActor(member) || dispatchEntry && entryIssues < TacticalCadence.MovementBatch) && !StationaryGuidance(member))
                        { EnsurePost(member,current.Stack[i],TacticalStackSecurity.Face(current,i,current.Stack[i]),tick); entryIssues++; }
                        continue;
                    }
                }
                if (!TacticalHighCqbPolicy.EntryReleased(command, i, active)) continue;
                if(current.Direct) PublishPostIntent(member,current.Positions[i],command.Goal,true);
                else PublishIngressIntent(command,member,i);
                if (!ContinuingActor(member) && (!dispatchEntry || entryIssues >= TacticalCadence.MovementBatch)) continue;
                if (member.Pawn.CurJob?.playerForced == true || StationaryGuidance(member)) continue;
                if (current.Direct)
                {
                    if (!HoldsPost(member, current.Positions[i], true))
                    { EnsurePost(member, current.Positions[i], command.Goal, tick, entryCombat: true); entryIssues++; }
                }
                else if (!IngressJob(member) || member.Pawn.CurJob != member.Job
                    || member.Pawn.jobs.curDriver is TacticalJobDriver stopped && stopped.AtPost)
                {
                    if (tick < member.RetryTick) continue;
                    if (!CanIssue(member)) continue;
                    Job ingress = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalIngress"),
                        current.Outside, current.Opening, current.Positions[i]);
                    ingress.targetQueueA = new List<LocalTargetInfo> { current.Inside };
                    if (!current.ReportedPassage && current.EntryLane.IsValid) ingress.targetQueueA.Add(current.EntryLane);
                    ingress.count = TacticalSharedPassagePolicy.IngressLeg(member.Crossed, member.Passed, current.ReportedPassage);
                    bool continuation=ContinuingActor(member);
                    Issue(member, ingress); if(!continuation) entryIssues++;
                }
            }
            AdvanceEntryPreview(command, active, tick);
            if (CqbEntryReady(command,active,tick))
            {
                if (HoldRoomCombat(command, active, tick)) return;
                // Keep owned posts while freeing the unused entry approach.
                // Vanilla next-job selection must not run for a whole squad here.
                BeginRoomClear(command, tick);
            }
        }
        private static Pawn PendingPlanner(IList<TacticalMemberCommand> active, IntVec3 goal)
        {
            // A formation may span the local portal-recognition range. Use a
            // forward member's actual position, not a rear roster index. The
            // caller already filtered availability; no extra LOS/reach query.
            Pawn nearest = active[0].Pawn;
            int distance = nearest.Position.DistanceToSquared(goal);
            for (int i = 1; i < active.Count; i++)
            {
                Pawn pawn = active[i].Pawn;
                int candidate = pawn.Position.DistanceToSquared(goal);
                if (candidate < distance) { nearest = pawn; distance = candidate; }
            }
            return nearest;
        }
        private static IntVec3 PendingGoal(TacticalSquadCommand command)
        {
            if (command.CqbFollow && command.CqbFollowGoal.IsValid) return command.CqbFollowGoal;
            if (command.CqbParent != null && command.CqbTask != null) return command.CqbTask.Inside;
            IntVec3 transit = TacticalMissionRouting.TransitGoal(command);
            if (transit.IsValid) return transit;
            if (command.GoalSecured || command.SecuredKnowledge.Cleared(command.Goal))
            {
                foreach (TacticalRoomFrontier frontier in command.Frontiers)
                    if (TacticalMissionRouting.UnenteredRoom(command, frontier)) return frontier.Inside;
                return IntVec3.Invalid;
            }
            return command.Goal;
        }
        private IntVec3 Goal(int tick)
        {
            if (explicitGoal.IsValid) return explicitGoal;
            if (automaticGoal.IsValid) return automaticGoal;
            if (tick < goalRetry) return IntVec3.Invalid;
            goalRetry = tick + 600;
            foreach (Building_Bed bed in map.listerThings.AllThings.OfType<Building_Bed>())
                if (bed.Spawned && bed.Faction == Faction.OfPlayer && bed.OwnersForReading.Any(pawn => pawn.Name != null))
                    return automaticGoal = bed.Position;
            return IntVec3.Invalid;
        }
        private bool OpeningUsable(TacticalLocalPlan plan)
        {
            return TacticalLocalPlanner.PassageOpen(map, plan);
        }
        private void RememberOpening(TacticalLocalPlan plan)
        {
            if (knownOpenings.Count < 8 && !knownOpenings.Any(known => known.Opening == plan.Opening)) knownOpenings.Add(plan);
            if (!SharedSecured.KnownPassage(plan.Opening, plan.Inward, plan.Depth))
                SharedSecured.Record(new TacticalSecuredReport { Opening = plan.Opening, Inward = plan.Inward, Depth = plan.Depth,
                    PortalUsable = true, SeenTick = GenTicks.TicksGame });
        }
        private TacticalLocalPlan ReuseOpening(TacticalSquadCommand command, Pawn leader, IntVec3 goal, int count, Func<IntVec3,bool> claimed,
            out bool tried, out TacticalPlanFailure failure)
        {
            tried = false; failure = TacticalPlanFailure.None; int probes = 0;
            foreach (TacticalLocalPlan known in knownOpenings)
            {
                if (command.SecuredCells.Contains(known.Inside)) continue;
                if (!TargetPlan(command, known, leader, out bool classificationPending))
                {
                    if (classificationPending) { tried = true; failure = TacticalPlanFailure.Classification; return null; }
                    continue;
                }
                if (!command.SecuredKnowledge.KnownOpening(known.Opening)
                    && !command.SecuredPlans.Any(p => p.Opening == known.Opening)
                    && (leader.Position.DistanceToSquared(known.Opening) > 784
                        || !GenSight.LineOfSight(leader.Position, known.Opening, map, true))) continue;
                IntVec3 travel = goal - leader.Position, relative = leader.Position - known.Opening;
                // Reuse only the same approach face, never route an already
                // indoor squad back out or an opposite-edge squad around a base.
                if (travel.x * known.Inward.x + travel.z * known.Inward.z <= 0
                    || relative.x * known.Inward.x + relative.z * known.Inward.z >= 1
                    || known.Outside.DistanceToSquared(leader.Position) > goal.DistanceToSquared(leader.Position)
                    || !OpeningUsable(known)) continue;
                if (TacticalSharedPassagePolicy.Cleared(command.SecuredKnowledge, known))
                {
                    tried = true;
                    if (probes++ >= 2) break;
                    var transit = new TacticalLocalPlan { Opening = known.Opening, Inward = known.Inward,
                        Depth = known.Depth, ExistingOpening = true };
                    if (!TacticalLocalPlanner.BuildClearedTransit(map, transit, count, claimed))
                    { failure |= TacticalPlanFailure.Inside; continue; }
                    if (!leader.CanReach(transit.Inside, PathEndMode.OnCell, Danger.Deadly))
                    { failure |= TacticalPlanFailure.Unreachable; continue; }
                    failure = TacticalPlanFailure.None; return transit;
                }
                // Guard assignments depend on this squad's secured history.
                if (known.RetainedOutside.Count > 0 || known.Stack.Count < count || known.Positions.Count < count) continue;
                bool physical = known.Outside.Standable(map) && known.Inside.Standable(map);
                physical &= !known.EntryLane.IsValid || known.EntryLane.Standable(map);
                for (int i = 0; physical && i < count; i++)
                    physical = known.Stack[i].Standable(map) && known.Positions[i].Standable(map);
                if (!physical) continue; // Changed obstruction: allow a fresh bounded plan.
                tried = true;
                bool busy = leases.ContainsKey(known.Opening) || claimed(known.Outside) || claimed(known.Inside);
                busy |= known.EntryLane.IsValid && claimed(known.EntryLane);
                for (int i = 0; !busy && i < count; i++) busy = claimed(known.Stack[i]) || claimed(known.Positions[i]);
                if (busy) { failure |= TacticalPlanFailure.Busy; continue; }
                if (probes++ >= 2) break;
                if (!leader.CanReach(known.Outside, PathEndMode.OnCell, Danger.Deadly))
                { failure |= TacticalPlanFailure.Unreachable; continue; }
                var plan = new TacticalLocalPlan { Opening = known.Opening, Inward = known.Inward, ExistingOpening = true,
                    Depth = known.Depth, StackSide = known.StackSide, Barrier = TacticalLocalPlanner.NextBarrier(map, known), EntryLane = known.EntryLane };
                for (int i = 0; i < count; i++) plan.Stack.Add(known.Stack[i]);
                foreach (int i in known.EntryOrder)
                    if (i < count && !known.RetainedOutside.Contains(i)) plan.Positions.Add(known.Positions[i]);
                plan.StackMembers.AddRange(known.StackMembers.Where(i => i < count));
                plan.Interior.UnionWith(known.Interior);
                failure = TacticalPlanFailure.None; return plan;
            }
            return !tried ? ReuseReportedOpening(command, leader, goal, count, claimed, out tried, out failure) : null;
        }
        private static bool AtPost(TacticalMemberCommand member) => member.Pawn.CurJob == member.Job
            && member.Pawn.jobs.curDriver is TacticalJobDriver driver && driver.AtPost
            && member.Pawn.Position == driver.PostCell;
        private bool HoldsPost(TacticalMemberCommand member, IntVec3 position, bool entryCombat) =>
            member.Pawn.CurJob == member.Job && (ContinuingActor(member)
                ? ActorValid(member,member.CqbOrder) && member.CqbOrder.Ingress==false
                    && member.CqbOrder.Destination==position && member.CqbOrder.Combat==entryCombat
                : member.Job?.def.defName == "HD_NewTacticalPost" && member.Job.targetA.Cell == position && member.Job.count == (entryCombat ? 1 : 0))
                && (!(member.Pawn.jobs.curDriver is TacticalJobDriver driver) || !driver.AtPost || member.Pawn.Position == position);
        private void EnsurePost(TacticalMemberCommand member, IntVec3 position, IntVec3 face, int tick, bool entryCombat = false)
        {
            if (HoldsPost(member, position, entryCombat) && member.Job.targetB.Cell == face) return;
            if (member.Pawn.CurJob?.playerForced == true || tick < member.RetryTick) return;
            if (ContinuingActor(member) && ActorContext(member,out var c)
                && !TacticalExplosiveEvasionPolicy.Pending(member.Pawn,c))
            { PublishPostIntent(member,position,face,entryCombat); return; }
            if (!ContinuingActor(member) && !CanIssue(member)) return;
            Job post = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalPost"), position, face);
            post.count = entryCombat ? 1 : 0;
            if (entryCombat) post.canUseRangedWeapon = false;
            Issue(member, post);
        }
        private void EnsureParking(TacticalSquadCommand command, TacticalMemberCommand member, int tick)
        {
            if (!member.Parking.IsValid || !claims.TryGetValue(member.Parking, out TacticalSquadCommand owner) || owner != command)
            {
                member.Parking = IntVec3.Invalid;
                int candidates = 0;
                for (int radius = 0; radius <= 2 && !member.Parking.IsValid; radius++)
                    for (int x = -radius; x <= radius && !member.Parking.IsValid; x++)
                        for (int z = -radius; z <= radius && !member.Parking.IsValid; z++)
                        {
                            if (Math.Max(Math.Abs(x), Math.Abs(z)) != radius || candidates++ >= 25) continue;
                            IntVec3 cell = member.Pawn.Position + new IntVec3(x,0,z);
                            if (!TacticalLocalPlanner.Free(map, cell, claims.ContainsKey)) continue;
                            Pawn occupant = cell.GetFirstPawn(map);
                            if (occupant != null && occupant != member.Pawn) continue;
                            member.Parking = cell; claims[cell] = command;
                        }
            }
            if (member.Parking.IsValid) EnsurePost(member, member.Parking, command.Goal, tick);
        }
        private static IntVec3 RetryPost(TacticalSquadCommand command, TacticalMemberCommand member)
        {
            if (command.Plan == null) return member.Pawn.Position;
            int index = command.Members.IndexOf(member);
            bool guard = command.Plan.RetainedOutside.Contains(index)
                && (command.Phase == TacticalCommandPhase.Enter || command.Phase == TacticalCommandPhase.Clear);
            return member.Crossed || command.Plan.Direct || command.Plan.ReportedPassage || guard
                ? command.Plan.Positions[index] : command.Plan.Stack[index];
        }
        private bool Issue(TacticalMemberCommand member, Job job)
        {
            if (member.Pawn.CurJobDef?.defName == InventoryGrenadeUtility.ThrowJobName) return false;
            if (StationaryGuidance(member) || TacticalExplosiveEvasionPolicy.OwnsMovement(member.Pawn.CurJob)) return false;
            if (byPawn.TryGetValue(member.Pawn, out TacticalSquadCommand owner)
                && TacticalExplosiveEvasionPolicy.Pending(member.Pawn, owner)) return false;
            int tick = GenTicks.TicksGame;
            if (PrepareActor(member,ref job)) return true;
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryJob(tick))
            {
                Defer(member);
                return false;
            }
            // Set ownership before StartJob; an old job's finish notification
            // cannot clear the replacement. One owner, one persistent job.
            member.RoomWatchUntil = 0;
            member.Job = job; JobsIssued++;
            if(byPawn.TryGetValue(member.Pawn,out var execution)) member.CqbExecutionVersion=execution.PlanVersion;
            member.PostReady=false;
            member.LastPosition = member.Pawn.Position; member.LastProgressTick = GenTicks.TicksGame;
            if (job.def.defName == "HD_NewTacticalPost" && job.count != 1) job.count = 0;
            job.locomotionUrgency = job.def.defName == "HD_NewTacticalIngress"
                || (job.def.defName == "HD_NewTacticalPost" || job.def.defName == "HD_NewTacticalActor") && job.count == 1
                ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
            long started = Stopwatch.GetTimestamp();
            try
            {
                member.Pawn.jobs.StartJob(job, JobCondition.InterruptForced, resumeCurJobAfterwards: false,
                    cancelBusyStances: true, keepCarryingThingOverride: true);
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            return true;
        }
        private bool CanIssue(TacticalMemberCommand member)
        {
            if (member.Pawn.CurJobDef?.defName == InventoryGrenadeUtility.ThrowJobName) return false;
            if (StationaryGuidance(member) || TacticalExplosiveEvasionPolicy.OwnsMovement(member.Pawn.CurJob)) return false;
            if (byPawn.TryGetValue(member.Pawn, out TacticalSquadCommand owner)
                && TacticalExplosiveEvasionPolicy.Pending(member.Pawn, owner)) return false;
            if (ContinuingActor(member)) return true;
            if (Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.CanJob(GenTicks.TicksGame)) return true;
            Defer(member); return false;
        }
        private void Defer(TacticalMemberCommand member)
        {
            if (byPawn.TryGetValue(member.Pawn, out TacticalSquadCommand command))
            { command.DeferredWork = true; command.CqbWait=TacticalCqbWait.JobBudget; }
        }
        private static void EndOwned(TacticalMemberCommand member, bool startNewJob = true)
        {
            member.RoomWatchUntil = 0;
            Job owned = member.Job; member.Job = null;
            if (owned != null && member.Pawn?.CurJob == owned)
                member.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: startNewJob);
        }
        private void ReleaseClaims(TacticalSquadCommand command)
        {
            foreach (IntVec3 cell in claims.Where(pair => pair.Value == command).Select(pair => pair.Key).ToArray()) claims.Remove(cell);
            foreach (IntVec3 cell in leases.Where(pair => pair.Value == command).Select(pair => pair.Key).ToArray()) leases.Remove(cell);
        }
        private void Release(TacticalSquadCommand command)
        {
            BeginReturn(command, GenTicks.TicksGame, true);
        }
        private static void BeginReturn(TacticalSquadCommand command, int tick, bool release, bool replan = false)
        {
            command.CancelEntryPreview();
            if (command.CqbTeams.Count > 0)
            {
                command.CqbCancelRequested = true; command.ReleaseAfterReturn = release;
                command.ReplanAfterReturn = replan; command.Due = tick + 1; return;
            }
            command.CancelPreparation();
            Current.Game.GetComponent<GameComponent_TacticalCommands>().Communications.Announce(command, tick, release || replan);
            command.Phase = TacticalCommandPhase.Returning; command.ReleaseAfterReturn = release;
            command.ReplanAfterReturn = replan;
            command.ReturnCursor = 0; command.Due = tick + 1;
        }
        private void ReturnMembers(TacticalSquadCommand command, int tick)
        {
            command.Due = tick + 1;
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            while (command.ReturnCursor < command.Members.Count)
            {
                TacticalMemberCommand member = command.Members[command.ReturnCursor];
                if (member.Job != null && member.Pawn.Spawned && member.Pawn.Map == map && member.Pawn.CurJob == member.Job)
                {
                    if (!budget.TryJob(tick, returning: true)) return;
                    long started = Stopwatch.GetTimestamp();
                    try { EndOwned(member); }
                    finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
                }
                else member.Job = null;
                if (command.ReleaseAfterReturn) byPawn.Remove(member.Pawn);
                command.ReturnCursor++;
            }
            ReleaseClaims(command);
            command.MedicalCare = null;
            command.BagRecovery = null;
            command.FocusFire = null;
            command.FurnitureAction = null; command.FurnitureResumePlan = null;
            if (command.ReplanAfterReturn) { RestartRetainedMission(command, tick); return; }
            command.Phase = command.ReleaseAfterReturn ? TacticalCommandPhase.Released
                : command.Defensive ? TacticalCommandPhase.Defending : TacticalCommandPhase.Complete;
            command.Due = tick + 600;
            if (command.Terminal) Retire(command);
        }
        private static void RestartRetainedMission(TacticalSquadCommand command, int tick)
        {
            command.CancelEntryPreview();
            command.CancelPreparation();
            command.ReplanAfterReturn = false; command.ReleaseAfterReturn = false;
            command.Plan = null; command.OpeningAction = null; command.RoomScan = null;
            command.ContactRestoring = false; command.Breacher = null;
            command.RecoveryFrontier = null; command.RoomRecoveryUntil = 0;
            command.Phase = TacticalCommandPhase.Pending; command.PhaseStarted = tick;
            command.PlanRetryAt = command.Due = tick + 240; command.Failures = 0;
            foreach (TacticalMemberCommand member in command.Members)
            {
                member.Passed = member.Crossed = member.Entered = member.EntryAssignmentDone = false;
                member.Parking = IntVec3.Invalid; member.RetryTick = 0;
            }
            // Same scheduler/member ownership and secured history. No terminal
            // retirement, repeated registration or replacement knowledge graph.
        }
        internal void Retire(TacticalSquadCommand command)
        {
            EndHighCqb(command);
            command.CancelEntryPreview();
            command.CancelPreparation();
            command.Phase = TacticalCommandPhase.Released;
            foreach (var member in command.Members)
            {
                pendingFieldJobs.Remove(member.Pawn);
                member.Pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>()?.ReleaseAIMode();
            }
            foreach (TacticalMemberCommand member in command.Members)
                if (byPawn.TryGetValue(member.Pawn, out TacticalSquadCommand owner) && owner == command)
                    byPawn.Remove(member.Pawn);
            ReleaseClaims(command);
            squads.Remove(command.Id);
            if (!squads.Values.Any(c => c.Link.Unit.Organization == command.Link.Unit.Organization && !c.Terminal))
                RaidSupport.Cancel(command.Link.Unit.Organization);
            Current.Game?.GetComponent<GameComponent_TacticalCommands>()?.Forget(command);
            if (squads.Count == 0)
            {
                claims.Clear(); leases.Clear(); knownOpenings.Clear();
                SharedSecured = new TacticalSecuredKnowledge();
                automaticGoal = IntVec3.Invalid; goalRetry = 0;
            }
        }
        public override void MapRemoved()
        {
            // A removed map cannot keep a deferred queue alive. Clean ownership
            // without doing a new vanilla job search on a map being disposed.
            foreach (TacticalSquadCommand command in squads.Values)
            {
                EndHighCqb(command);
                command.CancelEntryPreview();
                command.CancelPreparation();
                foreach (TacticalMemberCommand member in command.Members) EndOwned(member, false);
                command.Phase = TacticalCommandPhase.Released;
                Current.Game?.GetComponent<GameComponent_TacticalCommands>()?.Forget(command);
            }
            squads.Clear(); byPawn.Clear(); claims.Clear(); leases.Clear(); knownOpenings.Clear();
            pendingFieldJobs.Clear();
            SharedSecured = new TacticalSecuredKnowledge();
        }
    }

    [NewTactical, HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord))]
    public static class Patch_NewTactical_RaidCreated
    {
        private static void Postfix(Lord __result) => Current.Game.GetComponent<GameComponent_TacticalCommands>()?.RegisterLord(__result);
    }
}
