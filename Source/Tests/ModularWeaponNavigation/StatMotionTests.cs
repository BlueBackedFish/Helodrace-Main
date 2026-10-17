using System;
using Helodrace.ModernWar;

internal static class StatMotionTests
{
    public static void Run()
    {
        var motion = new ModularWeaponStatMotion();
        motion.Retarget(10, 100);
        Near(motion.ValueAt(100), 10, 0.000001, "Initial values must appear immediately.");
        motion.Retarget(20, 101);
        Near(motion.ValueAt(101), 10, 0.000001, "Changing targets must not jump the displayed stat.");
        double prior = 10;
        for (int frame = 1; frame <= 120; frame++)
        {
            double current = motion.ValueAt(101 + frame / 120.0);
            if (current < prior || current > 20.000001) throw new Exception("A settled step must ease monotonically without overshooting.");
            prior = current;
        }
        Near(prior, 20, 0.001, "The bar must settle without relying on game ticks.");
        motion.Retarget(40, 103);
        const double time = 103.11, step = 0.000001;
        double value = motion.ValueAt(time);
        double speedBefore = (value - motion.ValueAt(time - step)) / step;
        motion.Retarget(5, time);
        Near(motion.ValueAt(time), value, 0.000001, "Rapid swaps must preserve the current bar position.");
        double speedAfter = (motion.ValueAt(time + step) - value) / step;
        Near(speedAfter, speedBefore, 0.02, "Rapid swaps must preserve easing speed.");
        Near(motion.ValueAt(106), 5, 0.00001, "A reversed target must eventually settle.");
        var other = new ModularWeaponStatMotion();
        other.Retarget(10, 100);
        other.Retarget(20, 101);
        for (int i = 0; i < 120; i++) other.Retarget(20, 101 + i / 60.0);
        Near(other.ValueAt(102), prior, 0.000001, "Repeated GUI events must not restart unchanged transitions.");
        Console.WriteLine("PASS: real-time stat easing, no jumps, monotonic steps, rapid retarget speed continuity and unchanged-target stability.");
    }

    private static void Near(double actual, double expected, double epsilon, string message)
    { if (Math.Abs(actual - expected) > epsilon) throw new Exception(message); }
}
