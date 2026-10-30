// Frozen game-vector implementations from 85bcbfe4e04ee3056f1eabf57e88fa3e62930fc1.
using UnityEngine;
using RimWorld;
using Verse;

namespace Helodrace.Tests.Frozen
{
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

    public struct FleckWhitePhosphorusParticle : IFleck
    {
        private FleckStatic baseData;
        private Vector3 origin;
        private Vector3 driftVelocity;
        private Vector3 swayDirection;
        private float swayFrequency;
        private float swayAmplitude;
        private float swayPhase;
        private float age;

        public void Setup(FleckCreationData creationData)
        {
            baseData.Setup(creationData);
            origin = baseData.position.worldPosition;
            driftVelocity = creationData.velocity ?? Vector3.zero;
            driftVelocity.y = 0f;
            Vector3 direction = driftVelocity.sqrMagnitude > 0.0001f
                ? driftVelocity.normalized
                : Vector3.forward;
            swayDirection = new Vector3(-direction.z, 0f, direction.x);
            swayFrequency = Mathf.Max(0.1f, creationData.velocitySpeed);
            swayAmplitude = Mathf.Max(0f, creationData.orbitSpeed);
            swayPhase = creationData.airTimeLeft ?? 0f;
            age = 0f;
        }

        public bool TimeInterval(float deltaTime, Map map)
        {
            age += deltaTime;
            float sway = Mathf.Sin(swayPhase + age * swayFrequency * Mathf.PI * 2f)
                * swayAmplitude;
            baseData.position.worldPosition = origin + driftVelocity * age + swayDirection * sway;
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
    internal static class WhitePhosphorusEmitter
    {
        internal static FleckCreationData? SpawnDriftingFleck(
            IntVec3 cell,
            Map map,
            FleckDef fleckDef,
            float scale,
            Color emissionColor)
        {
            if (fleckDef == null)
            {
                return null;
            }

            Vector3 position = cell.ToVector3Shifted();
            position.x += Rand.Range(-0.38f, 0.38f);
            position.z += Rand.Range(-0.38f, 0.38f);
            float angle = Rand.Range(0f, 360f);
            Vector3 driftDirection = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
            FleckCreationData data = FleckMaker.GetDataStatic(position, map, fleckDef, scale);
            data.instanceColor = emissionColor;
            data.rotation = Rand.Range(0f, 360f);
            data.rotationRate = Rand.Range(-14f, 14f);
            data.velocity = driftDirection * Rand.Range(0.025f, 0.065f);
            data.velocitySpeed = Rand.Range(0.45f, 0.85f);
            data.orbitSpeed = Rand.Range(0.025f, 0.065f);
            data.airTimeLeft = Rand.Range(0f, Mathf.PI * 2f);
            return data;
        }
    }
}
