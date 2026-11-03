using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Tests.PowerStageG;

public sealed partial class Checks : GameComponent
{
    private string output;
    private Map map;
    private Building generatorBuilding, primaryEngine, smallEngine;
    private int stage, remaining;
    private bool started, complete, loadRequested;
    private IEnumerator<int> steps;
    public Checks(Game game) { }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref stage, "powerStageG", 0);
        Scribe_References.Look(ref generatorBuilding, "generator");
        Scribe_References.Look(ref primaryEngine, "primaryEngine");
        Scribe_References.Look(ref smallEngine, "smallEngine");
    }
    public override void LoadedGame()
    {
        if (stage != 1 || !GenCommandLine.TryGetCommandLineArg("hdPowerStageG", out output)) return;
        try { Check(generatorBuilding.GetComp<CompMechanicalGenerator>().TargetElectricalOutput == 1800f,
            "Actual save/load preserves the selected 1800 W target before the first tick"); }
        catch (Exception e) { Fail(e); }
    }
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + "\n");
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Write("PASS: " + text); }
    private void Near(float value, float expected, string text) => Check(MathF.Abs(value - expected) < .1f, text + "; actual=" + value + "; expected=" + expected);
    private static T Def<T>(string name) where T : Verse.Def => DefDatabase<T>.GetNamed(name);
    private void Fail(Exception e) { complete = true; Write("FAIL: " + e); Log.Error("Power stage G: " + e); Waylith.EngineApplication.Instance.Quit(1); }
    private void Finish() { complete = true; Write("COMPLETE"); Waylith.EngineApplication.Instance.Quit(0); }
    public override void GameComponentTick()
    {
        if (started || complete || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdPowerStageG", out output)) return;
        started = true;
        try
        {
            Directory.CreateDirectory(output); map = Find.CurrentMap; Find.TickManager.Pause();
            DebugSettings.enableStoryteller = false; DebugSettings.noAnimals = true; DebugSettings.enableRandomMentalStates = false;
            if (stage == 1) { steps = AfterLoad().GetEnumerator(); return; }
            File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 power stage G checks\n");
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (pawn.Faction != Faction.OfPlayer) pawn.DeSpawn();
                else
                {
                    if (pawn.story?.bodyType == null && pawn.story != null) pawn.story.bodyType = pawn.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male;
                    if (pawn.drafter != null) pawn.drafter.Drafted = true;
                    if (pawn.workSettings != null) foreach (var type in DefDatabase<WorkTypeDef>.AllDefsListForReading) pawn.workSettings.SetPriority(type, 0);
                }
            }
            foreach (var cell in CellRect.CenteredOn(map.Center, 29))
            {
                foreach (var thing in cell.GetThingList(map).ToList()) if (thing is not Pawn && thing.Spawned && thing.def.destroyable) thing.Destroy();
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.roofGrid.SetRoof(cell, null);
                map.fogGrid.Unfog(cell); map.areaManager.Home[cell] = true;
            }
            steps = GeneratorChecks().GetEnumerator();
        }
        catch (Exception e) { Fail(e); }
    }
    public override void GameComponentUpdate()
    {
        if (steps == null || complete || Current.ProgramState != ProgramState.Playing) return;
        Find.TickManager.Pause();
        try
        {
            if (remaining == 0)
            {
                if (!steps.MoveNext()) { Finish(); return; }
                if (loadRequested) { steps = null; GameDataSaveLoader.LoadGame("PowerStageG"); return; }
                remaining = steps.Current;
            }
            int ticks = Math.Min(remaining, 240);
            for (int i = 0; i < ticks; i++) { Find.TickManager.DoSingleTick(); sampleTick?.Invoke(); }
            remaining -= ticks;
            foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
                foreach (var need in pawn.needs.AllNeeds) need.CurLevel = need.MaxLevel;
        }
        catch (Exception e) { Fail(e); }
    }
    private Building Spawn(string name, int x, int z)
    {
        var def = Def<ThingDef>(name); var building = (Building)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
        building.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(building, map.Center + new IntVec3(x, 0, z), map); return building;
    }
    private static void On(Building building, bool on) { building.GetComp<CompFlickable>().SwitchIsOn = on; }
    private void Shafts(int xMin, int xMax, int zMin, int zMax)
    {
        for (int z = zMin; z <= zMax; z++) for (int x = xMin; x <= xMax; x++)
            if (!((map.Center + new IntVec3(x, 0, z)).GetThingList(map)).Any(t => t.TryGetComp<CompMechanicalNode>() != null)) Spawn("HD_LineShaft", x, z);
    }
    private IEnumerable<int> GeneratorChecks()
    {
        primaryEngine = Spawn("HD_IndustrialDieselEngine", -12, 0);
        smallEngine = Spawn("HD_SmallSteamEngine", -12, 6);
        generatorBuilding = Spawn("HD_EarlyIndustrialGenerator", -4, 0);
        var lathe = Spawn("HD_LineShaftTurretLathe", 2, 0);
        var lamp = Spawn("StandingLamp", 4, 4);
        Shafts(-14, 3, -2, 7);
        for (int x = -6; x <= 5; x++) for (int z = 1; z <= 4; z++)
        {
            var cell = map.Center + new IntVec3(x, 0, z);
            if (!cell.GetThingList(map).Any(t => t.def.EverTransmitsPower && t.def.building?.isPowerConduit != true)) Spawn("PowerConduit", x, z);
        }
        primaryEngine.GetComp<CompRefuelable>().Refuel(80f); smallEngine.GetComp<CompRefuelable>().Refuel(25f); On(smallEngine, false);
        map.powerNetManager.UpdatePowerNetsAndConnections_First(); yield return 1200;
        var generator = generatorBuilding.GetComp<CompMechanicalGenerator>();
        var user = generatorBuilding.GetComp<CompMechanicalUser>();
        var net = user.Network;
        Check(net != null && user.HasPower, "Actual diesel engine and shafts power the generator");
        Near(generator.TargetElectricalOutput, 300f, "New generator starts at a small-colony 300 W target");
        foreach (float target in new[] { 0f, 300f, 600f, 1200f, 1800f, 2400f, 3600f, 4800f, 6000f, 8000f })
        {
            On(lathe, target < 8000f);
            generator.TargetElectricalOutput = target; yield return 180;
            Near(generator.CurrentElectricalOutput, target, "Actual generator reaches selected electrical output");
            Near(user.GridPowerDemanded, generator.RequestedMechanicalInput, "Target controls real requested shaft load");
            Near(user.Network.CurrentPowerNeeded, generator.RequestedMechanicalInput + lathe.GetComp<CompMechanicalUser>().GridPowerDemanded, "Shared network includes the generator and lathe exactly once; latheSpawned=" + lathe.Spawned + "; latheOn=" + lathe.GetComp<CompFlickable>().SwitchIsOn + "; sameNet=" + ReferenceEquals(user.Network, lathe.GetComp<CompMechanicalUser>().Network) + "; cachedNet=" + ReferenceEquals(net, user.Network));
            Check(generator.CurrentElectricalOutput <= generator.CurrentMechanicalInput + .01f, "Electrical output never exceeds actual shaft input");
            if (target == 0f) Check(generator.CurrentMechanicalInput == 0f, "Zero output fully unloads the generator");
            if (target == 300f) Check(lamp.GetComp<CompPowerTrader>().PowerOn && generatorBuilding.GetComp<CompPowerPlant>().PowerNet == lamp.GetComp<CompPowerTrader>().PowerNet,
                "Real electrical network powers a connected standing lamp");
        }
        var command = generator.CompGetGizmosExtra().OfType<Command_Action>().Single(); command.action();
        var menu = Find.WindowStack.WindowOfType<FloatMenu>();
        var choices = (List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu);
        Check(choices.Count == 10, "Output gizmo opens the native menu with all ten targets");
        choices.Single(o => o.Label == "1800 W").action(); menu.Close(); yield return 120;
        Near(generator.CurrentElectricalOutput, 1800f, "Native menu callback changes actual electrical output");
        On(primaryEngine, false); On(smallEngine, true); generator.TargetElectricalOutput = 8000f; yield return 180;
        Check(net.GridRPM < net.NominalGridRPM && generator.CurrentElectricalOutput > 0 && generator.CurrentElectricalOutput < 8000,
            "An undersized real steam engine reduces RPM and delivered output under overload");
        Near(generator.CurrentMechanicalInput, user.GridPowerDemanded * user.PowerFulfillmentRatio, "Overload output uses only the supplied share of shaft power");
        On(smallEngine, false);
        for (int elapsed = 0; generator.CurrentElectricalOutput != 0f && elapsed < 1200; elapsed += 60) yield return 60;
        Check(generator.CurrentElectricalOutput == 0f && generator.CurrentMechanicalInput == 0f, "Stopped sources produce no mechanical input or electricity; input=" + generator.CurrentMechanicalInput + "; output=" + generator.CurrentElectricalOutput + "; user=" + user.HasPower + "; rpm=" + user.RealRPM + "; supply=" + user.Network.CurrentPowerOutput + "; sources=" + string.Join("/", user.Network.nodes.OfType<CompMechanicalEmitter>().Select(e => e.parent.def.defName + ":" + e.IsProducingPower + ":switch=" + e.parent.GetComp<CompFlickable>()?.SwitchIsOn)));
        On(primaryEngine, true); generator.TargetElectricalOutput = 1800f; yield return 180;
        stage = 1; GameDataSaveLoader.SaveGame("PowerStageG"); Write("SAVE: target 1800 W with live shaft and electrical networks");
        loadRequested = true; yield return 0;
    }
    private IEnumerable<int> AfterLoad()
    {
        var generator = generatorBuilding.GetComp<CompMechanicalGenerator>();
        for (int elapsed = 0; MathF.Abs(generator.CurrentElectricalOutput - 1800f) > .1f && elapsed < 1200; elapsed += 60) yield return 60;
        Write("LOADED STATE: output=" + generator.CurrentElectricalOutput + "; supply=" + generatorBuilding.GetComp<CompMechanicalUser>().Network?.CurrentPowerOutput + "; rpm=" + generatorBuilding.GetComp<CompMechanicalUser>().RealRPM + "; primary=" + primaryEngine.GetComp<CompMechanicalEmitter>().IsProducingPower + "; switch=" + primaryEngine.GetComp<CompFlickable>().SwitchIsOn + "; fuel=" + primaryEngine.GetComp<CompRefuelable>().Fuel);
        Near(generator.CurrentElectricalOutput, 1800f, "Loaded generator resumes its selected output on the real network");
        generator.TargetElectricalOutput = 0f; yield return 120;
        Check(generator.CurrentElectricalOutput == 0 && generatorBuilding.GetComp<CompMechanicalUser>().GridPowerDemanded == 0,
            "Selecting zero after load removes actual output and shaft load");
        generator.TargetElectricalOutput = 1800f; yield return 120;
        Near(generator.CurrentElectricalOutput, 1800f, "Selecting 1800 W again restores actual output");
        if (GenCommandLine.TryGetCommandLineArg("hdPowerBalance", out _)) foreach (int ticks in ColonyBudgets()) yield return ticks;
    }
}