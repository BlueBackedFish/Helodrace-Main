using System.Numerics;
using Waylith.Math;

namespace Helodrace
{
    internal static class SpatialMath
    {
        internal static float FlatAngle(Vector3 direction)
            => direction.X == 0f && direction.Z == 0f
                ? 0f : Quaternion.LookRotation(direction).eulerAngles.Y;

        internal static Vector3 ShotDirection(Vector3 source, Vector3 target, Vector3 fallback)
        {
            Vector3 direction = target - source;
            direction.Y = 0;
            if (direction.LengthSquared() < .0001f) direction = fallback;
            return direction.normalized;
        }

        internal static Vector3 RotateAroundY(Vector3 value, float degrees)
            => Vector3.Transform(value, Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * Mathf.Deg2Rad));

        // BCL row-vector convention: a point is scaled, rotated, then translated.
        // A zero quaternion has no rotation, matching the current game contract.
        internal static Matrix4x4 CreateTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            rotation = Quaternion.Dot(rotation, rotation) <= 1e-12f
                ? Quaternion.Identity : Quaternion.Normalize(rotation);
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(position);
        }
    }
}
