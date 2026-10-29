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
        private string output, mode;

        public MigrationChecks(Game game) { }

        public override void GameComponentTick()
        {
            if (complete || Find.CurrentMap == null
                || !GenCommandLine.TryGetCommandLineArg("hdMigrationOutput", out output)) return;
            GenCommandLine.TryGetCommandLineArg("hdMigrationCase", out mode);
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
                File.WriteAllText(Path.Combine(output, "checks.txt"), "Engine migration: " + mode + "\n");
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
#endif
                else throw new ArgumentException("Unknown migration case: " + mode);
                File.AppendAllText(Path.Combine(output, "checks.txt"), "COMPLETE\n");
                UnityEngine.Application.Quit(0);
            }
            catch (Exception error)
            {
                File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL " + error + "\n");
                Log.Error("Engine migration checks failed: " + error);
                UnityEngine.Application.Quit(1);
            }
        }

        private void CreateFixture()
        {
            string[] names = { "BD_MorphineHigh", "BD_FentanylHigh", "BD_KetamineHigh", "BD_LaudanumHigh",
                "BD_SonataHigh", "BD_AmbienHigh", "BD_RestorilHigh", "BD_SonataDeepSleeper",
                "BD_AmbienDeepSleeper", "BD_RestorilDeepSleeper", "BD_Somnifacientstronghold",
                "BD_OpioidsAddiction", "BD_ZDrugsTolerance", "BD_MKultraConditioning" };
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
            }
        }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            File.AppendAllText(Path.Combine(output, "checks.txt"), "PASS " + message + "\n");
        }
    }
}
