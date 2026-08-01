using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed class M651CSProjectileExtension : DefModExtension
    {
        public ThingDef gasDef;
        public int gasReleaseDelayTicks = 15;
        public float gasEmissionRadius = 1.9f;
        public float gasDensity = 1f;
        public float gasEdgeDensityFactor = 0.68f;
        public int gasEmissionDurationTicksMin = 1200;
        public int gasEmissionDurationTicksMax = 1800;
        public int gasEmissionIntervalTicks = 60;
        public float gasPulseDensityFactor = 0.12f;
    }

    public sealed class Projectile_M651CS : Projectile_Explosive
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;
            M651CSProjectileExtension extension = def.GetModExtension<M651CSProjectileExtension>();
            if (impactMap != null && extension?.gasDef != null)
            {
                ModernWar.CSGasEmitter emitter = ThingMaker.MakeThing(
                    DefDatabase<ThingDef>.GetNamed("HD_M651CSGasEmitter")) as ModernWar.CSGasEmitter;
                emitter?.Initialize(
                    extension.gasDef,
                    extension.gasReleaseDelayTicks,
                    extension.gasEmissionRadius,
                    extension.gasDensity,
                    extension.gasEdgeDensityFactor,
                    extension.gasEmissionDurationTicksMin,
                    extension.gasEmissionDurationTicksMax,
                    extension.gasEmissionIntervalTicks,
                    extension.gasPulseDensityFactor);
                if (emitter != null)
                {
                    GenSpawn.Spawn(emitter, impactCell, impactMap);
                }
            }

            base.Impact(hitThing, blockedByShield);
        }
    }
}
