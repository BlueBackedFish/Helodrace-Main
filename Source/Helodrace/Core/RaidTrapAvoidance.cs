using System.Linq;
using HarmonyLib;
using Helodrace.Squads;
using RimWorld;

namespace Helodrace
{
    [LegacyTactical]
    [HarmonyPatch(typeof(LordJob_AssaultColony), "get_AvoidTrapRatio")]
    public static class Patch_OrganizedHighRaidAvoidsTraps
    {
        public static void Postfix(LordJob_AssaultColony __instance, ref float __result)
        {
            if (__instance?.lord?.ownedPawns?.Any(pawn =>
                    pawn?.Faction?.def?.defName == "HD_HelodCivilHighFaction"
                    && OrganizationAPI.GetOrganization(pawn) != null) == true)
                __result = 1f;
        }
    }
}
