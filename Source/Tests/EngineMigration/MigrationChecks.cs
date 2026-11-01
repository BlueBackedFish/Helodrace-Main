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
        private bool mainStatePrepared;
        private bool blancasGenerationPrepared;
        private bool lra7Prepared;
        private bool grenadePrepared;
        private bool towPrepared;
        private bool m47Prepared;
        private bool shotgunPrepared;
#if HELODRACE_MAIN_CHECKS
        private bool lra7VerifiedOnLoad;
        private bool grenadeVerifiedOnLoad;
        private bool towVerifiedOnLoad;
        private bool m47VerifiedOnLoad;
        private bool shotgunVerifiedOnLoad;
#endif
        private string output, mode;

        public MigrationChecks(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref mainStatePrepared, "migrationMainStatePrepared", false);
            Scribe_Values.Look(ref blancasGenerationPrepared, "migrationBlancasGenerationPrepared", false);
            Scribe_Values.Look(ref lra7Prepared, "migrationLra7Prepared", false);
            Scribe_Values.Look(ref grenadePrepared, "migrationGrenadePrepared", false);
            Scribe_Values.Look(ref towPrepared, "migrationTowPrepared", false);
            Scribe_Values.Look(ref m47Prepared, "migrationM47Prepared", false);
            Scribe_Values.Look(ref shotgunPrepared, "migrationShotgunPrepared", false);
        }

#if HELODRACE_MAIN_CHECKS
        public override void LoadedGame()
        {
            if (!GenCommandLine.TryGetCommandLineArg("hdMigrationCase", out string activeCase)
                || !((lra7Prepared && activeCase == "native-lra7") || (grenadePrepared && activeCase == "native-grenades") || (towPrepared && activeCase == "native-tow") || (m47Prepared && activeCase == "native-m47") || (shotgunPrepared && activeCase == "native-shotgun"))
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
                else
                {
                    NativeShotgunChecks.Verify(output, Check);
                    shotgunVerifiedOnLoad = true;
                }
            }
            catch (Exception error)
            {
                complete = true;
                File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL " + error + "\n");
                Log.Error("Engine migration checks failed: " + error);
                Quit(1);
            }
        }

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
            if (mode == "load" && !requestedLoad)
            {
                requestedLoad = true;
                complete = true;
                GameDataSaveLoader.LoadGame(SaveName);
                return;
            }
            complete = true;
            try
            {
                if ((mode != "main-state" || !mainStatePrepared) && (mode != "blancas-generation" || !blancasGenerationPrepared)
                    && (mode != "native-lra7" || !lra7Prepared) && (mode != "native-grenades" || !grenadePrepared) && (mode != "native-tow" || !towPrepared) && (mode != "native-m47" || !m47Prepared) && (mode != "native-shotgun" || !shotgunPrepared))
                    File.WriteAllText(Path.Combine(output, "checks.txt"), "Engine migration: " + mode + "\n");
                else File.AppendAllText(Path.Combine(output, "checks.txt"), "Loaded new 1.7 state\n");
                Check(DefDatabase<ThingDef>.GetNamedSilentFail("BD_Ketamine") != null, "Blancas drug definitions loaded");
                Check(DefDatabase<HediffDef>.GetNamedSilentFail("BD_DeepSleep") != null, "Blancas sleep definitions loaded");
                Assembly drugAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "HelodBlancasDrug");
                Check(drugAssembly != null, "Blancas versioned assembly: " + drugAssembly.Location);
                if (mode == "baseline")
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
