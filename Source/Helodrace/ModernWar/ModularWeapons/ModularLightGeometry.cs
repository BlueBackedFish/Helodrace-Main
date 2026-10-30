using System.Numerics;
using Waylith.Math;

namespace Helodrace.ModernWar
{
    // Pure beam calculations. XML properties are authoring inputs; all derived
    // positions, rotations and draw transforms remain native until submission.
    internal static class ModularLightGeometry
    {
        internal static Vector3 EmitterOrigin(ModularTransform2D transform, Vector3 localPoint,
            Vector3 drawLoc, float bodyAngle, bool flipped, float altitude)
        {
            Vector2 local = transform.TransformPoint(localPoint);
            if (flipped) local.X = -local.X;
            Vector2 world = ModularTransform2D.RotateForWorldYaw(local, bodyAngle);
            Vector3 origin = drawLoc + new Vector3(world.X, 0, world.Y);
            origin.Y = altitude;
            return origin;
        }

        internal static bool TryLaserBeam(Vector3 origin, Vector3 target, Vector3 weaponDirection,
            bool alignToWeapon, float maxLength, out Vector3 end, out bool clipped)
        {
            end = target;
            end.Y = origin.Y;
            Vector3 delta = end - origin;
            delta.Y = 0;
            clipped = false;
            if (delta.LengthSquared() < .0001f) return false;
            if (alignToWeapon && weaponDirection.LengthSquared() > .5f)
            {
                delta = weaponDirection.normalized * delta.Length();
                end = origin + delta;
                end.Y = origin.Y;
            }
            clipped = delta.Length() > maxLength;
            if (clipped) end = origin + delta.normalized * maxLength;
            return true;
        }

        internal static bool TryFlashlightBeam(Vector3 origin, Vector3 target, Vector3 weaponDirection,
            bool alignToWeapon, ModularWeaponFlashlightProperties light, out ModularFlashlightBeam beam)
        {
            beam = default;
            Vector3 delta = target - origin;
            delta.Y = 0;
            float length = delta.Length();
            if (length < .05f || length > light.maxRange) return false;
            Vector3 beamTarget = target;
            if (alignToWeapon && weaponDirection.LengthSquared() > .5f)
            {
                delta = weaponDirection * length;
                beamTarget = origin + delta;
                beamTarget.Y = origin.Y;
            }
            float farWidth = light.farWidth, poolRadius = light.circleRadius;
            if (light.closeRange > .01f && length < light.closeRange)
            {
                float scale = Mathf.Max(light.closeRangeMinScale, length / light.closeRange);
                farWidth *= scale;
                poolRadius *= scale;
            }
            Vector3 pool = beamTarget;
            pool.Y = origin.Y + .001f;
            float diameter = poolRadius * 2;
            beam = new ModularFlashlightBeam(
                Mathf.Min(light.nearWidth, farWidth), farWidth, poolRadius,
                SpatialMath.CreateTransform(origin,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, SpatialMath.FlatAngle(delta) * Mathf.Deg2Rad),
                    new Vector3(1, 1, Mathf.Max(.05f, length + light.lengthOvershoot))),
                SpatialMath.CreateTransform(pool, Quaternion.Identity, new Vector3(diameter, 1, diameter)));
            return true;
        }
    }

    internal readonly struct ModularFlashlightBeam
    {
        internal readonly float NearWidth, FarWidth, PoolRadius;
        internal readonly Matrix4x4 ConeTransform, PoolTransform;

        internal ModularFlashlightBeam(float nearWidth, float farWidth, float poolRadius,
            Matrix4x4 coneTransform, Matrix4x4 poolTransform)
        {
            NearWidth = nearWidth; FarWidth = farWidth; PoolRadius = poolRadius;
            ConeTransform = coneTransform; PoolTransform = poolTransform;
        }
    }
}
