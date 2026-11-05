using System.Collections.Generic;
using System.Linq;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    // A production field such as turning or milling. Facilities offer fields at a
    // level, recipes require fields at a minimum level.
    public class FacilityFieldDef : Def
    {
        public int order;
        // The satisfying facility must be powered, switched on and not broken down.
        public bool requiresOperational;
        // The satisfying worktable facility takes the production job's work load.
        public bool drivesFacilityWork;
    }

    // XML form: <HD_Turning>2</HD_Turning>
    public sealed class FacilityLevel
    {
        public FacilityFieldDef field;
        public int level = 1;

        public void LoadDataFromXmlCustom(XmlNode xmlRoot)
        {
            DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(this, nameof(field), xmlRoot.Name);
            level = ParseHelper.FromString<int>(xmlRoot.FirstChild.Value);
        }

        public string Label => "HD_Production_FacilityLevel".Translate(this.field.LabelCap, level).Resolve();
    }

    // On a facility ThingDef: the fields it provides to linked workbenches.
    public class FacilityCapabilityExtension : DefModExtension
    {
        public List<FacilityLevel> capabilities = new List<FacilityLevel>();

        public int LevelOf(FacilityFieldDef field)
        {
            int best = 0;
            foreach (FacilityLevel capability in capabilities)
                if (capability.field == field && capability.level > best) best = capability.level;
            return best;
        }
    }

    // On a RecipeDef, or on the produced ThingDef for every recipe making it.
    public class FacilityRequirementExtension : DefModExtension
    {
        public List<FacilityLevel> requirements = new List<FacilityLevel>();
    }

    internal static class LinkedFacilityRecipes
    {
        private static readonly List<FacilityLevel> None = new List<FacilityLevel>();
        private static readonly Dictionary<RecipeDef, List<FacilityLevel>> requirementCache = new();

        internal static List<FacilityLevel> Requirements(RecipeDef recipe)
        {
            if (recipe == null) return None;
            if (requirementCache.TryGetValue(recipe, out var cached)) return cached;
            List<FacilityLevel> result = recipe.GetModExtension<FacilityRequirementExtension>()?.requirements
                ?? recipe.ProducedThingDef?.GetModExtension<FacilityRequirementExtension>()?.requirements
                ?? None;
            result = result.OrderBy(r => r.field.order).ToList();
            requirementCache.Add(recipe, result);
            return result;
        }

        public static bool RequiresFacility(RecipeDef recipe) => Requirements(recipe).Count > 0;

        public static bool IsAvailable(RecipeDef recipe, Thing billGiver)
        {
            foreach (FacilityLevel requirement in Requirements(recipe))
                if (SatisfyingFacility(requirement, billGiver) == null) return false;
            return true;
        }

        internal static Thing SatisfyingFacility(FacilityLevel requirement, Thing billGiver)
        {
            if (billGiver == null || !billGiver.Spawned) return null;
            var affected = billGiver.TryGetComp<CompAffectedByFacilities>();
            if (affected == null) return null;
            foreach (Thing facility in affected.LinkedFacilitiesListForReading)
            {
                if (facility == null || !affected.IsFacilityActive(facility)) continue;
                var capability = facility.def.GetModExtension<FacilityCapabilityExtension>();
                if (capability == null || capability.LevelOf(requirement.field) < requirement.level) continue;
                if (requirement.field.requiresOperational && !IsOperational(facility)) continue;
                return facility;
            }
            return null;
        }

        internal static bool IsOperational(Thing facility)
        {
            if (facility?.Spawned != true || !FlickUtility.WantsToBeOn(facility) || facility.IsBrokenDown()) return false;
            var mechanical = facility.TryGetComp<CompMechanicalUser>();
            if (mechanical != null) return mechanical.HasPower && mechanical.RealRPM >= mechanical.Props.minimalRPM;
            var power = facility.TryGetComp<CompPowerTrader>();
            return power == null || power.PowerOn;
        }

        internal static void DriveFacilities(RecipeDef recipe, Thing billGiver)
        {
            foreach (FacilityLevel requirement in Requirements(recipe))
                if (requirement.field.drivesFacilityWork && SatisfyingFacility(requirement, billGiver) is Building_WorkTable table)
                    table.UsedThisTick();
        }

        internal static string RequiredFacilitiesText(RecipeDef recipe)
        {
            var requirements = Requirements(recipe);
            if (requirements.Count == 0) return "HD_Production_NoFacility".Translate().Resolve();
            return string.Join(" + ", requirements.Select(r => r.Label));
        }
    }

    [HarmonyPatch(typeof(Toils_Recipe), nameof(Toils_Recipe.DoRecipeWork))]
    internal static class Patch_RecipeFacilityWork
    {
        private static void Postfix(Toil __result)
        {
            __result.AddPreTickAction(() =>
            {
                Job job = __result.actor.CurJob;
                if (job?.bill?.recipe != null) LinkedFacilityRecipes.DriveFacilities(job.bill.recipe, job.targetA.Thing);
            });
        }
    }

    [HarmonyPatch(typeof(RecipeWorker), nameof(RecipeWorker.AvailableOnNow))]
    internal static class Patch_RecipeFacilityAvailability
    {
        private static void Postfix(RecipeDef ___recipe, Thing thing, ref bool __result)
        {
            if (__result && LinkedFacilityRecipes.RequiresFacility(___recipe))
                __result = LinkedFacilityRecipes.IsAvailable(___recipe, thing);
        }
    }

    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
    internal static class Patch_LinkedFacilityBillAvailability
    {
        private static void Postfix(Bill_Production __instance, ref bool __result)
        {
            if (__result && LinkedFacilityRecipes.RequiresFacility(__instance.recipe))
                __result = LinkedFacilityRecipes.IsAvailable(__instance.recipe, __instance.billStack?.billGiver as Thing);
        }
    }

    [HarmonyPatch(typeof(JobDriver_DoBill), "MakeNewToils")]
    internal static class Patch_LinkedFacilityBillWork
    {
        private static void Prefix(JobDriver_DoBill __instance)
        {
            __instance.AddFailCondition(delegate
            {
                return LinkedFacilityRecipes.RequiresFacility(__instance.job?.bill?.recipe)
                    && !LinkedFacilityRecipes.IsAvailable(__instance.job.bill.recipe, __instance.BillGiver as Thing);
            });
        }
    }
}
