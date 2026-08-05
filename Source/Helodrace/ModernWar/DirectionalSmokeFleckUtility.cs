using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public static class DirectionalSmokeFleckUtility
    {
        public static void ThrowDirectional(
            Vector3 origin,
            Map map,
            Vector3 direction,
            int count,
            float spreadDegrees,
            FloatRange speedRange,
            FloatRange scaleRange,
            FloatRange solidTimeRange,
            float spawnRadius = 0f,
            FleckDef smokeDef = null)
        {
            if (map == null || count <= 0 || !origin.ToIntVec3().InBounds(map))
            {
                return;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            direction.Normalize();
            float halfSpread = Mathf.Max(0f, spreadDegrees) * 0.5f;
            float radius = Mathf.Max(0f, spawnRadius);
            for (int i = 0; i < count; i++)
            {
                Vector3 smokeDirection = Quaternion.AngleAxis(
                    Rand.Range(-halfSpread, halfSpread),
                    Vector3.up) * direction;
                Vector3 position = origin + smokeDirection * Rand.Range(0f, radius);
                if (!position.ToIntVec3().InBounds(map))
                {
                    continue;
                }

                FleckCreationData smoke = FleckMaker.GetDataStatic(
                    position,
                    map,
                    smokeDef ?? FleckDefOf.Smoke,
                    Mathf.Max(0.05f, scaleRange.RandomInRange));
                smoke.rotation = Rand.Range(0f, 360f);
                smoke.rotationRate = Rand.Range(-18f, 18f);
                smoke.velocity = smokeDirection * Mathf.Max(0f, speedRange.RandomInRange);
                smoke.solidTimeOverride = Mathf.Max(0.05f, solidTimeRange.RandomInRange);
                map.flecks.CreateFleck(smoke);
            }
        }

        public static void ThrowRadial(
            Vector3 origin,
            Map map,
            int count,
            float directionJitterDegrees,
            FloatRange speedRange,
            FloatRange scaleRange,
            FloatRange solidTimeRange,
            FloatRange spawnRadiusRange,
            float angleOffset = 0f,
            FleckDef smokeDef = null)
        {
            if (map == null || count <= 0 || !origin.ToIntVec3().InBounds(map))
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                float angle = angleOffset
                    + i * 360f / count
                    + Rand.Range(-directionJitterDegrees, directionJitterDegrees);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                Vector3 position = origin + direction * Mathf.Max(0f, spawnRadiusRange.RandomInRange);
                ThrowDirectional(
                    position,
                    map,
                    direction,
                    1,
                    0f,
                    speedRange,
                    scaleRange,
                    solidTimeRange,
                    0f,
                    smokeDef);
            }
        }

        public static void ThrowDirectionalEaseOut(
            Vector3 origin,
            Map map,
            Vector3 direction,
            int count,
            float spreadDegrees,
            FloatRange targetDistanceRange,
            FloatRange movementDurationRange,
            FloatRange minimumDriftSpeedRange,
            FloatRange scaleRange,
            FloatRange solidTimeRange,
            float spawnRadius,
            FleckDef smokeDef)
        {
            if (map == null
                || smokeDef == null
                || count <= 0
                || !origin.ToIntVec3().InBounds(map))
            {
                return;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            direction.Normalize();
            float halfSpread = Mathf.Max(0f, spreadDegrees) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                Vector3 smokeDirection = Quaternion.AngleAxis(
                    Rand.Range(-halfSpread, halfSpread),
                    Vector3.up) * direction;
                Vector3 position = origin
                    + smokeDirection * Rand.Range(0f, Mathf.Max(0f, spawnRadius));
                if (!position.ToIntVec3().InBounds(map))
                {
                    continue;
                }

                FleckCreationData smoke = FleckMaker.GetDataStatic(
                    position,
                    map,
                    smokeDef,
                    Mathf.Max(0.05f, scaleRange.RandomInRange));
                smoke.rotation = Rand.Range(0f, 360f);
                smoke.rotationRate = Rand.Range(-18f, 18f);
                smoke.velocity = smokeDirection;
                smoke.velocitySpeed = Mathf.Max(0f, targetDistanceRange.RandomInRange);
                smoke.airTimeLeft = Mathf.Max(0.05f, movementDurationRange.RandomInRange);
                smoke.orbitSpeed = Mathf.Max(0f, minimumDriftSpeedRange.RandomInRange);
                smoke.solidTimeOverride = Mathf.Max(0.05f, solidTimeRange.RandomInRange);
                map.flecks.CreateFleck(smoke);
            }
        }

        public static void ThrowRadialEaseOut(
            Vector3 origin,
            Map map,
            int count,
            float directionJitterDegrees,
            FloatRange targetDistanceRange,
            FloatRange movementDurationRange,
            FloatRange minimumDriftSpeedRange,
            FloatRange scaleRange,
            FloatRange solidTimeRange,
            FloatRange spawnRadiusRange,
            FleckDef smokeDef,
            float angleOffset = 0f)
        {
            if (map == null || smokeDef == null || count <= 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                float angle = angleOffset
                    + i * 360f / count
                    + Rand.Range(-directionJitterDegrees, directionJitterDegrees);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                Vector3 position = origin
                    + direction * Mathf.Max(0f, spawnRadiusRange.RandomInRange);
                ThrowDirectionalEaseOut(
                    position,
                    map,
                    direction,
                    1,
                    0f,
                    targetDistanceRange,
                    movementDurationRange,
                    minimumDriftSpeedRange,
                    scaleRange,
                    solidTimeRange,
                    0f,
                    smokeDef);
            }
        }
    }
}
