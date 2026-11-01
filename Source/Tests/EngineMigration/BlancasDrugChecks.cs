using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using HelodBlancasDrug;
using RimWorld;
using Verse;

namespace Helodrace.Tests
{
    internal static class BlancasDrugChecks
    {
        internal const string FixtureSave = "BlancasDrugFixture16", RoundtripSave = "BlancasDrugRoundtrip17";
        private const string Prefix = "BlancasDose_";
        private static readonly FieldInfo Transitioned = typeof(HediffComp_SleepLogic).GetField("hasTransitioned", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool Equivalent(XElement actual, XElement expected)
        {
            var a = new XElement(actual); var e = new XElement(expected);
            string[] floatFields = { "rest", "food", "severity", "severityPerDay", "strength", "level" };
            foreach (var tree in new[] { a, e })
                foreach (var field in tree.DescendantsAndSelf().Attributes().Where(v => floatFields.Contains(v.Name.LocalName)))
                {
                    float value = float.Parse(field.Value, CultureInfo.InvariantCulture);
                    field.Value = BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString(CultureInfo.InvariantCulture);
                }
            return XNode.DeepEquals(a, e);
        }
        private static List<Pawn> Subjects() => Find.CurrentMap.mapPawns.AllPawnsSpawned.Concat(Find.CurrentMap.listerThings.AllThings.OfType<Corpse>().Select(c => c.InnerPawn))
            .Where(p => p.Name?.ToStringFull.StartsWith(Prefix, StringComparison.Ordinal) == true).Distinct().OrderBy(p => p.Name.ToStringFull, StringComparer.Ordinal).ToList();
        private static XElement State(Pawn pawn, string name)
        {
            var node = new XElement("pawn", new XAttribute("name", name), new XAttribute("dead", pawn.Dead),
                new XAttribute("rest", pawn.needs.rest.CurLevel.ToString("R", CultureInfo.InvariantCulture)), new XAttribute("food", pawn.needs.food.CurLevel.ToString("R", CultureInfo.InvariantCulture)));
            foreach (Hediff h in pawn.health.hediffSet.hediffs.OrderBy(h => h.def.defName, StringComparer.Ordinal).ThenBy(h => h.Part?.def.defName ?? "", StringComparer.Ordinal).ThenBy(h => h.Severity))
            {
                var entry = new XElement("hediff", new XAttribute("def", h.def.defName), new XAttribute("type", h.GetType().FullName),
                    new XAttribute("part", h.Part == null ? -1 : pawn.RaceProps.body.AllParts.IndexOf(h.Part)), new XAttribute("stage", h.CurStageIndex),
                    new XAttribute("severity", h.Severity.ToString("R", CultureInfo.InvariantCulture)), new XAttribute("age", h.ageTicks));
                var group = h.TryGetComp<HediffComp_ExclusiveGroup>(); var removal = h.TryGetComp<HediffComp_OnRemoval>(); var sleep = h.TryGetComp<HediffComp_SleepLogic>();
                var rate = h.TryGetComp<HediffComp_SeverityPerDay>(); var timer = h.TryGetComp<HediffComp_Disappears>();
                if (rate != null) entry.Add(new XAttribute("severityPerDay", rate.severityPerDay.ToString("R", CultureInfo.InvariantCulture)));
                if (timer != null) entry.Add(new XAttribute("ticksToDisappear", timer.ticksToDisappear));
                if (group != null) entry.Add(new XAttribute("strength", group.CurrentStrength.ToString("R", CultureInfo.InvariantCulture)));
                if (removal != null) entry.Add(new XAttribute("skipRemoval", removal.skipNextRemoval));
                if (sleep != null) entry.Add(new XAttribute("sleepType", sleep.GetType().FullName), new XAttribute("hasTransitioned", (bool)Transitioned.GetValue(sleep)));
                node.Add(entry);
            }
            foreach (Need n in pawn.needs.AllNeeds.Where(n => n.def.defName.StartsWith("BD_", StringComparison.Ordinal)).OrderBy(n => n.def.defName, StringComparer.Ordinal))
                node.Add(new XElement("need", new XAttribute("def", n.def.defName), new XAttribute("type", n.GetType().FullName), new XAttribute("level", n.CurLevel.ToString("R", CultureInfo.InvariantCulture))));
            return node;
        }
        private static Pawn Spawn(string cohort, string key, int index)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, fixedBiologicalAge: 25, forcedXenotype: XenotypeDefOf.Baseliner, canGeneratePawnRelations: false, allowAddictions: false));
            foreach (Hediff h in p.health.hediffSet.hediffs.ToArray()) p.health.RemoveHediff(h);
            p.story.traits.allTraits.Clear(); p.Name = new NameSingle(Prefix + cohort + "_" + key);
            GenSpawn.Spawn(p, Find.CurrentMap.Center + new IntVec3(index % 18 * 2 - 18, 0, index / 18 * 2 - 12), Find.CurrentMap);
            p.needs.rest.CurLevel = .3f; p.needs.food.CurLevel = .25f; return p;
        }
        private static void Dose(Pawn p, ThingDef def, int seed, Action<bool, string> check)
        {
            Thing drug = ThingMaker.MakeThing(def); Rand.PushState(seed);
            try { drug.Ingested(p, 0); }
            finally { Rand.PopState(); if (!drug.Destroyed) drug.Destroy(); }
            check(!p.health.hediffSet.hediffs.Any(h => h.TryGetComp<HediffComp_CompareSetNeedAndRemove>() != null || h.TryGetComp<HediffComp_CompareGiveHediffAndRemove>() != null), p.Name + " transient comparison scripts removed after " + def.defName);
        }
        private static void Removed(Pawn pawn, Hediff h, Action<bool, string> check)
        {
            var props = h.TryGetComp<HediffComp_OnRemoval>().Props;
            check(!pawn.health.hediffSet.hediffs.Contains(h), pawn.Name + " high removed");
            check(props.hediffToGive == null || pawn.health.hediffSet.hediffs.Count(e => e.def == props.hediffToGive) == 1, pawn.Name + " one configured recovery after actual removal");
            check(props.hediffsToRemove == null || props.hediffsToRemove.All(d => !pawn.health.hediffSet.HasHediff(d)), pawn.Name + " removal clears configured stimulant factor");
        }
        private static XDocument Exercise(string cohort, Action<bool, string> check)
        {
            var drugs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("BD_", StringComparison.Ordinal) && d.defName != "BD_MKultra" && d.ingestible != null && d.ingestible.outcomeDoers != null)
                .OrderBy(d => d.defName, StringComparer.Ordinal).ToList();
            check(drugs.Count == 13, "all 13 published non-MKultra ingestibles included");
            var trace = new XElement("drugActions"); int index = 0, natural = 0;
            foreach (ThingDef drug in drugs)
            {
                string key = "single-" + drug.defName; Pawn p = Spawn(cohort, key, index++);
                Dose(p, drug, 170800 + index, check); trace.Add(State(p, key + "-dose"));
                var removal = p.health.hediffSet.hediffs.Where(h => h.TryGetComp<HediffComp_OnRemoval>() != null).ToArray();
                foreach (Hediff h in removal) { p.health.RemoveHediff(h); Removed(p, h, check); }
                if (removal.Length > 0) trace.Add(State(p, key + "-removed"));
            }
            foreach (ThingDef first in drugs)
                foreach (ThingDef second in drugs)
                {
                    string key = "pair-" + first.defName + "-" + second.defName; Pawn p = Spawn(cohort, key, index++);
                    Dose(p, first, 170900 + index * 2, check); trace.Add(State(p, key + "-first"));
                    Dose(p, second, 170901 + index * 2, check); trace.Add(State(p, key + "-second"));
                }
            foreach (ThingDef drug in drugs)
            {
                var high = drug.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_GiveHediff>().Select(d => d.hediffDef)
                    .FirstOrDefault(d => d.comps?.Any(c => c is HediffCompProperties_OnRemoval) == true);
                if (high == null) continue;
                string key = "expiry-" + drug.defName; Pawn p = Spawn(cohort, key, index++);
                Dose(p, drug, 171400 + index, check); var effect = p.health.hediffSet.GetFirstHediffOfDef(high);
                effect.Severity = .0000001f; trace.Add(State(p, key + "-before"));
                int originalTick = Find.TickManager.TicksGame;
                try
                {
                    int step = 0;
                    while (!p.IsHashIntervalTick(200) && step < 200) Find.TickManager.DebugSetTicksGame(originalTick + ++step);
                    check(p.IsHashIntervalTick(200), p.Name + " actual 200-tick severity boundary selected");
                    p.health.HealthTick(); p.health.HealthTickInterval(1);
                }
                finally { Find.TickManager.DebugSetTicksGame(originalTick); }
                Removed(p, effect, check); trace.Add(State(p, key + "-after")); natural++;
            }
            check(index == 187 && trace.Elements().Count() == 366 && natural == 5, "13 singles, 169 ordered pairs and five natural expiry boundaries produced 187 subjects/366 state records");
            return new XDocument(trace);
        }
        internal static void Snapshot(string output, bool roundtrip)
            => new XDocument(new XElement("drugState", Subjects().Select(p => State(p, p.Name.ToStringFull)))).Save(Path.Combine(output, roundtrip ? "blancas-drug-roundtrip.xml" : "blancas-drug-state.xml"));
        internal static void Create(string output, Action<bool, string> check)
        { Exercise("legacy", check).Save(Path.Combine(output, "blancas-drug-actions.xml")); Snapshot(output, false); check(Subjects().Count == 187, "187 actual release drug subjects retained for legacy save"); }
        internal static void VerifyLoaded(string output, bool roundtrip, Action<bool, string> check)
        {
            var expected = XDocument.Load(Path.Combine(output, roundtrip ? "blancas-drug-roundtrip.xml" : "blancas-drug-state.xml")); var actual = Subjects();
            check(actual.Count == (roundtrip ? 374 : 187), "all drug subjects/corpses restored before simulation resumes");
            foreach (XElement p in expected.Root.Elements("pawn"))
            {
                Pawn pawn = actual.Single(v => v.Name.ToStringFull == (string)p.Attribute("name"));
                check(Equivalent(State(pawn, pawn.Name.ToStringFull), p), pawn.Name + " exact effect types/stages/severity/age, needs and custom Comp state restored");
            }
        }
        internal static void CompareCurrent(string output, Action<bool, string> check)
        {
            var current = Exercise("current", check); current.Save(Path.Combine(output, "blancas-drug-current-actions.xml"));
            var legacy = XDocument.Load(Path.Combine(output, "blancas-drug-actions.xml"));
            check(Equivalent(current.Root, legacy.Root), "all 366 actual drug action states match frozen 1.6 including ordered overlaps, recovery suppression, needs and expiry removal");
        }
    }
}
