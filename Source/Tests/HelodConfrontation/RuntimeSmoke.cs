using System;
using System.Linq;
using HarmonyLib;
using Helodrace;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

[StaticConstructorOnStartup]
public static class HelodConfrontationSmokeBoot
{
    static HelodConfrontationSmokeBoot()
    {
        var host = new GameObject("Helod confrontation regression");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<HelodConfrontationSmoke>();
    }
}

public sealed class HelodConfrontationSmoke : MonoBehaviour
{
    private int phase, frames, firstId, secondId, mediatorId, savedDeadline, savedMediation;
    private Game savedGame;
    private Pawn first, second, mediator;
    private static HelodConfrontationOutcome forcedOutcome;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Ticks(int count) { for (int i = 0; i < count; i++) Find.TickManager.DoSingleTick(); }
    private static bool Choose(ref HelodConfrontationOutcome __result) { __result = forcedOutcome; return false; }
    private MentalState_HelodConfrontation Leader => (MentalState_HelodConfrontation)
        (first.thingIDNumber < second.thingIDNumber ? first.MentalState : second.MentalState);

    private void Update()
    {
        if (phase == 9) return;
        try
        {
            if (++frames > 24000) throw new Exception("Timed out waiting for quicktest map/reload.");
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null
                || LongEventHandler.AnyEventNowOrWaiting) return;
            Find.TickManager.Pause();
            if (phase == 0) Begin();
            else if (phase == 1 && !ReferenceEquals(savedGame, Current.Game)) Finish();
        }
        catch (Exception ex)
        {
            Log.Error("HELOD_CONFRONTATION_FAIL: " + ex);
            phase = 9;
            Application.Quit();
        }
    }

    private Pawn Spawn(PawnKindDef kind, IntVec3 cell)
    {
        Pawn pawn = null;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            pawn = PawnGenerator.GeneratePawn(kind, Faction.OfPlayer);
            if (!pawn.WorkTagIsDisabled(WorkTags.Violent) && !pawn.skills.GetSkill(SkillDefOf.Social).TotallyDisabled) break;
            pawn.Destroy();
            pawn = null;
        }
        Check(pawn != null, "Could not generate a combat/social-capable test pawn.");
        GenSpawn.Spawn(pawn, cell, Find.CurrentMap);
        pawn.needs.rest.CurLevel = 1f;
        pawn.needs.food.CurLevel = 1f;
        return pawn;
    }

    private void Begin()
    {
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Pawn_InteractionsTracker), "StartSocialFight"))
            .Prefixes.Any(p => p.PatchMethod.DeclaringType == typeof(Patch_HelodConfrontationStart)), "StartSocialFight hook missing.");
        Map map = Find.CurrentMap;
        IntVec3 center = map.AllCells.OrderBy(c => c.DistanceToSquared(map.Center)).First(c =>
            CellRect.CenteredOn(c, 4).Cells.All(p => p.InBounds(map) && p.Standable(map) && p.GetEdifice(map) == null));
        first = Spawn(HelodRace.ColonistKind, center);
        second = Spawn(HelodRace.ColonistKind, center + new IntVec3(2, 0, 0));
        mediator = Spawn(HelodRace.ColonistKind, center + new IntVec3(0, 0, 2));
        HelodRace.Settings.confrontation.mediatorJoinChance = 100f;
        HelodRace.Settings.confrontation.durationTicks = new IntRange(1200, 1200);
        first.interactions.StartSocialFight(second);
        Check(first.MentalState is MentalState_HelodConfrontation && second.MentalState is MentalState_HelodConfrontation,
            "Both Helods must enter the confrontation instead of vanilla combat.");
        var state = Leader;
        Check(state.Partner != null && state.mediator == mediator, "Pairing/immediate mediator recruitment failed.");
        var sentences = new System.Collections.Generic.List<RulePackDef> { RulePackDefOf.Sentence_SocialFightStarted };
        new PlayLogEntry_Interaction(InteractionDefOf.Insult, first, second, sentences);
        Check(sentences.Single() == HelodConfrontationDefOf.HD_Sentence_HelodConfrontationStarted,
            "Interaction log must describe confrontation, not a fight that has not happened.");
        int deadline = state.resolveTick;
        first.interactions.StartSocialFight(second);
        Check(ReferenceEquals(state, Leader) && state.resolveTick == deadline, "Repeated starts must not replace an active pair.");
        int injuries = first.health.hediffSet.hediffs.Count + second.health.hediffSet.hediffs.Count;
        Ticks(240);
        Check(first.CurJobDef == HelodConfrontationDefOf.HD_HelodConfront
            && second.CurJobDef == HelodConfrontationDefOf.HD_HelodConfront, "Think tree did not issue confrontation jobs.");
        Check(state.mediatedTicks > 0 && mediator.CurJobDef == HelodConfrontationDefOf.HD_HelodMediate,
            "Mediator must walk into position and actually mediate.");
        Check(injuries == first.health.hediffSet.hediffs.Count + second.health.hediffSet.hediffs.Count,
            "Confrontation must not inflict injuries.");
        firstId = first.thingIDNumber; secondId = second.thingIDNumber; mediatorId = mediator.thingIDNumber;
        savedDeadline = state.resolveTick; savedMediation = state.mediatedTicks; savedGame = Current.Game;
        Log.Message("HELOD_CONFRONTATION_START_PASS: Harmony interception, paired states, no attacks, real mediator movement and repeated-start guard.");
        GameDataSaveLoader.SaveGame("HelodConfrontationSmoke");
        phase = 1;
        GameDataSaveLoader.LoadGame("HelodConfrontationSmoke");
    }

    private void StartWithoutMediator()
    {
        HelodRace.Settings.confrontation.mediatorJoinChance = 0f;
        first.interactions.StartSocialFight(second);
        Check(Leader.Partner != null && Leader.mediator == null, "Unmediated confrontation must still start.");
    }

    private void Resolve(HelodConfrontationOutcome outcome)
    {
        forcedOutcome = outcome;
        Leader.resolveTick = Find.TickManager.TicksGame;
        Leader.MentalStateTick(1);
    }

    private void Finish()
    {
        first = Find.CurrentMap.mapPawns.AllPawnsSpawned.Single(p => p.thingIDNumber == firstId);
        second = Find.CurrentMap.mapPawns.AllPawnsSpawned.Single(p => p.thingIDNumber == secondId);
        mediator = Find.CurrentMap.mapPawns.AllPawnsSpawned.Single(p => p.thingIDNumber == mediatorId);
        Check(Leader.Partner != null && Leader.mediator == mediator && Leader.resolveTick == savedDeadline
            && Leader.mediatedTicks >= savedMediation, "Save/reload lost pair, mediator, deadline or mediation progress.");
        Log.Message("HELOD_CONFRONTATION_SAVE_PASS: full game save/reload preserves both participants, mediator and progress.");
        new Harmony("HelodConfrontation.RuntimeSmoke").Patch(AccessTools.Method(typeof(HelodConfrontationWeights), "Choose"),
            prefix: new HarmonyMethod(typeof(HelodConfrontationSmoke), nameof(Choose)));
        first.needs.mood.thoughts.memories.TryGainMemory(HelodConfrontationDefOf.Insulted, second);
        Resolve(HelodConfrontationOutcome.Reconcile);
        Check(!first.InMentalState && !second.InMentalState && mediator.CurJobDef != HelodConfrontationDefOf.HD_HelodMediate,
            "Reconciliation must release both participants and the mediator.");
        Check(first.needs.mood.thoughts.memories.Memories.Any(m => m.def == HelodConfrontationDefOf.HD_HelodReconciled && m.otherPawn == second)
            && second.needs.mood.thoughts.memories.Memories.Any(m => m.def == HelodConfrontationDefOf.HD_HelodReconciled && m.otherPawn == first),
            "Reconciliation must grant mutual relationship memories.");
        Check(!first.needs.mood.thoughts.memories.Memories.Any(m => m.def == HelodConfrontationDefOf.Insulted && m.otherPawn == second),
            "Reconciliation must clear insults from this partner.");
        StartWithoutMediator();
        int memories = first.needs.mood.thoughts.memories.Memories.Count;
        Resolve(HelodConfrontationOutcome.Disperse);
        Check(!first.InMentalState && !second.InMentalState && first.needs.mood.thoughts.memories.Memories.Count == memories,
            "Dispersal must end peacefully without additional memories.");
        StartWithoutMediator();
        Resolve(HelodConfrontationOutcome.Fight);
        Check(first.MentalStateDef == MentalStateDefOf.SocialFighting && second.MentalStateDef == MentalStateDefOf.SocialFighting,
            "Escalation must enter exact vanilla SocialFighting states without reentering confrontation.");
        bool firstAttacked = false, secondAttacked = false;
        for (int i = 0; i < 120 && !(firstAttacked && secondAttacked); i++)
        {
            Ticks(1);
            firstAttacked |= first.CurJobDef == JobDefOf.SocialFight;
            secondAttacked |= second.CurJobDef == JobDefOf.SocialFight;
        }
        Check(firstAttacked && secondAttacked,
            "Vanilla combat jobs must run after escalation: " + first.CurJobDef + "/" + second.CurJobDef
            + "; states=" + first.MentalStateDef + "/" + second.MentalStateDef);
        first.MentalState.RecoverFromState();
        Check(!second.InMentalState, "Vanilla paired recovery must remain intact.");
        StartWithoutMediator();
        second.MentalState.RecoverFromState();
        Check(!first.InMentalState, "External recovery must cancel the partner's confrontation.");
        StartWithoutMediator();
        second.DeSpawn();
        if (first.MentalState is MentalState_HelodConfrontation remaining) remaining.MentalStateTick(1);
        Check(!first.InMentalState && !second.InMentalState, "Despawn must release both participants.");
        GenSpawn.Spawn(second, first.Position + new IntVec3(1, 0, 0), first.Map);
        Pawn human = Spawn(PawnKindDefOf.Colonist, first.Position + new IntVec3(0, 0, 1));
        first.interactions.StartSocialFight(human);
        Check(first.MentalStateDef == MentalStateDefOf.SocialFighting && human.MentalStateDef == MentalStateDefOf.SocialFighting,
            "Mixed-race fights must keep vanilla behavior.");
        first.MentalState.RecoverFromState();
        Log.Message("HELOD_CONFRONTATION_OUTCOMES_PASS: reconciliation, dispersal, exact vanilla escalation/jobs, cancellation, despawn and mixed-race fallback.");
        phase = 9;
        Application.Quit();
    }
}
