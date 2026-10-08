using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Tactical
{
    // A visible, timed marker on the exact treated body part. Does not tend wounds.
    public sealed class Hediff_PartHemostasis : Hediff_TcccTimed
    {
        public float bleedingFactor = 1f;
        public override float PainOffset => PartHemostasis.PainOffset(pawn?.health?.hediffSet, this, Find.TickManager.TicksGame);
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

    public sealed class Hediff_SystemicHemostatic : Hediff_TcccTimed
    {
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) PartHemostasis.Invalidate(pawn?.health?.hediffSet);
        }
        public override void PostAdd(DamageInfo? dinfo)
        { base.PostAdd(dinfo); PartHemostasis.Invalidate(pawn?.health?.hediffSet); }
        public override void PostRemoved()
        { base.PostRemoved(); PartHemostasis.Invalidate(pawn?.health?.hediffSet); }
    }

    public static class PartHemostasis
    {
        public const string DressingDefName = "HD_FieldHemostasis";
        public const float DressingFactor = .20f;
        public const int DressingTicks = 12 * GenDate.TicksPerHour;
        public const float PainPerPart = .05f;
        public const int MaxPainfulParts = 4;
        private sealed class Cache
        {
            public Cache() { }
            public bool dirty = true;
            public int validUntil;
            public float systemicFactor;
            public readonly Dictionary<BodyPartRecord, float> factors = new Dictionary<BodyPartRecord, float>();
            public readonly HashSet<BodyPartRecord> painfulParts = new HashSet<BodyPartRecord>();
            public readonly HashSet<Hediff_PartHemostasis> painOwners = new HashSet<Hediff_PartHemostasis>();
        }

        public static Hediff_PartHemostasis ApplyDressing(Pawn patient, BodyPartRecord part)
            => Apply(patient, part, DressingDefName, DressingFactor, DressingTicks);
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
            if (set == null) return 1f;
            Cache cache = GetCache(set, tick);
            float local = part != null && cache.factors.TryGetValue(part, out float result) ? result : 1f;
            return local * cache.systemicFactor;
        }

        public static float PainOffset(HediffSet set, Hediff_PartHemostasis marker, int tick)
            => set != null && GetCache(set, tick).painOwners.Contains(marker) ? PainPerPart : 0f;

        private static Cache GetCache(HediffSet set, int tick)
        {
            Cache cache = caches.GetOrCreateValue(set);
            if (cache.dirty || tick >= cache.validUntil)
            {
                cache.factors.Clear();
                cache.painfulParts.Clear(); cache.painOwners.Clear(); cache.systemicFactor = 1f;
                cache.validUntil = int.MaxValue;
                foreach (Hediff hediff in set.hediffs)
                {
                    if (hediff is Hediff_SystemicHemostatic drug && drug.expiresTick > tick)
                    {
                        cache.systemicFactor = TcccRules.HemostaticBleedingFactor;
                        cache.validUntil = Math.Min(cache.validUntil, drug.expiresTick);
                        continue;
                    }
                    if (!(hediff is Hediff_PartHemostasis marker) || marker.Part == null || marker.expiresTick <= tick) continue;
                    cache.validUntil = Math.Min(cache.validUntil, marker.expiresTick);
                    float factor = Math.Max(0f, Math.Min(1f, marker.bleedingFactor));
                    if (!cache.factors.TryGetValue(marker.Part, out float previous) || factor < previous)
                        cache.factors[marker.Part] = factor;
                    if (marker.def?.defName == DressingDefName && cache.painfulParts.Count < MaxPainfulParts
                        && cache.painfulParts.Add(marker.Part)) cache.painOwners.Add(marker);
                }
                cache.dirty = false;
            }
            return cache;
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
            if (__result <= 0f) return;
            __result *= PartHemostasis.Factor(__instance.pawn?.health?.hediffSet,
                __instance.Part, Find.TickManager.TicksGame);
        }
    }
}
