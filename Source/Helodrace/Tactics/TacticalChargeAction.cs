using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed class TacticalChargeAction
    {
        public Pawn Installer;
        public Job Installation;
        public CompInstalledBreachCharge Charge;
        public readonly List<IntVec3> OriginalStack = new List<IntVec3>(), Withdrawal = new List<IntVec3>();
        public int Deadline, SettledAt = -1;
        public bool Withdrawing, Detonated, EffectsCleared;
        public float Radius;
    }
    public static class TacticalChargePolicy
    {
        public static float SafeRadius(CompProperties_InstalledBreachCharge props, int count) =>
            Math.Max(props.explosionRadiusBase + (float)Math.Sqrt(Math.Max(1, count)) * props.explosionRadiusPerC4 + 1,
                props.beyondFragmentRadius + 1);
        public static bool EffectsPending(bool charge, bool fragments, bool explosion, int settledAt, int tick) =>
            charge || fragments || explosion || settledAt < 0 || tick - settledAt < TacticalOpeningPolicy.SettleTicks;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        public long ChargesInstalled, ChargeDetonations, ChargeOperatorTransfers, ChargeWaits;

        internal void ChargeInstalled(Pawn pawn, Job job, CompInstalledBreachCharge charge)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.ChargeAction?.Installer != pawn
                || command.ChargeAction.Installation != job || command.Phase != TacticalCommandPhase.Breach)
            { if (job.def.defName == TacticalBreachTools.InstallJob) charge.Abandon(false); return; }
            command.ChargeAction.Charge = charge; ChargesInstalled++; Wake(pawn);
        }
        internal bool TriggerCharge(Pawn pawn, Job job)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.ChargeAction?.Charge == null
                || command.ChargeAction.Charge.parent != job.targetA.Thing || command.Phase != TacticalCommandPhase.Breach
                || command.ChargeAction.Detonated || !ChargeSafeToTrigger(command)) return false;
            TacticalChargeAction action = command.ChargeAction;
            action.Detonated = true; action.SettledAt = -1; ChargeDetonations++;
            action.Charge.Trigger(); Wake(pawn); return true;
        }
        private bool BeginCharge(TacticalSquadCommand command, TacticalMemberCommand installer, int tick)
        {
            CompBreachIgniter igniter = TacticalBreachTools.IgniterFor(installer.Pawn);
            if (igniter == null) return false;
            int count = BreachExplosiveUtility.RequiredC4For(command.Plan.Barrier);
            var action = new TacticalChargeAction { Installer = installer.Pawn,
                Radius = TacticalChargePolicy.SafeRadius(BreachExplosiveUtility.ChargeProps, count),
                Deadline = tick + 1200 + BreachExplosiveUtility.WorkTicksFor(installer.Pawn, igniter,
                    igniter.Supports(BreachInitiationMode.ShockTube) ? BreachInitiationMode.ShockTube : BreachInitiationMode.TimeFuse, count) };
            action.OriginalStack.AddRange(command.Plan.Stack);
            if (!PrepareChargeWithdrawal(command, action, igniter.RangeFor(igniter.Supports(BreachInitiationMode.ShockTube)
                ? BreachInitiationMode.ShockTube : BreachInitiationMode.TimeFuse))) return false;
            command.ChargeAction = action; return true;
        }

        private bool PrepareChargeWithdrawal(TacticalSquadCommand command, TacticalChargeAction action, float range)
        {
            TacticalLocalPlan plan = command.Plan; IntVec3 lateral = new IntVec3(-plan.Inward.z, 0, plan.Inward.x);
            int minimumDepth = Math.Max(4, (int)Math.Ceiling(action.Radius) + 1);
            var pending = new Queue<IntVec3>(); var seen = new HashSet<IntVec3>();
            IntVec3 root = plan.Opening - plan.Inward * minimumDepth;
            bool Free(IntVec3 cell) => TacticalLocalPlanner.Free(map, cell,
                p => claims.TryGetValue(p, out TacticalSquadCommand owner) && owner != command);
            if (!Free(root) || root.DistanceTo(plan.Opening) > range) return false;
            pending.Enqueue(root); seen.Add(root);
            while (pending.Count > 0 && action.Withdrawal.Count < command.Members.Count && seen.Count <= 128)
            {
                IntVec3 cell = pending.Dequeue(); action.Withdrawal.Add(cell);
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction, delta = next - plan.Opening;
                    int depth = -(delta.x * plan.Inward.x + delta.z * plan.Inward.z), width = delta.x * lateral.x + delta.z * lateral.z;
                    if (depth < minimumDepth || depth > minimumDepth + 4 || Math.Abs(width) > 8 || !Free(next) || !seen.Add(next)) continue;
                    pending.Enqueue(next);
                }
            }
            if (action.Withdrawal.Count != command.Members.Count || !TacticalLocalPlanner.Connected(action.Withdrawal)) return false;
            // The installer must retain the cable/fuse range. Other withdrawn
            // members can occupy farther slots in the same connected formation.
            int installer = command.Members.FindIndex(m => m.Pawn == action.Installer);
            IntVec3 temp = action.Withdrawal[installer]; action.Withdrawal[installer] = root; action.Withdrawal[0] = temp;
            foreach (IntVec3 cell in action.Withdrawal) claims[cell] = command;
            return true;
        }

        private void AdvanceCharge(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalChargeAction action = command.ChargeAction; TacticalLocalPlan plan = command.Plan;
            command.Due = tick + 15;
            if (action.Detonated)
            {
                foreach (TacticalMemberCommand member in active)
                    EnsurePost(member, action.Withdrawal[command.Members.IndexOf(member)], plan.Opening, tick);
                if (ChargeEffectsPending(command, tick)) { ChargeWaits++; return; }
                RestoreChargeStack(command);
                RememberOpening(plan);
                if (command.ReplanAfterSupport) { ResetAfterSupport(command, tick); return; }
                command.ChargeAction = null;
                command.Phase = TacticalCommandPhase.Observe; command.PhaseStarted = tick; return;
            }
            if (OpeningUsable(plan))
            {
                AbandonCharge(command); RememberOpening(plan); command.Phase = TacticalCommandPhase.Observe;
                command.PhaseStarted = tick; return;
            }
            if (tick > action.Deadline) { AbandonCharge(command); Release(command); return; }
            if (action.Charge == null)
            {
                TacticalMemberCommand installer = active.Find(m => m.Pawn == action.Installer);
                if (installer == null || !TacticalBreachTools.CanCharge(installer.Pawn, plan.Barrier))
                {
                    // A completed install consumes its items; the callback sets
                    // Charge before this branch can mistake that for equipment loss.
                    installer = active.Find(m => TacticalBreachTools.CanCharge(m.Pawn, plan.Barrier));
                    if (installer == null)
                    {
                        if (!RecoverBreachTool(command, active, tick)) { AbandonCharge(command); Release(command); return; }
                        foreach (TacticalMemberCommand member in active)
                            if (member.Job?.def.defName != TacticalBreachTools.RecoveryJob || member.Pawn.CurJob != member.Job)
                                EnsurePost(member, action.OriginalStack[command.Members.IndexOf(member)], plan.Opening, tick);
                        return;
                    }
                    action.Installer = installer.Pawn; command.Breacher = installer.Pawn;
                    CompBreachIgniter replacement = TacticalBreachTools.IgniterFor(installer.Pawn);
                    action.Deadline = tick + 1200 + BreachExplosiveUtility.WorkTicksFor(installer.Pawn, replacement,
                        replacement.Supports(BreachInitiationMode.ShockTube) ? BreachInitiationMode.ShockTube : BreachInitiationMode.TimeFuse,
                        BreachExplosiveUtility.RequiredC4For(plan.Barrier));
                }
                foreach (TacticalMemberCommand member in active)
                    if (member != installer) EnsurePost(member, action.OriginalStack[command.Members.IndexOf(member)], plan.Opening, tick);
                if (installer.Pawn.CurJob == action.Installation && action.Installation != null) return;
                if (tick < installer.RetryTick || !CanIssue(installer)) return;
                CompBreachIgniter igniter = TacticalBreachTools.IgniterFor(installer.Pawn);
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TacticalBreachTools.InstallJob), plan.Barrier, plan.Outside, igniter.parent);
                job.count = BreachExplosiveUtility.RequiredC4For(plan.Barrier); action.Installation = job; Issue(installer, job); return;
            }
            if (!action.Charge.parent.Spawned) { AbandonCharge(command); Release(command); return; }
            if (!action.Withdrawing)
            {
                int controllerIndex = command.Members.FindIndex(m => m.Pawn == action.Charge.OperatorPawn);
                int withinRange = action.Withdrawal.FindIndex(cell => cell.DistanceTo(plan.Opening) <= action.Charge.TetherRange);
                if (withinRange < 0) { AbandonCharge(command); Release(command); return; }
                IntVec3 temp = action.Withdrawal[controllerIndex]; action.Withdrawal[controllerIndex] = action.Withdrawal[withinRange];
                action.Withdrawal[withinRange] = temp;
                plan.Stack.Clear(); plan.Stack.AddRange(action.Withdrawal); action.Withdrawing = true;
            }
            Pawn controller = action.Charge.OperatorPawn;
            if (!active.Exists(m => m.Pawn == controller))
            {
                TacticalMemberCommand successor = active.Find(m => BreachExplosiveUtility.CanOperate(m.Pawn)
                    && action.Withdrawal[command.Members.IndexOf(m)].DistanceTo(plan.Opening) <= action.Charge.TetherRange);
                if (successor == null || !action.Charge.TryAssignOperator(successor.Pawn)) { AbandonCharge(command); Release(command); return; }
                controller = successor.Pawn; ChargeOperatorTransfers++;
            }
            foreach (TacticalMemberCommand member in active)
                if (member.Pawn != controller || member.Job?.def.defName != TacticalBreachTools.TriggerJob || member.Pawn.CurJob != member.Job)
                    EnsurePost(member, action.Withdrawal[command.Members.IndexOf(member)], plan.Opening, tick);
            TacticalMemberCommand worker = active.Find(m => m.Pawn == controller);
            if (worker.Pawn.CurJob == worker.Job && worker.Job?.def.defName == TacticalBreachTools.TriggerJob) return;
            if (active.Any(m => !AtPost(m)) || !ChargeSafeToTrigger(command) || tick < worker.RetryTick || !CanIssue(worker)) return;
            Issue(worker, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TacticalBreachTools.TriggerJob), action.Charge.parent));
        }

        private bool ChargeSafeToTrigger(TacticalSquadCommand command)
        {
            TacticalChargeAction action = command.ChargeAction;
            if (action?.Charge == null || !action.Charge.CanTrigger(out _)) return false;
            IntVec3 position = action.Charge.parent.Position;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                if (pawn.Faction != null && action.Charge.OperatorPawn.Faction != null && !pawn.HostileTo(action.Charge.OperatorPawn)
                    && pawn.Position.DistanceToSquared(position) <= action.Radius * action.Radius) return false;
            return true;
        }
        private bool ChargeEffectsPending(TacticalSquadCommand command, int tick)
        {
            TacticalChargeAction action = command.ChargeAction;
            if (action?.Detonated != true || action.EffectsCleared) return false;
            bool charge = action.Charge.parent.Spawned, fragments = action.Charge.Fragments.Any(p => p.Spawned), explosion = false;
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Explosion))
                if (thing is Explosion effect && effect.Spawned && effect.instigator == action.Charge.OperatorPawn
                    && effect.Position == command.Plan.Opening) { explosion = true; break; }
            if (charge || fragments || explosion) action.SettledAt = -1;
            else if (action.SettledAt < 0) action.SettledAt = tick;
            bool pending = TacticalChargePolicy.EffectsPending(charge, fragments, explosion, action.SettledAt, tick);
            if (!pending) action.EffectsCleared = true;
            return pending;
        }
        private void RestoreChargeStack(TacticalSquadCommand command)
        {
            TacticalChargeAction action = command.ChargeAction;
            if (action == null) return;
            foreach (IntVec3 cell in action.Withdrawal)
                if (claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner == command) claims.Remove(cell);
            if (action.Withdrawing)
            { command.Plan.Stack.Clear(); command.Plan.Stack.AddRange(action.OriginalStack); action.Withdrawing = false; }
        }
        private void AbandonCharge(TacticalSquadCommand command)
        {
            TacticalChargeAction action = command.ChargeAction;
            if (action?.Detonated == true && !action.EffectsCleared) return;
            action?.Charge?.Abandon(false); RestoreChargeStack(command); command.ChargeAction = null;
        }
        private void ResetAfterSupport(TacticalSquadCommand command, int tick)
        {
            command.ReplanAfterSupport = false; ReleaseClaims(command); command.Plan = null;
            command.OpeningAction = null; command.ChargeAction = null; command.Phase = TacticalCommandPhase.Pending;
            command.Due = tick + 1; command.PhaseStarted = tick; command.PlanRetryAt = 0;
            command.ReturnCursor = 0; command.ReleaseAfterReturn = false;
            foreach (TacticalMemberCommand member in command.Members) member.Passed = member.Crossed = member.Entered = false;
        }
    }
}
