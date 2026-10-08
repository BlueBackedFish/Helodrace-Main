using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace
{
    public static partial class RaidSmokeUtility
    {
        public static bool IsScreeningProjectile(ThingDef projectile)
        {
            if (projectile?.projectile?.damageDef?.defName != "Smoke"
                || projectile.GetModExtension<Helodrace.ModernWar.FragmentationGrenadeExtension>() != null
                || projectile.GetModExtension<Helodrace.ModernWar.FlashbangProjectileExtension>() != null) return false;
            HelodGasDef burstGas = projectile.GetModExtension<HelodGasOnExplosionExtension>()?.gasDef;
            HelodGasDef emitterGas = projectile.GetModExtension<Helodrace.ModernWar.ModernGrenadeProjectileExtension>()?.gasDef;
            bool hcBurst = burstGas?.defName == "HD_HCSmokeGrid";
            bool hcEmitter = emitterGas?.defName == "HD_HCSmokeGrid";
            return (projectile.projectile.postExplosionGasType == GasType.BlindSmoke || hcBurst || hcEmitter)
                && (burstGas == null || hcBurst) && (emitterGas == null || hcEmitter);
        }

        // Only harmless smoke is suitable for advancing inside a screen.
        public static bool SafeSmokeAt(Map map, IntVec3 cell, byte minimumDensity = 32) => cell.InBounds(map)
            && (map.gasGrid.DensityAt(cell, GasType.BlindSmoke) >= minimumDensity
                || HelodGasStore.DensityAt(cell, map, HelodGasDefOf.HD_HCSmokeGrid) >= minimumDensity);

        // WP obscures fire too, but is never selected as a safe movement screen.
        public static bool CoveringSmokeAt(Map map, IntVec3 cell, byte minimumDensity = 64) =>
            SafeSmokeAt(map, cell, minimumDensity) || cell.InBounds(map)
                && HelodGasStore.DensityAt(cell, map, HelodGasDefOf.HD_WhitePhosphorusSmokeGrid) >= minimumDensity;

        public static bool IsSmoke(Thing item) => IsScreeningProjectile(item?.def.projectileWhenLoaded);

        public static bool SmokeAt(Map map, IntVec3 target) => target.IsValid
            && GenRadial.RadialCellsAround(target, 3f, true).Any(cell => cell.InBounds(map)
                && SafeSmokeAt(map, cell));
    }

}
