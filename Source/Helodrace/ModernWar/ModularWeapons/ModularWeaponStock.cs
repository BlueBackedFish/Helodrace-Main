using System.Collections.Generic;
using UnityEngine;

namespace Helodrace.ModernWar
{
    // Physical values, independent of texture canvas, pivot and render animation.
    // Offsets are in the parent socket's authoring space, not metres.
    public sealed class ModularWeaponStockProperties
    {
        public bool adjustable;
        public float retractedOffset;
        public float extendedOffset;
        public float retractedSupport = 0.45f;
        public float extendedSupport = 0.95f;
        public float retractedLengthMeters = 0.16f;
        public float extendedLengthMeters = 0.25f;

        public float Extension(float offset)
        {
            if (!adjustable) return 1f;
            float travel = extendedOffset - retractedOffset;
            // Keep live authoring usable while the second endpoint is being set.
            return Mathf.Abs(travel) < 0.0001f ? 0f : Mathf.Clamp01((offset - retractedOffset) / travel);
        }
        public float Support(float offset) => Mathf.Lerp(retractedSupport, extendedSupport, Extension(offset));
        public float Length(float offset) => Mathf.Lerp(retractedLengthMeters, extendedLengthMeters, Extension(offset));
        public ModularWeaponStockProperties Clone() => (ModularWeaponStockProperties)MemberwiseClone();

        public IEnumerable<string> ConfigErrors()
        {
            foreach (var field in GetType().GetFields())
                if (field.FieldType == typeof(float))
                {
                    float value = (float)field.GetValue(this);
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        yield return "stock." + field.Name + " must be finite.";
                }
            if (adjustable && Mathf.Abs(extendedOffset - retractedOffset) < 0.0001f)
                yield return "An adjustable stock needs distinct extension endpoints.";
            if (retractedSupport < 0f || retractedSupport > 1f || extendedSupport < 0f || extendedSupport > 1f)
                yield return "Stock support must be within 0..1.";
            if (retractedLengthMeters <= 0f || extendedLengthMeters < retractedLengthMeters)
                yield return "Stock lengths must be positive and extended length must not be shorter.";
        }
    }

}
