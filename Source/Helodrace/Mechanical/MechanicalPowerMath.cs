using System;

namespace Helodrace;

public static class MechanicalPowerMath
{
    public static float TorqueForPower(float powerWatts, float rpm) =>
        rpm > 0f ? MathF.Max(0f, powerWatts) * 60f / (MathF.Tau * rpm) : 0f;

    public static float GeneratorEfficiency(float input, float ratedInput, float minimum, float peak)
    {
        if (ratedInput <= 0f || input <= 0f) return 0f;
        peak = Math.Clamp(peak, 0f, 1f);
        minimum = Math.Clamp(minimum, 0f, peak);
        return minimum + (peak - minimum) * Math.Clamp(input / ratedInput, 0f, 1f);
    }

    public static float ElectricalOutput(float input, float ratedInput, float minimum, float peak) =>
        Math.Clamp(input, 0f, MathF.Max(0f, ratedInput)) * GeneratorEfficiency(input, ratedInput, minimum, peak);

    public static float MechanicalInputFor(float target, float ratedInput, float minimum, float peak)
    {
        if (ratedInput <= 0f || !float.IsFinite(target)) return 0f;
        peak = Math.Clamp(peak, 0f, 1f);
        minimum = Math.Clamp(minimum, 0f, peak);
        target = Math.Clamp(target, 0f, ratedInput * peak);
        if (target <= 0f) return 0f;
        // Invert target = minimum * input + (peak - minimum) * input² / rated.
        // This form avoids cancellation for small targets.
        double a = (peak - minimum) / ratedInput;
        if (a <= 0d) return minimum > 0f ? target / minimum : 0f;
        return Math.Clamp((float)(2d * target / (minimum + Math.Sqrt(minimum * minimum + 4d * a * target))), 0f, ratedInput);
    }
}