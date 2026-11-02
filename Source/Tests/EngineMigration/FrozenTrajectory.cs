using System;
using UnityEngine;
using Verse;
using RimWorld;
using static Helodrace.InventoryGrenadeUtility;
namespace Helodrace.Tests.FrozenTrajectory
{
internal static class Geometry
{
public static Vector3 SecondaryEndpoint(Vector3 origin, Vector3 primary,
            float spreadDegrees, float sample)
        {
            Vector3 delta = primary - origin;
            float radians = Math.Max(0f, Math.Min(10f, spreadDegrees)) * (float)Math.PI / 180f;
            float offset = (float)Math.Tan(radians) * Math.Max(-1f, Math.Min(1f, sample));
            return primary + new Vector3(-delta.z * offset, 0f, delta.x * offset);
        }
private static bool CoversWholeMap(IntVec3 center, float radius, Map map)
        {
            Vector3 origin = center.ToVector3Shifted();
            float maxX = map.Size.x - 0.5f;
            float maxZ = map.Size.z - 0.5f;
            float radiusSquared = radius * radius;

            return (new Vector3(0.5f, 0f, 0.5f) - origin).sqrMagnitude <= radiusSquared
                && (new Vector3(maxX, 0f, 0.5f) - origin).sqrMagnitude <= radiusSquared
                && (new Vector3(0.5f, 0f, maxZ) - origin).sqrMagnitude <= radiusSquared
                && (new Vector3(maxX, 0f, maxZ) - origin).sqrMagnitude <= radiusSquared;
        }
internal static IntVec3 IncomingEdgeCell(Vector3 targetCenter, Vector3 baseCenter, IntVec3 size)
        {
            Vector3 target = targetCenter.normalized;
            Vector3 origin = baseCenter.normalized;
            Vector3 towardBase = Vector3.ProjectOnPlane(origin - target, target).normalized;
            Vector3 east = Vector3.Cross(Vector3.up, target).normalized;
            if (east.sqrMagnitude < 0.01f) east = Vector3.right;
            Vector3 north = Vector3.Cross(target, east).normalized;
            Vector2 direction = new Vector2(Vector3.Dot(towardBase, east), Vector3.Dot(towardBase, north)).normalized;
            if (direction.sqrMagnitude < 0.01f) direction = Vector2.up;
            float halfX = (size.x - 1) * 0.5f;
            float halfZ = (size.z - 1) * 0.5f;
            float scale = Mathf.Min(Mathf.Abs(direction.x) > 0.001f ? halfX / Mathf.Abs(direction.x) : float.MaxValue,
                Mathf.Abs(direction.y) > 0.001f ? halfZ / Mathf.Abs(direction.y) : float.MaxValue);
            return new IntVec3(Mathf.Clamp(Mathf.RoundToInt(halfX + direction.x * scale), 0, size.x - 1), 0,
                Mathf.Clamp(Mathf.RoundToInt(halfZ + direction.y * scale), 0, size.z - 1));
        }
internal static float TravelDistance(Vector3 exactPosition, Vector3 origin) => (exactPosition - origin).MagnitudeHorizontal();
}
internal static class Grenades
{
private static LocalTargetInfo ScatteredThrowTarget(
            Pawn pawn,
            LocalTargetInfo intendedTarget)
        {
            float distance = pawn.Position.DistanceTo(intendedTarget.Cell);
            float missRadius = ThrowMissRadius(pawn, distance);
            Vector2 missOffset = Rand.InsideUnitCircle * missRadius;
            Vector3 scatteredPosition = intendedTarget.CenterVector3
                + new Vector3(missOffset.x, 0f, missOffset.y);
            IntVec3 scatteredCell = scatteredPosition.ToIntVec3();
            return scatteredCell.InBounds(pawn.Map)
                ? new LocalTargetInfo(scatteredCell)
                : intendedTarget;
        }
public static Projectile Launch(
            Pawn pawn,
            Thing grenade,
            LocalTargetInfo target)
        {
            ThingDef projectileDef = grenade?.def?.projectileWhenLoaded;
            float range = ThrowRange;
            if (grenade == null
                || !CanUseGrenades(pawn)
                || projectileDef == null
                || !target.IsValid
                || !TryFindThrowSource(pawn, target.Cell, range, out IntVec3 sourceCell))
            {
                return null;
            }

            Thing consumed = grenade.SplitOff(1);
            Projectile projectile = GenSpawn.Spawn(projectileDef, sourceCell, pawn.Map) as Projectile;
            if (projectile != null)
            {
                LocalTargetInfo usedTarget = ScatteredThrowTarget(pawn, target);
                Vector3 throwOrigin = pawn.DrawPos;
                if (sourceCell != pawn.Position)
                {
                    Vector3 leanDirection = sourceCell.ToVector3Shifted() - pawn.DrawPos;
                    leanDirection.y = 0f;
                    throwOrigin += leanDirection.normalized * 0.65f;
                }

                projectile.Launch(
                    pawn,
                    throwOrigin,
                    usedTarget,
                    target,
                    ProjectileHitFlags.All,
                    false,
                    null);

                if (projectile is global::Helodrace.ModernWar.Projectile_ModernGrenade modernGrenade)
                {
                    modernGrenade.ConfigureInventoryThrow(pawn.Position.DistanceTo(target.Cell));
                }
            }

            consumed.Destroy(DestroyMode.Vanish);
            return projectile;
        }
}
}
