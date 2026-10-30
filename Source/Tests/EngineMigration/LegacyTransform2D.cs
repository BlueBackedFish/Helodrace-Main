// Geometry baseline from Main 98fdf004, before internal native vectors.
using UnityEngine;
using Helodrace.ModernWar;
namespace Helodrace.Tests
{
    public struct LegacyTransform2D
    {
        public Vector2 position;
        public float angle;
        public Vector2 scale;
        public float layer;

        public static LegacyTransform2D Identity => new LegacyTransform2D
        {
            position = Vector2.zero,
            angle = 0f,
            scale = Vector2.one,
            layer = 0f
        };

        public Vector2 TransformPoint(Vector3 local)
        {
            Vector2 scaled = new Vector2(local.x * scale.x, local.z * scale.y);
            return position + Rotate(scaled, angle);
        }

        public Vector2 InverseTransformPoint(Vector2 world)
        {
            Vector2 unrotated = Rotate(world - position, -angle);
            Vector2 safeScale = SafeScale(scale);
            return new Vector2(
                unrotated.x / safeScale.x,
                unrotated.y / safeScale.y);
        }

        public LegacyTransform2D Attach(
            ModularAttachmentTransform socket,
            ModularAttachmentTransform mount,
            float childAngleOffset = 0f,
            float childPaletteRatio = 1f)
        {
            socket = socket ?? new ModularAttachmentTransform();
            mount = mount ?? new ModularAttachmentTransform();

            Vector2 socketPosition = TransformPoint(socket.position);
            float socketAngle = angle + socket.angle;
            Vector2 socketScale = Vector2.Scale(scale, SafeScale(socket.scale));
            Vector2 mountScale = SafeScale(mount.scale);
            Vector2 childScale = new Vector2(
                socketScale.x / mountScale.x * childPaletteRatio,
                socketScale.y / mountScale.y * childPaletteRatio);
            // The authored part angle belongs to the complete child node, not just its
            // texture. Folding it into the attachment transform keeps the mount pinned
            // to the parent socket while the graphic centre and all descendant sockets
            // orbit around that mount.
            float childAngle = socketAngle - mount.angle + childAngleOffset;
            Vector2 mountedOffset = new Vector2(
                mount.position.x * childScale.x,
                mount.position.z * childScale.y);

            return new LegacyTransform2D
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
                UnityEngine.Mathf.Abs(value.x) < 0.0001f ? 1f : value.x,
                UnityEngine.Mathf.Abs(value.y) < 0.0001f ? 1f : value.y);
        }

        public static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * UnityEngine.Mathf.Deg2Rad;
            float sin = UnityEngine.Mathf.Sin(radians);
            float cos = UnityEngine.Mathf.Cos(radians);
            return new Vector2(
                value.x * cos - value.y * sin,
                value.x * sin + value.y * cos);
        }
    }

}
