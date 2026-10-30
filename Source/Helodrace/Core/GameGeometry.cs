namespace Helodrace
{
    // Explicit crossings for game/UI APIs whose public signatures still use
    // compatibility structs. Internal geometry stays in native/BCL values.
    internal static class GameGeometry
    {
        internal static System.Numerics.Quaternion ToNative(this UnityEngine.Quaternion value)
            => new System.Numerics.Quaternion(value.x, value.y, value.z, value.w);

        internal static Waylith.Math.Color ToNative(this UnityEngine.Color value)
            => new Waylith.Math.Color(value.r, value.g, value.b, value.a);

        internal static UnityEngine.Color ToGame(this Waylith.Math.Color value)
            => new UnityEngine.Color(value.r, value.g, value.b, value.a);

        internal static Waylith.Math.Color32 ToNative(this UnityEngine.Color32 value)
            => new Waylith.Math.Color32(value.r, value.g, value.b, value.a);

        internal static UnityEngine.Color32 ToGame(this Waylith.Math.Color32 value)
            => new UnityEngine.Color32(value.r, value.g, value.b, value.a);

        // Native/BCL matrices multiply row vectors; game matrices multiply columns.
        internal static UnityEngine.Matrix4x4 ToGame(this System.Numerics.Matrix4x4 value)
            => new UnityEngine.Matrix4x4
            {
                m00 = value.M11, m01 = value.M21, m02 = value.M31, m03 = value.M41,
                m10 = value.M12, m11 = value.M22, m12 = value.M32, m13 = value.M42,
                m20 = value.M13, m21 = value.M23, m22 = value.M33, m23 = value.M43,
                m30 = value.M14, m31 = value.M24, m32 = value.M34, m33 = value.M44
            };

        internal static System.Numerics.Vector3 ToNative(this UnityEngine.Vector3 value)
            => new System.Numerics.Vector3(value.x, value.y, value.z);

        internal static UnityEngine.Vector3 ToGame(this System.Numerics.Vector3 value)
            => new UnityEngine.Vector3(value.X, value.Y, value.Z);

        internal static System.Numerics.Vector2 ToNative(this UnityEngine.Vector2 value)
            => new System.Numerics.Vector2(value.x, value.y);

        internal static UnityEngine.Vector2 ToGame(this System.Numerics.Vector2 value)
            => new UnityEngine.Vector2(value.X, value.Y);

        internal static Waylith.Math.Rect ToNative(this UnityEngine.Rect value)
            => new Waylith.Math.Rect(value.x, value.y, value.width, value.height);

        internal static UnityEngine.Rect ToGame(this Waylith.Math.Rect value)
            => new UnityEngine.Rect(value.x, value.y, value.width, value.height);
    }
}
