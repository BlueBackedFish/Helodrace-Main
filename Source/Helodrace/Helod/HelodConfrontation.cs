using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class HelodConfrontationSettings
    {
        public float mediatorRadius = 6f;
        public float mediatorJoinChance = 0.45f;
        public IntRange durationTicks = new IntRange(360, 600);
        public int fullMediationTicks = 120;
    }

    [DefOf]
    public static class HelodConfrontationDefOf
    {
        public static MentalStateDef HD_HelodConfrontation;
        public static JobDef HD_HelodConfront;
        public static JobDef HD_HelodMediate;
        public static ThoughtDef HD_HelodReconciled;
        public static ThoughtDef Insulted;
        public static ThoughtDef Slighted;
        public static RulePackDef HD_Sentence_HelodConfrontationStarted;

        static HelodConfrontationDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(HelodConfrontationDefOf));
    }

    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.StartSocialFight))]
    public static class Patch_HelodConfrontationStart
    {
        public static bool Prefix(Pawn ___pawn, Pawn otherPawn)
        {
            if (!HelodRace.IsHelod(___pawn) || !HelodRace.IsHelod(otherPawn)) return true;
            // The resolved confrontation has already been removed before vanilla is entered.
            if (HelodConfrontationUtility.StartingVanillaFight) return true;
            HelodConfrontationUtility.TryStart(___pawn, otherPawn);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayLogEntry_Interaction), MethodType.Constructor,
        new[] { typeof(InteractionDef), typeof(Pawn), typeof(Pawn), typeof(List<RulePackDef>) })]
    public static class Patch_HelodConfrontationLog
    {
        public static void Prefix(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks)
        {
            if (!(recipient?.MentalState is MentalState_HelodConfrontation state)
                || state.Other != initiator || extraSentencePacks == null) return;
            for (int i = 0; i < extraSentencePacks.Count; i++)
                if (extraSentencePacks[i] == RulePackDefOf.Sentence_SocialFightStarted
                    || extraSentencePacks[i]?.defName == "Sentence_ConvertIdeoAttemptFailSocialFight")
                    extraSentencePacks[i] = HelodConfrontationDefOf.HD_Sentence_HelodConfrontationStarted;
        }
    }

    public static class HelodConfrontationUtility
    {
        [System.ThreadStatic] internal static bool StartingVanillaFight;
        internal static HelodConfrontationSettings Settings => HelodRace.Settings.confrontation;

        internal static bool Available(Pawn pawn) => pawn != null && pawn.Spawned && !pawn.Dead
            && !pawn.Downed && pawn.Awake() && !pawn.IsBurning();

        public static bool TryStart(Pawn first, Pawn second)
        {
            if (!HelodRace.IsHelod(first) || !HelodRace.IsHelod(second)
                || first == second || !Available(first) || !Available(second) || first.Map != second.Map
                || first.InMentalState || second.InMentalState || first.HostileTo(second)) return false;
            MentalStateDef stateDef = HelodConfrontationDefOf.HD_HelodConfrontation;
            if (!stateDef.Worker.StateCanOccur(first) || !stateDef.Worker.StateCanOccur(second)
                || first.mindState.mentalStateHandler.MentalBreaksBlocked()
                || second.mindState.mentalStateHandler.MentalBreaksBlocked()) return false;

            if (!first.mindState.mentalStateHandler.TryStartMentalState(stateDef, otherPawn: second)) return false;
            if (!second.mindState.mentalStateHandler.TryStartMentalState(stateDef, otherPawn: first))
            {
                first.MentalState.RecoverFromState();
                return false;
            }

            var leader = (MentalState_HelodConfrontation)(first.thingIDNumber < second.thingIDNumber
                ? first.MentalState : second.MentalState);
            leader.resolveTick = Find.TickManager.TicksGame + Mathf.Max(60, Settings.durationTicks.RandomInRange);
            Notify("HD_Confrontation_Started", first, second, MessageTypeDefOf.ThreatSmall);
            leader.RecruitMediator();
            return true;
        }

        internal static void StartVanillaFight(Pawn first, Pawn second)
        {
            StartingVanillaFight = true;
            try { first.interactions.StartSocialFight(second); }
            finally { StartingVanillaFight = false; }
        }

        internal static void Notify(string key, Pawn first, Pawn second, MessageTypeDef type)
        {
            if (PawnUtility.ShouldSendNotificationAbout(first) || PawnUtility.ShouldSendNotificationAbout(second))
                Messages.Message(key.Translate(first.Named("FIRST"), second.Named("SECOND")),
                    new LookTargets(first, second), type);
        }

        internal static float Mood(Pawn pawn) => pawn.needs?.mood?.CurLevel ?? 0.5f;
        internal static float Opinion(Pawn pawn, Pawn other) => pawn.relations?.OpinionOf(other) ?? 0f;
        internal static int Social(Pawn pawn) => pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
        internal static bool Trait(Pawn pawn, TraitDef trait) => pawn.story?.traits?.HasTrait(trait) == true;
    }

    public sealed class MentalState_HelodConfrontation : MentalState
    {
        public Pawn Other => causedByPawn;
        public Pawn mediator;
        public int resolveTick;
        public int mediatedTicks;

        public MentalState_HelodConfrontation Partner => Other?.MentalState is MentalState_HelodConfrontation state
            && state.Other == pawn ? state : null;
        public bool IsLeader => Other != null && pawn.thingIDNumber < Other.thingIDNumber;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref mediator, "mediator");
            Scribe_Values.Look(ref resolveTick, "resolveTick");
            Scribe_Values.Look(ref mediatedTicks, "mediatedTicks");
        }

        public override RandomSocialMode SocialModeMax() => RandomSocialMode.Off;

        public override void MentalStateTick(int delta)
        {
            // This paired state has one deadline and one outcome roll, owned by the lower ID.
            // Do not call the base random mental-state recovery while the confrontation is active.
            if (!HelodConfrontationUtility.Available(pawn) || !HelodConfrontationUtility.Available(Other)
                || pawn.Map != Other.Map || Partner == null || pawn.HostileTo(Other)
                || !pawn.Position.InHorDistOf(Other.Position, 8f))
            {
                RecoverFromState();
                return;
            }
            if (!IsLeader) return;
            if (MediatorPresent()) mediatedTicks += delta;
            if (Find.TickManager.TicksGame >= resolveTick) Resolve();
        }

        public override void PostEnd()
        {
            base.PostEnd();
            // RecoverFromState clears this state before PostEnd, so reciprocal cleanup cannot recurse.
            MentalState_HelodConfrontation partner = Partner;
            partner?.RecoverFromState();
            if (mediator?.CurJobDef == HelodConfrontationDefOf.HD_HelodMediate
                && mediator.CurJob.targetA.Pawn == pawn)
                mediator.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        internal void RecruitMediator()
        {
            var candidates = new List<Pawn>();
            float radius = Mathf.Max(0f, HelodConfrontationUtility.Settings.mediatorRadius);
            foreach (Pawn candidate in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (candidate == pawn || candidate == Other || !HelodRace.IsHelod(candidate)
                    || !HelodConfrontationUtility.Available(candidate) || candidate.InMentalState
                    || candidate.Drafted || candidate.IsPrisoner || candidate.IsSlave
                    || candidate.Faction != pawn.Faction || candidate.Faction != Other.Faction
                    || candidate.CurJob?.playerForced == true || candidate.IsCarryingPawn()
                    || candidate.CurJobDef == HelodConfrontationDefOf.HD_HelodMediate
                    || (candidate.CurJobDef != null && !candidate.CurJobDef.casualInterruptible)
                    || !candidate.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                    || candidate.skills?.GetSkill(SkillDefOf.Social)?.TotallyDisabled == true) continue;
                if (!candidate.Position.InHorDistOf(pawn.Position, radius)
                    && !candidate.Position.InHorDistOf(Other.Position, radius)) continue;
                if (!GenSight.LineOfSight(candidate.Position, pawn.Position, pawn.Map)
                    || !GenSight.LineOfSight(candidate.Position, Other.Position, pawn.Map)
                    || !candidate.CanReach(pawn, PathEndMode.Touch, Danger.Some)) continue;
                candidates.Add(candidate);
            }
            candidates.Shuffle();
            foreach (Pawn candidate in candidates)
            {
                float chance = HelodConfrontationUtility.Settings.mediatorJoinChance
                    * Mathf.Lerp(0.5f, 1.25f, HelodConfrontationUtility.Mood(candidate))
                    * Mathf.Lerp(0.75f, 1.5f, HelodConfrontationUtility.Social(candidate) / 20f);
                if (HelodConfrontationUtility.Trait(candidate, TraitDefOf.Kind)) chance *= 1.5f;
                if (HelodConfrontationUtility.Trait(candidate, TraitDefOf.Abrasive)) chance *= 0.5f;
                if (!Rand.Chance(Mathf.Clamp01(chance))) continue;
                mediator = candidate;
                candidate.jobs.StartJob(JobMaker.MakeJob(HelodConfrontationDefOf.HD_HelodMediate, pawn, Other),
                    JobCondition.InterruptOptional, resumeCurJobAfterwards: true);
                HelodConfrontationUtility.Notify("HD_Confrontation_Mediator", candidate, pawn, MessageTypeDefOf.NeutralEvent);
                break;
            }
        }

        private bool MediatorPresent()
        {
            return HelodConfrontationUtility.Available(mediator) && !mediator.InMentalState && !mediator.Drafted
                && mediator.CurJobDef == HelodConfrontationDefOf.HD_HelodMediate
                && mediator.CurJob.targetA.Pawn == pawn
                && mediator.Position.InHorDistOf(pawn.Position, 3f)
                && mediator.Position.InHorDistOf(Other.Position, 6f)
                && mediator.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                && GenSight.LineOfSight(mediator.Position, pawn.Position, pawn.Map)
                && GenSight.LineOfSight(mediator.Position, Other.Position, pawn.Map);
        }

        private void Resolve()
        {
            float mediation = MediatorPresent() ? Mathf.Clamp01((float)mediatedTicks
                / Mathf.Max(1, HelodConfrontationUtility.Settings.fullMediationTicks))
                * Mathf.Lerp(0.35f, 1f, HelodConfrontationUtility.Social(mediator) / 20f)
                * Mathf.Lerp(0.5f, 1f, HelodConfrontationUtility.Mood(mediator)) : 0f;
            float aggression = (Aggression(pawn) + Aggression(Other)) * 0.5f;
            HelodConfrontationOutcome outcome = HelodConfrontationOutcomes.Calculate(
                HelodConfrontationUtility.Mood(pawn), HelodConfrontationUtility.Mood(Other),
                HelodConfrontationUtility.Opinion(pawn, Other), HelodConfrontationUtility.Opinion(Other, pawn),
                mediation, aggression).Choose(Rand.Value);
            Pawn other = Other;
            RecoverFromState();
            if (outcome == HelodConfrontationOutcome.Fight)
            {
                HelodConfrontationUtility.StartVanillaFight(pawn, other);
                return;
            }
            if (outcome == HelodConfrontationOutcome.Reconcile)
            {
                Reconcile(pawn, other);
                Reconcile(other, pawn);
            }
            HelodConfrontationUtility.Notify(outcome == HelodConfrontationOutcome.Reconcile
                ? "HD_Confrontation_Reconciled" : "HD_Confrontation_Dispersed", pawn, other,
                MessageTypeDefOf.SituationResolved);
        }

        private static float Aggression(Pawn pawn)
        {
            float value = 1f;
            if (HelodConfrontationUtility.Trait(pawn, TraitDefOf.Abrasive)) value += 0.4f;
            if (HelodConfrontationUtility.Trait(pawn, TraitDefOf.Bloodlust)) value += 0.4f;
            if (HelodConfrontationUtility.Trait(pawn, TraitDefOf.Kind)) value -= 0.4f;
            return value;
        }

        private static void Reconcile(Pawn pawn, Pawn other)
        {
            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts.memories;
            if (memories == null) return;
            memories.RemoveMemoriesOfDefWhereOtherPawnIs(HelodConfrontationDefOf.Insulted, other);
            memories.RemoveMemoriesOfDefWhereOtherPawnIs(HelodConfrontationDefOf.Slighted, other);
            memories.TryGainMemory(HelodConfrontationDefOf.HD_HelodReconciled, other);
        }
    }

    public sealed class JobGiver_HelodConfrontation : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var state = pawn.MentalState as MentalState_HelodConfrontation;
            if (state?.Partner == null) return null;
            return JobMaker.MakeJob(HelodConfrontationDefOf.HD_HelodConfront, state.Other);
        }
    }

    public sealed class JobDriver_HelodConfront : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !(pawn.MentalState is MentalState_HelodConfrontation state)
                || state.Other != job.targetA.Pawn || state.Partner == null);
            yield return Toils_General.Wait(120, TargetIndex.A);
        }
    }

    public sealed class JobDriver_HelodMediate : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.FailOn(() => !(job.targetA.Pawn?.MentalState is MentalState_HelodConfrontation state)
                || state.mediator != pawn || state.Other != job.targetB.Pawn || state.Partner == null
                || pawn.Drafted || pawn.InMentalState);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(1200, TargetIndex.B);
        }
    }
}
