using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    public static class BTXUtility
    {
        public const string ChemicalDefName = "BTX";
        public const string BTXNeedDefName = "HD_BTXNeed";
        public const string StabilizationHediffDefName = "HD_BTXHigh";
        public const string DeficiencyHediffDefName = "HD_BTXDeficiency";
        public const string ToxicityHediffDefName = "HD_BTXToxicity";
        public const string RawBTXMadmanTraitDefName = "HD_RawBTXMadman";
        public const string RawBTXDisgustThoughtDefName = "HD_DrankNaphtha";
        public const string RawBTXEnjoyedThoughtDefName = "HD_EnjoyedRawBTX";
        public const string NaphthaThingDefName = "HD_Naphtha";
        public const string HelodRaceDefName = "Helod";

        public static bool IsHelod(Pawn pawn)
        {
            return pawn?.def?.defName == HelodRaceDefName;
        }

        public static bool ContainsBTX(ThingDef thingDef)
        {
            return thingDef?.comps != null
                && thingDef.comps.OfType<CompProperties_Drug>()
                    .Any(comp => comp.chemical?.defName == ChemicalDefName);
        }

        public static bool IsRawBTX(ThingDef thingDef)
        {
            return thingDef?.defName == NaphthaThingDefName;
        }

        public static bool HasRawBTXMadmanTrait(Pawn pawn)
        {
            TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(RawBTXMadmanTraitDefName);
            return traitDef != null
                && IsHelod(pawn)
                && pawn?.story?.traits?.HasTrait(traitDef) == true;
        }

        public static float BTXNeedOffset(ThingDef thingDef)
        {
            return thingDef?.comps?.OfType<CompProperties_Drug>()
                .Where(comp => comp.chemical?.defName == ChemicalDefName)
                .Select(comp => comp.needLevelOffset)
                .DefaultIfEmpty(0f)
                .Max() ?? 0f;
        }

        public static void SatisfyBTXNeed(Pawn pawn, float offset)
        {
            if (pawn?.needs?.AllNeeds == null || offset <= 0f)
            {
                return;
            }

            foreach (Need need in pawn.needs.AllNeeds)
            {
                if (need is Need_BTX)
                {
                    need.CurLevel += offset;
                }
            }

            RemoveLegacyBTXGeneticDependency(pawn);
        }

        public static void RemoveLegacyBTXGeneticDependency(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
            {
                return;
            }

            for (int i = pawn.health.hediffSet.hediffs.Count - 1; i >= 0; i--)
            {
                if (pawn.health.hediffSet.hediffs[i] is Hediff_ChemicalDependency dependency
                    && dependency.chemical?.defName == ChemicalDefName)
                {
                    pawn.health.RemoveHediff(dependency);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class Patch_Thing_Ingested_RawBTXMadmanThought
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, Pawn ingester)
        {
            if (!BTXUtility.IsRawBTX(__instance?.def) || !BTXUtility.HasRawBTXMadmanTrait(ingester))
            {
                return;
            }

            ThoughtDef disgustThought = DefDatabase<ThoughtDef>.GetNamedSilentFail(BTXUtility.RawBTXDisgustThoughtDefName);
            ThoughtDef enjoyedThought = DefDatabase<ThoughtDef>.GetNamedSilentFail(BTXUtility.RawBTXEnjoyedThoughtDefName);
            MemoryThoughtHandler memories = ingester?.needs?.mood?.thoughts?.memories;
            if (memories == null || enjoyedThought == null)
            {
                return;
            }

            if (disgustThought != null)
            {
                memories.RemoveMemoriesOfDef(disgustThought);
            }

            memories.TryGainMemory(enjoyedThought);
        }
    }

    [HarmonyPatch(typeof(CompDrug), nameof(CompDrug.PostIngested))]
    public static class Patch_CompDrug_PostIngested_BTXEffects
    {
        [HarmonyPostfix]
        public static void Postfix(CompDrug __instance, Pawn ingester)
        {
            ThingDef drugDef = __instance?.parent?.def;
            if (ingester?.health == null || !BTXUtility.ContainsBTX(drugDef))
            {
                return;
            }

            if (BTXUtility.IsHelod(ingester))
            {
                BTXUtility.SatisfyBTXNeed(ingester, BTXUtility.BTXNeedOffset(drugDef));
                return;
            }

            HediffDef toxicityDef = DefDatabase<HediffDef>.GetNamedSilentFail(BTXUtility.ToxicityHediffDefName);
            if (toxicityDef == null)
            {
                Log.WarningOnce("Helodrace: HD_BTXToxicity hediff is missing, so non-Helod BTX toxicity cannot be applied.", 71930411);
                return;
            }

            Hediff existing = ingester.health.hediffSet.GetFirstHediffOfDef(toxicityDef);
            if (existing != null)
            {
                if (existing is Hediff_BTXToxicity btxToxicity)
                {
                    btxToxicity.AddBTXDose(0.20f);
                }
                else
                {
                    existing.Severity += 0.20f;
                }
                return;
            }

            Hediff toxicity = HediffMaker.MakeHediff(toxicityDef, ingester);
            toxicity.Severity = 0.20f;
            ingester.health.AddHediff(toxicity);
        }
    }

    [HarmonyPatch(typeof(IngestionOutcomeDoer_GiveHediff), "DoIngestionOutcomeSpecial")]
    public static class Patch_GiveHediff_BlockBTXStabilizationForNonHelods
    {
        [HarmonyPrefix]
        public static bool Prefix(IngestionOutcomeDoer_GiveHediff __instance, Pawn pawn)
        {
            if (__instance?.hediffDef?.defName == BTXUtility.StabilizationHediffDefName && !BTXUtility.IsHelod(pawn))
            {
                return false;
            }

            return true;
        }
    }
}
