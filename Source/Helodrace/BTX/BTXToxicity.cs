using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    public static class BTXUtility
    {
        public const string ChemicalDefName = "BTX";
        public const string BTXDependencyGeneDefName = "HD_Gene_BTXDependency";
        public const string StabilizationHediffDefName = "HD_BTXHigh";
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

        public static bool HasBTXDependency(Pawn pawn)
        {
            GeneDef dependencyGene = DefDatabase<GeneDef>.GetNamedSilentFail(BTXDependencyGeneDefName);
            return dependencyGene != null
                && pawn?.genes?.HasActiveGene(dependencyGene) == true;
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

            if (BTXUtility.HasBTXDependency(ingester))
            {
                // CompDrug already replenishes Chemical_BTX through the addiction hediff.
                // Only non-carriers need the custom toxicity.
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
            if (__instance?.hediffDef?.defName == BTXUtility.StabilizationHediffDefName && !BTXUtility.HasBTXDependency(pawn))
            {
                return false;
            }

            return true;
        }
    }
}
