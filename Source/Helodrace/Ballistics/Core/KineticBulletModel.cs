using System;
using System.Collections.Generic;
using System.Numerics;

namespace Helodrace.Ballistics
{
    /// <summary>Core hardness class of a small-arms projectile. Selects per-material coefficients.</summary>
    public enum PenetratorClass
    {
        /// <summary>Lead or copper core (FMJ, OTM, monolithic copper).</summary>
        Soft,
        /// <summary>Mild steel core or steel tip (M855, 12.7 mm ball).</summary>
        MildSteel,
        /// <summary>Hardened steel core (M855A1, M80A1, 12.7 mm API).</summary>
        HardSteel,
        /// <summary>Tungsten or tungsten-carbide core (M995, M993, SLAP).</summary>
        Tungsten
    }

    public enum ArmorMaterialFamily
    {
        Metal,
        Ceramic,
        Fiber
    }

    /// <summary>
    /// Compiled material response for kinetic small-arms impacts. Density is a measured property [physical];
    /// every per-class coefficient is a calibrated game value [game] until a sourced value replaces it.
    /// </summary>
    public sealed class KineticMaterial
    {
        public const int ClassCount = 4;

        public readonly string Id;
        public readonly ArmorMaterialFamily Family;
        public readonly float DensityKgM3;
        private readonly float[] limitCoefficient;
        private readonly float[] erosion;
        private readonly float[] mushroom;
        /// <summary>Relative standard deviation of the ballistic limit (lot and shot scatter).</summary>
        public readonly float Scatter;
        /// <summary>Fraction of the incoming wound converted to blunt trauma when this layer stops the round.</summary>
        public readonly float TraumaTransfer;
        /// <summary>Durability loss per joule absorbed (before the item's own material coefficient).</summary>
        public readonly float WearPerJoule;

        public KineticMaterial(string id, ArmorMaterialFamily family, float densityKgM3,
            float[] limitCoefficient, float[] erosion, float[] mushroom,
            float scatter, float traumaTransfer, float wearPerJoule)
        {
            Id = id ?? string.Empty;
            Family = family;
            DensityKgM3 = densityKgM3;
            this.limitCoefficient = Copy(limitCoefficient, 0f);
            this.erosion = Copy(erosion, 0f);
            this.mushroom = Copy(mushroom, 1f);
            Scatter = scatter;
            TraumaTransfer = traumaTransfer;
            WearPerJoule = wearPerJoule;
        }

        public float LimitCoefficient(PenetratorClass penetrator) => limitCoefficient[(int)penetrator];
        public float Erosion(PenetratorClass penetrator) => erosion[(int)penetrator];
        public float Mushroom(PenetratorClass penetrator) => mushroom[(int)penetrator];

        public IEnumerable<string> Validate()
        {
            if (!(DensityKgM3 > 0f)) yield return Id + ": density must be positive";
            for (int i = 0; i < ClassCount; i++)
            {
                if (!(limitCoefficient[i] > 0f)) yield return Id + ": ballistic limit coefficient must be positive";
                if (!(erosion[i] >= 0f && erosion[i] < 1f)) yield return Id + ": erosion must be in [0, 1)";
                if (!(mushroom[i] >= 1f)) yield return Id + ": mushroom factor must be at least 1";
            }
            if (!(Scatter >= 0f && Scatter < 0.4f)) yield return Id + ": scatter must be in [0, 0.4)";
            if (!(TraumaTransfer >= 0f && TraumaTransfer <= 1f)) yield return Id + ": trauma transfer must be in [0, 1]";
            if (!(WearPerJoule >= 0f)) yield return Id + ": wear per joule must not be negative";
        }

        private static float[] Copy(float[] values, float fallback)
        {
            var copy = new float[ClassCount];
            for (int i = 0; i < ClassCount; i++)
                copy[i] = values != null && i < values.Length ? values[i] : fallback;
            return copy;
        }
    }

    public readonly struct KineticLayer
    {
        public readonly KineticMaterial Material;
        public readonly float ThicknessM;

        public KineticLayer(KineticMaterial material, float thicknessM)
        {
            Material = material;
            ThicknessM = thicknessM;
        }
    }

    public struct KineticLayerResult
    {
        public bool Perforated;
        public float Obliquity;
        public float LosArealDensity;
        public float LimitVelocity;
        public float SampledLimitVelocity;
        public float VelocityIn;
        public float VelocityOut;
        public float AbsorbedJ;
        public PenetratorState Out;
    }

    public struct KineticLayupResult
    {
        public bool Perforated;
        /// <summary>Index of the layer that stopped the round, or -1.</summary>
        public int StoppingLayer;
        public float AbsorbedJ;
        public float EnergyIn;
        public float EnergyOut;
        public PenetratorState Out;
    }

    /// <summary>
    /// Kinetic small-arms penetration of one armor layer.
    ///   ballistic limit  V50 = k(material, class) * sqrt(AD_LOS * A_p / m_p)          [physical form, game k]
    ///   perforation      v > V50 * (1 + scatter * z),  z ~ N(0, 1) clamped to +-2.5    [game scatter]
    ///   residual         v_r = sqrt(v^2 - V_bl^2)  (Lambert-Jonas with p = 2)          [physical approximation]
    ///   ceramic face     erodes the core (mass) and flattens it (presented area)       [game]
    /// The square-root form is the energy balance of a projectile spending its energy on an areal-density
    /// cylinder of its own presented area; it is the same dimensionless group (AD * A_p / m_p) that the Cunniff
    /// scaling uses for fibre armour. The coefficient is calibrated, not derived.
    /// </summary>
    public static class KineticBulletModel
    {
        public const float MaxScatterSigmas = 2.5f;

        public static float PresentedArea(float diameterM) => MathF.PI * 0.25f * diameterM * diameterM;

        /// <summary>Areal density along the shot line, kg/m^2. [physical] Effectiveness is the damage-state factor.</summary>
        public static float LosArealDensity(KineticMaterial material, float thicknessM, float obliquity, float effectiveness) =>
            material.DensityKgM3 * BallisticGeometry.LineOfSightThickness(thicknessM, obliquity) * Math.Clamp(effectiveness, 0f, 1f);

        public static float LimitVelocity(KineticMaterial material, PenetratorClass penetrator, float losArealDensity,
            float massKg, float diameterM)
        {
            if (massKg <= 0f || diameterM <= 0f || losArealDensity <= 0f) return 0f;
            return material.LimitCoefficient(penetrator) * MathF.Sqrt(losArealDensity * PresentedArea(diameterM) / massKg);
        }

        /// <summary>
        /// Probability that a round at velocity v perforates a layer of limit V50. Matches the sampling in
        /// <see cref="ResolveLayer"/>: z is clamped (not truncated), so beyond +-2.5 sigma the outcome is certain.
        /// </summary>
        public static float PerforationProbability(float velocity, float limitVelocity, float scatter)
        {
            if (limitVelocity <= 0f) return 1f;
            if (scatter <= 0f) return velocity > limitVelocity ? 1f : 0f;
            float z = (velocity / limitVelocity - 1f) / scatter;
            if (z > MaxScatterSigmas) return 1f;
            if (z <= -MaxScatterSigmas) return 0f;
            return NormalCdf(z);
        }

        public static KineticLayerResult ResolveLayer(in PenetratorState penetrator, PenetratorClass penetratorClass,
            KineticMaterial material, float thicknessM, float obliquity, float effectiveness, ref BallisticRng rng)
        {
            var result = new KineticLayerResult
            {
                Obliquity = obliquity,
                VelocityIn = penetrator.Speed,
                Out = penetrator
            };
            float energyIn = penetrator.KineticEnergyJ;
            result.LosArealDensity = LosArealDensity(material, thicknessM, obliquity, effectiveness);
            result.LimitVelocity = LimitVelocity(material, penetratorClass, result.LosArealDensity,
                penetrator.MassKg, penetrator.DiameterM);
            float z = Math.Clamp(rng.NextGaussian(), -MaxScatterSigmas, MaxScatterSigmas);
            result.SampledLimitVelocity = MathF.Max(0f, result.LimitVelocity * (1f + material.Scatter * z));
            float velocity = result.VelocityIn;
            if (velocity <= result.SampledLimitVelocity || penetrator.IsSpent)
            {
                result.Perforated = false;
                result.VelocityOut = 0f;
                result.AbsorbedJ = energyIn;
                result.Out.Velocity = Vector3.Zero;
                result.Out.Condition = PenetratorCondition.Spent;
                return result;
            }

            float residual = MathF.Sqrt(velocity * velocity - result.SampledLimitVelocity * result.SampledLimitVelocity);
            PenetratorState output = penetrator;
            output.Velocity = penetrator.Direction * residual;
            if (material.Family == ArmorMaterialFamily.Ceramic)
            {
                output.MassKg *= 1f - material.Erosion(penetratorClass);
                output.DiameterM *= material.Mushroom(penetratorClass);
                output.Condition = penetratorClass == PenetratorClass.Soft ? PenetratorCondition.Deformed : PenetratorCondition.Eroded;
            }
            result.Perforated = true;
            result.VelocityOut = residual;
            result.Out = output;
            result.AbsorbedJ = MathF.Max(0f, energyIn - output.KineticEnergyJ);
            return result;
        }

        /// <summary>
        /// A layup met at one obliquity (flat plate or one curved panel). Each layer rolls its own stream so the
        /// result of an outer layer does not shift the rolls of inner layers.
        /// </summary>
        public static KineticLayupResult ResolveLayup(in PenetratorState penetrator, PenetratorClass penetratorClass,
            IReadOnlyList<KineticLayer> layers, float obliquity, float effectiveness, ulong seed,
            ImpactTrace trace = null, string label = null)
        {
            var result = new KineticLayupResult
            {
                Perforated = true,
                StoppingLayer = -1,
                EnergyIn = penetrator.KineticEnergyJ,
                Out = penetrator
            };
            PenetratorState current = penetrator;
            for (int i = 0; i < layers.Count; i++)
            {
                KineticLayer layer = layers[i];
                var rng = BallisticSeed.Stream(seed, (label ?? string.Empty) + "#" + i);
                KineticLayerResult step = ResolveLayer(current, penetratorClass, layer.Material, layer.ThicknessM,
                    obliquity, effectiveness, ref rng);
                result.AbsorbedJ += step.AbsorbedJ;
                if (trace != null)
                {
                    string prefix = (label ?? "layer") + "." + i + "." + layer.Material.Id + ".";
                    trace.Value(prefix + "obliquityDeg", step.Obliquity * BallisticGeometry.RadToDeg, EvidenceGrade.Physical);
                    trace.Value(prefix + "losArealDensity", step.LosArealDensity, EvidenceGrade.Physical);
                    trace.Value(prefix + "v50", step.LimitVelocity, EvidenceGrade.Game);
                    trace.Value(prefix + "vLimitSample", step.SampledLimitVelocity, EvidenceGrade.Game);
                    trace.Value(prefix + "vIn", step.VelocityIn, EvidenceGrade.Physical);
                    trace.Value(prefix + "vOut", step.VelocityOut, EvidenceGrade.Physical);
                }
                current = step.Out;
                if (!step.Perforated)
                {
                    result.Perforated = false;
                    result.StoppingLayer = i;
                    break;
                }
            }
            result.Out = current;
            result.EnergyOut = result.Perforated ? current.KineticEnergyJ : 0f;
            return result;
        }

        /// <summary>
        /// Wound multiplier after armour. [game] g(E_out / E_in) = sqrt(E_out / E_in): g(1) = 1, so an unarmoured
        /// hit keeps the cartridge's balance damage, and the product over layers equals g over the whole stack.
        /// </summary>
        public static float WoundFactor(float energyIn, float energyOut)
        {
            if (energyIn <= 0f) return 0f;
            return MathF.Sqrt(Math.Clamp(energyOut / energyIn, 0f, 1f));
        }

        /// <summary>
        /// Armour penetration handed to layers that still use the vanilla roll, scaled like the existing
        /// distance model (velocity^1.2). [game]
        /// </summary>
        public static float VanillaPenetrationFactor(float velocityIn, float velocityOut)
        {
            if (velocityIn <= 0f) return 0f;
            return MathF.Pow(Math.Clamp(velocityOut / velocityIn, 0f, 1f), 1.2f);
        }

        /// <summary>Standard normal CDF, Abramowitz-Stegun 7.1.26 (|error| &lt; 1.5e-7).</summary>
        public static float NormalCdf(float x)
        {
            double z = Math.Abs(x) / Math.Sqrt(2.0);
            double t = 1.0 / (1.0 + 0.3275911 * z);
            double erf = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t
                + 0.254829592) * t * Math.Exp(-z * z);
            return (float)(x >= 0 ? 0.5 * (1.0 + erf) : 0.5 * (1.0 - erf));
        }
    }

    /// <summary>Body coverage of a protective item in the body-part frame. Azimuth 0 = front, +90 = right.</summary>
    public readonly struct CoverageRegion
    {
        public readonly float[] DirectionCenters;
        public readonly float HalfArcDegrees;
        public readonly float HeightMin;
        public readonly float HeightMax;

        public CoverageRegion(float[] directionCenters, float arcDegrees, float heightMin, float heightMax)
        {
            DirectionCenters = directionCenters;
            HalfArcDegrees = Math.Clamp(arcDegrees, 0f, 360f) * 0.5f;
            HeightMin = heightMin;
            HeightMax = heightMax;
        }

        public bool Directional => DirectionCenters != null && DirectionCenters.Length > 0;

        /// <summary>True when the point is covered; centerIndex selects the plate (front/back) that covers it.</summary>
        public bool Contains(float azimuthDegrees, float heightFraction, out int centerIndex)
        {
            centerIndex = -1;
            if (heightFraction < HeightMin || heightFraction > HeightMax) return false;
            if (!Directional)
            {
                return true;
            }
            float best = float.MaxValue;
            for (int i = 0; i < DirectionCenters.Length; i++)
            {
                float delta = MathF.Abs(BallisticGeometry.DeltaDegrees(DirectionCenters[i], azimuthDegrees));
                if (delta <= HalfArcDegrees && delta < best)
                {
                    best = delta;
                    centerIndex = i;
                }
            }
            return centerIndex >= 0;
        }
    }

    /// <summary>Impact point on a body part, in the part's local frame (+Z = the way the pawn faces).</summary>
    public readonly struct BodyHit
    {
        public readonly Vector3 LocalPoint;
        public readonly Vector3 LocalNormal;
        public readonly Vector3 LocalTravel;
        public readonly float AzimuthDegrees;
        public readonly float HeightFraction;
        public readonly float SurfaceObliquity;

        public BodyHit(Vector3 localPoint, Vector3 localNormal, Vector3 localTravel, float azimuth, float height, float obliquity)
        {
            LocalPoint = localPoint;
            LocalNormal = localNormal;
            LocalTravel = localTravel;
            AzimuthDegrees = azimuth;
            HeightFraction = height;
            SurfaceObliquity = obliquity;
        }

        /// <summary>Obliquity against a flat plate facing the given body azimuth (plate carriers, shields).</summary>
        public float PlateObliquity(float plateFacingDegrees)
        {
            Vector3 normal = BallisticGeometry.PlateNormal(plateFacingDegrees, 0f);
            return BallisticGeometry.TryObliquity(LocalTravel, normal, out float obliquity) ? obliquity : MathF.PI * 0.5f;
        }
    }

    public static class BodyHitGeometry
    {
        /// <summary>Approximate half-extents (x width, y height, z depth), metres. [game]</summary>
        public static readonly Vector3 Torso = new Vector3(0.17f, 0.30f, 0.11f);
        public static readonly Vector3 Head = new Vector3(0.08f, 0.11f, 0.10f);
        public static readonly Vector3 Neck = new Vector3(0.06f, 0.05f, 0.06f);
        public static readonly Vector3 Limb = new Vector3(0.05f, 0.22f, 0.05f);

        /// <summary>Samples where a parallel beam with the given local travel meets an ellipsoidal part. [physical]</summary>
        public static BodyHit Sample(Vector3 radii, Vector3 localTravel, ref BallisticRng rng)
        {
            float u = rng.NextFloat();
            float v = rng.NextFloat();
            SurfaceImpact impact = BallisticGeometry.SampleEllipsoidImpact(Vector3.Zero, radii, localTravel, u, v);
            float azimuth = MathF.Atan2(impact.Point.X, impact.Point.Z) * BallisticGeometry.RadToDeg;
            float height = radii.Y > 0f ? Math.Clamp(impact.Point.Y / radii.Y, -1f, 1f) : 0f;
            return new BodyHit(impact.Point, impact.OutwardNormal, Vector3.Normalize(localTravel), azimuth, height, impact.Obliquity);
        }
    }
}
