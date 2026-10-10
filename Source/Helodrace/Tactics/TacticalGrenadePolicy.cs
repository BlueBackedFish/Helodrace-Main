using Helodrace.ModernWar;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalGrenadePolicy
    {
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
