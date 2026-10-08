using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace Helodrace.Tactical
{
    // A visible, timed marker on the exact treated body part. Does not tend wounds.
    public sealed class Hediff_PartHemostasis : Hediff_TcccTimed
    {
        public float bleedingFactor = 1f;
        public override string TipStringExtra => base.TipStringExtra + "\n"
            + "HD_Hemostasis_Reduction".Translate((1f - bleedingFactor).ToString("P0"));
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref bleedingFactor, "bleedingFactor", 1f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) PartHemostasis.Invalidate(pawn?.health?.hediffSet);
        }
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            PartHemostasis.Invalidate(pawn?.health?.hediffSet);
        }
        public override void PostRemoved()
        {
            base.PostRemoved();
            PartHemostasis.Invalidate(pawn?.health?.hediffSet);
        }
    }

    public static class PartHemostasis
    {
        private sealed class Cache
        {
            public Cache() { }
            public bool dirty = true;
            public int validUntil;
            public readonly Dictionary<BodyPartRecord, float> factors = new Dictionary<BodyPartRecord, float>();
        }
        // Weak keys: no global pawn list, idle tick, or retained world pawns.
        private static readonly ConditionalWeakTable<HediffSet, Cache> caches = new ConditionalWeakTable<HediffSet, Cache>();

        public static void Invalidate(HediffSet set)
        {
            if (set != null && caches.TryGetValue(set, out Cache cache)) cache.dirty = true;
        }

        public static Hediff_PartHemostasis Apply(Pawn patient, BodyPartRecord part,
            string effectName, float factor, int duration)
        {
            if (patient?.health?.hediffSet == null || part == null || duration <= 0) return null;
            HediffDef def = DefDatabase<HediffDef>.GetNamed(effectName);
            Hediff_PartHemostasis marker = null;
            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
                if (hediff.def == def && hediff.Part == part && hediff is Hediff_PartHemostasis found)
                { marker = found; break; }
            int expires = Find.TickManager.TicksGame + duration;
            factor = Math.Max(0f, Math.Min(1f, factor));
            if (marker == null)
            {
                marker = (Hediff_PartHemostasis)HediffMaker.MakeHediff(def, patient, part);
                marker.expiresTick = expires;
                marker.bleedingFactor = factor;
                patient.health.AddHediff(marker);
            }
            else
            {
                // An expired marker awaiting native removal must not carry old potency forward.
                marker.bleedingFactor = marker.expiresTick <= Find.TickManager.TicksGame
                    ? factor : Math.Min(marker.bleedingFactor, factor);
                marker.expiresTick = Math.Max(marker.expiresTick, expires);
            }
            Invalidate(patient.health.hediffSet);
            patient.health.hediffSet.DirtyCache();
            return marker;
        }

        public static List<BodyPartRecord> BleedingParts(Pawn patient)
        {
            var parts = new List<BodyPartRecord>();
            if (patient?.health?.hediffSet == null) return parts;
            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
                if (hediff.Part != null && hediff.BleedRate > 0f && !parts.Contains(hediff.Part))
                    parts.Add(hediff.Part);
            return parts;
        }

        public static float Factor(HediffSet set, BodyPartRecord part, int tick)
        {
            if (set == null || part == null) return 1f;
            Cache cache = caches.GetOrCreateValue(set);
            if (cache.dirty || tick >= cache.validUntil)
            {
                cache.factors.Clear();
                cache.validUntil = int.MaxValue;
                foreach (Hediff hediff in set.hediffs)
                {
                    if (!(hediff is Hediff_PartHemostasis marker) || marker.Part == null || marker.expiresTick <= tick) continue;
                    cache.validUntil = Math.Min(cache.validUntil, marker.expiresTick);
                    float factor = Math.Max(0f, Math.Min(1f, marker.bleedingFactor));
                    if (!cache.factors.TryGetValue(marker.Part, out float previous) || factor < previous)
                        cache.factors[marker.Part] = factor;
                }
                cache.dirty = false;
            }
            return cache.factors.TryGetValue(part, out float result) ? result : 1f;
        }
    }

    [HarmonyPatch]
    public static class Patch_PartHemostasis_Bleeding
    {
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            // Each vanilla implementation is independent; scaled/total bleeding already
            // calls these getters. Hooking totals as well would apply the effect twice.
            yield return AccessTools.DeclaredPropertyGetter(typeof(Hediff), nameof(Hediff.BleedRate));
            yield return AccessTools.DeclaredPropertyGetter(typeof(Hediff_Injury), nameof(Hediff.BleedRate));
            yield return AccessTools.DeclaredPropertyGetter(typeof(Hediff_MissingPart), nameof(Hediff.BleedRate));
        }
        public static void Postfix(Hediff __instance, ref float __result)
        {
            if (__result <= 0f || __instance.Part == null) return;
            __result *= PartHemostasis.Factor(__instance.pawn?.health?.hediffSet,
                __instance.Part, Find.TickManager.TicksGame);
        }
    }
}
