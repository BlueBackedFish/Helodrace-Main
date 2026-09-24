using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public enum HelodForwardBaseService
    {
        InfantryMortarSupport,
        InfantrySniperSupport,
        InfantryDeployment,
        LogisticsFreshFood,
        LogisticsPreservedFood,
        LogisticsMedicalSupplies,
        LogisticsWeapons,
        CloseAirSupport,
        Artillery105mmSupport,
        Artillery155mmSupport,
        W48Support,
        HelicopterQRF,
        HelicopterMedevac
    }

    public enum HelodForwardBaseCostKind
    {
        FFP,
        CostReimbursement,
    }

    public static class HelodForwardBaseServiceUtility
    {
        public const int ServiceBillingPeriodDays = 30;
        public const int ServiceBillingPeriodTicks = ServiceBillingPeriodDays * GenDate.TicksPerDay;
        public const int DefaultFfpServiceUnitsPerBillingPeriod = 10;
        public const float GoldStandardSthalerSilverValue = 5f;

        public static float SupportRange(HelodForwardBaseService service)
        {
            switch (service)
            {
                case HelodForwardBaseService.InfantrySniperSupport:
                    return 3f;
                case HelodForwardBaseService.InfantryMortarSupport:
                    return 5f;
                case HelodForwardBaseService.InfantryDeployment:
                    return 4f;
                case HelodForwardBaseService.LogisticsFreshFood:
                case HelodForwardBaseService.LogisticsPreservedFood:
                case HelodForwardBaseService.LogisticsMedicalSupplies:
                case HelodForwardBaseService.LogisticsWeapons:
                    return 8f;
                case HelodForwardBaseService.HelicopterQRF:
                case HelodForwardBaseService.HelicopterMedevac:
                case HelodForwardBaseService.CloseAirSupport:
                    return 10f;
                case HelodForwardBaseService.Artillery105mmSupport:
                    return 8f;
                case HelodForwardBaseService.Artillery155mmSupport:
                case HelodForwardBaseService.W48Support:
                    return 10f;
                default:
                    return 0f;
            }
        }

        public static bool CanSupport(HelodForwardBaseService service, WorldObject target)
        {
            HelodForwardBase forwardBase;
            float distance;
            return TryFindSupportingBase(service, target, out forwardBase, out distance);
        }

        public static bool CanSupport(HelodForwardBaseService service, Map map)
        {
            HelodForwardBase forwardBase;
            float distance;
            return TryFindSupportingBase(service, map, out forwardBase, out distance);
        }

        public static bool CanSupport(HelodForwardBaseService service, Map map, Faction faction)
        {
            HelodForwardBase forwardBase;
            float distance;
            return TryFindSupportingBase(service, map, faction, out forwardBase, out distance);
        }

        public static bool CanSupportAvailable(HelodForwardBaseService service, Map map, Faction faction)
        {
            HelodForwardBase forwardBase;
            float distance;
            return TryFindSupportingBase(service, TargetTile(map), faction, true, out forwardBase, out distance);
        }

        public static bool TryFindSupportingBase(HelodForwardBaseService service, WorldObject target, out HelodForwardBase forwardBase, out float distance)
        {
            return TryFindSupportingBase(service, target?.Tile ?? -1, out forwardBase, out distance);
        }

        public static bool TryFindSupportingBase(HelodForwardBaseService service, Map map, out HelodForwardBase forwardBase, out float distance)
        {
            return TryFindSupportingBase(service, TargetTile(map), out forwardBase, out distance);
        }

        public static bool TryFindSupportingBase(HelodForwardBaseService service, Map map, Faction faction, out HelodForwardBase forwardBase, out float distance)
        {
            return TryFindSupportingBase(service, TargetTile(map), faction, out forwardBase, out distance);
        }

        public static bool TryFindSupportingBase(HelodForwardBaseService service, int targetTile, out HelodForwardBase forwardBase, out float distance)
        {
            return TryFindSupportingBase(service, targetTile, null, out forwardBase, out distance);
        }

        public static bool TryFindSupportingBase(HelodForwardBaseService service, int targetTile, Faction faction, out HelodForwardBase forwardBase, out float distance)
        {
            return TryFindSupportingBase(service, targetTile, faction, false, out forwardBase, out distance);
        }

        public static bool TryFindSupportingBase(HelodForwardBaseService service, Map map, Faction faction, bool requireAvailableUse, out HelodForwardBase forwardBase, out float distance)
        {
            return TryFindSupportingBase(service, TargetTile(map), faction, requireAvailableUse, out forwardBase, out distance);
        }

        public static bool TryFindSupportingBase(HelodForwardBaseService service, int targetTile, Faction faction, bool requireAvailableUse, out HelodForwardBase forwardBase, out float distance)
        {
            forwardBase = null;
            distance = float.MaxValue;
            if (targetTile < 0 || Find.WorldObjects?.AllWorldObjects == null || Find.WorldGrid == null)
            {
                return false;
            }

            float range = SupportRange(service);
            for (int i = 0; i < Find.WorldObjects.AllWorldObjects.Count; i++)
            {
                HelodForwardBase candidate = Find.WorldObjects.AllWorldObjects[i] as HelodForwardBase;
                if (candidate == null || !candidate.HasService(service) || candidate.Tile < 0 || (faction != null && candidate.Faction != faction))
                {
                    continue;
                }

                if (requireAvailableUse && !candidate.HasServiceCapacity(service))
                {
                    continue;
                }

                float candidateDistance = Find.WorldGrid.ApproxDistanceInTiles(candidate.Tile, targetTile);
                if (candidateDistance <= range && candidateDistance < distance)
                {
                    forwardBase = candidate;
                    distance = candidateDistance;
                }
            }

            if (forwardBase == null)
            {
                distance = 0f;
                return false;
            }

            return true;
        }

        public static float ServiceBaseCost(HelodForwardBaseService service)
        {
            switch (service)
            {
                case HelodForwardBaseService.HelicopterQRF:
                    return 700f;
                case HelodForwardBaseService.HelicopterMedevac:
                    return 400f;
                case HelodForwardBaseService.InfantryDeployment:
                    return 220f;
                case HelodForwardBaseService.LogisticsFreshFood:
                    return 160f;
                case HelodForwardBaseService.LogisticsPreservedFood:
                    return 220f;
                case HelodForwardBaseService.LogisticsMedicalSupplies:
                    return 320f;
                case HelodForwardBaseService.LogisticsWeapons:
                    return 480f;
                case HelodForwardBaseService.CloseAirSupport:
                    return 950f;
                case HelodForwardBaseService.InfantrySniperSupport:
                    return 160f;
                case HelodForwardBaseService.InfantryMortarSupport:
                    return 120f;
                case HelodForwardBaseService.Artillery105mmSupport:
                    return 420f;
                case HelodForwardBaseService.Artillery155mmSupport:
                    return 780f;
                case HelodForwardBaseService.W48Support:
                    return 1200f;
                default:
                    return 0f;
            }
        }

        public static float ServiceUseCostGoldStandard(HelodForwardBaseService service)
        {
            // Preserve the existing per-call reimbursement rate while allowing
            // unlimited service uses within a billing period.
            return ServiceBaseCost(service) / 10f;
        }

        public static float MortarCallCostGoldStandard(ThingDef shellDef, int shellCount)
        {
            if (shellDef == null || shellCount <= 0) return 0f;
            // Ammo market values are silver-denominated. Convert the full fire mission to
            // gold-standard Sthaler so every future shell Def automatically gets its own price.
            return Mathf.Max(1f, shellDef.BaseMarketValue * shellCount / GoldStandardSthalerSilverValue);
        }

        public static System.Collections.Generic.List<ThingDef> AvailableMortarShells()
        {
            return AvailableSupportShells(HelodForwardBaseService.InfantryMortarSupport);
        }

        public static System.Collections.Generic.List<ThingDef> AvailableSupportShells(
            HelodForwardBaseService service)
        {
            string categoryDefName;
            switch (service)
            {
                case HelodForwardBaseService.Artillery105mmSupport:
                    categoryDefName = "HD_105mmHowitzerShells";
                    break;
                case HelodForwardBaseService.Artillery155mmSupport:
                    categoryDefName = "HD_155mmHowitzerShells";
                    break;
                default:
                    categoryDefName = "HD_81mmMortarShells";
                    break;
            }

            ThingCategoryDef category = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(categoryDefName);
            var result = new System.Collections.Generic.List<ThingDef>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.projectileWhenLoaded == null || category == null || def.thingCategories == null || !def.thingCategories.Contains(category)) continue;
                if (def.defName == "HD_155mmShell_W48") continue;
                result.Add(def);
            }
            result.SortBy(x => x.label);
            return result;
        }

        public static float CurrentSthalerSilverValue()
        {
            return Mathf.Max(0.01f, HelodMarketState.Current?.SthalerSilverValue ?? GoldStandardSthalerSilverValue);
        }

        public static string FormatSthalerValue(float goldStandardSthalerValue)
        {
            float silverValue = goldStandardSthalerValue * GoldStandardSthalerSilverValue;
            float sthalerValue = silverValue / CurrentSthalerSilverValue();
            return "HD_TelegraphTable_ForwardBase_SthalerAmount".Translate(sthalerValue.ToString("F0")).ToString();
        }

        private static int TargetTile(Map map)
        {
            if (map == null)
            {
                return -1;
            }

            if (map.Tile >= 0)
            {
                return map.Tile;
            }

            return map.Parent?.Tile ?? -1;
        }
    }
}
