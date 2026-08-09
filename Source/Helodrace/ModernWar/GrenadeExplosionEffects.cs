using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class ExplosiveGrenadeVisualExtension : DefModExtension
    {
        public int smokeCount = 30;
        public float initialRadius = 0.28f;
        public float expansionSpeed = 1.8f;
        public float smokeScale = 0.9f;
    }

    public sealed class FragmentationGrenadeExtension : DefModExtension
    {
        public ThingDef fragmentProjectile;
        public int fragmentCount = 24;
        public float radius = 8f;
        public float minimumRangeFactor = 0.55f;
        public float targetedFragmentFraction = 0.5f;
        public float closeAimMultiplier = 1.5f;
        public float edgeAimMultiplier = 0.25f;
        public float minimumTargetedAimChance = 0.35f;
        public float longRangeFragmentFraction = 0.15f;
        public float longRangeRadius = 12f;
        public float longRangeMinimumFactor = 0.7f;
    }

    internal sealed class Verb_GrenadeFragmentShot : Verb_LaunchProjectile
    {
        public void Initialize(Thing shotCaster, ThingDef projectileDef, float range)
        {
            caster = shotCaster;
            verbProps = new VerbProperties
            {
                verbClass = typeof(Verb_GrenadeFragmentShot),
                range = range,
                requireLineOfSight = true,
                defaultProjectile = projectileDef
            };
            GrenadeFragmentVerbOwner owner = new GrenadeFragmentVerbOwner(
                shotCaster,
                verbProps);
            verbTracker = owner.VerbTracker;
        }
    }

    internal sealed class GrenadeFragmentVerbOwner : IVerbOwner
    {
        private readonly List<VerbProperties> verbProperties;

        public GrenadeFragmentVerbOwner(Thing caster, VerbProperties properties)
        {
            ConstantCaster = caster;
            verbProperties = new List<VerbProperties> { properties };
            VerbTracker = new VerbTracker(this);
        }

        public VerbTracker VerbTracker { get; }
        public List<VerbProperties> VerbProperties => verbProperties;
        public List<Tool> Tools => null;
        public ImplementOwnerTypeDef ImplementOwnerTypeDef => null;
        public Thing ConstantCaster { get; }

        public string UniqueVerbOwnerID()
        {
            return $"HD_GrenadeFragment_{ConstantCaster?.thingIDNumber ?? 0}";
        }

        public bool VerbsStillUsableBy(Pawn pawn)
        {
            return ConstantCaster != null && !ConstantCaster.Destroyed;
        }
    }

    internal static class GrenadeExplosionEffectUtility
    {
        private const string RapidSmokeDefName = "HD_DirectionalVanillaSmoke";
        private const string FlashbangLightDefName = "HD_FlashbangLightEffect";

        public static void ThrowExpandingSmokeRing(
            IntVec3 center,
            Map map,
            ExplosiveGrenadeVisualExtension extension)
        {
            if (map == null || !center.InBounds(map))
            {
                return;
            }

            int smokeCount = Mathf.Clamp(extension?.smokeCount ?? 30, 12, 64);
            float initialRadius = Mathf.Max(0f, extension?.initialRadius ?? 0.28f);
            float expansionSpeed = Mathf.Max(0.1f, extension?.expansionSpeed ?? 1.8f);
            float smokeScale = Mathf.Max(0.1f, extension?.smokeScale ?? 0.9f);
            Vector3 origin = center.ToVector3Shifted();
            FleckDef radialSmokeDef = DefDatabase<FleckDef>.GetNamedSilentFail(RapidSmokeDefName)
                ?? FleckDefOf.Smoke;

            DirectionalSmokeFleckUtility.ThrowRadialEaseOut(
                origin,
                map,
                smokeCount,
                2.5f,
                new FloatRange(expansionSpeed * 1.10f, expansionSpeed * 1.55f),
                new FloatRange(0.42f, 0.62f),
                new FloatRange(0.06f, 0.13f),
                new FloatRange(smokeScale * 0.82f, smokeScale * 1.18f),
                new FloatRange(0.65f, 1.25f),
                new FloatRange(initialRadius * 0.75f, initialRadius * 1.25f),
                radialSmokeDef);
        }

        public static void ThrowLingeringExplosionDust(
            IntVec3 center,
            Map map,
            ExplosiveGrenadeVisualExtension extension)
        {
            if (map == null || !center.InBounds(map))
            {
                return;
            }

            int dustCount = Mathf.Clamp(
                Mathf.RoundToInt((extension?.smokeCount ?? 28) * 0.65f),
                14,
                28);
            float dustRadius = Mathf.Max(
                0.8f,
                (extension?.expansionSpeed ?? 1.8f) * 0.85f);
            float baseScale = Mathf.Max(0.2f, extension?.smokeScale ?? 0.9f);
            Vector3 origin = center.ToVector3Shifted();

            for (int i = 0; i < dustCount; i++)
            {
                float angle = Rand.Range(0f, 360f);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                float distance = Mathf.Sqrt(Rand.Value) * dustRadius;
                Vector3 position = origin + direction * distance;
                if (!position.ToIntVec3().InBounds(map))
                {
                    continue;
                }

                FleckCreationData dust = FleckMaker.GetDataStatic(
                    position,
                    map,
                    FleckDefOf.DustPuffThick,
                    baseScale * Rand.Range(0.75f, 1.35f));
                dust.rotation = Rand.Range(0f, 360f);
                dust.rotationRate = Rand.Range(-9f, 9f);
                dust.velocity = direction * Rand.Range(0.04f, 0.14f);
                dust.solidTimeOverride = Rand.Range(1.6f, 3.0f);
                dust.instanceColor = new Color(0.50f, 0.46f, 0.40f, 0.72f);
                map.flecks.CreateFleck(dust);
            }
        }

        public static void ThrowFragments(
            IntVec3 center,
            Map map,
            FragmentationGrenadeExtension extension,
            Thing instigator,
            Thing shotCaster)
        {
            ThingDef fragmentDef = extension?.fragmentProjectile;
            if (map == null
                || fragmentDef == null
                || fragmentDef.projectile == null
                || shotCaster == null
                || !center.InBounds(map))
            {
                return;
            }

            // Off-map mortar support intentionally launches its parent shell
            // without a Thing launcher. A null fragment launcher breaks pawn
            // clamor/flee reactions and combat-log grammar when the fragment
            // hits. The still-spawned parent projectile is a safe fallback.
            instigator = instigator ?? shotCaster;
            float radius = Mathf.Max(0.1f, extension.radius);
            float minimumRange = radius * Mathf.Clamp(extension.minimumRangeFactor, 0.1f, 1f);
            int fragmentCount = Mathf.Clamp(extension.fragmentCount, 4, 64);
            int longRangeCount = Mathf.Clamp(
                Mathf.RoundToInt(fragmentCount * Mathf.Clamp01(extension.longRangeFragmentFraction)),
                0,
                fragmentCount);
            float longRangeRadius = Mathf.Max(radius, extension.longRangeRadius);
            float longRangeMinimum = Mathf.Max(
                radius,
                longRangeRadius * Mathf.Clamp(extension.longRangeMinimumFactor, 0.1f, 1f));
            Vector3 origin = center.ToVector3Shifted();
            int launchedCount = LaunchTargetedFragments(
                center,
                map,
                extension,
                instigator,
                shotCaster,
                fragmentDef,
                fragmentCount,
                radius,
                origin);
            int randomFragmentCount = fragmentCount - launchedCount;
            longRangeCount = Mathf.Min(longRangeCount, randomFragmentCount);

            for (int randomIndex = 0; randomIndex < randomFragmentCount; randomIndex++)
            {
                int i = launchedCount + randomIndex;
                float angle = (i + Rand.Range(-0.42f, 0.42f)) * 360f / fragmentCount;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                bool longRange = randomIndex >= randomFragmentCount - longRangeCount;
                float distance = longRange
                    ? Rand.Range(longRangeMinimum, longRangeRadius)
                    : Rand.Range(minimumRange, radius);
                IntVec3 targetCell = (origin + direction * distance).ToIntVec3();
                targetCell.x = Mathf.Clamp(targetCell.x, 0, map.Size.x - 1);
                targetCell.z = Mathf.Clamp(targetCell.z, 0, map.Size.z - 1);
                targetCell.y = center.y;
                if (targetCell == center)
                {
                    continue;
                }

                LocalTargetInfo target = new LocalTargetInfo(targetCell);
                LaunchFragment(
                    center,
                    map,
                    fragmentDef,
                    instigator,
                    origin,
                    target,
                    target,
                    null);
            }
        }

        public static void ThrowDirectionalFragments(
            IntVec3 center,
            Map map,
            FragmentationGrenadeExtension extension,
            Thing instigator,
            Thing shotCaster,
            Vector3 direction,
            float coneDegrees)
        {
            ThingDef fragmentDef = extension?.fragmentProjectile;
            direction.y = 0f;
            if (map == null
                || fragmentDef?.projectile == null
                || shotCaster == null
                || !center.InBounds(map)
                || direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            instigator = instigator ?? shotCaster;
            direction.Normalize();
            int fragmentCount = Mathf.Clamp(extension.fragmentCount, 1, 64);
            float radius = Mathf.Max(0.1f, extension.radius);
            float minimumRange = radius
                * Mathf.Clamp(extension.minimumRangeFactor, 0.05f, 1f);
            float cone = Mathf.Clamp(coneDegrees, 1f, 360f);
            float coneStart = direction.AngleFlat() - cone * 0.5f;
            Vector3 origin = center.ToVector3Shifted();

            for (int i = 0; i < fragmentCount; i++)
            {
                float angle = coneStart
                    + (i + Rand.Range(0.05f, 0.95f)) * cone / fragmentCount;
                Vector3 fragmentDirection = Quaternion.AngleAxis(
                    angle,
                    Vector3.up) * Vector3.forward;
                float distance = Rand.Range(minimumRange, radius);
                IntVec3 targetCell = (origin + fragmentDirection * distance).ToIntVec3();
                targetCell.x = Mathf.Clamp(targetCell.x, 0, map.Size.x - 1);
                targetCell.z = Mathf.Clamp(targetCell.z, 0, map.Size.z - 1);
                targetCell.y = center.y;
                if (targetCell == center)
                {
                    continue;
                }

                LocalTargetInfo target = new LocalTargetInfo(targetCell);
                LaunchFragment(
                    center,
                    map,
                    fragmentDef,
                    instigator,
                    origin,
                    target,
                    target,
                    null);
            }
        }

        private static int LaunchTargetedFragments(
            IntVec3 center,
            Map map,
            FragmentationGrenadeExtension extension,
            Thing instigator,
            Thing shotCaster,
            ThingDef fragmentDef,
            int fragmentCount,
            float radius,
            Vector3 origin)
        {
            int targetedLimit = Mathf.Clamp(
                Mathf.RoundToInt(fragmentCount * Mathf.Clamp01(extension.targetedFragmentFraction)),
                0,
                fragmentCount);
            if (targetedLimit <= 0)
            {
                return 0;
            }

            List<Pawn> targets = new List<Pawn>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn != null
                    && !pawn.Dead
                    && pawn.health != null
                    && center.DistanceTo(pawn.Position) <= radius)
                {
                    targets.Add(pawn);
                }
            }

            targets.Sort((left, right) =>
                center.DistanceToSquared(left.Position).CompareTo(
                    center.DistanceToSquared(right.Position)));

            Verb_GrenadeFragmentShot fragmentVerb = new Verb_GrenadeFragmentShot();
            fragmentVerb.Initialize(shotCaster, fragmentDef, radius);
            int launched = 0;
            for (int i = 0; i < targets.Count && launched < targetedLimit; i++)
            {
                Pawn pawn = targets[i];
                LocalTargetInfo intendedTarget = new LocalTargetInfo(pawn);
                if (!fragmentVerb.CanHitTargetFrom(center, intendedTarget))
                {
                    continue;
                }

                ShotReport report = ShotReport.HitReportFor(
                    shotCaster,
                    fragmentVerb,
                    intendedTarget);
                float distanceProgress = Mathf.Clamp01(center.DistanceTo(pawn.Position) / radius);
                float aimMultiplier = Mathf.Lerp(
                    Mathf.Max(0f, extension.closeAimMultiplier),
                    Mathf.Max(0f, extension.edgeAimMultiplier),
                    distanceProgress);
                float aimChance = Mathf.Clamp01(Mathf.Max(
                    extension.minimumTargetedAimChance,
                    report.AimOnTargetChance * aimMultiplier));
                if (!Rand.Chance(aimChance))
                {
                    continue;
                }

                LocalTargetInfo usedTarget = intendedTarget;
                ThingDef targetCoverDef = null;
                if (!Rand.Chance(report.PassCoverChance))
                {
                    Thing cover = report.GetRandomCoverToMissInto();
                    if (cover != null)
                    {
                        usedTarget = new LocalTargetInfo(cover);
                        targetCoverDef = cover.def;
                    }
                }

                LaunchFragment(
                    center,
                    map,
                    fragmentDef,
                    instigator,
                    origin,
                    usedTarget,
                    intendedTarget,
                    targetCoverDef);
                launched++;
            }

            return launched;
        }

        private static void LaunchFragment(
            IntVec3 center,
            Map map,
            ThingDef fragmentDef,
            Thing instigator,
            Vector3 origin,
            LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget,
            ThingDef targetCoverDef)
        {
            Thing blockingEdifice = FirstBlockingEdificeOnLine(
                center,
                usedTarget.Cell,
                map);
            if (blockingEdifice != null)
            {
                usedTarget = new LocalTargetInfo(blockingEdifice);
                intendedTarget = usedTarget;
                targetCoverDef = blockingEdifice.def;
            }

            Projectile fragment = GenSpawn.Spawn(fragmentDef, center, map) as Projectile;
            fragment?.Launch(
                instigator,
                origin,
                usedTarget,
                intendedTarget,
                ProjectileHitFlags.All,
                false,
                null,
                targetCoverDef);
        }

        private static Thing FirstBlockingEdificeOnLine(
            IntVec3 start,
            IntVec3 end,
            Map map)
        {
            int deltaX = end.x - start.x;
            int deltaZ = end.z - start.z;
            int cellCountX = Mathf.Abs(deltaX);
            int cellCountZ = Mathf.Abs(deltaZ);
            int stepX = deltaX == 0 ? 0 : deltaX > 0 ? 1 : -1;
            int stepZ = deltaZ == 0 ? 0 : deltaZ > 0 ? 1 : -1;
            int x = start.x;
            int z = start.z;
            int movedX = 0;
            int movedZ = 0;

            while (movedX < cellCountX || movedZ < cellCountZ)
            {
                int xBoundary = (1 + 2 * movedX) * cellCountZ;
                int zBoundary = (1 + 2 * movedZ) * cellCountX;

                if (xBoundary == zBoundary)
                {
                    // The ray crosses a cell corner. Check both adjacent cells so a
                    // fragment cannot slip diagonally through two touching walls.
                    Thing sideBlocker = BlockingEdificeAt(
                        new IntVec3(x + stepX, start.y, z),
                        map);
                    if (sideBlocker != null)
                    {
                        return sideBlocker;
                    }

                    sideBlocker = BlockingEdificeAt(
                        new IntVec3(x, start.y, z + stepZ),
                        map);
                    if (sideBlocker != null)
                    {
                        return sideBlocker;
                    }

                    x += stepX;
                    z += stepZ;
                    movedX++;
                    movedZ++;
                }
                else if (xBoundary < zBoundary)
                {
                    x += stepX;
                    movedX++;
                }
                else
                {
                    z += stepZ;
                    movedZ++;
                }

                Thing blocker = BlockingEdificeAt(new IntVec3(x, start.y, z), map);
                if (blocker != null)
                {
                    return blocker;
                }
            }

            return null;
        }

        private static Thing BlockingEdificeAt(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
            {
                return null;
            }

            Building edifice = cell.GetEdifice(map);
            if (edifice == null || edifice.def.Fillage != FillCategory.Full)
            {
                return null;
            }

            if (edifice is Building_Door door && door.Open)
            {
                return null;
            }

            return edifice;
        }

        public static void ThrowFlashbangEffects(IntVec3 center, Map map)
        {
            if (map == null || !center.InBounds(map))
            {
                return;
            }

            Vector3 origin = center.ToVector3Shifted();
            FleckDef rapidSmokeDef = DefDatabase<FleckDef>.GetNamedSilentFail(RapidSmokeDefName)
                ?? FleckDefOf.Smoke;
            DirectionalSmokeFleckUtility.ThrowRadialEaseOut(
                origin,
                map,
                36,
                2.2f,
                new FloatRange(2.7f, 3.4f),
                new FloatRange(0.55f, 0.70f),
                new FloatRange(0.12f, 0.22f),
                new FloatRange(0.72f, 1.08f),
                new FloatRange(0.8f, 1.35f),
                new FloatRange(0.04f, 0.20f),
                rapidSmokeDef);
            DirectionalSmokeFleckUtility.ThrowRadialEaseOut(
                origin,
                map,
                24,
                3.2f,
                new FloatRange(2.0f, 2.8f),
                new FloatRange(0.50f, 0.65f),
                new FloatRange(0.10f, 0.18f),
                new FloatRange(0.92f, 1.36f),
                new FloatRange(1.0f, 1.65f),
                new FloatRange(0.08f, 0.28f),
                rapidSmokeDef,
                7.5f);
            DirectionalSmokeFleckUtility.ThrowRadialEaseOut(
                origin,
                map,
                16,
                4f,
                new FloatRange(1.35f, 2.1f),
                new FloatRange(0.45f, 0.60f),
                new FloatRange(0.08f, 0.15f),
                new FloatRange(1.12f, 1.68f),
                new FloatRange(1.2f, 1.9f),
                new FloatRange(0.02f, 0.18f),
                rapidSmokeDef,
                4f);

            ThrowFlashbangSparks(origin, map);
            FleckMaker.Static(origin, map, FleckDefOf.ExplosionFlash, 13f);
            FleckMaker.ThrowFireGlow(origin, map, 5.6f);

            SpawnFlashbangLight(center, map);
        }

        private static void ThrowFlashbangSparks(Vector3 origin, Map map)
        {
            for (int i = 0; i < 2; i++)
            {
                FleckMaker.ThrowMicroSparks(origin, map);
            }

            const int sparkCount = 28;
            for (int i = 0; i < sparkCount; i++)
            {
                float angle = (i + Rand.Range(-0.38f, 0.38f)) * 360f / sparkCount;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                Vector3 position = origin + direction * Rand.Range(0.02f, 0.10f);
                if (!position.ToIntVec3().InBounds(map))
                {
                    continue;
                }

                FleckCreationData data = FleckMaker.GetDataStatic(
                    position,
                    map,
                    FleckDefOf.MicroSparks,
                    Rand.Range(0.38f, 0.92f));
                data.rotation = angle + Rand.Range(-12f, 12f);
                data.rotationRate = Rand.Range(-35f, 35f);
                data.velocity = direction * Rand.Range(0.18f, 0.42f);
                data.instanceColor = new Color(
                    1f,
                    Rand.Range(0.72f, 0.94f),
                    0.35f,
                    1f);
                map.flecks.CreateFleck(data);
            }
        }

        private static void SpawnFlashbangLight(IntVec3 center, Map map)
        {
            ThingDef lightDef = DefDatabase<ThingDef>.GetNamedSilentFail(FlashbangLightDefName);
            Thing_FlashbangLightEffect light = lightDef == null
                ? null
                : ThingMaker.MakeThing(lightDef) as Thing_FlashbangLightEffect;
            if (light != null)
            {
                GenSpawn.Spawn(light, center, map);
            }
        }
    }

    public class Projectile_FragmentingExplosive : Projectile_Explosive
    {
        protected override void Explode()
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;
            Thing instigator = Launcher;
            FragmentationGrenadeExtension fragmentation =
                def.GetModExtension<FragmentationGrenadeExtension>();

            try
            {
                GrenadeExplosionEffectUtility.ThrowFragments(
                    impactCell,
                    impactMap,
                    fragmentation,
                    instigator,
                    this);
            }
            catch (System.Exception exception)
            {
                Log.ErrorOnce(
                    $"Helodrace explosive fragment calculation failed; continuing main explosion. {exception}",
                    GetHashCode());
            }

            base.Explode();
        }
    }

    public sealed class Projectile_ExplosiveGrenade : Projectile_ModernGrenade
    {
        protected override void Explode()
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;
            Thing instigator = Launcher;
            ExplosiveGrenadeVisualExtension extension =
                def.GetModExtension<ExplosiveGrenadeVisualExtension>();
            FragmentationGrenadeExtension fragmentation =
                def.GetModExtension<FragmentationGrenadeExtension>();
            FlashbangProjectileExtension concussion =
                def.GetModExtension<FlashbangProjectileExtension>();

            try
            {
                GrenadeExplosionEffectUtility.ThrowFragments(
                    impactCell,
                    impactMap,
                    fragmentation,
                    instigator,
                    this);
            }
            catch (System.Exception exception)
            {
                Log.ErrorOnce(
                    $"Helodrace grenade fragment calculation failed; continuing main explosion. {exception}",
                    GetHashCode());
            }
            base.Explode();
            FlashbangUtility.ApplySuppression(
                impactCell,
                impactMap,
                concussion,
                instigator);
            GrenadeExplosionEffectUtility.ThrowExpandingSmokeRing(
                impactCell,
                impactMap,
                extension);
            GrenadeExplosionEffectUtility.ThrowLingeringExplosionDust(
                impactCell,
                impactMap,
                extension);
        }
    }

    public sealed class Thing_FlashbangLightEffect : ThingWithComps
    {
        private const int LifetimeTicks = 6;
        private int ticksRemaining = LifetimeTicks;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", LifetimeTicks);
        }

        protected override void Tick()
        {
            base.Tick();
            ticksRemaining--;
            if (ticksRemaining <= 0)
            {
                Destroy(DestroyMode.Vanish);
            }
        }
    }
}
