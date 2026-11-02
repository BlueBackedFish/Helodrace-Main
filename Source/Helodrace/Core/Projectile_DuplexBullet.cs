using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using NativeVector3 = System.Numerics.Vector3;

namespace Helodrace
{
    public sealed class DuplexProjectileExtension : DefModExtension
    {
        public float secondarySpreadDegrees = 1.2f;

        public override IEnumerable<string> ConfigErrors()
        {
            if (!(secondarySpreadDegrees >= 0f && secondarySpreadDegrees <= 10f))
                yield return "Duplex secondary spread must be between 0 and 10 degrees.";
        }
    }

    public static class DuplexProjectileRules
    {
        // Spread around the already resolved primary trajectory, never re-aim at
        // the intended pawn after the first bullet has missed or hit cover.
        public static NativeVector3 SecondaryEndpoint(NativeVector3 origin, NativeVector3 primary,
            float spreadDegrees, float sample)
        {
            NativeVector3 delta = primary - origin;
            float radians = Math.Max(0f, Math.Min(10f, spreadDegrees)) * (float)Math.PI / 180f;
            float offset = (float)Math.Tan(radians) * Math.Max(-1f, Math.Min(1f, sample));
            return primary + new NativeVector3(-delta.Z * offset, 0f, delta.X * offset);
        }
    }

    // One verb shot/ammunition debit, two independent ordinary ballistic bullets.
    // Both use native interception, cover, shields and the existing damage snapshot.
    public class Projectile_DuplexBullet : Projectile_BallisticBullet
    {
        private bool companionIssued;

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false,
            Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags,
                preventFriendlyFire, equipment, targetCoverDef);
            if (companionIssued) return;
            companionIssued = true;
            Projectile_DuplexBullet secondary = CreateCompanion();
            if (secondary == null) return;
            // Set before Launch, not after, so the companion cannot recursively split.
            secondary.companionIssued = true;
            secondary.damageDefOverride = damageDefOverride;
            secondary.extraDamages = extraDamages == null ? new List<ExtraDamage>() : new List<ExtraDamage>(extraDamages);
            float spread = def.GetModExtension<DuplexProjectileExtension>()?.secondarySpreadDegrees ?? 1.2f;
            Vector3 endpoint = DuplexProjectileRules.SecondaryEndpoint(origin.ToNative(), destination.ToNative(),
                spread, Rand.Range(-1f, 1f)).ToGame();
            if (Map != null && !endpoint.ToIntVec3().InBounds(Map)) endpoint = destination;
            LocalTargetInfo secondaryTarget = endpoint.ToIntVec3() == usedTarget.Cell
                ? usedTarget : new LocalTargetInfo(endpoint.ToIntVec3());
            secondary.Launch(launcher, origin, secondaryTarget, intendedTarget, hitFlags,
                preventFriendlyFire, equipment, targetCoverDef);
            // Preserve the primary shot's shared error plus the sampled divergence.
            // Native Launch's independent cell jitter must not add a second miss roll.
            secondary.destination = endpoint;
            secondary.ticksToImpact = Math.Max(1, (int)Math.Ceiling(secondary.StartingTicksToImpact));
            secondary.lifetime = secondary.ticksToImpact;
        }

        protected virtual Projectile_DuplexBullet CreateCompanion()
        {
            return Map == null || Destroyed ? null
                : (Projectile_DuplexBullet)GenSpawn.Spawn(def, Position, Map);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref companionIssued, "duplexCompanionIssued");
        }
    }
}
