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
Console.WriteLine("PASS: mechanical W, RPM and N·m dimensional checks");for (int target = 0; target <= 8000; target += 37)
{
    float input = MechanicalPowerMath.MechanicalInputFor(target, 10000, .6f, .8f);
    float output = MechanicalPowerMath.ElectricalOutput(input, 10000, .6f, .8f);
    Near(output, target, .002f, "Every requested output is met by the inverse efficiency curve");
    if (input < output || input > 10000) throw new Exception("Energy creation or rated input exceeded");
}
Near(MechanicalPowerMath.MechanicalInputFor(8000, 10000, .6f, .8f), 10000, .002f, "Full-load rated point");
Near(MechanicalPowerMath.MechanicalInputFor(9000, 10000, .6f, .8f), 10000, .002f, "Above-rating target is clamped");
Near(MechanicalPowerMath.MechanicalInputFor(400, 1000, .8f, .8f), 500, .001f, "Constant efficiency case");
Near(MechanicalPowerMath.MechanicalInputFor(100, 1000, 0, 0), 0, 0, "Zero efficiency cannot create output");
Near(MechanicalPowerMath.MechanicalInputFor(float.NaN, 1000, .6f, .8f), 0, 0, "NaN target is rejected");
Console.WriteLine("PASS: generator target inversion, energy conservation and rating bounds");