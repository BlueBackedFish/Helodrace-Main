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
        public List<HeadTypeDef> headTypes = new List<HeadTypeDef>();
        public List<HelodApparelGraphic> apparelGraphics = new List<HelodApparelGraphic>();
        public List<ThingDef> apparelList = new List<ThingDef>();
        public List<ThingDef> whiteApparelList = new List<ThingDef>();
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

    public sealed class HelodApparelGraphic
    {
        public ThingDef key;
        public string value;
    }

    public static class HelodRace
    {
        public static ThingDef Def => DefDatabase<ThingDef>.GetNamedSilentFail("Helod");
        public static HelodRaceExtension Settings => Def?.GetModExtension<HelodRaceExtension>();
        public static bool IsHelod(Pawn pawn) => pawn?.def?.defName == "Helod";
        private static readonly Dictionary<ApparelProperties, ThingDef> apparelOwners =
            new Dictionary<ApparelProperties, ThingDef>();
        private static readonly Dictionary<ThingDef, string> apparelPaths =
            new Dictionary<ThingDef, string>();
        private static readonly HashSet<ThingDef> permittedApparel = new HashSet<ThingDef>();
        private static readonly HashSet<ThingDef> exclusiveApparel = new HashSet<ThingDef>();

        public static void Initialize()
        {
            HelodRaceExtension settings = Settings;
            if (settings == null)
            {
                Log.Error("[Helodrace] Missing standalone race settings.");
                return;
            }
            apparelOwners.Clear();
            apparelPaths.Clear();
            permittedApparel.Clear();
            exclusiveApparel.Clear();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (def.apparel != null) apparelOwners[def.apparel] = def;
            foreach (HelodApparelGraphic entry in settings.apparelGraphics)
                if (entry.key != null) apparelPaths[entry.key] = entry.value;
            exclusiveApparel.UnionWith(settings.apparelList);
            permittedApparel.UnionWith(settings.apparelList);
            permittedApparel.UnionWith(settings.whiteApparelList);

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
            if (apparel == null || !apparel.IsApparel) return true;
            return IsHelod(pawn) ? permittedApparel.Contains(apparel)
                : !exclusiveApparel.Contains(apparel);
        }

        public static bool CanWear(Pawn pawn, ApparelProperties properties)
        {
            return !apparelOwners.TryGetValue(properties, out ThingDef def) || CanWear(pawn, def);
        }

        public static bool CanHaveTrait(Pawn pawn, Trait trait)
        {
            if (!IsHelod(pawn)) return trait.def.defName != "HD_RawBTXMadman";
            return trait.def.defName != "CreepyBreathing"
                && trait.def.defName != "BodyPurist"
                && !(trait.def.defName == "SpeedOffset" && trait.Degree == -1);
        }

        public static bool CanHaveGene(Pawn pawn, GeneDef gene)
        {
            HelodRaceExtension settings = Settings;
            return !IsHelod(pawn) || settings == null || gene == null
                || (!settings.blackGeneList.Contains(gene)
                    && !settings.blackEndoCategories.Contains(gene.endogeneCategory));
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
