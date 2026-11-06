using System;
using System.IO;
using System.Linq;
using Helodrace.ModernWar;
using Helodrace.Squads;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.BasicRaidAI;

// Actual-game audit of the default (tactical AI off) raid behaviour.
public sealed class Checks : GameComponent
{
    private bool started, complete;
    private int step, deadline;
    private string output;
    private Map map;
    private Faction enemy;
    private Pawn breacher, grenadier, target;
    private Building_Door door;
    public Checks(Game game) { }
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + "\n");
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Write("PASS: " + text); }
    private static void Body(Pawn p) { if (p.story != null && p.story.bodyType == null) p.story.bodyType = p.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male; }

    public override void GameComponentTick()
    {
        if (complete || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdBasicRaidAI", out output)) return;
        try
        {
            if (!started) { started = true; Setup(); return; }
            Advance();
        }
        catch (Exception e) { Fail(e); }
    }

    private void Setup()
    {
        map = Find.CurrentMap; DebugSettings.enableStoryteller = false;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 basic raid AI audit\n");
        foreach (Corpse c in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()) Body(c.InnerPawn);
        foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList()) { Body(p); if (p.Faction != Faction.OfPlayer) p.Destroy(DestroyMode.Vanish); }
        foreach (IntVec3 c in CellRect.CenteredOn(map.Center, 22))
        {
            foreach (Thing t in c.GetThingList(map).ToList()) if (t is not Pawn && t.Spawned && t.def.destroyable) t.Destroy();
            map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); map.roofGrid.SetRoof(c, null);
            map.fogGrid.Unfog(c);
        }
        foreach (Pawn p in map.mapPawns.FreeColonistsSpawned.ToList()) p.Destroy(DestroyMode.Vanish);

        Check(TacticalEngineSelection.Kind == TacticalEngineKind.Vanilla && !RaidSettings.TacticalAI,
            "Tactical AI setting defaults off and selects the vanilla engine");
        Check(map.GetComponent<MapComponent_TacticalCommands>() == null && map.GetComponent<MapComponent_BasicRaidAI>() != null
            && Current.Game.GetComponent<GameComponent_TacticalCommands>() == null,
            "Squad tactical components are absent; the basic raid AI component is installed");

        FactionDef def = DefDatabase<FactionDef>.GetNamed("HD_HelodCivilHighFaction");
        enemy = Find.FactionManager.FirstFactionOfDef(def);
        if (enemy == null) { enemy = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def)); Find.FactionManager.Add(enemy); }
        enemy.TryAffectGoodwillWith(Faction.OfPlayer, -200, false, false);
        enemy.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
        Check(enemy.HostileTo(Faction.OfPlayer), "Test Helod faction is hostile");
        var parms = new PawnGroupMakerParms { groupKind = PawnGroupKindDefOf.Combat, faction = enemy, points = 2500,
            raidStrategy = RaidStrategyDefOf.ImmediateAttack, tile = map.Tile };
        var pawns = PawnGroupMakerUtility.GeneratePawns(parms).ToList();
        Check(pawns.Count > 0 && OrganizationAPI.GetGroup(pawns[0]) != null, "Helod raids still generate as organized squads");
        bool HandGrenade(Thing t) => t.def.defName.StartsWith("HD_Grenade_") || t.def.defName == "HD_C4_Charge" || t.def.defName == "HD_M81Igniter";
        Check(!pawns.Any(p => p.inventory.innerContainer.Any(HandGrenade)), "Raiders carry no hand grenades, smoke grenades or charges");
        grenadier = pawns.FirstOrDefault(p => ModularUnderbarrelLauncherUtility.Installed(p.equipment?.Primary) != null);
        breacher = pawns.FirstOrDefault(p => CompSledgehammerBreach.WornBy(p) != null);
        Check(grenadier != null && grenadier.inventory.innerContainer.Any(t => t.def.defName == "HD_40mmM381HE_Round"),
            "Grenadiers keep the underbarrel launcher and 40mm rounds");
        Check(breacher != null, "Engineers keep the sledgehammer");
        foreach (Pawn p in pawns.Where(p => p != grenadier && p != breacher)) p.Destroy(DestroyMode.Vanish);

        // Closed player room: the only route to its centre is the door.
        IntVec3 c0 = map.Center;
        foreach (IntVec3 c in CellRect.CenteredOn(c0, 2).EdgeCells)
        {
            Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel); wall.SetFaction(Faction.OfPlayer);
            if (c == c0 + new IntVec3(0, 0, -2))
            { door = (Building_Door)ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.Steel); door.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(door, c, map); }
            else GenSpawn.Spawn(wall, c, map);
        }
        Body(breacher); GenSpawn.Spawn(breacher, c0 + new IntVec3(0, 0, -6), map);
        Job go = JobMaker.MakeJob(JobDefOf.Goto, c0); go.canBashDoors = true; go.expiryInterval = -1;
        breacher.jobs.StartJob(go, JobCondition.InterruptForced);
        deadline = GenTicks.TicksGame + 1500; step = 1;
        Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
    }

    private void Advance()
    {
        int tick = GenTicks.TicksGame;
        if (step == 1)
        {
            if (breacher.CurJobDef?.defName == CompSledgehammerBreach.JobDefName) { Write("PASS: Blocked raider switched the door bash to a sledgehammer breach"); step = 2; deadline = tick + 1200; return; }
            if (tick % 250 == 0) Write($"  t={tick} pos={breacher.Position} door={door.Position} job={breacher.CurJobDef} moving={breacher.pather.Moving} dest={breacher.pather.Destination} valid={CompSledgehammerBreach.IsValidTarget(breacher, door)} hostile={breacher.HostileTo(door)} canOpen={door.PawnCanOpen(breacher)}");
            if (tick > deadline)
            {
                Job bash = JobMaker.MakeJob(JobDefOf.AttackMelee, door);
                breacher.jobs.StartJob(bash, JobCondition.InterruptForced);
                throw new InvalidOperationException("Raider never started a sledgehammer breach; job=" + breacher.CurJobDef + " direct=" + breacher.CurJobDef);
            }
            return;
        }
        if (step == 2)
        {
            if (door.GetComp<CompDoorBreachFault>()?.Jammed == true) { Write("PASS: Sledgehammer breach jammed the player door"); breacher.Destroy(); SetupGrenadier(); return; }
            if (tick > deadline) throw new InvalidOperationException("Door was not jammed; job=" + breacher.CurJobDef);
            return;
        }
        if (step == 3)
        {
            var launcher = ModularUnderbarrelLauncherUtility.Installed(grenadier.equipment.Primary);
            if (launcher.Loaded)
            {
                Write("PASS: Raider reloaded the empty underbarrel launcher from inventory");
                Verb verb = grenadier.TryGetAttackVerb(target);
                Check(verb is Verb_ShootModularUnderbarrel, "Raider selects the loaded underbarrel launcher against an isolated target");
                // A friendly standing beside the target vetoes the grenade.
                Pawn ally = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, enemy)); Body(ally);
                GenSpawn.Spawn(ally, target.Position + IntVec3.East, map);
                ResetCheck();
                Check(!(grenadier.TryGetAttackVerb(target) is Verb_ShootModularUnderbarrel), "Allies near the target veto the underbarrel grenade");
                Finish(); return;
            }
            if (tick > deadline) throw new InvalidOperationException("Underbarrel was not reloaded; job=" + grenadier.CurJobDef);
        }
    }

    private void ResetCheck()
    {
        var field = typeof(Helodrace.Tactics.BasicRaidAI).GetField("nextUnderbarrelCheck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        ((System.Collections.IDictionary)field.GetValue(null)).Clear();
    }

    private void SetupGrenadier()
    {
        IntVec3 c0 = map.Center + new IntVec3(-14, 0, 0);
        Body(grenadier); GenSpawn.Spawn(grenadier, c0, map);
        var launcher = ModularUnderbarrelLauncherUtility.Installed(grenadier.equipment.Primary);
        if (launcher.Loaded) launcher.NotifyFired();
        target = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer)); Body(target);
        GenSpawn.Spawn(target, c0 + new IntVec3(0, 0, 14), map); target.drafter.Drafted = true;
        grenadier.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 4000), JobCondition.InterruptForced);
        ResetCheck();
        step = 3; deadline = GenTicks.TicksGame + 900;
    }

    private void Finish() { complete = true; Write("COMPLETE"); Waylith.EngineApplication.Instance.Quit(0); }
    private void Fail(Exception e) { complete = true; Write("FAIL: " + e); Log.Error(e.ToString()); Waylith.EngineApplication.Instance.Quit(1); }
}
