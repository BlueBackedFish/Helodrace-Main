using System.Collections.Generic;
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

    // Worktables can also act as facilities. Higher tiers satisfy lower-tier
    // firearm requirements when their ThingDef is linkable to the workbench.
    public class MachineToolFacilityExtension : DefModExtension
    {
        public int latheTier;
        public int millingTier;
    }

    internal static class LinkedFacilityRecipes
    {
        private static HashSet<ResearchProjectDef> earlyGunResearch;

        public static bool RequiresFacility(RecipeDef recipe)
        {
            return recipe != null && (recipe.GetModExtension<RecipeExtension_RequiredFacility>() != null
                || IsGunAssemblyRecipe(recipe));
        }

        public static bool IsAvailable(RecipeDef recipe, Thing billGiver)
        {
            RecipeExtension_RequiredFacility requirement = recipe?.GetModExtension<RecipeExtension_RequiredFacility>();
            if (requirement != null)
            {
                return HasSpecificFacility(requirement, billGiver);
            }

            return !IsGunAssemblyRecipe(recipe) || HasGunMachineTools(recipe, billGiver);
        }

        private static bool HasSpecificFacility(RecipeExtension_RequiredFacility requirement, Thing billGiver)
        {
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

        private static bool IsGunAssemblyRecipe(RecipeDef recipe)
        {
            if (recipe?.ProducedThingDef?.defName == null
                || !recipe.ProducedThingDef.defName.StartsWith("HD_Gun_")
                || recipe.recipeUsers == null)
            {
                return false;
            }

            foreach (ThingDef worktable in recipe.recipeUsers)
            {
                if (IsGunWorkbench(worktable))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasGunMachineTools(RecipeDef recipe, Thing billGiver)
        {
            if (billGiver == null || !billGiver.Spawned || !IsGunWorkbench(billGiver.def))
            {
                return false;
            }

            CompAffectedByFacilities affected = billGiver.TryGetComp<CompAffectedByFacilities>();
            if (affected == null)
            {
                return false;
            }

            int requiredLatheTier = IsEarlyGunRecipe(recipe) ? 1 : 2;
            bool hasLathe = false;
            bool hasMill = requiredLatheTier == 1;
            foreach (Thing facility in affected.LinkedFacilitiesListForReading)
            {
                if (facility == null || !affected.IsFacilityActive(facility))
                {
                    continue;
                }

                MachineToolFacilityExtension machine = facility.def.GetModExtension<MachineToolFacilityExtension>();
                if (machine == null)
                {
                    continue;
                }

                hasLathe |= machine.latheTier >= requiredLatheTier;
                hasMill |= machine.millingTier >= 1;
                if (hasLathe && hasMill)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGunWorkbench(ThingDef worktable)
        {
            return worktable?.defName == "HD_BasicWorkbench"
                || worktable?.defName == "HD_GunSmithTable";
        }

        private static bool IsEarlyGunRecipe(RecipeDef recipe)
        {
            if (earlyGunResearch == null)
            {
                earlyGunResearch = new HashSet<ResearchProjectDef>();
                AddPrerequisites(DefDatabase<ResearchProjectDef>.GetNamed("HelodBreechLoadingGun", false));
            }

            return recipe.researchPrerequisite != null
                && earlyGunResearch.Contains(recipe.researchPrerequisite);
        }

        private static void AddPrerequisites(ResearchProjectDef research)
        {
            if (research == null || !earlyGunResearch.Add(research) || research.prerequisites == null)
            {
                return;
            }

            foreach (ResearchProjectDef prerequisite in research.prerequisites)
            {
                AddPrerequisites(prerequisite);
            }
        }
    }

    public class RecipeWorker_RequiresLinkedFacility : RecipeWorker
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            return base.AvailableOnNow(thing, part) && LinkedFacilityRecipes.IsAvailable(recipe, thing);
        }
    }

    [HarmonyPatch(typeof(RecipeWorker), nameof(RecipeWorker.AvailableOnNow))]
    internal static class Patch_GunRecipeFacilityAvailability
    {
        private static void Postfix(RecipeDef ___recipe, Thing thing, ref bool __result)
        {
            if (__result && LinkedFacilityRecipes.RequiresFacility(___recipe))
            {
                __result = LinkedFacilityRecipes.IsAvailable(___recipe, thing);
            }
        }
    }

    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
    internal static class Patch_LinkedFacilityBillAvailability
    {
        private static void Postfix(Bill_Production __instance, ref bool __result)
        {
            if (__result && LinkedFacilityRecipes.RequiresFacility(__instance.recipe))
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
                return LinkedFacilityRecipes.RequiresFacility(__instance.job?.bill?.recipe)
                    && !LinkedFacilityRecipes.IsAvailable(__instance.job.bill.recipe, __instance.BillGiver as Thing);
            });
        }
    }
}
