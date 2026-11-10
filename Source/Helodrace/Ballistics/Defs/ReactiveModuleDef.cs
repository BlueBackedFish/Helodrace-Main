using System.Collections.Generic;
using Verse;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// Reactive armour module type (design doc 3.3). Geometry of the flyer-plate interaction is a physical
    /// approximation; every value here is a game value [game] (quantitative ERA data is not public).
    /// </summary>
    public sealed class ReactiveModuleDef : Def
    {
        public ReactiveKind kind = ReactiveKind.Explosive;
        public float sandwichTiltDegrees = 60f;
        public float flyerVelocityMetersPerSecond = 500f;
        public float flyerWidthMeters = 0.3f;
        public float activationDelayMicroseconds = 3f;
        public float efficiency = 1f;
        public float kineticTriggerEnergyKilojoules = 60f;
        public float kineticResidual = 0.9f;
        public float smallArmsWear = 0.04f;
        public float blastWear = 0.65f;
        public float degradationPerHit = 0.2f;
        // Detonation effects (game boundary): blast at the module and a fan of fragments away from the carrier.
        public ThingDef fragment;
        public int fragmentCount = 16;
        public float fragmentRange = 7f;

        private ReactiveModuleSpec spec;

        public ReactiveModuleSpec Spec => spec ?? (spec = new ReactiveModuleSpec
        {
            Id = defName,
            Kind = kind,
            SandwichTiltDegrees = sandwichTiltDegrees,
            FlyerVelocityMs = flyerVelocityMetersPerSecond,
            FlyerWidthM = flyerWidthMeters,
            ActivationDelayS = activationDelayMicroseconds * 1e-6f,
            Efficiency = efficiency,
            KineticTriggerEnergyJ = kineticTriggerEnergyKilojoules * 1000f,
            KineticResidual = kineticResidual,
            SmallArmsWear = smallArmsWear,
            BlastWear = blastWear,
            DegradationPerHit = degradationPerHit
        });

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            foreach (string error in Spec.Validate()) yield return error;
            if (kind == ReactiveKind.Explosive && (fragment?.projectile == null || fragmentCount < 1 || fragmentRange <= 0f))
                yield return defName + ": explosive modules need a fragment projectile, count and range.";
        }
    }
}
