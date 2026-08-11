using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace
{
    [DefOf]
    public static class HelodGasFleckDefOf
    {
        public static FleckDef HD_WhitePhosphorusSpark;
        public static FleckDef HD_WhitePhosphorusEmber;

        static HelodGasFleckDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HelodGasFleckDefOf));
        }
    }

    public sealed class HelodGasWorker_WhitePhosphorus : HelodGasWorker
    {
        // CellTick is reached once per map-area sweep (about every 64 ticks per
        // cell), so these probabilities stay independent of map size.
        private const float EmberChanceAtFullDensity = 0.12f;
        private const float SparkChanceAtFullDensity = 0.05f;
        private const float IgnitionChanceAtFullDensity = 0.018f;

        public override void Apply(Pawn pawn, float density, float severityAtFullDensity)
        {
            // White-phosphorus harm is represented by attached fire rather than
            // a separate gas hediff.
        }

        public override void CellTick(IntVec3 cell, Map map, float density)
        {
            float clampedDensity = Mathf.Clamp01(density);
            TryIgniteThing(cell, map, clampedDensity);

            if (cell.ShouldSpawnMotesAt(map))
            {
                // Camera-dependent visual rolls must not perturb gameplay RNG.
                Rand.PushState((cell.GetHashCode() * 397) ^ Find.TickManager.TicksGame);
                try
                {
                    if (Rand.Chance(EmberChanceAtFullDensity * clampedDensity))
                    {
                        SpawnDriftingFleck(
                            cell,
                            map,
                            HelodGasFleckDefOf.HD_WhitePhosphorusEmber,
                            Rand.Range(0.72f, 1.05f),
                            new Color(
                                Rand.Range(1.20f, 1.75f),
                                Rand.Range(0.42f, 0.78f),
                                Rand.Range(0.08f, 0.20f),
                                1f));
                    }

                    if (Rand.Chance(SparkChanceAtFullDensity * clampedDensity))
                    {
                        SpawnDriftingFleck(
                            cell,
                            map,
                            HelodGasFleckDefOf.HD_WhitePhosphorusSpark,
                            Rand.Range(0.34f, 0.52f),
                            new Color(
                                Rand.Range(1.55f, 2.05f),
                                Rand.Range(0.82f, 1.18f),
                                Rand.Range(0.18f, 0.42f),
                                1f));
                    }
                }
                finally
                {
                    Rand.PopState();
                }
            }
        }

        private static void SpawnDriftingFleck(
            IntVec3 cell,
            Map map,
            FleckDef fleckDef,
            float scale,
            Color emissionColor)
        {
            if (fleckDef == null)
            {
                return;
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
            map.flecks.CreateFleck(data);
        }

        private static void TryIgniteThing(IntVec3 cell, Map map, float density)
        {
            List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
            Thing candidate = null;
            float candidateFlammability = 0f;
            int candidateCount = 0;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed || thing is Fire ||
                    thing.def.category == ThingCategory.Ethereal)
                {
                    continue;
                }

                float flammability = thing.GetStatValue(StatDefOf.Flammability);
                if (flammability <= 0f)
                {
                    continue;
                }

                candidateCount++;
                if (Rand.Range(0, candidateCount) == 0)
                {
                    candidate = thing;
                    candidateFlammability = flammability;
                }
            }

            if (candidate == null || !Rand.Chance(
                IgnitionChanceAtFullDensity * density * Mathf.Clamp01(candidateFlammability)))
            {
                return;
            }

            candidate.TryAttachFire(Mathf.Lerp(0.12f, 0.30f, density), null);
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

    public sealed class FleckSystemWhitePhosphorusParticle
        : FleckSystemBase<FleckWhitePhosphorusParticle>
    {
        public FleckSystemWhitePhosphorusParticle(FleckManager manager)
            : base(manager)
        {
        }
    }
}
