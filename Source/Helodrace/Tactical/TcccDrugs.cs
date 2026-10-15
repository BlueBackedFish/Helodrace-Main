using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Tactical
{
    public static class TcccDrugs
    {
        private static readonly MethodInfo CloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone");
        internal static T Clone<T>(T value) where T : class => (T)CloneMethod.Invoke(value, null);
        private const float BreathingThreshold = .70f;
        private const float AddictionFactor = .70f;

        public sealed class DoseContext
        {
            public Pawn patient;
            public Thing drug;
            public DoseContext previous;
        }

        [ThreadStatic] public static DoseContext Current;

        public static string DrugNameForBreathing(float breathing) =>
            breathing < BreathingThreshold ? "BD_Ketamine" : "BD_Fentanyl";

        public static ThingDef SelectedDef(Pawn patient)
        {
            if (patient?.health?.capacities == null) return null;
            string name = DrugNameForBreathing(patient.health.capacities.GetLevel(PawnCapacityDefOf.Breathing));
            return DefDatabase<ThingDef>.GetNamedSilentFail(name);
        }

        public static HediffDef HighDef(ThingDef def) => def?.ingestible?.outcomeDoers?
            .OfType<IngestionOutcomeDoer_GiveHediff>()
            .FirstOrDefault(outcome => outcome.hediffDef?.stages?.Any(stage => stage.painFactor < 1f) == true)
            ?.hediffDef;

        public static bool Administer(Pawn actor, Pawn patient)
        {
            if (!TcccUtility.CanTreat(actor, patient, TcccTreatment.Analgesic)) return false;
            if (patient.health.hediffSet.PainTotal <= 0f)
            { TcccUtility.Reject("HD_TCCC_NoPain"); return false; }
            ThingDef chosen = SelectedDef(patient);
            HediffDef high = HighDef(chosen);
            if (chosen == null || high == null)
            { TcccUtility.Reject("HD_TCCC_NoAnalgesic"); return false; }
            CompMedicalPouch pouch = MedicalPouchUtility.ForTreatment(actor, TcccTreatment.Analgesic);
            if (pouch == null) return false;
            Thing drug = ThingMaker.MakeThing(chosen);
            DoseContext context = new DoseContext { patient = patient, drug = drug, previous = Current };
            Current = context;
            try { drug.Ingested(patient, 0f); }
            finally
            {
                Current = context.previous;
                if (!drug.Destroyed) drug.Destroy();
            }
            pouch.TrySpend(TcccTreatment.Analgesic);
            Messages.Message("HD_TCCC_DoseGiven".Translate(patient.LabelShortCap, chosen.LabelCap),
                patient, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        public static float ReducedAddictionChance(float original) => original * AddictionFactor;
    }

    [HarmonyPatch(typeof(CompDrug), nameof(CompDrug.PostIngested))]
    public static class Patch_TcccDrugAddiction
    {
        public static void Prefix(CompDrug __instance, Pawn ingester, out CompProperties __state)
        {
            __state = null;
            TcccDrugs.DoseContext context = TcccDrugs.Current;
            if (context == null || context.drug != __instance.parent || context.patient != ingester) return;
            __state = __instance.props;
            var scaled = TcccDrugs.Clone((CompProperties_Drug)__instance.props);
            scaled.addictiveness = TcccDrugs.ReducedAddictionChance(scaled.addictiveness);
            __instance.props = scaled;
        }

        public static void Finalizer(CompDrug __instance, CompProperties __state)
        { if (__state != null) __instance.props = __state; }
    }
}
