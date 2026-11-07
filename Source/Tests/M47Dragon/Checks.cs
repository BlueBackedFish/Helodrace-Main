using System;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.M47Dragon;

// Actual-game check: the M47 needs an FGM-77A to fire and shows only its tracker while empty.
public sealed class Checks : GameComponent
{
    private const string FullTex = "HD_M47";
    private const string TrackerTex = "HD_M47_Tracker";
    private bool started, complete;
    private int step, deadline;
    private string output;
    private Map map;
    private Pawn gunner;
    private ThingWithComps weapon;
    private CompRecoillessWeapon comp;
    private ThingDef roundDef, projectileDef;
    private IntVec3 aim;
    private bool sawWarmupWhileFull;
    public Checks(Game game) { }
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + "\n");
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Write("PASS: " + text); }
    private static void Body(Pawn p) { if (p.story != null && p.story.bodyType == null) p.story.bodyType = p.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male; }
    private string Tex => weapon.Graphic?.MatSingle?.mainTexture?.name;

    public override void GameComponentTick()
    {
        if (complete || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdM47Dragon", out output)) return;
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
        File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 M47 Dragon / FGM-77A check\n");
        foreach (Corpse c in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()) Body(c.InnerPawn);
        foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList()) { Body(p); if (p.Faction != Faction.OfPlayer) p.Destroy(DestroyMode.Vanish); }
        foreach (IntVec3 c in CellRect.CenteredOn(map.Center, 30))
        {
            if (!c.InBounds(map)) continue;
            foreach (Thing t in c.GetThingList(map).ToList()) if (t is not Pawn && t.Spawned && t.def.destroyable) t.Destroy();
            map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); map.roofGrid.SetRoof(c, null);
            map.fogGrid.Unfog(c);
        }
        foreach (Pawn p in map.mapPawns.FreeColonistsSpawned.ToList()) p.Destroy(DestroyMode.Vanish);

        ThingDef m47 = DefDatabase<ThingDef>.GetNamed("HD_Gun_M47_Dragon");
        roundDef = DefDatabase<ThingDef>.GetNamed("HD_FGM77A_Round");
        projectileDef = DefDatabase<ThingDef>.GetNamed("HD_M222_Proj");
        Check(roundDef.projectileWhenLoaded == projectileDef, "FGM-77A loads the M222 missile");
        Check(DefDatabase<RecipeDef>.GetNamedSilentFail("HD_MakeFGM77AMissile")?.ProducedThingDef == roundDef, "FGM-77A has a crafting recipe");
        Check(roundDef.graphic?.MatSingle != null && roundDef.graphic.MatSingle.mainTexture != BaseContent.BadTex, "FGM-77A texture loads");

        // A dropped, empty M47 shows only the tracker.
        weapon = (ThingWithComps)ThingMaker.MakeThing(m47);
        comp = weapon.TryGetComp<CompRecoillessWeapon>();
        Check(comp != null && !comp.Loaded && comp.SelectedAmmoDef == roundDef, "New M47 is unloaded and takes the FGM-77A");
        GenSpawn.Spawn(weapon, map.Center + new IntVec3(3, 0, 3), map);
        Check(Tex == TrackerTex, "Empty M47 on the ground shows the tracker graphic (" + Tex + ")");
        Check(weapon.Graphic.GetType() == weapon.def.graphic.GetType(), "Tracker uses the same on-ground graphic wrapper as the full launcher (" + weapon.Graphic.GetType().Name + ")");
        weapon.DeSpawn();

        gunner = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer)); Body(gunner);
        GenSpawn.Spawn(gunner, map.Center, map);
        gunner.equipment.DestroyAllEquipment();
        gunner.inventory.innerContainer.ClearAndDestroyContents();
        gunner.equipment.AddEquipment(weapon);
        gunner.drafter.Drafted = true;
        Check(Tex == TrackerTex, "Empty M47 held by a pawn shows the tracker graphic");

        // No round: firing is refused.
        aim = map.Center + new IntVec3(0, 0, 20);
        Verb verb = weapon.GetComp<CompEquippable>().PrimaryVerb;
        Check(!verb.TryStartCastOn(new LocalTargetInfo(aim)), "Empty M47 without an FGM-77A cannot fire");
        Check(!comp.Loaded, "Still unloaded after the refused shot");

        // With a round in inventory, reloading consumes it.
        Thing round = ThingMaker.MakeThing(roundDef);
        gunner.inventory.innerContainer.TryAdd(round);
        Check(comp.HasInventoryAmmo(gunner), "Gunner carries an FGM-77A");
        Check(comp.TryStartReloadJob(gunner), "Reload job starts");
        deadline = GenTicks.TicksGame + 1500; step = 1;
        Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
    }

    private void Advance()
    {
        int tick = GenTicks.TicksGame;
        if (step == 1)
        {
            if (comp.Loaded)
            {
                Check(!comp.HasInventoryAmmo(gunner), "Reloading consumed the FGM-77A from inventory");
                Check(Tex == FullTex, "Loaded M47 shows the full launcher graphic (" + Tex + ")");
                Job attack = JobMaker.MakeJob(JobDefOf.AttackStatic, new LocalTargetInfo(aim));
                attack.endIfCantShootTargetFromCurPos = false;
                Check(gunner.jobs.TryTakeOrderedJob(attack, JobTag.Misc), "Attack order accepted");
                step = 2; deadline = tick + 1200; return;
            }
            if (tick > deadline) throw new InvalidOperationException("M47 was not reloaded; job=" + gunner.CurJobDef);
            return;
        }
        if (step == 2)
        {
            if (gunner.stances.curStance is Stance_Warmup && !comp.Loaded)
            {
                if (!sawWarmupWhileFull) Check(Tex == FullTex, "During warm-up the round is spent but the launcher still shows the missile");
                sawWarmupWhileFull = true;
            }
            if (map.listerThings.ThingsOfDef(projectileDef).Any())
            {
                Check(sawWarmupWhileFull, "Warm-up was observed before launch");
                Check(!comp.Loaded, "Firing consumed the loaded round");
                step = 3; deadline = tick + 600; return;
            }
            if (tick > deadline) throw new InvalidOperationException("M47 never launched; job=" + gunner.CurJobDef + " stance=" + gunner.stances.curStance + " loaded=" + comp.Loaded);
            return;
        }
        if (step == 3)
        {
            if (!(gunner.stances.curStance is Stance_Warmup) && !weapon.GetComp<CompEquippable>().PrimaryVerb.Bursting)
            {
                Check(Tex == TrackerTex, "After launch only the tracker is shown");
                gunner.jobs.EndCurrentJob(JobCondition.InterruptForced);
                Check(!weapon.GetComp<CompEquippable>().PrimaryVerb.TryStartCastOn(new LocalTargetInfo(aim)), "Spent M47 cannot fire again without another FGM-77A");
                Finish(); return;
            }
            if (tick > deadline) throw new InvalidOperationException("Shot never finished");
        }
    }

    private void Finish() { complete = true; Write("COMPLETE"); Waylith.EngineApplication.Instance.Quit(0); }
    private void Fail(Exception e) { complete = true; Write("FAIL: " + e); Log.Error(e.ToString()); Waylith.EngineApplication.Instance.Quit(1); }
}
