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
            return childhood + "|" + adulthood;
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
                ThoughtDefExtension_HelodPersonalityDescriptions extension =
                    def.GetModExtension<ThoughtDefExtension_HelodPersonalityDescriptions>();
                if (extension?.descriptions == null || extension.descriptions.Count == 0 || pawn == null)
                {
                    return base.Description;
                }

                int index = HelodPersonalityUtility.GetCachedVariantIndex(pawn);
                if (index < 0)
                {
                    return base.Description;
                }

                index = Math.Min(index, extension.descriptions.Count - 1);
                string translationKey = def.defName + ".personalityDescriptions." + index;
                if (translationKey.CanTranslate())
                {
                    return translationKey.Translate(pawn.Named("PAWN")).Resolve();
                }

                return extension.descriptions[index].Formatted(pawn.Named("PAWN")).Resolve();
            }
        }
    }

    public static class HelodPersonalityUtility
    {
        private const float RandomJitter = 0.12f;

        public static int GetCachedVariantIndex(Pawn pawn)
        {
            if (pawn == null)
            {
                return -1;
            }

            CompHelodPersonalitySeed comp = pawn.GetComp<CompHelodPersonalitySeed>();
            return comp?.GetPersonalityDescriptionVariant(pawn) ?? CalculateVariantIndex(pawn, pawn.thingIDNumber);
        }

        public static int CalculateVariantIndex(Pawn pawn, int seed)
        {
            HelodPersonalityAxis childhoodAxis = DominantAxis(pawn?.story?.Childhood, seed, 0);
            HelodPersonalityAxis adulthoodAxis = DominantAxis(pawn?.story?.Adulthood, seed, 1);
            return ((int)childhoodAxis * 4) + (int)adulthoodAxis;
        }

        private static HelodPersonalityAxis DominantAxis(BackstoryDef backstory, int seed, int salt)
        {
            BackstoryPersonalityDirectionExtension extension =
                backstory?.GetModExtension<BackstoryPersonalityDirectionExtension>();
            if (extension == null)
            {
                return (HelodPersonalityAxis)(Math.Abs(seed + salt) % 4);
            }

            float assertive = extension.Assertive + Jitter(seed, salt, 0);
            float passive = extension.Passive + Jitter(seed, salt, 1);
            float cooperative = extension.Cooperative + Jitter(seed, salt, 2);
            float independent = extension.Independent + Jitter(seed, salt, 3);

            HelodPersonalityAxis axis = HelodPersonalityAxis.Assertive;
            float best = assertive;
            if (passive > best)
            {
                axis = HelodPersonalityAxis.Passive;
                best = passive;
            }
            if (cooperative > best)
            {
                axis = HelodPersonalityAxis.Cooperative;
                best = cooperative;
            }
            if (independent > best)
            {
                axis = HelodPersonalityAxis.Independent;
            }
            return axis;
        }

        private static float Jitter(int seed, int salt, int axis)
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
                return (normalized * 2f - 1f) * RandomJitter;
            }
        }
    }
}
