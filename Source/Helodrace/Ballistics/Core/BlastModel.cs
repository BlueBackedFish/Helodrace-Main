using System;
using System.Collections.Generic;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// Blast and warhead fragments (design doc 5.3, 5.4, 7.3). Kept apart from the penetration models.
    /// </summary>
    public static class BlastModel
    {
        /// <summary>Energy of 1 kg of TNT equivalent by definition, J. [physical]</summary>
        public const float TntEnergyJPerKg = 4.184e6f;
        public const float AirGamma = 1.4f;
        public const float SoundSpeedMs = 340f;
        public const float AmbientKPa = 101.325f;
        private const float PsiToKPa = 6.89476f;
        /// <summary>m/kg^(1/3) to ft/lb^(1/3).</summary>
        private const float ScaledSiToEnglish = 3.28084f / 1.30060f;

        /// <summary>
        /// Incident peak overpressure of a hemispherical TNT surface burst, kPa. [experimental] Kingery-Bulmash fit
        /// as simplified by Swisdak (1994, NSWC-TR-5081, "Simplified Kingery Airblast Calculations"), coefficients as
        /// reproduced in arXiv:2511.16816 Table S7. The fit is ln P(psi) = A + B lnZ + C lnZ^2 + D lnZ^3 + E lnZ^4 with
        /// Z in ft/lb^(1/3): the reproduction writes log10, but only natural logs make the three regimes meet at their
        /// boundaries. Z outside 0.5-500 ft/lb^(1/3) is clamped and reported.
        /// </summary>
        public static float IncidentOverpressureKPa(float rangeM, float tntKg, out bool clamped)
        {
            clamped = false;
            if (tntKg <= 0f) return 0f;
            float z = MathF.Max(1e-3f, rangeM) / MathF.Cbrt(tntKg) * ScaledSiToEnglish;
            if (z < 0.5f) { z = 0.5f; clamped = true; }
            if (z > 500f) { z = 500f; clamped = true; }
            float l = MathF.Log(z);
            double lnP;
            if (z < 7.25f) lnP = 6.914 - 1.439 * l - 0.282 * l * l - 0.142 * l * l * l + 0.069 * l * l * l * l;
            else if (z < 60f) lnP = 8.831 - 3.700 * l + 0.271 * l * l + 0.073 * l * l * l - 0.013 * l * l * l * l;
            else lnP = 5.424 - 1.407 * l;
            return (float)Math.Exp(lnP) * PsiToKPa;
        }

        public static float ScaledDistanceSi(float rangeM, float tntKg) => tntKg <= 0f ? float.MaxValue : rangeM / MathF.Cbrt(tntKg);

        /// <summary>
        /// Quasi-static gas overpressure after a detonation inside a closed volume, kPa: the blast energy heating the
        /// air, P = (gamma - 1) E W / V [physical approximation; afterburning and heat loss ignored]. The UFC 3-340-02
        /// chart is not wired in.
        /// </summary>
        public static float QuasiStaticPressureKPa(float tntKg, float volumeM3) =>
            volumeM3 <= 0f ? 0f : (AirGamma - 1f) * TntEnergyJPerKg * tntKg / volumeM3 * 0.001f;

        /// <summary>
        /// Blow-down time constant of a vented volume, s: tau = V / (Cd A c) [physical approximation, Cd = 0.6].
        /// </summary>
        public static float VentTimeConstantS(float volumeM3, float ventAreaM2) =>
            ventAreaM2 <= 0f ? float.MaxValue : volumeM3 / (0.6f * ventAreaM2 * SoundSpeedMs);

        /// <summary>
        /// Share of the full quasi-static load a person takes, by how long the pressure lasts: 1 - exp(-tau / 50 ms).
        /// [game] A sealed volume keeps its pressure; a large opening vents it before it does its harm.
        /// </summary>
        public static float VentedLoadShare(float tauS) => tauS >= 1e6f ? 1f : 1f - MathF.Exp(-tauS / 0.05f);

        /// <summary>Reflection near walls inside a compartment multiplies the first shock. [game]</summary>
        public const float InteriorReflection = 2f;

        /// <summary>
        /// Game damage of a blast load on a person: base damage x (pressure / 200 kPa), capped at 3x. [game]
        /// Anchored so a 200 kPa load does the warhead's balance damage.
        /// </summary>
        public static float CrewBlastDamage(float baseDamage, float pressureKPa) =>
            baseDamage * Math.Clamp(pressureKPa / 200f, 0f, 3f);

        /// <summary>
        /// Gurney initial fragment speed of a cylindrical charge, m/s: v = sqrt(2E) (M/C + 1/2)^-1/2.
        /// [physical approximation] sqrt(2E) = 2440 m/s for TNT is the widely quoted value; no primary source is wired
        /// in, so it is a game value here. [game]
        /// </summary>
        public const float TntGurneyMs = 2440f;

        public static float GurneyCylinderSpeed(float caseKg, float chargeKg, float gurney = TntGurneyMs) =>
            chargeKg <= 0f ? 0f : gurney / MathF.Sqrt(caseKg / chargeKg + 0.5f);

        /// <summary>
        /// Mott distribution: share of fragments heavier than m is exp(-sqrt(m / mu)) [experimental form]; mean mass
        /// 2 mu. Samples one fragment mass by inversion. mu is a game value per warhead. [game]
        /// </summary>
        public static float MottSample(float mu, float u) => mu * MathF.Pow(-MathF.Log(MathF.Max(1e-6f, 1f - u)), 2f);

        public static int MottCount(float caseKg, float mu) => mu <= 0f ? 0 : Math.Max(1, (int)MathF.Round(caseKg / (2f * mu)));

        /// <summary>
        /// HESH scab: a squash-head charge on a steel face throws a scab off the inner face when the plate is thinner
        /// than about 1.3 shell calibres and has no spaced layer [physical mechanism, game threshold and scab values].
        /// </summary>
        public static bool HeshScabs(float steelFaceThicknessM, float calibreM, bool spacedOrLined) =>
            !spacedOrLined && calibreM > 0f && steelFaceThicknessM <= 1.3f * calibreM;

        /// <summary>Scab fragments from a HESH charge: 25 per kg of explosive, at 150 m/s. [game]</summary>
        public static int HeshScabFragments(float explosiveKg) => Math.Max(4, (int)MathF.Round(25f * explosiveKg));
        public const float HeshScabSpeedMs = 150f;
        public const float HeshScabMassKg = 0.02f;
    }
}
