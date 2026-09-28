using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed class SpongeRoundDamageExtension : DefModExtension
    {
        public HediffDef suppressionHediff;
        public float severityPerHit = 0.3f;
    }

    public sealed class DamageWorker_SpongeRound : DamageWorker_Blunt
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            DamageResult result = base.Apply(dinfo, victim);
            if (!(victim is Pawn pawn) || pawn.RaceProps?.IsFlesh != true ||
                pawn.Dead || result.totalDamageDealt <= 0f)
            {
                return result;
            }

            SpongeRoundDamageExtension extension =
                def.GetModExtension<SpongeRoundDamageExtension>();
            if (extension?.suppressionHediff == null)
            {
                return result;
            }

            Hediff suppression = pawn.health.hediffSet.GetFirstHediffOfDef(
                extension.suppressionHediff);
            float severityGain = Mathf.Max(0.01f, extension.severityPerHit);
            if (suppression == null)
            {
                suppression = HediffMaker.MakeHediff(extension.suppressionHediff, pawn);
                suppression.Severity = Mathf.Min(
                    severityGain,
                    extension.suppressionHediff.maxSeverity);
                pawn.health.AddHediff(suppression);
            }
            else
            {
                suppression.Severity = Mathf.Min(
                    suppression.Severity + severityGain,
                    extension.suppressionHediff.maxSeverity);
            }

            return result;
        }
    }

    public sealed class M651CSProjectileExtension : DefModExtension
    {
        public HelodGasDef gasDef;
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
