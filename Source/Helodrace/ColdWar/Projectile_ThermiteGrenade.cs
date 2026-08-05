using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class ThermiteGrenadeExtension : DefModExtension
    {
        public ThingDef emitterDef;
        public int reactionDelayTicks = 90;
        public int reactionDurationTicks = 420;
        public int damageIntervalTicks = 12;
        public int pawnDamageIntervalTicks = 30;
        public int visualIntervalTicks = 30;
        public int crossRadius = 1;
        public float damage = 18f;
        public float pawnDamage = 12f;
        public float armorPenetration = 0.75f;
        public float minimumFlammabilityDamageFactor = 0.30f;
        public float lowFlammabilityThreshold = 0.75f;
        public float fireSize = 1.15f;
    }

    public sealed class Projectile_ThermiteGrenade : Projectile_ModernGrenade
    {
        private bool thermiteEmitterSpawned;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref thermiteEmitterSpawned, "thermiteEmitterSpawned");
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            TrySpawnThermiteEmitter();
            base.Impact(hitThing, blockedByShield);
        }

        protected override void PrepareFuseExplosion()
        {
            TrySpawnThermiteEmitter();
            base.PrepareFuseExplosion();
        }

        private void TrySpawnThermiteEmitter()
        {
            ThermiteGrenadeExtension extension = def.GetModExtension<ThermiteGrenadeExtension>();
            if (!thermiteEmitterSpawned && extension?.emitterDef != null && Map != null)
            {
                thermiteEmitterSpawned = true;
                ThermiteGrenadeEmitter emitter = ThingMaker.MakeThing(extension.emitterDef)
                    as ThermiteGrenadeEmitter;
                if (emitter != null)
                {
                    emitter.Initialize(extension, Launcher);
                    GenSpawn.Spawn(emitter, Position, Map);
                }
            }
        }
    }

    public sealed class ThermiteGrenadeEmitter : Thing
    {
        private int delayTicks;
        private int remainingReactionTicks;
        private int damageIntervalTicks;
        private int pawnDamageIntervalTicks;
        private int visualIntervalTicks;
        private int ticksUntilDamage;
        private int ticksUntilPawnDamage;
        private int ticksUntilVisual;
        private int crossRadius;
        private float damage;
        private float pawnDamage;
        private float armorPenetration;
        private float minimumFlammabilityDamageFactor;
        private float lowFlammabilityThreshold;
        private float fireSize;
        private Thing instigator;

        public void Initialize(ThermiteGrenadeExtension extension, Thing source)
        {
            delayTicks = Mathf.Max(0, extension.reactionDelayTicks);
            remainingReactionTicks = Mathf.Max(1, extension.reactionDurationTicks);
            damageIntervalTicks = Mathf.Max(1, extension.damageIntervalTicks);
            pawnDamageIntervalTicks = Mathf.Max(1, extension.pawnDamageIntervalTicks);
            visualIntervalTicks = Mathf.Max(1, extension.visualIntervalTicks);
            ticksUntilDamage = 0;
            ticksUntilPawnDamage = 0;
            ticksUntilVisual = 0;
            crossRadius = Mathf.Max(0, extension.crossRadius);
            damage = Mathf.Max(0f, extension.damage);
            pawnDamage = Mathf.Max(0f, extension.pawnDamage);
            armorPenetration = Mathf.Max(0f, extension.armorPenetration);
            minimumFlammabilityDamageFactor = Mathf.Clamp01(extension.minimumFlammabilityDamageFactor);
            lowFlammabilityThreshold = Mathf.Clamp01(extension.lowFlammabilityThreshold);
            fireSize = Mathf.Max(0.1f, extension.fireSize);
            instigator = source;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref delayTicks, "delayTicks");
            Scribe_Values.Look(ref remainingReactionTicks, "remainingReactionTicks", 420);
            Scribe_Values.Look(ref damageIntervalTicks, "damageIntervalTicks", 12);
            Scribe_Values.Look(ref pawnDamageIntervalTicks, "pawnDamageIntervalTicks", 30);
            Scribe_Values.Look(ref visualIntervalTicks, "visualIntervalTicks", 30);
            Scribe_Values.Look(ref ticksUntilDamage, "ticksUntilDamage");
            Scribe_Values.Look(ref ticksUntilPawnDamage, "ticksUntilPawnDamage");
            Scribe_Values.Look(ref ticksUntilVisual, "ticksUntilVisual");
            Scribe_Values.Look(ref crossRadius, "crossRadius", 1);
            Scribe_Values.Look(ref damage, "damagePerPulse", 18f);
            Scribe_Values.Look(ref pawnDamage, "pawnDamagePerPulse", 12f);
            Scribe_Values.Look(ref armorPenetration, "armorPenetration", 0.75f);
            Scribe_Values.Look(ref minimumFlammabilityDamageFactor, "minimumFlammabilityDamageFactor", 0.30f);
            Scribe_Values.Look(ref lowFlammabilityThreshold, "lowFlammabilityThreshold", 0.75f);
            Scribe_Values.Look(ref fireSize, "fireSize", 1.15f);
            Scribe_References.Look(ref instigator, "instigator");
        }

        protected override void Tick()
        {
            base.Tick();
            if (delayTicks > 0)
            {
                delayTicks--;
                return;
            }

            if (remainingReactionTicks <= 0)
            {
                Destroy(DestroyMode.Vanish);
                return;
            }

            if (ticksUntilDamage <= 0)
            {
                ApplyThermiteDamage(pawnsOnly: false);
                ticksUntilDamage = damageIntervalTicks;
            }

            if (ticksUntilPawnDamage <= 0)
            {
                ApplyThermiteDamage(pawnsOnly: true);
                ticksUntilPawnDamage = pawnDamageIntervalTicks;
            }

            if (ticksUntilVisual <= 0)
            {
                EmitThermiteEffects();
                ticksUntilVisual = visualIntervalTicks;
            }

            ticksUntilDamage--;
            ticksUntilPawnDamage--;
            ticksUntilVisual--;
            remainingReactionTicks--;
        }

        private void ApplyThermiteDamage(bool pawnsOnly)
        {
            if (Map == null)
            {
                return;
            }

            HashSet<Thing> damagedThings = new HashSet<Thing>();
            foreach (IntVec3 cell in CrossCells(Position, crossRadius))
            {
                if (!cell.InBounds(Map))
                {
                    continue;
                }

                float distanceFactor = cell == Position ? 1.2f : 1f;
                List<Thing> things = new List<Thing>(cell.GetThingList(Map));
                for (int i = 0; i < things.Count; i++)
                {
                    Thing target = things[i];
                    bool targetIsPawn = target is Pawn;
                    if (target == this || target.Destroyed ||
                        (!targetIsPawn && target.def?.useHitPoints != true) ||
                        targetIsPawn != pawnsOnly ||
                        !damagedThings.Add(target))
                    {
                        continue;
                    }

                    float flammability = target.GetStatValue(StatDefOf.Flammability);
                    float flammabilityFactor = FlammabilityDamageFactor(flammability);
                    float damagePerPulse = targetIsPawn ? pawnDamage : damage;
                    target.TakeDamage(new DamageInfo(
                        DamageDefOf.Burn,
                        damagePerPulse * distanceFactor * flammabilityFactor,
                        armorPenetration,
                        -1f,
                        instigator));
                }

            }
        }

        private float FlammabilityDamageFactor(float flammability)
        {
            float threshold = Mathf.Max(0.01f, lowFlammabilityThreshold);
            float clampedFlammability = Mathf.Max(0f, flammability);
            if (clampedFlammability <= threshold)
            {
                return Mathf.Lerp(
                    minimumFlammabilityDamageFactor,
                    threshold,
                    clampedFlammability / threshold);
            }

            return Mathf.Clamp(clampedFlammability, threshold, 1f);
        }

        private void EmitThermiteEffects()
        {
            if (Map == null)
            {
                return;
            }

            foreach (IntVec3 cell in CrossCells(Position, crossRadius))
            {
                if (!cell.InBounds(Map))
                {
                    continue;
                }

                float distanceFactor = cell == Position ? 1.2f : 1f;
                FireUtility.TryStartFireIn(cell, Map, fireSize * distanceFactor, instigator);
                Vector3 effectPosition = cell.ToVector3Shifted();
                FleckMaker.ThrowFireGlow(effectPosition, Map, 1.1f * distanceFactor);
                FleckMaker.ThrowSmoke(effectPosition, Map, 0.75f * distanceFactor);
            }
        }

        private static IEnumerable<IntVec3> CrossCells(IntVec3 center, int radius)
        {
            yield return center;
            for (int distance = 1; distance <= radius; distance++)
            {
                yield return center + new IntVec3(distance, 0, 0);
                yield return center + new IntVec3(-distance, 0, 0);
                yield return center + new IntVec3(0, 0, distance);
                yield return center + new IntVec3(0, 0, -distance);
            }
        }
    }
}
