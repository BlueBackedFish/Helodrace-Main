using System;
using Helodrace.ModernWar;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalGrenadePolicy
    {
        public static float HazardRadius(ThingDef projectile, bool indoors)
        {
            float radius = Math.Max(0, projectile?.projectile?.explosionRadius ?? 0);
            FragmentationGrenadeExtension fragments = projectile?.GetModExtension<FragmentationGrenadeExtension>();
            if (fragments != null) radius = Math.Max(radius, Math.Max(fragments.radius,
                fragments.longRangeFragmentFraction > 0 ? fragments.longRangeRadius : 0));
            FlashbangProjectileExtension flash = projectile?.GetModExtension<FlashbangProjectileExtension>();
            if (flash != null) radius = Math.Max(radius, Math.Max(.1f, flash.effectRadius)
                * (indoors ? Math.Max(1, flash.indoorRadiusFactor) : 1));
            ModernGrenadeProjectileExtension modern = projectile?.GetModExtension<ModernGrenadeProjectileExtension>();
            if (modern?.gasDef != null) radius = Math.Max(radius, modern.gasEmissionRadius);
            HelodGasOnExplosionExtension burst = projectile?.GetModExtension<HelodGasOnExplosionExtension>();
            if (burst?.gasDef != null) radius = Math.Max(radius, burst.radius);
            return radius + Math.Max(0, modern?.projectileBounceTravelDistance ?? 0);
        }

        // Use the live native room only for last-moment friendly safety, never
        // as mission progress or an enemy observation. Door occupants belong
        // to both adjacent rooms for this conservative safety check.
        public static bool FriendlyInRoom(Room targetRoom, IntVec3 ally, bool door, Func<IntVec3, Room> roomAt)
        {
            if (targetRoom == null) return false;
            if (roomAt(ally) == targetRoom) return true;
            if (door)
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                    if (roomAt(ally + direction) == targetRoom) return true;
            return false;
        }

        // Tactical automatic selection only. Manual inventory throws and other
        // factions retain the item until gas-mask doctrine is implemented.
        public static bool AllowsAutomatic(FactionDef faction, ThingDef grenade)
        {
            if (grenade == null) return false;
            if (faction?.defName != "HD_HelodCivilHighFaction") return true;
            ThingDef projectile = grenade.projectileWhenLoaded;
            return grenade.defName != "HD_Grenade_M7A2_Item"
                && projectile?.GetModExtension<ModernGrenadeProjectileExtension>()?.gasDef?.defName != "HD_CSGasGrid"
                && projectile?.GetModExtension<HelodGasOnExplosionExtension>()?.gasDef?.defName != "HD_CSGasGrid";
        }
    }
}
