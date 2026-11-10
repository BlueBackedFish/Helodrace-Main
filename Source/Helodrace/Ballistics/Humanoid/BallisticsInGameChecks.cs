#if BALLISTICS_TESTS
// Isolated -quicktest validation build only; excluded from the shipped assembly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// Real-engine checks of the phase 2 humanoid protection path: actual projectile defs, Bullet.Impact,
    /// shield PreApplyDamage, vanilla ArmorUtility, modular plates and soft armour. Torso hits are forced so the
    /// plate coverage statistics are measurable; everything else runs the shipped code.
    /// </summary>
    public sealed class BallisticsInGameChecks : GameComponent
    {
        private bool finished;
        private Pawn shooter;
        private Pawn victim;
        private static bool forceTorso;
        private bool diagnosed;
        private int shotsLogged;
        private static readonly List<ArmorCall> calls = new List<ArmorCall>();
        private static readonly MethodInfo Impact = typeof(Projectile).GetMethod("Impact",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private string Results => Path.Combine(GenFilePaths.SaveDataFolderPath, "ballistics-results.txt");

        private struct ArmorCall
        {
            public float Before;
            public float After;
            public DamageDef Def;
        }

        private sealed class Shot
        {
            public bool Called;
            public ArmorCall Call;
            public ImpactTrace Trace;
            public float Damage;
            public bool Notes(string text) => Trace != null && Trace.Stages.Any(s => s.Note != null && s.Note.Contains(text));
            public bool Stopped => Trace?.Result == ImpactResult.Stopped;
        }

        public BallisticsInGameChecks(Game game) { }

        private void Check(bool condition, string message, bool quiet = false)
        {
            if (!condition) throw new InvalidOperationException(message);
            if (!quiet) File.AppendAllText(Results, "PASS " + message + "\n");
        }

        private void Note(string message) => File.AppendAllText(Results, "INFO " + message + "\n");

        private bool armourDone;
        private readonly List<TurnTrial> trials = new List<TurnTrial>();
        private int trialIndex = -1;

        private sealed class TurnTrial
        {
            public string WeaponDef;
            public Pawn Shooter;
            public Thing Target;
            public int StartTick;
            public int SettledTick = -1;
            public float LastYaw;
            public float MaxStep;
            public float WorstSpriteGap;
            public bool AimSeen;
            public int AimTick = -1;
            public float Rate;
            public float Offset;
        }

        public override void GameComponentTick()
        {
            if (finished || Find.CurrentMap == null || Find.TickManager.TicksGame < 120) return;
            try
            {
                if (!armourDone)
                {
                    armourDone = true;
                    File.WriteAllText(Results, "Native ballistic protection checks\n");
                    var harmony = new Harmony("hd.ballistics.ingamechecks");
                    harmony.Patch(AccessTools.Method(typeof(DamageWorker_AddInjury), "ChooseHitPart"),
                        prefix: new HarmonyMethod(typeof(BallisticsInGameChecks), nameof(ForceTorso)));
                    harmony.Patch(AccessTools.Method(typeof(ArmorUtility), nameof(ArmorUtility.GetPostArmorDamage)),
                        prefix: new HarmonyMethod(typeof(BallisticsInGameChecks), nameof(RecordArmorInput)),
                        postfix: new HarmonyMethod(typeof(BallisticsInGameChecks), nameof(RecordArmor)));
                    forceTorso = true;
                    Run();
                    forceTorso = false;
                    if (victim != null && !victim.Destroyed) victim.Destroy();
                    foreach (string weapon in new[] { "HD_Gun_Glock19Gen5_Weapon", "HD_Gun_M14_Weapon", "HD_Gun_M249_Weapon" })
                        trials.Add(new TurnTrial { WeaponDef = weapon });
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                    return;
                }
                if (TickTrials())
                {
                    finished = true;
                    File.AppendAllText(Results, "COMPLETE\n");
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

        /// <summary>
        /// Drafted Helod shooters facing north are ordered to fire at a wall to the east. Each tick the torso
        /// yaw must move at no more than the stance rate, and the drawn Rot4 must show the side the torso
        /// faces. Returns true when all trials and the follow-up checks are done.
        /// </summary>
        private bool TickTrials()
        {
            Map map = Find.CurrentMap;
            int now = Find.TickManager.TicksGame;
            if (trialIndex < 0 || trials[trialIndex].SettledTick >= 0 || now - trials[trialIndex].StartTick > 600)
            {
                if (trialIndex >= 0) FinishTrial(trials[trialIndex]);
                trialIndex++;
                if (trialIndex >= trials.Count)
                {
                    CompareTrials();
                    return true;
                }
                StartTrial(trials[trialIndex], map, now);
                return false;
            }

            TurnTrial trial = trials[trialIndex];
            CompBodyOrientation comp = trial.Shooter.TryGetComp<CompBodyOrientation>();
            float yaw = comp.TorsoYaw;
            trial.MaxStep = Math.Max(trial.MaxStep, Math.Abs(BallisticGeometry.DeltaDegrees(trial.LastYaw, yaw)));
            trial.LastYaw = yaw;
            trial.WorstSpriteGap = Math.Max(trial.WorstSpriteGap,
                Math.Abs(BallisticGeometry.DeltaDegrees(trial.Shooter.Rotation.AsAngle, yaw)));
            if (comp.Aiming)
            {
                if (!trial.AimSeen) trial.AimTick = now;
                trial.AimSeen = true;
                float target = comp.AimYaw + trial.Offset;
                if (Math.Abs(BallisticGeometry.DeltaDegrees(yaw, target)) < 1f) trial.SettledTick = now;
            }
            return false;
        }

        private void StartTrial(TurnTrial trial, Map map, int now)
        {
            IntVec3 origin = map.Center + new IntVec3(-20, 0, 8 + trialIndex * 4);
            foreach (IntVec3 cell in CellRect.FromLimits(origin + new IntVec3(-1, 0, -1), origin + new IntVec3(10, 0, 1)))
                foreach (Thing thing in cell.GetThingList(map).ToArray())
                    if (thing.def.category != ThingCategory.Filth && thing.def.destroyable) thing.Destroy();
            Pawn shooter = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"), Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedBiologicalAge: 25,
                fixedChronologicalAge: 25, allowAddictions: false, mustBeCapableOfViolence: true));
            shooter.equipment.DestroyAllEquipment();
            var weapon = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(trial.WeaponDef),
                GenStuff.DefaultStuffFor(DefDatabase<ThingDef>.GetNamed(trial.WeaponDef)));
            shooter.equipment.AddEquipment(weapon);
            GenSpawn.Spawn(shooter, origin, map, Rot4.North);
            Thing wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.BlocksGranite), origin + new IntVec3(8, 0, 0), map);
            shooter.drafter.Drafted = true;
            CompBodyOrientation comp = shooter.TryGetComp<CompBodyOrientation>();
            Check(comp != null, "Humanlike pawns get the body orientation comp");
            StanceProfileDef profile = StanceProfileDef.For(weapon.def);
            Check(profile != null, trial.WeaponDef + " has a stance profile: " + profile?.defName);
            Note(trial.WeaponDef + " stance " + profile.defName + " (" + profile.family + ")");
            trial.Shooter = shooter;
            trial.Target = wall;
            trial.StartTick = now;
            trial.LastYaw = comp.TorsoYaw;
            trial.Rate = profile.torsoTurnRate;
            trial.Offset = profile.torsoOffsetDegrees;
            Check(Math.Abs(BallisticGeometry.DeltaDegrees(trial.LastYaw, 0f)) < 0.01f, trial.WeaponDef + " shooter starts facing north");
            Job job = JobMaker.MakeJob(JobDefOf.AttackStatic, wall);
            Check(shooter.jobs.TryTakeOrderedJob(job), trial.WeaponDef + " takes the attack order");
        }

        private void FinishTrial(TurnTrial trial)
        {
            CompBodyOrientation comp = trial.Shooter.TryGetComp<CompBodyOrientation>();
            Note(trial.WeaponDef + ": aiming after " + (trial.AimTick - trial.StartTick) + " ticks, turned onto target in "
                + (trial.SettledTick - trial.AimTick) + " ticks, max step "
                + trial.MaxStep.ToString("0.00") + " deg/tick, worst sprite gap " + trial.WorstSpriteGap.ToString("0.0")
                + ", final torso " + comp.TorsoYaw.ToString("0.0") + ", aim " + comp.AimYaw.ToString("0.0")
                + ", head " + comp.HeadYaw.ToString("0.0"));
            Check(trial.AimSeen, trial.WeaponDef + " aims at the target");
            Check(trial.SettledTick >= 0, trial.WeaponDef + " torso settles on aim + stance offset");
            Check(trial.MaxStep <= trial.Rate + 0.01f, trial.WeaponDef + " torso never turns faster than its stance rate");
            Check(trial.WorstSpriteGap <= 45f + BodyOrientationRules.SpriteHysteresis + 0.01f,
                trial.WeaponDef + " drawn side always matches the torso (within one sprite sector)");
            Check(Math.Abs(BallisticGeometry.DeltaDegrees(comp.HeadYaw, comp.AimYaw)) < 1f, trial.WeaponDef + " head looks at the target");

            // A hit on the settled shooter is resolved in the torso frame, not the Rot4 frame.
            if (trial.WeaponDef == "HD_Gun_M14_Weapon")
            {
                victim = trial.Shooter;
                victim.jobs.StopAll();
                victim.drafter.Drafted = false;
                // A physical layer is needed for a trace; the carrier does not change the torso yaw.
                WearCarrier(DefDatabase<ThingDef>.GetNamed("HD_Apparel_IBTVAssault"), "HD_IOTVPart_FrontRear_ESAPI");
                Pawn previousShooter = shooter;
                shooter = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false, forcedXenotype: XenotypeDefOf.Baseliner));
                shooter.apparel?.DestroyAll();
                GenSpawn.Spawn(shooter, victim.Position + new IntVec3(0, 0, -6), victim.Map);
                float torso = comp.TorsoYaw;
                forceTorso = true;
                Shot shot = FireOnce(DefDatabase<ThingDef>.GetNamed("HD_Bullet_556M855A1_Proj"));
                forceTorso = false;
                shooter.Destroy();
                shooter = previousShooter;
                Check(shot.Trace != null, "A physical hit on the turned shooter is traced");
                TraceStage location = shot.Trace.Find(ImpactStageIds.HitLocation);
                double tracedYaw = location.Values.First(v => v.Key == "torsoYawDeg").Value;
                double azimuth = location.Values.First(v => v.Key == "azimuthDeg").Value;
                Note("Turned shooter hit from the south: torso " + torso.ToString("0.0") + ", traced " + tracedYaw.ToString("0.0")
                    + ", azimuth " + azimuth.ToString("0.0") + " (Rot4 " + victim.Rotation.AsAngle + ")");
                Check(Math.Abs(BallisticGeometry.DeltaDegrees((float)tracedYaw, torso)) < 1f, "Hit uses the continuous torso yaw");
                // The round comes from the south: relative to the torso it arrives from 180 - torso.
                float expected = BallisticGeometry.DeltaDegrees(torso, 180f);
                Check(Math.Abs(BallisticGeometry.DeltaDegrees((float)azimuth, expected)) <= 90f,
                    "Hit lands on the side of the torso that faces the shot");
                victim = null;
            }
            trial.Shooter.jobs?.StopAll();
            if (!trial.Shooter.Destroyed) trial.Shooter.Destroy();
            if (!trial.Target.Destroyed) trial.Target.Destroy();
        }

        private void CompareTrials()
        {
            // Settle time also depends on where the pawn faced before it began aiming, so the families are
            // compared by the turn rate actually observed while turning.
            TurnTrial pistol = trials[0], rifle = trials[1], mg = trials[2];
            foreach (TurnTrial trial in trials)
                Check(trial.MaxStep >= trial.Rate - 0.01f, trial.WeaponDef + " turns at its full stance rate");
            Check(pistol.MaxStep > rifle.MaxStep && rifle.MaxStep > mg.MaxStep,
                "Pistol turns faster than rifle faster than machine gun: " + pistol.MaxStep + "/" + rifle.MaxStep + "/" + mg.MaxStep);
        }

        public static bool ForceTorso(Pawn pawn, ref BodyPartRecord __result)
        {
            if (!forceTorso) return true;
            __result = pawn.RaceProps.body.corePart;
            return false;
        }

        // Vanilla mutates the amount parameter in place, so the input is captured before the call.
        public static void RecordArmorInput(float amount, out float __state) => __state = amount;

        public static void RecordArmor(float __state, DamageDef damageDef, float __result)
        {
            calls.Add(new ArmorCall { Before = __state, After = __result, Def = damageDef });
        }

        private void Run()
        {
            Map map = Find.CurrentMap;
            IntVec3 center = map.Center;
            foreach (IntVec3 cell in CellRect.CenteredOn(center, 14))
                foreach (Thing thing in cell.GetThingList(map).ToArray())
                    if (!(thing is Pawn) && thing.def.category != ThingCategory.Filth && thing.def.destroyable) thing.Destroy();

            ThingDef a1 = DefDatabase<ThingDef>.GetNamed("HD_Bullet_556M855A1_Proj");
            ThingDef m855 = DefDatabase<ThingDef>.GetNamed("HD_Bullet_556M855_Proj");
            ThingDef m193 = DefDatabase<ThingDef>.GetNamed("HD_Bullet_556M193_Proj");
            ThingDef pistol = DefDatabase<ThingDef>.GetNamed("HD_Bullet_919M882_Proj");
            ThingDef ball50 = DefDatabase<ThingDef>.GetNamed("HD_Bullet_12799M2Ball_Proj");
            ThingDef vanilla = DefDatabase<ThingDef>.GetNamed("Bullet_Revolver");
            ThingDef ibtv = DefDatabase<ThingDef>.GetNamed("HD_Apparel_IBTVAssault");
            ThingDef defenTech = DefDatabase<ThingDef>.GetNamed("HD_Apparel_ShieldDefenTechIIIA");
            Check(a1.GetModExtension<BulletBallisticsExtension>().HasPhysics
                && a1.GetModExtension<BulletBallisticsExtension>().penetratorClass == PenetratorClass.HardSteel,
                "M855A1 loads physical data from XML");
            Check(ibtv.GetModExtension<BallisticLayupExtension>()?.layup?.KineticLayers.Count == 1, "IBTV soft layup loads");
            Check(DefDatabase<ArmorLayupDef>.GetNamed("HD_Layup_ESAPI").ArealDensity > 30f, "ESAPI layup areal density");

            Pawn Generate() => PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                fixedBiologicalAge: 25, fixedChronologicalAge: 25, allowAddictions: false,
                forcedXenotype: XenotypeDefOf.Baseliner));
            shooter = Generate();
            shooter.apparel?.DestroyAll();
            GenSpawn.Spawn(shooter, center + new IntVec3(0, 0, -10), map);

            // 1. Unarmoured anchor: the wound equals the cartridge's balance damage.
            var unarmoured = Fire(a1, 20, () => { });
            Check(unarmoured.All(s => s.Called && Math.Abs(s.Call.After - s.Call.Before) < 0.001f && s.Call.Def == DamageDefOf.Bullet),
                "Unarmoured M855A1 keeps its full balance damage (20 shots)");
            Check(unarmoured.All(s => s.Trace == null), "No physical layer is traced on a bare pawn");

            // 2. IBTV + ESAPI against M855A1 from the front.
            var esapi = Fire(a1, 150, () => WearCarrier(ibtv, "HD_IOTVPart_FrontRear_ESAPI"));
            Summarize("ESAPI/M855A1", esapi, "HD_IOTVPart_FrontRear_ESAPI");
            var onPlate = esapi.Where(s => s.Notes("HD_IOTVPart_FrontRear_ESAPI")).ToList();
            var offPlate = esapi.Where(s => !s.Notes("HD_IOTVPart_FrontRear_") && !s.Notes("HD_IOTVPart_Sides")).ToList();
            Check(onPlate.Count >= 40, "Front shots reach the ESAPI plate: " + onPlate.Count);
            Check(offPlate.Count >= 15, "Some front torso shots miss the plate area: " + offPlate.Count);
            Check(onPlate.Count(s => s.Stopped) >= onPlate.Count * 0.97, "ESAPI stops M855A1");
            Check(onPlate.Where(s => s.Called).All(s => s.Call.After <= 0f || s.Call.Def == DamageDefOf.Blunt),
                "A stopped round leaves only blunt trauma");
            Check(offPlate.All(s => s.Notes("HD_Apparel_IBTVAssault")), "Off-plate hits meet the IBTV soft panel");
            Check(offPlate.Count(s => !s.Stopped && s.Call.Def == DamageDefOf.Bullet && s.Call.After > 0f
                && s.Call.After < s.Call.Before) >= offPlate.Count * 0.9, "M855A1 perforates the soft panel with a reduced wound");

            // 3. The plate's own grade: SAPI is beaten by M855A1 but stops M855.
            var sapiA1 = Fire(a1, 120, () => WearCarrier(ibtv, "HD_IOTVPart_FrontRear_SAPI"));
            Summarize("SAPI/M855A1", sapiA1, "HD_IOTVPart_FrontRear_SAPI");
            var sapiHits = sapiA1.Where(s => s.Notes("HD_IOTVPart_FrontRear_SAPI")).ToList();
            Check(sapiHits.Count >= 30 && sapiHits.Count(s => !s.Stopped) >= sapiHits.Count * 0.5,
                "M855A1 perforates SAPI on most plate hits");
            Check(sapiHits.Where(s => !s.Stopped && s.Called).All(s => s.Call.After < s.Call.Before),
                "A round through SAPI and the soft panel arrives weaker");
            var sapi855 = Fire(m855, 120, () => WearCarrier(ibtv, "HD_IOTVPart_FrontRear_SAPI"));
            var sapi855Hits = sapi855.Where(s => s.Notes("HD_IOTVPart_FrontRear_SAPI")).ToList();
            Check(sapi855Hits.Count >= 30 && sapi855Hits.Count(s => s.Stopped) >= sapi855Hits.Count * 0.95, "SAPI stops M855");

            // 4. A shot in the back meets the back plate.
            var back = Fire(a1, 60, () => WearCarrier(ibtv, "HD_IOTVPart_FrontRear_ESAPI"), Rot4.North);
            Check(back.Count(s => s.Notes("HD_IOTVPart_FrontRear_ESAPI")) >= 15, "Back shots reach the rear plate");

            // 5. Plate wear: repeated hits on one plate reduce its own durability, and .50 BMG goes through.
            victim = NewVictim();
            InstalledModularArmorPart plates = WearCarrier(ibtv, "HD_IOTVPart_FrontRear_ESAPI");
            int fresh = plates.PlateHitPointsForFacing(ModularArmorFacing.Front);
            int rear = plates.PlateHitPointsForFacing(ModularArmorFacing.Back);
            for (int i = 0; i < 30; i++) { HealVictim(); FireOnce(a1); }
            Check(plates.PlateHitPointsForFacing(ModularArmorFacing.Front) < fresh, "Front plate wears from absorbed energy");
            Check(plates.PlateHitPointsForFacing(ModularArmorFacing.Back) == rear, "Rear plate keeps its own durability");
            Note("ESAPI front plate after 30 frontal M855A1: " + plates.PlateHitPointsForFacing(ModularArmorFacing.Front) + "/" + fresh);
            var big = Fire(ball50, 60, () => WearCarrier(ibtv, "HD_IOTVPart_FrontRear_ESAPI"));
            var bigHits = big.Where(s => s.Notes("HD_IOTVPart_FrontRear_ESAPI")).ToList();
            Check(bigHits.Count >= 15 && bigHits.Count(s => !s.Stopped) >= bigHits.Count * 0.6, "12.7 mm ball perforates ESAPI");

            // 6. Shields: HG2 panel stops 9 mm and is perforated by 5.56 M193.
            var shield9 = Fire(pistol, 40, () => WearShield(defenTech));
            Check(shield9.Count(s => s.Notes("HD_Apparel_ShieldDefenTechIIIA") && s.Stopped && !s.Called) >= 38,
                "DefenTech stops 9 mm in its frontal arc");
            var shield556 = Fire(m193, 40, () => WearShield(defenTech));
            Check(shield556.Count(s => s.Notes("HD_Apparel_ShieldDefenTechIIIA") && s.Called && s.Call.After > 0f) >= 36,
                "M193 perforates the HG2 shield and reaches the body");
            float m193Damage = DamageOf(m193);
            Check(shield556.Where(s => s.Called).All(s => s.Call.After < m193Damage), "A round through the shield arrives weaker");

            // 7. Projectiles without physical data keep the vanilla roll and record nothing.
            var vanillaShots = Fire(vanilla, 30, () => WearCarrier(ibtv, "HD_IOTVPart_FrontRear_ESAPI"));
            Check(vanillaShots.All(s => s.Trace == null && s.Called), "Vanilla revolver rounds use the vanilla armour roll");

            forceTorso = false;
        }

        private void Summarize(string label, List<Shot> shots, string plate)
        {
            var hits = shots.Where(s => s.Notes(plate)).ToList();
            Note(label + ": shots " + shots.Count + ", plate hits " + hits.Count + ", stopped on plate " + hits.Count(s => s.Stopped)
                + ", mean wound after armour " + (shots.Where(s => s.Called).Select(s => s.Call.After).DefaultIfEmpty(0f).Average()).ToString("0.00"));
            ImpactTrace sample = hits.FirstOrDefault()?.Trace;
            if (sample != null) Note("sample trace:\n" + sample.ToText());
        }

        private float DamageOf(ThingDef projectileDef) => projectileDef.projectile.GetDamageAmount(null);

        private Pawn NewVictim() => NewVictim(Rot4.South);

        // The torso yaw starts from the spawn rotation; setting Rotation afterwards would only change the sprite.
        private Pawn NewVictim(Rot4 facing)
        {
            if (victim != null && !victim.Destroyed) victim.Destroy();
            // Helod equipment is restricted to the Helod race (HelodRace PawnCanWear).
            victim = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"), Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedBiologicalAge: 25,
                fixedChronologicalAge: 25, allowAddictions: false));
            victim.apparel?.DestroyAll();
            GenSpawn.Spawn(victim, Find.CurrentMap.Center, Find.CurrentMap, facing);
            return victim;
        }

        private void HealVictim()
        {
            if (victim == null || victim.Dead || victim.Destroyed)
            {
                NewVictim();
                return;
            }
            victim.health.RemoveAllHediffs();
        }

        private InstalledModularArmorPart WearCarrier(ThingDef carrierDef, string plateSet)
        {
            victim.apparel.DestroyAll();
            var carrier = (Apparel)ThingMaker.MakeThing(carrierDef);
            CompModularArmor comp = carrier.TryGetComp<CompModularArmor>();
            ModularArmorPartDef part = DefDatabase<ModularArmorPartDef>.GetNamed(plateSet);
            var preset = new ModularArmorPresetDef
            {
                defName = "HD_BallisticsTest_" + plateSet,
                armorDef = carrierDef,
                fixedParts = new List<ModularArmorPresetPart> { new ModularArmorPresetPart { slot = part.slot, part = part } }
            };
            Check(comp.TryApplyPreset(preset, out string rejection), "Install " + plateSet + " " + rejection, quiet: true);
            victim.apparel.Wear(carrier, false, true);
            Check(victim.apparel.WornApparel.Contains(carrier), "Victim wears " + carrierDef.defName, quiet: true);
            if (!diagnosed)
            {
                diagnosed = true;
                BodyPartRecord torso = victim.RaceProps.body.corePart;
                Note("victim race " + victim.def.defName + ", worn " + victim.apparel.WornApparel.Count
                    + ", carrier worn " + victim.apparel.WornApparel.Contains(carrier)
                    + ", covers torso " + carrier.def.apparel.CoversBodyPart(torso)
                    + ", groups " + string.Join(",", carrier.def.apparel.bodyPartGroups.Select(g => g.defName)));
            }
            return comp.InstalledParts.First(p => p.part == part);
        }

        private void WearShield(ThingDef shieldDef)
        {
            victim.apparel.DestroyAll();
            var shield = (Apparel)ThingMaker.MakeThing(shieldDef);
            victim.apparel.Wear(shield, false, true);
            Check(victim.apparel.WornApparel.Contains(shield), "Victim wears " + shieldDef.defName, quiet: true);
        }

        private List<Shot> Fire(ThingDef projectileDef, int count, Action dress, Rot4? facing = null)
        {
            var shots = new List<Shot>();
            for (int i = 0; i < count; i++)
            {
                NewVictim(facing ?? Rot4.South);
                dress();
                shots.Add(FireOnce(projectileDef));
            }
            return shots;
        }

        private Shot FireOnce(ThingDef projectileDef)
        {
            ImpactTrace before = HumanoidBallisticImpact.RecentTraces.Count > 0 ? HumanoidBallisticImpact.RecentTraces[0] : null;
            calls.Clear();
            var projectile = (Projectile)GenSpawn.Spawn(projectileDef, shooter.Position, shooter.Map);
            projectile.Launch(shooter, shooter.DrawPos, victim, victim, ProjectileHitFlags.IntendedTarget);
            float damage = projectile.DamageAmount;
            Impact.Invoke(projectile, new object[] { victim, false });
            ImpactTrace after = HumanoidBallisticImpact.RecentTraces.Count > 0 ? HumanoidBallisticImpact.RecentTraces[0] : null;
            if (shotsLogged++ < 3 || (victim.apparel.WornApparel.Count > 0 && shotsLogged < 28 && shotsLogged > 22))
                Note("shot " + projectileDef.defName + " damage " + damage + " calls " + calls.Count + " "
                    + string.Join(";", calls.Select(c => c.Before + "->" + c.After + " " + c.Def?.defName))
                    + " worn " + victim.apparel.WornApparel.Count + " trace " + (after != before)
                    + " hediffs " + string.Join(",", victim.health.hediffSet.hediffs.Select(h => h.def.defName + "@" + h.Part?.def?.defName)));
            return new Shot
            {
                Called = calls.Count > 0,
                Call = calls.Count > 0 ? calls[0] : default,
                Trace = after != before ? after : null,
                Damage = damage
            };
        }
    }
}
#endif
