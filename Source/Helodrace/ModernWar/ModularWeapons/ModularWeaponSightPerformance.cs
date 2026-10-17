using UnityEngine;

namespace Helodrace.ModernWar
{
    // One complete sight line, not one bonus per optic component. Multiple lines
    // contribute their best value per statistic; they never multiply together.
    public sealed class ModularWeaponSightPerformance
    {
        public float Warmup = 1.12f;
        public float Touch = 0.95f;
        public float Short = 0.90f;
        public float Medium = 0.82f;
        public float Long = 0.70f;

        public static ModularWeaponSightPerformance For(
            ModularSightKind kind, bool magnified, float efficiency)
        {
            float warmup, touch, near, medium, far;
            if (magnified)
            {
                warmup = 1.12f; touch = 0.88f; near = 0.95f; medium = 1.045f; far = 1.075f;
            }
            else switch (kind)
            {
                case ModularSightKind.Reflex:
                    warmup = 0.94f; touch = 1.02f; near = 1.025f; medium = 1f; far = 0.90f; break;
                case ModularSightKind.Holographic:
                    warmup = 0.96f; touch = 1f; near = 1.03f; medium = 1.015f; far = 0.92f; break;
                case ModularSightKind.Scope:
                    warmup = 1.20f; touch = 0.80f; near = 0.90f; medium = 1.055f; far = 1.10f; break;
                case ModularSightKind.LowPowerVariableOptic:
                case ModularSightKind.FireControl:
                    warmup = 1f; touch = 1f; near = 1.02f; medium = 1.04f; far = 1.08f; break;
                default:
                    warmup = 1.02f; touch = 0.99f; near = 1f; medium = 0.99f; far = 0.94f; break;
            }
            // A blocked/poorly placed line loses its advantages. Its inherent
            // close-range and acquisition costs cannot disappear through occlusion.
            float weight = Mathf.Clamp01(efficiency);
            return new ModularWeaponSightPerformance
            {
                Warmup = Mathf.Lerp(Mathf.Max(1.12f, warmup), warmup, weight),
                Touch = Mathf.Lerp(Mathf.Min(0.95f, touch), touch, weight),
                Short = Mathf.Lerp(Mathf.Min(0.90f, near), near, weight),
                Medium = Mathf.Lerp(Mathf.Min(0.82f, medium), medium, weight),
                Long = Mathf.Lerp(Mathf.Min(0.70f, far), far, weight)
            };
        }

        internal void TakeBest(ModularWeaponSightPerformance other)
        {
            Warmup = Mathf.Min(Warmup, other.Warmup);
            Touch = Mathf.Max(Touch, other.Touch);
            Short = Mathf.Max(Short, other.Short);
            Medium = Mathf.Max(Medium, other.Medium);
            Long = Mathf.Max(Long, other.Long);
        }
    }
}
