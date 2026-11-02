using System;
using Helodrace;

void Near(float actual, float expected, float tolerance, string message)
{
    if (MathF.Abs(actual - expected) > tolerance) throw new Exception(message + ": " + actual + " != " + expected);
}
Near(MechanicalPowerMath.TorqueForPower(250, 250), 9.5492966f, .0001f, "250 W at 250 RPM uses actual N·m");
Near(MechanicalPowerMath.TorqueForPower(10000, 1200), 79.57747f, .0001f, "Generator rated shaft torque");
Near(MechanicalPowerMath.TorqueForPower(0, 100), 0, 0, "Zero power");
Near(MechanicalPowerMath.TorqueForPower(100, 0), 0, 0, "Stopped shaft has no rated torque calculation");
Near(MechanicalPowerMath.TorqueForPower(-100, 50), 0, 0, "Negative demand is rejected");
foreach (float rpm in new[] { 20f, 60f, 120f, 250f, 300f, 1200f })
    Near(MechanicalPowerMath.TorqueForPower(500, rpm) * (MathF.Tau * rpm / 60), 500, .001f, "Power is preserved through gearing");
Console.WriteLine("PASS: mechanical W, RPM and N·m dimensional checks");