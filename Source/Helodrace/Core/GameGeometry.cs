namespace Helodrace
{
    // Explicit crossings for game/UI APIs whose public signatures still use
    // compatibility structs. Internal geometry stays in native/BCL values.
    internal static class GameGeometry
    {
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
