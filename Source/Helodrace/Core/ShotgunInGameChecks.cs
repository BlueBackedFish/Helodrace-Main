#if SHOTGUN_TESTS
// Isolated -quicktest validation build only; excluded from the shipped assembly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed partial class ShotgunInGameChecks : GameComponent
    {
        private bool finished;
        private int phase;
        private int waitUntil;
        private Pawn shooter;
        private Pawn victim;
        private int shellId;
        private int recordedShots;
        private int expectedHits;
        private float expectedDamageBudget;
        private Thing testWall;
        private bool accuracyPairPrepared;
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
            if (GenCommandLine.TryGetCommandLineArg("hdShotgunPrecisionAudit", out _))
            {
                TickPrecisionAudit();
                return;
            }
            if (GenCommandLine.TryGetCommandLineArg("hdShotgunImpactAudit", out _))
            {
                TickImpactAudit();
                return;
            }
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
                    AccuracyChecks();
                    StartShot(PawnKindDefOf.Colonist, 8, 0);
                    phase = 3;
                }
                else if (phase == 3)
                {
                    InspectShot("skill-0 human at eight tiles");
                    StartShot(PawnKindDefOf.Colonist, 8);
                    testWall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel),
                        shooter.Position + new IntVec3(0, 0, 5), shooter.Map);
                    phase = 4;
                }
                else if (phase == 4)
                {
                    Check(ShotgunInGameImpactProbe.hits.Count == 0, "swept pawn collision does not bypass a wall placed during flight");
                    Check(testWall.HitPoints < testWall.MaxHitPoints, "native flight resolves the blocking wall before pawn collision");
                    Check(!victim.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(injury =>
                        GameComponent_ShotgunWounds.Current.For(injury)?.shellId == shellId), "wall-blocked pellets create no pawn wounds");
                    File.AppendAllText(Results, "COMPLETE\n");
                    finished = true;
                    Waylith.EngineApplication.Instance.Quit(0);
                }
            }
            catch (Exception error)
            {
                finished = true;
                File.AppendAllText(Results, "FAIL " + error + "\n");
                Waylith.EngineApplication.Instance.Quit(1);
            }
        }
        private Verb_ShootShotgun PrepareShot(PawnKindDef kind, int distance, int skill, string weaponName, bool accuracyFixture = false)
        {
            Map map = Find.CurrentMap;
            IntVec3 source = map.Center + new IntVec3(0, 0, -12);
            IntVec3 destination = source + new IntVec3(0, 0, distance);
            foreach (IntVec3 cell in CellRect.FromLimits(source + new IntVec3(-3, 0, -3), destination + new IntVec3(3, 0, 3)))
                foreach (Thing thing in cell.GetThingList(map).ToArray())
                    if (!(thing is Pawn) && (thing.def.category == ThingCategory.Building || thing.def.category == ThingCategory.Plant
                        || thing.def.Fillage != FillCategory.None)) thing.Destroy();
            bool reuse = accuracyFixture && accuracyPairPrepared && shooter?.Spawned == true && victim?.Spawned == true;
            if (!reuse)
            {
                if (shooter != null && !shooter.Destroyed) shooter.Destroy();
                if (victim != null && !victim.Destroyed) victim.Destroy();
                // Functional damage checks also need healthy adult inputs. The
                // accuracy sweep reuses this exact pair across skill changes.
                Rand.PushState(70012);
                try
                {
                    Pawn Generate(PawnKindDef pawnKind, Faction faction) => PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        pawnKind, faction, forceGenerateNewPawn: true, mustBeCapableOfViolence: true,
                        canGeneratePawnRelations: false, fixedBiologicalAge: 25, fixedChronologicalAge: 25,
                        allowAddictions: false, forcedXenotype: XenotypeDefOf.Baseliner));
                    shooter = Generate(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    victim = Generate(kind, null);
                }
                finally { Rand.PopState(); }
                shooter.apparel?.DestroyAll();
                shooter.health.RemoveAllHediffs();
                victim.health.RemoveAllHediffs();
                accuracyPairPrepared = accuracyFixture;
                GenSpawn.Spawn(shooter, source, map);
                GenSpawn.Spawn(victim, destination, map);
            }
            else { shooter.Position = source; victim.Position = destination; }
            Check(!shooter.skills.GetSkill(SkillDefOf.Shooting).TotallyDisabled, "test shooter can use shooting accuracy");
            shooter.skills.GetSkill(SkillDefOf.Shooting).Level = skill;
            victim.apparel?.DestroyAll();
            victim.equipment?.DestroyAllEquipment();
            foreach (Pawn pawn in new[] { shooter, victim })
            {
                Job wait = JobMaker.MakeJob(JobDefOf.Wait);
                wait.expiryInterval = 99999;
                pawn.jobs.StartJob(wait, JobCondition.InterruptForced);
            }
            ThingWithComps weapon = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(weaponName));
            shooter.equipment.DestroyAllEquipment();
            shooter.equipment.AddEquipment(weapon);
            if (accuracyFixture)
            {
                // Ideology's darkness offsets and weather are independent of
                // skill and can floor both native hit reports at 0.0201.
                shooter.ideo?.SetIdeo(null);
                victim.ideo?.SetIdeo(null);
                map.weatherManager.curWeather = map.weatherManager.lastWeather = WeatherDefOf.Clear;
                map.skyManager.ForceSetCurSkyGlow(1f);
                foreach (StatDef stat in DefDatabase<StatDef>.AllDefsListForReading) stat.Worker.ClearCacheForThing(shooter);
                Check(shooter.skills.GetSkill(SkillDefOf.Shooting).Level == skill, "controlled shooter has the requested effective skill");
            }
            Verb_ShootShotgun verb = (Verb_ShootShotgun)weapon.TryGetComp<CompEquippable>().PrimaryVerb;
            verb.caster = shooter;
            AccessTools.FieldRefAccess<Verb, LocalTargetInfo>("currentTarget")(verb) = victim;
            return verb;
        }
        private void StartShot(PawnKindDef kind, int distance, int skill = 20)
        {
            Verb_ShootShotgun verb = PrepareShot(kind, distance, skill, "HD_Gun_M1897_Weapon");
            recordedShots = (int)shooter.records.GetValue(RecordDefOf.ShotsFired);
            Check((bool)AccessTools.Method(typeof(Verb_ShootShotgun), "TryCastShot").Invoke(verb, null), kind.defName + " shell fired");
            var launched = shooter.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile_ShotgunPellet>()
                .Where(pellet => pellet.Launcher == shooter).ToArray();
            Check(launched.Length == 9 && launched.Select(pellet => pellet.Shell).Distinct().Count() == 1,
                kind.defName + " has nine native projectiles sharing one shell");
            shellId = launched[0].Shell.id;
            expectedHits = launched.Count(pellet => pellet.usedTarget.Thing == victim && pellet.HitsPawn(victim));
            // Generated pawns may have Tough/Delicate or damage-factor genes.
            // The shell budget must include their actual incoming multiplier.
            expectedDamageBudget = launched.Sum(pellet => pellet.DamageAmount) * victim.GetStatValue(StatDefOf.IncomingDamageFactor);
            var jitter = AccessTools.FieldRefAccess<Pawn_DrawTracker, JitterHandler>("jitterer")(victim.Drawer);
            var offset = AccessTools.FieldRefAccess<JitterHandler, Vector3>("curOffset");
            Vector3 previousOffset = offset(jitter);
            try
            {
                var visualDamage = new DamageInfo(DamageDefOf.Bullet, 1f, angle: 0f);
                jitter.Notify_DamageApplied(visualDamage);
                jitter.Notify_DamageApplied(visualDamage);
                Check(launched.Count(pellet => pellet.usedTarget.Thing == victim && pellet.HitsPawn(victim)) == expectedHits,
                    "two bullet flinch animations do not remove on-target pellets");
            }
            finally { offset(jitter) = previousOffset; }
            ShotgunInGameImpactProbe.shellId = shellId;
            ShotgunInGameImpactProbe.hits.Clear();
            Check((int)shooter.records.GetValue(RecordDefOf.ShotsFired) - recordedShots == 1, "native shot record increments once");
            Check(ShotgunBodyProfile.For(victim.RaceProps.body).surfaces.Count > 0, kind.defName + " body automatically has an external profile");
            waitUntil = Find.TickManager.TicksGame + 120;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        }
        private void AccuracyChecks()
        {
            foreach (string weapon in new[] { "HD_Gun_M1897_Weapon", "HD_Gun_WinM1887_Weapon" })
            {
                SampleAccuracy(weapon, 0, 5);
                float near = SampleAccuracy(weapon, 0, 8);
                float skilled = SampleAccuracy(weapon, 20, 8);
                Check(skilled > near, weapon + " better aim improves close grouping");
            }
            SampleAccuracy("HD_Gun_M1897_Weapon", 0, 12);
            float farNovice = SampleAccuracy("HD_Gun_M1897_Weapon", 0, 17);
            float farSkilled = SampleAccuracy("HD_Gun_M1897_Weapon", 20, 17);
            Check(farSkilled > farNovice + 1f, "native shooting skill improves distant hits");
            Check(farSkilled < 7f, "native skilled aim retains unchoked distant spread");
            SampleAccuracy("HD_Gun_M79_Weapon", 0, 8);
        }
        private float SampleAccuracy(string weaponName, int skill, int distance)
        {
            const int samples = 256;
            Verb_ShootShotgun verb = PrepareShot(PawnKindDefOf.Colonist, distance, skill, weaponName, accuracyFixture: true);
            Check(verb.verbProps.canGoWild, weaponName + " tests ordinary native aim failures");
            CompM79Launcher launcher = verb.EquipmentSource.TryGetComp<CompM79Launcher>();
            if (launcher != null)
                AccessTools.FieldRefAccess<CompM79Launcher, ThingDef>("selectedAmmoDef")(launcher) = DefDatabase<ThingDef>.GetNamed("HD_40mmM576MP_Round");
            int count = launcher == null ? 9 : 20;
            var initialReport = ShotReport.HitReportFor(shooter, verb, victim);
            float weatherFactor = (float)AccessTools.Field(typeof(ShotReport), "factorFromWeather").GetValue(initialReport);
            float darknessOffset = (float)AccessTools.Field(typeof(ShotReport), "offsetFromDarkness").GetValue(initialReport);
            Check(weatherFactor == 1f && darknessOffset == 0f, "controlled accuracy excludes weather and ideology lighting offsets");
            File.AppendAllText(Results, $"AIM skill={shooter.skills.GetSkill(SkillDefOf.Shooting).Level}, pawnAccuracy={shooter.GetStatValue(StatDefOf.ShootingAccuracyPawn):R}, factor={ShotReport.HitFactorFromShooter(shooter, distance):R}, report={initialReport.AimOnTargetChance_StandardTarget:R}\n");
            int total = 0, four = 0, five = 0;
            bool validShells = true;
            Rand.PushState(39715);
            try
            {
                for (int shot = 0; shot < samples; shot++)
                {
                    if (launcher != null) AccessTools.FieldRefAccess<CompM79Launcher, bool>("loaded")(launcher) = true;
                    if (!(bool)AccessTools.Method(typeof(Verb_ShootShotgun), "TryCastShot").Invoke(verb, null))
                        throw new InvalidOperationException(weaponName + " failed to fire accuracy sample " + shot);
                    var launched = shooter.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile_ShotgunPellet>()
                        .Where(pellet => pellet.Launcher == shooter).ToArray();
                    validShells &= launched.Length == count && launched.All(pellet => pellet.Shell != null)
                        && launched.Select(pellet => pellet.Shell).Distinct().Count() == 1;
                    // Native firing and cover selection, sampled before flight
                    // so repeated shots do not destroy or down the target.
                    int hits = launched.Count(pellet => pellet.usedTarget.Thing == victim && pellet.HitsPawn(victim));
                    total += hits;
                    if (hits >= 4) four++;
                    if (hits >= 5) five++;
                    foreach (var pellet in launched) pellet.Destroy();
                }
            }
            finally { Rand.PopState(); }
            float mean = (float)total / samples;
            File.AppendAllText(Results, $"ACCURACY {weaponName}, skill={skill}, distance={distance}, mean={mean:0.00}/{count}, >=4={100f * four / samples:0.0}%, >=5={100f * five / samples:0.0}%\n");
            Check(validShells, weaponName + " accuracy samples retain correct shell/projectile count");
            if (skill == 0 && distance <= 8)
            {
                Check(mean >= count * 0.65f, weaponName + " inexperienced close shots land most pellets");
                Check(four >= samples * 0.90f && five >= samples * 0.85f, weaponName + " inexperienced close shots usually land at least four to five pellets");
            }
            return mean;
        }
        private void InspectShot(string label)
        {
            int actualHits = ShotgunInGameImpactProbe.hits.Count;
            File.AppendAllText(Results, $"IMPACTS {label}: {actualHits}/9 actual, {expectedHits}/9 initially on target\n");
            Check(actualHits >= Math.Min(4, expectedHits), label + " sampled close trajectories reach the target during native flight");
            var wounds = victim.health.hediffSet.hediffs.OfType<Hediff_Injury>()
                .Where(injury => GameComponent_ShotgunWounds.Current.For(injury)?.shellId == shellId).ToArray();
            File.AppendAllText(Results, $"WOUNDS expectedShell={shellId}, total={victim.health.hediffSet.hediffs.OfType<Hediff_Injury>().Count()}, tracked={wounds.Length}, dead={victim.Dead}, destroyed={victim.Destroyed}, budget={expectedDamageBudget:R}\n");
            Check(wounds.Length > 0, label + " received native pellet injuries");
            Check(wounds.Sum(injury => injury.Severity) <= expectedDamageBudget + 0.01f, label + " surface and internal injuries respect shell damage budget");
            Check(wounds.All(injury => GameComponent_ShotgunWounds.Current.For(injury).pelletCount > 0), label + " wounds retain pellet counts");
            Check(wounds.Where(injury => !injury.IsPermanent() && !injury.IsTended()).GroupBy(injury => new { injury.Part, injury.def })
                .All(group => group.Count() == 1), label + " same-shell same-part injuries are grouped");
            Check(wounds.All(injury => injury.LabelBase.Contains("HD_Shotgun_PelletCount".Translate(
                GameComponent_ShotgunWounds.Current.For(injury).pelletCount).Resolve())), label + " wound labels show pellet counts");
            Hediff_Injury scar = wounds.FirstOrDefault(injury => injury.TryGetComp<HediffComp_GetsPermanent>() != null);
            Check(scar != null, label + " gunshot supports the permanent-injury label path");
            var permanent = scar.TryGetComp<HediffComp_GetsPermanent>();
            HediffCompProperties previousProps = permanent.props;
            bool wasPermanent = permanent.IsPermanent;
            try
            {
                permanent.props = new HediffCompProperties_GetsPermanent { permanentLabel = "audit scar", instantlyPermanentLabel = "audit destroyed" };
                permanent.IsPermanent = true;
                string expected = "HD_Shotgun_PelletCount".Translate(GameComponent_ShotgunWounds.Current.For(scar).pelletCount).Resolve();
                string actual = scar.LabelBase;
                Check(actual.StartsWith("audit") && actual.Contains(expected) && actual.IndexOf(expected) == actual.LastIndexOf(expected),
                    label + " permanent and destroyed-organ labels show pellet counts exactly once");
            }
            finally { permanent.props = previousProps; permanent.IsPermanent = wasPermanent; }
        }
    }

    [HarmonyPatch(typeof(Projectile_ShotgunPellet), "Impact")]
    internal static class ShotgunInGameImpactProbe
    {
        internal static int shellId;
        internal static readonly HashSet<int> hits = new HashSet<int>();
        private static void Prefix(Projectile_ShotgunPellet __instance, Thing hitThing, bool blockedByShield)
        {
            if (!blockedByShield && hitThing is Pawn && hitThing == __instance.intendedTarget.Thing && __instance.Shell?.id == shellId)
                hits.Add(__instance.thingIDNumber);
        }
        private static void Postfix(Projectile_ShotgunPellet __instance, Thing hitThing)
        {
            if (!(hitThing is Pawn pawn) || !hits.Contains(__instance.thingIDNumber)) return;
            string injuries = string.Join(";", pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Select(i =>
                $"{i.def.defName}:{i.Severity:R}:{i.Part?.def.defName}:shell={GameComponent_ShotgunWounds.Current.For(i)?.shellId}:pellets={GameComponent_ShotgunWounds.Current.For(i)?.pelletCount}"));
            File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "shotgun-results.txt"),
                $"AFTER_IMPACT pellet={__instance.thingIDNumber}, expectedShell={shellId}, damage={__instance.DamageAmount}, energy={__instance.Energy:R}, dead={pawn.Dead}, injuries=[{injuries}]\n");
        }
    }
}
#endif
