using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Economy
{
    public sealed class TraderSpecificPawnGroupMaker : PawnGroupMaker
    {
        public string traderKindDefName;
    }

    internal static class HelodCommercialCaravan
    {
        public const string FactionName = "HD_HelodCivilLowFaction";
        public static bool IsHelodCaravan(PawnGroupMakerParms parms)
        {
            return parms?.groupKind == PawnGroupKindDefOf.Trader
                && parms.faction?.def?.defName == FactionName;
        }

        public static bool IsCaptain(PawnKindDef kind)
        {
            return kind?.defName == "HD_GW_HelodCaravanGuardCaptain"
                || kind?.defName == "HD_GW_HelodEliteCaravanGuardCaptain";
        }

        public static IEnumerable<PawnGenOptionWithXenotype> LimitCaptains(
            IEnumerable<PawnGenOptionWithXenotype> selected, PawnGenOption regularGuard)
        {
            bool hasCaptain = false;
            foreach (PawnGenOptionWithXenotype choice in selected)
            {
                if (!IsCaptain(choice.Option.kind))
                {
                    yield return choice;
                    continue;
                }

                if (!hasCaptain)
                {
                    hasCaptain = true;
                    yield return choice;
                }
                else
                {
                    yield return new PawnGenOptionWithXenotype(
                        regularGuard, choice.Xenotype, choice.SelectionWeight);
                }
            }
        }
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.TryGetRandomPawnGroupMaker))]
    internal static class Patch_HelodTraderGroupMaker
    {
        public static bool Prefix(PawnGroupMakerParms parms, ref PawnGroupMaker pawnGroupMaker,
            ref bool __result)
        {
            if (parms?.groupKind != PawnGroupKindDefOf.Trader
                || parms.faction?.def?.defName != HelodCommercialCaravan.FactionName)
            {
                return true;
            }

            if (parms.traderKind == null)
            {
                List<TraderKindDef> kinds = parms.faction.def.caravanTraderKinds;
                if (kinds == null || kinds.Count == 0)
                    return true;
                parms.traderKind = kinds.RandomElementByWeight(kind => kind.commonality);
            }

            pawnGroupMaker = parms.faction.def.pawnGroupMakers
                .OfType<TraderSpecificPawnGroupMaker>()
                .FirstOrDefault(maker => maker.traderKindDefName == parms.traderKind.defName);
            __result = pawnGroupMaker != null && pawnGroupMaker.CanGenerateFrom(parms);
            return false;
        }
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints))]
    internal static class Patch_HelodCaravanCaptainLimit
    {
        public static void Postfix(List<PawnGenOption> options, PawnGroupMakerParms groupParms,
            ref IEnumerable<PawnGenOptionWithXenotype> __result)
        {
            if (!HelodCommercialCaravan.IsHelodCaravan(groupParms) || options == null)
                return;

            PawnGenOption regularGuard = options.FirstOrDefault(option =>
                option.kind?.defName == "HD_GW_HelodCaravanGuard");
            if (regularGuard != null && options.Any(option =>
                HelodCommercialCaravan.IsCaptain(option.kind)))
            {
                __result = HelodCommercialCaravan.LimitCaptains(__result, regularGuard);
            }
        }
    }
}
