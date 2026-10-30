using System.Numerics;
using Verse;

namespace Helodrace
{
    /// <summary>
    /// A short-lived spark that turns to follow its instantaneous velocity.
    /// This keeps the streak tangent to the falling trajectory instead of
    /// retaining only its initial emission angle.
    /// </summary>
    public struct FleckFallingSpark : IFleck
    {
        private FleckStatic baseData;
        private Vector3 velocity;
        private Vector3 acceleration;

        public void Setup(FleckCreationData creationData)
        {
            baseData.Setup(creationData);
            velocity = creationData.velocity?.ToNative() ?? Vector3.Zero;
            velocity.Y = 0f;
            acceleration = (creationData.def?.acceleration ?? default).ToNative();
            acceleration.Y = 0f;
            FaceVelocity();
        }

        public bool TimeInterval(float deltaTime, Map map)
        {
            velocity += acceleration * deltaTime;
            baseData.position.worldPosition = (baseData.position.worldPosition.ToNative() + velocity * deltaTime).ToGame();
            FaceVelocity();
            return baseData.TimeInterval(deltaTime, map);
        }

        private void FaceVelocity()
        {
            Vector3 flatVelocity = velocity;
            flatVelocity.Y = 0f;
            if (flatVelocity.LengthSquared() > 0.0001f)
            {
                baseData.exactRotation = SpatialMath.FlatAngle(flatVelocity);
            }
        }

        public void Draw(DrawBatch drawBatch)
        {
            baseData.Draw(drawBatch);
        }

        public UnityEngine.Vector3 GetPosition()
        {
            return baseData.GetPosition();
        }
    }

    public sealed class FleckSystemFallingSpark : FleckSystemBase<FleckFallingSpark>
    {
        public FleckSystemFallingSpark(FleckManager manager)
            : base(manager)
        {
        }
    }

}
