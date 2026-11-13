using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Helodrace;
using Helodrace.Squads;
using Helodrace.SquadAI;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace.Tests.SquadAI;

// Protected targets absorb every hit so the squads keep a live contact.
[HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
public static class ProtectTargets
{
    public static int Hits;
    public static bool Prefix(Pawn __instance, ref bool absorbed)
    {
        if (__instance.Faction != Faction.OfPlayer) return true;
        Hits++; absorbed = true; return false;
    }
}

// Actual-game audit of the squad AI (field stage) on RimWorld 1.7.
public sealed class Checks : GameComponent
{
    private bool started, complete, load;
    private int phase, step, deadline, movers, lastMoverTeam = -1;
    private string output;
    private Map map;
    private Pawn target;
    public Checks(Game game) { }

    public override void ExposeData() => Scribe_Values.Look(ref phase, "squadAIChecksPhase");
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + "\n");
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Write("PASS: " + text); }
    private static void Body(Pawn p) { if (p.story != null && p.story.bodyType == null) p.story.bodyType = p.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male; }
    private MapComponent_SquadAI Component => MapComponent_SquadAI.For(map);

    public override void GameComponentTick()
    {
        if (complete || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdSquadAIChecks", out output)) return;
        try
        {
            if (!started)
            {
                started = true; map = Find.CurrentMap;
                bool vanillaLoad = GenCommandLine.CommandLineArgPassed("hdSquadAIVanillaLoad");
                if (vanillaLoad && phase == 0) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "checks.txt"), "Squad AI save loaded with the vanilla engine\n"); load = true; complete = true; }
                else if (vanillaLoad) VanillaLoad();
                else if (phase == 0) Setup(); else AfterLoad();
                return;
            }
            Advance();
        }
        catch (Exception e) { Fail(e); }
    }

    public override void GameComponentUpdate()
    {
        if (!load) return;
        load = false; GameDataSaveLoader.LoadGame("HelodSquadAIChecks");
    }

    private void Setup()
    {
        DebugSettings.enableStoryteller = false;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 squad AI audit\n");
        new Harmony("helodrace.tests.squadai").PatchAll(typeof(Checks).Assembly);
        foreach (Corpse c in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()) Body(c.InnerPawn);
        foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList()) { Body(p); p.Destroy(DestroyMode.Vanish); }
        foreach (IntVec3 c in CellRect.CenteredOn(map.Center, 48).ClipInsideMap(map))
        {
            foreach (Thing t in c.GetThingList(map).ToList()) if (t.Spawned && t.def.destroyable) t.Destroy();
            map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); map.roofGrid.SetRoof(c, null); map.fogGrid.Unfog(c);
        }
        // Scattered sandbags give the post search real cover to prefer.
        var rng = new System.Random(7);
        for (int i = 0; i < 70; i++)
        {
            IntVec3 c = map.Center + new IntVec3(rng.Next(-44, 20), 0, rng.Next(-30, 31));
            if (c.InBounds(map) && c.Standable(map)) GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Sandbags, ThingDefOf.Steel), c, map);
        }
        Check(TacticalEngineSelection.Kind == TacticalEngineKind.Squad, "Command line selects the squad AI engine");
        Check(Component != null && map.GetComponent<MapComponent_TacticalCommands>() == null
            && Current.Game.GetComponent<GameComponent_TacticalCommands>() == null && map.GetComponent<MapComponent_BasicRaidAI>() != null,
            "Squad AI and basic raid components are installed; legacy components are absent");
        SpawnSquad("HD_HelodCivilHighFaction", 2400, map.Center + new IntVec3(-38, 0, 0));
        Squad squad = Component.Squads.Single();
        Check(squad.Radio && squad.Unit.Group.children.Count == 3 && squad.Stage == SquadStage.Vanilla,
            $"HIGH squad registers on lord creation with 3 fireteams and starts vanilla (live={squad.Unit.Members.Count()})");
        step = 1; deadline = GenTicks.TicksGame + 120;
        Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
    }

    private void SpawnSquad(string factionDef, float points, IntVec3 at)
    {
        FactionDef def = DefDatabase<FactionDef>.GetNamed(factionDef);
        Faction faction = Find.FactionManager.FirstFactionOfDef(def);
        if (faction == null) { faction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def)); Find.FactionManager.Add(faction); }
        faction.TryAffectGoodwillWith(Faction.OfPlayer, -200, false, false);
        faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
        var parms = new PawnGroupMakerParms { groupKind = PawnGroupKindDefOf.Combat, faction = faction, points = points,
            raidStrategy = RaidStrategyDefOf.ImmediateAttack, tile = map.Tile };
        var pawns = PawnGroupMakerUtility.GeneratePawns(parms).ToList();
        RaidTacticalUnit unit = RaidTacticalUnit.ForPawn(pawns[0]);
        Check(unit != null, factionDef + " generates organized squads");
        var keep = unit.Members.ToList();
        foreach (Pawn p in pawns.Where(p => !keep.Contains(p))) p.Destroy(DestroyMode.Vanish);
        foreach (Pawn p in keep) { Body(p); GenSpawn.Spawn(p, CellFinder.StandableCellNear(at, map, 5), map); }
        LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, false, false, false, false, false), map, keep);
    }

    private Pawn SpawnTarget(IntVec3 at)
    {
        Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, mustBeCapableOfViolence: true)); Body(pawn);
        pawn.equipment.DestroyAllEquipment();
        pawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("Gun_AssaultRifle")));
        GenSpawn.Spawn(pawn, CellFinder.StandableCellNear(at, map, 3), map);
        pawn.drafter.Drafted = true; pawn.drafter.FireAtWill = false;
        return pawn;
    }

    private static double Distance(IntVec3 a, IntVec3 b) => Math.Sqrt(a.DistanceToSquared(b));
    private string Describe(Squad s) => $"stage={s.Stage} range={s.Range:0.0} focus={s.Focus} live={s.Live.Count} contacts={s.Contacts.Entries.Count} teams=" +
        string.Join(",", s.Teams.Select(t => $"[{(t.Flank ? "F" : "")}{t.Task}:{t.Members.Count}]"));

    private void Advance()
    {
        int tick = GenTicks.TicksGame;
        // Steps 1-6 follow the HIGH squad, later steps the LOW squad.
        Squad squad = Component.Squads.FirstOrDefault(s => s.Radio == step <= 6 && s.Live.Count > 0);
        if (tick % 300 == 0 && squad != null) Write($"  t={tick} step={step} {Describe(squad)} hits={ProtectTargets.Hits}");
        if (tick > deadline) throw new InvalidOperationException($"Step {step} timed out: " + (squad == null ? "no squad" : Describe(squad)));
        switch (step)
        {
            case 1:
                if (tick < deadline - 60) return;
                // Stationary enemy well beyond effective range: radio squads bound.
                IntVec3 c = squad.Centroid();
                target = SpawnTarget(c + new IntVec3((int)Math.Min(squad.Range * 1.7f, 62f), 0, 0));
                Write($"  HIGH range {squad.Range:0.0}; target at {Distance(c, target.Position):0.0}");
                step = 2; deadline = tick + 600; return;
            case 2:
                if (squad.Stage != SquadStage.Advance) return;
                int moving = squad.Teams.Count(t => t.Task == TeamTask.Move);
                Check(moving == 1 && squad.Teams.Count(t => t.Task == TeamTask.Hold) >= 2,
                    "Out-of-range stationary contact starts bounding: one fireteam moves, the others hold");
                CheckPosts(squad);
                Check(squad.Live.All(p => SquadDuties.Owned(p.mindState.duty?.def)), "Every squad member runs a squad duty");
                step = 3; deadline = tick + 5000; return;
            case 3:
            {
                if (squad.Stage == SquadStage.Brace)
                    throw new InvalidOperationException("Our own advance was mistaken for an approaching enemy: " + Describe(squad));
                SquadTeam mover = squad.Teams.FirstOrDefault(t => t.Task == TeamTask.Move);
                int index = mover == null ? -1 : squad.Teams.IndexOf(mover);
                if (index >= 0 && index != lastMoverTeam) { movers++; lastMoverTeam = index; }
                if (squad.Stage == SquadStage.Engage)
                {
                    Check(movers >= 2, $"Bounding rotated the moving fireteam ({movers} distinct bounds) before reaching range");
                    step = 4; deadline = tick + 300;
                }
                return;
            }
            case 4:
            {
                Check(Distance(squad.Centroid(), target.Position) <= squad.Range * SquadPolicy.EngageHoldFactor + 2, "Bounding closed to effective range");
                SquadTeam flank = squad.Teams.FirstOrDefault(t => t.Flank);
                Check(flank != null && flank.Group != null, "Engaging HIGH squad sends a real fireteam to flank");
                CheckPosts(squad);
                step = 5; deadline = tick + 3000; return;
            }
            case 5:
            {
                SquadTeam flank = squad.Teams.FirstOrDefault(t => t.Flank);
                if (flank == null || flank.Task != TeamTask.Hold) return;
                IntVec3 focus = squad.Focus;
                IntVec3 baseCenter = squad.Centroid(squad.Teams.Where(t => !t.Flank).SelectMany(t => t.Members));
                IntVec3 flankCenter = squad.Centroid(flank.Members);
                double angle = Math.Abs(Angle(baseCenter - focus) - Angle(flankCenter - focus));
                if (angle > 180) angle = 360 - angle;
                Write($"  flank angle {angle:0} base={baseCenter} flank={flankCenter} focus={focus}");
                Check(angle >= 35, "Flank team reached a firing position at a different angle from the base of fire");
                int held = squad.Teams.Where(t => t.Task == TeamTask.Hold).SelectMany(t => t.Posts)
                    .Count(pair => pair.Key.Position.InHorDistOf(pair.Value, SquadPolicy.HoldRadius + 1));
                Check(held >= squad.Live.Count * 0.6, $"Most members stand at their posts ({held}/{squad.Live.Count})");
                Check(ProtectTargets.Hits > 0, $"The squad actually fired on the target ({ProtectTargets.Hits} hits)");
                target.Destroy(DestroyMode.Vanish);
                step = 6; deadline = tick + 900; return;
            }
            case 6:
                if (squad.Stage != SquadStage.Vanilla) return;
                Check(squad.Live.All(p => p.mindState.duty?.def == DutyDefOf.AssaultColony), "With the contact gone the squad returns to vanilla assault duties");
                // Being shot from out of sight still registers the shooter.
                Pawn sniper = SpawnTarget(squad.Centroid() + new IntVec3(0, 0, 40));
                squad.Live[0].TakeDamage(new DamageInfo(DamageDefOf.Bullet, 1, instigator: sniper));
                Check(squad.Contacts.Entries.Any(e => e.Target == sniper), "A hit registers its shooter as a squad contact");
                sniper.Destroy(DestroyMode.Vanish);
                foreach (Pawn p in squad.Unit.Members.ToList()) if (!p.Destroyed) p.Destroy(DestroyMode.Vanish);
                SpawnSquad("HD_HelodCivilLowFaction", 1500, map.Center + new IntVec3(-38, 0, 0));
                step = 7; deadline = tick + 300; return;
            case 7:
            {
                Squad low = Component.Squads.FirstOrDefault(s => !s.Radio && s.Live.Count > 0);
                if (low == null || low.Live.Count < 6) return;
                Check(low.Stage == SquadStage.Vanilla, $"LOW squad registers and starts vanilla (live={low.Live.Count})");
                // An enemy walking in from beyond range: take cover first.
                target = SpawnTarget(low.Centroid() + new IntVec3((int)Math.Min(low.Range * 1.9f, 64f), 0, 0));
                Job walk = JobMaker.MakeJob(JobDefOf.Goto, low.Centroid()); walk.locomotionUrgency = LocomotionUrgency.Walk;
                target.jobs.TryTakeOrderedJob(walk);
                Write($"  LOW range {low.Range:0.0}; target at {Distance(low.Centroid(), target.Position):0.0}");
                step = 8; deadline = tick + 1500; return;
            }
            case 8:
            {
                if (squad.Stage == SquadStage.Engage) throw new InvalidOperationException("LOW engaged before bracing: " + Describe(squad));
                if (squad.Stage != SquadStage.Brace) return;
                Check(true, "Approaching enemy beyond range makes the LOW squad brace in cover");
                CheckPosts(squad);
                target.jobs.StopAll(); target.drafter.Drafted = true;
                target.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, squad.Centroid() + new IntVec3((int)(squad.Range * .8f), 0, 0)));
                step = 9; deadline = tick + 2000; return;
            }
            case 9:
            {
                if (squad.Stage != SquadStage.Engage) return;
                SquadTeam flank = squad.Teams.FirstOrDefault(t => t.Flank);
                Check(flank != null && flank.Group == null && flank.Members.Count >= 3 && flank.Members.Count <= 4,
                    $"Engaging LOW squad splits a 3-4 man temporary flank team ({Describe(squad)})");
                Check(!flank.Members.Any(p => { string f = OrganizationAPI.GetGroup(p)?.roleAssignments.FirstOrDefault(a => a.pawn == p)?.combatRole?.combatFunction; return f == "HAR" || f == "MachineGun"; }),
                    "Automatic weapons stay with the LOW base of fire");
                CheckPosts(squad);
                phase = 1; GameDataSaveLoader.SaveGame("HelodSquadAIChecks");
                Write("  saved while engaged; loading");
                complete = true; load = true; return;
            }
            case 10:
                if (squad == null || squad.Stage != SquadStage.Engage) return;
                Check(squad.Live.All(p => SquadDuties.Owned(p.mindState.duty?.def)), "Reloaded squad members run squad duties again");
                Check(true, "After loading, the squad reregisters and resumes engaging from what it sees");
                Finish(); return;
        }
    }

    private void AfterLoad()
    {
        Write("PASS: Saved game with squad duties loads");
        target = map.mapPawns.PawnsInFaction(Faction.OfPlayer).FirstOrDefault();
        Check(target != null, "Protected target survives the load");
        // Harmony patches from before the load stay installed in this process.
        step = 10; deadline = GenTicks.TicksGame + 1200;
        Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
    }

    // A save made while squads were fighting, loaded with the squad AI off.
    private void VanillaLoad()
    {
        Check(TacticalEngineSelection.Kind == TacticalEngineKind.Vanilla && Component == null, "Vanilla engine loads the save without the squad AI component");
        var raiders = map.mapPawns.AllPawnsSpawned.Where(p => p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)).ToList();
        Check(raiders.Count > 0, $"Raiders survive the load ({raiders.Count})");
        Check(!raiders.Any(p => SquadDuties.Owned(p.mindState.duty?.def)), "No raider keeps a squad duty under the vanilla engine");
        Check(raiders.Where(p => p.GetLord() != null).All(p => p.mindState.duty?.def == DutyDefOf.AssaultColony), "Raiders return to the lord's vanilla assault duty");
        Finish();
    }

    private static double Angle(IntVec3 v) => Math.Atan2(v.z, v.x) * 180 / Math.PI;

    private void CheckPosts(Squad squad)
    {
        var posts = squad.Teams.SelectMany(t => t.Posts.Values).ToList();
        Check(posts.Count == squad.Live.Count, $"Every live member has a post ({posts.Count}/{squad.Live.Count})");
        int close = 0;
        for (int i = 0; i < posts.Count; i++)
            for (int j = i + 1; j < posts.Count; j++)
                if (posts[i].DistanceToSquared(posts[j]) < 4) close++;
        Check(close == 0, "No two posts are closer than two cells");
        int covered = posts.Count(p => CoverUtility.CalculateOverallBlockChance(p, squad.Focus, map) > .01f);
        Write($"  posts covered from focus: {covered}/{posts.Count}");
    }

    private void Finish() { complete = true; Write("COMPLETE"); Waylith.EngineApplication.Instance.Quit(0); }
    private void Fail(Exception e) { complete = true; Write("FAIL: " + e); Log.Error(e.ToString()); Waylith.EngineApplication.Instance.Quit(1); }
}
