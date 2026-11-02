using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using HelodBlancasDrug;
using RimWorld;
using Verse;

namespace Helodrace.Tests
{
    internal static class BlancasChronicChecks
    {
        internal const string FixtureSave = "BlancasChronicFixture16", RoundtripSave = "BlancasChronicRoundtrip17";
        private const string Prefix = "BlancasChronic_";
        private const int StartTick = 10000, SavedElapsed = 15000, EndElapsed = 180000;
        private sealed class Scenario
        {
            internal string Name, Kind, Definition, Recovery;
            internal Pawn Pawn;
        }
        private static XElement State(Pawn pawn, string name)
        {
            try
            {
                var state=BlancasDrugChecks.State(pawn,name);
                state.Add(new XAttribute("needsTracker",pawn.needs!=null));
                return state;
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("Chronic state "+name+": pawn="+(pawn!=null)
                    +", dead="+pawn?.Dead+", needs="+(pawn?.needs!=null)+", health="+(pawn?.health!=null),error);
            }
        }
        private static List<Pawn> Subjects() => Find.CurrentMap.mapPawns.AllPawnsSpawned
            .Concat(Find.CurrentMap.listerThings.AllThings.OfType<Corpse>().Select(c => c.InnerPawn))
            .Where(p => p.Name?.ToStringFull.StartsWith(Prefix, StringComparison.Ordinal) == true)
            .Distinct().OrderBy(p => p.Name.ToStringFull, StringComparer.Ordinal).ToList();
        private static void Dose(Pawn pawn, string drug)
        {
            Thing item = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(drug));
            try { item.Ingested(pawn, 0); }
            finally { if (!item.Destroyed) item.Destroy(); }
            if (pawn.health.hediffSet.hediffs.Any(h => h.TryGetComp<HediffComp_CompareGiveHediffAndRemove>() != null
                || h.TryGetComp<HediffComp_CompareSetNeedAndRemove>() != null))
                throw new InvalidOperationException("Chronic dose retained a transient comparison script.");
        }
        private static Pawn Spawn(string name, int index)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                fixedBiologicalAge:25, forcedXenotype:XenotypeDefOf.Baseliner, canGeneratePawnRelations:false, allowAddictions:false));
            foreach (Hediff h in pawn.health.hediffSet.hediffs.ToArray()) pawn.health.RemoveHediff(h);
            pawn.story.traits.allTraits.Clear(); pawn.Name = new NameSingle(Prefix + name);
            GenSpawn.Spawn(pawn, Find.CurrentMap.Center + new IntVec3(index % 10 * 2 - 10, 0, index / 10 * 2 - 6), Find.CurrentMap);
            pawn.needs.rest.CurLevel = .3f; pawn.needs.food.CurLevel = .25f;
            return pawn;
        }
        private static List<Scenario> CreateScenarios(Action<bool,string> check)
        {
            var scenarios = new List<Scenario>();
            foreach (ChemicalDef chemical in DefDatabase<ChemicalDef>.AllDefsListForReading
                .Where(d => d.defName.StartsWith("BD_", StringComparison.Ordinal)).OrderBy(d => d.defName, StringComparer.Ordinal))
            {
                scenarios.Add(new Scenario { Name="tolerance-"+chemical.defName, Kind="tolerance", Definition=chemical.toleranceHediff.defName });
                // Published Modafinil/Caffeine have inert placeholders despite the old default canBeAddicted flag.
                // Exercise the six actual withdrawal definitions, retaining all eight tolerance definitions.
                if (chemical.addictionHediff.chemicalNeed.fallPerDay > 0f)
                    foreach (string kind in new[] { "withdrawal", "cure" })
                        scenarios.Add(new Scenario { Name=kind+"-"+chemical.defName, Kind=kind, Definition=chemical.addictionHediff.defName });
            }
            foreach (ThingDef drug in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("BD_", StringComparison.Ordinal)
                && d.defName != "BD_MKultra" && d.ingestible?.outcomeDoers != null).OrderBy(d => d.defName, StringComparer.Ordinal))
            {
                scenarios.Add(new Scenario { Name="repeat-"+drug.defName, Kind="repeat", Definition=drug.defName });
                var removal = drug.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_GiveHediff>()
                    .Select(d => d.hediffDef).FirstOrDefault(d => d.comps?.Any(c => c is HediffCompProperties_OnRemoval) == true);
                if (removal != null)
                    scenarios.Add(new Scenario { Name="recovery-"+drug.defName, Kind="recovery", Definition=drug.defName,
                        Recovery=removal.CompProps<HediffCompProperties_OnRemoval>().hediffToGive.defName });
            }
            check(scenarios.Count == 38, "eight tolerance, six withdrawal, six cure, thirteen repeat-dose and five recovery scenarios included");
            for (int i=0;i<scenarios.Count;i++)
            {
                Scenario s=scenarios[i]; Rand.PushState(177000+i);
                try
                {
                    s.Pawn=Spawn(s.Name,i);
                    if (s.Kind == "repeat" || s.Kind == "recovery")
                    {
                        Dose(s.Pawn,s.Definition);
                        if (s.Kind == "recovery")
                        {
                            var high=s.Pawn.health.hediffSet.hediffs.Single(h => h.TryGetComp<HediffComp_OnRemoval>() != null);
                            s.Pawn.health.RemoveHediff(high);
                            check(s.Pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed(s.Recovery)),s.Name+" actual high removal creates recovery");
                        }
                    }
                    else
                    {
                        var h=HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed(s.Definition),s.Pawn);
                        h.Severity=s.Kind == "withdrawal" ? 1f : .04f; s.Pawn.health.AddHediff(h);
                        s.Pawn.needs.AddOrRemoveNeedsAsAppropriate();
                        if (h is Hediff_Addiction addiction)
                        {
                            check(addiction.Need != null,s.Name+" actual addiction creates its chemical need");
                            // Cure cohort remains supplied while its already-low addiction severity recovers.
                            addiction.Need.CurLevel=s.Kind == "withdrawal" ? .12f : 1f;
                        }
                    }
                }
                finally { Rand.PopState(); }
            }
            return scenarios;
        }
        private static void Advance(List<Scenario> scenarios, int from, int to, XElement trace)
        {
            for (int i=0;i<scenarios.Count;i++)
            {
                Scenario s=scenarios[i]; Pawn pawn=s.Pawn; Rand.PushState(178000+i*31+from);
                try
                {
                    for (int elapsed=from+1;elapsed<=to;elapsed++)
                    {
                        Find.TickManager.DebugSetTicksGame(StartTick+elapsed);
                        if (pawn.Dead) break; // Preserve terminal release behavior; never ingest or tick a corpse.
                        if (s.Kind == "repeat" && (elapsed == 60000 || elapsed == 120000)) Dose(pawn,s.Definition);
                        pawn.health.HealthTick(); pawn.health.HealthTickInterval(1);
                        // Only chemical needs advance. No AI, rest/food depletion or whole-world simulation is claimed.
                        if (pawn.IsHashIntervalTick(150))
                        {
                            pawn.needs.AddOrRemoveNeedsAsAppropriate();
                            foreach (Need need in pawn.needs.AllNeeds.Where(n => n is Need_Chemical).ToArray()) need.NeedInterval();
                        }
                    }
                    trace.Add(State(pawn,s.Name+"@"+to));
                }
                finally { Rand.PopState(); }
            }
            Find.TickManager.DebugSetTicksGame(StartTick+to);
        }
        internal static void Create(string output,Action<bool,string> check)
        {
            Find.TickManager.DebugSetTicksGame(StartTick);
            var scenarios=CreateScenarios(check);
            new XDocument(new XElement("scenarios",scenarios.Select(s => new XElement("scenario",new XAttribute("name",s.Name),
                new XAttribute("kind",s.Kind),new XAttribute("definition",s.Definition),new XAttribute("recovery",s.Recovery??"")))))
                .Save(Path.Combine(output,"blancas-chronic-scenarios.xml"));
            var trace=new XElement("initial",scenarios.Select(s => State(s.Pawn,s.Name+"@0")));
            Advance(scenarios,0,SavedElapsed,trace); new XDocument(trace).Save(Path.Combine(output,"blancas-chronic-initial.xml"));
            Snapshot(output,false); check(Subjects().Count==38,"38 chronic subjects retained at actual quarter-day legacy save boundary");
        }
        private static List<Scenario> ReadScenarios(string output)
        {
            var subjects=Subjects();
            return XDocument.Load(Path.Combine(output,"blancas-chronic-scenarios.xml")).Root.Elements("scenario")
                .Select(e => new Scenario { Name=(string)e.Attribute("name"),Kind=(string)e.Attribute("kind"),Definition=(string)e.Attribute("definition"),
                    Recovery=(string)e.Attribute("recovery"),Pawn=subjects.Single(p => p.Name.ToStringFull == Prefix+(string)e.Attribute("name")) }).ToList();
        }
        private static bool EquivalentProgression(XElement actual, XElement expected, string output)
        {
            // State restoration remains bit-exact. Continued core-engine float arithmetic is compared
            // separately: only a chemical need with the same category may differ by one ULP of its
            // original saved level. All other values, structure and state still use exact comparison.
            var compared=new XElement(actual);
            var saved=XDocument.Load(Path.Combine(output,"blancas-chronic-state.xml")).Root;
            var differences=new XElement("chemicalNeedRounding");
            foreach (XElement pawn in compared.Elements("pawn"))
            {
                string key=(string)pawn.Attribute("name");
                var reference=expected.Elements("pawn").SingleOrDefault(e => (string)e.Attribute("name")==key);
                string subject=Prefix+key.Substring(0,key.LastIndexOf('@'));
                var original=saved.Elements("pawn").SingleOrDefault(e => (string)e.Attribute("name")==subject);
                if(reference==null || original==null) continue;
                foreach (XElement need in pawn.Elements("need"))
                {
                    string def=(string)need.Attribute("def");
                    var other=reference.Elements("need").SingleOrDefault(e => (string)e.Attribute("def")==def);
                    var initial=original.Elements("need").SingleOrDefault(e => (string)e.Attribute("def")==def);
                    if(other==null || initial==null || (string)need.Attribute("type")!="RimWorld.Need_Chemical"
                        || (string)other.Attribute("type")!="RimWorld.Need_Chemical") continue;
                    float a=float.Parse((string)need.Attribute("level"),CultureInfo.InvariantCulture);
                    float e=float.Parse((string)other.Attribute("level"),CultureInfo.InvariantCulture);
                    float start=float.Parse((string)initial.Attribute("level"),CultureInfo.InvariantCulture);
                    int bits=BitConverter.ToInt32(BitConverter.GetBytes(start),0);
                    float next=BitConverter.ToSingle(BitConverter.GetBytes(bits+1),0);
                    double budget=(double)next-start, error=Math.Abs((double)a-e);
                    int Category(float value) => value>.1f?2:value>.01f?1:0;
                    if(error==0 || double.IsNaN(error) || error>budget || budget<=0 || Category(a)!=Category(e)) continue;
                    differences.Add(new XElement("difference",new XAttribute("state",key),new XAttribute("need",def),
                        new XAttribute("expected",e.ToString("R",CultureInfo.InvariantCulture)),new XAttribute("actual",a.ToString("R",CultureInfo.InvariantCulture)),
                        new XAttribute("absoluteError",error.ToString("R",CultureInfo.InvariantCulture)),new XAttribute("savedLevelUlp",budget.ToString("R",CultureInfo.InvariantCulture))));
                    need.SetAttributeValue("level",(string)other.Attribute("level"));
                }
            }
            new XDocument(differences).Save(Path.Combine(output,"blancas-chronic-numeric-differences.xml"));
            return BlancasDrugChecks.Equivalent(compared,expected);
        }
        internal static void Continue(string output,bool baseline,Action<bool,string> check)
        {
            int tick=Find.TickManager.TicksGame;
            bool completed=false;
            try
            {
                var scenarios=ReadScenarios(output); var trace=new XElement("progression"); int from=SavedElapsed;
                foreach (int to in new[] { 30000,60000,120000,EndElapsed }) { Advance(scenarios,from,to,trace); from=to; }
                foreach (Scenario s in scenarios)
                {
                    if(s.Kind=="withdrawal")
                    {
                        if(s.Definition=="BD_OpioidsAddiction")
                        {
                            check(s.Pawn.Dead,s.Name+" preserves the release lethal withdrawal path (negative consciousness cap; separate balance defect)");
                            continue;
                        }
                        var addiction=(Hediff_Addiction)s.Pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed(s.Definition));
                        check(addiction!=null && addiction.CurStageIndex==1 && addiction.Need.CurCategory==DrugDesireCategory.Withdrawal,
                            s.Name+" naturally reaches withdrawal while addiction persists");
                    }
                    else if(s.Kind=="cure") check(!s.Pawn.Dead && !s.Pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed(s.Definition)),s.Name+" supplied need permits natural addiction recovery/removal");
                    else if(s.Kind=="tolerance")
                    {
                        var h=s.Pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed(s.Definition));
                        check(h==null || h.Severity<.04f,s.Name+" tolerance decreases through actual health intervals");
                    }
                    else if(s.Kind=="recovery") check(!s.Pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed(s.Recovery)),s.Name+" recovery effect naturally expires");
                }
                new XDocument(trace).Save(Path.Combine(output,baseline?"blancas-chronic-expected.xml":"blancas-chronic-current.xml"));
                if(!baseline) check(EquivalentProgression(trace,XDocument.Load(Path.Combine(output,"blancas-chronic-expected.xml")).Root,output),
                    "152 chronic continuation states preserve exact health/Comp state; core chemical needs keep category and stay within one saved-level ULP (separate drift report)");
                completed=true;
            }
            // The next save represents the end of the simulated timeline, including absolute ingestion times.
            // The original quarter-day 1.6 save was written before continuation and remains untouched.
            finally { Find.TickManager.DebugSetTicksGame(completed ? StartTick+EndElapsed : tick); }
        }
        internal static void Snapshot(string output,bool roundtrip)
            => new XDocument(new XElement("chronicState",Subjects().Select(p => State(p,p.Name.ToStringFull))))
                .Save(Path.Combine(output,roundtrip?"blancas-chronic-roundtrip.xml":"blancas-chronic-state.xml"));
        internal static void VerifyLoaded(string output,bool roundtrip,Action<bool,string> check)
        {
            var subjects=Subjects(); check(subjects.Count==38,"all 38 chronic subjects restored before simulation resumes");
            foreach (var e in XDocument.Load(Path.Combine(output,roundtrip?"blancas-chronic-roundtrip.xml":"blancas-chronic-state.xml")).Root.Elements("pawn"))
            {
                Pawn p=subjects.Single(v => v.Name.ToStringFull==(string)e.Attribute("name"));
                check(BlancasDrugChecks.Equivalent(State(p,p.Name.ToStringFull),e),p.Name+" exact chronic health/needs/types/Comp state restored");
            }
        }
    }
}
