// Captures the native transform passed through the existing game drawing boundary.
internal static class GeometryBoundary
{
    internal static System.Numerics.Vector3 ToNative(this UnityEngine.Vector3 value)
        => new(value.x,value.y,value.z);
    internal static UnityEngine.Matrix4x4 ToGame(this System.Numerics.Matrix4x4 value)
    {
        System.Numerics.Matrix4x4.Decompose(value,out var scale,out _,out var position);
        return new UnityEngine.Matrix4x4 {pos=new(position.X,position.Y,position.Z),scale=new(scale.X,scale.Y,scale.Z)};
    }
}
