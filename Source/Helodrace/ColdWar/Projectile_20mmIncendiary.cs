using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed class IncendiaryProjectileExtension : DefModExtension
    {
        public float ignitionChance = 0.25f;
        public float ignitionRadius;
        public float fireSize = 0.3f;
    }

    internal static class IncendiaryProjectileUtility
    {
        public static void TryIgnite(IntVec3 center, Map map, IncendiaryProjectileExtension extension)
        {
            if (map == null || extension == null)
            {
                return;
            }

            float radius = Mathf.Max(0f, extension.ignitionRadius);
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (cell.InBounds(map) && Rand.Chance(extension.ignitionChance))
                {
                    FireUtility.TryStartFireIn(cell, map, extension.fireSize, null);
                }
            }
        }
    }

    public sealed class Projectile_20mmHighExplosiveIncendiary : Projectile_Explosive
    {
        protected override void Explode()
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;
            IncendiaryProjectileExtension extension =
                def.GetModExtension<IncendiaryProjectileExtension>();

            base.Explode();
            IncendiaryProjectileUtility.TryIgnite(impactCell, impactMap, extension);
        }
    }

    public sealed class Projectile_20mmArmorPiercingIncendiary : Bullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;
            IncendiaryProjectileExtension extension =
                def.GetModExtension<IncendiaryProjectileExtension>();

            base.Impact(hitThing, blockedByShield);
            if (!blockedByShield)
            {
                IncendiaryProjectileUtility.TryIgnite(impactCell, impactMap, extension);
            }
        }
    }
}
