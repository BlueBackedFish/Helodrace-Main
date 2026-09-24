using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    internal static class MechanicalWorkTablePowerUtility
    {
        public static bool HasRequiredPower(Thing thing)
        {
            CompMechanicalUser mechanicalUser = thing?.TryGetComp<CompMechanicalUser>();
            return mechanicalUser == null || mechanicalUser.HasPower;
        }
    }

    /// <summary>
    /// Vanilla work tables know how to reject missing electrical power and fuel,
    /// but do not know about the Helod mechanical network.
    /// </summary>
    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.CurrentlyUsableForBills))]
    internal static class Patch_MechanicalWorkTable_CurrentlyUsableForBills
    {
        private static void Postfix(Building_WorkTable __instance, ref bool __result)
        {
            if (__result && !MechanicalWorkTablePowerUtility.HasRequiredPower(__instance))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// A bill can already be in progress when a belt or engine is disconnected.
    /// End that job instead of allowing the cached bill toil to keep producing.
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_DoBill), "MakeNewToils")]
    internal static class Patch_MechanicalWorkTable_DoBill
    {
        private static void Prefix(JobDriver_DoBill __instance)
        {
            __instance.AddFailCondition(delegate
            {
                return __instance.BillGiver is Thing thing
                    && !MechanicalWorkTablePowerUtility.HasRequiredPower(thing);
            });
        }
    }
}
