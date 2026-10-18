using System.Collections.Generic;
using UnityEngine;

namespace Helodrace.ModernWar
{
    // One complete sight line, not one bonus per optic component. Multiple lines
    // contribute their best efficiency per distance; they never multiply together.
    public sealed class ModularWeaponSightPerformance
    {
        public float Warmup = 1.12f;
        public float Touch = 0.80f;
        public float Short = 0.75f;
        public float Medium = 0.62f;
        public float Long = 0.45f;
        public float Range = 0.75f;

        public float AtDistance(float cells)
        {
            if (cells <= 3f) return Touch;
            if (cells <= 12f) return Mathf.Lerp(Touch, Short, (cells - 3f) / 9f);
            if (cells <= 25f) return Mathf.Lerp(Short, Medium, (cells - 12f) / 13f);
            return Mathf.Lerp(Medium, Long, (cells - 25f) / 15f);
        }

        public static float WarmupAtDistance(IReadOnlyList<ModularSightGroupStatus> groups, float cells)
        {
            float best = -1f, cost = 1.12f;
            foreach (var group in groups)
            {
                if (group.isSecondaryWeapon) continue;
                var profile = group.performance;
                if (profile == null) continue;
                float efficiency = profile.AtDistance(cells);
                if (efficiency > best + 0.0001f
                    || (Mathf.Approximately(efficiency, best) && profile.Warmup < cost))
                { best = efficiency; cost = profile.Warmup; }
            }
            return cost;
        }

        public static ModularWeaponSightPerformance For(
            ModularSightKind kind, bool magnified, float efficiency)
        {
            float warmup, touch, near, medium, far;
            if (magnified)
            {
                warmup = 1.12f; touch = 0.85f; near = 0.91f; medium = 0.96f; far = 0.96f;
            }
            else switch (kind)
            {
                case ModularSightKind.Reflex:
                    warmup = 0.94f; touch = 0.99f; near = 0.99f; medium = 0.91f; far = 0.76f; break;
                case ModularSightKind.Holographic:
                    warmup = 0.96f; touch = 0.98f; near = 0.99f; medium = 0.93f; far = 0.79f; break;
                case ModularSightKind.Scope:
                    warmup = 1.20f; touch = 0.78f; near = 0.86f; medium = 0.97f; far = 0.98f; break;
                case ModularSightKind.LowPowerVariableOptic:
                    // Until a selectable zoom state exists, use a conservative
                    // magnified profile, not all zoom-state advantages at once.
                    warmup = 1.10f; touch = 0.86f; near = 0.93f; medium = 0.96f; far = 0.96f; break;
                case ModularSightKind.FireControl:
                    warmup = 1.04f; touch = 0.95f; near = 0.97f; medium = 0.97f; far = 0.98f; break;
                default:
                    warmup = 1.02f; touch = 0.98f; near = 0.97f; medium = 0.93f; far = 0.85f; break;
            }
            // A blocked/poorly placed line loses its advantages. Its inherent
            // close-range and acquisition costs cannot disappear through occlusion.
            float weight = Mathf.Clamp01(efficiency);
            float range = magnified ? 1.06f : kind == ModularSightKind.Scope ? 1.08f
                : kind == ModularSightKind.LowPowerVariableOptic ? 1.10f
                : kind == ModularSightKind.FireControl ? 1.12f
                : kind == ModularSightKind.Reflex ? 0.97f
                : kind == ModularSightKind.Holographic ? 0.98f : 1f;
            return new ModularWeaponSightPerformance
            {
                Warmup = Mathf.Lerp(Mathf.Max(1.12f, warmup), warmup, weight),
                Touch = Mathf.Lerp(Mathf.Min(0.80f, touch), touch, weight),
                Short = Mathf.Lerp(Mathf.Min(0.75f, near), near, weight),
                Medium = Mathf.Lerp(Mathf.Min(0.62f, medium), medium, weight),
                Long = Mathf.Lerp(Mathf.Min(0.45f, far), far, weight),
                Range = Mathf.Lerp(0.75f, range, weight)
            };
        }

        internal void TakeBest(ModularWeaponSightPerformance other)
        {
            Touch = Mathf.Max(Touch, other.Touch);
            Short = Mathf.Max(Short, other.Short);
            Medium = Mathf.Max(Medium, other.Medium);
            Long = Mathf.Max(Long, other.Long);
            Range = Mathf.Max(Range, other.Range);
        }
    }
}
