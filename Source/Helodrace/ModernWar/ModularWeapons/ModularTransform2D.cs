using System.Numerics;

namespace Helodrace.ModernWar
{
    // Immutable input copied once from XML authoring data at snapshot creation.
    public readonly record struct ModularAttachmentPose(Vector3 position, float angle, Vector2 scale, float layer)
    {
        public static ModularAttachmentPose Identity => new(Vector3.Zero, 0f, Vector2.One, 0f);
    }

    public struct ModularTransform2D
    {
        public Vector2 position;
        public float angle;
        public Vector2 scale;
        public float layer;

        public static ModularTransform2D Identity => new ModularTransform2D
        {
            position = Vector2.Zero,
            angle = 0f,
            scale = Vector2.One,
            layer = 0f
        };

        public Vector2 TransformPoint(Vector3 local)
        {
            Vector2 scaled = new Vector2(local.X * scale.X, local.Z * scale.Y);
            return position + Rotate(scaled, angle);
        }

        public Vector2 InverseTransformPoint(Vector2 world)
        {
            Vector2 unrotated = Rotate(world - position, -angle);
            Vector2 safeScale = SafeScale(scale);
            return new Vector2(
                unrotated.X / safeScale.X,
                unrotated.Y / safeScale.Y);
        }

        public ModularTransform2D Attach(
            ModularAttachmentPose socket,
            ModularAttachmentPose mount,
            float childAngleOffset = 0f,
            float childPaletteRatio = 1f)
        {
            Vector2 socketPosition = TransformPoint(socket.position);
            float socketAngle = angle + socket.angle;
            Vector2 socketScale = scale * SafeScale(socket.scale);
            Vector2 mountScale = SafeScale(mount.scale);
            Vector2 childScale = new Vector2(
                socketScale.X / mountScale.X * childPaletteRatio,
                socketScale.Y / mountScale.Y * childPaletteRatio);
            // The authored part angle belongs to the complete child node, not just its
            // texture. Folding it into the attachment transform keeps the mount pinned
            // to the parent socket while the graphic centre and all descendant sockets
            // orbit around that mount.
            float childAngle = socketAngle - mount.angle + childAngleOffset;
            Vector2 mountedOffset = new Vector2(
                mount.position.X * childScale.X,
                mount.position.Z * childScale.Y);

            return new ModularTransform2D
            {
                position = socketPosition - Rotate(mountedOffset, childAngle),
                angle = childAngle,
                scale = childScale,
                layer = layer + socket.layer - mount.layer
            };
        }

        private static Vector2 SafeScale(Vector2 value)
        {
            return new Vector2(
                Mathf.Abs(value.X) < 0.0001f ? 1f : value.X,
                Mathf.Abs(value.Y) < 0.0001f ? 1f : value.Y);
        }

        public static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(
                value.X * cos - value.Y * sin,
                value.X * sin + value.Y * cos);
        }

        // The graph uses mathematical X/Z angles; the world uses positive yaw.
        public static Vector2 RotateForWorldYaw(Vector2 value, float degrees)
        {
            Vector3 rotated = Vector3.Transform(new Vector3(value.X, 0f, value.Y),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * Mathf.Deg2Rad));
            return new Vector2(rotated.X, rotated.Z);
        }
    }

}
