using System.Numerics;
using Waylith.Math;

namespace Helodrace
{
    internal static class SpatialMath
    {
        internal static float HorizontalLength(Vector3 value)
            => Mathf.Sqrt(value.X * value.X + value.Z * value.Z);

        // Retain subtraction-first interpolation at grid boundaries. BCL Lerp
        // may use a different arithmetic order on SIMD hardware.
        internal static Vector3 Interpolate(Vector3 from, Vector3 to, float fraction)
            => from + (to - from) * Mathf.Clamp01(fraction);

        internal static float AngleBetween(Vector3 from, Vector3 to)
        {
            static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            double denominator = System.Math.Sqrt((double)Dot(from, from) * Dot(to, to));
            if (denominator < 1e-15) return 0;
            return (float)(System.Math.Acos(System.Math.Clamp(Dot(from, to) / denominator, -1d, 1d)) * 57.2957795);
        }

        internal static float FlatAngle(Vector3 direction)
            => direction.X == 0f && direction.Z == 0f
                ? 0f : Quaternion.LookRotation(direction).eulerAngles.Y;

        internal static float SignedAngleBetween(Vector3 from, Vector3 to, Vector3 axis)
        {
            Vector3 cross = Vector3.Cross(from, to);
            float sign = axis.X * cross.X + axis.Y * cross.Y + axis.Z * cross.Z >= 0f ? 1f : -1f;
            return AngleBetween(from, to) * sign;
        }

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
