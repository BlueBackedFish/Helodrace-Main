using System;
using System.Collections.Generic;
using System.Numerics;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// Shaped-charge warhead data. The nominal penetration is the warhead's RHA figure at its design stand-off
    /// [game: public-claim range, not test data]; the jet itself is the hydrodynamic rod that figure implies.
    /// </summary>
    public sealed class ShapedChargeWarhead
    {
        public float PenetrationRhaM;
        /// <summary>Tandem precursor's own RHA figure; 0 for a single charge.</summary>
        public float PrecursorPenetrationRhaM;
        /// <summary>Delay between precursor and main jet arriving at the target, seconds. [game]</summary>
        public float TandemDelayS = 120e-6f;
        public float JetDensityKgM3 = ShapedChargeModel.CopperDensity;
        public float TipVelocityMs = 8000f;
        public float TailVelocityMs = 2000f;
        /// <summary>Shot-to-shot scatter of the jet (stand-off, liner quality). [game]</summary>
        public float Scatter = 0.05f;
        /// <summary>Fuze failure on grazing strikes: logistic around this obliquity. [game]</summary>
        public float GrazeCriticalDegrees = 80f;
        public float GrazeWidthDegrees = 2f;

        public bool Tandem => PrecursorPenetrationRhaM > 0f;
    }

    public sealed class JetResult
    {
        public ImpactResult Result;
        public int StoppingLayer = -1;
        public bool Dud;
        public float PotentialInRhaM;
        public float PotentialOutRhaM;
        public readonly List<VehicleLayerStep> Steps = new List<VehicleLayerStep>(4);

        public bool Perforated => Result == ImpactResult.Perforation || Result == ImpactResult.Overmatch;

        public void Clear()
        {
            Result = ImpactResult.None;
            StoppingLayer = -1;
            Dud = false;
            PotentialInRhaM = PotentialOutRhaM = 0f;
            Steps.Clear();
        }
    }

    /// <summary>
    /// Shaped-charge jet against armour layers (design doc 4.2). Hydrodynamic penetration P = L_jet sqrt(rho_j / rho_t)
    /// [physical approximation]: the jet is tracked as its remaining RHA-equivalent potential, and each layer spends
    /// its RHA-equivalent LOS thickness (density ratio x massEfficiencyVsJet [game] for non-steels). Jets do not
    /// ricochet; extreme grazing strikes fail to fuze [game].
    /// </summary>
    public static class ShapedChargeModel
    {
        public const float CopperDensity = 8960f;
        /// <summary>Residual potential share above which a perforation is an overmatch. [game]</summary>
        public const float OvermatchShare = 0.5f;

        /// <summary>Physical jet length implied by an RHA figure: L = P / sqrt(rho_j / rho_RHA). [physical approximation]</summary>
        public static float JetLength(float penetrationRhaM, float jetDensity) =>
            penetrationRhaM / MathF.Sqrt(jetDensity / VehicleKineticModel.RhaDensity);

        public static float DudProbability(ShapedChargeWarhead warhead, float obliquity) =>
            VehicleKineticModel.RicochetProbability(obliquity, warhead.GrazeCriticalDegrees, warhead.GrazeWidthDegrees);

        /// <summary>
        /// Jet with the given potential (after any reactive disruption) through the layers at one obliquity.
        /// potentialScale multiplies the sampled potential (reactive armour residual).
        /// </summary>
        public static void Resolve(ShapedChargeWarhead warhead, float potentialRhaM, IReadOnlyList<VehicleArmorLayer> layers,
            float obliquity, ulong seed, JetResult result, ImpactTrace trace = null, string label = "jet")
        {
            result.Clear();
            var rng = BallisticSeed.Stream(seed, label + "#scatter");
            float z = Math.Clamp(rng.NextGaussian(), -KineticBulletModel.MaxScatterSigmas, KineticBulletModel.MaxScatterSigmas);
            float potential = MathF.Max(0f, potentialRhaM * (1f + warhead.Scatter * z));
            result.PotentialInRhaM = potential;
            trace?.Value(label + ".potentialRhaMm", potential * 1000f, EvidenceGrade.Game);
            for (int i = 0; i < (layers?.Count ?? 0); i++)
            {
                VehicleArmorLayer layer = layers[i];
                float los = BallisticGeometry.LineOfSightThickness(layer.ThicknessM, obliquity);
                float equivalent = layer.Material.RhaEquivalentVsJet(los);
                var step = new VehicleLayerStep
                {
                    MaterialId = layer.Material.Id, LosThicknessM = los, RhaEquivalentM = equivalent,
                    Capability = potential, Perforated = potential > equivalent
                };
                result.Steps.Add(step);
                trace?.Value(label + ".layer." + i + "." + layer.Material.Id + ".rhaEquivalentMm", equivalent * 1000f,
                    layer.Material.SteelTarget ? EvidenceGrade.Physical : EvidenceGrade.Game);
                if (!step.Perforated)
                {
                    result.Result = i == 0 ? ImpactResult.Stopped : ImpactResult.PartialPenetration;
                    result.StoppingLayer = i;
                    result.PotentialOutRhaM = 0f;
                    return;
                }
                potential -= equivalent;
            }
            result.PotentialOutRhaM = potential;
            result.Result = result.PotentialInRhaM > 0f && potential / result.PotentialInRhaM >= OvermatchShare
                ? ImpactResult.Overmatch : ImpactResult.Perforation;
            trace?.Value(label + ".residualRhaMm", potential * 1000f, EvidenceGrade.Game);
        }

        /// <summary>Damage share after armour: 0.5 + 0.5 x residual potential share. [game]</summary>
        public static float DamageFactor(JetResult result) =>
            result.Perforated && result.PotentialInRhaM > 0f
                ? 0.5f + 0.5f * Math.Clamp(result.PotentialOutRhaM / result.PotentialInRhaM, 0f, 1f)
                : 0f;
    }

    public enum ReactiveKind
    {
        /// <summary>Explosive reactive armour: consumed when it initiates.</summary>
        Explosive,
        /// <summary>Non-explosive (bulging) armour: reusable, weaker, degrades with hits.</summary>
        NonExplosive
    }

    /// <summary>Compiled reactive module type (design doc 3.3). Every coefficient is a game value unless noted.</summary>
    public sealed class ReactiveModuleSpec
    {
        public string Id = string.Empty;
        public ReactiveKind Kind = ReactiveKind.Explosive;
        /// <summary>Tilt of the internal sandwich from the module face, degrees (cassettes are mounted inclined inside).</summary>
        public float SandwichTiltDegrees = 60f;
        /// <summary>Flyer plate speed, m/s, and its extent across the jet line, m.</summary>
        public float FlyerVelocityMs = 500f;
        public float FlyerWidthM = 0.3f;
        /// <summary>Delay from jet tip arrival to flyer motion, s.</summary>
        public float ActivationDelayS = 3e-6f;
        /// <summary>Share of the engaged jet the moving plates defeat.</summary>
        public float Efficiency = 1f;
        /// <summary>KE rounds initiate the module above this kinetic energy, J.</summary>
        public float KineticTriggerEnergyJ = 60000f;
        /// <summary>Rod/shot length and mass share kept after a triggered module (lateral impulse, yaw). [game]</summary>
        public float KineticResidual = 0.9f;
        /// <summary>Health lost per damage point from small arms (no initiation) and from blast.</summary>
        public float SmallArmsWear = 0.04f;
        public float BlastWear = 0.65f;
        /// <summary>Non-explosive: efficiency lost per hit taken.</summary>
        public float DegradationPerHit = 0.2f;

        public IEnumerable<string> Validate()
        {
            if (!(SandwichTiltDegrees >= 0f && SandwichTiltDegrees < 89f)) yield return Id + ": sandwich tilt must be in [0, 89)";
            if (!(FlyerVelocityMs > 0f && FlyerWidthM > 0f)) yield return Id + ": flyer speed and width must be positive";
            if (!(ActivationDelayS >= 0f)) yield return Id + ": activation delay must not be negative";
            if (!(Efficiency >= 0f && Efficiency <= 1f)) yield return Id + ": efficiency must be in [0, 1]";
            if (!(KineticResidual > 0f && KineticResidual <= 1f)) yield return Id + ": kinetic residual must be in (0, 1]";
            if (!(DegradationPerHit >= 0f && DegradationPerHit <= 1f)) yield return Id + ": degradation must be in [0, 1]";
        }
    }

    /// <summary>
    /// Flyer-plate disruption of a jet [physical approximation, game coefficients]. The sandwich normal is the module
    /// face normal tilted by its internal angle; theta is the jet's obliquity to it.
    ///   tip passes untouched for the activation delay:  L_tip = v_tip t_d
    ///   plates stay across the jet line while the cut slides less than half their width: T = (W/2) / (v_f tan theta)
    ///   engaged jet length = v_mean T, the plates defeat efficiency x sin(theta) of it (no lateral sweep at normal incidence)
    /// A tandem main jet arriving t_m later meets only the overlap the plates still have: 1 - v_f t_m tan(theta) / (W/2).
    /// </summary>
    public static class ReactiveArmorModel
    {
        public static Vector3 SandwichNormal(Vector3 faceNormal, float tiltDegrees)
        {
            Vector3 n = Vector3.Normalize(faceNormal);
            if (tiltDegrees <= 0f) return n;
            // Tilt toward vertical (the cassette leans its plates down from the face, as on hull sides). For a roof
            // face the tilt goes toward the vehicle-forward horizontal instead.
            Vector3 axisHint = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            Vector3 toward = Vector3.Normalize(axisHint - Vector3.Dot(axisHint, n) * n);
            float r = tiltDegrees * BallisticGeometry.DegToRad;
            return Vector3.Normalize(n * MathF.Cos(r) + toward * MathF.Sin(r));
        }

        /// <summary>Remaining share of a jet's potential after one module. 1 = no effect.</summary>
        public static float JetResidual(ReactiveModuleSpec spec, ShapedChargeWarhead warhead, float jetLengthM, float theta,
            float arrivalDelayS = 0f, float efficiencyScale = 1f)
        {
            if (jetLengthM <= 0f) return 1f;
            float tan = MathF.Tan(MathF.Min(theta, 85f * BallisticGeometry.DegToRad));
            float sin = MathF.Sin(theta);
            float halfWidth = spec.FlyerWidthM * 0.5f;
            float overlap = 1f;
            if (arrivalDelayS > 0f)
                overlap = Math.Clamp(1f - spec.FlyerVelocityMs * arrivalDelayS * tan / halfWidth, 0f, 1f);
            if (overlap <= 0f) return 1f;
            float tip = arrivalDelayS > 0f ? 0f : MathF.Min(jetLengthM, warhead.TipVelocityMs * spec.ActivationDelayS);
            float contact = tan > 1e-4f ? halfWidth / (spec.FlyerVelocityMs * tan) : float.MaxValue;
            float mean = 0.5f * (warhead.TipVelocityMs + warhead.TailVelocityMs);
            float engaged = MathF.Min(jetLengthM - tip, mean * contact) / jetLengthM;
            float defeated = spec.Efficiency * efficiencyScale * sin * overlap * engaged;
            return Math.Clamp(1f - defeated, 0f, 1f);
        }
    }
}
