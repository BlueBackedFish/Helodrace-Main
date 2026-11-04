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
        public bool requireOperationalFacility;
    }

    // Worktables can also act as facilities. Higher tiers satisfy lower-tier
    // firearm requirements when their ThingDef is linkable to the workbench.
    public class MachineToolFacilityExtension : DefModExtension
    {
        public int latheTier;
        public int millingTier;
        public int pressTier;
    }

    public class ArmorProductionExtension : DefModExtension
    {
        public bool requiresHydraulicPress;
    }

    internal static class LinkedFacilityRecipes
    {
        private static HashSet<ResearchProjectDef> earlyGunResearch;

        public static bool RequiresFacility(RecipeDef recipe)
        {
            return recipe != null && (recipe.GetModExtension<RecipeExtension_RequiredFacility>() != null
                || RequiresArmorPress(recipe) || IsGunAssemblyRecipe(recipe));
        }

        public static bool IsAvailable(RecipeDef recipe, Thing billGiver)
        {
            RecipeExtension_RequiredFacility requirement = recipe?.GetModExtension<RecipeExtension_RequiredFacility>();
            if (requirement != null)
            {
                return HasSpecificFacility(requirement, billGiver);
            }

            if (RequiresArmorPress(recipe)) return OperationalArmorPress(billGiver) != null;
            return !IsGunAssemblyRecipe(recipe) || HasGunMachineTools(recipe, billGiver);
        }

        internal static bool RequiresArmorPress(RecipeDef recipe)
            => recipe?.ProducedThingDef?.GetModExtension<ArmorProductionExtension>()?.requiresHydraulicPress == true;

        internal static Building_WorkTable OperationalArmorPress(Thing billGiver)
        {
            var affected = billGiver?.TryGetComp<CompAffectedByFacilities>();
            if (affected == null) return null;
            foreach (Thing facility in affected.LinkedFacilitiesListForReading)
            {
                if (facility is not Building_WorkTable press
                    || facility.def.GetModExtension<MachineToolFacilityExtension>()?.pressTier < 1
                    || facility.def.GetModExtension<MachineToolFacilityExtension>() == null
                    || !affected.IsFacilityActive(facility)
                    || !FlickUtility.WantsToBeOn(facility) || facility.IsBrokenDown()) continue;
                var mechanical = facility.TryGetComp<CompMechanicalUser>();
                if (mechanical != null)
                {
                    if (mechanical.HasPower && mechanical.RealRPM >= mechanical.Props.minimalRPM) return press;
                }
                else if (facility.TryGetComp<CompPowerTrader>()?.PowerOn == true) return press;
            }
            return null;
        }

        internal static string RequiredFacilitiesText(RecipeDef recipe)
        {
            var specific = recipe.GetModExtension<RecipeExtension_RequiredFacility>();
            if (specific != null) return specific.facility.LabelCap;
            if (RequiresArmorPress(recipe)) return "HD_Production_ArmorPress".Translate().Resolve();
            if (!IsGunAssemblyRecipe(recipe)) return "HD_Production_NoFacility".Translate().Resolve();
            return (IsEarlyGunRecipe(recipe) ? "HD_Production_EarlyGunTools" : "HD_Production_LateGunTools").Translate().Resolve();
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
                if (facility.def == requirement.facility && affected.IsFacilityActive(facility)
                    && (!requirement.requireOperationalFacility || IsOperationalFacility(facility)))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsOperationalFacility(Thing facility)
            => facility?.Spawned == true && facility.TryGetComp<CompPowerTrader>()?.PowerOn == true
                && FlickUtility.WantsToBeOn(facility) && !facility.IsBrokenDown();

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

    [HarmonyPatch(typeof(Toils_Recipe), nameof(Toils_Recipe.DoRecipeWork))]
    internal static class Patch_ArmorRecipePressWork
    {
        private static void Postfix(Toil __result)
        {
            __result.AddPreTickAction(() =>
            {
                Job job = __result.actor.CurJob;
                if (LinkedFacilityRecipes.RequiresArmorPress(job?.bill?.recipe))
                    LinkedFacilityRecipes.OperationalArmorPress(job.targetA.Thing)?.UsedThisTick();
            });
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
