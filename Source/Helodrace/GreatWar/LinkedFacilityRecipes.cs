using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class RecipeExtension_RequiredFacility : DefModExtension
    {
        public ThingDef workTable;
        public ThingDef facility;
    }

    internal static class LinkedFacilityRecipes
    {
        public static bool IsAvailable(RecipeDef recipe, Thing billGiver)
        {
            RecipeExtension_RequiredFacility requirement = recipe?.GetModExtension<RecipeExtension_RequiredFacility>();
            if (requirement == null)
            {
                return true;
            }

            if (billGiver == null || !billGiver.Spawned || billGiver.def != requirement.workTable)
            {
                return false;
            }

            CompAffectedByFacilities affected = billGiver.TryGetComp<CompAffectedByFacilities>();
            if (affected == null)
            {
                return false;
            }

            foreach (Thing facility in affected.LinkedFacilitiesListForReading)
            {
                if (facility.def == requirement.facility && affected.IsFacilityActive(facility))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class RecipeWorker_RequiresLinkedFacility : RecipeWorker
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            return base.AvailableOnNow(thing, part) && LinkedFacilityRecipes.IsAvailable(recipe, thing);
        }
    }

    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
    internal static class Patch_LinkedFacilityBillAvailability
    {
        private static void Postfix(Bill_Production __instance, ref bool __result)
        {
            if (__result && __instance.recipe.GetModExtension<RecipeExtension_RequiredFacility>() != null)
            {
                __result = LinkedFacilityRecipes.IsAvailable(__instance.recipe, __instance.billStack?.billGiver as Thing);
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver_DoBill), "MakeNewToils")]
    internal static class Patch_LinkedFacilityBillWork
    {
        private static void Prefix(JobDriver_DoBill __instance)
        {
            __instance.AddFailCondition(delegate
            {
                return __instance.job?.bill?.recipe?.GetModExtension<RecipeExtension_RequiredFacility>() != null
                    && !LinkedFacilityRecipes.IsAvailable(__instance.job.bill.recipe, __instance.BillGiver as Thing);
            });
        }
    }
}
