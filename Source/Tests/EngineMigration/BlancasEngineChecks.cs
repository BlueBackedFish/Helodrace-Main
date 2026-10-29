using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;
using CurrentDrug = HelodBlancasDrug;
using LegacyDrug = HelodBlancasDrug.Legacy;

namespace Helodrace.Tests
{
    internal static class BlancasEngineChecks
    {
        private sealed record Fixture(Pawn Pawn, HediffComp Self)
        {
            public void Check() => Self.CompPostPostAdd(null);
            public string State => string.Join(",", Pawn.health.hediffSet.hediffs.Where(h => h.def.defName.StartsWith("MigrationCompetition_")).Select(h => h.def.defName));
        }

        internal static void Run(Action<bool, string> check)
        {
            foreach (float selfStrength in new[] { 1f, 2f })
            foreach (float otherStrength in new[] { 1f, 2f })
            foreach (float otherSeverity in new[] { .2f, .8f })
            foreach (bool protectOther in new[] { false, true })
            {
                Fixture legacy = Create(true, 2, selfStrength, otherStrength, otherSeverity, protectOther);
                Fixture updated = Create(false, 2, selfStrength, otherStrength, otherSeverity, protectOther);
                legacy.Check(); updated.Check();
                check(legacy.State == updated.State, $"actual HealthTracker removal matches release: strength {selfStrength}/{otherStrength}, severity {otherSeverity}, offset {protectOther}");
                check(!updated.Pawn.health.hediffSet.HasHediff(HediffDefOf.BloodLoss), "competition suppresses actual OnRemoval follow-up");
            }

            Fixture oldWork = Create(true, 80, 1, 1, .5f, false, separateGroups: true);
            Fixture newWork = Create(false, 80, 1, 1, .5f, false, separateGroups: true);
            for (int i = 0; i < 2000; i++) { oldWork.Check(); newWork.Check(); }
            var before = Measure(oldWork.Check);
            var after = Measure(newWork.Check);
            check(after.Bytes == 0 && before.Bytes > 0, $"actual engine allocation / 10000 checks, 80 hediffs: release {before.Bytes} B; pooled {after.Bytes} B");
            // Timing is evidence, not a threshold: other host work and JIT can affect it.
            check(true, $"actual engine elapsed / 10000 checks: release {before.Milliseconds:F3} ms; pooled {after.Milliseconds:F3} ms");
        }

        private static Fixture Create(bool legacy, int count, float selfStrength, float otherStrength, float otherSeverity, bool protectOther, bool separateGroups = false)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            HediffComp self = null;
            for (int i = 0; i < count; i++)
            {
                string group = separateGroups && i > 0 ? "unrelated" : "competing";
                var strengths = new List<float> { i == 0 ? selfStrength : otherStrength };
                var offsets = protectOther && i > 0 ? new List<int> { 0 } : null;
                HediffCompProperties props = legacy
                    ? new LegacyDrug.HediffCompProperties_ExclusiveGroup { groupName = group, stageStrengths = strengths, offsetStages = offsets }
                    : new CurrentDrug.HediffCompProperties_ExclusiveGroup { groupName = group, stageStrengths = strengths, offsetStages = offsets };
                var definition = new HediffDef {
                    defName = "MigrationCompetition_" + i,
                    label = "migration competition check", description = "Temporary test effect.",
                    hediffClass = typeof(HediffWithComps),
                    comps = new List<HediffCompProperties> {
                        props, new CurrentDrug.HediffCompProperties_OnRemoval { hediffToGive = HediffDefOf.BloodLoss }
                    }
                };
                var effect = (HediffWithComps)HediffMaker.MakeHediff(definition, pawn);
                effect.Severity = i == 0 ? .5f : otherSeverity;
                // Assemble the fixture before invoking a callback; AddHediff would run
                // competition immediately, while this test needs a complete snapshot.
                pawn.health.hediffSet.hediffs.Add(effect);
                if (i == 0) self = effect.comps[0];
            }
            return new Fixture(pawn, self);
        }

        private static (long Bytes, double Milliseconds) Measure(Action run)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 10000; i++) run();
            return (GC.GetAllocatedBytesForCurrentThread() - allocated, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}
