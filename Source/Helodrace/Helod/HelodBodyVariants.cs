using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    public static class HelodBodyVariants
    {
        public static readonly string[] Sizes = { "None", "Tiny", "Small", "Mid", "Big", "Huge" };
        private static readonly Dictionary<string, string> Fitted = new Dictionary<string, string>
        {
            { "HD_Apparel_Hoodie", "HD_Hoodie" },
            { "HD_Apparel_CarvalyUniform", "HD_Cavalry" },
            { "HD_Apparel_GreatWarTrenchArmyInner", "HD_GreatWarTrenchArmyInner" },
            { "HD_Apparel_GreatWarTrenchArmyOuter", "HD_GreatWarTrenchArmyOuter" },
            { "HD_Apparel_GreatWarStormFrontInner", "HD_GreatWarStormFrontInner" },
            { "HD_Apparel_GreatWarStormFrontOuter", "HD_GreatWarStormFrontOuter" },
            { "HD_Apparel_GreatWarStormFrontChestRig", "HD_GreatWarStormFrontBelt" },
            { "HD_Apparel_ACSUpper", "HD_ACSUpper" },
            { "HD_Apparel_UCPBlouse", "HD_UCPBlouse" }
        };
        // These rigid shells keep the authored single-body graphics. Do not infer
        // compatibility from armor stats: ordinary shirts can also have armor stats.
        private static readonly HashSet<string> Independent = new HashSet<string>
        {
            "HD_Apparel_IBTVAssault", "HD_Apparel_M1952AFlakJacket",
            "HD_WildWestLeatherVest", "HD_WildWestMetalVest",
            "Apparel_FlakVest", "Apparel_PlateArmor", "Apparel_PowerArmor",
            "Apparel_ArmorRecon", "Apparel_ArmorCataphract"
        };

        public static bool Active(Pawn pawn) => RaidSettings.BodyVariants
            && HelodRace.IsHelod(pawn) && pawn.DevelopmentalStage.Adult();

        public static string SizeFor(Pawn pawn)
        {
            var comp = pawn.TryGetComp<CompHelodBodyVariant>();
            return Sizes[comp?.Variant ?? DefaultVariant(pawn)];
        }

        internal static int DefaultVariant(Pawn pawn) => pawn.gender == Gender.Male
            ? 0 : (int)((uint)pawn.thingIDNumber % (uint)Sizes.Length);

        public static string BodyPath(Pawn pawn) => "Helod/BodyVariants/Bodies/Naked_Female_" + SizeFor(pawn);

        public static string ApparelPath(Apparel apparel)
        {
            if (apparel == null) return null;
            bool hoodie = apparel.def.defName == "HD_Apparel_Hoodie";
            if (!Active(apparel.Wearer)) return hoodie
                ? "Helod/BodyVariants/Apparel/HD_Hoodie/Huge/HD_Hoodie" : null;
            if (apparel.def.defName == "HD_Apparel_UCPPants")
                return "Helod/BodyVariants/Apparel/HD_UCPPants/HD_UCPPants";
            return Fitted.TryGetValue(apparel.def.defName, out string name)
                ? "Helod/BodyVariants/Apparel/" + name + "/" + SizeFor(apparel.Wearer) + "/" + name
                    + (apparel.def.apparel.LastLayer == ApparelLayerDefOf.Belt ? "_Female" : "")
                : null;
        }

        public static bool Supports(Pawn pawn, ThingDef def) => !Active(pawn)
            || Fitted.ContainsKey(def.defName) || IsIndependent(def);

        public static bool IsIndependent(ThingDef def)
        {
            if (Independent.Contains(def.defName) || def.defName == "HD_Apparel_UCPPants") return true;
            ApparelProperties apparel = def.apparel;
            if (apparel.wornGraphicPath.NullOrEmpty() && apparel.wornGraphicPaths.NullOrEmpty()
                && !HelodRace.HasApparelGraphic(def)) return true;
            if (apparel.wornGraphicData?.renderUtilityAsPack == true) return true;
            if (apparel.layers.All(layer => layer == ApparelLayerDefOf.Belt)) return true;
            // Headgear and utility equipment do not follow the torso/leg outline.
            return !apparel.bodyPartGroups.Any(group => group.defName == "Torso"
                || group.defName == "Shoulders" || group.defName == "Legs" || group.defName == "Waist");
        }

        public static void ApplyToLoadedPawns()
        {
            if (Current.Game == null) return;
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive.ToArray())
                if (HelodRace.IsHelod(pawn)) pawn.TryGetComp<CompHelodBodyVariant>()?.Synchronize();
        }
    }

    public sealed class CompProperties_HelodBodyVariant : CompProperties
    {
        public CompProperties_HelodBodyVariant() { compClass = typeof(CompHelodBodyVariant); }
    }

    public sealed class CompHelodBodyVariant : ThingComp
    {
        private int variant = -1;
        private bool applied;
        public int Variant
        {
            get
            {
                if (variant < 0 || variant >= HelodBodyVariants.Sizes.Length)
                    variant = HelodBodyVariants.DefaultVariant((Pawn)parent);
                return variant;
            }
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref variant, "bodyVariant", -1);
            Scribe_Values.Look(ref applied, "bodyVariantsApplied", false);
        }

        // Generation already filters outfits using the current setting. A newly
        // generated soldier must keep its fitted uniform rather than get civilian gear.
        public void InitializeGenerated() { applied = HelodBodyVariants.Active((Pawn)parent); }
        public override void PostSpawnSetup(bool respawningAfterLoad) { Synchronize(); }
        public override void CompTickRare() { Synchronize(); }
        public override void CompTick()
        {
            if (parent.IsHashIntervalTick(250)) Synchronize();
        }

        public void Synchronize()
        {
            Pawn pawn = (Pawn)parent;
            if (pawn.Dead || pawn.apparel == null || !pawn.DevelopmentalStage.Adult()) return;
            bool active = HelodBodyVariants.Active(pawn);
            if (applied == active) return;
            ThingDef hoodieDef = DefDatabase<ThingDef>.GetNamed("HD_Apparel_Hoodie");
            ThingDef pantsDef = DefDatabase<ThingDef>.GetNamed(active ? "HD_Apparel_UCPPants" : "Apparel_Pants");
            // Preserve removed garments and their quality, dye, damage and installed parts.
            foreach (Apparel item in pawn.apparel.WornApparel.ToArray())
            {
                bool fittedClothing = item.def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Torso)
                    && !HelodBodyVariants.IsIndependent(item.def);
                if (!fittedClothing && HelodRace.CanWear(pawn, item.def)
                    && ApparelUtility.CanWearTogether(item.def, hoodieDef, pawn.RaceProps.body)
                    && ApparelUtility.CanWearTogether(item.def, pantsDef, pawn.RaceProps.body)) continue;
                pawn.apparel.Remove(item);
                if (pawn.Spawned && GenPlace.TryPlaceThing(item, pawn.Position, pawn.Map, ThingPlaceMode.Near)) continue;
                pawn.inventory.innerContainer.TryAdd(item);
            }
            WearIfMissing(pawn, hoodieDef);
            WearIfMissing(pawn, pantsDef);
            applied = active;
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(pawn);
        }

        private static void WearIfMissing(Pawn pawn, ThingDef def)
        {
            if (pawn.apparel.WornApparel.Any(item => item.def == def)) return;
            var item = (Apparel)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            pawn.apparel.Wear(item, false);
        }
    }

    public sealed class HelodBodyVariantGameComponent : GameComponent
    {
        public HelodBodyVariantGameComponent(Game game) { }
        public override void FinalizeInit() { HelodBodyVariants.ApplyToLoadedPawns(); }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_HelodGeneratedBodyVariant
    {
        public static void Postfix(Pawn __result)
        {
            if (HelodRace.IsHelod(__result)) __result.TryGetComp<CompHelodBodyVariant>()?.InitializeGenerated();
        }
    }
}
