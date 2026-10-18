using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Helodrace.ModernWar;

namespace Helodrace
{
    // Opt-in, game-balance values; projectile render speed is not muzzle velocity.
    public sealed class BulletFragmentationExtension : DefModExtension
    {
        public float chance = 0.7f;
        public float minimumVelocityFactor = 0.45f;
        public float fullVelocityFactor = 1f;
        public float armorResistanceFactor = 0.65f;
        public float damageFraction = 0.55f;
        public int minFragments = 2;
        public int maxFragments = 5;
        public float maxFragmentDamage = 5f;

        internal float Strength(float velocityFactor, float penetratedArmor)
        {
            float velocity = Math.Max(0f, velocityFactor)
                / (1f + Math.Max(0f, penetratedArmor) * Math.Max(0f, armorResistanceFactor));
            return Mathf.Clamp01((velocity - minimumVelocityFactor)
                / Math.Max(0.01f, fullVelocityFactor - minimumVelocityFactor));
        }
    }

    public sealed class Projectile_FragmentingBullet : Projectile_BallisticBullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            BulletFragmentationImpact previous = BulletFragmentationImpact.Current;
            BulletFragmentationExtension settings = def.GetModExtension<BulletFragmentationExtension>();
            BulletFragmentationImpact.Current = !blockedByShield && hitThing is Pawn pawn && settings != null
                ? new BulletFragmentationImpact
                {
                    pawn = pawn, settings = settings, damageDef = DamageDef,
                    velocityFactor = ImpactVelocityFactor
                } : null;
            try { base.Impact(hitThing, blockedByShield); }
            finally { BulletFragmentationImpact.Current = previous; }
        }
    }

    internal sealed class BulletFragmentationImpact
    {
        [ThreadStatic] internal static BulletFragmentationImpact Current;
        internal Pawn pawn;
        internal BulletFragmentationExtension settings;
        internal DamageDef damageDef;
        internal float velocityFactor = 1f, penetratedArmor;
        internal BodyPartRecord armorPart;
        internal bool resolved;

        internal static bool IsPresent(Pawn pawn, BodyPartRecord part)
        {
            if (part == null) return false;
            for (BodyPartRecord current = part; current != null; current = current.parent)
                if (pawn.health.hediffSet.PartIsMissing(current)) return false;
            return true;
        }

        internal static void RecordArmor(Pawn pawn, float armorRating, float remainingDamage, DamageDef damageDef)
        {
            BulletFragmentationImpact impact = Current;
            if (impact != null && impact.pawn == pawn && !impact.resolved && remainingDamage > 0f
                && damageDef == impact.damageDef)
                impact.penetratedArmor += Math.Max(0f, armorRating);
        }

        internal static List<BodyPartRecord> Neighbors(Pawn pawn, BodyPartRecord entry)
        {
            var candidates = new List<BodyPartRecord>();
            if (entry == null) return candidates;
            Add(entry.parent);
            foreach (BodyPartRecord child in entry.parts) Add(child);
            // Organs in the same cavity are close. External siblings can be
            // distant limbs, so do not jump from a hand to the opposite hand.
            if (entry.depth == BodyPartDepth.Inside && entry.parent != null)
                foreach (BodyPartRecord sibling in entry.parent.parts)
                    if (sibling.depth == BodyPartDepth.Inside && sibling != entry) Add(sibling);
            if (candidates.Count == 0) Add(entry);
            return candidates;

            void Add(BodyPartRecord part)
            {
                if (IsPresent(pawn, part) && !candidates.Contains(part)) candidates.Add(part);
            }
        }
    }

    [HarmonyPatch(typeof(ArmorUtility), nameof(ArmorUtility.GetPostArmorDamage))]
    public static class Patch_ArmorUtility_FragmentationEntry
    {
        public static void Prefix(Pawn pawn, BodyPartRecord part)
        {
            BulletFragmentationImpact impact = BulletFragmentationImpact.Current;
            if (impact?.pawn != pawn || impact.resolved) return;
            impact.armorPart = part;
            impact.penetratedArmor = 0f;
        }
    }

    [HarmonyPatch(typeof(ArmorUtility), "ApplyArmor")]
    public static class Patch_ArmorUtility_FragmentationResistance
    {
        public static void Postfix(Pawn pawn, Thing armorThing, float armorRating, float damAmount, DamageDef damageDef)
        {
            // Natural armor affects the primary wound normally, but is not clothing.
            if (armorThing is Apparel)
                BulletFragmentationImpact.RecordArmor(pawn, armorRating, damAmount, damageDef);
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "FinalizeAndAddInjury", new[]
    {
        typeof(Pawn), typeof(float), typeof(DamageInfo), typeof(DamageWorker.DamageResult)
    })]
    public static class Patch_DamageWorker_BulletFragmentation
    {
        private delegate float FinalizeInjury(DamageWorker_AddInjury worker, Pawn pawn, float damage,
            DamageInfo info, DamageWorker.DamageResult result);
        private static readonly FinalizeInjury apply = AccessTools.MethodDelegate<FinalizeInjury>(AccessTools.Method(
            typeof(DamageWorker_AddInjury), "FinalizeAndAddInjury", new[]
            { typeof(Pawn), typeof(float), typeof(DamageInfo), typeof(DamageWorker.DamageResult) }));

        internal static void Postfix(Pawn pawn, DamageInfo dinfo, DamageWorker.DamageResult result, float __result)
        {
            BulletFragmentationImpact impact = BulletFragmentationImpact.Current;
            if (impact?.pawn != pawn || impact.resolved || dinfo.Def != impact.damageDef
                || dinfo.Def.armorCategory != DamageArmorCategoryDefOf.Sharp
                || dinfo.InstantPermanentInjury || __result <= 0f || pawn.Dead || !pawn.RaceProps.IsFlesh
                || (!dinfo.IgnoreArmor && dinfo.HitPart != impact.armorPart)) return;
            impact.resolved = true; // Also guards every recursively finalized fragment.
            BulletFragmentationExtension settings = impact.settings;
            float strength = settings.Strength(impact.velocityFactor, impact.penetratedArmor);
            if (strength <= 0f || !Rand.Chance(Mathf.Clamp01(settings.chance) * strength)) return;
            List<BodyPartRecord> parts = BulletFragmentationImpact.Neighbors(pawn, dinfo.HitPart);
            if (parts.Count == 0) return;
            float budget = __result * Math.Max(0f, settings.damageFraction) * strength;
            int minimum = Mathf.Clamp(settings.minFragments, 1, 8);
            int maximum = Mathf.Clamp(settings.maxFragments, minimum, 8);
            int count = Math.Min(Rand.RangeInclusive(minimum, maximum), (int)(budget / 0.35f));
            if (count <= 0) return;
            float amount = Math.Min(budget / count, Math.Max(0.01f, settings.maxFragmentDamage));
            DamageDef fragment = DefDatabase<DamageDef>.GetNamed("HD_BulletFragment");
            int primaryPartIndex = (result.parts?.Count ?? 0) - 1;
            parts.Shuffle();
            for (int i = 0; i < count && !pawn.Dead; i++)
            {
                // Recheck after each wound: a parent may have been destroyed.
                parts.RemoveAll(part => !BulletFragmentationImpact.IsPresent(pawn, part));
                if (parts.Count == 0) break;
                DamageInfo inner = dinfo;
                inner.Def = fragment;
                inner.SetHitPart(parts[i % parts.Count]);
                inner.SetAmount(amount);
                inner.SetAllowDamagePropagation(false);
                // The fragments form inside the body. Armor and incoming damage
                // factors were already applied to the primary wound and budget.
                apply((DamageWorker_AddInjury)fragment.Worker, pawn, amount, inner, result);
            }
            // Native small-pawn propagation reads LastHitPart after this returns.
            // Keep its primary hit last, so fragments cannot redirect that pass.
            if (primaryPartIndex >= 0 && result.parts.Count > primaryPartIndex + 1)
            {
                BodyPartRecord primaryPart = result.parts[primaryPartIndex];
                result.parts.RemoveAt(primaryPartIndex);
                result.parts.Add(primaryPart);
            }
        }
    }
}
