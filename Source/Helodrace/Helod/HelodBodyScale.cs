using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    // Gene extension for a larger frame: body size, rendered size and extra stomach capacity.
    public sealed class HelodBodyScaleExtension : DefModExtension
    {
        public float bodySizeFactor = 1f;
        public float drawScaleFactor = 1f;
        // Applied on top of the body size increase, which already enlarges the stomach.
        public float foodCapacityFactor = 1f;
    }

    // Redraws the pawn when a frame gene is added or removed so the mesh size follows.
    public sealed class Gene_HelodBodyScale : Gene
    {
        public override void PostAdd()
        {
            base.PostAdd();
            pawn?.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        public override void PostRemove()
        {
            base.PostRemove();
            pawn?.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    public static class HelodBodyScale
    {
        private static List<GeneDef> genes;

        private static List<GeneDef> Genes
        {
            get
            {
                if (genes == null)
                {
                    genes = new List<GeneDef>();
                    foreach (GeneDef def in DefDatabase<GeneDef>.AllDefsListForReading)
                        if (def.HasModExtension<HelodBodyScaleExtension>())
                            genes.Add(def);
                }
                return genes;
            }
        }

        private static HelodBodyScaleExtension ActiveExtension(Pawn pawn)
        {
            if (pawn?.genes == null) return null;
            var list = Genes;
            for (int i = 0; i < list.Count; i++)
                if (pawn.genes.HasActiveGene(list[i]))
                    return list[i].GetModExtension<HelodBodyScaleExtension>();
            return null;
        }

        public static float BodySizeFactor(Pawn pawn) => ActiveExtension(pawn)?.bodySizeFactor ?? 1f;
        public static float DrawScaleFactor(Pawn pawn) => ActiveExtension(pawn)?.drawScaleFactor ?? 1f;
        public static float FoodCapacityFactor(Pawn pawn) => ActiveExtension(pawn)?.foodCapacityFactor ?? 1f;
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.BodySize), MethodType.Getter)]
    public static class Patch_HelodBodySize
    {
        public static void Postfix(Pawn __instance, ref float __result)
        {
            if (HelodRace.IsHelod(__instance))
                __result *= HelodBodyScale.BodySizeFactor(__instance);
        }
    }

    [HarmonyPatch(typeof(Need_Food), nameof(Need_Food.MaxLevel), MethodType.Getter)]
    public static class Patch_HelodFoodCapacity
    {
        public static void Postfix(Pawn ___pawn, ref float __result)
        {
            if (HelodRace.IsHelod(___pawn))
                __result *= HelodBodyScale.FoodCapacityFactor(___pawn);
        }
    }

    // The sharpshooter gene is archite but shared by every Helod lineage; vanilla never inherits archite endogenes.
    [HarmonyPatch(typeof(PregnancyUtility), nameof(PregnancyUtility.GetInheritedGenes),
        new[] { typeof(Pawn), typeof(Pawn), typeof(bool) }, new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
    public static class Patch_HelodArchiteInheritance
    {
        public const string SharpshooterGene = "HD_Gene_Sharpshooter";

        public static void Postfix(Pawn father, Pawn mother, bool success, List<GeneDef> __result)
        {
            if (!success || __result == null) return;
            GeneDef gene = DefDatabase<GeneDef>.GetNamedSilentFail(SharpshooterGene);
            if (gene == null || __result.Contains(gene)) return;
            if (HasEndogene(father, gene) || HasEndogene(mother, gene))
                __result.Add(gene);
        }

        private static bool HasEndogene(Pawn pawn, GeneDef gene)
        {
            if (pawn?.genes == null) return false;
            foreach (Gene g in pawn.genes.Endogenes)
                if (g.def == gene) return true;
            return false;
        }
    }
}
