using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace.Future
{
    public sealed class LanceLra7ProjectileExtension : DefModExtension
    {
        public float damagePerCell = 0.36f;
        public float armorPenetrationPerCell = 0.008f;
        public float feedbackRunawayMinDistance = 30f;
        public float feedbackRunawayGuaranteedDistance = 70f;
        public float feedbackConeRadius = 3.2f;
        public float feedbackConeAngle = 70f;
        public float feedbackDamageFactor = 0.18f;
        public SoundDef feedbackSound;
        public float attractionConeRadius = 4.2f;
        public float attractionConeAngle = 70f;
        public int attractionPullCells = 2;
        public float vacuumBurnDamage = 5f;
        public int beamDurationTicks = 7;
        public float beamWidth = 0.12f;
        public SoundDef shotSound;
    }

    /// <summary>
    /// The LRA-7 resolves its flight during Launch. The projectile Thing exists
    /// only long enough to use RimWorld's normal verb, accuracy and shield path.
    /// </summary>
    public sealed class Projectile_LanceLra7 : Projectile
    {
        public override void Launch(
            Thing launcher,
            Vector3 origin,
            LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget,
            ProjectileHitFlags hitFlags,
            bool preventFriendlyFire = false,
            Thing equipment = null,
            ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);

            Map map = Map;
            if (map == null)
            {
                return;
            }

            IntVec3 impactCell = usedTarget.Cell;
            if (!impactCell.InBounds(map))
            {
                impactCell = intendedTarget.Cell;
            }

            float distance = Vector3.Distance(origin, impactCell.ToVector3Shifted());
            LanceLra7ProjectileExtension extension = def.GetModExtension<LanceLra7ProjectileExtension>();
            CompSharpshooterWeapon sharpshooter = equipment?.TryGetComp<CompSharpshooterWeapon>();
            float accuracyMultiplier = sharpshooter != null
                && sharpshooter.altModeActive
                && sharpshooter.CanUseSharpshooterMode
                    ? sharpshooter.Props.altAccuracyMultiplier
                    : 1f;
            bool hitRollSucceeded = RollHit(launcher, equipment, distance, accuracyMultiplier);
            if (!hitRollSucceeded)
            {
                Vector2 missOffset = Rand.InsideUnitCircle.normalized * Rand.Range(1.2f, 3.2f);
                IntVec3 missedCell = impactCell + new IntVec3(Mathf.RoundToInt(missOffset.x), 0, Mathf.RoundToInt(missOffset.y));
                if (missedCell.InBounds(map))
                {
                    impactCell = missedCell;
                }
            }
            SpawnBeamEffect(map, origin, impactCell.ToVector3Shifted(), extension);
            PlayBeamSound(map, origin, impactCell.ToVector3Shifted(), extension);

            Position = impactCell;
            float damage = def.projectile.GetDamageAmount(equipment) + distance * (extension?.damagePerCell ?? 0.36f);
            float armorPenetration = def.projectile.GetArmorPenetration(equipment)
                + distance * (extension?.armorPenetrationPerCell ?? 0.008f);

            float runawayChance = Mathf.InverseLerp(
                extension?.feedbackRunawayMinDistance ?? 30f,
                extension?.feedbackRunawayGuaranteedDistance ?? 70f,
                distance);
            if (runawayChance > 0f && Rand.Chance(runawayChance))
            {
                ApplyFeedbackCone(
                    map,
                    impactCell,
                    impactCell.ToVector3Shifted() - origin,
                    launcher,
                    equipment,
                    damage,
                    armorPenetration,
                    extension);
                if (!Destroyed)
                {
                    Destroy(DestroyMode.Vanish);
                }
                return;
            }

            Thing hitThing = hitRollSucceeded ? usedTarget.Thing ?? intendedTarget.Thing : null;
            if (hitThing != null && !hitThing.Destroyed)
            {
                DamageInfo damageInfo = new DamageInfo(
                    def.projectile.damageDef,
                    damage,
                    armorPenetration,
                    ExactRotation.eulerAngles.y,
                    launcher,
                    null,
                    equipment?.def);
                hitThing.TakeDamage(damageInfo);
            }
            else
            {
                FleckMaker.Static(impactCell, map, FleckDefOf.ShotHit_Dirt);
            }

            if (!Destroyed)
            {
                Destroy(DestroyMode.Vanish);
            }
        }

        private static bool RollHit(
            Thing launcher,
            Thing equipment,
            float distance,
            float accuracyMultiplier)
        {
            StatDef accuracyStat = distance <= 3f
                ? StatDefOf.AccuracyTouch
                : distance <= 12f
                    ? StatDefOf.AccuracyShort
                    : distance <= 25f
                        ? StatDefOf.AccuracyMedium
                        : StatDefOf.AccuracyLong;
            float weaponAccuracy = equipment?.GetStatValue(accuracyStat) ?? 0.75f;
            float shooterAccuracy = launcher is Pawn pawn ? pawn.GetStatValue(StatDefOf.ShootingAccuracyPawn) : 1f;
            float chance = weaponAccuracy
                * Mathf.Clamp(shooterAccuracy, 0.35f, 1.35f)
                * Mathf.Max(0.01f, accuracyMultiplier);

            return Rand.Chance(Mathf.Clamp(chance, 0.05f, 0.99f));
        }

        private static void ApplyFeedbackCone(
            Map map,
            IntVec3 originCell,
            Vector3 firingDirection,
            Thing launcher,
            Thing equipment,
            float shotDamage,
            float armorPenetration,
            LanceLra7ProjectileExtension extension)
        {
            float radius = Mathf.Max(0.1f, extension?.feedbackConeRadius ?? 3.2f);
            float halfAngle = Mathf.Clamp(extension?.feedbackConeAngle ?? 70f, 1f, 180f) * 0.5f;
            float damageFactor = Mathf.Max(0f, extension?.feedbackDamageFactor ?? 0.22f);
            int maximumDamage = Mathf.Max(1, Mathf.RoundToInt(shotDamage * damageFactor));
            Vector3 forward = firingDirection;
            forward.y = 0f;
            forward.Normalize();

            HashSet<Thing> damagedThings = new HashSet<Thing>();
            List<IntVec3> affectedCells = new List<IntVec3>(
                LanceLra7FeedbackUtility.AffectedCells(map, originCell, forward, radius, halfAngle * 2f));
            foreach (IntVec3 cell in affectedCells)
            {
                Vector3 offset = cell.ToVector3Shifted() - originCell.ToVector3Shifted();
                offset.y = 0f;
                float cellDistance = offset.magnitude;

                float falloff = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(cellDistance / radius));
                int cellDamage = Mathf.Max(1, Mathf.RoundToInt(maximumDamage * falloff));
                List<Thing> things = cell.GetThingList(map);
                for (int index = things.Count - 1; index >= 0; index--)
                {
                    Thing thing = things[index];
                    if (thing == null || thing.Destroyed || thing.def.category == ThingCategory.Ethereal || !damagedThings.Add(thing))
                    {
                        continue;
                    }

                    thing.TakeDamage(new DamageInfo(
                        DamageDefOf.Bomb,
                        cellDamage,
                        armorPenetration * damageFactor,
                        forward.AngleFlat(),
                        launcher,
                        null,
                        equipment?.def));
                }

            }

            extension?.feedbackSound?.PlayOneShot(new TargetInfo(originCell, map));
            SpawnVanillaExplosionEffect(map, originCell, radius, affectedCells, launcher, equipment);
            ApplyOppositeAttractionCone(map, originCell, -forward, launcher, extension);
        }

        private static void SpawnVanillaExplosionEffect(
            Map map,
            IntVec3 originCell,
            float radius,
            List<IntVec3> affectedCells,
            Thing launcher,
            Thing equipment)
        {
            GenExplosion.DoExplosion(
                originCell,
                map,
                radius,
                DamageDefOf.Bomb,
                launcher,
                damAmount: 0,
                armorPenetration: 0f,
                explosionSound: null,
                weapon: equipment?.def,
                doVisualEffects: true,
                propagationSpeed: 1f,
                doSoundEffects: false,
                screenShakeFactor: 0.35f,
                overrideCells: affectedCells);
        }

        private static void ApplyOppositeAttractionCone(
            Map map,
            IntVec3 originCell,
            Vector3 reverseDirection,
            Thing launcher,
            LanceLra7ProjectileExtension extension)
        {
            float radius = Mathf.Max(0.1f, extension?.attractionConeRadius ?? 4.2f);
            float angle = Mathf.Clamp(extension?.attractionConeAngle ?? 70f, 1f, 180f);
            List<IntVec3> cells = new List<IntVec3>(
                LanceLra7FeedbackUtility.AffectedCells(map, originCell, reverseDirection, radius, angle));
            SpawnFeedbackWave(map, originCell, cells, true);

            DamageDef vacuumDamage = DefDatabase<DamageDef>.GetNamedSilentFail("VacuumBurn")
                ?? DefDatabase<DamageDef>.GetNamedSilentFail("Frostbite");
            HashSet<Pawn> affectedPawns = new HashSet<Pawn>();
            foreach (IntVec3 cell in cells)
            {
                List<Thing> things = cell.GetThingList(map);
                for (int index = things.Count - 1; index >= 0; index--)
                {
                    Pawn pawn = things[index] as Pawn;
                    if (pawn == null || pawn.Destroyed || !affectedPawns.Add(pawn))
                    {
                        continue;
                    }

                    if (vacuumDamage != null)
                    {
                        pawn.TakeDamage(new DamageInfo(
                            vacuumDamage,
                            Mathf.Max(1f, extension?.vacuumBurnDamage ?? 5f),
                            0f,
                            reverseDirection.AngleFlat(),
                            launcher));
                    }

                    if (!pawn.Destroyed && pawn.Spawned)
                    {
                        TryPullPawn(pawn, originCell, Mathf.Max(1, extension?.attractionPullCells ?? 2));
                    }
                }
            }
        }

        private static void TryPullPawn(Pawn pawn, IntVec3 originCell, int maximumCells)
        {
            Map map = pawn.Map;
            IntVec3 destination = pawn.Position;
            for (int step = 0; step < maximumCells; step++)
            {
                IntVec3 delta = originCell - destination;
                IntVec3 next = destination + new IntVec3(System.Math.Sign(delta.x), 0, System.Math.Sign(delta.z));
                if (next == destination
                    || !next.InBounds(map)
                    || !next.Standable(map)
                    || next.GetFirstPawn(map) != null)
                {
                    break;
                }

                destination = next;
            }

            if (destination == pawn.Position)
            {
                return;
            }

            // Direct forced displacement keeps the pawn's current job, stance
            // and burst intact. The drawer tweener provides the visible slide.
            pawn.SetPositionDirect(destination);
        }

        private static void SpawnFeedbackWave(Map map, IntVec3 originCell, List<IntVec3> affectedCells, bool inward)
        {
            ThingDef waveDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_LANCE_LRA7_FeedbackWave");
            LanceLra7FeedbackWave wave = waveDef == null ? null : ThingMaker.MakeThing(waveDef) as LanceLra7FeedbackWave;
            if (wave == null)
            {
                return;
            }

            wave.Initialize(originCell, affectedCells, inward);
            GenSpawn.Spawn(wave, originCell, map);
        }

        private static void SpawnBeamEffect(Map map, Vector3 origin, Vector3 destination, LanceLra7ProjectileExtension extension)
        {
            ThingDef effectDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_LANCE_LRA7_BeamEffect");
            if (effectDef == null)
            {
                return;
            }

            const float maximumSegmentLength = 6f;
            float totalLength = Vector3.Distance(origin, destination);
            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(totalLength / maximumSegmentLength));
            int duration = extension?.beamDurationTicks ?? 7;
            float width = extension?.beamWidth ?? 0.12f;
            for (int index = 0; index < segmentCount; index++)
            {
                float startProgress = index / (float)segmentCount;
                float endProgress = (index + 1) / (float)segmentCount;
                Vector3 segmentStart = Vector3.Lerp(origin, destination, startProgress);
                Vector3 segmentEnd = Vector3.Lerp(origin, destination, endProgress);
                IntVec3 anchorCell = Vector3.Lerp(segmentStart, segmentEnd, 0.5f).ToIntVec3();
                if (!anchorCell.InBounds(map))
                {
                    continue;
                }

                LanceLra7BeamEffect effect = ThingMaker.MakeThing(effectDef) as LanceLra7BeamEffect;
                if (effect == null)
                {
                    continue;
                }

                effect.Initialize(segmentStart, segmentEnd, duration, width);
                GenSpawn.Spawn(effect, anchorCell, map);
            }
        }

        private static void PlayBeamSound(Map map, Vector3 origin, Vector3 destination, LanceLra7ProjectileExtension extension)
        {
            SoundDef sound = extension?.shotSound;
            if (sound == null)
            {
                return;
            }

            IntVec3 soundCell = Vector3.Lerp(origin, destination, 0.5f).ToIntVec3();
            if (!soundCell.InBounds(map))
            {
                soundCell = origin.ToIntVec3();
            }
            sound.PlayOneShot(new TargetInfo(soundCell, map));
        }
    }

    public static class LanceLra7FeedbackUtility
    {
        public static IEnumerable<IntVec3> AffectedCells(
            Map map,
            IntVec3 originCell,
            Vector3 forward,
            float radius,
            float coneAngle)
        {
            if (map == null || !originCell.InBounds(map))
            {
                yield break;
            }

            forward.y = 0f;
            forward.Normalize();
            float halfAngle = Mathf.Clamp(coneAngle, 1f, 180f) * 0.5f;
            Building originEdifice = originCell.GetEdifice(map);
            bool embeddedInWall = originEdifice?.def.Fillage == FillCategory.Full;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(originCell, radius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                Vector3 offset = cell.ToVector3Shifted() - originCell.ToVector3Shifted();
                offset.y = 0f;
                if (offset.sqrMagnitude > 0.01f && Vector3.Angle(forward, offset) > halfAngle)
                {
                    continue;
                }

                if (cell != originCell && (embeddedInWall || !GenSight.LineOfSight(originCell, cell, map, true)))
                {
                    continue;
                }

                yield return cell;
            }
        }
    }

    public sealed class LanceLra7FeedbackWave : Thing
    {
        private IntVec3 originCell;
        private List<IntVec3> cells;
        private int ageTicks;
        private bool inward;

        public void Initialize(IntVec3 origin, List<IntVec3> affectedCells, bool drawInward)
        {
            originCell = origin;
            cells = affectedCells ?? new List<IntVec3>();
            inward = drawInward;
            cells.Sort((left, right) => inward
                ? right.DistanceToSquared(origin).CompareTo(left.DistanceToSquared(origin))
                : left.DistanceToSquared(origin).CompareTo(right.DistanceToSquared(origin)));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref originCell, "originCell");
            Scribe_Collections.Look(ref cells, "cells", LookMode.Value);
            Scribe_Values.Look(ref ageTicks, "ageTicks");
            Scribe_Values.Look(ref inward, "inward");
        }

        protected override void Tick()
        {
            base.Tick();
            if (Map == null || cells == null)
            {
                Destroy(DestroyMode.Vanish);
                return;
            }

            int cellsPerTick = Mathf.Max(1, Mathf.CeilToInt(cells.Count / 5f));
            int startIndex = ageTicks * cellsPerTick;
            int endIndex = Mathf.Min(cells.Count, startIndex + cellsPerTick);
            for (int index = startIndex; index < endIndex; index++)
            {
                IntVec3 cell = cells[index];
                if (inward)
                {
                    FleckMaker.Static(cell, Map, FleckDefOf.FlashHollow, Rand.Range(0.72f, 1.05f));
                }
                else
                {
                    // This is the same cell-level effect used by vanilla explosions,
                    // constrained to the already calculated directional cells.
                    FleckMaker.ThrowExplosionCell(
                        cell,
                        Map,
                        FleckDefOf.ExplosionFlash,
                        new Color(0.72f, 0.9f, 1f));
                }
                FleckMaker.ThrowMicroSparks(cell.ToVector3Shifted(), Map);
                if (Rand.Chance(0.48f))
                {
                    FleckMaker.ThrowSmoke(cell.ToVector3Shifted(), Map, Rand.Range(0.35f, 0.7f));
                }
            }

            ageTicks++;
            if (endIndex >= cells.Count || ageTicks > 7)
            {
                Destroy(DestroyMode.Vanish);
            }
        }
    }

    public sealed class LanceLra7BeamEffect : Thing
    {
        private const int GradientSteps = 16;
        private static Material[] outerMaterials;
        private static Material[] coreMaterials;

        private Vector3 origin;
        private Vector3 destination;
        private int ticksLeft;
        private int initialTicks;
        private float width;

        private static void EnsureGradientMaterials()
        {
            if (outerMaterials != null && coreMaterials != null)
            {
                return;
            }

            outerMaterials = new Material[GradientSteps];
            coreMaterials = new Material[GradientSteps];
            Color outerStart = new Color(1f, 1f, 1f, 0.76f);
            Color outerEnd = new Color(0.55f, 0.86f, 1f, 0.76f);
            Color coreStart = new Color(1f, 1f, 1f, 0.99f);
            Color coreEnd = new Color(0.82f, 0.95f, 1f, 0.99f);
            for (int index = 0; index < GradientSteps; index++)
            {
                float progress = index / (float)(GradientSteps - 1);
                outerMaterials[index] = SolidColorMaterials.SimpleSolidColorMaterial(Color.Lerp(outerStart, outerEnd, progress));
                coreMaterials[index] = SolidColorMaterials.SimpleSolidColorMaterial(Color.Lerp(coreStart, coreEnd, progress));
            }
        }

        public void Initialize(Vector3 beamOrigin, Vector3 beamDestination, int durationTicks, float beamWidth)
        {
            origin = beamOrigin;
            destination = beamDestination;
            initialTicks = ticksLeft = Mathf.Max(1, durationTicks);
            width = Mathf.Max(0.02f, beamWidth);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref origin, "origin");
            Scribe_Values.Look(ref destination, "destination");
            Scribe_Values.Look(ref ticksLeft, "ticksLeft");
            Scribe_Values.Look(ref initialTicks, "initialTicks");
            Scribe_Values.Look(ref width, "width", 0.12f);
        }

        protected override void Tick()
        {
            base.Tick();
            if (--ticksLeft <= 0)
            {
                Destroy(DestroyMode.Vanish);
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            EnsureGradientMaterials();
            float fade = Mathf.Clamp01(ticksLeft / (float)Mathf.Max(1, initialTicks));
            float colorProgress = 1f - fade;
            int materialIndex = Mathf.Clamp(
                Mathf.RoundToInt(colorProgress * (GradientSteps - 1)),
                0,
                GradientSteps - 1);
            Vector3 start = origin;
            Vector3 end = destination;
            start.y = end.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            GenDraw.DrawLineBetween(start, end, outerMaterials[materialIndex], width * fade);
            GenDraw.DrawLineBetween(start, end, coreMaterials[materialIndex], width * 0.34f * fade);
        }
    }
}
