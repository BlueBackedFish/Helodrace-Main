using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Tactical
{
    public sealed class CompProperties_TcccDose : CompProperties
    { public CompProperties_TcccDose() { compClass = typeof(CompTcccDose); } }

    public sealed class CompTcccDose : ThingComp
    {
        public float remaining = 1f;
        public override void PostExposeData() { base.PostExposeData(); Scribe_Values.Look(ref remaining, "tcccRemainingDose", 1f); }
        public override bool AllowStackWith(Thing other)
        { return remaining >= .99999f && (other.TryGetComp<CompTcccDose>()?.remaining ?? 1f) >= .99999f; }
        public override void PostSplitOff(Thing piece)
        { if (piece.TryGetComp<CompTcccDose>() is CompTcccDose comp) comp.remaining = remaining; }
        public override string CompInspectStringExtra() => "HD_TCCC_DoseRemaining".Translate(remaining.ToString("P0"));
        public override string TransformLabel(string label) => remaining < .99999f
            ? label + " (" + remaining.ToString("P0") + ")" : label;
    }

    // Associates an actual Blanca's Drugs high with its fractional dose. The native high
    // still determines duration, tolerance interaction, thoughts and chemical identity.
    public sealed class Hediff_TcccAnalgesia : Hediff
    {
        public HediffDef highDef;
        public float fraction;
        public float painRelief;
        private HediffStage originalStage;
        private HediffStage scaledStage;
        public override bool ShouldRemove => highDef == null || !pawn.health.hediffSet.HasHediff(highDef);
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Defs.Look(ref highDef, "highDef");
            Scribe_Values.Look(ref fraction, "fraction"); Scribe_Values.Look(ref painRelief, "painRelief");
        }
        public override string TipStringExtra => "HD_TCCC_DoseEffect".Translate(fraction.ToString("P0"), painRelief.ToString("P0"));
        public HediffStage Stage(HediffStage original)
        {
            if (originalStage == original && scaledStage != null) return scaledStage;
            originalStage = original;
            scaledStage = TcccDrugs.Clone(original);
            // Apply the absolute, measured analgesia after vanilla's other pain modifiers.
            scaledStage.painFactor = 1f; scaledStage.painOffset = 0f;
            if (original.capMods != null) scaledStage.capMods = original.capMods.Select(m =>
            {
                PawnCapacityModifier scaled = TcccDrugs.Clone(m);
                scaled.offset *= fraction;
                scaled.postFactor = Mathf.Lerp(1f, m.postFactor, fraction);
                if (m.setMax < 1f) scaled.setMax = Mathf.Lerp(1f, m.setMax, fraction);
                return scaled;
            }).ToList();
            if (original.statOffsets != null) scaledStage.statOffsets = original.statOffsets.Select(m =>
                new StatModifier { stat = m.stat, value = m.value * fraction }).ToList();
            if (original.statFactors != null) scaledStage.statFactors = original.statFactors.Select(m =>
                new StatModifier { stat = m.stat, value = Mathf.Lerp(1f, m.value, fraction) }).ToList();
            return scaledStage;
        }
    }

    public static class TcccDrugs
    {
        private static readonly MethodInfo CloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone");
        public static T Clone<T>(T value) where T : class => (T)CloneMethod.Invoke(value, null);
        private static readonly HashSet<string> Names = new HashSet<string>
        { "BD_Morphine", "BD_Fentanyl", "BD_Ketamine", "BD_Laudanum" };
        public static bool IsAnalgesic(ThingDef def) => def != null && Names.Contains(def.defName);
        public static HediffDef HighDef(ThingDef def) => def.ingestible?.outcomeDoers?
            .OfType<IngestionOutcomeDoer_GiveHediff>().FirstOrDefault(d => d.hediffDef?.stages?.Any(s => s.painFactor < 1f) == true)?.hediffDef;
        public static Hediff_TcccAnalgesia Marker(Pawn pawn, HediffDef high) => pawn?.health?.hediffSet?.hediffs
            .OfType<Hediff_TcccAnalgesia>().FirstOrDefault(h => h.highDef == high);

        public sealed class DoseContext
        {
            public Pawn patient;
            public Thing drug;
            public float fraction;
            public bool skilled;
            public Hediff_TcccAnalgesia marker;
            public DoseContext previous;
        }
        [ThreadStatic] public static DoseContext Current;
        [ThreadStatic] public static bool ApplyingScaledOutcome;

        public static DoseContext Begin(Pawn patient, Thing drug, float fraction, float relief, bool skilled)
        {
            var context = new DoseContext { patient = patient, drug = drug, fraction = fraction, skilled = skilled, previous = Current };
            HediffDef high = HighDef(drug.def);
            // Do not weaken or relabel an existing full, normally ingested dose.
            if (high != null && !patient.health.hediffSet.HasHediff(high))
            {
                context.marker = (Hediff_TcccAnalgesia)HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("HD_TCCC_Analgesia"), patient);
                context.marker.highDef = high; context.marker.fraction = fraction; context.marker.painRelief = relief;
                patient.health.AddHediff(context.marker);
            }
            Current = context;
            return context;
        }
        public static void End(DoseContext context)
        {
            if (context == null) return;
            Current = context.previous;
            if (context.marker != null && context.marker.ShouldRemove) context.patient.health.RemoveHediff(context.marker);
            context.patient.health.hediffSet.DirtyCache();
        }

        public static bool Administer(Pawn actor, Pawn patient, Thing source)
        {
            if (!TcccUtility.CanTreat(actor, patient) || source == null || source.Destroyed
                || source.ParentHolder != actor.inventory || !IsAnalgesic(source.def)) return false;
            float pain = patient.health.hediffSet.PainTotal;
            HediffDef high = HighDef(source.def);
            if (pain <= TcccRules.TargetPain) { TcccUtility.Reject("HD_TCCC_NoPain"); return false; }
            if (patient.health.hediffSet.hediffs.Any(h => h is Hediff_TcccAnalgesia ||
                Names.Any(n => DefDatabase<ThingDef>.GetNamedSilentFail(n) is ThingDef d && HighDef(d) == h.def)))
            { TcccUtility.Reject("HD_TCCC_AnalgesicActive"); return false; }
            if (high == null || !(source.TryGetComp<CompTcccDose>() is CompTcccDose dose)) return false;
            float potency = 1f - high.stages[0].painFactor;
            float used = Mathf.Min(dose.remaining, TcccRules.DoseFor(pain, high.stages[0].painFactor));
            if (used <= .00001f) return false;

            // Separate one physical unit before changing its remaining amount; full stacks stay full.
            Thing unit = source.stackCount > 1 ? source.SplitOff(1) : source;
            dose = unit.TryGetComp<CompTcccDose>();
            // A transient ingestion unit runs the drug's original outcomes exactly once. The
            // real inventory unit below is charged only the amount actually administered.
            Thing ingestion = ThingMaker.MakeThing(unit.def);
            DoseContext context = Begin(patient, ingestion, used, Mathf.Min(pain - TcccRules.TargetPain, potency * used), true);
            try
            {
                ingestion.Ingested(patient, 0f);
                dose.remaining = Mathf.Max(0f, dose.remaining - used);
                if (dose.remaining <= .00001f) unit.Destroy();
            }
            finally
            {
                End(context);
                if (!ingestion.Destroyed) ingestion.Destroy();
                // Reinsert only after setting the remainder, otherwise TryAdd can merge the
                // still-full split unit back into the source stack before it is charged.
                if (!unit.Destroyed && unit.ParentHolder == null && !actor.inventory.innerContainer.TryAdd(unit))
                    GenPlace.TryPlaceThing(unit, actor.Position, actor.Map, ThingPlaceMode.Near);
            }
            Messages.Message("HD_TCCC_DoseGiven".Translate(patient.LabelShortCap, source.def.LabelCap, used.ToString("P0")),
                patient, MessageTypeDefOf.PositiveEvent);
            return true;
        }
    }

    [HarmonyPatch(typeof(IngestionOutcomeDoer_GiveHediff), "DoIngestionOutcomeSpecial")]
    public static class Patch_TcccFractionalOutcome
    {
        private static readonly Action<IngestionOutcomeDoer_GiveHediff, Pawn, Thing, int> ApplySpecial =
            AccessTools.MethodDelegate<Action<IngestionOutcomeDoer_GiveHediff, Pawn, Thing, int>>(
                AccessTools.Method(typeof(IngestionOutcomeDoer_GiveHediff), "DoIngestionOutcomeSpecial"));
        public static bool Prefix(IngestionOutcomeDoer_GiveHediff __instance, Pawn pawn, Thing ingested, int ingestedCount)
        {
            var context = TcccDrugs.Current;
            if (context == null || context.patient != pawn || context.drug != ingested || TcccDrugs.ApplyingScaledOutcome) return true;
            var scaled = TcccDrugs.Clone(__instance);
            scaled.severity *= context.fraction;
            TcccDrugs.ApplyingScaledOutcome = true;
            try { ApplySpecial(scaled, pawn, ingested, ingestedCount); }
            finally { TcccDrugs.ApplyingScaledOutcome = false; }
            return false;
        }
    }

    [HarmonyPatch(typeof(CompDrug), nameof(CompDrug.PostIngested))]
    public static class Patch_TcccFractionalDrug
    {
        public static void Prefix(CompDrug __instance, Pawn ingester, out CompProperties __state)
        {
            __state = null;
            var context = TcccDrugs.Current;
            if (context == null || context.drug != __instance.parent || context.patient != ingester) return;
            __state = __instance.props;
            var scaled = TcccDrugs.Clone((CompProperties_Drug)__instance.props);
            scaled.addictiveness = TcccRules.AddictionChance(scaled.addictiveness, context.fraction, context.skilled);
            scaled.existingAddictionSeverityOffset *= context.fraction;
            scaled.needLevelOffset *= context.fraction;
            scaled.largeOverdoseChance *= context.fraction;
            scaled.overdoseSeverityOffset = new FloatRange(scaled.overdoseSeverityOffset.min * context.fraction,
                scaled.overdoseSeverityOffset.max * context.fraction);
            __instance.props = scaled;
        }
        public static void Finalizer(CompDrug __instance, CompProperties __state)
        { if (__state != null) __instance.props = __state; }
    }

    // A leftover vial used via the normal ingest command must not grant a full dose.
    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class Patch_TcccPartialIngestion
    {
        public static void Prefix(Thing __instance, Pawn ingester, out TcccDrugs.DoseContext __state)
        {
            __state = null;
            var dose = __instance.TryGetComp<CompTcccDose>();
            if (dose == null || dose.remaining >= .99999f || TcccDrugs.Current?.drug == __instance) return;
            HediffDef high = TcccDrugs.HighDef(__instance.def);
            if (high == null) return;
            float relief = Mathf.Min(ingester.health.hediffSet.PainTotal, (1f - high.stages[0].painFactor) * dose.remaining);
            __state = TcccDrugs.Begin(ingester, __instance, dose.remaining, relief, false);
        }
        public static void Finalizer(TcccDrugs.DoseContext __state) { TcccDrugs.End(__state); }
    }

    [HarmonyPatch(typeof(Hediff), nameof(Hediff.CurStage), MethodType.Getter)]
    public static class Patch_TcccDoseStage
    {
        public static void Postfix(Hediff __instance, ref HediffStage __result)
        {
            if (__result == null || !__instance.def.defName.StartsWith("BD_")) return;
            var marker = TcccDrugs.Marker(__instance.pawn, __instance.def);
            if (marker != null) __result = marker.Stage(__result);
        }
    }

    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.PainTotal), MethodType.Getter)]
    public static class Patch_TcccDosePain
    {
        public static void Postfix(HediffSet __instance, ref float __result)
        {
            foreach (var marker in __instance.hediffs.OfType<Hediff_TcccAnalgesia>())
                if (!marker.ShouldRemove) __result = Mathf.Max(0f, __result - marker.painRelief);
        }
    }
}
