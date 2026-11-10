using System;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// Phase 11 refinements (design doc 4.2, 3.2, 4.2 slat, 6.5 visor). No sourced quantitative data is wired in for
    /// any of them; every coefficient here is a game value [game] chosen from published trends only.
    /// </summary>
    public static class Normalization
    {
        /// <summary>
        /// Normalization toward the plate normal, degrees, for rounds that bite into an oblique face (blunt or capped
        /// cores). [game] Long rods get none: the Lanz-Odermatt fit already contains the obliquity behaviour (cos^m).
        /// </summary>
        public static float Degrees(PenetrationMechanism mechanism, PenetratorClass penetratorClass)
        {
            switch (mechanism)
            {
                // Blunt full-calibre steel shot normalizes most; sharp sub-calibre tungsten cores (APDS) little.
                case PenetrationMechanism.KineticHardCore: return penetratorClass == PenetratorClass.Tungsten ? 2f : 5f;
                case PenetrationMechanism.KineticLongRod: return 0f;
                default:
                    return penetratorClass == PenetratorClass.Soft ? 0f : penetratorClass == PenetratorClass.Tungsten ? 2f : 3f;
            }
        }

        /// <summary>Effective obliquity after normalization (never below 0). The LOS growth is the geometric one.</summary>
        public static float Apply(float obliquity, float normalizationDegrees) =>
            MathF.Max(0f, obliquity - normalizationDegrees * BallisticGeometry.DegToRad);
    }

    /// <summary>
    /// Long-rod yaw grown in an air gap between spaced layers, and its cost. [game]
    ///   yaw gain = 0.05 rad per (gap / rod length), capped at 0.35 rad
    ///   perforation-limit factor = 1 - min(0.5, yaw x (L/D) / 20)
    /// </summary>
    public static class RodYaw
    {
        public static float Grow(float yawRadians, float gapM, float rodLengthM) =>
            rodLengthM <= 0f ? yawRadians : MathF.Min(0.35f, yawRadians + 0.05f * gapM / rodLengthM);

        public static float LimitFactor(float yawRadians, float lengthM, float diameterM) =>
            diameterM <= 0f ? 1f : 1f - MathF.Min(0.5f, yawRadians * (lengthM / diameterM) / 20f);
    }

    public enum SlatOutcome
    {
        /// <summary>The round slips between the bars untouched.</summary>
        PassThrough,
        /// <summary>The bars short or crush the fuze: no jet.</summary>
        Dud,
        /// <summary>The warhead fires on the bars, at too long a stand-off.</summary>
        EarlyInitiation
    }

    /// <summary>
    /// Slat (bar) armour against shaped charges: a probabilistic result, as public trial reports describe it. [game]
    /// Kinetic rounds pass through. Pass 35 %, dud 50 %, early initiation 15 % (jet potential x 0.8).
    /// </summary>
    public static class SlatArmor
    {
        public const float PassThroughChance = 0.35f;
        public const float DudChance = 0.5f;
        public const float EarlyInitiationPotential = 0.8f;

        public static SlatOutcome Roll(float u) =>
            u < PassThroughChance ? SlatOutcome.PassThrough : u < PassThroughChance + DudChance ? SlatOutcome.Dud : SlatOutcome.EarlyInitiation;
    }

    /// <summary>
    /// Ceramic multi-hit damage on a plate face as a 3 x 3 grid (design doc 3.2). [game] A hit cracks its cell (0.5 when
    /// stopped, 0.8 when perforated) and its neighbours (0.15); the ceramic's effectiveness at a cell is
    /// 1 - 0.45 x crack(cell) - 0.1 x mean crack(neighbours). Calibrated to the NIJ multi-hit idea (shots spaced
    /// apart hold, shots on the same spot do not), not to test data.
    /// </summary>
    public static class CrackGrid
    {
        public const int Size = 3;
        public const int Cells = Size * Size;

        /// <summary>Cell of a hit: horizontal offset across the plate's arc and height within its coverage band.</summary>
        public static int CellOf(float azimuthOffsetDegrees, float halfArcDegrees, float height, float heightMin, float heightMax)
        {
            float u = halfArcDegrees > 0f ? (azimuthOffsetDegrees / halfArcDegrees + 1f) * 0.5f : 0.5f;
            float v = heightMax > heightMin ? (height - heightMin) / (heightMax - heightMin) : 0.5f;
            int col = Math.Clamp((int)(u * Size), 0, Size - 1);
            int row = Math.Clamp((int)(v * Size), 0, Size - 1);
            return row * Size + col;
        }

        public static float Effectiveness(float[] cracks, int cell)
        {
            if (cracks == null || cracks.Length != Cells || cell < 0) return 1f;
            float neighbours = 0f;
            int count = 0;
            foreach (int n in Neighbours(cell)) { neighbours += cracks[n]; count++; }
            float factor = 1f - 0.45f * cracks[cell] - 0.1f * (count > 0 ? neighbours / count : 0f);
            return Math.Clamp(factor, 0.1f, 1f);
        }

        public static void Record(float[] cracks, int cell, bool perforated)
        {
            if (cracks == null || cracks.Length != Cells || cell < 0) return;
            cracks[cell] = MathF.Min(1f, cracks[cell] + (perforated ? 0.8f : 0.5f));
            foreach (int n in Neighbours(cell)) cracks[n] = MathF.Min(1f, cracks[n] + 0.15f);
        }

        private static System.Collections.Generic.IEnumerable<int> Neighbours(int cell)
        {
            int row = cell / Size, col = cell % Size;
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int r = row + dr, c = col + dc;
                    if (r >= 0 && r < Size && c >= 0 && c < Size) yield return r * Size + c;
                }
        }
    }

    /// <summary>
    /// Transparent armour crazing (design doc 6.5): every hit the visor takes clouds it. [game] Crazing grows by
    /// 0.25 per stopped hit and 0.4 per perforation, up to 1; sight loss is 0.35 x crazing.
    /// </summary>
    public static class VisorCrazing
    {
        public static float Grow(float crazing, bool perforated) => MathF.Min(1f, crazing + (perforated ? 0.4f : 0.25f));
        public static float SightLoss(float crazing) => 0.35f * Math.Clamp(crazing, 0f, 1f);
    }
}
