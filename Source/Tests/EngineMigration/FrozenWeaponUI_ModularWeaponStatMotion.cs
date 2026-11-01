// Frozen from 586d610f before owned weapon editor geometry conversion.
using Helodrace.ModernWar;
using System;

namespace Helodrace.Tests.FrozenWeaponUI
{
    // Exact critically damped easing, driven by real time rather than game ticks.
    // Retargeting preserves both the displayed value and its current speed.
    internal sealed class ModularWeaponStatMotion
    {
        private bool initialized;
        private double value, velocity, changedAt;
        public double Target { get; private set; }
        public double PreviousTarget { get; private set; }
        public double ChangedAt => changedAt;

        public void Retarget(double target, double now)
        {
            if (!initialized)
            {
                initialized = true;
                value = Target = PreviousTarget = target;
                changedAt = now - 10;
                return;
            }
            if (Math.Abs(target - Target) < 0.000001) return;
            Sample(now, out value, out velocity);
            PreviousTarget = Target;
            Target = target;
            changedAt = now;
        }

        public double ValueAt(double now)
        {
            double current, speed;
            Sample(now, out current, out speed);
            return current;
        }

        private void Sample(double now, out double current, out double speed)
        {
            const double frequency = 14;
            double time = Math.Max(0, now - changedAt);
            double offset = value - Target;
            double c = velocity + frequency * offset;
            double decay = Math.Exp(-frequency * time);
            current = Target + (offset + c * time) * decay;
            speed = (velocity - frequency * c * time) * decay;
            if (Math.Abs(current - Target) < 0.00001 && Math.Abs(speed) < 0.00001)
            {
                current = Target;
                speed = 0;
            }
        }
    }
}
