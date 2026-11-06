using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests
{
    // Saves taken by the frozen 1.6 release while stimulant effects are in their crash stage or about to
    // cross into it. 1.7 splits the crash into its own hediff; loading must convert in place and the
    // continued timeline must match the 1.6 reference exactly in release form.
    internal static class BlancasPhaseChecks
    {
        internal const string FixtureSave = "BlancasPhaseFixture16", RoundtripSave = "BlancasPhaseRoundtrip17";
        private const string Prefix = "BlancasPhase_";
        private const int StartTick = 20000, EventElapsed = 1000;
        private static readonly int[] Checkpoints = { 1, 999, 1000, 2000, 6000, 20000, 60000 };
        private static readonly string[] Drugs = { "Desoxyn", "Benzedrine", "Dexedrine", "Modafinil", "CocaineCola" };
        private sealed class Scenario { internal string Name, Kind, Drug; internal Pawn Pawn; }

        private static List<Pawn> Subjects() => Find.CurrentMap.mapPawns.AllPawnsSpawned
            .Concat(Find.CurrentMap.listerThings.AllThings.OfType<Corpse>().Select(c => c.InnerPawn))
            .Where(p => p.Name?.ToStringFull.StartsWith(Prefix, StringComparison.Ordinal) == true)
            .Distinct().OrderBy(p => p.Name.ToStringFull, StringComparer.Ordinal).ToList();

        private static List<Scenario> Plan()
        {
            var list = new List<Scenario>();
            foreach (string drug in Drugs)
                foreach (string kind in new[] { "crash", "boundary", "redose", "deepsleep", "small", "other" })
                    if (kind != "other" || drug != "Modafinil") list.Add(new Scenario { Name = kind + "-" + drug, Kind = kind, Drug = drug });
            return list;
        }

        private static HediffDef Effect(string drug) => DefDatabase<HediffDef>.GetNamed("BD_" + drug + "High");
        private static float Threshold(string drug)
        {
            if (BlancasDrugChecks.Phases().TryGetValue("BD_" + drug + "High", out var phase)) return phase.Threshold;
            return Effect(drug).stages[1].minSeverity; // frozen release: the high stage starts at the crash threshold
        }

        private static void Dose(Pawn pawn, string drug)
        {
            Thing item = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("BD_" + drug));
            try { item.Ingested(pawn, 0); }
            finally { if (!item.Destroyed) item.Destroy(); }
        }

        private static void Event(Scenario s)
        {
            if (s.Kind == "redose") Dose(s.Pawn, s.Drug);
            else if (s.Kind == "other") Dose(s.Pawn, s.Drug == "Desoxyn" ? "Benzedrine" : "Desoxyn");
            else if (s.Kind == "small")
            {
                // A small external addition: the release merged it into the crash stage of the same hediff.
                Hediff h = HediffMaker.MakeHediff(Effect(s.Drug), s.Pawn); h.Severity = .05f; s.Pawn.health.AddHediff(h);
            }
        }

        internal static void Create(string output, Action<bool, string> check)
        {
            Find.TickManager.DebugSetTicksGame(StartTick);
            var plan = Plan(); int index = 0;
            foreach (Scenario s in plan)
            {
                Rand.PushState(179000 + index);
                try
                {
                    Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                        fixedBiologicalAge: 25, forcedXenotype: XenotypeDefOf.Baseliner, canGeneratePawnRelations: false, allowAddictions: false));
                    foreach (Hediff h in p.health.hediffSet.hediffs.ToArray()) p.health.RemoveHediff(h);
                    p.story.traits.allTraits.Clear(); p.Name = new NameSingle(Prefix + s.Name);
                    GenSpawn.Spawn(p, Find.CurrentMap.Center + new IntVec3(index % 10 * 2 - 10, 0, index / 10 * 2 - 4), Find.CurrentMap);
                    p.needs.rest.CurLevel = .3f; p.needs.food.CurLevel = .25f;
                    Dose(p, s.Drug);
                    float t = Threshold(s.Drug);
                    p.health.hediffSet.GetFirstHediffOfDef(Effect(s.Drug)).Severity = s.Kind == "boundary" ? t + .0004f : t * .5f;
                    if (s.Kind == "deepsleep") { Hediff sleep = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("BD_DeepSleep"), p); sleep.Severity = 1f; p.health.AddHediff(sleep); }
                }
                finally { Rand.PopState(); }
                index++;
            }
            check(Subjects().Count == 29, "29 actual release crash/boundary subjects prepared");
            check(Subjects().All(p => p.health.hediffSet.hediffs.Any(h => h.def.defName.EndsWith("High", StringComparison.Ordinal) && h.def.defName.StartsWith("BD_", StringComparison.Ordinal))),
                "every release subject keeps its stimulant effect in the saved state");
            Snapshot(output, false);
        }

        internal static void Snapshot(string output, bool roundtrip)
            => new XDocument(new XElement("phaseState", Subjects().Select(p => BlancasDrugChecks.State(p, p.Name.ToStringFull))))
                .Save(Path.Combine(output, roundtrip ? "blancas-phase-roundtrip.xml" : "blancas-phase-state.xml"));

        internal static void VerifyLoaded(string output, bool roundtrip, Action<bool, string> check)
        {
            var subjects = Subjects();
            check(subjects.Count == 29, "all 29 phase subjects restored");
            foreach (XElement e in XDocument.Load(Path.Combine(output, roundtrip ? "blancas-phase-roundtrip.xml" : "blancas-phase-state.xml")).Root.Elements("pawn"))
            {
                Pawn p = subjects.Single(v => v.Name.ToStringFull == (string)e.Attribute("name"));
                check(BlancasDrugChecks.Equivalent(BlancasDrugChecks.State(p, p.Name.ToStringFull), e), p.Name + " exact saved state restored in release form");
            }
        }

        // Item info entries computed from the 1.7 Defs; values are the documented release durations/factors.
        internal static void VerifyDrugInfo(Action<bool, string> check)
        {
            var expected = new Dictionary<string, string[]> {
                ["BD_Desoxyn"] = new[] { "48h", "38.4h", "+15", "-30", "3" }, ["BD_Benzedrine"] = new[] { "24h", "19.2h", "+8", "-15", "2" },
                ["BD_Dexedrine"] = new[] { "24h", "19.2h", "0", "2" }, ["BD_Modafinil"] = new[] { "12h", "9.6h", "2" },
                ["BD_CocaineCola"] = new[] { "9.6h", "19.2h", "+12", "-6", "2.5" }, ["BD_Cola"] = new[] { "7.2h", "+3" },
                ["BD_Ambien"] = new[] { "12h", "2" }, ["BD_Sonata"] = new[] { "8h", "1" }, ["BD_Restoril"] = new[] { "24h", "4" },
                ["BD_Morphine"] = new[] { "12h", "20%", "90%" }, ["BD_Fentanyl"] = new[] { "8h", "0%", "70%" },
                ["BD_Ketamine"] = new[] { "9.6h", "10%", "80%" }, ["BD_Laudanum"] = new[] { "6h", "45%", "95%" } };
            foreach (var pair in expected)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamed(pair.Key);
                var request = StatRequest.For(def, null);
                var entries = def.SpecialDisplayStats(request).Where(e => e.category == StatCategoryDefOf.Drug).ToList();
                var values = entries.Select(e => e.ValueString).ToList();
                check(pair.Value.All(values.Contains), def.defName + " info entries " + string.Join(" | ", values) + " include " + string.Join(", ", pair.Value));
                check(entries.All(e => !string.IsNullOrEmpty(e.LabelCap) && !e.LabelCap.Contains("BD_") && !e.ValueString.Contains("BD_")
                    && !e.GetExplanationText(request).Contains("BD_")), def.defName + " info entry labels and explanations are translated");
            }
        }

        // Called before the first simulation tick after a 1.7 load.
        internal static void VerifyConverted(Action<bool, string> check)
        {
            VerifyDrugInfo(check);
            foreach (Pawn p in Subjects())
            {
                var left = BlancasDrugChecks.UnconvertedEffects(p).ToList();
                check(left.Count == 0, p.Name + " no effect remains below its crash threshold before the first tick " + string.Join(",", left));
                string drug = p.Name.ToStringFull.Substring(p.Name.ToStringFull.IndexOf('-') + 1);
                bool crash = !p.Name.ToStringFull.StartsWith(Prefix + "boundary", StringComparison.Ordinal);
                var crashDef = DefDatabase<HediffDef>.GetNamed("BD_" + drug + "Crash");
                check(p.health.hediffSet.HasHediff(crashDef) == crash && p.health.hediffSet.HasHediff(Effect(drug)) == !crash,
                    p.Name + (crash ? " saved crash stage loaded as the crash hediff" : " saved high stays the effect hediff"));
                if (crash)
                {
                    // Position preserved: the crash takes the effect's place right after the dose.
                    check(p.health.hediffSet.hediffs.IndexOf(p.health.hediffSet.GetFirstHediffOfDef(crashDef)) == 0, p.Name + " crash keeps the effect's list position");
                }
            }
        }

        internal static void Continue(string output, bool baseline, Action<bool, string> check)
        {
            int tick = Find.TickManager.TicksGame; bool completed = false;
            try
            {
                var subjects = Subjects();
                var plan = Plan(); foreach (Scenario s in plan) s.Pawn = subjects.Single(p => p.Name.ToStringFull == Prefix + s.Name);
                var trace = new XElement("progression");
                for (int i = 0; i < plan.Count; i++)
                {
                    Scenario s = plan[i]; int from = 0;
                    foreach (int to in Checkpoints)
                    {
                        Rand.PushState(180000 + i * 31 + from);
                        try
                        {
                            for (int elapsed = from + 1; elapsed <= to; elapsed++)
                            {
                                Find.TickManager.DebugSetTicksGame(StartTick + elapsed);
                                if (s.Pawn.Dead) break;
                                if (elapsed == EventElapsed) Event(s);
                                s.Pawn.health.HealthTick(); s.Pawn.health.HealthTickInterval(1);
                            }
                        }
                        finally { Rand.PopState(); }
                        trace.Add(BlancasDrugChecks.State(s.Pawn, s.Name + "@" + to));
                        if (!baseline)
                        {
                            var left = BlancasDrugChecks.UnconvertedEffects(s.Pawn).ToList();
                            check(left.Count == 0, s.Name + "@" + to + " no live effect below its crash threshold " + string.Join(",", left));
                        }
                        from = to;
                    }
                }
                new XDocument(trace).Save(Path.Combine(output, baseline ? "blancas-phase-expected.xml" : "blancas-phase-current.xml"));
                if (!baseline)
                {
                    var expected = XDocument.Load(Path.Combine(output, "blancas-phase-expected.xml")).Root;
                    foreach (XElement state in trace.Elements("pawn"))
                    {
                        XElement reference = expected.Elements("pawn").Single(e => (string)e.Attribute("name") == (string)state.Attribute("name"));
                        check(BlancasDrugChecks.Equivalent(state, reference), (string)state.Attribute("name") + " continued state matches the frozen 1.6 timeline");
                    }
                    check(trace.Elements("pawn").Count() == 29 * Checkpoints.Length, "29 subjects x 7 checkpoints compared");
                }
                completed = true;
            }
            finally { Find.TickManager.DebugSetTicksGame(completed ? StartTick + Checkpoints[Checkpoints.Length - 1] : tick); }
        }
    }
}
