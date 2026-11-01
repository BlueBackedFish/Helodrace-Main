using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests
{
    public sealed class MigrationChecks : GameComponent
    {
        private const string SaveName = "BlancasMigrationFixture16";
        private const string Prefix = "BlancasFixture_";
        private static bool requestedLoad;
        private bool complete;
        private bool blancasSleepRoundtrip, blancasSleepVerified;
        private bool blancasMkultraRoundtrip, blancasMkultraVerified;
        private bool blancasDrugRoundtrip, blancasDrugVerified;
        private bool mainStatePrepared;
        private bool blancasGenerationPrepared;
        private bool lra7Prepared;
        private bool grenadePrepared;
        private bool towPrepared;
        private bool m47Prepared;
        private bool shotgunPrepared;
        private bool switchbladePrepared;
        private bool casPrepared;
        private bool cigarettePrepared;
        private bool mh60Prepared;
#if HELODRACE_MAIN_CHECKS
        private bool lra7VerifiedOnLoad;
        private bool grenadeVerifiedOnLoad;
        private bool towVerifiedOnLoad;
        private bool m47VerifiedOnLoad;
        private bool shotgunVerifiedOnLoad;
        private bool switchbladeVerifiedOnLoad;
        private bool casVerifiedOnLoad;
        private bool cigaretteVerifiedOnLoad;
        private bool mh60VerifiedOnLoad;
#endif
        private string output, mode;

        public MigrationChecks(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref blancasSleepRoundtrip, "migrationBlancasSleepRoundtrip", false);
            Scribe_Values.Look(ref blancasMkultraRoundtrip, "migrationBlancasMkultraRoundtrip", false);
            Scribe_Values.Look(ref blancasDrugRoundtrip, "migrationBlancasDrugRoundtrip", false);
            Scribe_Values.Look(ref mainStatePrepared, "migrationMainStatePrepared", false);
            Scribe_Values.Look(ref blancasGenerationPrepared, "migrationBlancasGenerationPrepared", false);
            Scribe_Values.Look(ref lra7Prepared, "migrationLra7Prepared", false);
            Scribe_Values.Look(ref grenadePrepared, "migrationGrenadePrepared", false);
            Scribe_Values.Look(ref towPrepared, "migrationTowPrepared", false);
            Scribe_Values.Look(ref m47Prepared, "migrationM47Prepared", false);
            Scribe_Values.Look(ref shotgunPrepared, "migrationShotgunPrepared", false);
            Scribe_Values.Look(ref switchbladePrepared, "migrationSwitchbladePrepared", false);
            Scribe_Values.Look(ref casPrepared, "migrationCasPrepared", false);
            Scribe_Values.Look(ref cigarettePrepared, "migrationCigarettePrepared", false);
            Scribe_Values.Look(ref mh60Prepared, "migrationMh60Prepared", false);
        }

        public override void LoadedGame()
        {
            if (GenCommandLine.TryGetCommandLineArg("hdMigrationCase", out string drugCase)
                && (drugCase == "blancas-drug-load" || drugCase == "blancas-drug-main")
                && GenCommandLine.TryGetCommandLineArg("hdMigrationOutput", out string drugOutput))
            {
                output = drugOutput; Directory.CreateDirectory(output);
                File.AppendAllText(Path.Combine(output, "checks.txt"), "Drug verification before first simulation tick; roundtrip=" + blancasDrugRoundtrip + "\n");
                try { BlancasDrugChecks.VerifyLoaded(output, blancasDrugRoundtrip, Check); blancasDrugVerified = true; }
                catch (Exception error)
                {
                    complete = true; File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL " + error + "\n");
                    Log.Error("Drug migration verification failed: " + error); Quit(1);
                }
                return;
            }
            if (GenCommandLine.TryGetCommandLineArg("hdMigrationCase", out string mkCase)
                && (mkCase == "blancas-mkultra-load" || mkCase == "blancas-mkultra-main")
                && GenCommandLine.TryGetCommandLineArg("hdMigrationOutput", out string mkOutput))
            {
                output = mkOutput; Directory.CreateDirectory(output);
                File.AppendAllText(Path.Combine(output, "checks.txt"), "MKultra verification before first simulation tick; roundtrip=" + blancasMkultraRoundtrip + "\n");
                try
                {
                    BlancasMkultraChecks.VerifyLoaded(output, blancasMkultraRoundtrip, Check);
                    blancasMkultraVerified = true;
                }
                catch (Exception error)
                {
                    complete = true; File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL " + error + "\n");
                    Log.Error("MKultra migration verification failed: " + error); Quit(1);
                }
                return;
            }
            if (GenCommandLine.TryGetCommandLineArg("hdMigrationCase", out string sleepCase)
                && (sleepCase == "blancas-sleep-load" || sleepCase == "blancas-sleep-main")
                && GenCommandLine.TryGetCommandLineArg("hdMigrationOutput", out string sleepOutput))
            {
                output = sleepOutput; Directory.CreateDirectory(output);
                File.AppendAllText(Path.Combine(output, "checks.txt"), "Sleep verification before first simulation tick; roundtrip=" + blancasSleepRoundtrip + "\n");
                try
                {
                    BlancasSleepChecks.VerifyLoaded(output, blancasSleepRoundtrip, Check);
                    if (!blancasSleepRoundtrip) BlancasSleepChecks.Resume(output, Check);
                    blancasSleepVerified = true;
                }
                catch (Exception error)
                {
                    complete = true; File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL " + error + "\n");
                    Log.Error("Sleep migration verification failed: " + error); Quit(1);
                }
                return;
            }
#if HELODRACE_MAIN_CHECKS
            if (!GenCommandLine.TryGetCommandLineArg("hdMigrationCase", out string activeCase)
                || !((lra7Prepared && activeCase == "native-lra7") || (grenadePrepared && activeCase == "native-grenades") || (towPrepared && activeCase == "native-tow") || (m47Prepared && activeCase == "native-m47") || (shotgunPrepared && activeCase == "native-shotgun") || (switchbladePrepared && activeCase == "native-switchblade") || (casPrepared && activeCase == "native-cas") || (cigarettePrepared && activeCase == "native-cigarette") || (mh60Prepared && activeCase == "native-mh60"))
                || !GenCommandLine.TryGetCommandLineArg("hdMigrationOutput", out output)) return;
            try
            {
                // Inspect before ordinary simulation ticks decrement the lifetime.
                if (activeCase == "native-lra7")
                {
                    NativeLra7Checks.Verify(output, Check);
                    lra7VerifiedOnLoad = true;
                }
                else if (activeCase == "native-grenades")
                {
                    NativeGrenadeChecks.Verify(output, Check);
                    grenadeVerifiedOnLoad = true;
                }
                else if (activeCase == "native-tow")
                {
                    NativeTowChecks.Verify(output, Check);
                    towVerifiedOnLoad = true;
                }
                else if (activeCase == "native-m47")
                {
                    NativeM47Checks.Verify(output, Check);
                    m47VerifiedOnLoad = true;
                }
                else if (activeCase == "native-shotgun")
                {
                    NativeShotgunChecks.Verify(output, Check);
                    shotgunVerifiedOnLoad = true;
                }
                else if (activeCase == "native-switchblade")
                {
                    NativeSwitchbladeChecks.Verify(output, Check);
                    switchbladeVerifiedOnLoad = true;
                }
                else if (activeCase == "native-cigarette")
                {
                    NativeCigaretteChecks.Verify(output, Check);
                    cigaretteVerifiedOnLoad = true;
                }
                else if (activeCase == "native-cas")
                {
                    NativeCasChecks.Verify(output, Check);
                    casVerifiedOnLoad = true;
                }
                else
                {
                    NativeMh60Checks.Verify(output, Check);
                    mh60VerifiedOnLoad = true;
                }
            }
            catch (Exception error)
            {
                complete = true;
                File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL " + error + "\n");
                Log.Error("Engine migration checks failed: " + error);
                Quit(1);
            }
#endif
        }

#if HELODRACE_MAIN_CHECKS
        public override void GameComponentUpdate()
        {
            if (Find.CurrentMap != null && GenCommandLine.TryGetCommandLineArg("hdMigrationCase", out string activeCase)
                && activeCase == "native-runtime") NativeRuntimeChecks.ObserveFrame();
        }
#endif

        public override void GameComponentTick()
        {
            if (complete || Find.CurrentMap == null
                || !GenCommandLine.TryGetCommandLineArg("hdMigrationOutput", out output)) return;
            GenCommandLine.TryGetCommandLineArg("hdMigrationCase", out mode);
#if HELODRACE_MAIN_CHECKS
            if (mode == "native-runtime" && !NativeRuntimeChecks.FrameProbeReady) return;
#endif
            Directory.CreateDirectory(output);
            if ((mode == "load" || mode == "blancas-sleep-load" || mode == "blancas-sleep-main" || mode == "blancas-mkultra-load" || mode == "blancas-mkultra-main" || mode == "blancas-drug-load" || mode == "blancas-drug-main") && !requestedLoad)
            {
                requestedLoad = true;
                complete = true;
                GameDataSaveLoader.LoadGame(mode == "load" ? SaveName : mode.StartsWith("blancas-drug", StringComparison.Ordinal) ? BlancasDrugChecks.FixtureSave : mode.StartsWith("blancas-mkultra", StringComparison.Ordinal) ? BlancasMkultraChecks.FixtureSave : BlancasSleepChecks.FixtureSave);
                return;
            }
            complete = true;
            try
            {
                if (mode != "blancas-drug-load" && mode != "blancas-drug-main" && mode != "blancas-mkultra-load" && mode != "blancas-mkultra-main" && mode != "blancas-sleep-load" && mode != "blancas-sleep-main" && (mode != "main-state" || !mainStatePrepared) && (mode != "blancas-generation" || !blancasGenerationPrepared)
                    && (mode != "native-lra7" || !lra7Prepared) && (mode != "native-grenades" || !grenadePrepared) && (mode != "native-tow" || !towPrepared) && (mode != "native-m47" || !m47Prepared) && (mode != "native-shotgun" || !shotgunPrepared) && (mode != "native-switchblade" || !switchbladePrepared) && (mode != "native-cas" || !casPrepared) && (mode != "native-mh60" || !mh60Prepared) && (mode != "native-cigarette" || !cigarettePrepared))
                    File.WriteAllText(Path.Combine(output, "checks.txt"), "Engine migration: " + mode + "\n");
                else File.AppendAllText(Path.Combine(output, "checks.txt"), "Loaded new 1.7 state\n");
                Check(DefDatabase<ThingDef>.GetNamedSilentFail("BD_Ketamine") != null, "Blancas drug definitions loaded");
                Check(DefDatabase<HediffDef>.GetNamedSilentFail("BD_DeepSleep") != null, "Blancas sleep definitions loaded");
                Assembly drugAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "HelodBlancasDrug");
                Check(drugAssembly != null, "Blancas versioned assembly: " + drugAssembly.Location);
                if (mode == "blancas-drug-fixture")
                {
                    BlancasDrugChecks.Create(output, Check); GameDataSaveLoader.SaveGame(BlancasDrugChecks.FixtureSave);
                    Check(File.Exists(GenFilePaths.FilePathForSavedGame(BlancasDrugChecks.FixtureSave)), "actual frozen 1.6 drug matrix fixture saved");
                }
                else if (mode == "blancas-drug-load" || mode == "blancas-drug-main")
                {
                    Check(blancasDrugVerified, "drug state verified before first simulation tick");
                    if (!blancasDrugRoundtrip)
                    {
                        BlancasDrugChecks.CompareCurrent(output, Check); blancasDrugRoundtrip = true; BlancasDrugChecks.Snapshot(output, true);
                        GameDataSaveLoader.SaveGame(BlancasDrugChecks.RoundtripSave); GameDataSaveLoader.LoadGame(BlancasDrugChecks.RoundtripSave); return;
                    }
                    Check(File.Exists(GenFilePaths.FilePathForSavedGame(BlancasDrugChecks.RoundtripSave)), "migrated and current drug states saved and reloaded");
                }
                else if (mode == "blancas-mkultra-fixture")
                {
                    BlancasMkultraChecks.Create(output, Check);
                    GameDataSaveLoader.SaveGame(BlancasMkultraChecks.FixtureSave);
                    Check(File.Exists(GenFilePaths.FilePathForSavedGame(BlancasMkultraChecks.FixtureSave)), "actual frozen 1.6 MKultra fixture saved");
                }
                else if (mode == "blancas-mkultra-load" || mode == "blancas-mkultra-main")
                {
                    Check(blancasMkultraVerified, "MKultra stored state verified before simulation resumed");
                    if (!blancasMkultraRoundtrip)
                    {
                        BlancasMkultraChecks.CompareCurrent(output, Check);
                        blancasMkultraRoundtrip = true; BlancasMkultraChecks.Snapshot(output, true);
                        GameDataSaveLoader.SaveGame(BlancasMkultraChecks.RoundtripSave);
                        GameDataSaveLoader.LoadGame(BlancasMkultraChecks.RoundtripSave); return;
                    }
                    Check(File.Exists(GenFilePaths.FilePathForSavedGame(BlancasMkultraChecks.RoundtripSave)), "migrated and current MKultra state saved/reloaded without duplicate or reset");
                }
                else if (mode == "blancas-sleep-fixture")
                {
                    BlancasSleepChecks.Create(output, Check);
                    GameDataSaveLoader.SaveGame(BlancasSleepChecks.FixtureSave);
                    Check(File.Exists(GenFilePaths.FilePathForSavedGame(BlancasSleepChecks.FixtureSave)), "actual frozen 1.6 sleep fixture saved");
                }
                else if (mode == "blancas-sleep-load" || mode == "blancas-sleep-main")
                {
                    Check(blancasSleepVerified, "sleep state and callbacks verified before simulation resumed");
                    if (!blancasSleepRoundtrip)
                    {
                        blancasSleepRoundtrip = true; BlancasSleepChecks.Snapshot(output, true);
                        GameDataSaveLoader.SaveGame(BlancasSleepChecks.RoundtripSave);
                        GameDataSaveLoader.LoadGame(BlancasSleepChecks.RoundtripSave); return;
                    }
                    Check(File.Exists(GenFilePaths.FilePathForSavedGame(BlancasSleepChecks.RoundtripSave)), "migrated sleep state saved and reloaded without duplicate or reset");
                }
                else if (mode == "baseline")
                {
                    CreateFixture();
                    WriteSnapshot();
                    GameDataSaveLoader.SaveGame(SaveName);
                    Check(File.Exists(GenFilePaths.FilePathForSavedGame(SaveName)), "legacy fixture saved by the actual game");
                }
                else if (mode == "load")
                {
                    VerifySnapshot();
                    GameDataSaveLoader.SaveGame("BlancasMigrationRoundtrip17");
                    Check(File.Exists(GenFilePaths.FilePathForSavedGame("BlancasMigrationRoundtrip17")), "updated fixture saved again");
                }
                else if (mode == "main")
                {
                    Check(DefDatabase<GeneDef>.GetNamedSilentFail("HD_Gene_AmphetamineDependency") != null, "Main mandatory Blancas gene loaded");
                    Check(DefDatabase<ThingDef>.GetNamedSilentFail("HD_BasicWorkbench") != null, "Main industry definitions loaded");
                    Check(AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Helodrace"), "Main assembly loaded");
                    Check(DefDatabase<ThingDef>.GetNamed("HD_WoodenBaseballBat").relicChance == 0,
                        "single-material wooden bat excluded from unique-stuff relic generation");
                    File.WriteAllLines(Path.Combine(output, "relic-stuffs.tsv"),
                        DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.relicChance > 0 && d.MadeFromStuff)
                            .Select(d => d.defName + "\t" + string.Join(",", GenStuff.AllowedStuffsFor(d).Select(s => s.defName + ":" + s.BaseMarketValue))));
                    GameDataSaveLoader.SaveGame("HelodraceMigration17");
                }
#if NET10_0_OR_GREATER
                else if (mode == "blancas") BlancasEngineChecks.Run(Check);
                else if (mode == "blancas-generation")
                {
                    if (!blancasGenerationPrepared)
                    {
                        BlancasGenerationChecks.Create(Check);
                        blancasGenerationPrepared = true;
                        GameDataSaveLoader.SaveGame("BlancasGeneration17");
                        GameDataSaveLoader.LoadGame("BlancasGeneration17");
                        return;
                    }
                    BlancasGenerationChecks.Verify(Check);
                }
#if HELODRACE_MAIN_CHECKS
                else if (mode == "native-mh60")
                {
                    if (!mh60Prepared)
                    {
                        NativeMh60Checks.Create(output, Check);
                        mh60Prepared = true;
                        GameDataSaveLoader.SaveGame("Mh60NativeState17");
                        GameDataSaveLoader.LoadGame("Mh60NativeState17");
                        return;
                    }
                    Check(mh60VerifiedOnLoad, "native MH-60 state verified before the first simulation tick and resumed through departure");
                }
                else if (mode == "native-cas")
                {
                    if (!casPrepared)
                    {
                        NativeCasChecks.Create(output, Check);
                        casPrepared = true;
                        GameDataSaveLoader.SaveGame("CasNativeState17");
                        GameDataSaveLoader.LoadGame("CasNativeState17");
                        return;
                    }
                    Check(casVerifiedOnLoad, "native CAS state verified before the first simulation tick");
                }
                else if (mode == "native-switchblade")
                {
                    if (!switchbladePrepared)
                    {
                        NativeSwitchbladeChecks.Create(output, Check);
                        switchbladePrepared = true;
                        GameDataSaveLoader.SaveGame("SwitchbladeNativeState17");
                        GameDataSaveLoader.LoadGame("SwitchbladeNativeState17");
                        return;
                    }
                    Check(switchbladeVerifiedOnLoad, "native Switchblade state verified before the first simulation tick");
                }
                else if (mode == "native-shotgun")
                {
                    if (!shotgunPrepared)
                    {
                        NativeShotgunChecks.Create(output, Check);
                        shotgunPrepared = true;
                        GameDataSaveLoader.SaveGame("ShotgunNativeState17");
                        GameDataSaveLoader.LoadGame("ShotgunNativeState17");
                        return;
                    }
                    Check(shotgunVerifiedOnLoad, "native shotgun state verified before the first simulation tick");
                }
                else if (mode == "native-cigarette")
                {
                    if (!cigarettePrepared)
                    {
                        NativeCigaretteChecks.Create(output, Check); cigarettePrepared = true;
                        GameDataSaveLoader.SaveGame("CigaretteNativeState17");
                        GameDataSaveLoader.LoadGame("CigaretteNativeState17"); return;
                    }
                    Check(cigaretteVerifiedOnLoad, "native cigarette state verified before first simulation tick");
                }
                else if (mode == "native-m47")
                {
                    if (!m47Prepared)
                    {
                        NativeM47Checks.Create(output, Check);
                        m47Prepared = true;
                        GameDataSaveLoader.SaveGame("M47NativeState17");
                        GameDataSaveLoader.LoadGame("M47NativeState17");
                        return;
                    }
                    Check(m47VerifiedOnLoad, "native M47 state verified before the first simulation tick");
                }
                else if (mode == "native-tow")
                {
                    if (!towPrepared)
                    {
                        NativeTowChecks.Create(output, Check);
                        towPrepared = true;
                        GameDataSaveLoader.SaveGame("TowNativeState17");
                        GameDataSaveLoader.LoadGame("TowNativeState17");
                        return;
                    }
                    Check(towVerifiedOnLoad, "native TOW state verified before the first simulation tick");
                }
                else if (mode == "native-grenades")
                {
                    if (!grenadePrepared)
                    {
                        NativeGrenadeChecks.Create(output, Check);
                        grenadePrepared = true;
                        GameDataSaveLoader.SaveGame("GrenadeNativeState17");
                        GameDataSaveLoader.LoadGame("GrenadeNativeState17");
                        return;
                    }
                    Check(grenadeVerifiedOnLoad, "native grenade state verified before the first simulation tick");
                }
                else if (mode == "native-lra7")
                {
                    if (!lra7Prepared)
                    {
                        NativeLra7Checks.Create(output, Check);
                        lra7Prepared = true;
                        GameDataSaveLoader.SaveGame("Lra7NativeState17");
                        GameDataSaveLoader.LoadGame("Lra7NativeState17");
                        return;
                    }
                    Check(lra7VerifiedOnLoad, "LRA-7 reloaded state verified before the first simulation tick");
                }
                else if (mode == "blancas-integration") BlancasIntegrationChecks.Run(Check);
                else if (mode == "native-runtime") NativeRuntimeChecks.Run(output, Check);
                else if (mode == "native-math") NativeMathChecks.Run(output, Check);
                else if (mode == "native-geometry") NativeGeometryChecks.Run(output, Check);
                else if (mode == "native-anti-air") NativeAntiAirChecks.Run(output, Check);
                else if (mode == "native-m167") NativeM167Checks.Run(output, Check);
                else if (mode == "native-support-helicopter") NativeSupportHelicopterChecks.Run(output, Check);
                else if (mode == "native-telegraph-ui") NativeWindowSubmissionChecks.Run(output, Check);
                else if (mode == "native-economy-ui") NativeWindowSubmissionChecks.RunEconomy(output, Check);
                else if (mode == "native-research-ui") NativeWindowSubmissionChecks.RunResearch(output, Check);
                else if (mode == "native-overlays") NativeWindowSubmissionChecks.RunOverlays(output, Check);
                else if (mode == "native-preview-images") NativePreviewImageChecks.Run(output, Check);
                else if (mode == "native-armor-ui") NativeWindowSubmissionChecks.RunArmor(output, Check);
                else if (mode == "native-weapon-ui") NativeWindowSubmissionChecks.RunWeapon(output, Check);
                else if (mode == "native-preemptive") NativePreemptiveChecks.Run(output, Check);
                else if (mode == "native-attachment-ui") NativeWindowSubmissionChecks.RunAttachment(output, Check);
                else if (mode == "main-state")
                {
                    if (!mainStatePrepared)
                    {
                        MainStateChecks.Create(Check);
                        mainStatePrepared = true;
                        GameDataSaveLoader.SaveGame("HelodraceState17");
                        Check(File.Exists(GenFilePaths.FilePathForSavedGame("HelodraceState17")), "new Main state saved");
                        GameDataSaveLoader.LoadGame("HelodraceState17");
                        return;
                    }
                    MainStateChecks.Verify(Check);
                }
#endif
#endif
                else throw new ArgumentException("Unknown migration case: " + mode);
                File.AppendAllText(Path.Combine(output, "checks.txt"), "COMPLETE\n");
                Quit(0);
            }
            catch (Exception error)
            {
                File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL " + error + "\n");
                Log.Error("Engine migration checks failed: " + error);
                Quit(1);
            }
        }

        private static void Quit(int code)
        {
#if NET10_0_OR_GREATER
            Waylith.EngineApplication.Instance.Quit(code);
#else
            UnityEngine.Application.Quit(code);
#endif
        }

        private void CreateFixture()
        {
            string[] names = { "BD_MorphineHigh", "BD_FentanylHigh", "BD_KetamineHigh", "BD_LaudanumHigh",
                "BD_SonataHigh", "BD_AmbienHigh", "BD_RestorilHigh", "BD_SonataDeepSleeper",
                "BD_AmbienDeepSleeper", "BD_RestorilDeepSleeper", "BD_Somnifacientstronghold",
                "BD_OpioidsAddiction", "BD_ZDrugsTolerance", "BD_MKultraConditioning",
                "BD_ModafinilAddiction", "BD_CaffeineAddiction" };
            for (int i = 0; i < names.Length; i++)
            {
                // Single fixture names are identifiers, not family names for relation generation.
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, canGeneratePawnRelations: false));
                pawn.Name = new NameSingle(Prefix + i);
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(Find.CurrentMap.Center, Find.CurrentMap, 15), Find.CurrentMap);
                Hediff hediff = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed(names[i]), pawn);
                hediff.Severity = .75f;
                pawn.health.AddHediff(hediff);
            }
            Check(Find.CurrentMap.mapPawns.AllPawnsSpawned.Count(p => p.Name?.ToStringFull.StartsWith(Prefix) == true) == names.Length,
                "fixture includes drug, dependence, tolerance, sleep and MKultra states");
        }

        private void WriteSnapshot()
        {
            var root = new XElement("fixture");
            foreach (Pawn pawn in Find.CurrentMap.mapPawns.AllPawnsSpawned.Where(p => p.Name?.ToStringFull.StartsWith(Prefix) == true))
            {
                var node = new XElement("pawn", new XAttribute("name", pawn.Name.ToStringFull));
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs.Where(h => h.def.defName.StartsWith("BD_")))
                {
                    var effect = new XElement("hediff", new XAttribute("def", hediff.def.defName), new XAttribute("severity", hediff.Severity));
                    var withComps = hediff as HediffWithComps;
                    if (withComps?.comps != null)
                        foreach (HediffComp comp in withComps.comps.Where(c => c.GetType().Namespace == "HelodBlancasDrug"))
                        {
                            var entry = new XElement("comp", new XAttribute("type", comp.GetType().FullName));
                            FieldInfo transitioned = comp.GetType().GetField("hasTransitioned", BindingFlags.Instance | BindingFlags.NonPublic);
                            if (transitioned != null) entry.Add(new XAttribute("hasTransitioned", (bool)transitioned.GetValue(comp)));
                            effect.Add(entry);
                        }
                    node.Add(effect);
                }
                foreach (Need need in pawn.needs.AllNeeds.Where(n => n.def.defName.StartsWith("BD_")))
                    node.Add(new XElement("need", new XAttribute("def", need.def.defName),
                        new XAttribute("type", need.GetType().FullName), new XAttribute("level", need.CurLevel)));
                root.Add(node);
            }
            new XDocument(root).Save(Path.Combine(output, "fixture.xml"));
        }

        private void VerifySnapshot()
        {
            XDocument snapshot = XDocument.Load(Path.Combine(output, "fixture.xml"));
            foreach (XElement subject in snapshot.Root.Elements("pawn"))
            {
                Pawn pawn = Find.CurrentMap.mapPawns.AllPawnsSpawned.SingleOrDefault(p => p.Name?.ToStringFull == (string)subject.Attribute("name"));
                Check(pawn != null, "legacy pawn restored: " + subject.Attribute("name").Value);
                foreach (XElement effect in subject.Elements("hediff"))
                {
                    Hediff actual = pawn.health.hediffSet.hediffs.SingleOrDefault(h => h.def.defName == (string)effect.Attribute("def"));
                    Check(actual != null, "legacy hediff restored: " + effect.Attribute("def").Value);
                    Check(Math.Abs(actual.Severity - (float)effect.Attribute("severity")) < .002f, "severity preserved: " + actual.def.defName);
                    foreach (XElement compState in effect.Elements("comp"))
                    {
                        HediffComp comp = (actual as HediffWithComps)?.comps.SingleOrDefault(c => c.GetType().FullName == (string)compState.Attribute("type"));
                        Check(comp != null, "serialized comp type preserved: " + compState.Attribute("type").Value);
                        if (compState.Attribute("hasTransitioned") != null)
                            Check((bool)comp.GetType().GetField("hasTransitioned", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(comp)
                                == (bool)compState.Attribute("hasTransitioned"), "sleep transition state preserved");
                    }
                }
                foreach (XElement savedNeed in subject.Elements("need"))
                {
                    Need need = pawn.needs.TryGetNeed(DefDatabase<NeedDef>.GetNamed((string)savedNeed.Attribute("def")));
                    Check(need != null && need.GetType().FullName == (string)savedNeed.Attribute("type"),
                        "legacy need type restored: " + savedNeed.Attribute("def").Value);
                    Check(Math.Abs(need.CurLevel - (float)savedNeed.Attribute("level")) < .002f,
                        "legacy need level preserved: " + need.def.defName);
                }
            }
        }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            File.AppendAllText(Path.Combine(output, "checks.txt"), "PASS " + message + "\n");
        }
    }
}
