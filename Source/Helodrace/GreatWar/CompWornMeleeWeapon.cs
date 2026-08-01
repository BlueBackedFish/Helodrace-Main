using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    public class CompProperties_WornMeleeWeapon : CompProperties
    {
        public float minimumBestVerbWeightFactor = 1.15f;

        public CompProperties_WornMeleeWeapon()
        {
            compClass = typeof(CompWornMeleeWeapon);
        }
    }

    /// <summary>
    /// Owns the verbs generated from an apparel def's tools and makes their
    /// caster the pawn while the apparel is worn.
    /// </summary>
    public class CompWornMeleeWeapon : CompEquippable
    {
        public CompProperties_WornMeleeWeapon Props =>
            (CompProperties_WornMeleeWeapon)props;

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            SetCaster(pawn);
            pawn?.meleeVerbs?.Notify_PawnDespawned();
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            pawn?.meleeVerbs?.Notify_PawnDespawned();
            SetCaster(parent);
        }

        public void SetCaster(Thing caster)
        {
            foreach (Verb verb in AllVerbs)
            {
                verb.caster = caster;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.GetUpdatedAvailableVerbsList))]
    public static class Patch_PawnMeleeVerbs_WornMeleeWeapons
    {
        private static readonly FieldInfo CachedSelectionWeightField =
            AccessTools.Field(typeof(VerbEntry), "cachedSelectionWeight");

        public static void Postfix(Pawn_MeleeVerbs __instance, ref List<VerbEntry> __result)
        {
            Pawn pawn = __instance?.Pawn;
            if (pawn?.apparel?.WornApparel == null)
            {
                return;
            }

            Dictionary<Verb, float> wornVerbWeightFactors = new Dictionary<Verb, float>();
            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                CompWornMeleeWeapon comp = apparel.TryGetComp<CompWornMeleeWeapon>();
                if (comp == null)
                {
                    continue;
                }

                comp.SetCaster(pawn);
                foreach (Verb verb in comp.AllVerbs.Where(verb =>
                    verb != null && verb.IsMeleeAttack && verb.Available()))
                {
                    wornVerbWeightFactors[verb] = comp.Props.minimumBestVerbWeightFactor;
                }
            }

            if (wornVerbWeightFactors.Count == 0)
            {
                return;
            }

            List<Verb> allVerbs = (__result ?? new List<VerbEntry>())
                .Select(entry => entry.verb)
                .Where(verb => verb != null)
                .Concat(wornVerbWeightFactors.Keys)
                .Distinct()
                .ToList();

            float highestSelectionWeight = allVerbs.Max(verb =>
                verb.verbProps.AdjustedMeleeSelectionWeight(verb, pawn));

            __result = allVerbs
                .Select(verb => new VerbEntry(verb, pawn, allVerbs, highestSelectionWeight))
                .ToList();

            if (CachedSelectionWeightField == null)
            {
                return;
            }

            foreach (VerbEntry entry in __result)
            {
                if (!wornVerbWeightFactors.TryGetValue(entry.verb, out float factor))
                {
                    continue;
                }

                float currentWeight = (float)CachedSelectionWeightField.GetValue(entry);
                float minimumWeight = highestSelectionWeight * factor;
                if (currentWeight < minimumWeight)
                {
                    CachedSelectionWeightField.SetValue(entry, minimumWeight);
                }
            }
        }
    }
}
