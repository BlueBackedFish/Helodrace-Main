using System.Collections.Generic;
using System.Linq;
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
        public const int AimTicks = 120;
        public const int HiddenCancelTicks = 30 * 60;
        public const int TrailDurationTicks = 36;
        private const string SniperProjectileDefName = "HD_Bullet_SprMThree_Proj";
        private const string SniperWeaponDefName = "HD_Gun_M1903A4_Weapon";
        private const string HitSoundDefName = "HD_Snipe";
        private const string DistantFireSoundDefName = "HD_DistantFired";
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

        public static readonly Material AimMaterial = MaterialPool.MatFrom("Icons/HD_Icon_Aimed", ShaderDatabase.Transparent);
        public static readonly Material TrailGlowMaterial = SolidColorMaterials.SimpleSolidColorMaterial(new Color(1f, 0.62f, 0.18f, 0.50f), false);
        public static readonly Material TrailCoreMaterial = SolidColorMaterials.SimpleSolidColorMaterial(new Color(1f, 0.96f, 0.68f, 0.95f), false);

        public static string DirectionLabel(HelodSniperSupportDirection direction)
        {
            return ("HD_SniperSupport_Direction_" + direction).Translate().ToString();
        }

        public static void OpenSupportDialog(Map map, HelodForwardBase forwardBase,
            CompTelegraphTable telegraphComp = null, Pawn caller = null)
        {
            if (!IsBaseInRange(map, forwardBase))
            {
                Messages.Message("HD_SniperSupport_Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            Find.WindowStack.Add(new Dialog_HelodSniperControl(map, forwardBase, telegraphComp, caller));
        }

        public static bool IsBaseInRange(Map map, HelodForwardBase forwardBase)
        {
            if (map == null || forwardBase == null || forwardBase.Tile < 0
                || !forwardBase.HasService(HelodForwardBaseService.InfantrySniperSupport)) return false;
            int tile = map.Tile >= 0 ? map.Tile : map.Parent?.Tile ?? -1;
            return tile >= 0 && Find.WorldGrid.ApproxDistanceInTiles(forwardBase.Tile, tile)
                <= HelodForwardBaseServiceUtility.SupportRange(HelodForwardBaseService.InfantrySniperSupport);
        }

        public static string RequestRejection(Map map, HelodForwardBase forwardBase,
            CompTelegraphTable telegraphComp, Pawn caller)
        {
            if (!IsBaseInRange(map, forwardBase)
                || !forwardBase.HasServiceCapacity(HelodForwardBaseService.InfantrySniperSupport))
                return "HD_SniperSupport_Unavailable".Translate().ToString();
            string transmissionFailure = telegraphComp?.TransmissionFailure(forwardBase?.Faction);
            if (transmissionFailure != null) return transmissionFailure;
            if (caller != null && (caller.Dead || caller.Downed || !caller.Spawned || caller.Map != map))
                return "HD_SniperSupport_CallerUnavailable".Translate().ToString();
            if (caller != null && SCR300RadioUtility.IsBlackout(map))
                return "HD_SCR300_SolarFlare".Translate().ToString();
            return null;
        }

        public static void BeginTargeting(Map map, HelodSniperSupportMode mode,
            HelodForwardBase forwardBase, CompTelegraphTable telegraphComp, Pawn caller)
        {
            string rejection = RequestRejection(map, forwardBase, telegraphComp, caller);
            if (rejection != null)
            {
                Messages.Message(rejection, MessageTypeDefOf.RejectInput);
                return;
            }
            MapComponent_HelodSniperSupport support = map.GetComponent<MapComponent_HelodSniperSupport>();
            HelodSniperSupportSession session = support.GetSession(forwardBase);
            if (session?.CanRequestShot(Find.TickManager.TicksGame) != true || support.HasActiveStrike(forwardBase))
            {
                Messages.Message("HD_SniperSupport_NotReady".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            Current.Game.CurrentMap = map;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            CameraJumper.TryJump(new TargetInfo(caller?.Position ?? map.Center, map));
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetItems = false,
                canTargetLocations = false,
                validator = target => IsValidTarget(map, target.Thing, session.Direction)
            }, target =>
            {
                if (support.TryQueueStrike(target.Thing, mode, session, telegraphComp, caller))
                    Messages.Message("HD_SniperSupport_Called".Translate(), target.Thing, MessageTypeDefOf.NeutralEvent);
            });
        }

        public static bool IsValidTarget(Map map, Thing target, HelodSniperSupportDirection direction)
        {
            return IsEligibleTarget(map, target) && EdgeSightCells(map, target, direction).Count > 0;
        }

        public static bool IsEligibleTarget(Map map, Thing target)
        {
            return map != null && target != null && !target.Destroyed && target.Spawned
                && target.Map == map && !target.Position.Fogged(map)
                && (target is Pawn pawn ? !pawn.Dead : target is Building)
                && target.HostileTo(Faction.OfPlayer);
        }

        public static bool TryFindSniperSourceCell(Map map, Thing target,
            HelodSniperSupportDirection direction, IntVec3 previousCell, out IntVec3 edgeCell)
        {
            edgeCell = IntVec3.Invalid;
            if (!IsEligibleTarget(map, target)) return false;
            if (HelodSniperSupportSession.IsOnEdge(previousCell, map.Size, direction)
                && CanSeeTarget(previousCell, target, map, out edgeCell)) return true;
            List<IntVec3> edgeCells = EdgeSightCells(map, target, direction);
            if (edgeCells.Count == 0) return false;
            edgeCell = previousCell.IsValid
                ? edgeCells.RandomElementByWeight(cell => SimilarSourceWeight(previousCell, cell))
                : edgeCells.RandomElement();
            return true;
        }

        private static float SimilarSourceWeight(IntVec3 previousCell, IntVec3 candidate)
        {
            float distance = Mathf.Sqrt(previousCell.DistanceToSquared(candidate));
            return 1f / Mathf.Pow(1f + distance / SimilarSourceFalloffDistance, 3f);
        }

        private static List<IntVec3> EdgeSightCells(Map map, Thing target, HelodSniperSupportDirection direction)
        {
            List<IntVec3> cells = new List<IntVec3>();
            if (!IsEligibleTarget(map, target)) return cells;
            foreach (IntVec3 cell in HelodSniperSupportSession.EdgeCells(map.Size, direction))
            {
                TryAddEdgeSightCell(cell, target, map, cells);
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

        public static void PlayDistantFireSound(IntVec3 cell, Map map)
        {
            PlaySound(DistantFireSoundDefName, cell, map);
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
        private const int DistantFireDelayTicks = 30;
        private List<SniperSupportEffect> effects = new List<SniperSupportEffect>();
        private List<SniperSupportTrail> trails = new List<SniperSupportTrail>();
        private List<SniperSupportDelayedSound> delayedSounds = new List<SniperSupportDelayedSound>();
        private List<HelodSniperSupportSession> sessions = new List<HelodSniperSupportSession>();

        public MapComponent_HelodSniperSupport(Map map) : base(map)
        {
        }

        public HelodSniperSupportSession GetSession(HelodForwardBase forwardBase)
        {
            return sessions.FirstOrDefault(session => session.ForwardBase == forwardBase);
        }

        public bool HasActiveStrike(HelodForwardBase forwardBase)
        {
            return effects.Any(effect => effect.ForwardBase == forwardBase);
        }

        public bool TryRequestSupport(HelodForwardBase forwardBase, HelodSniperSupportDirection direction,
            CompTelegraphTable telegraphComp = null, Pawn caller = null)
        {
            int now = Find.TickManager.TicksGame;
            if (!System.Enum.IsDefined(typeof(HelodSniperSupportDirection), direction)
                || HelodSniperSupportUtility.RequestRejection(map, forwardBase, telegraphComp, caller) != null
                || GetSession(forwardBase)?.CanRequestShot(now) == true || HasActiveStrike(forwardBase)) return false;
            sessions.RemoveAll(session => session.ForwardBase == forwardBase);
            sessions.Add(new HelodSniperSupportSession(forwardBase, direction, now));
            return true;
        }

        public bool TryQueueStrike(Thing target, HelodSniperSupportMode mode, HelodSniperSupportSession session,
            CompTelegraphTable telegraphComp = null, Pawn caller = null)
        {
            string rejection = HelodSniperSupportUtility.RequestRejection(map, session?.ForwardBase, telegraphComp, caller);
            if (rejection != null)
            {
                Messages.Message(rejection, MessageTypeDefOf.RejectInput);
                return false;
            }
            int now = Find.TickManager.TicksGame;
            if (!sessions.Contains(session) || !session.CanRequestShot(now) || HasActiveStrike(session.ForwardBase))
            {
                Messages.Message("HD_SniperSupport_NotReady".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (effects.Any(effect => effect.Target == target))
            {
                Messages.Message("HD_SniperSupport_DuplicateTarget".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (!HelodSniperSupportUtility.TryFindSniperSourceCell(map, target, session.Direction,
                IntVec3.Invalid, out IntVec3 sourceCell))
            {
                Messages.Message("HD_SniperSupport_InvalidTarget".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            HelodForwardBase forwardBase = session.ForwardBase;
            if (forwardBase.ShouldRecordServiceUseOnCall(HelodForwardBaseService.InfantrySniperSupport)
                && !forwardBase.TryConsumeServiceUse(HelodForwardBaseService.InfantrySniperSupport, out string reason))
            {
                Messages.Message(reason ?? "HD_SniperSupport_Unavailable".Translate().ToString(), MessageTypeDefOf.RejectInput);
                return false;
            }
            session.TryConsumeAction(now);
            telegraphComp?.ConsumePrimaryCell();
            effects.Add(new SniperSupportEffect(target, mode, now, now + HelodSniperSupportUtility.AimTicks,
                forwardBase, session.Direction, sourceCell));
            return true;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            for (int i = delayedSounds.Count - 1; i >= 0; i--)
            {
                SniperSupportDelayedSound delayedSound = delayedSounds[i];
                if (Find.TickManager.TicksGame < delayedSound.PlayTick)
                {
                    continue;
                }

                HelodSniperSupportUtility.PlayDistantFireSound(delayedSound.Cell, map);
                delayedSounds.RemoveAt(i);
            }

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
                HelodSniperSupportSession session = GetSession(effect.ForwardBase);
                if (session == null || !session.IsWithinDuration(Find.TickManager.TicksGame)
                    || !HelodSniperSupportUtility.IsBaseInRange(map, effect.ForwardBase)
                    || !HelodSniperSupportUtility.IsEligibleTarget(map, effect.Target))
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

                if (Find.TickManager.TicksGame >= effect.FireTick)
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
                    delayedSounds.Add(new SniperSupportDelayedSound(effect.ImpactCell, Find.TickManager.TicksGame + DistantFireDelayTicks));
                    if (hitTarget)
                    {
                        FleckMaker.ThrowMicroSparks(effect.ImpactCell.ToVector3Shifted(), map);
                    }
                    else
                    {
                        FleckMaker.Static(effect.ImpactCell, map, FleckDefOf.ShotHit_Dirt);
                    }
                    effects.RemoveAt(i);
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
                if (effect.TargetHidden || !effect.IsAiming || effect.Target == null || effect.Target.Destroyed || !effect.Target.Spawned || effect.Target.Map != map)
                {
                    continue;
                }

                DrawAimIcon(effect);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref sessions, "helodSniperSupportSessions", LookMode.Deep);
            if (sessions == null) sessions = new List<HelodSniperSupportSession>();
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

            Scribe_Collections.Look(ref delayedSounds, "helodSniperSupportDelayedSounds", LookMode.Deep);
            if (delayedSounds == null)
            {
                delayedSounds = new List<SniperSupportDelayedSound>();
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

    public class SniperSupportDelayedSound : IExposable
    {
        private IntVec3 cell;
        private int playTick;

        public IntVec3 Cell => cell;
        public int PlayTick => playTick;

        public SniperSupportDelayedSound()
        {
        }

        public SniperSupportDelayedSound(IntVec3 cell, int playTick)
        {
            this.cell = cell;
            this.playTick = playTick;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref playTick, "playTick");
        }
    }

    public class SniperSupportEffect : IExposable
    {
        private Thing target;
        private HelodSniperSupportMode mode;
        private HelodSniperSupportDirection direction;
        private int startTick;
        private int fireTick;
        private IntVec3 impactCell = IntVec3.Invalid;
        private HelodForwardBase forwardBase;
        private int hiddenSinceTick = -1;
        private IntVec3 sourceCell = IntVec3.Invalid;

        public Thing Target => target;
        public HelodForwardBase ForwardBase => forwardBase;
        public HelodSniperSupportMode Mode => mode;
        public int FireTick => fireTick;
        public IntVec3 ImpactCell => impactCell;
        public IntVec3 SourceCell => sourceCell;
        public bool TargetHidden => hiddenSinceTick >= 0;
        public bool IsAiming => Find.TickManager.TicksGame >= startTick;
        public float Progress => Mathf.Clamp01((Find.TickManager.TicksGame - startTick) / Mathf.Max(1f, fireTick - startTick));

        public SniperSupportEffect() { }

        public SniperSupportEffect(Thing target, HelodSniperSupportMode mode, int startTick, int fireTick,
            HelodForwardBase forwardBase, HelodSniperSupportDirection direction, IntVec3 sourceCell)
        {
            this.target = target;
            this.mode = mode;
            this.startTick = startTick;
            this.fireTick = fireTick;
            this.forwardBase = forwardBase;
            this.direction = direction;
            this.sourceCell = sourceCell;
        }

        public bool UpdateTargetSight(Map map)
        {
            if (HelodSniperSupportUtility.TryFindSniperSourceCell(map, target, direction,
                sourceCell, out IntVec3 visibleSource))
            {
                sourceCell = visibleSource;
                hiddenSinceTick = -1;
                return true;
            }
            int now = Find.TickManager.TicksGame;
            if (hiddenSinceTick < 0) hiddenSinceTick = now;
            startTick++;
            fireTick++;
            return now - hiddenSinceTick < HelodSniperSupportUtility.HiddenCancelTicks;
        }

        public bool TryConsumeShotUse(out string failReason)
        {
            failReason = null;
            if (forwardBase == null || !forwardBase.HasService(HelodForwardBaseService.InfantrySniperSupport))
            {
                failReason = "HD_SniperSupport_Unavailable".Translate().ToString();
                return false;
            }
            return !forwardBase.ShouldRecordServiceUseOnExecution(HelodForwardBaseService.InfantrySniperSupport)
                || forwardBase.TryConsumeServiceUse(HelodForwardBaseService.InfantrySniperSupport, out failReason);
        }

        public void RecordImpactCell(IntVec3 cell)
        {
            impactCell = cell.IsValid ? cell : target?.Position ?? IntVec3.Invalid;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_References.Look(ref forwardBase, "forwardBase");
            Scribe_Values.Look(ref mode, "mode");
            Scribe_Values.Look(ref direction, "direction");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref fireTick, "fireTick");
            Scribe_Values.Look(ref impactCell, "impactCell");
            Scribe_Values.Look(ref hiddenSinceTick, "hiddenSinceTick", -1);
            Scribe_Values.Look(ref sourceCell, "sourceCell");
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

}
