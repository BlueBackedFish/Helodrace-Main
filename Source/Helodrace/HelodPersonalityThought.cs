using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace
{
    public enum HelodPersonalityAxis
    {
        Assertive,
        Passive,
        Cooperative,
        Independent
    }

    public class CompProperties_HelodPersonalitySeed : CompProperties
    {
        public CompProperties_HelodPersonalitySeed()
        {
            compClass = typeof(CompHelodPersonalitySeed);
        }
    }

    public class CompHelodPersonalitySeed : ThingComp
    {
        private int cachedVariantIndex = -1;
        private int cachedSeed = 0;
        private string cachedBackstorySignature;

        public int GetPersonalityDescriptionVariant(Pawn pawn)
        {
            string signature = GetBackstorySignature(pawn);
            if (cachedVariantIndex < 0 || cachedBackstorySignature != signature)
            {
                cachedBackstorySignature = signature;
                cachedSeed = StableHash($"{pawn?.thingIDNumber ?? 0}:{signature}");
                cachedVariantIndex = HelodPersonalityUtility.CalculateVariantIndex(pawn, cachedSeed);
            }

            return cachedVariantIndex;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref cachedVariantIndex, "cachedVariantIndex", -1);
            Scribe_Values.Look(ref cachedSeed, "cachedSeed", 0);
            Scribe_Values.Look(ref cachedBackstorySignature, "cachedBackstorySignature");
        }

        private static string GetBackstorySignature(Pawn pawn)
        {
            string childhood = pawn?.story?.Childhood?.defName ?? "NoChildhood";
            string adulthood = pawn?.story?.Adulthood?.defName ?? "NoAdulthood";
            return "VectorSumTop2PTSD|" + childhood + "|" + adulthood;
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = 23;
                for (int i = 0; i < text.Length; i++)
                {
                    hash = hash * 31 + text[i];
                }
                return hash;
            }
        }
    }

    public class ThoughtDefExtension_HelodPersonalityDescriptions : DefModExtension
    {
        public List<string> descriptions;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (descriptions == null || descriptions.Count != 16)
            {
                yield return "descriptions must contain exactly 16 entries.";
            }
        }
    }

    public class Thought_Memory_HelodPersonalityDescription : Thought_Memory
    {
        public override string Description
        {
            get
            {
                return HelodPersonalityUtility.PersonalityDescription(def, pawn, base.Description);
            }
        }
    }

    public class Thought_Situational_HelodPersonalityDescription : Thought_Situational
    {
        public override string Description
        {
            get
            {
                return HelodPersonalityUtility.PersonalityDescription(def, pawn, base.Description);
            }
        }
    }

    public static class HelodPersonalityUtility
    {
        private const float RandomJitter = 0.12f;
        private const float PtsdJitter = 0.4f;
        private const float PtsdThreshold = 1.0f;

        public static int GetCachedVariantIndex(Pawn pawn)
        {
            if (pawn == null)
            {
                return -1;
            }

            CompHelodPersonalitySeed comp = pawn.GetComp<CompHelodPersonalitySeed>();
            return comp?.GetPersonalityDescriptionVariant(pawn) ?? CalculateVariantIndex(pawn, pawn.thingIDNumber);
        }

        public static string PersonalityDescription(ThoughtDef def, Pawn pawn, string fallback)
        {
            ThoughtDefExtension_HelodPersonalityDescriptions extension =
                def.GetModExtension<ThoughtDefExtension_HelodPersonalityDescriptions>();
            if (extension?.descriptions == null || extension.descriptions.Count == 0 || pawn == null)
            {
                return fallback;
            }

            int index = GetCachedVariantIndex(pawn);
            if (index < 0)
            {
                return fallback;
            }

            index = Math.Min(index, extension.descriptions.Count - 1);
            string translationKey = def.defName + ".personalityDescriptions." + index;
            if (translationKey.CanTranslate())
            {
                return translationKey.Translate(pawn.Named("PAWN")).Resolve();
            }

            return extension.descriptions[index].Formatted(pawn.Named("PAWN")).Resolve();
        }

        public static int CalculateVariantIndex(Pawn pawn, int seed)
        {
            float[] scores = new float[4];
            bool hasPersonalityVector = false;
            float ptsd = 0f;

            hasPersonalityVector |= AddBackstoryVector(scores, pawn?.story?.Childhood, ref ptsd);
            hasPersonalityVector |= AddBackstoryVector(scores, pawn?.story?.Adulthood, ref ptsd);

            if (!hasPersonalityVector)
            {
                HelodPersonalityAxis firstFallback = (HelodPersonalityAxis)(Math.Abs(seed) % 4);
                HelodPersonalityAxis secondFallback = (HelodPersonalityAxis)(Math.Abs(seed + 1) % 4);
                if (secondFallback == firstFallback)
                {
                    secondFallback = (HelodPersonalityAxis)(((int)secondFallback + 1) % 4);
                }
                return VariantIndexForAxes(firstFallback, secondFallback);
            }

            for (int i = 0; i < scores.Length; i++)
            {
                scores[i] += Jitter(seed, 0, i);
            }
            ptsd += Jitter(seed, 1, 4, PtsdJitter);

            HelodPersonalityAxis primaryAxis;
            HelodPersonalityAxis secondaryAxis;
            TopTwoAxes(scores, out primaryAxis, out secondaryAxis);
            if (ptsd >= PtsdThreshold)
            {
                return 12 + (int)primaryAxis;
            }
            return VariantIndexForAxes(primaryAxis, secondaryAxis);
        }

        private static int VariantIndexForAxes(HelodPersonalityAxis primaryAxis, HelodPersonalityAxis secondaryAxis)
        {
            int primary = (int)primaryAxis;
            int secondary = (int)secondaryAxis;
            if (secondary == primary)
            {
                secondary = (secondary + 1) % 4;
            }
            if (secondary > primary)
            {
                secondary--;
            }
            return primary * 3 + secondary;
        }

        private static bool AddBackstoryVector(float[] scores, BackstoryDef backstory, ref float ptsd)
        {
            BackstoryPersonalityDirectionExtension extension =
                backstory?.GetModExtension<BackstoryPersonalityDirectionExtension>();
            if (extension == null)
            {
                return false;
            }

            scores[(int)HelodPersonalityAxis.Assertive] += extension.Assertive;
            scores[(int)HelodPersonalityAxis.Passive] += extension.Passive;
            scores[(int)HelodPersonalityAxis.Cooperative] += extension.Cooperative;
            scores[(int)HelodPersonalityAxis.Independent] += extension.Independent;
            ptsd += extension.PTSD;
            return true;
        }

        private static void TopTwoAxes(float[] scores, out HelodPersonalityAxis primaryAxis, out HelodPersonalityAxis secondaryAxis)
        {
            primaryAxis = HelodPersonalityAxis.Assertive;
            secondaryAxis = HelodPersonalityAxis.Passive;

            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i] > scores[(int)primaryAxis])
                {
                    secondaryAxis = primaryAxis;
                    primaryAxis = (HelodPersonalityAxis)i;
                }
                else if (i != (int)primaryAxis && scores[i] > scores[(int)secondaryAxis])
                {
                    secondaryAxis = (HelodPersonalityAxis)i;
                }
            }

            if (secondaryAxis == primaryAxis)
            {
                secondaryAxis = (HelodPersonalityAxis)(((int)primaryAxis + 1) % 4);
            }

            for (int i = 0; i < scores.Length; i++)
            {
                if (i != (int)primaryAxis && scores[i] > scores[(int)secondaryAxis])
                {
                    secondaryAxis = (HelodPersonalityAxis)i;
                }
            }
        }

        private static float Jitter(int seed, int salt, int axis)
        {
            return Jitter(seed, salt, axis, RandomJitter);
        }

        private static float Jitter(int seed, int salt, int axis, float amount)
        {
            unchecked
            {
                int hash = seed;
                hash = hash * 397 ^ (salt + 17);
                hash = hash * 397 ^ (axis + 31);
                hash ^= hash >> 13;
                hash *= 1274126177;
                hash ^= hash >> 16;
                float normalized = (hash & 0x7fffffff) / (float)int.MaxValue;
                return (normalized * 2f - 1f) * amount;
            }
        }
    }
}
