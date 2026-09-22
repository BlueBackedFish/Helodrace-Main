using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    // Only Helod's authored rules live here; no external race framework is needed.
    public sealed class HelodRaceExtension : DefModExtension
    {
        public HelodRaceSettingsDef settingsDef;
    }

    public sealed class HelodRaceSettingsDef : Def
    {
        public List<HelodHeadOffset> headOffsets = new List<HelodHeadOffset>();
        public List<HeadTypeDef> headTypes = new List<HeadTypeDef>();
        public List<HelodApparelGraphic> apparelGraphics = new List<HelodApparelGraphic>();
        public List<ThingDef> apparelList = new List<ThingDef>();
        public List<ThingDef> whiteApparelList = new List<ThingDef>();
        public List<ThingDef> earCoveringApparel = new List<ThingDef>();
        public List<GeneDef> blackGeneList = new List<GeneDef>();
        public List<EndogeneCategory> blackEndoCategories = new List<EndogeneCategory>();
        public List<XenotypeDef> xenotypeList = new List<XenotypeDef>();
        public float drawScale = 0.8f;
        public float maleProbability = 0.0000001f;
        public float socialFightDamageLimit = 6f;
        public float refugeeChance = 0.15f;
        public float slaveChance = 0.15f;
        public float wandererChance = 0.15f;
    }

    public sealed class HelodHeadOffset
    {
        public float minAge;
        public float female;
        public float male;
    }

    public sealed class HelodApparelGraphic
    {
        public ThingDef key;
        public string value;
    }

    public static class HelodRace
    {
        public static ThingDef RaceDef { get; private set; }
        public static ThingDef Def => RaceDef;
        public static HelodRaceSettingsDef Settings { get; private set; }
        public static PawnKindDef ColonistKind { get; private set; }
        public static float DrawScale { get; private set; } = 1f;
        public static bool IsHelod(Pawn pawn) => RaceDef != null && pawn?.def == RaceDef;
        internal static readonly HashSet<HeadTypeDef> HeadTypes = new HashSet<HeadTypeDef>();
        internal static readonly HashSet<HeadTypeDef> NonHumanHeads = new HashSet<HeadTypeDef>();
        internal static readonly HashSet<ThingDef> EarCoveringApparel = new HashSet<ThingDef>();
        internal static readonly HashSet<GeneDef> ForbiddenGenes = new HashSet<GeneDef>();
        internal static readonly HashSet<EndogeneCategory> ForbiddenCategories = new HashSet<EndogeneCategory>();
        internal static readonly HashSet<XenotypeDef> XenotypeSet = new HashSet<XenotypeDef>();
        internal static readonly HashSet<HairDef> HairSet = new HashSet<HairDef>();
        internal static readonly List<HairDef> HelodHairs = new List<HairDef>();
        internal static readonly List<XenotypeDef> HelodXenotypes = new List<XenotypeDef>();
        internal static readonly List<XenotypeDef> NonHelodXenotypes = new List<XenotypeDef>();
        internal static readonly List<BodyPartRecord> TailParts = new List<BodyPartRecord>();
        internal static readonly List<BodyPartRecord> LeftEarParts = new List<BodyPartRecord>();
        internal static readonly List<BodyPartRecord> RightEarParts = new List<BodyPartRecord>();
        private static readonly Dictionary<ApparelProperties, ThingDef> apparelOwners =
            new Dictionary<ApparelProperties, ThingDef>();
        private static readonly Dictionary<ThingDef, string> apparelPaths =
            new Dictionary<ThingDef, string>();
        private static readonly HashSet<ThingDef> permittedApparel = new HashSet<ThingDef>();
        private static readonly HashSet<ThingDef> exclusiveApparel = new HashSet<ThingDef>();

        // Explicit rebuild boundary: startup after Def loading, never a rendering fallback.
        public static void RebuildRuntimeCaches()
        {
            RaceDef = DefDatabase<ThingDef>.GetNamedSilentFail("Helod");
            Settings = RaceDef?.GetModExtension<HelodRaceExtension>()?.settingsDef;
            ColonistKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("HD_WW_HelodColonist");
            DrawScale = Settings?.drawScale ?? 1f;
            HeadTypes.Clear();
            NonHumanHeads.Clear();
            EarCoveringApparel.Clear();
            ForbiddenGenes.Clear();
            ForbiddenCategories.Clear();
            XenotypeSet.Clear();
            HairSet.Clear();
            HelodHairs.Clear();
            HelodXenotypes.Clear();
            NonHelodXenotypes.Clear();
            TailParts.Clear();
            LeftEarParts.Clear();
            RightEarParts.Clear();
            apparelOwners.Clear();
            apparelPaths.Clear();
            permittedApparel.Clear();
            exclusiveApparel.Clear();
            HelodRaceSettingsDef settings = Settings;
            if (settings == null) { RaceDef = null; return; }
            HeadTypes.UnionWith(settings.headTypes);
            NonHumanHeads.UnionWith(settings.headTypes);
            foreach (HeadTypeDef head in DefDatabase<HeadTypeDef>.AllDefsListForReading)
                if (head.defName.StartsWith("HD_HelodHead", StringComparison.Ordinal)) NonHumanHeads.Add(head);
            EarCoveringApparel.UnionWith(settings.earCoveringApparel);
            ForbiddenGenes.UnionWith(settings.blackGeneList);
            ForbiddenCategories.UnionWith(settings.blackEndoCategories);
            XenotypeSet.UnionWith(settings.xenotypeList);
            HelodXenotypes.AddRange(settings.xenotypeList);
            foreach (XenotypeDef xenotype in DefDatabase<XenotypeDef>.AllDefsListForReading)
                if (!XenotypeSet.Contains(xenotype)) NonHelodXenotypes.Add(xenotype);
            foreach (HairDef hair in DefDatabase<HairDef>.AllDefsListForReading)
                if (hair.styleTags != null && hair.styleTags.Contains("HelodHair"))
                { HelodHairs.Add(hair); HairSet.Add(hair); }
            if (RaceDef.race?.body != null)
                foreach (BodyPartRecord part in RaceDef.race.body.AllParts)
                {
                    if (part.def.defName == "HD_HelodTail") TailParts.Add(part);
                    if (PawnRenderNodeWorker_HelodAppendage.MatchesEar(part, "left ear")) LeftEarParts.Add(part);
                    if (PawnRenderNodeWorker_HelodAppendage.MatchesEar(part, "right ear")) RightEarParts.Add(part);
                }
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (def.apparel != null) apparelOwners[def.apparel] = def;
            foreach (HelodApparelGraphic entry in settings.apparelGraphics)
                if (entry.key != null) apparelPaths[entry.key] = entry.value;
            exclusiveApparel.UnionWith(settings.apparelList);
            permittedApparel.UnionWith(settings.apparelList);
            permittedApparel.UnionWith(settings.whiteApparelList);
            PatchHelodShellApparelLayer.RebuildCache();
        }

        public static void Initialize()
        {
            RebuildRuntimeCaches();
            HelodRaceSettingsDef settings = Settings;
            if (settings == null)
            {
                Log.Error("[Helodrace] Missing standalone race settings.");
                return;
            }
            // Import human operations whose target parts exist in the Helod body.
            // AllRecipes also includes operations declared through recipeUsers.
            Def.recipes = (Def.recipes ?? new List<RecipeDef>())
                .Concat(ThingDefOf.Human.AllRecipes.Where(recipe =>
                    !recipe.targetsBodyPart || recipe.appliedOnFixedBodyParts.NullOrEmpty()
                    || recipe.appliedOnFixedBodyParts.Any(part =>
                        Def.race.body.AllParts.Any(record => record.def == part))))
                .Distinct().ToList();
            AccessTools.Field(typeof(ThingDef), "allRecipesCached")?.SetValue(Def, null);
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
                if (recipe.defaultIngredientFilter != null
                    && !recipe.defaultIngredientFilter.Allows(ThingDefOf.Meat_Human)
                    && Def.race.meatDef != null)
                    recipe.defaultIngredientFilter.SetAllow(Def.race.meatDef, false);
            Log.Message("[Helodrace] Standalone race initialized: "
                + settings.headTypes.Count + " heads, " + apparelPaths.Count
                + " apparel graphics, " + Def.recipes.Count + " operations.");
        }

        public static string ApparelPath(Apparel apparel)
        {
            return apparel != null && IsHelod(apparel.Wearer)
                && apparelPaths.TryGetValue(apparel.def, out string path) ? path : null;
        }

        public static bool CanWear(Pawn pawn, ThingDef apparel)
        {
            if (Settings == null || apparel == null || !apparel.IsApparel) return true;
            return IsHelod(pawn) ? permittedApparel.Contains(apparel)
                : !exclusiveApparel.Contains(apparel);
        }

        public static bool CanWear(Pawn pawn, ApparelProperties properties)
        {
            return properties == null || !apparelOwners.TryGetValue(properties, out ThingDef def) || CanWear(pawn, def);
        }

        public static bool CanHaveTrait(Pawn pawn, Trait trait)
        {
            if (Settings == null) return true;
            if (!IsHelod(pawn)) return trait.def.defName != "HD_RawBTXMadman";
            return trait.def.defName != "CreepyBreathing"
                && trait.def.defName != "BodyPurist"
                && !(trait.def.defName == "SpeedOffset" && trait.Degree == -1);
        }

        public static bool CanHaveGene(Pawn pawn, GeneDef gene)
        {
            return !IsHelod(pawn) || gene == null
                || (!ForbiddenGenes.Contains(gene)
                    && !ForbiddenCategories.Contains(gene.endogeneCategory));
        }
    }

    [HarmonyPatch(typeof(ApparelProperties), nameof(ApparelProperties.PawnCanWear),
        new[] { typeof(Pawn), typeof(bool) })]
    public static class Patch_HelodApparelPermission
    {
        public static void Postfix(ApparelProperties __instance, Pawn pawn, ref bool __result)
            => __result &= HelodRace.CanWear(pawn, __instance);
    }

    [HarmonyPatch(typeof(EquipmentUtility), nameof(EquipmentUtility.CanEquip),
        new[] { typeof(Thing), typeof(Pawn), typeof(string), typeof(bool) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    public static class Patch_HelodEquipPermission
    {
        public static void Postfix(Thing thing, Pawn pawn, ref string cantReason, ref bool __result)
        {
            if (__result && !HelodRace.CanWear(pawn, thing.def))
            {
                __result = false;
                cantReason = "HD_Race_ApparelRestricted".Translate();
            }
        }
    }

    [HarmonyPatch(typeof(TraitSet), nameof(TraitSet.GainTrait))]
    public static class Patch_HelodTraitPermission
    {
        public static bool Prefix(Pawn ___pawn, Trait trait) => HelodRace.CanHaveTrait(___pawn, trait);
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GenerateTraitsFor))]
    public static class Patch_HelodTraitChoices
    {
        [ThreadStatic] private static bool refilling;

        public static void Postfix(Pawn pawn, int traitCount, PawnGenerationRequest? req,
            bool growthMomentTrait, List<Trait> __result)
        {
            // Also removes forbidden traits from growth-moment offers.
            __result.RemoveAll(trait => !HelodRace.CanHaveTrait(pawn, trait));
            if (refilling || __result.Count >= traitCount) return;
            refilling = true;
            try
            {
                for (int attempt = 0; attempt < 100 && __result.Count < traitCount; attempt++)
                    foreach (Trait candidate in PawnGenerator.GenerateTraitsFor(pawn, 1, req, growthMomentTrait))
                        if (!__result.Any(existing => existing.def == candidate.def
                            || existing.def.ConflictsWith(candidate) || candidate.def.ConflictsWith(existing)))
                            __result.Add(candidate);
            }
            finally { refilling = false; }
        }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "AddGene", new[] { typeof(Gene), typeof(bool) })]
    public static class Patch_HelodGenePermission
    {
        public static bool Prefix(Pawn_GeneTracker __instance, Gene gene, ref Gene __result)
        {
            if (HelodRace.CanHaveGene(__instance.pawn, gene.def)) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_StoryTracker), "get_SkinColorBase")]
    public static class Patch_HelodSkinColor
    {
        public static bool Prefix(Pawn ___pawn, ref Color __result)
        {
            if (!HelodRace.IsHelod(___pawn)) return true;
            // The painted body/head textures already contain their skin colors.
            __result = Color.white;
            return false;
        }
    }
}
