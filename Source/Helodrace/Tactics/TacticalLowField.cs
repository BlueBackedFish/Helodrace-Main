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
    public sealed partial class TacticalMemberCommand { public int LowDecisionAt; }
    public sealed partial class TacticalFieldResponse
    {
        public bool LowFlank, LowFlankAttack;
        public int NextFlank, FlankStarted, FlankSide = 1, NativeCursor;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private readonly Dictionary<Pawn, Job> pendingLowJobs = new Dictionary<Pawn, Job>();
        private static bool LowAutomatic(TacticalMemberCommand member) => member.CombatFunction == "HAR"
            && member.Pawn.equipment?.PrimaryEq?.PrimaryVerb?.IsMeleeAttack == false;
        private static bool LowTreatmentWait(TacticalSquadCommand command, Pawn pawn) => command.MedicalCandidate?.Pawn == pawn
            && pawn.CurJobDef?.defName == "HD_NewTacticalPost" && command.NextMedicalCheck <= GenTicks.TicksGame + 30;
        internal bool LowFieldContext(Pawn pawn, out TacticalSquadCommand command)
        {
            return byPawn.TryGetValue(pawn, out command) && command.FieldResponse != null && !command.FieldResponse.High
                && !command.Terminal && !pawn.Dead && !pawn.Downed && !pawn.InMentalState && !pawn.Drafted
                && !pawn.IsBurning() && !(pawn.jobs.curDriver is JobDriver_Flee)
                && !(pawn.jobs.curDriver is JobDriver_FleeAndCower) && !(pawn.jobs.curDriver is JobDriver_FleeAndCowerShort)
                && command.Phase != TacticalCommandPhase.Returning
                && !TacticalFieldDoctrine.EquipmentWork(pawn.CurJob)
                && !LowTreatmentWait(command, pawn)
                && command.MedicalCare == null && !PawnUtility.PlayerForcedJobNowOrSoon(pawn) && command.BagRecovery?.Helper != pawn
                && command.FieldResponse.Screen?.Thrower != pawn
                && pawn.CurJobDef?.defName != TacticalExplosiveEvasionPolicy.JobName
                && pawn.CurJobDef?.defName != "HD_NewTacticalFocusFire"
                && !(command.FieldResponse.LowFlank && !command.FieldResponse.LowFlankAttack
                    && command.FieldResponse.FireGroup.Contains(command.Members.FindIndex(m => m.Pawn == pawn)));
        }
        internal bool LowFieldFloor(TacticalSquadCommand command, IntVec3 cell) =>
            TacticalFieldDoctrine.WithinEllipse(command.FieldResponse.Anchor, cell, command.FieldResponse.Forward,
                TacticalFieldDoctrine.LowRadius, TacticalFieldDoctrine.LowDepth) && FieldFloor(command, cell);
        internal bool LowLocusContains(TacticalSquadCommand command, Pawn pawn, IntVec3 cell)
        {
            LowLocus(command, pawn, out IntVec3 locus, out int radius);
            return TacticalFieldDoctrine.WithinEllipse(locus, cell, command.FieldResponse.Forward, radius,
                radius == TacticalFieldDoctrine.LowFlankRadius ? TacticalFieldDoctrine.LowFlankDepth : TacticalFieldDoctrine.LowMainDepth);
        }
        internal void LowLocus(TacticalSquadCommand command, Pawn pawn, out IntVec3 locus, out int radius)
        {
            var field = command.FieldResponse;
            bool flank = field.LowFlank && field.LowFlankAttack && field.FireGroup.Contains(command.Members.FindIndex(m => m.Pawn == pawn));
            locus = flank ? field.FirePoints[1] : field.Anchor;
            radius = flank ? TacticalFieldDoctrine.LowFlankRadius : TacticalFieldDoctrine.LowMainRadius;
        }
        internal Job LowNativeJob(Pawn pawn, int tick, bool controller = false)
        {
            if (!LowFieldContext(pawn, out var command)) return null;
            var member = command.Members.Find(m => m.Pawn == pawn);
            var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!controller && !budget.TryJob(tick)) return null;
            Job job = null; bool nativeDecision = false;
            if (tick >= member.LowDecisionAt && budget.TryPlan(tick))
            {
                long started = Stopwatch.GetTimestamp();
                try
                {
                    member.LowDecisionAt = tick + 180 + pawn.thingIDNumber % 30;
                    if (TryLowShelter(command, pawn, out IntVec3 shelter)) job = JobMaker.MakeJob(JobDefOf.Goto, shelter);
                    else if (LowLocusContains(command, pawn, pawn.Position)) job = TacticalLowFieldFight.Instance.Build(pawn);
                    if (job != null && job.def == JobDefOf.Goto && (!LowFieldFloor(command, job.targetA.Cell)
                        || !LowLocusContains(command, pawn, job.targetA.Cell)))
                    { JobMaker.ReturnToPool(job); job = null; }
                    nativeDecision = job != null;
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            }
            if (job == null)
            {
                LowLocus(command, pawn, out IntVec3 locus, out int radius);
                if (!LowLocusContains(command, pawn, pawn.Position))
                {
                    // Bounded straight seed probes, one native path only after
                    // acceptance. Never roam around the map to regain cohesion.
                    IntVec3 best = IntVec3.Invalid; int score = int.MaxValue;
                    foreach (IntVec3 cell in GenRadial.RadialCellsAround(locus, 3, true))
                        if (LowFieldFloor(command, cell) && cell.GetFirstPawn(map) == null && cell.DistanceToSquared(pawn.Position) < score)
                        { best = cell; score = cell.DistanceToSquared(pawn.Position); }
                    if (best.IsValid) job = JobMaker.MakeJob(JobDefOf.Goto, best);
                }
                if (job == null) job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            }
            job.expiryInterval = job.def == JobDefOf.Wait_Combat && !nativeDecision ? 30 + pawn.thingIDNumber % 15 : 240 + pawn.thingIDNumber % 60;
            job.checkOverrideOnExpire = false;
            if (job.def == JobDefOf.Goto && !budget.TryPath(tick))
            {
                JobMaker.ReturnToPool(job); job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
                job.expiryInterval = 30 + pawn.thingIDNumber % 15; job.checkOverrideOnExpire = false;
            }
            pendingLowJobs[pawn] = job;
            return job;
        }
        internal void AdoptLowNative(Pawn pawn, Job job)
        {
            if (!pendingLowJobs.TryGetValue(pawn, out Job pending) || pending != job) return;
            pendingLowJobs.Remove(pawn);
            if (pawn.CurJob != job) return;
            if (!LowFieldContext(pawn, out var command)) return;
            var member = command.Members.Find(m => m.Pawn == pawn); if (member == null) return;
            member.Job = job; member.LastProgressTick = GenTicks.TicksGame;
            pawn.jobs.curDriver.AddFinishAction(condition =>
            {
                if (member.Job != job) return;
                member.Job = null; Wake(pawn);
            });
        }
        private bool AdvanceLowField(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick, bool support)
        {
            command.Due = Math.Min(command.Due, tick + 10);
            AdvanceLowDefense(command, active, field, tick);
            if (field.LowFlank && (tick - field.FlankStarted >= TacticalFieldDoctrine.FlankLifetime
                || active.Count(m => field.FireGroup.Contains(command.Members.IndexOf(m))) < 3
                || !active.Exists(m => LowAutomatic(m) && field.FireGroup.Contains(command.Members.IndexOf(m)))))
            {
                field.LowFlank = field.LowFlankAttack = false; field.FireGroup.Clear(); field.Stage = TacticalFieldStage.Defending;
                for (int i = 0; i < command.Members.Count; i++) { ClearFieldPost(command, field, i); field.Groups[i] = 0; command.Members[i].LowDecisionAt = 0; }
            }
            bool automatic = active.Exists(LowAutomatic);
            if (!field.LowFlank && field.LowDefenseSearch == null && tick >= field.NextFlank)
            {
                bool attempt = TacticalFieldDoctrine.FlankOpportunity(tick, field.NextFlank, command.Defensive, support,
                    active.Count, automatic, unchecked(field.EnemyId * 397 ^ tick / TacticalFieldDoctrine.FlankInterval));
                field.NextFlank = tick + TacticalFieldDoctrine.FlankInterval;
                if (attempt)
                {
                    var gunner = active.First(LowAutomatic);
                    var party = active.OrderBy(m => m == gunner ? -1 : m.Pawn.Position.DistanceToSquared(gunner.Pawn.Position))
                        .Take(TacticalFieldDoctrine.FlankSize(active.Count));
                    field.FireGroup.Clear(); foreach (var member in party) field.FireGroup.Add(command.Members.IndexOf(member));
                    for (int i = 0; i < command.Members.Count; i++) ClearFieldPost(command, field, i);
                    field.LowFlank = true; field.LowFlankAttack = false; field.FlankStarted = tick;
                    field.FlankSide = (field.EnemyId ^ tick) % 2 == 0 ? -1 : 1;
                    field.FirePoints[1] = TacticalFieldDoctrine.FlankPoint(field.Anchor, field.Forward, field.FlankSide, false);
                    field.Areas[1] = new TacticalFireArea { Started = tick };
                    field.Stage = TacticalFieldStage.Forming;
                    for (int i = 0; i < field.Groups.Count; i++) field.Groups[i] = field.FireGroup.Contains(i) ? 1 : 0;
                    if (tick >= field.SmokeRetryAt) PlanFieldSmoke(command, active, field, tick);
                }
            }
            if (field.LowFlank && !field.LowFlankAttack)
            {
                if (field.Areas[1].Prepared && field.Areas[1].Cells.Count == 0)
                { field.FlankStarted = tick - TacticalFieldDoctrine.FlankLifetime; command.Due = tick + 1; return true; }
                int arrived = active.Count(m => field.FireGroup.Contains(command.Members.IndexOf(m)) && AtPost(m));
                if (TacticalFieldPolicy.Ready(field.FireGroup.Count, arrived) || tick - field.FlankStarted >= TacticalFieldDoctrine.FlankGatherTicks)
                {
                    field.LowFlankAttack = true; field.Stage = TacticalFieldStage.Defending;
                    field.FirePoints[1] = TacticalFieldDoctrine.FlankPoint(field.Anchor, field.Forward, field.FlankSide, true);
                    foreach (var member in active) if (field.FireGroup.Contains(command.Members.IndexOf(member)))
                    { ClearFieldPost(command, field, command.Members.IndexOf(member)); member.LowDecisionAt = 0; }
                }
            }
            var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            int issued = 0;
            for (int visit = 0; visit < active.Count && issued < 2; visit++)
            {
                if (field.NativeCursor >= active.Count) field.NativeCursor = 0;
                var member = active[field.NativeCursor++]; Pawn pawn = member.Pawn;
                if (pawn.CurJob?.playerForced == true || TacticalExplosiveEvasionPolicy.OwnsMovement(pawn.CurJob)
                    || LowTreatmentWait(command, pawn)
                    || TacticalFieldDoctrine.EquipmentWork(pawn.CurJob)
                    || pawn.IsBurning() || pawn.jobs.curDriver is JobDriver_Flee || pawn.jobs.curDriver is JobDriver_FleeAndCower
                    || pawn.jobs.curDriver is JobDriver_FleeAndCowerShort
                    || field.Screen?.Thrower == pawn && field.Screen.ActiveJob != null) continue;
                int index = command.Members.IndexOf(member);
                if (field.LowFlank && !field.LowFlankAttack && field.FireGroup.Contains(index))
                {
                    if (field.Screen != null) continue;
                    if (!field.Posts[index].IsValid && budget.TryPlan(tick))
                    {
                        long start = Stopwatch.GetTimestamp();
                        try { AssignFieldPost(command, member, field, index, field.FirePoints[1]); }
                        finally { budget.Account(tick, Stopwatch.GetTimestamp() - start); }
                    }
                    if (field.Posts[index].IsValid && (member.Pawn.CurJob != member.Job || member.Job?.def.defName != "HD_NewTacticalContactGuard")
                        && CanIssue(member)) { Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"), field.Posts[index], field.Focus)); issued++; }
                    continue;
                }
                bool native = pawn.CurJob == member.Job && (pawn.CurJobDef == JobDefOf.Wait_Combat || pawn.CurJobDef == JobDefOf.Goto || pawn.CurJobDef == JobDefOf.AttackMelee);
                if (native && TacticalFieldDoctrine.WithinEllipse(field.Anchor, pawn.Position, field.Forward,
                        TacticalFieldDoctrine.LowRadius + 2, TacticalFieldDoctrine.LowDepth + 2)
                    && FieldFloor(command, pawn.Position)
                    && LowLocusContains(command, pawn, pawn.CurJobDef == JobDefOf.Goto ? pawn.CurJob.targetA.Cell : pawn.Position)
                    && !(pawn.CurJobDef == JobDefOf.Wait_Combat && tick >= member.LowDecisionAt && LowNeedsShelter(command, pawn))) continue;
                if (!CanIssue(member)) continue;
                Job job = LowNativeJob(pawn, tick, controller: true);
                if (job != null && Issue(member, job)) issued++;
            }
            if (!field.LowFlank) field.Stage = TacticalFieldStage.Defending;
            return true;
        }
    }

    // Native acquisition, ability selection, cover scoring, Wait_Combat firing
    // and Goto execution; only the shooting-position request is constrained.
    public sealed class TacticalLowFieldFight : JobGiver_AIFightEnemies
    {
        public static readonly TacticalLowFieldFight Instance = new TacticalLowFieldFight();
        public TacticalLowFieldFight() { ignoreNonCombatants = true; }
        protected override bool DisableAbilityVerbs => true;
        public Job Build(Pawn pawn) => base.TryGiveJob(pawn);
        protected override float GetFlagRadius(Pawn pawn) => TacticalFieldDoctrine.LowRadius + (pawn.equipment?.PrimaryEq?.PrimaryVerb?.EffectiveRange ?? 30);
        protected override IntVec3 GetFlagPosition(Pawn pawn) => pawn.Map.GetComponent<MapComponent_TacticalCommands>().LowFieldContext(pawn, out var command)
            ? command.FieldResponse.Anchor : pawn.Position;
        protected override bool ExtraTargetValidator(Pawn pawn, Thing target) => (target is Building_Turret
            || target is Pawn enemy && !enemy.Dead && !enemy.Downed && (TacticalThreatEligibility.Ranged(enemy) || enemy.RaceProps.Humanlike))
            && base.ExtraTargetValidator(pawn, target);
        protected override bool TryFindShootingPosition(Pawn pawn, out IntVec3 dest, Verb verbToUse = null)
        {
            var owner = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
            if (!owner.LowFieldContext(pawn, out var command)) { dest = IntVec3.Invalid; return false; }
            var target = pawn.mindState.enemyTarget;
            Verb verb = verbToUse ?? pawn.TryGetAttackVerb(target, true);
            if (verb == null) { dest = IntVec3.Invalid; return false; }
            owner.LowLocus(command, pawn, out IntVec3 locus, out int radius);
            return CastPositionFinder.TryFindCastPosition(new CastPositionRequest { caster = pawn, target = target, verb = verb,
                maxRangeFromTarget = verb.EffectiveRange, maxRangeFromCaster = 8, locus = locus, maxRangeFromLocus = radius,
                maxRegions = 3, wantCoverFromTarget = verb.EffectiveRange > 5,
                validator = c => owner.LowFieldFloor(command, c) && owner.LowLocusContains(command, pawn, c) }, out dest);
        }
    }
    [NewTactical, HarmonyPatch(typeof(Pawn_JobTracker), "DetermineNextJob")]
    public static class Patch_NewTactical_LowFieldJobs
    {
        private static readonly Func<Pawn_JobTracker, ThinkResult> ConstantJobs =
            (Func<Pawn_JobTracker, ThinkResult>)Delegate.CreateDelegate(typeof(Func<Pawn_JobTracker, ThinkResult>),
                AccessTools.Method(typeof(Pawn_JobTracker), "DetermineNextConstantThinkTreeJob"));
        public static bool Prefix(Pawn_JobTracker __instance, Pawn ___pawn, ref bool ___determiningNextJob,
            ref ThinkTreeDef thinkTree, ref ThinkResult __result)
        {
            var owner = ___pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            if (___determiningNextJob || owner?.LowFieldContext(___pawn, out _) != true) return true;
            ___determiningNextJob = true;
            try
            {
                // Preserve the exact native constant-tree safety/mental checks.
                ThinkResult safety = ConstantJobs(__instance);
                if (safety.IsValid) { __result = safety; thinkTree = ___pawn.thinker.ConstantThinkTree; return false; }
                Job job = owner.LowNativeJob(___pawn, GenTicks.TicksGame);
                // Use the pawn's registered tree root as provenance; the
                // reusable request adapter is not a serialized think-tree node.
                __result = job == null ? ThinkResult.NoJob : new ThinkResult(job, ___pawn.thinker.MainThinkNodeRoot);
                thinkTree = ___pawn.thinker.MainThinkTree; return false;
            }
            finally { ___determiningNextJob = false; }
        }
    }
    [NewTactical, HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_NewTactical_AdoptLowFieldJob
    {
        public static void Postfix(Pawn ___pawn, Job newJob) => ___pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.AdoptLowNative(___pawn, newJob);
    }
}
