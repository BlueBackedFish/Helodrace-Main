using System;
using Helodrace.ModernWar;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalGrenadePolicy
    {
        public static bool CanRetreatBeforeFuse(ThingDef projectile, IntVec3 from, IntVec3 to, float ticksPerStep, Func<IntVec3,bool> clear)
        {
            int steps = Math.Abs(from.x - to.x) + Math.Abs(from.z - to.z);
            if (steps == 0) return clear(to);
            if (steps > 4 || projectile?.thingClass == null
                || !typeof(Projectile_ModernGrenade).IsAssignableFrom(projectile.thingClass)) return false;
            var extension = projectile.GetModExtension<ModernGrenadeProjectileExtension>();
            int fuse = Math.Max(1, (extension?.fuseTicks ?? 270) - Math.Max(0, extension?.fuseVarianceTicks ?? 30));
            if (Math.Max(1,ticksPerStep) * (steps + 2) + 60 >= fuse) return false;
            bool Path(bool xFirst)
            {
                IntVec3 cell = from;
                for (int i = 0; i < steps; i++)
                {
                    if (xFirst ? cell.x != to.x : cell.z == to.z)
                        cell.x += Math.Sign(to.x - cell.x);
                    else cell.z += Math.Sign(to.z - cell.z);
                    if (!clear(cell)) return false;
                }
                return true;
            }
            return Path(true) || Path(false);
        }
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
        public static bool FriendlyInRoom(Room targetRoom, IntVec3 ally, bool door, Func<IntVec3, Room> roomAt, TacticalLocalPlan opening = null)
        {
            if (targetRoom == null) return false;
            // A destroyed wall joins the old approach and destination in the
            // native Room graph. The approach side is not the target room;
            // it still receives the separate physical hazard/LOS check.
            if (opening != null && !opening.Direct && roomAt(opening.Outside) == targetRoom)
            {
                IntVec3 delta = ally - opening.Opening;
                if (delta.x * opening.Inward.x + delta.z * opening.Inward.z < 0) return false;
            }
            if (roomAt(ally) == targetRoom) return true;
            if (door)
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                    if (roomAt(ally + direction) == targetRoom) return true;
            return false;
        }

        public static bool DangerousThroughCover(ThingDef projectile)
        {
            if (projectile?.GetModExtension<ModernGrenadeProjectileExtension>()?.gasDef != null
                || projectile?.GetModExtension<HelodGasOnExplosionExtension>()?.gasDef != null) return true;
            var flash = projectile?.GetModExtension<FlashbangProjectileExtension>();
            if (flash == null) return false;
            float environment = Math.Max(Math.Max(0, flash.outdoorFactor), Math.Max(flash.smallRoomFactor, flash.largeRoomFactor));
            float intensity = Math.Max(0, Math.Max(flash.severityAtCenter, flash.severityAtEdge)) * Math.Max(0, environment)
                * Math.Max(0, Math.Min(1, flash.blockedPathFactor));
            // MK II/III's small audible shock through a wall is not a stun or
            // intensity injury. Do not equate any nonzero attenuation to damage.
            return intensity > 0 && (intensity >= Math.Max(0, flash.stunThreshold)
                || flash.intensityDamageDef != null && intensity >= Math.Max(0, flash.damageThreshold));
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
