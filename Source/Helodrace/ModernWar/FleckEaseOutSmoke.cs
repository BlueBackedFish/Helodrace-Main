using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public struct FleckEaseOutSmoke : IFleck
    {
        private const float EasePower = 4f;

        private FleckStatic baseData;
        private Vector3 movementOrigin;
        private Vector3 movementDirection;
        private float targetDistance;
        private float movementDuration;
        private float movementAge;
        private float minimumDriftSpeed;

        public void Setup(FleckCreationData creationData)
        {
            baseData.Setup(creationData);
            movementOrigin = baseData.position.worldPosition;
            movementDirection = creationData.velocity ?? Vector3.zero;
            movementDirection.y = 0f;
            if (movementDirection.sqrMagnitude > 0.0001f)
            {
                movementDirection.Normalize();
            }

            targetDistance = Mathf.Max(0f, creationData.velocitySpeed);
            movementDuration = Mathf.Max(0.01f, creationData.airTimeLeft ?? 0.5f);
            minimumDriftSpeed = Mathf.Max(0f, creationData.orbitSpeed);
            movementAge = 0f;
        }

        public bool TimeInterval(float deltaTime, Map map)
        {
            movementAge += deltaTime;
            float progress = Mathf.Clamp01(movementAge / movementDuration);
            float easedProgress = 1f - Mathf.Pow(1f - progress, EasePower);
            baseData.position.worldPosition = movementOrigin
                + movementDirection
                * (targetDistance * easedProgress + minimumDriftSpeed * movementAge);
            return baseData.TimeInterval(deltaTime, map);
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

    public sealed class FleckSystemEaseOutSmoke : FleckSystemBase<FleckEaseOutSmoke>
    {
        public FleckSystemEaseOutSmoke(FleckManager manager)
            : base(manager)
        {
        }
    }
}
