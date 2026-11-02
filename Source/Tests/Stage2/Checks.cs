using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Helodrace.Economy;
using RimWorld;
using Verse;

namespace Helodrace.Tests.Stage2;

public sealed partial class Checks : GameComponent
{
    private bool started, complete;
    private string output;
    private Map map;
    private Pawn actor;
    private IEnumerator<int> steps;
    private int remaining;
    private bool loadRequested;
    private int roundtripStage;
    private Pawn fighter;
    private Apparel savedKnife;
    private static Map fixtureMap;
    private static bool climatePatched;
    private static bool FixtureTemperature(MapTemperature __instance, ref float __result)
    {
        if (fixtureMap == null || AccessTools.Field(typeof(MapTemperature), "map").GetValue(__instance) != fixtureMap) return true;
        __result = 21f;
        return false;
    }
    private void ConfigureClimate()
    {
        fixtureMap = map;
        if (climatePatched) return;
        new Harmony("helodrace.tests.stage2.climate").Patch(AccessTools.PropertyGetter(typeof(MapTemperature), nameof(MapTemperature.OutdoorTemp)),
            prefix: new HarmonyMethod(typeof(Checks), nameof(FixtureTemperature)));
        climatePatched = true;
    }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref roundtripStage, "stage2Roundtrip", 0);
        Scribe_References.Look(ref fighter, "stage2Fighter");
        Scribe_References.Look(ref savedKnife, "stage2Knife");
    }
    public Checks(Game game) { }
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + "\n");
    private void Check(bool ok, string text)
    {
        if (!ok) throw new InvalidOperationException(text);
        Write("PASS: " + text);
    }
    public override void GameComponentTick()
    {
        if (started || complete || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdStage2", out output)) return;
        started = true;
        try
        {
            Directory.CreateDirectory(output);
            if (roundtripStage == 1)
            {
                map = fighter.Map;
                ConfigureClimate();
                Find.TickManager.Pause();
                steps = AfterLoad().GetEnumerator();
                return;
            }
            File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 stage 2 checks\n");
            map = Find.CurrentMap;
            ConfigureClimate();
            // noAnimals destroys caravan pack animals during Pawn.Tick as well.
            // Preserve normal animal simulation for real stock hauling and exit.
            DebugSettings.noAnimals = false;
            DebugSettings.enableRandomMentalStates = false;
            Find.Storyteller.difficulty.allowBigThreats = false;
            Find.Storyteller.difficulty.allowIntroThreats = false;
            Find.Storyteller.difficulty.allowExtremeWeatherIncidents = false;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                BodyType(pawn);
                if (pawn.Faction != Faction.OfPlayer) pawn.DeSpawn();
                else if (pawn.drafter != null) pawn.drafter.Drafted = true;
            }
            foreach (IntVec3 cell in CellRect.CenteredOn(map.Center, 24))
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (thing is not Pawn && thing.Spawned && thing.def.destroyable) thing.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                map.roofGrid.SetRoof(cell, null);
                map.fogGrid.Unfog(cell);
                map.areaManager.Home[cell] = true;
            }
            actor = NewPawn(PawnKindDefOf.Colonist, Faction.OfPlayer, map.Center);
            actor.skills.GetSkill(SkillDefOf.Social).Level = 15;
            Snapshot();
            if (GenCommandLine.TryGetCommandLineArg("hdStage2Baseline", out _)) Finish();
            else
            {
                Find.TickManager.Pause();
                steps = AllChecks().GetEnumerator();
            }
        }
        catch (Exception error) { Fail(error); }
    }
    public override void GameComponentUpdate()
    {
        if (complete || steps == null) return;
        try
        {
            if (remaining == 0)
            {
                if (!steps.MoveNext()) { Finish(); return; }
                if (loadRequested)
                {
                    steps = null;
                    GameDataSaveLoader.LoadGame("Stage2Roundtrip");
                    return;
                }
                remaining = steps.Current;
            }
            int count = Math.Min(remaining, 200);
            for (int i = 0; i < count; i++) Find.TickManager.DoSingleTick();
            remaining -= count;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                if (pawn.needs != null) foreach (Need need in pawn.needs.AllNeeds) need.CurLevel = need.MaxLevel;
        }
        catch (Exception error) { Fail(error); }
    }
    private void Fail(Exception error)
    {
        complete = true;
        if (actor != null) Write($"ACTOR spawned={actor.Spawned} dead={actor.Dead} downed={actor.Downed} map={actor.Map?.uniqueID} talking={actor.health.capacities.CapableOf(PawnCapacityDefOf.Talking)} socialDisabled={actor.skills.GetSkill(SkillDefOf.Social).TotallyDisabled} temp={map.mapTemperature.OutdoorTemp} hediffs="
            + string.Join(",", actor.health.hediffSet.hediffs.Select(h => h.def.defName + "=" + h.Severity)));
        Write("FAIL: " + error);
        Log.Error("Stage 2 checks: " + error);
        Waylith.EngineApplication.Instance.Quit(1);
    }
    private void Finish() { complete = true; Write("COMPLETE"); Waylith.EngineApplication.Instance.Quit(0); }
    private static void BodyType(Pawn pawn)
    {
        if (pawn.story != null && pawn.story.bodyType == null)
            pawn.story.bodyType = pawn.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male;
    }
    private Pawn NewPawn(PawnKindDef kind, Faction faction, IntVec3 position)
    {
        var request = new PawnGenerationRequest(kind, faction, canGeneratePawnRelations: false,
            fixedBiologicalAge: 25, fixedChronologicalAge: 25);
        request.ForceNoBackstory = true;
        Pawn pawn = PawnGenerator.GeneratePawn(request);
        BodyType(pawn);
        GenSpawn.Spawn(pawn, position, map);
        if (pawn.drafter != null) pawn.drafter.Drafted = true;
        return pawn;
    }
    private static T Def<T>(string name) where T : Verse.Def => DefDatabase<T>.GetNamed(name);
    private static Faction Low => Find.FactionManager.AllFactionsListForReading.First(f => f.def.defName == "HD_HelodCivilLowFaction");
    private PawnGroupMakerParms Group(float points) => new PawnGroupMakerParms
    {
        faction = Low, groupKind = PawnGroupKindDefOf.Trader, tile = map.Tile,
        traderKind = Def<TraderKindDef>("HD_WW_Caravan_HelodPetroleum"), points = points
    };
    private void Snapshot()
    {
        foreach (float points in new[] { 20f, 260f, 1000f })
        {
            var parms = Group(points);
            bool found = PawnGroupMakerUtility.TryGetRandomPawnGroupMaker(parms, out PawnGroupMaker maker);
            var pawns = found ? PawnGroupMakerUtility.GeneratePawns(parms).ToList() : new List<Pawn>();
            Write($"SNAPSHOT petroleum input={points} selected={maker?.GetType().Name} remaining={parms.points} maxGuardCost={PawnGroupMakerUtility.MaxPawnCost(Low, parms.points, null, PawnGroupKindDefOf.Trader)} pawns="
                + string.Join(",", pawns.Select(p => p.kindDef.defName + ":" + p.inventory.innerContainer.Count)));
        }
        var knife = (Apparel)ThingMaker.MakeThing(Def<ThingDef>("HD_USMCFightingKnife"), ThingDefOf.Steel);
        var wearer = NewPawn(HelodRace.ColonistKind, Faction.OfPlayer, map.Center + new IntVec3(8, 0, 0));
        wearer.equipment.DestroyAllEquipment();
        wearer.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def<ThingDef>("Gun_BoltActionRifle")));
        wearer.apparel.Wear(knife, false);
        var comp = knife.TryGetComp<CompWornMeleeWeapon>();
        var verbs = wearer.meleeVerbs.GetUpdatedAvailableVerbsList(false);
        Check(wearer.apparel.WornApparel.Contains(knife), "Baseline knife fixture actually wears the knife on a Helod");
        Write("SNAPSHOT knife " + string.Join(";", comp.AllVerbs.Select(v => $"{v.tool?.label} caster={v.caster?.def.defName} available={v.Available()} entries={verbs.Count(e => e.verb == v)} cachedWeight={verbs.Where(e => e.verb == v).Sum(e => (float)AccessTools.Field(typeof(VerbEntry), "cachedSelectionWeight").GetValue(e))}")));
        wearer.apparel.Remove(knife);
        knife.Destroy();
        wearer.DeSpawn();
    }
    private IEnumerable<int> AllChecks()
    {
        PolicyChecks();
        foreach (int ticks in CaravanChecks()) yield return ticks;
        foreach (int ticks in MeleeChecks()) yield return ticks;
        yield return 1;
    }
}
