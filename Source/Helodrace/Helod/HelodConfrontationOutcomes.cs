using System;

namespace Helodrace
{
    public enum HelodConfrontationOutcome { Reconcile, Disperse, Fight }

    // Kept independent of the engine so balance can be exercised deterministically.
    public struct HelodConfrontationWeights
    {
        public float reconcile;
        public float disperse;
        public float fight;

        public HelodConfrontationOutcome Choose(float roll)
        {
            float value = Clamp01(roll) * (reconcile + disperse + fight);
            if (value < reconcile) return HelodConfrontationOutcome.Reconcile;
            if (value < reconcile + disperse) return HelodConfrontationOutcome.Disperse;
            return HelodConfrontationOutcome.Fight;
        }

        internal static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }

    public static class HelodConfrontationOutcomes
    {
        public static HelodConfrontationWeights Calculate(float firstMood, float secondMood,
            float firstOpinion, float secondOpinion, float mediation, float aggression)
        {
            float calm = HelodConfrontationWeights.Clamp01((firstMood + secondMood) * 0.25f
                + Math.Min(firstMood, secondMood) * 0.5f);
            float goodwill = HelodConfrontationWeights.Clamp01((firstOpinion + secondOpinion + 200f) / 400f);
            mediation = HelodConfrontationWeights.Clamp01(mediation);
            aggression = Math.Max(0.25f, Math.Min(3f, aggression));
            return new HelodConfrontationWeights
            {
                reconcile = (0.1f + calm * 0.7f + goodwill * 0.5f + mediation * 1.5f) / aggression,
                disperse = 0.65f + calm * 0.2f + mediation * 0.5f,
                fight = (0.15f + (1f - calm) * 1.2f + (1f - goodwill) * 0.7f)
                    * aggression * (1f - mediation * 0.75f)
            };
        }
    }
}
