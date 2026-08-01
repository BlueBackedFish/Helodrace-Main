using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace
{
    public sealed class IncendiaryGelProjectileExtension : DefModExtension
    {
        public HediffDef directHitHediff;
        public ThingDef fuelFilthDef;
        public float fuelRadius = 1.9f;
        public float fuelChance = 0.72f;
        public float fireRadius = 2.4f;
        public float fireChance = 0.72f;
        public float edgeChanceFactor = 0.35f;
        public float fireSize = 0.45f;
        public float directHitFireSize = 0.65f;
        public float directHitFireGrowth = 0.35f;
        public float maximumAttachedFireSize = 1.75f;
        public int directHitStaggerTicks = 30;
        public int trailIntervalTicks = 2;
        public float trailSize = 0.42f;
    }

    /// <summary>
    /// A slow glob of burning gel. It starts scattered ground fires at the
    /// impact point and coats a directly-hit pawn in gel that can reignite.
    /// </summary>
    public sealed class Projectile_IncendiaryGel : Projectile
    {
        private int trailTicks;

        protected override void Tick()
        {
            base.Tick();

            IncendiaryGelProjectileExtension extension =
                def.GetModExtension<IncendiaryGelProjectileExtension>();
            int interval = Mathf.Max(1, extension?.trailIntervalTicks ?? 2);
            trailTicks++;
            if (Spawned && trailTicks % interval == 0)
            {
                Vector3 position = ExactPosition;
                FleckMaker.ThrowFireGlow(position, Map, Mathf.Max(0.1f, extension?.trailSize ?? 0.42f));
                FleckMaker.ThrowMicroSparks(position, Map);
                if (Rand.Chance(0.35f))
                {
                    FleckMaker.ThrowSmoke(position, Map, 0.18f);
                }
            }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;
            Thing instigator = Launcher;
            Pawn directlyHitPawn = blockedByShield ? null : hitThing as Pawn;
            IncendiaryGelProjectileExtension extension =
                def.GetModExtension<IncendiaryGelProjectileExtension>();

            base.Impact(hitThing, blockedByShield);

            if (directlyHitPawn != null && !directlyHitPawn.Dead)
            {
                IgniteDirectHit(directlyHitPawn, instigator, extension);
                PlayBulletImpactSound(directlyHitPawn, impactMap);
                directlyHitPawn.stances?.stagger.StaggerFor(
                    Mathf.Max(0, extension?.directHitStaggerTicks ?? 30));
                ApplyBurningGel(directlyHitPawn, extension?.directHitHediff);
            }

            ScatterFuel(impactMap, impactCell, extension);
            ScatterFire(impactMap, impactCell, instigator, extension);
        }

        private static void IgniteDirectHit(
            Pawn pawn,
            Thing instigator,
            IncendiaryGelProjectileExtension extension)
        {
            float initialSize = Mathf.Max(0.1f, extension?.directHitFireSize ?? 0.65f);
            float growth = Mathf.Max(0f, extension?.directHitFireGrowth ?? 0.35f);
            float maximumSize = Mathf.Max(initialSize, extension?.maximumAttachedFireSize ?? 1.75f);
            BurningGelUtility.IgniteOrIntensify(pawn, instigator, initialSize, growth, maximumSize);
        }

        private static void PlayBulletImpactSound(Pawn pawn, Map map)
        {
            if (map == null)
            {
                return;
            }

            string soundDefName = pawn.RaceProps.IsMechanoid
                ? "BulletImpact_Metal"
                : "BulletImpact_Flesh";
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail(soundDefName);
            if (sound != null)
            {
                SoundStarter.PlayOneShot(
                    sound,
                    SoundInfo.InMap(new TargetInfo(pawn.Position, map), MaintenanceType.None));
            }
        }

        private static void ApplyBurningGel(Pawn pawn, HediffDef hediffDef)
        {
            if (hediffDef == null || pawn.health == null)
            {
                return;
            }

            // A fresh direct hit refreshes the full duration instead of adding
            // several independent copies of the same whole-body condition.
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
            }

            pawn.health.AddHediff(hediffDef);
        }

        private static void ScatterFuel(
            Map map,
            IntVec3 center,
            IncendiaryGelProjectileExtension extension)
        {
            if (map == null || !center.InBounds(map))
            {
                return;
            }

            ThingDef fuelDef = extension?.fuelFilthDef
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Filth_Fuel");
            if (fuelDef == null)
            {
                return;
            }

            float radius = Mathf.Max(0.1f, extension?.fuelRadius ?? 1.9f);
            float chance = Mathf.Clamp01(extension?.fuelChance ?? 0.72f);
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (cell.InBounds(map) && Rand.Chance(chance))
                {
                    FilthMaker.TryMakeFilth(cell, map, fuelDef, 1);
                }
            }
        }

        private static void ScatterFire(
            Map map,
            IntVec3 center,
            Thing instigator,
            IncendiaryGelProjectileExtension extension)
        {
            if (map == null || !center.InBounds(map))
            {
                return;
            }

            float radius = Mathf.Max(0.1f, extension?.fireRadius ?? 2.4f);
            float chance = Mathf.Clamp01(extension?.fireChance ?? 0.72f);
            float edgeFactor = Mathf.Clamp01(extension?.edgeChanceFactor ?? 0.35f);
            float fireSize = Mathf.Max(0.1f, extension?.fireSize ?? 0.45f);

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                float centerFactor = Mathf.InverseLerp(radius, 0f, center.DistanceTo(cell));
                float cellChance = chance * Mathf.Lerp(edgeFactor, 1f, centerFactor);
                if (Rand.Chance(cellChance))
                {
                    FireUtility.TryStartFireIn(cell, map, fireSize, instigator);
                }
            }

            FleckMaker.ThrowSmoke(center.ToVector3Shifted(), map, 0.8f);
        }
    }

    /// <summary>
    /// Uses the scorcher's mini-flameblaster report for every glob in the
    /// stream. Looking it up at runtime keeps Biotech optional; Core's fire
    /// spew sound is used as a fallback when that expansion is not active.
    /// </summary>
    public sealed class Verb_ShootIncendiaryGel : Verb_Shoot
    {
        protected override bool TryCastShot()
        {
            bool fired = base.TryCastShot();
            if (fired && caster?.Spawned == true)
            {
                SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("Shot_MiniFlameblaster")
                    ?? DefDatabase<SoundDef>.GetNamedSilentFail("FireSpew_Resolve");
                if (sound != null)
                {
                    SoundStarter.PlayOneShot(
                        sound,
                        SoundInfo.InMap(new TargetInfo(caster), MaintenanceType.None));
                }
            }

            return fired;
        }
    }

    public sealed class HediffCompProperties_BurningGel : HediffCompProperties
    {
        public int ignitionIntervalTicks = 90;
        public float fireSize = 0.35f;
        public float fireGrowth = 0.18f;
        public float maximumFireSize = 1.75f;

        public HediffCompProperties_BurningGel()
        {
            compClass = typeof(HediffComp_BurningGel);
        }
    }

    /// <summary>
    /// Reattaches fire while the gel remains on the pawn. Firefoam or rain can
    /// put the visible fire out temporarily, but the remaining gel may ignite
    /// it again on the next interval.
    /// </summary>
    public sealed class HediffComp_BurningGel : HediffComp
    {
        private int ticksUntilIgnition = 1;

        private HediffCompProperties_BurningGel Props =>
            (HediffCompProperties_BurningGel)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            ticksUntilIgnition--;
            if (ticksUntilIgnition > 0)
            {
                return;
            }

            ticksUntilIgnition = Mathf.Max(1, Props.ignitionIntervalTicks);
            Pawn pawn = parent.pawn;
            if (pawn?.Spawned == true && !pawn.Dead)
            {
                BurningGelUtility.IgniteOrIntensify(
                    pawn,
                    null,
                    Mathf.Max(0.1f, Props.fireSize),
                    Mathf.Max(0f, Props.fireGrowth),
                    Mathf.Max(Props.fireSize, Props.maximumFireSize));
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksUntilIgnition, "ticksUntilGelIgnition", 1);
        }
    }

    internal static class BurningGelUtility
    {
        public static void IgniteOrIntensify(
            Pawn pawn,
            Thing instigator,
            float initialSize,
            float growth,
            float maximumSize)
        {
            Fire attachedFire = pawn.GetAttachment(ThingDefOf.Fire) as Fire;
            if (attachedFire == null)
            {
                pawn.TryAttachFire(initialSize, instigator);
                return;
            }

            attachedFire.fireSize = Mathf.Min(maximumSize, attachedFire.fireSize + growth);
        }
    }
}
