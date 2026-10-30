internal static class GeometryBoundary
{
    internal static System.Numerics.Vector2 ToNative(this UnityEngine.Vector2 value)
        => new System.Numerics.Vector2(value.x, value.y);

    internal static System.Numerics.Vector3 ToNative(this UnityEngine.Vector3 value)
        => new System.Numerics.Vector3(value.x, value.y, value.z);
}
