using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
        new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_HelodPawnRequest
    {
        public static void Prefix(ref PawnGenerationRequest request)
        {
            HelodRaceExtension settings = HelodRace.Settings;
            if (settings == null || request.KindDef == null) return;
            PawnKindDef colonist = DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist");
            bool explicitHelod = request.ForcedXenotype != null
                && settings.xenotypeList.Contains(request.ForcedXenotype);
            // Scenario xenotypes must create an actual Helod, not a human with Helod genes.
            if (explicitHelod && request.KindDef.race == ThingDefOf.Human)
            {
                request.PawnKindDefGetter = null;
                request.KindDef = colonist;
            }
            if (!request.AllowedDevelopmentalStages.Newborn()
                && request.ForcedXenotype == null && request.ForcedCustomXenotype == null)
            {
                string kind = request.KindDef.defName;
                float chance = kind == "Slave" ? settings.slaveChance
                    : kind == "Refugee" || kind == "SpaceRefugee" ? settings.refugeeChance : 0f;
                if (request.KindDef.race == ThingDefOf.Human && chance > 0f && Rand.Chance(chance))
                {
                    request.PawnKindDefGetter = null;
                    request.KindDef = colonist;
                }
            }
            if (request.KindDef.race != HelodRace.Def) return;
            if (!request.FixedGender.HasValue && !request.KindDef.fixedGender.HasValue)
                request.FixedGender = Rand.Chance(settings.maleProbability) ? Gender.Male : Gender.Female;
            // Keep explicit custom xenotypes; their incompatible genes are filtered on insertion.
            if (!request.AllowedDevelopmentalStages.Newborn()
                && request.ForcedCustomXenotype == null
                && !settings.xenotypeList.Contains(request.ForcedXenotype))
                request.ForcedXenotype = settings.xenotypeList.RandomElement();
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.XenotypesAvailableFor))]
    public static class Patch_HelodXenotypeChoices
    {
        public static void Postfix(PawnKindDef kind, Dictionary<XenotypeDef, float> __result)
        {
            HelodRaceExtension settings = HelodRace.Settings;
            if (settings == null) return;
            bool helod = kind.race == HelodRace.Def;
            // Vanilla also reads its shared dictionary directly, so mutate it in place.
            foreach (XenotypeDef xenotype in __result.Keys.ToList())
                if (settings.xenotypeList.Contains(xenotype) != helod) __result.Remove(xenotype);
            if (__result.Count == 0)
            {
                if (helod)
                    foreach (XenotypeDef xenotype in settings.xenotypeList) __result[xenotype] = 1f;
                else __result[XenotypeDefOf.Baseliner] = 1f;
            }
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.AdjustXenotypeForFactionlessPawn))]
    public static class Patch_HelodFactionlessXenotype
    {
        public static void Postfix(Pawn pawn, PawnGenerationRequest request, ref XenotypeDef xenotype)
        {
            HelodRaceExtension settings = HelodRace.Settings;
            if (settings == null || request.ForcedCustomXenotype != null) return;
            bool helod = HelodRace.IsHelod(pawn);
            if (settings.xenotypeList.Contains(xenotype) == helod) return;
            if (helod) xenotype = settings.xenotypeList.RandomElement();
            else if (!DefDatabase<XenotypeDef>.AllDefsListForReading
                .Where(x => !settings.xenotypeList.Contains(x))
                .TryRandomElementByWeight(x => x.factionlessGenerationWeight, out xenotype))
                xenotype = XenotypeDefOf.Baseliner;
        }
    }

    [HarmonyPatch(typeof(StartingPawnUtility), "get_DefaultStartingPawnRequest")]
    public static class Patch_HelodStartingPawn
    {
        public static void Postfix(ref PawnGenerationRequest __result)
        {
            string faction = __result.Faction?.def?.defName;
            if ((faction == "PlayerColony" || faction == "HD_HelodPlayerColony")
                && __result.KindDef == PawnKindDefOf.Colonist
                && __result.ForcedCustomXenotype == null
                && (__result.ForcedXenotype == null || __result.ForcedXenotype == XenotypeDefOf.Baseliner))
            {
                __result.PawnKindDefGetter = null;
                __result.KindDef = DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist");
                __result.ForcedXenotype = null;
            }
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GetBodyTypeFor))]
    public static class Patch_HelodBodyType
    {
        public static bool Prefix(Pawn pawn, ref BodyTypeDef __result)
        {
            if (!HelodRace.IsHelod(pawn)) return true;
            __result = pawn.DevelopmentalStage.Baby() ? BodyTypeDefOf.Baby
                : pawn.DevelopmentalStage.Child() ? BodyTypeDefOf.Child : BodyTypeDefOf.Female;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_StoryTracker), nameof(Pawn_StoryTracker.TryGetRandomHeadFromSet))]
    public static class Patch_HelodHeadChoices
    {
        public static void Prefix(Pawn ___pawn, ref IEnumerable<HeadTypeDef> options)
        {
            if (HelodRace.Settings == null || options == null) return;
            if (HelodRace.IsHelod(___pawn)) options = HelodRace.Settings.headTypes;
            else options = options.Where(head => !HelodRace.Settings.headTypes.Contains(head)
                && !head.defName.StartsWith("HD_HelodHead", StringComparison.Ordinal));
        }
    }

    [HarmonyPatch(typeof(PawnStyleItemChooser), nameof(PawnStyleItemChooser.WantsToUseStyle))]
    public static class Patch_HelodStyleChoices
    {
        public static bool Prefix(Pawn pawn, StyleItemDef styleItemDef, ref bool __result)
        {
            if (!HelodRace.IsHelod(pawn)) return true;
            if (styleItemDef is HairDef hair)
                __result = hair.styleTags.Contains("HelodHair");
            else if (styleItemDef is BeardDef)
                __result = styleItemDef == BeardDefOf.NoBeard;
            else if (styleItemDef is TattooDef)
                __result = styleItemDef == TattooDefOf.NoTattoo_Body
                    || styleItemDef == TattooDefOf.NoTattoo_Face;
            else return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PawnStyleItemChooser), nameof(PawnStyleItemChooser.RandomHairFor))]
    public static class Patch_HelodHair
    {
        public static void Postfix(Pawn pawn, ref HairDef __result)
        {
            if (!HelodRace.IsHelod(pawn) || __result?.styleTags?.Contains("HelodHair") == true) return;
            __result = DefDatabase<HairDef>.AllDefsListForReading
                .Where(h => h.styleTags.Contains("HelodHair")).RandomElement();
        }
    }

    [HarmonyPatch(typeof(PawnHairColors), nameof(PawnHairColors.RandomHairColor))]
    public static class Patch_HelodHairPalette
    {
        private static readonly Color32[] Colors =
        {
            new Color32(92,92,92,255), new Color32(181,132,102,255),
            new Color32(153,69,0,255), new Color32(221,150,0,255),
            new Color32(242,214,153,255), new Color32(148,138,138,255),
            new Color32(181,135,89,255), new Color32(171,40,46,255),
            new Color32(229,193,255,255), new Color32(0,206,255,255),
            new Color32(230,230,230,255)
        };
        private static readonly int[] Weights = { 70,150,150,100,100,50,40,30,40,40,20 };

        public static bool Prefix(Pawn pawn, int ageYears, ref Color __result)
        {
            if (!HelodRace.IsHelod(pawn)) return true;
            // Gene hair overrides still take precedence in the vanilla gene tracker.
            __result = PawnHairColors.HasGreyHair(pawn, ageYears)
                ? PawnHairColors.RandomGreyHairColor()
                : (Color)Colors[Enumerable.Range(0, Colors.Length).RandomElementByWeight(i => Weights[i])];
            return false;
        }
    }
}
