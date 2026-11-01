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
using Verse.AI;

namespace Helodrace.Tests
{
    // Built against the frozen 1.6 release and current 1.7 without copying product logic.
    internal static class BlancasSleepChecks
    {
        internal const string FixtureSave = "BlancasSleepFixture16", RoundtripSave = "BlancasSleepRoundtrip17";
        private const string Prefix = "BlancasSleep_";
        private static readonly string[] Drugs = { "Sonata", "Ambien", "Restoril" };
        private static readonly string[] Modes = { "awake-floor", "sleep-floor", "awake-bed", "sleep-bed", "transitioned" };
        private static readonly FieldInfo Transitioned = typeof(HediffComp_SleepLogic).GetField("hasTransitioned", BindingFlags.Instance | BindingFlags.NonPublic);
        private static int FloatBits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        private static HediffDef Ready(string drug) => DefDatabase<HediffDef>.GetNamed("BD_" + drug + "DeepSleeper");
        private static HediffDef Deep => DefDatabase<HediffDef>.GetNamed("BD_DeepSleep");
        private static HediffComp_SleepLogic SleepComp(Hediff h) => ((HediffWithComps)h).TryGetComp<HediffComp_SleepLogic>();
        private static List<Pawn> Subjects() => Find.CurrentMap.mapPawns.AllPawnsSpawned.Where(p => p.Name?.ToStringFull.StartsWith(Prefix, StringComparison.Ordinal) == true).OrderBy(p => p.Name.ToStringFull, StringComparer.Ordinal).ToList();
        private static Building_Bed Bed(Map map, IntVec3 cell)
        {
            var bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog); bed.SetFaction(Faction.OfPlayer);
            return (Building_Bed)GenSpawn.Spawn(bed, cell, map);
        }
        private static void LieDown(Pawn pawn, Building_Bed bed, bool asleep)
        {
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
            IntVec3 cell = bed == null ? pawn.Position : bed.GetSleepingSlotPos(0);
            if (bed != null) pawn.ownership.ClaimBedIfNonMedical(bed);
            pawn.Position = cell; pawn.pather.StopDead();
            var target = bed == null ? new LocalTargetInfo(cell) : new LocalTargetInfo(bed);
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, target), JobCondition.InterruptForced);
            if (!(pawn.jobs.curDriver is JobDriver_LayDown)) throw new InvalidOperationException("Actual LayDown job did not start.");
            pawn.jobs.posture = bed == null ? PawnPosture.LayingOnGroundNormal : PawnPosture.LayingInBed;
            pawn.jobs.curDriver.asleep = asleep;
        }
        internal static void Create(string output, Action<bool, string> check)
        {
            Map map = Find.CurrentMap; int index = 0;
            foreach (string drug in Drugs)
                foreach (string mode in Modes)
                {
                    IntVec3 cell = map.Center + new IntVec3(index % 5 * 5 - 10, 0, index / 5 * 5 - 5); index++;
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, fixedBiologicalAge: 25, forcedXenotype: XenotypeDefOf.Baseliner, canGeneratePawnRelations: false, allowAddictions: false));
                    pawn.Name = new NameSingle(Prefix + drug + "_" + mode); GenSpawn.Spawn(pawn, cell, map);
                    pawn.needs.rest.CurLevel = .5f;
                    bool bedMode = mode.Contains("bed") || mode == "transitioned", asleep = mode.StartsWith("sleep", StringComparison.Ordinal) || mode == "transitioned";
                    var bed = bedMode ? Bed(map, cell + new IntVec3(2, 0, 0)) : null;
                    LieDown(pawn, bed, asleep);
                    check(pawn.Awake() == !asleep && pawn.InBed() == bedMode, drug + " fixture actual posture/sleep: " + mode);
                    var ready = HediffMaker.MakeHediff(Ready(drug), pawn); ready.Severity = .75f; pawn.health.AddHediff(ready); var comp = SleepComp(ready);
                    if (mode != "sleep-bed") pawn.health.HealthTick();
                    if (mode == "transitioned")
                    {
                        check(!pawn.health.hediffSet.hediffs.Contains(ready) && pawn.health.hediffSet.hediffs.Count(h => h.def == Deep) == 1 && (bool)Transitioned.GetValue(comp), drug + " actual release health tick transitions only once in bed");
                        float adjustment = 0; comp.CompPostTick(ref adjustment); comp.CompPostTick(ref adjustment);
                        check(pawn.health.hediffSet.hediffs.Count(h => h.def == Deep) == 1, drug + " removed transitioned callback cannot add a duplicate");
                    }
                    else check(pawn.health.hediffSet.hediffs.Contains(ready) && !(bool)Transitioned.GetValue(comp), drug + " pre-transition state retained: " + mode);
                }
            check(Subjects().Count == 15, "15 real 1.6 pawns cover three sedatives and five before/after sleep states");
            Snapshot(output, false);
        }
        internal static void Snapshot(string output, bool roundtrip)
        {
            var root = new XElement("blancasSleep", new XAttribute("roundtrip", roundtrip));
            foreach (Pawn pawn in Subjects())
            {
                var node = new XElement("pawn", new XAttribute("name", pawn.Name.ToStringFull), new XAttribute("awake", pawn.Awake()), new XAttribute("bed", pawn.InBed()),
                    new XAttribute("job", pawn.CurJob?.def.defName ?? ""), new XAttribute("asleep", pawn.jobs.curDriver?.asleep ?? false), new XAttribute("posture", pawn.GetPosture()), new XAttribute("rest", pawn.needs.rest.CurLevel.ToString("R", CultureInfo.InvariantCulture)));
                foreach (Hediff h in pawn.health.hediffSet.hediffs.Where(h => h.def.defName.StartsWith("BD_", StringComparison.Ordinal)).OrderBy(h => h.def.defName, StringComparer.Ordinal))
                {
                    var item = new XElement("hediff", new XAttribute("def", h.def.defName), new XAttribute("type", h.GetType().FullName), new XAttribute("severity", h.Severity.ToString("R", CultureInfo.InvariantCulture)));
                    var sleep = (h as HediffWithComps)?.TryGetComp<HediffComp_SleepLogic>();
                    if (sleep != null) item.Add(new XAttribute("comp", sleep.GetType().FullName), new XAttribute("hasTransitioned", (bool)Transitioned.GetValue(sleep)));
                    node.Add(item);
                }
                root.Add(node);
            }
            new XDocument(root).Save(Path.Combine(output, roundtrip ? "blancas-sleep-roundtrip.xml" : "blancas-sleep.xml"));
        }
        internal static void VerifyLoaded(string output, bool roundtrip, Action<bool, string> check)
        {
            var document = XDocument.Load(Path.Combine(output, roundtrip ? "blancas-sleep-roundtrip.xml" : "blancas-sleep.xml"));
            var pawns = Subjects(); check(pawns.Count == 15, "all 15 sleep fixture pawns restored before the first simulation tick");
            foreach (var node in document.Root.Elements("pawn"))
            {
                Pawn pawn = pawns.Single(p => p.Name.ToStringFull == (string)node.Attribute("name"));
                check(pawn.Awake() == (bool)node.Attribute("awake") && pawn.InBed() == (bool)node.Attribute("bed")
                    && (pawn.jobs.curDriver?.asleep ?? false) == (bool)node.Attribute("asleep") && pawn.GetPosture().ToString() == (string)node.Attribute("posture")
                    && (pawn.CurJob?.def.defName ?? "") == (string)node.Attribute("job"), pawn.Name + " actual job/bed/asleep state restored");
                check(FloatBits(pawn.needs.rest.CurLevel) == FloatBits((float)node.Attribute("rest")), pawn.Name + " rest level preserved before ticking");
                var actual = pawn.health.hediffSet.hediffs.Where(h => h.def.defName.StartsWith("BD_", StringComparison.Ordinal)).OrderBy(h => h.def.defName, StringComparer.Ordinal).ToList();
                var expected = node.Elements("hediff").ToList(); check(actual.Count == expected.Count, pawn.Name + " no BD effect lost or duplicated");
                for (int i = 0; i < expected.Count; i++)
                {
                    Hediff h = actual[i]; var saved = expected[i];
                    check(h.def.defName == (string)saved.Attribute("def") && h.GetType().FullName == (string)saved.Attribute("type") && FloatBits(h.Severity) == FloatBits((float)saved.Attribute("severity")), pawn.Name + " effect type/severity bits preserved: " + h.def.defName);
                    if (saved.Attribute("comp") != null)
                    {
                        var comp = SleepComp(h);
                        check(comp != null && comp.GetType().FullName == (string)saved.Attribute("comp") && (bool)Transitioned.GetValue(comp) == (bool)saved.Attribute("hasTransitioned"), pawn.Name + " published sleep Comp and hasTransitioned key preserved");
                    }
                }
            }
        }
        internal static void Resume(string output, Action<bool, string> check)
        {
            foreach (Pawn pawn in Subjects())
            {
                string drug = Drugs.Single(d => pawn.Name.ToStringFull.StartsWith(Prefix + d + "_", StringComparison.Ordinal));
                string mode = Modes.Single(m => pawn.Name.ToStringFull.EndsWith("_" + m, StringComparison.Ordinal));
                pawn.health.HealthTick();
                bool alreadyDeep = mode == "sleep-bed" || mode == "transitioned";
                check(pawn.health.hediffSet.hediffs.Count(h => h.def == Deep) == (alreadyDeep ? 1 : 0)
                    && pawn.health.hediffSet.HasHediff(Ready(drug)) == !alreadyDeep, pawn.Name + " first actual 1.7 health tick honors the bed-only transition");
                if (!alreadyDeep)
                {
                    Building_Bed bed = pawn.CurrentBed() ?? Bed(pawn.Map, pawn.Position + new IntVec3(2, 0, 0));
                    LieDown(pawn, bed, true); check(!pawn.Awake() && pawn.InBed(), pawn.Name + " moved to actual sleeping-in-bed state");
                    pawn.health.HealthTick();
                }
                float before = pawn.health.hediffSet.hediffs.Single(h => h.def == Deep).Severity;
                for (int i = 0; i < 3; i++) pawn.health.HealthTick();
                check(pawn.health.hediffSet.hediffs.Count(h => h.def == Deep) == 1 && !pawn.health.hediffSet.HasHediff(Ready(drug))
                    && pawn.health.hediffSet.hediffs.Single(h => h.def == Deep).Severity <= before, pawn.Name + " repeated actual health ticks neither duplicate nor reset the deep sleep effect");
            }
            File.WriteAllText(Path.Combine(output, "blancas-sleep-resumed.txt"), "15 pawns; 3 source sedatives; all bed-only transitions complete without duplicate or reset.\n");
        }
    }
}
