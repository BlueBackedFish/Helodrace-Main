using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace
{
    public enum HelodSniperSupportMode
    {
        Suppress,
        Kill
    }

    [StaticConstructorOnStartup]
    public static class HelodSniperSupportUtility
    {
        private const int AimTicks = 120;
        public const int MaxShots = 5;
        public const int HiddenCancelTicks = 30 * 60;
        public const int TrailDurationTicks = 36;
        private const int MinRetryTicks = 240;
        private const int MaxRetryTicks = 300;
        private const string SniperProjectileDefName = "HD_Bullet_SprMThree_Proj";
        private const string SniperWeaponDefName = "HD_Gun_M1903A4_Weapon";
        private const string HitSoundDefName = "HD_GunHit";
        private const float WrongPartChance = 0.08f;
        private const float MissChance = 0.07f;
        private const float MinSizedMissChance = 0.025f;
        private const float MaxSizedMissChance = 0.22f;
        private const float HighAccuracyHeadshotMissChance = 0.06f;
        private const float HeadshotArmorMargin = 0.025f;
        private const float SimilarSourceFalloffDistance = 14f;
        private const float FallbackSniperDamage = 20f;
        private const float FallbackSniperArmorPenetration = 0.30f;
        private static Thing sniperWeaponForStats;

        public static readonly Texture2D CommandIcon = ContentFinder<Texture2D>.Get("Icon/HD_Icon_Aimed", false) ?? BaseContent.BadTex;
        public static readonly Material AimMaterial = MaterialPool.MatFrom("Icon/HD_Icon_Aimed", ShaderDatabase.Transparent);
        public static readonly Material TrailGlowMaterial = SolidColorMaterials.SimpleSolidColorMaterial(new Color(1f, 0.62f, 0.18f, 0.50f), false);
        public static readonly Material TrailCoreMaterial = SolidColorMaterials.SimpleSolidColorMaterial(new Color(1f, 0.96f, 0.68f, 0.95f), false);

        public static void OpenSupportDialog(Map map)
        {
            OpenSupportDialog(map, null);
        }

        public static void OpenSupportDialog(Map map, Faction faction)
        {
            if (!HasSniperSupport(map, faction))
            {
                Messages.Message("HD_SniperSupport_Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(new Dialog_MessageBox(
                "HD_SniperSupport_ModePrompt".Translate(),
                "HD_SniperSupport_Suppress".Translate(),
                () => BeginTargeting(map, HelodSniperSupportMode.Suppress, faction),
                "HD_SniperSupport_Kill".Translate(),
                () => BeginTargeting(map, HelodSniperSupportMode.Kill, faction),
                null,
                true
            ));
        }

        public static bool HasSniperSupport(Map map)
        {
            return HasSniperSupport(map, null);
        }

        public static bool HasSniperSupport(Map map, Faction faction)
        {
            HelodForwardBase forwardBase;
            float distance;
            return TryFindSniperBase(map, faction, out forwardBase, out distance);
        }

        public static void BeginTargeting(Map map, HelodSniperSupportMode mode)
        {
            BeginTargeting(map, mode, null);
        }

        public static void BeginTargeting(Map map, HelodSniperSupportMode mode, Faction faction)
        {
            if (map == null)
            {
                return;
            }

            if (!HasSniperSupport(map, faction))
            {
                Messages.Message("HD_SniperSupport_Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Find.Targeter.BeginTargeting(TargetingParameters(map), target => TryCallSupport(map, target.Thing, mode, faction));
        }

        private static TargetingParameters TargetingParameters(Map map)
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetItems = false,
                canTargetLocations = false,
                validator = target => IsValidTarget(map, target.Thing)
            };
        }

        private static bool TryCallSupport(Map map, Thing target, HelodSniperSupportMode mode, Faction faction)
        {
            IntVec3 edgeCell;
            if (!IsValidTarget(map, target, out edgeCell))
            {
                Messages.Message("HD_SniperSupport_InvalidTarget".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            HelodForwardBase forwardBase;
            float distance;
            if (!TryFindSniperBase(map, faction, out forwardBase, out distance))
            {
                Messages.Message("HD_SniperSupport_Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            MapComponent_HelodSniperSupport sniperComponent = map.GetComponent<MapComponent_HelodSniperSupport>();
            if (sniperComponent.HasActiveStrikeOnTarget(target))
            {
                Messages.Message("Sniper support is already aiming at that target.", target, MessageTypeDefOf.RejectInput);
                return false;
            }

            if (forwardBase.ShouldRecordServiceUseOnCall(HelodForwardBaseService.InfantrySniperSupport)
                && !forwardBase.TryConsumeServiceUse(HelodForwardBaseService.InfantrySniperSupport, out string failReason))
            {
                Messages.Message(failReason ?? "HD_SniperSupport_Unavailable".Translate().ToString(), MessageTypeDefOf.RejectInput);
                return false;
            }

            sniperComponent.QueueStrike(target, mode, AimTicks, forwardBase);
            Messages.Message("HD_SniperSupport_Called".Translate(), target, MessageTypeDefOf.NeutralEvent);
            return true;
        }

        private static bool TryFindSniperBase(Map map, Faction faction, out HelodForwardBase forwardBase, out float distance)
        {
            return HelodForwardBaseServiceUtility.TryFindSupportingBase(HelodForwardBaseService.InfantrySniperSupport, map, faction, true, out forwardBase, out distance);
        }

        private static bool IsValidTarget(Map map, Thing target)
        {
            IntVec3 edgeCell;
            return IsValidTarget(map, target, out edgeCell);
        }

        public static bool CanMaintainTargetSight(Map map, Thing target)
        {
            return IsValidTarget(map, target);
        }

        private static bool IsValidTarget(Map map, Thing target, out IntVec3 edgeCell)
        {
            edgeCell = IntVec3.Invalid;
            if (map == null || target == null || !target.Spawned || target.Map != map || target.Position.Fogged(map))
            {
                return false;
            }

            Pawn pawn = target as Pawn;
            if (pawn != null && pawn.Dead)
            {
                return false;
            }

            if (!(target is Pawn) && !(target is Building))
            {
                return false;
            }

            return TryFindRandomEdgeSightCell(map, target, out edgeCell);
        }

        public static bool TryFindRandomSniperSourceCell(Map map, Thing target, out IntVec3 edgeCell)
        {
            return TryFindRandomEdgeSightCell(map, target, out edgeCell);
        }

        public static bool TryFindSimilarSniperSourceCell(Map map, Thing target, IntVec3 previousCell, out IntVec3 edgeCell)
        {
            if (!previousCell.IsValid)
            {
                return TryFindRandomEdgeSightCell(map, target, out edgeCell);
            }

            edgeCell = IntVec3.Invalid;
            List<IntVec3> edgeCells = EdgeSightCells(map, target);
            if (edgeCells.Count == 0)
            {
                return false;
            }

            edgeCell = edgeCells.RandomElementByWeight(cell => SimilarSourceWeight(previousCell, cell));
            return true;
        }

        private static bool TryFindRandomEdgeSightCell(Map map, Thing target, out IntVec3 edgeCell)
        {
            edgeCell = IntVec3.Invalid;
            List<IntVec3> edgeCells = EdgeSightCells(map, target);
            if (edgeCells.Count == 0)
            {
                return false;
            }

            edgeCell = edgeCells.RandomElement();
            return true;
        }

        private static float SimilarSourceWeight(IntVec3 previousCell, IntVec3 candidate)
        {
            float distance = Mathf.Sqrt(previousCell.DistanceToSquared(candidate));
            return 1f / Mathf.Pow(1f + distance / SimilarSourceFalloffDistance, 3f);
        }

        private static List<IntVec3> EdgeSightCells(Map map, Thing target)
        {
            List<IntVec3> cells = new List<IntVec3>();
            CellRect rect = CellRect.WholeMap(map);
            for (int z = rect.minZ; z <= rect.maxZ; z++)
            {
                TryAddEdgeSightCell(new IntVec3(rect.minX, 0, z), target, map, cells);
                TryAddEdgeSightCell(new IntVec3(rect.maxX, 0, z), target, map, cells);
            }

            for (int x = rect.minX + 1; x < rect.maxX; x++)
            {
                TryAddEdgeSightCell(new IntVec3(x, 0, rect.minZ), target, map, cells);
                TryAddEdgeSightCell(new IntVec3(x, 0, rect.maxZ), target, map, cells);
            }

            return cells;
        }

        private static void TryAddEdgeSightCell(IntVec3 from, Thing target, Map map, List<IntVec3> cells)
        {
            IntVec3 edgeCell;
            if (CanSeeTarget(from, target, map, out edgeCell))
            {
                cells.Add(edgeCell);
            }
        }

        private static bool CanSeeTarget(IntVec3 from, Thing target, Map map, out IntVec3 edgeCell)
        {
            edgeCell = IntVec3.Invalid;
            if (!from.InBounds(map) || from.Fogged(map))
            {
                return false;
            }

            if (!GenSight.LineOfSight(from, target.Position, map, true))
            {
                return false;
            }

            edgeCell = from;
            return true;
        }

        public static bool TryApplyShot(Thing target, HelodSniperSupportMode mode, Map map, out IntVec3 impactCell, out bool hitTarget)
        {
            impactCell = IntVec3.Invalid;
            hitTarget = false;
            if (target == null || target.Destroyed)
            {
                return false;
            }

            if (TryMissNearby(target, map, out impactCell))
            {
                return true;
            }

            impactCell = target.Position;
            Pawn pawn = target as Pawn;
            if (pawn != null)
            {
                bool shotApplied = TryApplyPawnShot(pawn, mode);
                hitTarget = shotApplied;
                return shotApplied;
            }

            target.TakeDamage(MakeSniperDamageInfo(null));
            hitTarget = true;
            return true;
        }

        public static void PlayHitSound(IntVec3 cell, Map map)
        {
            PlaySound(HitSoundDefName, cell, map);
        }

        private static void PlaySound(string defName, IntVec3 cell, Map map)
        {
            if (map == null || !cell.IsValid)
            {
                return;
            }

            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail(defName);
            sound?.PlayOneShot(new TargetInfo(cell, map));
        }

        public static bool ShouldContinueShooting(Thing target, HelodSniperSupportMode mode)
        {
            if (target == null || target.Destroyed || !target.Spawned)
            {
                return false;
            }

            Pawn pawn = target as Pawn;
            if (pawn == null)
            {
                return !target.Destroyed;
            }

            if (pawn.Dead)
            {
                return false;
            }

            return mode != HelodSniperSupportMode.Suppress || RandomBodyPart(pawn, "Leg") != null;
        }

        public static int NextShotDelayTicks()
        {
            return Rand.RangeInclusive(MinRetryTicks, MaxRetryTicks);
        }

        private static bool TryMissNearby(Thing target, Map map, out IntVec3 missCell)
        {
            missCell = IntVec3.Invalid;
            if (!Rand.Chance(MissChanceFor(target)))
            {
                return false;
            }

            for (int i = 0; i < 12; i++)
            {
                IntVec3 cell = target.Position + GenRadial.RadialPattern[Rand.RangeInclusive(1, 8)];
                if (cell.InBounds(map) && !cell.Fogged(map))
                {
                    missCell = cell;
                    return true;
                }
            }

            missCell = target.Position;
            return true;
        }

        private static float MissChanceFor(Thing target)
        {
            float targetScale = Mathf.Max(0.15f, TargetShotScale(target));
            float sniperAccuracy = SniperAccuracy();
            return Mathf.Clamp(MissChance / (targetScale * sniperAccuracy), MinSizedMissChance, MaxSizedMissChance);
        }

        private static float TargetShotScale(Thing target)
        {
            Pawn pawn = target as Pawn;
            if (pawn != null)
            {
                return Mathf.Sqrt(Mathf.Max(0.15f, pawn.BodySize));
            }

            IntVec2 size = target?.def?.size ?? IntVec2.One;
            return Mathf.Sqrt(Mathf.Max(0.25f, size.x * size.z));
        }

        private static float SniperAccuracy()
        {
            Thing weapon = SniperWeaponForStats();
            if (weapon == null)
            {
                return 1f;
            }

            return Mathf.Clamp(weapon.GetStatValue(StatDefOf.AccuracyLong), 0.1f, 2f);
        }

        private static bool TryApplyPawnShot(Pawn pawn, HelodSniperSupportMode mode)
        {
            if (pawn.Dead)
            {
                return false;
            }

            if (mode == HelodSniperSupportMode.Suppress)
            {
                BodyPartRecord leg = RandomBodyPart(pawn, "Leg");
                if (leg == null)
                {
                    return false;
                }

                pawn.TakeDamage(MakeSniperDamageInfo(RandomWrongPart(pawn, leg) ?? leg));
                return true;
            }

            BodyPartRecord targetPart = KillShotTargetPart(pawn);
            pawn.TakeDamage(MakeSniperDamageInfo(RandomWrongPart(pawn, targetPart) ?? targetPart));
            return true;
        }

        private static BodyPartRecord KillShotTargetPart(Pawn pawn)
        {
            if (ShouldTryHeadshot(pawn))
            {
                BodyPartRecord head = RandomBodyPart(pawn, "Head");
                if (head != null)
                {
                    return head;
                }
            }

            BodyPartRecord vital = RandomKillVitalPart(pawn);
            if (vital != null)
            {
                return vital;
            }

            return RandomBodyPart(pawn, "Torso") ?? RandomBodyPart(pawn, "Head");
        }

        private static bool ShouldTryHeadshot(Pawn pawn)
        {
            BodyPartRecord head = RandomBodyPart(pawn, "Head");
            if (head == null)
            {
                return false;
            }

            bool highAccuracy = MissChanceFor(pawn) <= HighAccuracyHeadshotMissChance;
            bool chestArmorStronger = ArmorRatingForPart(pawn, RandomBodyPart(pawn, "Torso")) > ArmorRatingForPart(pawn, head) + HeadshotArmorMargin;
            if (!highAccuracy && !chestArmorStronger)
            {
                return false;
            }

            float chance = chestArmorStronger ? 0.65f : 0.35f;
            if (highAccuracy && chestArmorStronger)
            {
                chance = 0.80f;
            }

            return Rand.Chance(chance);
        }

        private static BodyPartRecord RandomKillVitalPart(Pawn pawn)
        {
            if (pawn.RaceProps?.body?.AllParts == null)
            {
                return null;
            }

            return pawn.RaceProps.body.AllParts
                .Where(part => IsAvailableKillVital(pawn, part))
                .RandomElementByWeightWithFallback(KillVitalWeight);
        }

        private static bool IsAvailableKillVital(Pawn pawn, BodyPartRecord part)
        {
            if (part == null || pawn.health.hediffSet.PartIsMissing(part))
            {
                return false;
            }

            string defName = part.def?.defName;
            return defName == "Heart" || defName == "Lung" || defName == "Liver" || defName == "Kidney";
        }

        private static float KillVitalWeight(BodyPartRecord part)
        {
            string defName = part.def?.defName;
            if (defName == "Heart")
            {
                return 5f;
            }

            if (defName == "Lung")
            {
                return 3f;
            }

            if (defName == "Liver")
            {
                return 1.5f;
            }

            if (defName == "Kidney")
            {
                return 1f;
            }

            return 0.1f;
        }

        private static float ArmorRatingForPart(Pawn pawn, BodyPartRecord part)
        {
            if (pawn?.apparel?.WornApparel == null || part == null)
            {
                return 0f;
            }

            float armor = 0f;
            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                Apparel apparel = wornApparel[i];
                if (apparel?.def?.apparel != null && apparel.def.apparel.CoversBodyPart(part))
                {
                    armor += apparel.GetStatValue(StatDefOf.ArmorRating_Sharp);
                }
            }

            return armor;
        }

        private static DamageInfo MakeSniperDamageInfo(BodyPartRecord hitPart)
        {
            ThingDef projectileDef = DefDatabase<ThingDef>.GetNamedSilentFail(SniperProjectileDefName);
            ProjectileProperties projectile = projectileDef?.projectile;
            DamageDef damageDef = projectile?.damageDef ?? DamageDefOf.Bullet;
            Thing weapon = SniperWeaponForStats();
            float damage = projectile?.GetDamageAmount(weapon) ?? FallbackSniperDamage;
            float armorPenetration = projectile?.GetArmorPenetration(weapon) ?? FallbackSniperArmorPenetration;
            return new DamageInfo(damageDef, damage, armorPenetration, -1f, null, hitPart);
        }

        private static Thing SniperWeaponForStats()
        {
            if (sniperWeaponForStats != null)
            {
                return sniperWeaponForStats;
            }

            ThingDef weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(SniperWeaponDefName);
            if (weaponDef == null)
            {
                return null;
            }

            sniperWeaponForStats = ThingMaker.MakeThing(weaponDef);
            return sniperWeaponForStats;
        }

        private static BodyPartRecord RandomBodyPart(Pawn pawn, string defName)
        {
            if (pawn.RaceProps?.body?.AllParts == null)
            {
                return null;
            }

            return pawn.RaceProps.body.AllParts
                .Where(part => part?.def?.defName == defName && !pawn.health.hediffSet.PartIsMissing(part))
                .RandomElementWithFallback();
        }

        private static BodyPartRecord RandomWrongPart(Pawn pawn, BodyPartRecord intendedPart)
        {
            if (!Rand.Chance(WrongPartChance) || pawn.RaceProps?.body?.AllParts == null)
            {
                return null;
            }

            return pawn.RaceProps.body.AllParts
                .Where(part => part != null && part != intendedPart && !pawn.health.hediffSet.PartIsMissing(part))
                .RandomElementWithFallback();
        }
    }

    public class MapComponent_HelodSniperSupport : MapComponent
    {
        private List<SniperSupportEffect> effects = new List<SniperSupportEffect>();
        private List<SniperSupportTrail> trails = new List<SniperSupportTrail>();

        public MapComponent_HelodSniperSupport(Map map) : base(map)
        {
        }

        public bool HasActiveStrikeOnTarget(Thing target)
        {
            if (target == null)
            {
                return false;
            }

            for (int i = 0; i < effects.Count; i++)
            {
                SniperSupportEffect effect = effects[i];
                if (effect.Target == target && !effect.Finished)
                {
                    return true;
                }
            }

            return false;
        }

        public void QueueStrike(Thing target, HelodSniperSupportMode mode, int ticks, HelodForwardBase forwardBase)
        {
            if (target == null || forwardBase == null)
            {
                return;
            }

            int startTick = Find.TickManager.TicksGame;
            IntVec3 sourceCell;
            HelodSniperSupportUtility.TryFindRandomSniperSourceCell(map, target, out sourceCell);
            effects.Add(new SniperSupportEffect(target, mode, startTick, startTick + ticks, HelodSniperSupportUtility.MaxShots, forwardBase, sourceCell));
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            for (int i = trails.Count - 1; i >= 0; i--)
            {
                if (trails[i].Expired)
                {
                    trails.RemoveAt(i);
                }
            }

            for (int i = effects.Count - 1; i >= 0; i--)
            {
                SniperSupportEffect effect = effects[i];
                if (!effect.ImpactApplied && (effect.Target == null || effect.Target.Destroyed || !effect.Target.Spawned || effect.Target.Map != map))
                {
                    effects.RemoveAt(i);
                    continue;
                }

                if (!effect.UpdateTargetSight(map))
                {
                    Messages.Message("HD_SniperSupport_LostTarget".Translate(), MessageTypeDefOf.NeutralEvent);
                    effects.RemoveAt(i);
                    continue;
                }

                if (effect.TargetHidden)
                {
                    continue;
                }

                if (!effect.ImpactApplied && Find.TickManager.TicksGame >= effect.FireTick)
                {
                    if (!effect.TryConsumeShotUse(out string failReason))
                    {
                        Messages.Message(failReason ?? "HD_SniperSupport_Unavailable".Translate().ToString(), MessageTypeDefOf.RejectInput);
                        effects.RemoveAt(i);
                        continue;
                    }

                    IntVec3 impactCell;
                    bool hitTarget;
                    bool shotFired = HelodSniperSupportUtility.TryApplyShot(effect.Target, effect.Mode, map, out impactCell, out hitTarget);
                    if (!shotFired)
                    {
                        effects.RemoveAt(i);
                        continue;
                    }

                    effect.RecordImpactCell(impactCell);
                    if (effect.SourceCell.IsValid)
                    {
                        trails.Add(new SniperSupportTrail(effect.SourceCell, effect.ImpactCell, Find.TickManager.TicksGame, HelodSniperSupportUtility.TrailDurationTicks));
                    }

                    HelodSniperSupportUtility.PlayHitSound(effect.ImpactCell, map);
                    if (hitTarget)
                    {
                        FleckMaker.ThrowMicroSparks(effect.ImpactCell.ToVector3Shifted(), map);
                    }
                    else
                    {
                        FleckMaker.Static(effect.ImpactCell, map, FleckDefOf.ShotHit_Dirt);
                    }
                    effect.MarkImpactApplied();
                    if (effect.CanScheduleNextShot())
                    {
                        effect.ScheduleNextShot(HelodSniperSupportUtility.NextShotDelayTicks());
                    }
                    else
                    {
                        effects.RemoveAt(i);
                    }
                }
            }
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            for (int i = 0; i < trails.Count; i++)
            {
                DrawTrail(trails[i]);
            }

            for (int i = 0; i < effects.Count; i++)
            {
                SniperSupportEffect effect = effects[i];
                if (effect.ImpactApplied || effect.TargetHidden || !effect.IsAiming || effect.Target == null || effect.Target.Destroyed || !effect.Target.Spawned || effect.Target.Map != map)
                {
                    continue;
                }

                DrawAimIcon(effect);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref effects, "helodSniperSupportEffects", LookMode.Deep);
            if (effects == null)
            {
                effects = new List<SniperSupportEffect>();
            }

            Scribe_Collections.Look(ref trails, "helodSniperSupportTrails", LookMode.Deep);
            if (trails == null)
            {
                trails = new List<SniperSupportTrail>();
            }
        }

        private static void DrawTrail(SniperSupportTrail trail)
        {
            if (trail == null || !trail.Valid)
            {
                return;
            }

            Vector3 start = trail.Source.ToVector3Shifted();
            Vector3 end = trail.Impact.ToVector3Shifted();
            start.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            end.y = start.y;
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.01f)
            {
                return;
            }

            Vector3 center = (start + end) * 0.5f;
            float angle = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            DrawTrailLayer(center, angle, length, Mathf.Lerp(0.18f, 0.07f, trail.Progress), HelodSniperSupportUtility.TrailGlowMaterial);
            DrawTrailLayer(center, angle, length, Mathf.Lerp(0.07f, 0.025f, trail.Progress), HelodSniperSupportUtility.TrailCoreMaterial);
        }

        private static void DrawTrailLayer(Vector3 center, float angle, float length, float width, Material material)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(center, Quaternion.AngleAxis(angle, Vector3.up), new Vector3(width, 1f, length));
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }

        private static void DrawAimIcon(SniperSupportEffect effect)
        {
            Thing target = effect.Target;
            Vector3 pos = target.DrawPos;
            pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            float progress = effect.Progress;
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            float angle = Mathf.Lerp(0f, 450f, eased);
            float scale = Mathf.Lerp(1.15f, 0.85f, Mathf.SmoothStep(0f, 1f, progress));
            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.AngleAxis(angle, Vector3.up), Vector3.one * scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, HelodSniperSupportUtility.AimMaterial, 0);
        }
    }

    public class SniperSupportEffect : IExposable
    {
        private Thing target;
        private HelodSniperSupportMode mode;
        private int startTick;
        private int fireTick;
        private int shotsFired;
        private int maxShots = 5;
        private bool impactApplied;
        private IntVec3 impactCell = IntVec3.Invalid;
        private HelodForwardBase forwardBase;
        private int hiddenSinceTick = -1;
        private IntVec3 sourceCell = IntVec3.Invalid;

        public Thing Target => target;
        public HelodSniperSupportMode Mode => mode;
        public int FireTick => fireTick;
        public bool ImpactApplied => impactApplied;
        public IntVec3 ImpactCell => impactCell;
        public IntVec3 SourceCell => sourceCell;
        public bool Finished => target == null || target.Destroyed || !target.Spawned || impactApplied;
        public bool TargetHidden => hiddenSinceTick >= 0;
        public bool IsAiming => Find.TickManager.TicksGame >= startTick;
        public bool CanScheduleNextShot()
        {
            return shotsFired < maxShots
                && HelodSniperSupportUtility.ShouldContinueShooting(target, mode)
                && (forwardBase == null
                    || !forwardBase.ShouldRecordServiceUseOnExecution(HelodForwardBaseService.InfantrySniperSupport)
                    || forwardBase.HasServiceCapacity(HelodForwardBaseService.InfantrySniperSupport));
        }

        public float Progress => Mathf.Clamp01((Find.TickManager.TicksGame - startTick) / Mathf.Max(1f, fireTick - startTick));

        public SniperSupportEffect()
        {
        }

        public SniperSupportEffect(Thing target, HelodSniperSupportMode mode, int startTick, int fireTick, int maxShots, HelodForwardBase forwardBase, IntVec3 sourceCell)
        {
            this.target = target;
            this.mode = mode;
            this.startTick = startTick;
            this.fireTick = fireTick;
            this.maxShots = maxShots;
            this.forwardBase = forwardBase;
            this.sourceCell = sourceCell;
        }

        public bool UpdateTargetSight(Map map)
        {
            bool visible = HelodSniperSupportUtility.CanMaintainTargetSight(map, target);
            if (visible)
            {
                hiddenSinceTick = -1;
                return true;
            }

            int now = Find.TickManager.TicksGame;
            if (hiddenSinceTick < 0)
            {
                hiddenSinceTick = now;
            }

            startTick++;
            fireTick++;
            return now - hiddenSinceTick < HelodSniperSupportUtility.HiddenCancelTicks;
        }

        public bool TryConsumeShotUse(out string failReason)
        {
            failReason = null;
            if (forwardBase == null)
            {
                failReason = "HD_SniperSupport_Unavailable".Translate().ToString();
                return false;
            }

            if (!forwardBase.ShouldRecordServiceUseOnExecution(HelodForwardBaseService.InfantrySniperSupport))
            {
                return true;
            }

            return forwardBase.TryConsumeServiceUse(HelodForwardBaseService.InfantrySniperSupport, out failReason);
        }

        public void RecordImpactCell(IntVec3 cell)
        {
            impactCell = cell.IsValid ? cell : target?.Position ?? IntVec3.Invalid;
        }

        public void MarkImpactApplied()
        {
            impactApplied = true;
            shotsFired++;
        }

        public void ScheduleNextShot(int delayTicks)
        {
            startTick = Find.TickManager.TicksGame + delayTicks;
            fireTick = startTick + 120;
            impactApplied = false;
            impactCell = IntVec3.Invalid;
            if (target?.Map != null)
            {
                IntVec3 newSourceCell;
                if (HelodSniperSupportUtility.TryFindSimilarSniperSourceCell(target.Map, target, sourceCell, out newSourceCell))
                {
                    sourceCell = newSourceCell;
                }
            }
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_References.Look(ref forwardBase, "forwardBase");
            Scribe_Values.Look(ref mode, "mode");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref fireTick, "fireTick");
            Scribe_Values.Look(ref shotsFired, "shotsFired");
            Scribe_Values.Look(ref maxShots, "maxShots", 5);
            Scribe_Values.Look(ref impactApplied, "impactApplied");
            Scribe_Values.Look(ref impactCell, "impactCell");
            Scribe_Values.Look(ref hiddenSinceTick, "hiddenSinceTick", -1);
            Scribe_Values.Look(ref sourceCell, "sourceCell");
            if (startTick <= 0 || startTick > fireTick)
            {
                startTick = fireTick - 120;
            }
        }
    }

    public class SniperSupportTrail : IExposable
    {
        private IntVec3 source = IntVec3.Invalid;
        private IntVec3 impact = IntVec3.Invalid;
        private int startTick;
        private int endTick;

        public IntVec3 Source => source;
        public IntVec3 Impact => impact;
        public bool Valid => source.IsValid && impact.IsValid;
        public bool Expired => Find.TickManager.TicksGame >= endTick;
        public float Progress => Mathf.Clamp01((Find.TickManager.TicksGame - startTick) / Mathf.Max(1f, endTick - startTick));

        public SniperSupportTrail()
        {
        }

        public SniperSupportTrail(IntVec3 source, IntVec3 impact, int startTick, int durationTicks)
        {
            this.source = source;
            this.impact = impact;
            this.startTick = startTick;
            this.endTick = startTick + durationTicks;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref source, "source");
            Scribe_Values.Look(ref impact, "impact");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref endTick, "endTick");
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_SniperSupport
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance?.Faction != Faction.OfPlayer || __instance.Map == null || Find.Selector.SingleSelectedThing != __instance)
            {
                return;
            }

            List<Gizmo> updated = __result.ToList();
            updated.Add(SniperSupportCommand(__instance.Map, HelodSniperSupportMode.Suppress));
            updated.Add(SniperSupportCommand(__instance.Map, HelodSniperSupportMode.Kill));
            __result = updated;
        }

        private static Command_Action SniperSupportCommand(Map map, HelodSniperSupportMode mode)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel = mode == HelodSniperSupportMode.Kill ? "HD_SniperSupport_Kill".Translate() : "HD_SniperSupport_Suppress".Translate(),
                defaultDesc = "HD_SniperSupport_CommandDesc".Translate(),
                icon = HelodSniperSupportUtility.CommandIcon,
                action = () => HelodSniperSupportUtility.BeginTargeting(map, mode)
            };

            if (!HelodSniperSupportUtility.HasSniperSupport(map))
            {
                command.Disable("HD_SniperSupport_Unavailable".Translate());
            }

            return command;
        }
    }
}
