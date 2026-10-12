using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public enum TacticalFocusStage { Focused, Returning, Normal }

    public sealed class TacticalFocusFire : IExposable
    {
        public TacticalFocusStage Stage;
        public int Started, NextDecision, SelectedAt, TargetId = -1, SeenTick, ReleaseCursor, NormalStarted, AdmissionCursor;
        public IntVec3 Target = IntVec3.Invalid;
        public bool Select(TacticalMeleeMemory memory, IntVec3 anchor, int tick)
        {
            if (tick < NextDecision) return false;
            NextDecision = tick + TacticalFocusPolicy.DecisionInterval;
            TacticalMeleeThreat selected = TacticalFocusPolicy.Select(memory.Entries, TargetId, SelectedAt, anchor, tick);
            if (selected != null)
            {
                bool changed = Stage != TacticalFocusStage.Focused || selected.EnemyId != TargetId;
                if (changed) SelectedAt = tick;
                Stage = TacticalFocusStage.Focused; TargetId = selected.EnemyId;
                Target = selected.Position; SeenTick = selected.SeenTick; return changed;
            }
            if (Stage == TacticalFocusStage.Focused) BeginReturn(tick);
            return false;
        }
        private void BeginReturn(int tick)
        {
            Stage = TacticalFocusStage.Returning; ReleaseCursor = 0;
            TargetId = -1; Target = IntVec3.Invalid; NormalStarted = tick;
        }
        public void Restore(TacticalMeleeMemory memory, int tick, int members)
        {
            ReleaseCursor = System.Math.Max(0, System.Math.Min(ReleaseCursor, members));
            AdmissionCursor = System.Math.Max(0, System.Math.Min(AdmissionCursor, members));
            NextDecision = tick;
            TacticalMeleeThreat target = memory.Find(TargetId);
            if (!System.Enum.IsDefined(typeof(TacticalFocusStage), Stage)
                || Stage == TacticalFocusStage.Focused && (!Target.IsValid || SeenTick > tick
                    || !TacticalFocusPolicy.Fresh(target, tick) || target.Urgency == 0)) BeginReturn(tick);
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Stage, "stage"); Scribe_Values.Look(ref Started, "started");
            Scribe_Values.Look(ref NextDecision, "nextDecision"); Scribe_Values.Look(ref SelectedAt, "selectedAt");
            Scribe_Values.Look(ref TargetId, "targetId", -1); Scribe_Values.Look(ref SeenTick, "seenTick");
            Scribe_Values.Look(ref Target, "target", IntVec3.Invalid); Scribe_Values.Look(ref ReleaseCursor, "releaseCursor");
            Scribe_Values.Look(ref NormalStarted, "normalStarted");
            Scribe_Values.Look(ref AdmissionCursor, "admissionCursor");
        }
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private const string FocusJob = "HD_NewTacticalFocusFire";
        public long FocusResponses, FocusChanges, FocusJobs, FocusShots, NormalCombatReturns;

        private static bool FocusSafetyPending(TacticalSquadCommand command) => command.ChargeAction != null
            || command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared
            || command.FieldResponse?.Screen != null;

        private static bool ProtectedFocusMember(TacticalSquadCommand command, TacticalMemberCommand member)
        {
            Pawn pawn = member.Pawn;
            if (pawn.CurJob?.playerForced == true || StationaryGuidance(member) || KnownFocusExplosion(pawn)
                || pawn.jobs.curDriver is JobDriver_Flee || pawn.jobs.curDriver is JobDriver_FleeAndCower
                || pawn.jobs.curDriver is JobDriver_FleeAndCowerShort) return true;
            // Keep ingress mouths and equipment/medical work out of the hold.
            string job = pawn.CurJobDef?.defName;
            return job == "HD_NewTacticalIngress" && !(pawn.jobs.curDriver is JobDriver_TacticalIngress ingress && ingress.AtPost)
                || job == "HD_NewTacticalRecoverTool"
                  || job == TacticalMedicalEquipmentPolicy.RecoveryJob
                || job == "HD_NewTacticalBreach" || job == "HD_NewTacticalCut"
                || job == TacticalFurniturePolicy.JobName
                || job == "HD_NewTacticalObserve" && !(pawn.jobs.curDriver is JobDriver_TacticalObserve observe && observe.AtPost && !observe.Peeking)
                || job == "HD_NewTacticalThrow" && !(pawn.jobs.curDriver is JobDriver_TacticalThrow throwing && throwing.Released && throwing.AtPost)
                || command.MedicalCare != null && (command.MedicalCare.Patient == member || command.MedicalCare.Helper == member)
                || command.Plan != null && !command.Plan.Direct && (pawn.Position == command.Plan.Opening
                    || pawn.Position == command.Plan.Outside || pawn.Position == command.Plan.Inside || pawn.Position == command.Plan.EntryLane);
        }
        private static bool KnownFocusExplosion(Pawn pawn) => pawn.mindState?.knownExploder?.Spawned == true
            && pawn.mindState.knownExploder.Map == pawn.Map && pawn.Position.DistanceToSquared(pawn.mindState.knownExploder.Position) <= 81;

        private bool AdvanceFocusFire(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            if (FocusSafetyPending(command)) return false;
            TacticalFocusFire focus = command.FocusFire;
            if (focus == null && !command.Contacts.Melee.Dangerous(tick)) return false;
            if (focus == null)
            {
                focus = new TacticalFocusFire { Started = tick };
                command.FocusFire = focus; FocusResponses++;
                command.Link.Cooperation.LocalReady = false;
            }
            command.Due = System.Math.Min(command.Due, tick + TacticalFocusPolicy.DecisionInterval);
            if (focus.Select(command.Contacts.Melee, active[active.Count / 2].Pawn.Position, tick))
            {
                FocusChanges++;
                foreach (TacticalMemberCommand member in active) member.FocusCheckAt = 0;
            }
            if (focus.Stage == TacticalFocusStage.Focused)
            { MaintainFocusFire(command, active, focus, tick); return true; }
            if (focus.Stage == TacticalFocusStage.Returning)
            { ReturnFocusControl(command, focus, tick); return true; }
            // A protected ingress/tool job may finish after the initial handoff.
            // Release its later tactical hold too, rather than leaving it pinned.
            focus.ReleaseCursor = 0;
            if (!ReturnFocusControl(command, focus, tick)) return true;
            // Native duties/think trees own cover, targets, movement and firing.
            // No tactical maintenance loop may overwrite them next decision.
            if (tick - focus.NormalStarted < TacticalFocusPolicy.NormalMinimumTicks || NormalRangedContact(command, active, tick)) return true;
            FinishFocusPause(command, tick); return false;
        }

        private void MaintainFocusFire(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFocusFire focus, int tick)
        {
            // Updating the shared value is cheap and never restarts these jobs.
            foreach (TacticalMemberCommand member in active)
                if (member.Pawn.CurJob == member.Job && member.Job?.def.defName == FocusJob)
                {
                    if (KnownFocusExplosion(member.Pawn))
                    {
                        TacticalWorkBudget safetyBudget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                        if (!safetyBudget.TryJob(tick, returning: true)) { command.DeferredWork = true; return; }
                        long started = Stopwatch.GetTimestamp();
                        try { EndOwned(member); }
                        finally { safetyBudget.Account(tick, Stopwatch.GetTimestamp() - started); }
                        continue;
                    }
                    member.Job.targetB = focus.Target; member.Job.count = focus.TargetId;
                }
            for (int checkedMembers = 0; checkedMembers < active.Count; checkedMembers++)
            {
                if (focus.AdmissionCursor >= active.Count) focus.AdmissionCursor = 0;
                TacticalMemberCommand member = active[focus.AdmissionCursor];
                Pawn pawn = member.Pawn;
                void Skip() { focus.AdmissionCursor++; }
                if (ProtectedFocusMember(command, member) || tick < member.FocusCheckAt
                    || pawn.CurJob == member.Job && member.Job?.def.defName == FocusJob)
                { Skip(); continue; }
                Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
                bool adjacent = pawn.Position.DistanceToSquared(focus.Target) <= 2;
                if (!adjacent && (verb == null || verb.IsMeleeAttack || !verb.Available()
                    || pawn.Position.DistanceToSquared(focus.Target) > verb.EffectiveRange * verb.EffectiveRange)
                    || pawn.stances.FullBodyBusy || tick < member.RetryTick)
                { Skip(); continue; }
                TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (!budget.TryObserve(tick)) { command.DeferredWork = true; return; }
                long started = Stopwatch.GetTimestamp();
                bool canFire;
                try
                {
                    Pawn enemy = LocalFocusCandidate(map, focus.Target, focus.TargetId);
                    canFire = enemy != null && enemy.thingIDNumber == focus.TargetId && !enemy.Dead && !enemy.Downed
                        && enemy.HostileTo(pawn) && TacticalContactSight.CanSee(map, pawn.Position, enemy, command.Contacts, TacticalFieldPolicy.SightRadius)
                        && (adjacent || verb.CanHitTarget(enemy));
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
                if (!canFire)
                { member.FocusCheckAt = tick + 60; Skip(); continue; }
                if (!CanIssue(member)) return;
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(FocusJob), pawn.Position, focus.Target);
                job.count = focus.TargetId;
                if (Issue(member, job)) FocusJobs++;
                Skip();
            }
        }

        private bool ReturnFocusControl(TacticalSquadCommand command, TacticalFocusFire focus, int tick)
        {
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            while (focus.ReleaseCursor < command.Members.Count)
            {
                TacticalMemberCommand member = command.Members[focus.ReleaseCursor];
                if (Available(member, map) && (!ProtectedFocusMember(command, member) || member.Pawn.CurJobDef?.defName == FocusJob)
                    && member.Job != null && member.Pawn.CurJob == member.Job)
                {
                    if (!budget.TryJob(tick, returning: true)) { command.DeferredWork = true; return false; }
                    long started = Stopwatch.GetTimestamp();
                    try { EndOwned(member); }
                    finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
                }
                focus.ReleaseCursor++;
            }
            if (focus.Stage != TacticalFocusStage.Normal) NormalCombatReturns++;
            focus.Stage = TacticalFocusStage.Normal; return true;
        }

        private static bool NormalRangedContact(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            IntVec3 anchor = active[active.Count / 2].Pawn.Position;
            foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                if (TacticalContactMemory.Fresh(contact, tick) && anchor.DistanceToSquared(contact.Position) <= 6400) return true;
            return false;
        }

        private static void FinishFocusPause(TacticalSquadCommand command, int tick)
        {
            TacticalFocusFire focus = command.FocusFire;
            if (focus == null) return;
            int duration = System.Math.Max(0, tick - focus.Started);
            command.PhaseStarted += duration;
            if (command.FieldResponse != null)
            { command.FieldResponse.Started += duration; command.FieldResponse.NextMove += duration; }
            if (command.ContactResponse != null) command.ContactResponse.Started += duration;
            command.FocusFire = null; command.Due = tick + 1;
        }

        internal void FocusFireSeen(Pawn observer, Pawn enemy, int tick)
        {
            if (!RootContext(observer, out TacticalSquadCommand command)) return;
            ObserveMeleeThreat(command, enemy, observer, tick, TacticalThreatEligibility.Ranged(enemy));
            TacticalMeleeThreat threat = command.Contacts.Melee.Find(enemy.thingIDNumber);
            if (threat?.Urgency == 0 && command.FocusFire != null)
            { command.FocusFire.NextDecision = tick; Wake(observer); }
        }
        internal void FocusShot() => FocusShots++;
        internal static Pawn LocalFocusCandidate(Map map, IntVec3 lastSeen, int enemyId)
        {
            // A moving target may have left its last tile. Probe only the small
            // observed neighbourhood; the caller MUST pass LOS before reading
            // intent/shield state or updating/shooting this candidate.
            for (int i = 0; i < TacticalFocusPolicy.FollowOffsets.Count; i++)
            {
                IntVec3 cell = lastSeen + TacticalFocusPolicy.FollowOffsets[i];
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map))
                    if (thing is Pawn pawn && pawn.thingIDNumber == enemyId) return pawn;
            }
            return null;
        }
        internal bool OwnsFocusTarget(Pawn pawn, Job job) => RootContext(pawn, out TacticalSquadCommand command)
            && command.FocusFire?.Stage == TacticalFocusStage.Focused && command.FocusFire.TargetId == job.count
            && command.Members.Find(m => m.Pawn == pawn)?.Job == job;
    }

    public sealed class JobDriver_TacticalFocusFire : TacticalJobDriver
    {
        private int nextShot;
        private readonly TacticalContactState sight = new TacticalContactState();
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner(); job.canUseRangedWeapon = false;
            // No movement or destination search: keep the shooter's current area.
            Toil fire = Hold(job.targetB.Cell); fire.tickAction = TickFire;
            yield return fire;
        }
        private void TickFire()
        {
            int tick = GenTicks.TicksGame;
            if (tick < nextShot || pawn.stances.FullBodyBusy) return;
            nextShot = tick + 3 + pawn.thingIDNumber % 3;
            MapComponent_TacticalCommands owner = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
            if (owner?.OwnsFocusTarget(pawn, job) != true) { nextShot = tick + 30; return; }
            IntVec3 cell = job.targetB.Cell;
            pawn.rotationTracker.FaceCell(cell);
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryObserve(tick)) return;
            nextShot = tick + 30 + pawn.thingIDNumber % 7;
            long started = Stopwatch.GetTimestamp();
            try
            {
                Pawn enemy = MapComponent_TacticalCommands.LocalFocusCandidate(pawn.Map, cell, job.count);
                if (enemy == null || !enemy.HostileTo(pawn)
                    || !TacticalContactSight.CanSee(pawn.Map, pawn.Position, enemy, sight, TacticalFieldPolicy.SightRadius)) return;
                // Only a real new sighting advances this driver's local anchor.
                job.targetB = enemy.Position;
                owner?.FocusFireSeen(pawn, enemy, tick);
                if (enemy.Dead || enemy.Downed) return;
                if (pawn.Position.DistanceToSquared(enemy.Position) <= 2 && pawn.meleeVerbs.TryMeleeAttack(enemy)) return;
                Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
                if (verb != null && !verb.IsMeleeAttack && verb.Available() && verb.CanHitTarget(enemy)
                    && verb.TryStartCastOn(enemy)) owner?.FocusShot();
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }
        public override void ExposeData()
        { base.ExposeData(); Scribe_Values.Look(ref nextShot, "focusNextShot"); }
    }
}
