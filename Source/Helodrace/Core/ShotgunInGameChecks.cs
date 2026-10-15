#if SHOTGUN_TESTS
// Isolated -quicktest validation build only; excluded from the shipped assembly.
using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class ShotgunInGameChecks : GameComponent
    {
        private bool finished;
        private int phase;
        private int waitUntil;
        private Pawn shooter;
        private Pawn victim;
        private int shellId;
        private int recordedShots;
        private string Results => Path.Combine(GenFilePaths.SaveDataFolderPath, "shotgun-results.txt");
        public ShotgunInGameChecks(Game game) { }
        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            File.AppendAllText(Results, "PASS " + message + "\n");
        }
        public override void GameComponentTick()
        {
            if (finished || Find.CurrentMap == null || Find.TickManager.TicksGame < waitUntil) return;
            try
            {
                if (phase == 0)
                {
                    File.WriteAllText(Results, "Native shotgun integration checks\n");
                    StartShot(PawnKindDefOf.Colonist, 5);
                    phase = 1;
                }
                else if (phase == 1)
                {
                    InspectShot("human");
                    StartShot(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"), 5);
                    phase = 2;
                }
                else if (phase == 2)
                {
                    InspectShot("Helod");
                    var weapon = DefDatabase<ThingDef>.GetNamed("HD_Gun_M1897_Weapon");
                    int stats = weapon.SpecialDisplayStats(StatRequest.For(weapon, null))
                        .Count(entry => entry.LabelCap == "HD_Shotgun_SpreadAngle".Translate().Resolve().CapitalizeFirst()
                            || entry.LabelCap == "HD_Shotgun_PatternDiameter".Translate().Resolve().CapitalizeFirst());
                    Check(stats == 2, "weapon info exposes spread and pattern diameter");
                    File.AppendAllText(Results, "COMPLETE\n");
                    finished = true;
                    Application.Quit(0);
                }
            }
            catch (Exception error)
            {
                finished = true;
                File.AppendAllText(Results, "FAIL " + error + "\n");
                Application.Quit(1);
            }
        }
        private void StartShot(PawnKindDef kind, int distance)
        {
            Map map = Find.CurrentMap;
            IntVec3 source = map.Center + new IntVec3(0, 0, -12);
            IntVec3 destination = source + new IntVec3(0, 0, distance);
            foreach (IntVec3 cell in CellRect.FromLimits(source + new IntVec3(-3, 0, -3), destination + new IntVec3(3, 0, 3)))
                foreach (Thing thing in cell.GetThingList(map).ToArray())
                    if (!(thing is Pawn) && (thing.def.category == ThingCategory.Building || thing.def.category == ThingCategory.Plant)) thing.Destroy();
            if (shooter != null && !shooter.Destroyed) shooter.Destroy();
            if (victim != null && !victim.Destroyed) victim.Destroy();
            shooter = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            victim = PawnGenerator.GeneratePawn(kind);
            shooter.skills.GetSkill(SkillDefOf.Shooting).Level = 20;
            victim.apparel?.DestroyAll();
            victim.equipment?.DestroyAllEquipment();
            GenSpawn.Spawn(shooter, source, map);
            GenSpawn.Spawn(victim, destination, map);
            foreach (Pawn pawn in new[] { shooter, victim })
            {
                Job wait = JobMaker.MakeJob(JobDefOf.Wait);
                wait.expiryInterval = 99999;
                pawn.jobs.StartJob(wait, JobCondition.InterruptForced);
            }
            ThingWithComps weapon = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Gun_M1897_Weapon"));
            shooter.equipment.DestroyAllEquipment();
            shooter.equipment.AddEquipment(weapon);
            Verb_ShootShotgun verb = (Verb_ShootShotgun)weapon.TryGetComp<CompEquippable>().PrimaryVerb;
            verb.caster = shooter;
            // Isolate trajectory/damage integration from ordinary aim failures.
            verb.verbProps.canGoWild = false;
            AccessTools.FieldRefAccess<Verb, LocalTargetInfo>("currentTarget")(verb) = victim;
            recordedShots = (int)shooter.records.GetValue(RecordDefOf.ShotsFired);
            Check((bool)AccessTools.Method(typeof(Verb_ShootShotgun), "TryCastShot").Invoke(verb, null), kind.defName + " shell fired");
            var launched = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile_ShotgunPellet>()
                .Where(pellet => pellet.Launcher == shooter).ToArray();
            Check(launched.Length == 9 && launched.Select(pellet => pellet.Shell).Distinct().Count() == 1,
                kind.defName + " has nine native projectiles sharing one shell");
            shellId = launched[0].Shell.id;
            Check((int)shooter.records.GetValue(RecordDefOf.ShotsFired) - recordedShots == 1, "native shot record increments once");
            Check(ShotgunBodyProfile.For(victim.RaceProps.body).surfaces.Count > 0, kind.defName + " body automatically has an external profile");
            waitUntil = Find.TickManager.TicksGame + 120;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        }
        private void InspectShot(string label)
        {
            var wounds = victim.health.hediffSet.hediffs.OfType<Hediff_Injury>()
                .Where(injury => GameComponent_ShotgunWounds.Current.For(injury)?.shellId == shellId).ToArray();
            Check(wounds.Length > 0, label + " received native pellet injuries");
            Check(wounds.Sum(injury => injury.Severity) <= 36.01f, label + " surface and internal injuries respect shell damage budget");
            Check(wounds.All(injury => GameComponent_ShotgunWounds.Current.For(injury).pelletCount > 0), label + " wounds retain pellet counts");
            Check(wounds.Where(injury => !injury.IsPermanent() && !injury.IsTended()).GroupBy(injury => new { injury.Part, injury.def })
                .All(group => group.Count() == 1), label + " same-shell same-part injuries are grouped");
            Check(wounds.All(injury => injury.LabelBase.Contains("HD_Shotgun_PelletCount".Translate(
                GameComponent_ShotgunWounds.Current.For(injury).pelletCount).Resolve())), label + " wound labels show pellet counts");
        }
    }
}
#endif
