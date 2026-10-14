using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalMemberCommand { public int FieldDecisionAt; }
    public sealed partial class TacticalFieldResponse
    {
        public bool LowFlank, LowFlankAttack;
        public int NativeCursor;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private readonly Dictionary<Pawn, Job> pendingFieldJobs = new Dictionary<Pawn, Job>();
        private static bool LowAutomatic(TacticalMemberCommand member) => member.CombatFunction == "HAR"
            && member.Pawn.equipment?.PrimaryEq?.PrimaryVerb?.IsMeleeAttack == false;
        private static bool LowTreatmentWait(TacticalSquadCommand command, Pawn pawn) => command.MedicalCandidate?.Pawn == pawn
            && pawn.CurJobDef?.defName == "HD_NewTacticalPost" && command.NextMedicalCheck <= GenTicks.TicksGame + 30;
        internal bool FieldCombatContext(Pawn pawn, out TacticalSquadCommand command)
        {
            return byPawn.TryGetValue(pawn, out command) && command.FieldResponse != null && command.CqbParent == null
                && command.ChargeAction == null && command.ContactResponse?.DirectAttack != true
                && !(command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared)
                && !TacticalEntryCombatPolicy.CommittedIngress(command)
                && !TacticalOpeningPolicy.OwnsContact(command, command.OpeningAction?.Enemy ?? IntVec3.Invalid)
                && !command.Terminal && !pawn.Dead && !pawn.Downed && !pawn.InMentalState && !pawn.Drafted
                && !pawn.IsBurning() && !(pawn.jobs.curDriver is JobDriver_Flee)
                && !(pawn.jobs.curDriver is JobDriver_FleeAndCower) && !(pawn.jobs.curDriver is JobDriver_FleeAndCowerShort)
                && command.Phase != TacticalCommandPhase.Returning
                && !TacticalFieldDoctrine.EquipmentWork(pawn.CurJob)
                && pawn.CurJobDef?.defName != FieldModeJobName
                && pawn.CurJobDef?.defName != InventoryGrenadeUtility.ThrowJobName
                && !LowTreatmentWait(command, pawn)
                && command.MedicalCare == null && !PawnUtility.PlayerForcedJobNowOrSoon(pawn) && command.BagRecovery?.Helper != pawn
                && command.FieldResponse.Screen?.Thrower != pawn
                && pawn.CurJobDef?.defName != TacticalExplosiveEvasionPolicy.JobName
                && pawn.CurJobDef?.defName != "HD_NewTacticalFocusFire";
        }
        internal bool FieldCombatFloor(TacticalSquadCommand command, IntVec3 cell) =>
            (command.FieldResponse.High || command.FieldResponse.Maneuver != null && TacticalFieldDoctrine.Within(command.FieldResponse.Anchor, cell, 60)
                || TacticalFieldDoctrine.WithinEllipse(command.FieldResponse.Anchor, cell, command.FieldResponse.Forward,
                TacticalFieldDoctrine.LowRadius, TacticalFieldDoctrine.LowDepth)) && FieldFloor(command, cell);
        internal bool FieldLocusContains(TacticalSquadCommand command, Pawn pawn, IntVec3 cell)
        {
            FieldLocus(command, pawn, out IntVec3 locus, out int radius);
            var field = command.FieldResponse;
            if (TacticalFieldDoctrine.WithinEllipse(locus, cell, field.Forward, radius, FieldDepth(command, pawn))) return true;
            if (!Hammer(command, pawn) || field.Maneuver == null) return false;
            var m = field.Maneuver;
            IntVec3 previous = m.Leg > 0 && m.Leg <= m.Waypoints.Count ? m.Waypoints[m.Leg - 1] : m.Origin;
            return TacticalFieldDoctrine.WithinEllipse(previous, cell, field.Forward, radius, FieldDepth(command, pawn));
        }
        internal void FieldLocus(TacticalSquadCommand command, Pawn pawn, out IntVec3 locus, out int radius)
        {
            var field = command.FieldResponse;
            if (Hammer(command, pawn)) { locus = field.FirePoints[1]; radius = field.FlankWidth; return; }
            var member = command.Members.Find(m => m.Pawn == pawn);
            if (member.FieldPost.IsValid)
            { locus = member.FieldPost; radius = field.High ? TacticalFieldPolicy.HighWidth : TacticalFieldDoctrine.LowMainRadius; return; }
            if (field.High)
            {
                locus = field.FirePoints[field.Groups[command.Members.FindIndex(m => m.Pawn == pawn)]];
                radius = TacticalFieldPolicy.HighWidth; return;
            }
            bool flank = field.LowFlank && field.FireGroup.Contains(command.Members.FindIndex(m => m.Pawn == pawn));
            locus = flank ? field.FirePoints[1] : field.Anchor;
            radius = flank ? TacticalFieldDoctrine.LowFlankRadius : TacticalFieldDoctrine.LowMainRadius;
        }
        internal Job FieldNativeJob(Pawn pawn, int tick)
        {
            if (!FieldCombatContext(pawn, out var command)) return null;
            var member = command.Members.Find(m => m.Pawn == pawn);
            var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryJob(tick)) return null;
            if (!Hammer(command, pawn) && member.FieldPostState != TacticalFieldPostState.Moving
                && member.FieldPostState != TacticalFieldPostState.Fixed && FieldSeparatedCover(command, pawn, pawn.Position))
                CaptureFieldPost(member, command.FieldResponse, tick);
            Job mode = FieldModeJob(command, member);
            if (mode != null) return mode;
            bool anvil = !command.FieldResponse.FireGroup.Contains(command.Members.IndexOf(member));
            if (!anvil)
            {
                Job moving = null;
                bool seekingCover = !FieldHammerMoving(command,member) && !FieldAtWaypoint(command,member)
                    && !FieldCoverAt(command,pawn.Position,command.FieldResponse.Focus);
                if ((!FieldHammerMoving(command, member) || FieldAtWaypoint(command, member)) && !seekingCover)
                { moving = JobMaker.MakeJob(JobDefOf.Wait_Combat); moving.expiryInterval = 600; }
                else if (tick >= member.FieldDecisionAt && budget.TryPlan(tick))
                {
                    long started = Stopwatch.GetTimestamp();
                    try { moving = FieldManeuverJob(command, member, tick); }
                    finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
                    member.FieldDecisionAt = tick + (moving?.def == JobDefOf.Goto ? 90 : seekingCover ? 180 : 30);
                }
                if (moving == null) { moving = JobMaker.MakeJob(JobDefOf.Wait_Combat); moving.expiryInterval = 600; }
                moving.checkOverrideOnExpire = false; pendingFieldJobs[pawn] = moving; return moving;
            }
            if (anvil)
            {
                RefreshFieldPost(member, command.FieldResponse, tick);
                Job fixedJob = FixedFieldPostJob(member, command, tick);
                if (fixedJob != null) { pendingFieldJobs[pawn] = fixedJob; return fixedJob; }
            }
            Job job = null; bool nativeDecision = false, planned = false, pathDeferred = false;
            if (tick >= member.FieldDecisionAt && budget.TryPlan(tick))
            {
                long started = Stopwatch.GetTimestamp();
                try
                {
                    planned = true;
                    member.FieldDecisionAt = tick + 180 + pawn.thingIDNumber % 30;
                    if (FieldLocusContains(command, pawn, pawn.Position)) job = TacticalFieldFight.Instance.BuildCovered(pawn);
                    if (job == null && TryFieldShelter(command, pawn, out IntVec3 shelter)) job = JobMaker.MakeJob(JobDefOf.Goto, shelter);
                    if (job != null && job.def == JobDefOf.Goto && (!FieldCombatFloor(command, job.targetA.Cell)
                        || !FieldLocusContains(command, pawn, job.targetA.Cell)))
                    { JobMaker.ReturnToPool(job); job = null; }
                    nativeDecision = job != null;
                    if (!nativeDecision) RejectFieldPost(member, tick);
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            }
            if (job == null)
            {
                if (!planned && member.Job != null && pawn.CurJob == member.Job) return null;
                FieldLocus(command, pawn, out IntVec3 locus, out int radius);
                if (planned && !FieldLocusContains(command, pawn, pawn.Position))
                {
                    // Bounded straight seed probes, one native path only after
                    // acceptance. Never roam around the map to regain cohesion.
                    IntVec3 best = IntVec3.Invalid; int score = int.MaxValue;
                    foreach (IntVec3 cell in GenRadial.RadialCellsAround(locus, 3, true))
                        if (FieldCombatFloor(command, cell) && FieldLocusContains(command, pawn, cell)
                            && cell.GetFirstPawn(map) == null && cell.DistanceToSquared(pawn.Position) < score)
                        { best = cell; score = cell.DistanceToSquared(pawn.Position); }
                    if (best.IsValid && map.reachability.CanReach(pawn.Position, best, PathEndMode.OnCell, TraverseParms.For(pawn, Danger.Some)))
                        job = JobMaker.MakeJob(JobDefOf.Goto, best);
                }
                if (job == null) job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            }
            job.expiryInterval = job.def == JobDefOf.Wait_Combat ? 600 + pawn.thingIDNumber % 90 : 1800;
            job.checkOverrideOnExpire = false;
            if (job.def == JobDefOf.Goto && !budget.TryPath(tick))
            {
                JobMaker.ReturnToPool(job); job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
                job.expiryInterval = 600 + pawn.thingIDNumber % 90; job.checkOverrideOnExpire = false;
                nativeDecision = false;
                pathDeferred = true;
            }
            if (anvil && planned && !pathDeferred && FieldLocusContains(command, pawn, pawn.Position))
            {
                if (job.def == JobDefOf.Goto && nativeDecision)
                { member.FieldPost = job.targetA.Cell; member.FieldPostState = TacticalFieldPostState.Moving; }
                else if (job.def == JobDefOf.Wait_Combat && nativeDecision) CaptureFieldPost(member, command.FieldResponse, tick);
            }
            pendingFieldJobs[pawn] = job;
            return job;
        }
        internal void PrepareFieldNativeStart(Pawn pawn, Job job)
        {
            if (!pendingFieldJobs.TryGetValue(pawn,out Job pending) || pending != job || !byPawn.TryGetValue(pawn,out var command)) return;
            var member = command.Members.Find(m => m.Pawn == pawn);
            if (member != null) member.Job = job; // before old-job cleanup can notify its owner
        }
        internal void AdoptFieldNative(Pawn pawn, Job job)
        {
            if (!pendingFieldJobs.TryGetValue(pawn, out Job pending) || pending != job) return;
            pendingFieldJobs.Remove(pawn);
            if (pawn.CurJob != job)
            {
                if (byPawn.TryGetValue(pawn,out var failed))
                {
                    var actor = failed.Members.Find(m => m.Pawn == pawn);
                    if (actor?.Job == job) { actor.Job = null; actor.FieldPostState = TacticalFieldPostState.Uncovered;
                        actor.FieldPost = IntVec3.Invalid; actor.FieldCover = null; actor.FieldHadCover = false; }
                }
                return;
            }
            if (!byPawn.TryGetValue(pawn, out var command) || command.FieldResponse == null) return;
            var member = command.Members.Find(m => m.Pawn == pawn); if (member == null) return;
            FieldNativeJobs++;
            member.Job = job; member.LastProgressTick = GenTicks.TicksGame;
            pawn.jobs.curDriver.AddFinishAction(condition =>
            {
                if (member.Job != job) return;
                if (command.FieldResponse == null) { member.Job = null; Wake(pawn); return; }
                if (member.FieldPostState == TacticalFieldPostState.Moving)
                {
                    if (condition == JobCondition.Succeeded && pawn.Position == member.FieldPost)
                        CaptureFieldPost(member, command.FieldResponse, GenTicks.TicksGame);
                    else RejectFieldPost(member, GenTicks.TicksGame);
                }
                else if (member.FieldPostState == TacticalFieldPostState.Fixed && job.def == JobDefOf.Goto
                    && condition != JobCondition.Succeeded) RejectFieldPost(member, GenTicks.TicksGame);
                member.Job = null; Wake(pawn);
            });
        }
        private bool AdvanceLowField(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick, bool support)
        {
            command.Due = Math.Min(command.Due, tick + 10);
            if (!field.High) AdvanceLowDefense(command, active, field, tick);
            AdvanceFieldManeuver(command, active, field, tick, support);
            if (field.High) { AdvanceHighDefense(command, active, field, tick); PrepareFieldCover(command, field, tick); }
            if (field.Maneuver != null && field.Maneuver.Phase != TacticalManeuverPhase.Planning)
            {
                if (!field.High) PrepareFieldCover(command, field, tick);
                if (field.Maneuver.Phase == TacticalManeuverPhase.Moving && tick >= field.SmokeRetryAt
                    && field.Screen == null) PlanFieldSmoke(command, active, field, tick);
            }
            AdvanceFieldCombatActors(command, active, field, tick);
            if (field.Maneuver == null) field.Stage = TacticalFieldStage.Defending;
            return true;
        }
    }
    // Native acquisition, ability selection, cover scoring, Wait_Combat firing
    // and Goto execution; only the shooting-position request is constrained.
    public sealed class TacticalFieldFight : JobGiver_AIFightEnemies
    {
        public static readonly TacticalFieldFight Instance = new TacticalFieldFight();
        public TacticalFieldFight() { ignoreNonCombatants = true; }
        protected override bool DisableAbilityVerbs => true;
        public Job BuildCovered(Pawn pawn, Predicate<IntVec3> extra = null)
        {
            UpdateEnemyTarget(pawn);
            Thing target = pawn.mindState.enemyTarget;
            Verb verb = target == null ? null : pawn.TryGetAttackVerb(target, true);
            var owner = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
            if (verb == null || verb.IsMeleeAttack || !owner.FieldCombatContext(pawn, out var command)) return null;
            owner.FieldLocus(command, pawn, out IntVec3 locus, out int radius);
            // 1.6 clips maxRangeFromLocus around targetLoc, not the supplied
            // locus. Keep the small caster bound and validate our real area
            // explicitly instead of accidentally discarding nearby covers.
            if (!CastPositionFinder.TryFindCastPosition(new CastPositionRequest { caster = pawn, target = target, verb = verb,
                maxRangeFromTarget = verb.EffectiveRange, maxRangeFromCaster = 8, locus = locus,
                wantCoverFromTarget = true, validator = c => owner.FieldSeparatedCover(command, pawn, c) && (extra==null || extra(c)) }, out IntVec3 cell)) return null;
            return cell == pawn.Position ? JobMaker.MakeJob(JobDefOf.Wait_Combat) : JobMaker.MakeJob(JobDefOf.Goto, cell);
        }
        protected override float GetFlagRadius(Pawn pawn) => FieldAnchor(pawn, out int radius).IsValid
            ? radius + (pawn.equipment?.PrimaryEq?.PrimaryVerb?.EffectiveRange ?? 30) : 54;
        protected override IntVec3 GetFlagPosition(Pawn pawn) => FieldAnchor(pawn, out _);
        private static IntVec3 FieldAnchor(Pawn pawn, out int radius)
        {
            var owner = pawn.Map.GetComponent<MapComponent_TacticalCommands>(); radius = TacticalFieldDoctrine.LowMainRadius;
            if (!owner.FieldCombatContext(pawn, out var command)) return pawn.Position;
            owner.FieldLocus(command, pawn, out IntVec3 locus, out radius); return locus;
        }
        protected override bool ExtraTargetValidator(Pawn pawn, Thing target) => (target is Building_Turret
            || target is Pawn enemy && !enemy.Dead && !enemy.Downed && (TacticalThreatEligibility.Ranged(enemy) || enemy.RaceProps.Humanlike))
            && base.ExtraTargetValidator(pawn, target);
        protected override bool TryFindShootingPosition(Pawn pawn, out IntVec3 dest, Verb verbToUse = null)
        {
            var owner = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
            if (!owner.FieldCombatContext(pawn, out var command)) { dest = IntVec3.Invalid; return false; }
            var target = pawn.mindState.enemyTarget;
            Verb verb = verbToUse ?? pawn.TryGetAttackVerb(target, true);
            if (verb == null) { dest = IntVec3.Invalid; return false; }
            owner.FieldLocus(command, pawn, out IntVec3 locus, out int radius);
            return CastPositionFinder.TryFindCastPosition(new CastPositionRequest { caster = pawn, target = target, verb = verb,
                maxRangeFromTarget = verb.EffectiveRange, maxRangeFromCaster = 8, locus = locus, maxRangeFromLocus = radius,
                wantCoverFromTarget = verb.EffectiveRange > 5,
                validator = c => owner.FieldCombatFloor(command, c) && owner.FieldLocusContains(command, pawn, c) }, out dest);
        }
    }
    [NewTactical, HarmonyPatch(typeof(Pawn_JobTracker), "DetermineNextJob")]
    public static class Patch_NewTactical_FieldJobs
    {
        private static readonly Func<Pawn_JobTracker, ThinkResult> ConstantJobs =
            (Func<Pawn_JobTracker, ThinkResult>)Delegate.CreateDelegate(typeof(Func<Pawn_JobTracker, ThinkResult>),
                AccessTools.Method(typeof(Pawn_JobTracker), "DetermineNextConstantThinkTreeJob"));
        public static bool Prefix(Pawn_JobTracker __instance, Pawn ___pawn, ref bool ___determiningNextJob,
            ref ThinkTreeDef thinkTree, ref ThinkResult __result)
        {
            var owner = ___pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            if (___determiningNextJob || owner?.FieldCombatContext(___pawn, out _) != true) return true;
            ___determiningNextJob = true;
            try
            {
                // Preserve the exact native constant-tree safety/mental checks.
                ThinkResult safety = ConstantJobs(__instance);
                if (safety.IsValid) { __result = safety; thinkTree = ___pawn.thinker.ConstantThinkTree; return false; }
                Job job = owner.FieldNativeJob(___pawn, GenTicks.TicksGame);
                // Use the pawn's registered tree root as provenance; the
                // reusable request adapter is not a serialized think-tree node.
                __result = job == null ? ThinkResult.NoJob : new ThinkResult(job, ___pawn.thinker.MainThinkNodeRoot);
                thinkTree = ___pawn.thinker.MainThinkTree; return false;
            }
            finally { ___determiningNextJob = false; }
        }
    }
    [NewTactical, HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_NewTactical_AdoptFieldJob
    {
        public static void Prefix(Pawn ___pawn, Job newJob)
        {
            MapComponent_TacticalCommands.FieldModeSafety(___pawn, newJob);
            ___pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.PrepareFieldNativeStart(___pawn,newJob);
        }
        public static void Postfix(Pawn ___pawn, Job newJob) => ___pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.AdoptFieldNative(___pawn, newJob);
    }
}
