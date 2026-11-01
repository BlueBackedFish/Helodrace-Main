using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using HelodBlancasDrug;
using RimWorld;
using Verse;

namespace Helodrace.Tests
{
    // Shared source runs the shipped 1.6 and 1.7 modules through game entry points.
    internal static class BlancasMkultraChecks
    {
        internal const string FixtureSave = "BlancasMkultraFixture16", RoundtripSave = "BlancasMkultraRoundtrip17";
        private const string Prefix = "BlancasMK_";
        private static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        private static List<Pawn> Subjects() => Find.CurrentMap.mapPawns.AllPawnsSpawned
            .Concat(Find.CurrentMap.listerThings.AllThings.OfType<Corpse>().Select(c => c.InnerPawn))
            .Where(p => p.Name?.ToStringFull.StartsWith(Prefix, StringComparison.Ordinal) == true).Distinct()
            .OrderBy(p => p.Name.ToStringFull, StringComparer.Ordinal).ToList();
        private static string Part(Pawn p, BodyPartRecord part) => part == null ? "none" : p.RaceProps.body.AllParts.IndexOf(part) + ":" + part.def.defName;
        private static XElement Effect(Pawn p, Hediff h) => new XElement("hediff", new XAttribute("def", h.def.defName), new XAttribute("type", h.GetType().FullName),
            new XAttribute("part", Part(p, h.Part)), new XAttribute("severity", h.Severity.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("age", h.ageTicks), new XAttribute("permanent", h.TryGetComp<HediffComp_GetsPermanent>()?.IsPermanent ?? false));
        private static XElement Patient(Pawn pawn, string name)
        {
            return new XElement("pawn", new XAttribute("name", name), new XAttribute("dead", pawn.Dead), new XAttribute("player", pawn.Faction == Faction.OfPlayer),
                new XAttribute("faction", pawn.Faction?.def.defName ?? "none"), new XAttribute("prisoner", pawn.IsPrisoner),
                pawn.health.hediffSet.hediffs.OrderBy(h => h.def.defName, StringComparer.Ordinal).ThenBy(h => Part(pawn, h.Part), StringComparer.Ordinal).ThenBy(h => h.Severity).Select(h => Effect(pawn, h)));
        }
        internal static void Snapshot(string output, bool roundtrip)
        { new XDocument(new XElement("mkultra", Subjects().Select(p => Patient(p, p.Name.ToStringFull)))).Save(Path.Combine(output, roundtrip ? "blancas-mkultra-roundtrip.xml" : "blancas-mkultra-state.xml")); }
        private static XDocument Exercise(string cohort, Action<bool, string> check)
        {
            Map map = Find.CurrentMap; var result = new XElement("actions");
            Faction foreign = Find.FactionManager.AllFactions.First(f => f.def == FactionDefOf.OutlanderCivil && !f.IsPlayer);
            int originalGoodwill = foreign.PlayerGoodwill;
            Pawn doctor = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, fixedBiologicalAge: 35, forcedXenotype: XenotypeDefOf.Baseliner, canGeneratePawnRelations: false, allowAddictions: false));
            GenSpawn.Spawn(doctor, map.Center + new IntVec3(-15, 0, -15), map);
            int index = 0;
            try
            {
                foreach (string route in new[] { "ingestion", "surgery" })
                    foreach (string status in new[] { "prisoner", "foreign", "colonist" })
                        foreach (int eyes in new[] { 2, 1, 0 })
                        {
                            int caseIndex = index++;
                            Faction originalFaction = status == "colonist" ? Faction.OfPlayer : foreign;
                            Pawn patient = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, originalFaction, fixedBiologicalAge: 25, forcedXenotype: XenotypeDefOf.Baseliner, canGeneratePawnRelations: false, allowAddictions: false));
                            foreach (Hediff initial in patient.health.hediffSet.hediffs.ToArray()) patient.health.RemoveHediff(initial);
                            check(patient.health.hediffSet.hediffs.Count == 0, route + " " + status + " healthy initial state without generated scars/disease");
                            string key = route + "-" + status + "-eyes" + eyes;
                            patient.Name = new NameSingle(Prefix + cohort + "_" + key);
                            GenSpawn.Spawn(patient, map.Center + new IntVec3(caseIndex % 6 * 3 - 9, 0, caseIndex / 6 * 3 - 5), map);
                            if (status == "prisoner") patient.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                            var eyeParts = patient.RaceProps.body.AllParts.Where(p => p.def == BodyPartDefOf.Eye).ToList();
                            foreach (BodyPartRecord part in eyeParts.Take(2 - eyes)) patient.health.AddHediff(HediffDefOf.MissingBodyPart, part);
                            check(patient.RaceProps.body.AllParts.Count(p => p.def == BodyPartDefOf.Eye && !patient.health.hediffSet.PartIsMissing(p)) == eyes, key + " actual missing-eye setup");
                            BodyPartRecord brain = patient.health.hediffSet.GetBrain();
                            int doses = status == "colonist" && eyes == 2 ? 2 : 1;
                            for (int dose = 0; dose < doses; dose++)
                            {
                                foreign.TryAffectGoodwillWith(Faction.OfPlayer, -foreign.PlayerGoodwill, false, false);
                                int goodwill = foreign.PlayerGoodwill;
                                string recruitmentLabel = "LetterLabelMKultraRecruit".Translate(patient.LabelShortCap).ToString();
                                var lettersBefore = Find.LetterStack.LettersListForReading.ToArray();
                                int recruitmentBefore = lettersBefore.Count(l => l.Label.ToString() == recruitmentLabel);
                                Thing drug = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("BD_MKultra"));
                                Rand.PushState(160701 + caseIndex * 10 + dose);
                                try
                                {
                                    if (route == "ingestion") drug.Ingested(patient, 0);
                                    else
                                    {
                                        var recipe = DefDatabase<RecipeDef>.GetNamed("BD_InjectMKultra");
                                        check(recipe.Worker is RecipeWorker_MKultraInjection, "published MKultra surgery worker resolved");
                                        recipe.Worker.ApplyOnPawn(patient, eyeParts.FirstOrDefault(p => !patient.health.hediffSet.PartIsMissing(p)), doctor, new List<Thing> { drug }, null);
                                    }
                                }
                                finally { Rand.PopState(); if (!drug.Destroyed) drug.Destroy(); }
                                int recruitmentLetters = Find.LetterStack.LettersListForReading.Count(l => l.Label.ToString() == recruitmentLabel) - recruitmentBefore;
                                var otherLetters = Find.LetterStack.LettersListForReading.Except(lettersBefore).Where(l => l.Label.ToString() != recruitmentLabel).Select(l => l.Label.ToString()).ToArray();
                                if (otherLetters.Length > 0) check(true, key + " additional core letters: " + string.Join(" | ", otherLetters));
                                bool expectedPlayer = status != "foreign";
                                check(patient.Faction == (expectedPlayer ? Faction.OfPlayer : foreign) && !patient.IsPrisoner, key + " dose " + dose + " recruitment respects prisoner status");
                                check(patient.health.hediffSet.hediffs.Count(h => h.def.defName == "BD_MKultraConditioning") == 1, key + " dose " + dose + " has one conditioning effect");
                                var scars = patient.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(h => h.def == HediffDefOf.Cut).ToList();
                                check(scars.All(h => h.TryGetComp<HediffComp_GetsPermanent>()?.IsPermanent == true), key + " dose " + dose + " injected injuries remain permanent");
                                if (dose == 0)
                                {
                                    check(scars.Where(h => h.Part == brain).Sum(h => h.Severity) == 5
                                        && scars.Where(h => h.Part?.def == BodyPartDefOf.Eye).Sum(h => h.Severity) == (eyes > 0 ? 3 : 0), key + " brain and intact-eye injury severity matches release contract");
                                    check(recruitmentLetters == (status == "prisoner" ? 1 : 0), key + " recruitment letter count");
                                }
                                var action = Patient(patient, key + "-dose" + dose);
                                action.SetAttributeValue("letters", recruitmentLetters);
                                action.SetAttributeValue("goodwill", foreign.PlayerGoodwill - goodwill); result.Add(action);
                            }
                        }
                check(index == 18 && result.Elements().Count() == 20, "both MKultra entry points cover 18 body/social cases and 20 first/repeated doses");
            }
            finally { foreign.TryAffectGoodwillWith(Faction.OfPlayer, originalGoodwill - foreign.PlayerGoodwill, false, false); if (!doctor.Destroyed) doctor.Destroy(); }
            return new XDocument(result);
        }
        internal static void Create(string output, Action<bool, string> check)
        {
            Exercise("legacy", check).Save(Path.Combine(output, "blancas-mkultra-actions.xml"));
            Snapshot(output, false); check(Subjects().Count == 18, "18 conditioned release subjects including corpses are present for legacy save");
        }
        internal static void CompareCurrent(string output, Action<bool, string> check)
        {
            XDocument current = Exercise("current", check); current.Save(Path.Combine(output, "blancas-mkultra-current-actions.xml"));
            XDocument legacy = XDocument.Load(Path.Combine(output, "blancas-mkultra-actions.xml"));
            // The module deliberately uses the host's shared Verse.Rand. Recruitment
            // and brain/death callbacks run in different surrounding pawn populations;
            // a dose seed does not promise identical left/right eye choices across those
            // contexts. Keep raw records, validate the allowed eye set, and normalize only
            // fresh Cut eye identity. Saved injuries are compared without normalization.
            var eyeParts = DefDatabase<ThingDef>.GetNamed("Human").race.body.AllParts
                .Where(p => p.def == BodyPartDefOf.Eye).Select(p => DefDatabase<ThingDef>.GetNamed("Human").race.body.AllParts.IndexOf(p) + ":Eye").ToArray();
            foreach (var document in new[] { legacy, current })
                foreach (var action in document.Root.Elements("pawn"))
                {
                    string key = (string)action.Attribute("name"); int count = key.Contains("eyes0-") ? 0 : key.Contains("eyes1-") ? 1 : 2;
                    var allowed = eyeParts.Skip(2 - count).ToArray();
                    check(action.Elements("hediff").Where(h => (string)h.Attribute("def") == "Cut" && ((string)h.Attribute("part")).EndsWith(":Eye", StringComparison.Ordinal))
                        .All(h => allowed.Contains((string)h.Attribute("part"))), "fresh MKultra injury targets only an initially intact eye: " + key);
                }
            int differentEyes = legacy.Root.Elements().Zip(current.Root.Elements(), (a, b) => XNode.DeepEquals(a, b) ? 0 : 1).Sum();
            var expected = new XDocument(legacy); var actual = new XDocument(current);
            foreach (var document in new[] { expected, actual })
                foreach (var h in document.Descendants("hediff").Where(h => (string)h.Attribute("def") == "Cut" && ((string)h.Attribute("part")).EndsWith(":Eye", StringComparison.Ordinal))) h.SetAttributeValue("part", "intact-random-eye");
            check(XNode.DeepEquals(actual.Root, expected.Root), "actual 1.7 MKultra actions match frozen 1.6 recruitment, own letters, goodwill, conditioning, fixed body parts, injury severity/permanence and repeated-dose outcomes; only fresh random eye laterality is canonicalized");
            File.WriteAllText(Path.Combine(output, "blancas-mkultra-comparison.txt"), "20 actions compared; fresh random eye choice differs in " + differentEyes + " cases. Raw records retained; loaded injury locations remain exact.\n");
        }
        internal static void VerifyLoaded(string output, bool roundtrip, Action<bool, string> check)
        {
            var saved = XDocument.Load(Path.Combine(output, roundtrip ? "blancas-mkultra-roundtrip.xml" : "blancas-mkultra-state.xml"));
            var actual = Subjects(); check(actual.Count == (roundtrip ? 36 : 18), "all conditioned MKultra subjects and corpses restored before the first simulation tick");
            foreach (XElement p in saved.Root.Elements("pawn"))
            {
                Pawn patient = actual.Single(a => a.Name.ToStringFull == (string)p.Attribute("name"));
                XElement current = Patient(patient, patient.Name.ToStringFull);
                check(XNode.DeepEquals(current, p), patient.Name + " faction/prisoner/death state and all effect type/part/severity/age/permanence values preserved");
                var effects = patient.health.hediffSet.hediffs.Where(h => h.def.defName == "BD_MKultraConditioning").ToList();
                check(effects.Count == 1, patient.Name + " conditioning not duplicated by load callbacks");
            }
        }
    }
}
