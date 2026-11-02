using System;

namespace Helodrace;

public static class MechanicalPowerMath
{
    // P = torque * angular speed; RPM is revolutions per minute.
    public static float TorqueForPower(float powerWatts, float rpm) =>
        rpm > 0f ? MathF.Max(0f, powerWatts) * 60f / (MathF.Tau * rpm) : 0f;
}