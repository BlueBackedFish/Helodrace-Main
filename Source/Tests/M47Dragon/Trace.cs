using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.M47Dragon;

// Flight trace: fires the M47 at still and moving targets, logs the missile, and fails when a
// guided missile detonates away from its target (the launch-time flight timer regression).
public sealed class Trace : GameComponent
{
    private static readonly AccessTools.FieldRef<Projectile, int> TicksToImpact = AccessTools.FieldRefAccess<Projectile, int>("ticksToImpact");
    private static readonly AccessTools.FieldRef<Projectile, UnityEngine.Vector3> Origin = AccessTools.FieldRefAccess<Projectile, UnityEngine.Vector3>("origin");
    private static readonly AccessTools.FieldRef<Projectile, UnityEngine.Vector3> Destination = AccessTools.FieldRefAccess<Projectile, UnityEngine.Vector3>("destination");
    private bool started, complete;
    private int step, deadline, shot;
    private string output;
    private Map map;
    private Pawn gunner, enemy;
    private ThingWithComps weapon;
    private CompRecoillessWeapon comp;
    private ThingDef roundDef, projectileDef;
    private Projectile missile;
    private UnityEngine.Vector3 lastPos, lastDest;
    private int lastTicks;
    private readonly System.Collections.Generic.List<string> misses = new System.Collections.Generic.List<string>();
    private Faction hostile;
    private LocalTargetInfo target;
    private readonly System.Collections.Generic.List<Thing> scenery = new System.Collections.Generic.List<Thing>();
    private static readonly string[] Scenarios = { "still30", "crossing30", "fleeing25", "approaching40", "sandbagTarget", "long44", "autoFire" };
    private int flightTicks;
    public Trace(Game game) { }
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "trace.txt"), text + "\n");
    private static void Body(Pawn p) { if (p.story != null && p.story.bodyType == null) p.story.bodyType = p.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male; }
    private static string V(UnityEngine.Vector3 v) => $"({v.x:F2},{v.z:F2})";

    public override void GameComponentTick()
    {
        if (complete || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdM47Trace", out output)) return;
        try
        {
            if (!started) { started = true; Setup(); return; }
            Advance();
        }
        catch (Exception e) { complete = true; Write("FAIL: " + e); Log.Error(e.ToString()); Waylith.EngineApplication.Instance.Quit(1); }
    }

    private void Setup()
    {
        map = Find.CurrentMap; DebugSettings.enableStoryteller = false;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "trace.txt"), "M47 flight trace\n");
        foreach (Corpse c in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()) Body(c.InnerPawn);
        foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList()) { Body(p); p.Destroy(DestroyMode.Vanish); }
        foreach (IntVec3 c in CellRect.CenteredOn(map.Center, 45))
        {
            if (!c.InBounds(map)) continue;
            foreach (Thing t in c.GetThingList(map).ToList()) if (t is not Pawn && t.Spawned && t.def.destroyable) t.Destroy(DestroyMode.Vanish);
            map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); map.roofGrid.SetRoof(c, null);
            map.fogGrid.Unfog(c);
        }
        roundDef = DefDatabase<ThingDef>.GetNamed("HD_FGM77A_Round");
        projectileDef = DefDatabase<ThingDef>.GetNamed("HD_M222_Proj");
        weapon = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Gun_M47_Dragon"));
        comp = weapon.TryGetComp<CompRecoillessWeapon>();
        gunner = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer)); Body(gunner);
        GenSpawn.Spawn(gunner, map.Center, map);
        gunner.equipment.DestroyAllEquipment();
        gunner.inventory.innerContainer.ClearAndDestroyContents();
        gunner.equipment.AddEquipment(weapon);
        gunner.skills.GetSkill(SkillDefOf.Shooting).Level = 10;
        gunner.drafter.Drafted = true;

        hostile = Find.FactionManager.RandomEnemyFaction(false, false, true);
        Write($"gunner={gunner.Position} accuracy={gunner.GetStatValue(StatDefOf.ShootingAccuracyPawn):F2}");
        NextShot();
        Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
    }

    private Pawn Enemy(IntVec3 cell, bool stun)
    {
        Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, hostile)); Body(p);
        GenSpawn.Spawn(p, cell, map); p.equipment?.DestroyAllEquipment(); scenery.Add(p);
        if (stun) p.stances.stunner.StunFor(6000, null, false, false);
        return p;
    }

    private void Build(ThingDef def, IntVec3 cell)
    {
        Thing t = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null); t.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(t, cell, map); scenery.Add(t);
    }

    private void Walk(Pawn p, IntVec3 to)
    {
        Job go = JobMaker.MakeJob(JobDefOf.Goto, to); go.locomotionUrgency = LocomotionUrgency.Sprint; go.expiryInterval = -1;
        p.jobs.StartJob(go, JobCondition.InterruptForced);
    }

    private void NextShot()
    {
        foreach (Thing t in scenery) if (!t.Destroyed) t.Destroy(DestroyMode.Vanish);
        foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).ToList()) t.Destroy();
        scenery.Clear();
        foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList()) if (p != gunner) p.Destroy(DestroyMode.Vanish);
        if (shot >= Scenarios.Length) { complete = true; if (misses.Count > 0) { Write("FAIL: detonated away from the target: " + string.Join(", ", misses)); Waylith.EngineApplication.Instance.Quit(1); return; } Write("COMPLETE"); Waylith.EngineApplication.Instance.Quit(0); return; }
        IntVec3 c0 = map.Center;
        if (gunner.health.hediffSet.hediffs.Any(h => h is Hediff_Injury))
        {
            Write("  gunner hurt: " + string.Join(", ", gunner.health.hediffSet.hediffs.OfType<Hediff_Injury>().Select(h => h.LabelCap + "/" + h.Part?.def.defName)));
            foreach (Hediff h in gunner.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList()) gunner.health.RemoveHediff(h);
        }
        gunner.jobs.EndCurrentJob(JobCondition.InterruptForced);
        gunner.Position = c0; gunner.Notify_Teleported();
        gunner.drafter.Drafted = true;
        comp.Load();
        string name = Scenarios[shot];
        enemy = null;
        switch (name)
        {
            case "still30": enemy = Enemy(c0 + new IntVec3(0, 0, 30), true); break;
            case "crossing30": enemy = Enemy(c0 + new IntVec3(-15, 0, 30), false); Walk(enemy, c0 + new IntVec3(25, 0, 30)); break;
            case "fleeing25": enemy = Enemy(c0 + new IntVec3(0, 0, 25), false); Walk(enemy, c0 + new IntVec3(0, 0, 44)); break;
            case "approaching40": enemy = Enemy(c0 + new IntVec3(0, 0, 40), false); Walk(enemy, c0 + new IntVec3(0, 0, 8)); break;
            case "sandbagTarget":
                foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(new TargetInfo(c0 + new IntVec3(0, 0, 30), map))) if (c.z < c0.z + 30) Build(ThingDefOf.Sandbags, c);
                enemy = Enemy(c0 + new IntVec3(0, 0, 30), true); break;
            case "long44": enemy = Enemy(c0 + new IntVec3(0, 0, 44), true); break;
            case "autoFire": enemy = Enemy(c0 + new IntVec3(10, 0, 30), false); Walk(enemy, c0 + new IntVec3(-10, 0, 35)); break;
        }
        target = new LocalTargetInfo(enemy);
        Write($"--- shot {shot} {name} enemy={enemy.Position} dist={enemy.Position.DistanceTo(gunner.Position):F1}");
        if (name != "autoFire")
        {
            Job attack = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            attack.endIfCantShootTargetFromCurPos = false;
            gunner.jobs.TryTakeOrderedJob(attack, JobTag.Misc);
        }
        missile = null; flightTicks = 0; step = 1; deadline = GenTicks.TicksGame + 1200;
    }

    private void Advance()
    {
        if (step == 1)
        {
            missile = map.listerThings.ThingsOfDef(projectileDef).OfType<Projectile>().FirstOrDefault();
            if (missile != null)
            {
                Write($"launch origin={V(Origin(missile))} dest={V(Destination(missile))} ticks={TicksToImpact(missile)}");
                step = 2;
            }
            else if (GenTicks.TicksGame > deadline) { Write($"  no launch; job={gunner.CurJobDef} stance={gunner.stances.curStance} loaded={comp.Loaded} downed={gunner.Downed} los={GenSight.LineOfSight(gunner.Position, target.Cell, map)}"); shot++; step = 3; deadline = GenTicks.TicksGame + 10; }
            return;
        }
        if (step == 2)
        {
            if (missile.Destroyed)
            {
                // Projectiles advance in batches at high speed, so the last seen position can lag;
                // when the remaining flight was short the missile ended at its destination.
                UnityEngine.Vector3 end = lastTicks <= 20 ? lastDest : lastPos;
                float miss = (end - enemy.DrawPos).MagnitudeHorizontal();
                Write($"END after {flightTicks} ticks near {V(end)}: from gunner {(end - gunner.DrawPos).MagnitudeHorizontal():F1}, from target {miss:F1} (target now {enemy.Position})");
                if (miss > 2f) Write("  at end cell: " + string.Join(",", end.ToIntVec3().GetThingList(map).Select(t => t.def.defName)) + " roof=" + end.ToIntVec3().GetRoof(map));
                if (miss > 2f) misses.Add(Scenarios[shot] + " " + miss.ToString("F1"));
                gunner.jobs.EndCurrentJob(JobCondition.InterruptForced);
                shot++; step = 3; deadline = GenTicks.TicksGame + 200;
                return;
            }
            lastPos = missile.ExactPosition; lastDest = Destination(missile); lastTicks = TicksToImpact(missile);
            if (flightTicks < 30 || flightTicks % 5 == 0)
                Write($"  t={flightTicks} pos={V(lastPos)} dest={V(Destination(missile))} ticks={TicksToImpact(missile)} rot={missile.ExactRotation.eulerAngles.y:F0} enemy={V(enemy.DrawPos)}");
            flightTicks++;
            return;
        }
        if (step == 3 && GenTicks.TicksGame > deadline) NextShot();
    }
}
