using System;
using System.Collections.Generic;
using System.Numerics;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// Lanz-Odermatt perforation coefficients of one rod material. [experimental] Values and test counts are
    /// W. Odermatt's perforation fit (longrods.ch perfcalc, reproduced at xplicitmaterials.com
    /// "Lanz-Odermatt Penetration Calculator"): tungsten 99 tests / 3.3 % sd, DU 42 / 3.5 %, steel 40 / 2.8 %.
    /// Units of the fit: density kg/m^3, velocity km/s, Brinell hardness.
    /// </summary>
    public sealed class LongRodCoefficients
    {
        /// <summary>Material-independent constants of the same fit. [experimental]</summary>
        public const float B0 = 0.283f;
        public const float B1 = 0.0656f;
        public const float M = -0.224f;

        public readonly float A;
        public readonly float C0;
        public readonly float C1;
        /// <summary>Steel rods use s^2 = c0 BHNt^k BHNp^n / rho_p instead of (c0 + c1 BHNt) BHNt / rho_p.</summary>
        public readonly bool SteelForm;
        public readonly float K;
        public readonly float N;
        /// <summary>Relative standard deviation of the fit (used as shot scatter).</summary>
        public readonly float Scatter;
        public readonly string Source;

        public LongRodCoefficients(float a, float c0, float c1, float scatter, string source,
            bool steelForm = false, float k = 0f, float n = 0f)
        {
            A = a;
            C0 = c0;
            C1 = c1;
            Scatter = scatter;
            Source = source ?? string.Empty;
            SteelForm = steelForm;
            K = k;
            N = n;
        }

        /// <summary>s^2 in (km/s)^2: the target-strength term of the exponential.</summary>
        public float StrengthTerm(float targetBrinell, float rodBrinell, float rodDensityKgM3)
        {
            if (rodDensityKgM3 <= 0f) return 0f;
            if (SteelForm)
                return C0 * MathF.Pow(MathF.Max(1f, targetBrinell), K) * MathF.Pow(MathF.Max(1f, rodBrinell), N) / rodDensityKgM3;
            return MathF.Max(0f, (C0 + C1 * targetBrinell) * targetBrinell) / rodDensityKgM3;
        }

        public IEnumerable<string> Validate(string owner)
        {
            if (!(A > 0f)) yield return owner + ": long-rod coefficient a must be positive";
            if (!(C0 > 0f)) yield return owner + ": long-rod coefficient c0 must be positive";
            if (!(Scatter >= 0f && Scatter < 0.2f)) yield return owner + ": long-rod scatter must be in [0, 0.2)";
            if (Source.Length == 0) yield return owner + ": long-rod coefficients need a source";
        }
    }

    /// <summary>Penetrator body material (rod or AP core).</summary>
    public sealed class PenetratorMaterial
    {
        public readonly string Id;
        public readonly float DensityKgM3;
        public readonly float BrinellHardness;
        /// <summary>Yield strength, Pa. Tate ricochet input. [physical property]</summary>
        public readonly float YieldStrengthPa;
        /// <summary>Null when no sourced long-rod fit exists for this material.</summary>
        public readonly LongRodCoefficients LongRod;

        public PenetratorMaterial(string id, float densityKgM3, float brinellHardness, float yieldStrengthPa,
            LongRodCoefficients longRod)
        {
            Id = id ?? string.Empty;
            DensityKgM3 = densityKgM3;
            BrinellHardness = brinellHardness;
            YieldStrengthPa = yieldStrengthPa;
            LongRod = longRod;
        }
    }

    /// <summary>
    /// Vehicle armour material. Density and hardness are measured properties [physical]. The small-arms block is
    /// the phase 2 response [game]. Mass efficiency against KE is the RHA-equivalence factor for materials the
    /// long-rod and De Marre fits do not cover (aluminium, composites) [game].
    /// </summary>
    public sealed class VehicleArmorMaterial
    {
        public readonly string Id;
        public readonly KineticMaterial SmallArms;
        public readonly float DensityKgM3;
        public readonly float BrinellHardness;
        public readonly float MassEfficiencyVsKinetic;
        /// <summary>True for steels: the long-rod fit takes this target's own density and hardness.</summary>
        public readonly bool SteelTarget;

        /// <summary>RHA-equivalence factor against shaped-charge jets (per unit mass). [game]</summary>
        public readonly float MassEfficiencyVsJet;

        public VehicleArmorMaterial(KineticMaterial smallArms, float brinellHardness, float massEfficiencyVsKinetic,
            bool steelTarget, float massEfficiencyVsJet = 1f)
        {
            MassEfficiencyVsJet = massEfficiencyVsJet;
            SmallArms = smallArms;
            Id = smallArms?.Id ?? string.Empty;
            DensityKgM3 = smallArms?.DensityKgM3 ?? 0f;
            BrinellHardness = brinellHardness;
            MassEfficiencyVsKinetic = massEfficiencyVsKinetic;
            SteelTarget = steelTarget;
        }

        /// <summary>RHA-equivalent line-of-sight thickness against KE, metres.</summary>
        public float RhaEquivalent(float losThicknessM) =>
            SteelTarget ? losThicknessM : losThicknessM * DensityKgM3 / VehicleKineticModel.RhaDensity * MassEfficiencyVsKinetic;

        /// <summary>RHA-equivalent line-of-sight thickness against a jet, metres (density ratio x jet efficiency).</summary>
        public float RhaEquivalentVsJet(float losThicknessM) =>
            losThicknessM * DensityKgM3 / VehicleKineticModel.RhaDensity * MassEfficiencyVsJet;

        public IEnumerable<string> Validate()
        {
            if (SmallArms == null) { yield return "vehicle armour material without small-arms response"; yield break; }
            foreach (string error in SmallArms.Validate()) yield return error;
            if (!(BrinellHardness > 0f)) yield return Id + ": Brinell hardness must be positive";
            if (!(MassEfficiencyVsKinetic > 0f)) yield return Id + ": KE mass efficiency must be positive";
            if (!(MassEfficiencyVsJet > 0f)) yield return Id + ": jet mass efficiency must be positive";
        }
    }

    public readonly struct VehicleArmorLayer
    {
        public readonly VehicleArmorMaterial Material;
        public readonly float ThicknessM;

        public VehicleArmorLayer(VehicleArmorMaterial material, float thicknessM)
        {
            Material = material;
            ThicknessM = thicknessM;
        }
    }

    /// <summary>Kinetic round data for vehicle armour. Mechanism selects the model; nothing is guessed by name.</summary>
    public sealed class VehicleKineticRound
    {
        public PenetrationMechanism Mechanism = PenetrationMechanism.KineticBullet;
        public PenetratorClass SmallArmsClass = PenetratorClass.Soft;
        public PenetratorMaterial Core;
        /// <summary>De Marre ricochet: obliquity at which half the AP shots glance off. [game]</summary>
        public float RicochetCriticalDegrees = 72f;
        public float RicochetWidthDegrees = 3f;
    }

    public struct VehicleLayerStep
    {
        public string MaterialId;
        public float LosThicknessM;
        public float RhaEquivalentM;
        /// <summary>Long rod: perforable LOS thickness. De Marre / small arms: limit velocity.</summary>
        public float Capability;
        public bool Perforated;
    }

    public sealed class VehicleKineticResult
    {
        public ImpactResult Result;
        public int StoppingLayer = -1;
        public bool EarlyStop;
        public float EnergyIn;
        public float EnergyOut;
        public float RicochetProbability;
        public float TateCriticalVelocity;
        public PenetratorState Out;
        public readonly List<VehicleLayerStep> Steps = new List<VehicleLayerStep>(4);

        public bool Perforated => Result == ImpactResult.Perforation || Result == ImpactResult.Overmatch;

        public void Clear()
        {
            Result = ImpactResult.None;
            StoppingLayer = -1;
            EarlyStop = false;
            EnergyIn = EnergyOut = RicochetProbability = TateCriticalVelocity = 0f;
            Out = default;
            Steps.Clear();
        }
    }

    /// <summary>
    /// Kinetic penetration of vehicle armour (design doc 4.2). Three separate models, never merged:
    ///   small arms (KineticBullet)  phase 2 V50 model per layer, with a certain-stop early exit (tier T2)
    ///   AP shot (KineticHardCore)   De Marre ballistic limit  v = K d^0.75 t^0.7 / m^0.5   [experimental form, game K]
    ///   long rod (KineticLongRod)   Lanz-Odermatt perforation, eroding the rod layer by layer   [experimental]
    /// </summary>
    public static class VehicleKineticModel
    {
        /// <summary>Rolled homogeneous armour reference: density [physical], Brinell 270 (MIL-A-12560 range). </summary>
        public const float RhaDensity = 7850f;
        public const float RhaBrinell = 270f;
        /// <summary>De Marre armour coefficient for RHA. [game] Historical acceptance practice used about 2400
        /// (d, t in decimetres, m in kg, v in m/s); no primary source is wired in.</summary>
        public const float DeMarreRha = 2400f;
        public const float DeMarreScatter = 0.05f;
        /// <summary>Long-rod residual length share above which the hit is an overmatch. [game]</summary>
        public const float OvermatchShare = 0.5f;
        /// <summary>Range clamps of the long-rod fit. [game] The fit's published range is not wired in.</summary>
        public const float MinRodAspect = 4f;
        public const float MaxRodAspect = 40f;
        public const float MaxRodObliquityDegrees = 75f;
        public const float FitSpeedMinKmS = 0.8f;
        public const float FitSpeedMaxKmS = 2.2f;

        /// <summary>
        /// Lanz-Odermatt perforation limit: LOS plate thickness a rod of length L can just perforate, metres.
        ///   P / L = a / tanh(b0 + b1 L/D) * cos^m(theta) * sqrt(rho_p / rho_t) * exp(-s^2 / v^2)
        /// L/D and obliquity are clamped to the fit range; speed is not (the exponential tends to the
        /// hydrodynamic limit P = L sqrt(rho_p/rho_t) times the fit constant).
        /// </summary>
        public static float RodPerforationLimit(PenetratorMaterial rod, float lengthM, float diameterM, float speedMs,
            float obliquity, float targetDensity, float targetBrinell, ImpactTrace trace = null, string label = null)
        {
            LongRodCoefficients c = rod?.LongRod;
            if (c == null || lengthM <= 0f || diameterM <= 0f || speedMs <= 0f || targetDensity <= 0f) return 0f;
            float aspect = lengthM / diameterM;
            float clampedAspect = Math.Clamp(aspect, MinRodAspect, MaxRodAspect);
            float maxObliquity = MaxRodObliquityDegrees * BallisticGeometry.DegToRad;
            float clampedObliquity = MathF.Min(obliquity, maxObliquity);
            float v = speedMs * 0.001f;
            if (trace != null)
            {
                string prefix = (label ?? "rod") + ".";
                if (clampedAspect != aspect) trace.Warn(prefix + "L/D " + aspect.ToString("0.0") + " outside fit range; clamped");
                if (clampedObliquity < obliquity) trace.Warn(prefix + "obliquity beyond " + MaxRodObliquityDegrees + " deg; clamped");
                if (v < FitSpeedMinKmS || v > FitSpeedMaxKmS) trace.Warn(prefix + "speed " + v.ToString("0.00") + " km/s outside fit data; extrapolated");
            }
            float tanh = MathF.Tanh(LongRodCoefficients.B0 + LongRodCoefficients.B1 * clampedAspect);
            float obliquityTerm = MathF.Pow(MathF.Max(BallisticGeometry.MinimumCosine, MathF.Cos(clampedObliquity)), LongRodCoefficients.M);
            float density = MathF.Sqrt(rod.DensityKgM3 / targetDensity);
            float strength = MathF.Exp(-c.StrengthTerm(targetBrinell, rod.BrinellHardness, rod.DensityKgM3) / (v * v));
            return lengthM * c.A / tanh * obliquityTerm * density * strength;
        }

        /// <summary>
        /// Tate ricochet criterion [physical approximation] (Tate 1979, "A simple estimate of the minimum target
        /// obliquity required for the ricochet of a high speed long rod"). Ricochet when
        ///   v^2 &lt; 3 Y_p tan^3(theta) / (2 rho_p ((L^2 + D^2)/(L D)) (1 + sqrt(rho_p/rho_t)))
        /// with theta measured from the plate normal, so the critical speed grows with obliquity.
        /// </summary>
        public static float TateCriticalVelocity(PenetratorMaterial rod, float lengthM, float diameterM, float obliquity,
            float targetDensity)
        {
            if (rod == null || rod.YieldStrengthPa <= 0f || lengthM <= 0f || diameterM <= 0f || targetDensity <= 0f) return 0f;
            float tan = MathF.Tan(MathF.Min(obliquity, 89.5f * BallisticGeometry.DegToRad));
            float shape = (lengthM * lengthM + diameterM * diameterM) / (lengthM * diameterM);
            float denominator = 2f * rod.DensityKgM3 * shape * (1f + MathF.Sqrt(rod.DensityKgM3 / targetDensity));
            return MathF.Sqrt(3f * rod.YieldStrengthPa * tan * tan * tan / denominator);
        }

        /// <summary>De Marre ballistic limit, m/s: v = K d^0.75 t^0.7 / m^0.5 (d, t in dm, m in kg).</summary>
        public static float DeMarreLimit(float coefficient, float diameterM, float rhaThicknessM, float massKg)
        {
            if (massKg <= 0f || diameterM <= 0f || rhaThicknessM <= 0f) return 0f;
            return coefficient * MathF.Pow(diameterM * 10f, 0.75f) * MathF.Pow(rhaThicknessM * 10f, 0.7f) / MathF.Sqrt(massKg);
        }

        public static float RicochetProbability(float obliquity, float criticalDegrees, float widthDegrees)
        {
            if (criticalDegrees <= 0f || widthDegrees <= 0f) return 0f;
            float x = (obliquity * BallisticGeometry.RadToDeg - criticalDegrees) / widthDegrees;
            return 1f / (1f + MathF.Exp(-x));
        }

        /// <summary>
        /// Resolves one kinetic round against the layers of the plate it struck. The velocity must be in the
        /// same frame as the outward normal. Each layer rolls its own stream.
        /// </summary>
        public static void Resolve(VehicleKineticRound round, in PenetratorState penetrator,
            IReadOnlyList<VehicleArmorLayer> layers, Vector3 outwardNormal, ulong seed, VehicleKineticResult result,
            ImpactTrace trace = null)
        {
            result.Clear();
            result.EnergyIn = penetrator.KineticEnergyJ;
            result.Out = penetrator;
            if (layers == null || layers.Count == 0)
            {
                result.Result = ImpactResult.Perforation;
                result.EnergyOut = result.EnergyIn;
                return;
            }
            if (!BallisticGeometry.TryObliquity(penetrator.Velocity, outwardNormal, out float obliquity))
            {
                result.Result = ImpactResult.None;
                return;
            }
            trace?.Value("obliquityDeg", obliquity * BallisticGeometry.RadToDeg, EvidenceGrade.Physical);
            switch (round.Mechanism)
            {
                case PenetrationMechanism.KineticLongRod:
                    ResolveLongRod(round, penetrator, layers, obliquity, outwardNormal, seed, result, trace);
                    break;
                case PenetrationMechanism.KineticHardCore:
                    ResolveDeMarre(round, penetrator, layers, obliquity, outwardNormal, seed, result, trace);
                    break;
                default:
                    ResolveSmallArms(round, penetrator, layers, obliquity, outwardNormal, seed, result, trace);
                    break;
            }
            result.EnergyOut = result.Perforated ? result.Out.KineticEnergyJ : 0f;
        }

        private static void ResolveLongRod(VehicleKineticRound round, PenetratorState current,
            IReadOnlyList<VehicleArmorLayer> layers, float obliquity, Vector3 normal, ulong seed,
            VehicleKineticResult result, ImpactTrace trace)
        {
            PenetratorMaterial rod = round.Core;
            if (rod?.LongRod == null)
            {
                trace?.Warn("long rod without a sourced rod material; treated as small arms");
                ResolveSmallArms(round, current, layers, obliquity, normal, seed, result, trace);
                return;
            }
            float initialLength = current.LengthM;
            VehicleArmorMaterial face = layers[0].Material;
            float faceDensity = face.SteelTarget ? face.DensityKgM3 : RhaDensity;
            result.TateCriticalVelocity = TateCriticalVelocity(rod, current.LengthM, current.DiameterM, obliquity, faceDensity);
            trace?.Value("tateCriticalVelocity", result.TateCriticalVelocity, EvidenceGrade.Physical);
            if (current.Speed < result.TateCriticalVelocity)
            {
                Ricochet(current, normal, 0.9f, 0.05f, result);
                trace?.Note("Tate: rod ricochets off the strike face");
                return;
            }
            for (int i = 0; i < layers.Count; i++)
            {
                VehicleArmorLayer layer = layers[i];
                float los = BallisticGeometry.LineOfSightThickness(layer.ThicknessM, obliquity);
                float equivalent = layer.Material.RhaEquivalent(los);
                float density = layer.Material.SteelTarget ? layer.Material.DensityKgM3 : RhaDensity;
                float brinell = layer.Material.SteelTarget ? layer.Material.BrinellHardness : RhaBrinell;
                string label = "layer." + i + "." + layer.Material.Id;
                float limit = RodPerforationLimit(rod, current.LengthM, current.DiameterM, current.Speed, obliquity,
                    density, brinell, trace, label);
                var rng = BallisticSeed.Stream(seed, "rod#" + i);
                float z = Math.Clamp(rng.NextGaussian(), -KineticBulletModel.MaxScatterSigmas, KineticBulletModel.MaxScatterSigmas);
                float sampled = MathF.Max(0f, limit * (1f + rod.LongRod.Scatter * z));
                var step = new VehicleLayerStep
                {
                    MaterialId = layer.Material.Id, LosThicknessM = los, RhaEquivalentM = equivalent,
                    Capability = sampled, Perforated = sampled > equivalent
                };
                result.Steps.Add(step);
                trace?.Value(label + ".losMm", los * 1000f, EvidenceGrade.Physical);
                trace?.Value(label + ".rhaEquivalentMm", equivalent * 1000f, layer.Material.SteelTarget ? EvidenceGrade.Physical : EvidenceGrade.Game);
                trace?.Value(label + ".perforationLimitMm", limit * 1000f, EvidenceGrade.Experimental);
                if (!step.Perforated)
                {
                    current.LengthM = 0f;
                    current.MassKg = 0f;
                    current.Velocity = Vector3.Zero;
                    current.Condition = PenetratorCondition.Spent;
                    result.Result = i == 0 ? ImpactResult.Stopped : ImpactResult.PartialPenetration;
                    result.StoppingLayer = i;
                    result.Out = current;
                    return;
                }
                // Steady erosion: the rod spends length in proportion to the share of its limit it used, at
                // (nearly) constant speed. [physical approximation]
                float remaining = 1f - equivalent / sampled;
                current.MassKg *= remaining;
                current.LengthM *= remaining;
                current.Condition = PenetratorCondition.Eroded;
            }
            result.Out = current;
            result.Result = initialLength > 0f && current.LengthM / initialLength >= OvermatchShare
                ? ImpactResult.Overmatch : ImpactResult.Perforation;
            trace?.Value("residualLengthMm", current.LengthM * 1000f, EvidenceGrade.Experimental);
        }

        private static void ResolveDeMarre(VehicleKineticRound round, PenetratorState current,
            IReadOnlyList<VehicleArmorLayer> layers, float obliquity, Vector3 normal, ulong seed,
            VehicleKineticResult result, ImpactTrace trace)
        {
            result.RicochetProbability = RicochetProbability(obliquity, round.RicochetCriticalDegrees, round.RicochetWidthDegrees);
            var ricochetRng = BallisticSeed.Stream(seed, "apRicochet");
            if (result.RicochetProbability > 0f && ricochetRng.NextFloat() < result.RicochetProbability)
            {
                Ricochet(current, normal, 0.8f, 0.1f, result);
                trace?.Value("ricochetProbability", result.RicochetProbability, EvidenceGrade.Game);
                return;
            }
            // De Marre describes a whole plate (t^0.7 is not additive), so the stack is summed as RHA
            // equivalent and resolved once; the stopping layer is where the shot's reach runs out.
            float initialSpeed = current.Speed;
            float total = 0f;
            var cumulative = new float[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                VehicleArmorLayer layer = layers[i];
                float los = BallisticGeometry.LineOfSightThickness(layer.ThicknessM, obliquity);
                float equivalent = layer.Material.RhaEquivalent(los);
                total += equivalent;
                cumulative[i] = total;
                result.Steps.Add(new VehicleLayerStep
                {
                    MaterialId = layer.Material.Id, LosThicknessM = los, RhaEquivalentM = equivalent
                });
                trace?.Value("layer." + i + "." + layer.Material.Id + ".rhaEquivalentMm", equivalent * 1000f,
                    layer.Material.SteelTarget ? EvidenceGrade.Physical : EvidenceGrade.Game);
            }
            var rng = BallisticSeed.Stream(seed, "apshot");
            float z = Math.Clamp(rng.NextGaussian(), -KineticBulletModel.MaxScatterSigmas, KineticBulletModel.MaxScatterSigmas);
            float limit = DeMarreLimit(DeMarreRha, current.DiameterM, total, current.MassKg);
            float sampled = MathF.Max(0f, limit * (1f + DeMarreScatter * z));
            trace?.Value("stackRhaEquivalentMm", total * 1000f, EvidenceGrade.Game);
            trace?.Value("deMarreLimit", limit, EvidenceGrade.Game);
            trace?.Value("vIn", initialSpeed, EvidenceGrade.Physical);
            bool perforated = initialSpeed > sampled;
            // Reach: the RHA thickness whose (sampled) limit equals the impact speed.
            float reach = sampled > 0f ? total * MathF.Pow(initialSpeed / sampled, 1f / 0.7f) : float.MaxValue;
            for (int i = 0; i < result.Steps.Count; i++)
            {
                VehicleLayerStep step = result.Steps[i];
                step.Capability = reach;
                step.Perforated = perforated || cumulative[i] < reach;
                result.Steps[i] = step;
            }
            if (!perforated)
            {
                int stop = result.Steps.FindIndex(s => !s.Perforated);
                current.Velocity = Vector3.Zero;
                current.Condition = PenetratorCondition.Spent;
                result.StoppingLayer = stop < 0 ? layers.Count - 1 : stop;
                result.Result = result.StoppingLayer == 0 ? ImpactResult.Stopped : ImpactResult.PartialPenetration;
                result.Out = current;
                return;
            }
            // Lambert-Jonas with p = 2. [physical approximation]
            current.Velocity = current.Direction * MathF.Sqrt(initialSpeed * initialSpeed - sampled * sampled);
            result.Out = current;
            result.Result = current.Speed / initialSpeed >= MathF.Sqrt(OvermatchShare)
                ? ImpactResult.Overmatch : ImpactResult.Perforation;
        }

        private static void ResolveSmallArms(VehicleKineticRound round, PenetratorState current,
            IReadOnlyList<VehicleArmorLayer> layers, float obliquity, Vector3 normal, ulong seed,
            VehicleKineticResult result, ImpactTrace trace)
        {
            KineticMaterial face = layers[0].Material.SmallArms;
            // Tier T2 early exit: below 2.5 sigma of the first layer's limit the stop is certain.
            float faceDensity = KineticBulletModel.LosArealDensity(face, layers[0].ThicknessM, obliquity, 1f);
            float faceLimit = KineticBulletModel.LimitVelocity(face, round.SmallArmsClass, faceDensity, current.MassKg, current.DiameterM);
            if (current.Speed <= faceLimit * (1f - face.Scatter * KineticBulletModel.MaxScatterSigmas))
            {
                result.EarlyStop = true;
                result.Result = ImpactResult.Stopped;
                result.StoppingLayer = 0;
                current.Velocity = Vector3.Zero;
                current.Condition = PenetratorCondition.Spent;
                result.Out = current;
                result.Steps.Add(new VehicleLayerStep
                {
                    MaterialId = face.Id, LosThicknessM = BallisticGeometry.LineOfSightThickness(layers[0].ThicknessM, obliquity),
                    RhaEquivalentM = 0f, Capability = faceLimit, Perforated = false
                });
                trace?.Value("smallArms.faceV50", faceLimit, EvidenceGrade.Game);
                trace?.Note("T2 early stop: below the certain-stop limit of the strike face");
                return;
            }
            var ricochetRng = BallisticSeed.Stream(seed, "smallArmsRicochet");
            if (KineticBulletModel.TryRicochet(current, normal, face, ref ricochetRng, out PenetratorState glanced,
                    out _, out float probability))
            {
                result.RicochetProbability = probability;
                result.Result = ImpactResult.Ricochet;
                result.Out = glanced;
                return;
            }
            result.RicochetProbability = probability;
            for (int i = 0; i < layers.Count; i++)
            {
                VehicleArmorLayer layer = layers[i];
                var rng = BallisticSeed.Stream(seed, "smallArms#" + i);
                KineticLayerResult step = KineticBulletModel.ResolveLayer(current, round.SmallArmsClass, layer.Material.SmallArms,
                    layer.ThicknessM, obliquity, 1f, ref rng);
                result.Steps.Add(new VehicleLayerStep
                {
                    MaterialId = layer.Material.Id, LosThicknessM = BallisticGeometry.LineOfSightThickness(layer.ThicknessM, obliquity),
                    RhaEquivalentM = 0f, Capability = step.SampledLimitVelocity, Perforated = step.Perforated
                });
                trace?.Value("layer." + i + "." + layer.Material.Id + ".v50", step.LimitVelocity, EvidenceGrade.Game);
                current = step.Out;
                if (!step.Perforated)
                {
                    result.Result = i == 0 ? ImpactResult.Stopped : ImpactResult.PartialPenetration;
                    result.StoppingLayer = i;
                    result.Out = current;
                    return;
                }
            }
            result.Out = current;
            result.Result = ImpactResult.Perforation;
        }

        private static void Ricochet(PenetratorState current, Vector3 normal, float tangentialRetention,
            float normalRestitution, VehicleKineticResult result)
        {
            Vector3 n = Vector3.Normalize(normal);
            Vector3 normalPart = Vector3.Dot(current.Velocity, n) * n;
            Vector3 tangent = current.Velocity - normalPart;
            current.Velocity = tangent * tangentialRetention - normalPart * normalRestitution;
            current.Condition = PenetratorCondition.Deformed;
            result.Result = ImpactResult.Ricochet;
            result.Out = current;
        }

        /// <summary>
        /// Damage share handed to the vehicle component after armour. [game] sqrt(E_out / E_in) as for pawns, so
        /// an unarmoured hit keeps the round's balance damage. Stopped rounds still mark the plate.
        /// </summary>
        public static float DamageFactor(VehicleKineticResult result, float surfaceShare)
        {
            if (result.Perforated) return KineticBulletModel.WoundFactor(result.EnergyIn, result.EnergyOut);
            return Math.Clamp(surfaceShare, 0f, 1f);
        }
    }
}
