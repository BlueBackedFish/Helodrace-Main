using UnityEngine;
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
            velocity = creationData.velocity ?? Vector3.zero;
            velocity.y = 0f;
            acceleration = creationData.def?.acceleration ?? Vector3.zero;
            acceleration.y = 0f;
            FaceVelocity();
        }

        public bool TimeInterval(float deltaTime, Map map)
        {
            velocity += acceleration * deltaTime;
            baseData.position.worldPosition += velocity * deltaTime;
            FaceVelocity();
            return baseData.TimeInterval(deltaTime, map);
        }

        private void FaceVelocity()
        {
            Vector3 flatVelocity = velocity;
            flatVelocity.y = 0f;
            if (flatVelocity.sqrMagnitude > 0.0001f)
            {
                baseData.exactRotation = flatVelocity.AngleFlat();
            }
        }

        public void Draw(DrawBatch drawBatch)
        {
            baseData.Draw(drawBatch);
        }

        public Vector3 GetPosition()
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

    public sealed class Thing_PowerCutterFlashLight : ThingWithComps
    {
        private const int LifetimeTicks = 2;
        private int ticksRemaining = LifetimeTicks;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", LifetimeTicks);
        }

        protected override void Tick()
        {
            base.Tick();
            ticksRemaining--;
            if (ticksRemaining <= 0)
            {
                Destroy(DestroyMode.Vanish);
            }
        }
    }
}
