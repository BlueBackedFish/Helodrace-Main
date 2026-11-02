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
    /// Gate the common check used both before assigning a bill and while doing
    /// one. CurrentlyUsableForBills also calls this method in RimWorld 1.7.
    /// </summary>
    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.UsableForBillsAfterFueling))]
    internal static class Patch_MechanicalWorkTable_UsableForBillsAfterFueling
    {
        private static void Postfix(Building_WorkTable __instance, ref bool __result)
        {
            if (__result && !MechanicalWorkTablePowerUtility.HasRequiredPower(__instance))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    internal static class Patch_MechanicalWorkTable_JobOnThing
    {
        private static bool Prefix(Thing thing, ref Job __result)
        {
            if (MechanicalWorkTablePowerUtility.HasRequiredPower(thing)) return true;
            JobFailReason.Is("HD_MechanicalUser_Stalled".Translate());
            __result = null;
            return false;
        }
    }
}
