using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class PatchHelodXenotype
    {
        private static readonly string[] HelodXenotypeDefNames =
        {
            "HD_MackenzieValleyHelod",
            "HD_TexasHelod",
            "HD_ArcticHelod",
            "HD_EasternHelod",
            "HD_MexicanHelod"
        };

        private static void Postfix(Pawn __result)
        {
            RemoveHelodOnlyTraitsFromNonHelods(__result);
            EnsureHelodXenotype(__result);
        }

        private static void RemoveHelodOnlyTraitsFromNonHelods(Pawn pawn)
        {
            if (pawn == null || BTXUtility.IsHelod(pawn) || pawn.story?.traits == null)
            {
                return;
            }

            TraitDef rawBTXMadman = DefDatabase<TraitDef>.GetNamedSilentFail(BTXUtility.RawBTXMadmanTraitDefName);
            if (rawBTXMadman == null)
            {
                return;
            }

            Trait trait = pawn.story.traits.GetTrait(rawBTXMadman);
            if (trait != null)
            {
                pawn.story.traits.RemoveTrait(trait);
            }
        }

        private static void EnsureHelodXenotype(Pawn pawn)
        {
            if (pawn?.def?.defName != "Helod" || pawn.genes == null)
            {
                return;
            }

            List<XenotypeDef> helodXenotypes = new List<XenotypeDef>();
            foreach (string defName in HelodXenotypeDefNames)
            {
                XenotypeDef xenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail(defName);
                if (xenotype != null)
                {
                    helodXenotypes.Add(xenotype);
                }
            }

            if (helodXenotypes.Count == 0 || helodXenotypes.Contains(pawn.genes.Xenotype))
            {
                return;
            }

            pawn.genes.SetXenotype(helodXenotypes.RandomElement());
        }
    }
}
