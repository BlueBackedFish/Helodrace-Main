using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Helodrace
{
    public enum HelodCasAttackKind
    {
        Bombing,
        Strafing,
        Hydra70,
        AGR20A,
        Maverick,
        GBU31,
        GBU54
    }

    public enum HelodCasAircraftKind
    {
        P47,
        A10C
    }

    public enum HelodCasGuidanceMode
    {
        TalkOn,
        Flare,
        Laser
    }

    public sealed class HelodCasLaserDesignatorExtension : DefModExtension
    {
        public float baseGuidanceChance = 0.90f;
        public float weatherPenaltyFactor = 0.75f;
        public bool adjustsGbu54Scatter = true;
        public bool usesPilotSignalVisual;
        public string guidanceGraphicPath;
        public float guidanceGraphicSize = 0.8f;
        public float guidanceGraphicRotationOffset = -90f;
        public float guidanceGraphicForwardOffset = 0.28f;
        public float guidanceGraphicLateralOffset = 0.16f;
        public bool fixedGuidanceGraphicPosition;
    }

    public enum HelodCasRunState
    {
        Approaching,
        GoAround,
        Attacking
    }

    public enum HelodCasAircraftPhase
    {
        NotStarted,
        Entry,
        Dive,
        Recovery,
        AbortTurn,
        StrafeApproach,
        Strafing,
        StrafeExit,
        Complete,
        LevelAttack
    }

    public enum HelodCasAircraftTickEvent
    {
        None,
        ReleaseBombPair,
        StrafeBurst,
        Complete
    }

    public static class HelodCasSupportUtility
    {
        public const int EntryEdgeDepth = 5;
        public const float MajorScatterRadius = 8f;
        public const float MinorScatterRadius = 3f;
        public const int AircraftCount = 2;
        public const int BombsPerAircraft = 2;
        public const int P47Playtime = 4;
        public const int A10CPlaytime = 6;
        public const int PlaytimeDecayTicks = 2 * 2500;
        public const int GuidanceFailuresPerPlaytime = 3;
        public const int ArrivalDelayTicks = 5 * 60;
        public const int GoAroundMinimumTicks = 25 * 60;
        public const int GoAroundMaximumTicks = 35 * 60;
        public const int A10CGoAroundMinimumTicks = GoAroundMinimumTicks / 3;
        public const int A10CGoAroundMaximumTicks = GoAroundMaximumTicks / 3;
        public const int GoAroundTurnTicks = 45;
        public const int GoAroundExitTicks = 180;
        public const float GoAroundTurnRadius = 4f;
        public const float AbortTurnRadius = 14.32f;
        public const float AbortTurnAngleDegrees = 225f;
        public const float GoAroundExitSpeed = 0.60f;
        public const float AircraftAttackSpeed = 1.00f;
        public const float AircraftDiveMinimumSpeed = AircraftAttackSpeed * 0.82f;
        public const float AircraftRecoverySpeed = AircraftAttackSpeed;
        public const int AircraftRecoveryTicks = 4 * 60;
        public const float DiveDistance = 60f;
        public const float BombReleaseDistance = 30f;
        public const float SelfPropelledAttackDistance = 120f;
        public const float SelfPropelledReleaseDistance = 110f;
        public const float StrafeLength = 30f;
        public const float StrafeWidth = 2.5f;
        public const float GroundReferenceRadius = 10f;
        public const float StrafeApproachDistance = 15f;
        public const float A10CStrafeApproachDistance = 80f;
        public const float StrafeSpeedFactor = 0.97f;
        public const float StrafeMinimumScale = 0.96f;
        public const float StrafeTurnRadius = 14.32f;
        public const float StrafeExitTurnAngleDegrees = 210f;
        public const int StrafeRoundsPerBurst = 8;
        public const float StrafeRoundsPerMinute = 750f;
        public const float StrafeTicksPerBurst = 60f * 60f / StrafeRoundsPerMinute;
        public const float StrafeBulletLeadDistance = 15f;
        public const float A10CStrafeBulletLeadDistance = 30f;
        public const float A10CRoundsPerMinute = 4200f;
        public const float A10CRoundsPerTick = A10CRoundsPerMinute / (60f * 60f);
        public const int A10CStrafeRoundCount = 90;
        public const float A10CMuzzleForwardOffset = 3.5f;
        public const string StrafeProjectileDefName = "HD_Bullet_M2HB_CAS_Proj";
        public const string StrafeSoundDefName = "HD_M2Fire";
        public const int FollowupEntryIntervalTicks = 12 * 60;
        public const int CancellationLockBeforeReleaseTicks = 60;
        public const int BombFallTicks = 75;
        public const float MunitionDrawScale = 2.5f;
        public const float BombExplosionRadius = 6.2f;
        public const int BombDamage = 180;
        public const float BombArmorPenetration = 0.45f;
        public const float GBU54LaserScatterMultiplier = 0.5f;
        public const int IzlidSkySignalTicks = 2 * 60;
        public const int IzlidSignalPauseTicks = 30;
        public const int IzlidTargetSignalTicks = 3 * 60;
        public const int LaserBlinkIntervalTicks = 30;
        public const float IzlidSkyBeamLength = 42f;
        public const int IzlidSkyBeamSegments = 14;
        public const float TalkOnGoAroundBonus = 0.12f;
        public const float FlareGoAroundBonus = 0.08f;
        public const float TalkOnFlareAssistRadius = 10f;
        public const float SimilarPawnRadius = 8f;
        public const string AircraftTexturePath = "Effects/CAS/HD_P47_CAS";
        public const string BombTexturePath = "Effects/CAS/Bombs/HD_ANM64_proj";

        public static string AircraftLabel(HelodCasAircraftKind aircraftKind)
        {
            return aircraftKind == HelodCasAircraftKind.A10C ? "A-10C" : "P-47";
        }

        public static int Playtime(HelodCasAircraftKind aircraftKind)
        {
            return aircraftKind == HelodCasAircraftKind.A10C ? A10CPlaytime : P47Playtime;
        }

        public static int AircraftCountFor(HelodCasAircraftKind aircraftKind)
        {
            return aircraftKind == HelodCasAircraftKind.A10C ? 1 : AircraftCount;
        }

        public static float StrafeApproachDistanceFor(
            HelodCasAircraftKind aircraftKind)
        {
            return aircraftKind == HelodCasAircraftKind.A10C
                ? A10CStrafeApproachDistance : StrafeApproachDistance;
        }

        public static float StrafeBulletLeadDistanceFor(
            HelodCasAircraftKind aircraftKind)
        {
            return aircraftKind == HelodCasAircraftKind.A10C
                ? A10CStrafeBulletLeadDistance : StrafeBulletLeadDistance;
        }

        public static string AircraftTexture(HelodCasAircraftKind aircraftKind)
        {
            return aircraftKind == HelodCasAircraftKind.A10C
                ? "Effects/CAS/A10/HD_A10C" : AircraftTexturePath;
        }

        public static float AircraftDrawSize(HelodCasAircraftKind aircraftKind)
        {
            return aircraftKind == HelodCasAircraftKind.A10C ? 8.25f : 6.5f;
        }

        public static bool SupportsAttack(HelodCasAircraftKind aircraftKind,
            HelodCasAttackKind attackKind)
        {
            if (aircraftKind == HelodCasAircraftKind.P47)
            {
                return attackKind == HelodCasAttackKind.Bombing
                    || attackKind == HelodCasAttackKind.Strafing;
            }
            return attackKind != HelodCasAttackKind.Bombing;
        }

        public static void ScatterFor(HelodCasAircraftKind aircraftKind,
            HelodCasAttackKind attackKind, out float major, out float minor)
        {
            major = MajorScatterRadius;
            minor = MinorScatterRadius;
            if (aircraftKind != HelodCasAircraftKind.A10C)
            {
                return;
            }
            switch (attackKind)
            {
                case HelodCasAttackKind.Hydra70: major = 5f; minor = 2f; break;
                case HelodCasAttackKind.AGR20A: major = 2f; minor = 0.8f; break;
                case HelodCasAttackKind.Maverick: major = 1.5f; minor = 0.6f; break;
                case HelodCasAttackKind.GBU31: major = 2.5f; minor = 1f; break;
                case HelodCasAttackKind.GBU54: major = 1f; minor = 0.4f; break;
            }
        }

        public static int MunitionCount(HelodCasAttackKind attackKind)
        {
            switch (attackKind)
            {
                case HelodCasAttackKind.Hydra70: return 7;
                case HelodCasAttackKind.AGR20A: return 4;
                case HelodCasAttackKind.Maverick:
                case HelodCasAttackKind.GBU31:
                case HelodCasAttackKind.GBU54: return 1;
                default: return BombsPerAircraft;
            }
        }

        public static bool UsesSequentialTargets(HelodCasAttackKind attackKind)
        {
            return true;
        }

        public static bool UsesAttackCorridor(HelodCasAttackKind attackKind)
        {
            return attackKind == HelodCasAttackKind.Strafing
                || attackKind == HelodCasAttackKind.Hydra70;
        }

        public static bool UsesAreaFire(HelodCasAttackKind attackKind)
        {
            return attackKind == HelodCasAttackKind.Bombing
                || attackKind == HelodCasAttackKind.Strafing
                || attackKind == HelodCasAttackKind.Hydra70;
        }

        public static bool IsGuidedMissile(HelodCasAttackKind attackKind)
        {
            return attackKind == HelodCasAttackKind.AGR20A
                || attackKind == HelodCasAttackKind.Maverick;
        }

        public static bool IsSelfPropelledMunition(HelodCasAttackKind attackKind)
        {
            return attackKind == HelodCasAttackKind.Hydra70
                || attackKind == HelodCasAttackKind.AGR20A
                || attackKind == HelodCasAttackKind.Maverick;
        }

        public static bool TryGetBestLaserDesignator(Pawn pawn,
            out HelodCasLaserDesignatorExtension designator)
        {
            return TryGetBestLaserDesignatorThing(pawn, out _, out designator);
        }

        public static bool TryGetBestLaserDesignatorThing(Pawn pawn,
            out Thing designatorThing,
            out HelodCasLaserDesignatorExtension designator)
        {
            designatorThing = null;
            designator = null;
            if (pawn == null)
            {
                return false;
            }

            IEnumerable<Thing> equipment = pawn.equipment?.AllEquipmentListForReading
                ?? Enumerable.Empty<ThingWithComps>();
            IEnumerable<Thing> apparel = pawn.apparel?.WornApparel
                ?? Enumerable.Empty<Apparel>();
            IEnumerable<Thing> inventory = pawn.inventory?.innerContainer
                ?? Enumerable.Empty<Thing>();
            IEnumerable<Thing> carried = pawn.carryTracker?.CarriedThing != null
                ? new[] { pawn.carryTracker.CarriedThing }
                : Enumerable.Empty<Thing>();
            foreach (Thing thing in equipment.Concat(apparel).Concat(inventory)
                .Concat(carried))
            {
                HelodCasLaserDesignatorExtension candidate = LaserDesignatorExtension(
                    thing?.def);
                if (candidate != null && (designator == null
                    || candidate.baseGuidanceChance > designator.baseGuidanceChance))
                {
                    designatorThing = thing;
                    designator = candidate;
                }
            }
            return designatorThing != null;
        }

        public static bool CanLaserDesignate(Pawn caller, Map map, IntVec3 target)
        {
            return TryGetLaserDesignatorOperator(caller, map, target, out _, out _);
        }

        public static bool TryGetLaserDesignatorOperator(Pawn caller, Map map,
            IntVec3 target, out Pawn designatorPawn,
            out HelodCasLaserDesignatorExtension designator)
        {
            designatorPawn = null;
            designator = null;
            if (map == null || !target.InBounds(map) || target.Fogged(map))
            {
                return false;
            }

            foreach (Pawn candidatePawn in LaserDesignatorOperators(caller))
            {
                HelodCasLaserDesignatorExtension candidate = PawnLaserDesignator(
                    candidatePawn);
                if (candidatePawn.Spawned && candidatePawn.Map == map
                    && !candidatePawn.Dead && !candidatePawn.Downed
                    && candidate != null
                    && SCR300RadioUtility.HasLineOfSight(candidatePawn, map, target)
                    && (designator == null
                        || candidate.baseGuidanceChance > designator.baseGuidanceChance))
                {
                    designatorPawn = candidatePawn;
                    designator = candidate;
                }
            }
            return designatorPawn != null;
        }

        private static IEnumerable<Pawn> LaserDesignatorOperators(Pawn caller)
        {
            if (caller == null)
            {
                yield break;
            }
            yield return caller;
        }

        private static HelodCasLaserDesignatorExtension PawnLaserDesignator(Pawn pawn)
        {
            TryGetBestLaserDesignatorThing(pawn, out _,
                out HelodCasLaserDesignatorExtension best);
            return best;
        }

        private static HelodCasLaserDesignatorExtension LaserDesignatorExtension(
            ThingDef def)
        {
            HelodCasLaserDesignatorExtension extension = def
                ?.GetModExtension<HelodCasLaserDesignatorExtension>();
            if (extension != null)
            {
                return extension;
            }

            switch (def?.defName)
            {
                case "HD_Apparel_ANPEQ1C":
                    return new HelodCasLaserDesignatorExtension
                    {
                        baseGuidanceChance = 0.97f,
                        weatherPenaltyFactor = 0.55f,
                        adjustsGbu54Scatter = true,
                        usesPilotSignalVisual = false,
                        guidanceGraphicPath =
                            "Weapons/ModernWar/ANPEQ1C/HD_ANPEQ1C",
                        guidanceGraphicSize = 1.1f,
                        guidanceGraphicRotationOffset = 0f,
                        guidanceGraphicForwardOffset = 0.12f,
                        guidanceGraphicLateralOffset = 0.75f,
                        fixedGuidanceGraphicPosition = true
                    };
                case "HD_Apparel_IZLIDUltra":
                    return new HelodCasLaserDesignatorExtension
                    {
                        baseGuidanceChance = 0.93f,
                        weatherPenaltyFactor = 0.72f,
                        adjustsGbu54Scatter = false,
                        usesPilotSignalVisual = true,
                        guidanceGraphicPath = "Weapons/ModernWar/HD_IZLIDUltra",
                        guidanceGraphicSize = 0.78f,
                        guidanceGraphicRotationOffset = -90f,
                        guidanceGraphicForwardOffset = 0f,
                        guidanceGraphicLateralOffset = 0f
                    };
                case "HD_Apparel_LA16uPEQ":
                    return new HelodCasLaserDesignatorExtension
                    {
                        baseGuidanceChance = 0.90f,
                        weatherPenaltyFactor = 0.85f,
                        adjustsGbu54Scatter = true,
                        usesPilotSignalVisual = false,
                        guidanceGraphicPath = "Weapons/ModernWar/HD_LA16uPEQ",
                        guidanceGraphicSize = 0.76f
                    };
                default:
                    return null;
            }
        }

        public static float WeatherGuidancePenalty(Map map)
        {
            if (map?.weatherManager == null)
            {
                return 0f;
            }
            float weatherAccuracy = map.weatherManager.curWeather
                ?.accuracyMultiplier ?? 1f;
            float visibilityLoss = Mathf.Clamp01(1f - weatherAccuracy);
            float rain = Mathf.Clamp01(map.weatherManager.RainRate);
            return Mathf.Clamp(visibilityLoss * 0.35f + rain * 0.10f,
                0f, 0.35f);
        }

        public static float AttackApproachDistance(HelodCasAttackKind attackKind)
        {
            return IsSelfPropelledMunition(attackKind)
                ? SelfPropelledAttackDistance : DiveDistance;
        }

        public static float MunitionReleaseDistance(HelodCasAttackKind attackKind)
        {
            return IsSelfPropelledMunition(attackKind)
                ? SelfPropelledReleaseDistance : BombReleaseDistance;
        }

        public static bool RequiresDive(HelodCasAircraftKind aircraftKind,
            HelodCasAttackKind attackKind)
        {
            if (aircraftKind == HelodCasAircraftKind.P47)
            {
                return attackKind == HelodCasAttackKind.Bombing;
            }
            return attackKind == HelodCasAttackKind.Hydra70
                || attackKind == HelodCasAttackKind.AGR20A
                || attackKind == HelodCasAttackKind.Maverick;
        }

        public static int TargetDesignationCount(HelodCasAttackKind attackKind)
        {
            return 1;
        }

        public static int InitialAmmo(HelodCasAircraftKind aircraftKind,
            HelodCasAttackKind attackKind)
        {
            if (aircraftKind == HelodCasAircraftKind.P47)
            {
                return attackKind == HelodCasAttackKind.Bombing ? 8
                    : attackKind == HelodCasAttackKind.Strafing ? 4 : 0;
            }
            switch (attackKind)
            {
                case HelodCasAttackKind.Strafing: return 4;
                case HelodCasAttackKind.Hydra70: return 28;
                case HelodCasAttackKind.AGR20A: return 8;
                case HelodCasAttackKind.Maverick: return 4;
                case HelodCasAttackKind.GBU31:
                case HelodCasAttackKind.GBU54: return 2;
                default: return 0;
            }
        }

        public static int AmmoCost(HelodCasAttackKind attackKind,
            int munitionCount, int aircraftCount)
        {
            int perAircraft = attackKind == HelodCasAttackKind.Strafing
                ? 1 : Mathf.Max(1, munitionCount);
            return perAircraft * Mathf.Max(1, aircraftCount);
        }

        public static int MunitionReleaseInterval(HelodCasAttackKind attackKind)
        {
            if (attackKind == HelodCasAttackKind.Hydra70)
            {
                return 2;
            }
            return attackKind == HelodCasAttackKind.AGR20A ? 3 : 0;
        }

        public static string MunitionTexture(HelodCasAttackKind attackKind)
        {
            switch (attackKind)
            {
                case HelodCasAttackKind.Hydra70: return "Effects/CAS/Bombs/HD_Hydra70_proj";
                case HelodCasAttackKind.AGR20A: return "Effects/CAS/Bombs/HD_AGR20A_proj";
                case HelodCasAttackKind.Maverick: return "Effects/CAS/Bombs/HD_Maverick_proj";
                case HelodCasAttackKind.GBU31: return "Effects/CAS/Bombs/HD_GBU31JDAM_proj";
                case HelodCasAttackKind.GBU54: return "Effects/CAS/Bombs/HD_GBU54LJDAM_proj";
                default: return BombTexturePath;
            }
        }

        public static void MunitionDamage(HelodCasAttackKind attackKind,
            out float radius, out int damage, out float armorPenetration)
        {
            radius = BombExplosionRadius;
            damage = BombDamage;
            armorPenetration = BombArmorPenetration;
            switch (attackKind)
            {
                case HelodCasAttackKind.Hydra70: radius = 2.2f; damage = 42; armorPenetration = 0.35f; break;
                case HelodCasAttackKind.AGR20A: radius = 2.4f; damage = 65; armorPenetration = 0.65f; break;
                case HelodCasAttackKind.Maverick: radius = 3.2f; damage = 210; armorPenetration = 3.0f; break;
                case HelodCasAttackKind.GBU31: radius = 7.5f; damage = 360; armorPenetration = 1.1f; break;
                case HelodCasAttackKind.GBU54: radius = 5.5f; damage = 260; armorPenetration = 0.9f; break;
            }
        }

        public static bool IsInRange(Map map, HelodForwardBase forwardBase)
        {
            if (map == null || forwardBase == null || forwardBase.Tile < 0)
            {
                return false;
            }

            int mapTile = map.Tile >= 0 ? map.Tile : map.Parent?.Tile ?? -1;
            return mapTile >= 0 && Find.WorldGrid.ApproxDistanceInTiles(forwardBase.Tile, mapTile)
                <= HelodForwardBaseServiceUtility.SupportRange(HelodForwardBaseService.CloseAirSupport);
        }

        public static bool CanUseBase(Map map, HelodForwardBase forwardBase)
        {
            return IsInRange(map, forwardBase)
                && forwardBase.HasService(HelodForwardBaseService.CloseAirSupport)
                && forwardBase.HasServiceCapacity(HelodForwardBaseService.CloseAirSupport);
        }

        public static void BeginTalkOnTargeting(Map map, HelodForwardBase forwardBase,
            Pawn caller, HelodCasAttackKind attackKind = HelodCasAttackKind.Bombing,
            HelodCasAircraftKind aircraftKind = HelodCasAircraftKind.P47)
        {
            BeginRouteTargeting(map, forwardBase, caller, HelodCasGuidanceMode.TalkOn,
                attackKind, aircraftKind);
        }

        public static void BeginFlareTargeting(Map map, HelodForwardBase forwardBase,
            Pawn caller, HelodCasAttackKind attackKind = HelodCasAttackKind.Bombing,
            HelodCasAircraftKind aircraftKind = HelodCasAircraftKind.P47)
        {
            if (!CasFlareTargetUtility.ActiveFlares(map).Any())
            {
                Messages.Message("HD_CAS_NoActiveFlare".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            BeginRouteTargeting(map, forwardBase, caller, HelodCasGuidanceMode.Flare,
                attackKind, aircraftKind);
        }

        public static void BeginLaserTargeting(Map map, HelodForwardBase forwardBase,
            Pawn caller, HelodCasAttackKind attackKind,
            HelodCasAircraftKind aircraftKind)
        {
            if (!TryGetBestLaserDesignator(caller, out _))
            {
                Messages.Message("HD_CAS_LaserUnavailable".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }
            BeginRouteTargeting(map, forwardBase, caller, HelodCasGuidanceMode.Laser,
                attackKind, aircraftKind);
        }

        private static void BeginRouteTargeting(Map map, HelodForwardBase forwardBase,
            Pawn caller, HelodCasGuidanceMode guidanceMode, HelodCasAttackKind attackKind,
            HelodCasAircraftKind aircraftKind)
        {
            if (!CanUseBase(map, forwardBase) || caller == null || caller.Map != map
                || SCR300RadioUtility.IsBlackout(map))
            {
                Messages.Message("HD_CAS_Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (guidanceMode == HelodCasGuidanceMode.Laser)
            {
                if (!TryGetBestLaserDesignator(caller, out _))
                {
                    Messages.Message("HD_CAS_LaserUnavailable".Translate(),
                        MessageTypeDefOf.RejectInput);
                    return;
                }
            }

            Find.WorldTargeter.StopTargeting();
            Find.Targeter.StopTargeting();
            Find.World.renderer.wantedMode = WorldRenderMode.None;
            Current.Game.CurrentMap = map;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            CameraJumper.TryJump(new TargetInfo(caller.Position, map));
            map.GetComponent<MapComponent_HelodCasSupport>()
                .BeginRouteTargeting(forwardBase, caller, guidanceMode, attackKind,
                    aircraftKind);
            string prompt = UsesSequentialTargets(attackKind)
                ? "HD_CAS_SequentialEntryPrompt".Translate(EntryEdgeDepth).ToString()
                : attackKind == HelodCasAttackKind.Hydra70
                ? "HD_CAS_HydraRoutePrompt".Translate(EntryEdgeDepth, StrafeLength,
                    StrafeWidth).ToString()
                : UsesAttackCorridor(attackKind)
                ? "HD_CAS_StrafeRoutePrompt".Translate(EntryEdgeDepth, StrafeLength,
                    StrafeWidth).ToString()
                : "HD_CAS_RoutePrompt".Translate(EntryEdgeDepth).ToString();
            Messages.Message(prompt,
                MessageTypeDefOf.NeutralEvent);
        }

        public static bool IsEntryCell(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }
            int edgeDistance = Mathf.Min(cell.x, map.Size.x - 1 - cell.x,
                cell.z, map.Size.z - 1 - cell.z);
            return edgeDistance <= EntryEdgeDepth;
        }

        public static int SocialLevel(Pawn caller)
        {
            return caller?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
        }

        public static bool HasNearbyFlare(Map map, IntVec3 target)
        {
            float radiusSquared = TalkOnFlareAssistRadius * TalkOnFlareAssistRadius;
            return CasFlareTargetUtility.ActiveFlares(map)
                .Any(flare => flare.Position.DistanceToSquared(target) <= radiusSquared);
        }

        public static bool CanUseTalkOnTarget(Pawn caller, Map map, IntVec3 target)
        {
            bool exactCellVisible = !target.Fogged(map)
                && SCR300RadioUtility.HasLineOfSight(caller, map, target);
            if (exactCellVisible)
            {
                return true;
            }
            bool targetsPawn = map?.thingGrid?.ThingsListAtFast(target)
                .OfType<Pawn>().Any(pawn => !pawn.Dead) == true;
            if (targetsPawn)
            {
                return false;
            }
            return GenRadial.RadialCellsAround(target, GroundReferenceRadius, true)
                .Any(cell => cell.InBounds(map) && !cell.Fogged(map)
                    && SCR300RadioUtility.HasLineOfSight(caller, map, cell));
        }

        public static int NearbySimilarPawnCount(Map map, HelodCasAttackPlan plan)
        {
            Pawn targetPawn = plan?.TargetPawn;
            if (map == null || targetPawn == null || targetPawn.Destroyed
                || targetPawn.Map != map)
            {
                return 0;
            }

            return GenRadial.RadialDistinctThingsAround(targetPawn.Position, map,
                    SimilarPawnRadius, true)
                .OfType<Pawn>()
                .Count(pawn => pawn != targetPawn && !pawn.Dead
                    && pawn.kindDef == targetPawn.kindDef && pawn.Faction == targetPawn.Faction);
        }

        public static float GuidanceSuccessChance(HelodCasAttackPlan plan, Pawn caller,
            Map map, int goAroundCount)
        {
            if (plan == null)
            {
                return 0f;
            }

            int social = Mathf.Clamp(SocialLevel(caller), 0, 20);
            float weatherPenalty = WeatherGuidancePenalty(map);
            if (plan.GuidanceMode == HelodCasGuidanceMode.Laser)
            {
                if (!TryGetLaserDesignatorOperator(caller, map,
                    plan.CurrentAimCell(map), out _,
                    out HelodCasLaserDesignatorExtension designator))
                {
                    return 0f;
                }
                float laserChance = designator.baseGuidanceChance
                    + goAroundCount * 0.06f
                    - weatherPenalty * designator.weatherPenaltyFactor;
                return Mathf.Clamp(laserChance, 0.05f, 0.995f);
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.Flare)
            {
                float flareChance = 0.88f + social * 0.005f
                    + goAroundCount * FlareGoAroundBonus
                    - weatherPenalty * 0.75f;
                return Mathf.Clamp(flareChance, 0.05f, 0.995f);
            }
            if (plan.GuidanceMode != HelodCasGuidanceMode.TalkOn)
            {
                return 0f;
            }

            float socialBonus = social * 0.025f;
            float routeBonus = Mathf.InverseLerp(8f, 80f, plan.RouteLength) * 0.15f;
            float roofPenalty = Mathf.Min(0.30f, plan.MountainRoofCount * 0.02f);
            float flareBonus = HasNearbyFlare(map, plan.TargetCell) ? 0.10f : 0f;
            float similarPawnPenalty = Mathf.Min(0.18f,
                NearbySimilarPawnCount(map, plan) * 0.03f);
            float retryBonus = goAroundCount * TalkOnGoAroundBonus;
            return Mathf.Clamp(0.30f + socialBonus + routeBonus - roofPenalty
                + flareBonus - similarPawnPenalty + retryBonus
                - weatherPenalty * 0.50f, 0.05f, 0.98f);
        }

        public static bool TryCall(Map map, HelodCasAttackPlan plan,
            HelodForwardBase forwardBase, Pawn caller, Thing_M8FlareTarget flareTarget)
        {
            if (SCR300RadioUtility.IsBlackout(map))
            {
                Messages.Message("HD_SCR300_SolarFlare".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (plan == null || !plan.EntryCell.InBounds(map) || !plan.TargetCell.InBounds(map)
                || !IsEntryCell(map, plan.EntryCell) || !CanUseBase(map, forwardBase)
                || !SupportsAttack(plan.AircraftKind, plan.AttackKind))
            {
                Messages.Message("HD_CAS_Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (UsesSequentialTargets(plan.AttackKind)
                && plan.DesignatedTargetCount
                    != TargetDesignationCount(plan.AttackKind))
            {
                Messages.Message("HD_CAS_Unavailable".Translate(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }
            if (UsesAttackCorridor(plan.AttackKind)
                && (!plan.StrafeStart.InBounds(map) || !plan.StrafeEnd.InBounds(map)
                    || plan.FlightRouteLength < 8f))
            {
                Messages.Message("HD_CAS_StrafeOutsideMap".Translate(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.TalkOn
                && !CanUseTalkOnTarget(caller, map, plan.TargetCell))
            {
                Messages.Message("HD_CAS_TargetNoVisibleReference".Translate(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.Flare
                && (flareTarget == null || !flareTarget.IsActiveFlare || flareTarget.Map != map
                    || flareTarget.Position != plan.TargetCell))
            {
                Messages.Message("HD_CAS_NoActiveFlare".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.Laser)
            {
                if (!TryGetLaserDesignatorOperator(caller, map,
                    plan.CurrentAimCell(map), out _, out _))
                {
                    Messages.Message("HD_CAS_LaserUnavailable".Translate(),
                        MessageTypeDefOf.RejectInput);
                    return false;
                }
            }

            MapComponent_HelodCasSupport support = map
                .GetComponent<MapComponent_HelodCasSupport>();
            if (!support.HasPlaytime(forwardBase, plan.AircraftKind))
            {
                Messages.Message("HD_CAS_PlaytimeExhaustedAircraft".Translate(
                    AircraftLabel(plan.AircraftKind)),
                    MessageTypeDefOf.RejectInput);
                return false;
            }
            if (!support.HasAmmoForAttack(forwardBase, plan.AircraftKind,
                plan.AttackKind, plan.MunitionCount, out _))
            {
                Messages.Message("HD_CAS_AmmoExhausted".Translate(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }
            if (!forwardBase.TryConsumeServiceUse(HelodForwardBaseService.CloseAirSupport,
                out string failReason))
            {
                Messages.Message(failReason ?? "HD_CAS_Unavailable".Translate().ToString(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }

            if (!support.TryConsumePlaytime(forwardBase, plan.AircraftKind,
                Find.TickManager.TicksGame, plan.AttackKind, out int aircraftCount, out _))
            {
                Messages.Message("HD_CAS_PlaytimeExhaustedAircraft".Translate(
                    AircraftLabel(plan.AircraftKind)),
                    MessageTypeDefOf.RejectInput);
                return false;
            }
            if (!support.TryConsumeAmmo(forwardBase, plan.AircraftKind,
                plan.AttackKind, plan.MunitionCount, aircraftCount))
            {
                Messages.Message("HD_CAS_AmmoExhausted".Translate(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }
            support.QueueStrike(plan, caller, forwardBase, flareTarget, aircraftCount);
            Messages.Message("HD_CAS_Called".Translate(forwardBase.LabelCap,
                (ArrivalDelayTicks / 60f).ToString("F0"), plan.MountainRoofCount),
                caller, MessageTypeDefOf.PositiveEvent);
            Messages.Message("HD_CAS_AircraftAssigned".Translate(aircraftCount),
                MessageTypeDefOf.NeutralEvent);
            return true;
        }
    }

    [StaticConstructorOnStartup]
    public sealed class MapComponent_HelodCasSupport : MapComponent
    {
        private List<HelodCasStrike> strikes = new List<HelodCasStrike>();
        private List<HelodCasFallingBomb> fallingBombs = new List<HelodCasFallingBomb>();
        private List<HelodCasPlaytimeState> playtimeStates
            = new List<HelodCasPlaytimeState>();
        private HelodForwardBase routeBase;
        private Pawn routeCaller;
        private HelodCasGuidanceMode routeGuidanceMode;
        private HelodCasAttackKind routeAttackKind;
        private HelodCasAircraftKind routeAircraftKind;
        private IntVec3 dragStart = IntVec3.Invalid;
        private IntVec3 dragEnd = IntVec3.Invalid;
        private readonly List<IntVec3> designatedTargets = new List<IntVec3>();
        private readonly List<Thing> designatedTargetThings = new List<Thing>();
        private bool routeTargeting;
        private static readonly Dictionary<HelodCasAircraftKind, Material> aircraftMaterials
            = new Dictionary<HelodCasAircraftKind, Material>();
        private static readonly Dictionary<string, Material> munitionMaterials
            = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Material> designatorMaterials
            = new Dictionary<string, Material>();
        private static readonly Material izlidBeamOuterMaterial
            = SolidColorMaterials.SimpleSolidColorMaterial(
                new Color(0.18f, 1f, 0.42f, 0.16f), false);
        private static readonly Material izlidBeamCoreMaterial
            = SolidColorMaterials.SimpleSolidColorMaterial(
                new Color(0.58f, 1f, 0.72f, 0.52f), false);
        private static readonly Material[] izlidSkyBeamOuterMaterials
            = CreateSkyFadeMaterials(new Color(0.18f, 1f, 0.42f), 0.20f,
                0.008f);
        private static readonly Material[] izlidSkyBeamCoreMaterials
            = CreateSkyFadeMaterials(new Color(0.58f, 1f, 0.72f), 0.58f,
                0.012f);
        private static readonly Material[] pulsingBeamOuterMaterials
            = CreatePulsingSolidMaterials(new Color(0.18f, 1f, 0.42f), 0f,
                0.16f);
        private static readonly Material[] pulsingBeamCoreMaterials
            = CreatePulsingSolidMaterials(new Color(0.58f, 1f, 0.72f), 0f,
                0.52f);
        private static readonly Material[] laserAimGlowMaterials
            = CreatePulsingGlowMaterials();

        public MapComponent_HelodCasSupport(Map map) : base(map)
        {
        }

        public void BeginRouteTargeting(HelodForwardBase forwardBase, Pawn caller,
            HelodCasGuidanceMode guidanceMode, HelodCasAttackKind attackKind,
            HelodCasAircraftKind aircraftKind)
        {
            routeBase = forwardBase;
            routeCaller = caller;
            routeGuidanceMode = guidanceMode;
            routeAttackKind = attackKind;
            routeAircraftKind = aircraftKind;
            dragStart = IntVec3.Invalid;
            dragEnd = IntVec3.Invalid;
            designatedTargets.Clear();
            designatedTargetThings.Clear();
            routeTargeting = true;
        }

        public void QueueStrike(HelodCasAttackPlan plan, Pawn caller,
            HelodForwardBase forwardBase, Thing_M8FlareTarget flareTarget,
            int aircraftCount)
        {
            HelodCasStrike strike = new HelodCasStrike(plan, caller, forwardBase,
                flareTarget,
                Find.TickManager.TicksGame,
                Find.TickManager.TicksGame + HelodCasSupportUtility.ArrivalDelayTicks,
                aircraftCount);
            strikes.Add(strike);
            EnsureStationaryGuidanceJobs(strike);
        }

        public bool HasPlaytime(HelodForwardBase forwardBase,
            HelodCasAircraftKind aircraftKind)
        {
            return GetPlaytimeState(forwardBase, aircraftKind, true).IsActive;
        }

        public bool CanRequestFlight(HelodForwardBase forwardBase,
            HelodCasAircraftKind aircraftKind)
        {
            HelodCasPlaytimeState state = GetPlaytimeState(forwardBase, aircraftKind, true);
            return !state.IsActive && !strikes.Any(strike => strike.ForwardBase == forwardBase
                && strike.Plan?.AircraftKind == aircraftKind);
        }

        public bool TryRequestFlight(HelodForwardBase forwardBase,
            HelodCasAircraftKind aircraftKind, int now)
        {
            return CanRequestFlight(forwardBase, aircraftKind)
                && GetPlaytimeState(forwardBase, aircraftKind, true).RequestFlight(now);
        }

        public void GetPlaytimeStatus(HelodForwardBase forwardBase,
            HelodCasAircraftKind aircraftKind,
            out bool flightRequested, out int remainingPlaytime,
            out int reservedAircraftCount)
        {
            HelodCasPlaytimeState state = GetPlaytimeState(forwardBase, aircraftKind, true);
            flightRequested = state.FlightRequested;
            remainingPlaytime = state.RemainingPlaytime;
            reservedAircraftCount = state.ReservedAircraftCount;
        }

        public int GetAmmoRemaining(HelodForwardBase forwardBase,
            HelodCasAircraftKind aircraftKind, HelodCasAttackKind attackKind)
        {
            return GetPlaytimeState(forwardBase, aircraftKind, true)
                .AmmoRemaining(attackKind);
        }

        public bool HasAmmoForAttack(HelodForwardBase forwardBase,
            HelodCasAircraftKind aircraftKind, HelodCasAttackKind attackKind,
            int munitionCount, out int required)
        {
            HelodCasPlaytimeState state = GetPlaytimeState(forwardBase,
                aircraftKind, true);
            required = HelodCasSupportUtility.AmmoCost(attackKind, munitionCount,
                state.ExpectedAircraftCount(attackKind));
            return state.HasAmmo(attackKind, required);
        }

        public bool TryConsumeAmmo(HelodForwardBase forwardBase,
            HelodCasAircraftKind aircraftKind, HelodCasAttackKind attackKind,
            int munitionCount, int aircraftCount)
        {
            int cost = HelodCasSupportUtility.AmmoCost(attackKind, munitionCount,
                aircraftCount);
            return GetPlaytimeState(forwardBase, aircraftKind, true)
                .TryConsumeAmmo(attackKind, cost);
        }

        public bool TryConsumePlaytime(HelodForwardBase forwardBase,
            HelodCasAircraftKind aircraftKind, int now,
            HelodCasAttackKind attackKind, out int aircraftCount, out int remaining)
        {
            HelodCasPlaytimeState state = GetPlaytimeState(forwardBase, aircraftKind, true);
            bool consumed = state.TryConsumeAction(now, attackKind, out aircraftCount);
            remaining = state.RemainingPlaytime;
            return consumed;
        }

        private HelodCasPlaytimeState GetPlaytimeState(HelodForwardBase forwardBase,
            HelodCasAircraftKind aircraftKind, bool create)
        {
            HelodCasPlaytimeState state = playtimeStates.FirstOrDefault(
                item => item.ForwardBase == forwardBase
                    && item.AircraftKind == aircraftKind);
            if (state == null && create)
            {
                state = new HelodCasPlaytimeState(forwardBase, aircraftKind);
                playtimeStates.Add(state);
            }
            return state;
        }

        private void ReserveBombingAircraft(HelodForwardBase forwardBase, int count)
        {
            if (forwardBase != null && count > 0)
            {
                GetPlaytimeState(forwardBase, HelodCasAircraftKind.P47, true)
                    .ReserveAircraft(count);
            }
        }

        private void ConsumeGuidanceFailurePlaytime(HelodCasStrike strike)
        {
            if (strike?.ForwardBase == null
                || strike.GoAroundCount % HelodCasSupportUtility.GuidanceFailuresPerPlaytime
                    != 0)
            {
                return;
            }
            HelodCasPlaytimeState state = GetPlaytimeState(strike.ForwardBase,
                strike.Plan.AircraftKind, true);
            if (state.ConsumePenalty())
            {
                Messages.Message("HD_CAS_PlaytimeGuidancePenaltyAircraft".Translate(
                    strike.ForwardBase.LabelCap,
                    HelodCasSupportUtility.AircraftLabel(strike.Plan.AircraftKind)),
                    MessageTypeDefOf.CautionInput);
            }
        }

        private void TickPlaytime(int now)
        {
            for (int i = 0; i < playtimeStates.Count; i++)
            {
                HelodCasPlaytimeState state = playtimeStates[i];
                int consumed = state.ConsumeElapsedTime(now);
                if (consumed > 0 && state.ForwardBase != null)
                {
                    Messages.Message("HD_CAS_PlaytimeTimePenaltyAircraft".Translate(
                        state.ForwardBase.LabelCap,
                        HelodCasSupportUtility.AircraftLabel(state.AircraftKind), consumed),
                        MessageTypeDefOf.CautionInput);
                }
            }
        }

        public bool HasActiveStrike(Pawn caller)
        {
            return FindStrike(caller) != null;
        }

        public bool RequiresStationaryGuidance(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }
            for (int i = 0; i < strikes.Count; i++)
            {
                HelodCasStrike strike = strikes[i];
                if (!strike.RequiresStationaryGuidance)
                {
                    continue;
                }
                if (strike.Caller == pawn)
                {
                    return true;
                }
                if (strike.Plan?.GuidanceMode == HelodCasGuidanceMode.Laser
                    && HelodCasSupportUtility.TryGetLaserDesignatorOperator(
                        strike.Caller, map, strike.Plan.CurrentAimCell(map),
                        out Pawn designatorPawn, out _)
                    && designatorPawn == pawn)
                {
                    return true;
                }
            }
            return false;
        }

        public void FaceLaserGuidanceDirection(Pawn pawn, int now)
        {
            if (pawn?.rotationTracker == null)
            {
                return;
            }
            for (int i = 0; i < strikes.Count; i++)
            {
                HelodCasStrike strike = strikes[i];
                if (strike?.Plan?.GuidanceMode != HelodCasGuidanceMode.Laser
                    || !strike.RequiresStationaryGuidance
                    || !HelodCasSupportUtility.TryGetLaserDesignatorOperator(
                        strike.Caller, map, strike.Plan.CurrentAimCell(map),
                        out Pawn operatorPawn,
                        out HelodCasLaserDesignatorExtension designator)
                    || operatorPawn != pawn)
                {
                    continue;
                }

                Vector3 facingPosition;
                if (designator.usesPilotSignalVisual)
                {
                    if (strike.IzlidSkySignalActive(now))
                    {
                        facingPosition = IzlidSkySignalEnd(strike, pawn, now);
                    }
                    else if (strike.IzlidTargetSignalActive(now))
                    {
                        facingPosition = strike.Plan.CurrentAimCell(map)
                            .ToVector3Shifted();
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    facingPosition = strike.Plan.CurrentAimCell(map)
                        .ToVector3Shifted();
                }
                pawn.rotationTracker.FaceCell(facingPosition.ToIntVec3());
                return;
            }
        }

        private void EnsureStationaryGuidanceJobs(HelodCasStrike strike)
        {
            if (strike == null || !strike.RequiresStationaryGuidance)
            {
                return;
            }
            EnsureStationaryGuidanceJob(strike.Caller);
            if (strike.Plan?.GuidanceMode == HelodCasGuidanceMode.Laser
                && HelodCasSupportUtility.TryGetLaserDesignatorOperator(strike.Caller,
                    map, strike.Plan.CurrentAimCell(map), out Pawn designatorPawn,
                    out _))
            {
                EnsureStationaryGuidanceJob(designatorPawn);
            }
        }

        private static void EnsureStationaryGuidanceJob(Pawn pawn)
        {
            if (pawn?.jobs == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                return;
            }
            JobDef guidanceJob = DefDatabase<JobDef>.GetNamedSilentFail(
                "HD_CASStationaryGuidance");
            if (guidanceJob == null || pawn.CurJob?.def == guidanceJob)
            {
                return;
            }
            pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(guidanceJob), JobTag.Misc);
        }

        public bool CanCancelStrike(Pawn caller, out string rejection)
        {
            HelodCasStrike strike = FindStrike(caller);
            if (strike == null)
            {
                rejection = "HD_CAS_Cancel_NoActive".Translate().ToString();
                return false;
            }
            return strike.CanCancel(Find.TickManager.TicksGame, out rejection);
        }

        public bool TryCancelStrike(Pawn caller)
        {
            HelodCasStrike strike = FindStrike(caller);
            string rejection = null;
            if (strike == null || !strike.CanCancel(Find.TickManager.TicksGame,
                out rejection))
            {
                Messages.Message(rejection ?? "HD_CAS_Cancel_NoActive".Translate().ToString(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }
            int recallableAircraft = strike.RecallableBombingAircraftCount;
            if (recallableAircraft > 0)
            {
                ReserveBombingAircraft(strike.ForwardBase, recallableAircraft);
            }
            if (!strike.BeginAbort(Find.TickManager.TicksGame, map))
            {
                strikes.Remove(strike);
            }
            Messages.Message("HD_CAS_Cancelled".Translate(), caller,
                MessageTypeDefOf.NeutralEvent);
            return true;
        }

        private HelodCasStrike FindStrike(Pawn caller)
        {
            for (int i = strikes.Count - 1; i >= 0; i--)
            {
                if (strikes[i].Caller == caller)
                {
                    return strikes[i];
                }
            }
            return null;
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (!routeTargeting)
            {
                return;
            }

            Event evt = Event.current;
            IntVec3 mouseCell = UI.MouseCell();
            if ((evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
                || (evt.type == EventType.MouseDown && evt.button == 1))
            {
                CancelRouteTargeting();
                evt.Use();
                return;
            }

            if (HelodCasSupportUtility.UsesSequentialTargets(routeAttackKind))
            {
                HandleSequentialTargeting(evt, mouseCell);
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (!HelodCasSupportUtility.IsEntryCell(map, mouseCell))
                {
                    Messages.Message("HD_CAS_InvalidEntry".Translate(
                        HelodCasSupportUtility.EntryEdgeDepth), MessageTypeDefOf.RejectInput);
                    evt.Use();
                    return;
                }
                dragStart = mouseCell;
                dragEnd = mouseCell;
                evt.Use();
            }
            else if (dragStart.IsValid && (evt.type == EventType.MouseDrag
                || evt.type == EventType.MouseMove) && mouseCell.InBounds(map))
            {
                dragEnd = mouseCell;
                if (evt.type == EventType.MouseDrag)
                {
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0 && dragStart.IsValid)
            {
                IntVec3 entry = dragStart;
                IntVec3 target = mouseCell.InBounds(map) ? mouseCell : dragEnd;
                evt.Use();
                TryFinishRoute(entry, target);
            }
        }

        private void HandleSequentialTargeting(Event evt, IntVec3 mouseCell)
        {
            if (evt.type != EventType.MouseDown || evt.button != 0)
            {
                return;
            }
            evt.Use();
            if (!dragStart.IsValid)
            {
                if (!HelodCasSupportUtility.IsEntryCell(map, mouseCell))
                {
                    Messages.Message("HD_CAS_InvalidEntry".Translate(
                        HelodCasSupportUtility.EntryEdgeDepth),
                        MessageTypeDefOf.RejectInput);
                    return;
                }
                dragStart = mouseCell;
                int count = HelodCasSupportUtility.TargetDesignationCount(
                    routeAttackKind);
                string prompt = HelodCasSupportUtility.UsesAreaFire(routeAttackKind)
                    ? "HD_CAS_AreaTargetPrompt".Translate().ToString()
                    : "HD_CAS_SequentialTargetPrompt".Translate(count).ToString();
                Messages.Message(prompt,
                    MessageTypeDefOf.NeutralEvent);
                return;
            }

            if (!mouseCell.InBounds(map) || dragStart.DistanceTo(mouseCell) < 8f)
            {
                Messages.Message("HD_CAS_InvalidRoute".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }
            bool targetVisible = routeGuidanceMode == HelodCasGuidanceMode.Laser
                ? HelodCasSupportUtility.CanLaserDesignate(routeCaller, map,
                    mouseCell)
                : HelodCasSupportUtility.CanUseTalkOnTarget(routeCaller, map,
                    mouseCell);
            if (!targetVisible)
            {
                Messages.Message("HD_CAS_TargetNoVisibleReference".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }
            if (HelodCasSupportUtility.UsesAttackCorridor(routeAttackKind)
                && (!HelodCasAttackPlan.StrafeStartCell(dragStart, mouseCell).InBounds(map)
                    || !HelodCasAttackPlan.StrafeEndCell(dragStart, mouseCell).InBounds(map)
                    || dragStart.DistanceTo(mouseCell)
                        - HelodCasSupportUtility.StrafeLength * 0.5f < 8f))
            {
                Messages.Message("HD_CAS_StrafeOutsideMap".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            designatedTargets.Add(mouseCell);
            Thing targetThing = HelodCasSupportUtility.UsesAreaFire(routeAttackKind)
                ? null : map.thingGrid.ThingsListAtFast(mouseCell)
                    .FirstOrDefault(thing => thing is Pawn
                        || thing.def.category == ThingCategory.Building);
            designatedTargetThings.Add(targetThing);
            int required = HelodCasSupportUtility.TargetDesignationCount(
                routeAttackKind);
            if (designatedTargets.Count < required)
            {
                Messages.Message("HD_CAS_SequentialTargetProgress".Translate(
                    designatedTargets.Count, required), MessageTypeDefOf.NeutralEvent);
                return;
            }
            if (routeAttackKind == HelodCasAttackKind.AGR20A)
            {
                OpenAgr20LaunchCountMenu();
            }
            else
            {
                int munitionCount = routeAttackKind == HelodCasAttackKind.Strafing
                    ? 1 : HelodCasSupportUtility.MunitionCount(routeAttackKind);
                FinishSequentialTargeting(munitionCount);
            }
        }

        private HelodCasAttackPlan CreateSequentialAttackPlan(int munitionCount)
        {
            IntVec3 entry = dragStart;
            IntVec3 primaryTarget = designatedTargets[0];
            HelodCasSupportUtility.ScatterFor(routeAircraftKind, routeAttackKind,
                out float majorScatter, out float minorScatter);
            return new HelodCasAttackPlan(entry, primaryTarget,
                routeGuidanceMode, map, majorScatter, minorScatter,
                routeAttackKind, routeAircraftKind, designatedTargets,
                designatedTargetThings, munitionCount);
        }

        private void FinishSequentialTargeting(int munitionCount)
        {
            HelodCasAttackPlan plan = CreateSequentialAttackPlan(munitionCount);
            HelodForwardBase forwardBase = routeBase;
            Pawn caller = routeCaller;
            CancelRouteTargeting();
            HelodCasSupportUtility.TryCall(map, plan, forwardBase, caller, null);
        }

        private void OpenAgr20LaunchCountMenu()
        {
            int available = GetAmmoRemaining(routeBase, routeAircraftKind,
                HelodCasAttackKind.AGR20A);
            if (available <= 0)
            {
                Messages.Message("HD_CAS_AmmoExhausted".Translate(),
                    MessageTypeDefOf.RejectInput);
                CancelRouteTargeting();
                return;
            }

            HelodForwardBase forwardBase = routeBase;
            Pawn caller = routeCaller;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int count = 1; count <= available; count++)
            {
                int selectedCount = count;
                HelodCasAttackPlan plan = CreateSequentialAttackPlan(selectedCount);
                options.Add(new FloatMenuOption(
                    "HD_CAS_AGR20LaunchCountOption".Translate(selectedCount),
                    () => HelodCasSupportUtility.TryCall(map, plan, forwardBase,
                        caller, null)));
            }
            CancelRouteTargeting();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            if (routeTargeting)
            {
                DrawRoutePreview();
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            for (int i = 0; i < strikes.Count; i++)
            {
                DrawLaserGuidanceEffect(strikes[i], now);
                DrawLaserDesignator(strikes[i], now);
                DrawAircraft(strikes[i], now);
            }
            for (int i = 0; i < fallingBombs.Count; i++)
            {
                DrawFallingBomb(fallingBombs[i], now);
            }
        }

        private void TryFinishRoute(IntVec3 entry, IntVec3 target)
        {
            if (!target.IsValid || !target.InBounds(map) || entry.DistanceTo(target) < 8f)
            {
                ResetDrag();
                Messages.Message("HD_CAS_InvalidRoute".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (HelodCasSupportUtility.UsesAttackCorridor(routeAttackKind)
                && (!HelodCasAttackPlan.StrafeStartCell(entry, target).InBounds(map)
                    || !HelodCasAttackPlan.StrafeEndCell(entry, target).InBounds(map)
                    || entry.DistanceTo(target)
                        - HelodCasSupportUtility.StrafeLength * 0.5f < 8f))
            {
                ResetDrag();
                Messages.Message("HD_CAS_StrafeOutsideMap".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            Thing_M8FlareTarget flareTarget = null;
            if (routeGuidanceMode == HelodCasGuidanceMode.TalkOn)
            {
                if (!HelodCasSupportUtility.CanUseTalkOnTarget(routeCaller, map, target))
                {
                    ResetDrag();
                    Messages.Message("HD_CAS_TargetNoVisibleReference".Translate(),
                        MessageTypeDefOf.RejectInput);
                    return;
                }
            }
            else if (routeGuidanceMode == HelodCasGuidanceMode.Flare)
            {
                flareTarget = CasFlareTargetUtility.ActiveFlares(map)
                    .FirstOrDefault(flare => flare.Position == target);
                if (flareTarget == null)
                {
                    ResetDrag();
                    Messages.Message("HD_CAS_RouteMustEndAtFlare".Translate(),
                        MessageTypeDefOf.RejectInput);
                    return;
                }
            }

            HelodCasSupportUtility.ScatterFor(routeAircraftKind, routeAttackKind,
                out float majorScatter, out float minorScatter);
            HelodCasAttackPlan plan = new HelodCasAttackPlan(entry, target,
                routeGuidanceMode, map, majorScatter, minorScatter, routeAttackKind,
                routeAircraftKind);
            HelodForwardBase forwardBase = routeBase;
            Pawn caller = routeCaller;
            CancelRouteTargeting();
            HelodCasSupportUtility.TryCall(map, plan, forwardBase, caller, flareTarget);
        }

        private void DrawRoutePreview()
        {
            IntVec3 mouseCell = UI.MouseCell();
            if (!dragStart.IsValid)
            {
                if (mouseCell.InBounds(map))
                {
                    GenDraw.DrawRadiusRing(mouseCell, 1f,
                        HelodCasSupportUtility.IsEntryCell(map, mouseCell)
                            ? Color.green : Color.red);
                }
                return;
            }

            if (HelodCasSupportUtility.UsesSequentialTargets(routeAttackKind))
            {
                if (mouseCell.InBounds(map))
                {
                    GenDraw.DrawLineBetween(dragStart.ToVector3Shifted(),
                        mouseCell.ToVector3Shifted(), SimpleColor.White);
                    if (HelodCasSupportUtility.UsesAttackCorridor(routeAttackKind))
                    {
                        DrawStrafeCorridor(dragStart, mouseCell);
                    }
                    else if (HelodCasSupportUtility.UsesAreaFire(routeAttackKind))
                    {
                        HelodCasSupportUtility.ScatterFor(routeAircraftKind,
                            routeAttackKind, out float major, out float minor);
                        DrawScatterEllipse(mouseCell, dragStart, major, minor);
                    }
                }
                foreach (IGrouping<IntVec3, IntVec3> group in designatedTargets
                    .GroupBy(cell => cell))
                {
                    GenDraw.DrawRadiusRing(group.Key, 0.7f + group.Count() * 0.18f,
                        Color.cyan);
                }
                return;
            }

            IntVec3 end = dragEnd.IsValid ? dragEnd : mouseCell;
            if (!end.InBounds(map))
            {
                return;
            }
            GenDraw.DrawLineBetween(dragStart.ToVector3Shifted(), end.ToVector3Shifted(),
                SimpleColor.White);
            if (HelodCasSupportUtility.UsesAttackCorridor(routeAttackKind))
            {
                DrawStrafeCorridor(dragStart, end);
            }
            else
            {
                DrawScatterEllipse(end, dragStart,
                    HelodCasSupportUtility.MajorScatterRadius,
                    HelodCasSupportUtility.MinorScatterRadius);
            }

            List<IntVec3> mountainRoofs = HelodCasAttackPlan.LineCells(dragStart, end)
                .Where(cell => HelodCasAttackPlan.IsMountainRoof(map, cell)).ToList();
            if (mountainRoofs.Count > 0)
            {
                GenDraw.DrawFieldEdges(mountainRoofs, Color.red, 0.08f);
            }
        }

        private static void DrawScatterEllipse(IntVec3 center, IntVec3 entry,
            float majorRadius, float minorRadius)
        {
            Vector2 direction = new Vector2(center.x - entry.x, center.z - entry.z).normalized;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }
            Vector2 lateral = new Vector2(-direction.y, direction.x);
            Vector3 previous = Vector3.zero;
            const int segments = 32;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector2 offset = direction * (Mathf.Cos(angle) * majorRadius)
                    + lateral * (Mathf.Sin(angle) * minorRadius);
                Vector3 point = new Vector3(center.x + 0.5f + offset.x,
                    AltitudeLayer.MetaOverlays.AltitudeFor(), center.z + 0.5f + offset.y);
                if (i > 0)
                {
                    GenDraw.DrawLineBetween(previous, point, SimpleColor.White);
                }
                previous = point;
            }
        }

        private static void DrawStrafeCorridor(IntVec3 entry, IntVec3 center)
        {
            Vector2 direction = new Vector2(center.x - entry.x,
                center.z - entry.z).normalized;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }
            Vector2 lateral = new Vector2(-direction.y, direction.x)
                * (HelodCasSupportUtility.StrafeWidth * 0.5f);
            Vector3 designatedCenter = center.ToVector3Shifted();
            Vector3 halfLength = new Vector3(direction.x, 0f, direction.y)
                * (HelodCasSupportUtility.StrafeLength * 0.5f);
            Vector3 startCenter = designatedCenter - halfLength;
            Vector3 endCenter = designatedCenter + halfLength;
            Vector3 lateral3 = new Vector3(lateral.x, 0f, lateral.y);
            GenDraw.DrawLineBetween(startCenter - lateral3, startCenter + lateral3,
                SimpleColor.White);
            GenDraw.DrawLineBetween(startCenter - lateral3, endCenter - lateral3,
                SimpleColor.White);
            GenDraw.DrawLineBetween(startCenter + lateral3, endCenter + lateral3,
                SimpleColor.White);
            GenDraw.DrawLineBetween(endCenter - lateral3, endCenter + lateral3,
                SimpleColor.White);
        }

        private void CancelRouteTargeting()
        {
            routeTargeting = false;
            routeBase = null;
            routeCaller = null;
            designatedTargets.Clear();
            designatedTargetThings.Clear();
            ResetDrag();
        }

        private void ResetDrag()
        {
            dragStart = IntVec3.Invalid;
            dragEnd = IntVec3.Invalid;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int now = Find.TickManager.TicksGame;
            TickPlaytime(now);
            for (int i = fallingBombs.Count - 1; i >= 0; i--)
            {
                if (now < fallingBombs[i].ImpactTick)
                {
                    continue;
                }
                ImpactBomb(fallingBombs[i]);
                fallingBombs.RemoveAt(i);
            }
            for (int i = strikes.Count - 1; i >= 0; i--)
            {
                HelodCasStrike strike = strikes[i];
                EnsureStationaryGuidanceJobs(strike);
                if (!strike.GuidanceValid(map))
                {
                    strikes.RemoveAt(i);
                    Messages.Message("HD_CAS_GuidanceLost".Translate(),
                        MessageTypeDefOf.NegativeEvent);
                    continue;
                }
                if (strike.NeedsGuidanceAttempt(now))
                {
                    bool success = strike.TryGuidance(map);
                    if (success)
                    {
                        Messages.Message("HD_CAS_GuidanceSuccess".Translate(
                            (strike.LastGuidanceChance * 100f).ToString("F0"),
                            strike.GuidanceAttempts), MessageTypeDefOf.PositiveEvent);
                    }
                    else
                    {
                        float nextChance = strike.CurrentGuidanceChance(map);
                        Messages.Message("HD_CAS_GoAround".Translate(
                            (strike.LastGuidanceChance * 100f).ToString("F0"),
                            (nextChance * 100f).ToString("F0"), strike.GoAroundCount,
                            strike.TicksUntilGuidanceAttempt(now).ToStringTicksToPeriod()),
                            MessageTypeDefOf.CautionInput);
                        ConsumeGuidanceFailurePlaytime(strike);
                    }
                    continue;
                }

                if (strike.RunState != HelodCasRunState.Attacking)
                {
                    continue;
                }

                HelodCasAircraftTickEvent tickEvent = strike.TickAircraft(now, map);
                if (tickEvent == HelodCasAircraftTickEvent.ReleaseBombPair)
                {
                    ReleaseOrdnance(strike);
                }
                else if (tickEvent == HelodCasAircraftTickEvent.StrafeBurst)
                {
                    FireStrafeBurst(strike, now);
                }
                else if (tickEvent == HelodCasAircraftTickEvent.Complete)
                {
                    strikes.RemoveAt(i);
                }
            }
        }

        private void ReleaseOrdnance(HelodCasStrike strike)
        {
            int now = Find.TickManager.TicksGame;
            Vector3 releasePosition = strike.AircraftDrawPosition(now);
            int count = strike.Plan.MunitionCount;
            for (int munition = 0; munition < count; munition++)
            {
                IntVec3 impact = strike.NextImpactCell(map, munition);
                if (!impact.InBounds(map))
                {
                    continue;
                }
                int releaseTick = now + munition
                    * HelodCasSupportUtility.MunitionReleaseInterval(
                        strike.Plan.AttackKind);
                Vector3 scheduledReleasePosition = munition == 0 ? releasePosition
                    : strike.AircraftDrawPosition(releaseTick);
                Thing guidedTarget = HelodCasSupportUtility.IsGuidedMissile(
                    strike.Plan.AttackKind)
                    ? strike.Plan.DesignatedTargetThing(munition) : null;
                fallingBombs.Add(new HelodCasFallingBomb(scheduledReleasePosition, impact,
                    strike.Caller, releaseTick,
                    releaseTick + HelodCasSupportUtility.BombFallTicks,
                    strike.Plan.ApproachDirection, strike.Plan.AttackKind,
                    guidedTarget));
            }
        }

        private void FireStrafeBurst(HelodCasStrike strike, int now)
        {
            bool isA10C = strike.Plan.AircraftKind == HelodCasAircraftKind.A10C;
            Vector3 origin = strike.AircraftDrawPosition(now);
            Vector2 direction = strike.Plan.ApproachDirection;
            if (isA10C)
            {
                origin += new Vector3(direction.x, 0f, direction.y)
                    * HelodCasSupportUtility.A10CMuzzleForwardOffset;
            }
            IntVec3 originCell = origin.ToIntVec3();
            if (!originCell.InBounds(map))
            {
                return;
            }
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail(
                HelodCasSupportUtility.StrafeSoundDefName);
            if (!isA10C)
            {
                sound?.PlayOneShot(new TargetInfo(originCell, map));
            }
            if (isA10C)
            {
                FleckMaker.ThrowSmoke(origin, map, 0.42f);
            }

            Vector2 lateral = new Vector2(-direction.y, direction.x);
            float aimDistance = isA10C ? 0f : strike.StrafeAimDistance(now);
            int roundsToFire = strike.ConsumePendingStrafeRounds();
            for (int round = 0; round < roundsToFire; round++)
            {
                int a10CShotIndex = -1;
                string projectileDefName = isA10C
                    ? (strike.NextA10CStrafeRoundIsHighExplosive(out a10CShotIndex)
                        ? "HD_Projectile_A10C_PGU13"
                        : "HD_Projectile_A10C_PGU14")
                    : HelodCasSupportUtility.StrafeProjectileDefName;
                ThingDef projectileDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                    projectileDefName);
                if (projectileDef == null)
                {
                    continue;
                }
                float lateralOffset = Rand.Range(
                    -HelodCasSupportUtility.StrafeWidth * 0.5f,
                    HelodCasSupportUtility.StrafeWidth * 0.5f);
                float longitudinalOffset = Rand.Range(-1.25f, 1.25f);
                float baseAimDistance = isA10C
                    ? HelodCasSupportUtility.StrafeLength * a10CShotIndex
                        / (HelodCasSupportUtility.A10CStrafeRoundCount - 1f)
                    : aimDistance;
                float roundAimDistance = Mathf.Clamp(baseAimDistance
                    + longitudinalOffset,
                    0f, HelodCasSupportUtility.StrafeLength);
                Vector3 start = strike.Plan.CurrentStrafeStartPosition(map);
                IntVec3 impact = new IntVec3(
                    Mathf.RoundToInt(start.x + direction.x
                        * roundAimDistance
                        + lateral.x * lateralOffset), 0,
                    Mathf.RoundToInt(start.z + direction.y
                        * roundAimDistance
                        + lateral.y * lateralOffset));
                if (!impact.InBounds(map))
                {
                    continue;
                }
                Projectile projectile = (Projectile)GenSpawn.Spawn(projectileDef,
                    originCell, map);
                projectile.Launch(strike.Caller, origin, impact, impact,
                    ProjectileHitFlags.All);
                if (isA10C)
                {
                    sound?.PlayOneShot(new TargetInfo(originCell, map));
                }
            }
        }

        private void ImpactBomb(HelodCasFallingBomb bomb)
        {
            if (bomb == null || !bomb.ImpactCell.InBounds(map))
            {
                return;
            }
            FleckMaker.ThrowSmoke(bomb.ImpactCell.ToVector3Shifted(), map, 1.8f);
            HelodCasSupportUtility.MunitionDamage(bomb.AttackKind,
                out float radius, out int damage, out float armorPenetration);
            GenExplosion.DoExplosion(bomb.ImpactCell, map, radius, DamageDefOf.Bomb,
                bomb.Caller, damage, armorPenetration);
        }

        private static Material AircraftMaterial(HelodCasAircraftKind aircraftKind)
        {
            if (aircraftMaterials.TryGetValue(aircraftKind, out Material material))
            {
                return material;
            }
            string texturePath = HelodCasSupportUtility.AircraftTexture(aircraftKind);
            if (ContentFinder<Texture2D>.Get(texturePath, false) == null)
            {
                return null;
            }
            material = MaterialPool.MatFrom(texturePath, ShaderDatabase.Cutout);
            aircraftMaterials[aircraftKind] = material;
            return material;
        }

        private static Material MunitionMaterial(HelodCasAttackKind attackKind)
        {
            string texturePath = HelodCasSupportUtility.MunitionTexture(attackKind);
            if (munitionMaterials.TryGetValue(texturePath, out Material material))
            {
                return material;
            }
            if (ContentFinder<Texture2D>.Get(texturePath, false) == null)
            {
                return null;
            }
            material = MaterialPool.MatFrom(texturePath, ShaderDatabase.Cutout);
            munitionMaterials[texturePath] = material;
            return material;
        }

        private void DrawLaserGuidanceEffect(HelodCasStrike strike, int now)
        {
            if (strike?.Plan?.GuidanceMode != HelodCasGuidanceMode.Laser
                || !strike.RequiresStationaryGuidance
                || !HelodCasSupportUtility.TryGetLaserDesignatorOperator(
                    strike.Caller, map, strike.Plan.CurrentAimCell(map),
                    out Pawn operatorPawn,
                    out HelodCasLaserDesignatorExtension designator))
            {
                return;
            }

            if (!designator.usesPilotSignalVisual)
            {
                float glowBlink = LaserBlinkIntensity(now);
                DrawLaserAimGlow(strike.Plan.CurrentAimCell(map), glowBlink,
                    1.25f, 0.45f);
                return;
            }

            if (strike.IzlidSkySignalActive(now))
            {
                DrawIzlidSkySignal(strike, operatorPawn, now);
                return;
            }
            if (strike.IzlidTargetSignalActive(now))
            {
                DrawIzlidTargetSignal(operatorPawn,
                    strike.Plan.CurrentAimCell(map),
                    LaserBlinkIntensity(now - strike.IzlidTargetSignalStartTick));
            }
        }

        private void DrawLaserDesignator(HelodCasStrike strike, int now)
        {
            if (strike?.Plan?.GuidanceMode != HelodCasGuidanceMode.Laser
                || !strike.RequiresStationaryGuidance
                || !HelodCasSupportUtility.TryGetLaserDesignatorOperator(
                    strike.Caller, map, strike.Plan.CurrentAimCell(map),
                    out Pawn operatorPawn,
                    out HelodCasLaserDesignatorExtension designator)
                || !HelodCasSupportUtility.TryGetBestLaserDesignatorThing(operatorPawn,
                    out _, out _)
                || designator.guidanceGraphicPath.NullOrEmpty())
            {
                return;
            }

            Material material = LaserDesignatorMaterial(
                designator.guidanceGraphicPath);
            if (material == null)
            {
                return;
            }

            Vector3 aimPosition = strike.Plan.CurrentAimCell(map).ToVector3Shifted();
            if (designator.usesPilotSignalVisual
                && strike.IzlidSkySignalActive(now))
            {
                aimPosition = IzlidSkySignalEnd(strike, operatorPawn, now);
            }
            Vector3 direction = aimPosition - operatorPawn.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = operatorPawn.Rotation.FacingCell.ToVector3();
            }
            direction.Normalize();

            Vector3 drawPosition;
            if (designator.fixedGuidanceGraphicPosition)
            {
                drawPosition = strike.FixedGuidanceDevicePosition(operatorPawn,
                    designator);
                direction = aimPosition - drawPosition;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.001f)
                {
                    direction = operatorPawn.Rotation.FacingCell.ToVector3();
                }
                direction.Normalize();
            }
            else
            {
                Vector3 right = new Vector3(direction.z, 0f, -direction.x);
                drawPosition = operatorPawn.DrawPos
                    + direction * designator.guidanceGraphicForwardOffset
                    + right * designator.guidanceGraphicLateralOffset;
            }

            drawPosition.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.04f;
            float rotation = direction.AngleFlat()
                + designator.guidanceGraphicRotationOffset;
            float size = Mathf.Max(0.1f, designator.guidanceGraphicSize);
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(drawPosition,
                    Quaternion.AngleAxis(rotation, Vector3.up),
                    new Vector3(size, 1f, size)), material, 0);
        }

        private static Material LaserDesignatorMaterial(string texturePath)
        {
            if (designatorMaterials.TryGetValue(texturePath, out Material material))
            {
                return material;
            }
            if (ContentFinder<Texture2D>.Get(texturePath, false) == null)
            {
                return null;
            }
            material = MaterialPool.MatFrom(texturePath, ShaderDatabase.Cutout);
            designatorMaterials[texturePath] = material;
            return material;
        }

        private static Material[] CreatePulsingSolidMaterials(Color color,
            float minimumAlpha, float maximumAlpha)
        {
            Material[] materials = new Material[16];
            for (int i = 0; i < materials.Length; i++)
            {
                float progress = i / (float)(materials.Length - 1);
                materials[i] = SolidColorMaterials.SimpleSolidColorMaterial(
                    new Color(color.r, color.g, color.b,
                        Mathf.Lerp(minimumAlpha, maximumAlpha, progress)), false);
            }
            return materials;
        }

        private static Material[] CreateSkyFadeMaterials(Color color,
            float nearAlpha, float farAlpha)
        {
            int count = Mathf.Max(2, HelodCasSupportUtility.IzlidSkyBeamSegments);
            Material[] materials = new Material[count];
            for (int i = 0; i < count; i++)
            {
                float progress = i / (float)(count - 1);
                float easedProgress = progress * progress;
                materials[i] = SolidColorMaterials.SimpleSolidColorMaterial(
                    new Color(color.r, color.g, color.b,
                        Mathf.Lerp(nearAlpha, farAlpha, easedProgress)), false);
            }
            return materials;
        }

        private static Material[] CreatePulsingGlowMaterials()
        {
            Material[] materials = new Material[16];
            for (int i = 0; i < materials.Length; i++)
            {
                float progress = i / (float)(materials.Length - 1);
                materials[i] = MaterialPool.MatFrom("Things/Mote/FireGlow",
                    ShaderDatabase.MoteGlow, new Color(0.20f, 1f, 0.40f,
                        Mathf.Lerp(0f, 0.65f, progress)));
            }
            return materials;
        }

        private static float LaserBlinkIntensity(int elapsedTicks)
        {
            int interval = Mathf.Max(1,
                HelodCasSupportUtility.LaserBlinkIntervalTicks);
            return Mathf.FloorToInt(Mathf.Max(0, elapsedTicks) / (float)interval)
                % 2 == 0 ? 1f : 0f;
        }

        private static int PulseMaterialIndex(float intensity)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(intensity)
                * (laserAimGlowMaterials.Length - 1)), 0,
                laserAimGlowMaterials.Length - 1);
        }

        private static void DrawIzlidSkySignal(HelodCasStrike strike,
            Pawn operatorPawn, int now)
        {
            Vector3 origin = operatorPawn.DrawPos;
            Vector3 end = IzlidSkySignalEnd(strike, operatorPawn, now);
            DrawIzlidSkyBeam(origin, end);
        }

        private static Vector3 IzlidSkySignalEnd(HelodCasStrike strike,
            Pawn operatorPawn, int now)
        {
            Vector3 origin = operatorPawn.DrawPos;
            Vector2 direction = new Vector2(0f, 1f);
            float waveDegrees = Mathf.Sin(now * 0.06f
                + operatorPawn.thingIDNumber * 0.37f) * 8f;
            float radians = waveDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            Vector2 wavedDirection = new Vector2(
                direction.x * cos - direction.y * sin,
                direction.x * sin + direction.y * cos);
            return origin + new Vector3(wavedDirection.x, 0f,
                wavedDirection.y) * HelodCasSupportUtility.IzlidSkyBeamLength;
        }

        private static void DrawIzlidSkyBeam(Vector3 origin, Vector3 destination)
        {
            origin.y = destination.y = AltitudeLayer.MoteOverhead.AltitudeFor()
                + 0.018f;
            int segments = Mathf.Min(izlidSkyBeamOuterMaterials.Length,
                izlidSkyBeamCoreMaterials.Length);
            Vector3 previous = origin;
            for (int i = 0; i < segments; i++)
            {
                float endProgress = (i + 1f) / segments;
                Vector3 current = Vector3.Lerp(origin, destination, endProgress);
                float widthProgress = i / (float)Mathf.Max(1, segments - 1);
                GenDraw.DrawLineBetween(previous, current,
                    izlidSkyBeamOuterMaterials[i],
                    Mathf.Lerp(0.095f, 0.022f, widthProgress));
                GenDraw.DrawLineBetween(previous, current,
                    izlidSkyBeamCoreMaterials[i],
                    Mathf.Lerp(0.030f, 0.005f, widthProgress));
                previous = current;
            }
        }

        private static void DrawIzlidTargetSignal(Pawn operatorPawn,
            IntVec3 target, float intensity)
        {
            Vector3 targetPosition = target.ToVector3Shifted();
            DrawIzlidBeam(operatorPawn.DrawPos, targetPosition, intensity);
            DrawLaserAimGlow(target, intensity, 1.05f, 0.35f);
        }

        private static void DrawIzlidBeam(Vector3 origin, Vector3 destination,
            float intensity = -1f)
        {
            origin.y = destination.y = AltitudeLayer.MoteOverhead.AltitudeFor()
                + 0.018f;
            Material outer = izlidBeamOuterMaterial;
            Material core = izlidBeamCoreMaterial;
            if (intensity >= 0f)
            {
                int materialIndex = PulseMaterialIndex(intensity);
                outer = pulsingBeamOuterMaterials[materialIndex];
                core = pulsingBeamCoreMaterials[materialIndex];
            }
            GenDraw.DrawLineBetween(origin, destination, outer, 0.075f);
            GenDraw.DrawLineBetween(origin, destination, core, 0.022f);
        }

        private static void DrawLaserAimGlow(IntVec3 target, float intensity,
            float baseSize, float pulseAmount)
        {
            Vector3 position = target.ToVector3Shifted();
            position.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.02f;
            float size = baseSize + Mathf.Clamp01(intensity) * pulseAmount;
            Matrix4x4 matrix = Matrix4x4.TRS(position, Quaternion.identity,
                new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, matrix,
                laserAimGlowMaterials[PulseMaterialIndex(intensity)], 0);
        }

        private static void DrawAircraft(HelodCasStrike strike, int now)
        {
            Material material = strike?.Plan == null ? null
                : AircraftMaterial(strike.Plan.AircraftKind);
            if (material == null || strike?.Plan == null || !strike.ShouldDrawAircraft(now))
            {
                return;
            }
            Vector3 position = strike.AircraftDrawPosition(now);
            position.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            Vector2 direction = strike.AircraftDrawDirection(now);
            float rotation = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
            float size = HelodCasSupportUtility.AircraftDrawSize(
                strike.Plan.AircraftKind) * strike.AircraftDrawScale(now);
            Matrix4x4 matrix = Matrix4x4.TRS(position,
                Quaternion.AngleAxis(rotation, Vector3.up), new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }

        private static void DrawFallingBomb(HelodCasFallingBomb bomb, int now)
        {
            Material material = bomb == null ? null
                : MunitionMaterial(bomb.AttackKind);
            if (material == null || bomb == null || now < bomb.ReleaseTick)
            {
                return;
            }
            float progress = bomb.Progress(now);
            float smoothProgress = progress * progress * (3f - 2f * progress);
            Vector3 position = Vector3.Lerp(bomb.ReleasePosition,
                bomb.ImpactCell.ToVector3Shifted(), progress);
            position.y = Mathf.Lerp(AltitudeLayer.MoteOverhead.AltitudeFor(),
                AltitudeLayer.Projectile.AltitudeFor(), smoothProgress);
            float size = Mathf.Lerp(1.05f, 0.5f, smoothProgress)
                * HelodCasSupportUtility.MunitionDrawScale;
            Vector2 drawDirection = bomb.AttackKind == HelodCasAttackKind.Hydra70
                || HelodCasSupportUtility.UsesSequentialTargets(bomb.AttackKind)
                ? bomb.FlightDirection : bomb.ApproachDirection;
            float rotation = Mathf.Atan2(drawDirection.x,
                drawDirection.y) * Mathf.Rad2Deg;
            Matrix4x4 matrix = Matrix4x4.TRS(position,
                Quaternion.AngleAxis(rotation, Vector3.up), new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref strikes, "helodCasSupportStrikes", LookMode.Deep);
            Scribe_Collections.Look(ref fallingBombs, "helodCasFallingBombs", LookMode.Deep);
            Scribe_Collections.Look(ref playtimeStates, "helodCasPlaytimeStates",
                LookMode.Deep);
            if (strikes == null)
            {
                strikes = new List<HelodCasStrike>();
            }
            if (fallingBombs == null)
            {
                fallingBombs = new List<HelodCasFallingBomb>();
            }
            if (playtimeStates == null)
            {
                playtimeStates = new List<HelodCasPlaytimeState>();
            }
        }
    }

    public sealed class JobDriver_CASStationaryGuidance : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil guide = new Toil
            {
                tickAction = delegate
                {
                    MapComponent_HelodCasSupport support = pawn.Map?
                        .GetComponent<MapComponent_HelodCasSupport>();
                    if (support == null || !support.RequiresStationaryGuidance(pawn))
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                        return;
                    }
                    support.FaceLaserGuidanceDirection(pawn,
                        Find.TickManager.TicksGame);
                },
                defaultCompleteMode = ToilCompleteMode.Never
            };
            yield return guide;
        }
    }

    public sealed class HelodCasFallingBomb : IExposable
    {
        private float releaseX;
        private float releaseZ;
        private IntVec3 impactCell;
        private Pawn caller;
        private int releaseTick;
        private int impactTick;
        private float approachX;
        private float approachZ;
        private HelodCasAttackKind attackKind;
        private Thing guidedTarget;

        public Vector3 ReleasePosition => new Vector3(releaseX, 0f, releaseZ);
        public IntVec3 ImpactCell => guidedTarget != null && guidedTarget.Spawned
            && !guidedTarget.Destroyed ? guidedTarget.Position : impactCell;
        public Pawn Caller => caller;
        public int ReleaseTick => releaseTick;
        public int ImpactTick => impactTick;
        public Vector2 ApproachDirection => new Vector2(approachX, approachZ).normalized;
        public HelodCasAttackKind AttackKind => attackKind;
        public Vector2 FlightDirection
        {
            get
            {
                IntVec3 currentImpact = ImpactCell;
                Vector2 direction = new Vector2(currentImpact.x + 0.5f - releaseX,
                    currentImpact.z + 0.5f - releaseZ);
                return direction.sqrMagnitude > 0.0001f
                    ? direction.normalized : ApproachDirection;
            }
        }

        public HelodCasFallingBomb()
        {
        }

        public HelodCasFallingBomb(Vector3 releasePosition, IntVec3 impactCell,
            Pawn caller, int releaseTick, int impactTick, Vector2 approachDirection,
            HelodCasAttackKind attackKind, Thing guidedTarget = null)
        {
            releaseX = releasePosition.x;
            releaseZ = releasePosition.z;
            this.impactCell = impactCell;
            this.caller = caller;
            this.releaseTick = releaseTick;
            this.impactTick = impactTick;
            approachX = approachDirection.x;
            approachZ = approachDirection.y;
            this.attackKind = attackKind;
            this.guidedTarget = guidedTarget;
        }

        public float Progress(int now)
        {
            return Mathf.InverseLerp(releaseTick, impactTick, now);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref releaseX, "releaseX", 0f);
            Scribe_Values.Look(ref releaseZ, "releaseZ", 0f);
            Scribe_Values.Look(ref impactCell, "impactCell");
            Scribe_References.Look(ref caller, "caller");
            Scribe_Values.Look(ref releaseTick, "releaseTick", 0);
            Scribe_Values.Look(ref impactTick, "impactTick", 0);
            Scribe_Values.Look(ref approachX, "approachX", 0f);
            Scribe_Values.Look(ref approachZ, "approachZ", 1f);
            Scribe_Values.Look(ref attackKind, "attackKind", HelodCasAttackKind.Bombing);
            Scribe_References.Look(ref guidedTarget, "guidedTarget");
        }
    }

    public sealed class HelodCasPlaytimeState : IExposable
    {
        private HelodForwardBase forwardBase;
        private int remainingPlaytime;
        private int nextDecayTick;
        private int reservedAircraftCount;
        private bool flightRequested;
        private HelodCasAircraftKind aircraftKind;
        private List<int> ammunition = new List<int>();

        public HelodForwardBase ForwardBase => forwardBase;
        public int RemainingPlaytime => remainingPlaytime;
        public int ReservedAircraftCount => reservedAircraftCount;
        public bool FlightRequested => flightRequested;
        public bool IsActive => flightRequested && remainingPlaytime > 0;
        public HelodCasAircraftKind AircraftKind => aircraftKind;

        public HelodCasPlaytimeState()
        {
        }

        public HelodCasPlaytimeState(HelodForwardBase forwardBase,
            HelodCasAircraftKind aircraftKind)
        {
            this.forwardBase = forwardBase;
            this.aircraftKind = aircraftKind;
        }

        public bool RequestFlight(int now)
        {
            if (IsActive)
            {
                return false;
            }
            flightRequested = true;
            remainingPlaytime = HelodCasSupportUtility.Playtime(aircraftKind);
            nextDecayTick = now + HelodCasSupportUtility.PlaytimeDecayTicks;
            reservedAircraftCount = 0;
            ResetAmmunition();
            return true;
        }

        public int AmmoRemaining(HelodCasAttackKind attackKind)
        {
            EnsureAmmunition();
            int index = (int)attackKind;
            return index >= 0 && index < ammunition.Count ? ammunition[index] : 0;
        }

        public int ExpectedAircraftCount(HelodCasAttackKind attackKind)
        {
            return aircraftKind == HelodCasAircraftKind.P47
                && attackKind == HelodCasAttackKind.Bombing
                && reservedAircraftCount > 0
                ? reservedAircraftCount
                : HelodCasSupportUtility.AircraftCountFor(aircraftKind);
        }

        public bool HasAmmo(HelodCasAttackKind attackKind, int amount)
        {
            return amount > 0 && AmmoRemaining(attackKind) >= amount;
        }

        public bool TryConsumeAmmo(HelodCasAttackKind attackKind, int amount)
        {
            if (!HasAmmo(attackKind, amount))
            {
                return false;
            }
            ammunition[(int)attackKind] -= amount;
            return true;
        }

        private void ResetAmmunition()
        {
            ammunition = new List<int>();
            for (int i = 0; i <= (int)HelodCasAttackKind.GBU54; i++)
            {
                ammunition.Add(HelodCasSupportUtility.InitialAmmo(aircraftKind,
                    (HelodCasAttackKind)i));
            }
        }

        private void EnsureAmmunition()
        {
            if (ammunition == null || ammunition.Count == 0)
            {
                ResetAmmunition();
            }
            while (ammunition.Count <= (int)HelodCasAttackKind.GBU54)
            {
                ammunition.Add(0);
            }
        }

        public bool TryConsumeAction(int now, HelodCasAttackKind attackKind,
            out int aircraftCount)
        {
            aircraftCount = 0;
            if (!IsActive)
            {
                return false;
            }
            remainingPlaytime--;
            aircraftCount = aircraftKind == HelodCasAircraftKind.P47
                && attackKind == HelodCasAttackKind.Bombing
                && reservedAircraftCount > 0
                ? reservedAircraftCount
                : HelodCasSupportUtility.AircraftCountFor(aircraftKind);
            if (aircraftKind == HelodCasAircraftKind.P47
                && attackKind == HelodCasAttackKind.Bombing)
            {
                reservedAircraftCount = 0;
            }
            return true;
        }

        public bool ConsumePenalty()
        {
            if (!IsActive)
            {
                return false;
            }
            remainingPlaytime--;
            return true;
        }

        public int ConsumeElapsedTime(int now)
        {
            if (!IsActive || nextDecayTick <= 0 || now < nextDecayTick)
            {
                return 0;
            }
            int elapsedPeriods = 1 + (now - nextDecayTick)
                / HelodCasSupportUtility.PlaytimeDecayTicks;
            int consumed = Mathf.Min(remainingPlaytime, elapsedPeriods);
            remainingPlaytime -= consumed;
            nextDecayTick += elapsedPeriods * HelodCasSupportUtility.PlaytimeDecayTicks;
            return consumed;
        }

        public void ReserveAircraft(int count)
        {
            reservedAircraftCount = Mathf.Clamp(count, 0,
                HelodCasSupportUtility.AircraftCount);
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref forwardBase, "forwardBase");
            Scribe_Values.Look(ref remainingPlaytime, "remainingPlaytime", 0);
            Scribe_Values.Look(ref nextDecayTick, "nextDecayTick", 0);
            Scribe_Values.Look(ref reservedAircraftCount, "reservedAircraftCount", 0);
            Scribe_Values.Look(ref flightRequested, "flightRequested", false);
            Scribe_Values.Look(ref aircraftKind, "aircraftKind",
                HelodCasAircraftKind.P47);
            Scribe_Collections.Look(ref ammunition, "ammunition", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureAmmunition();
            }
        }
    }

    public sealed class HelodCasAttackPlan : IExposable
    {
        private IntVec3 entryCell;
        private IntVec3 targetCell;
        private float approachX;
        private float approachZ;
        private float majorScatterRadius;
        private float minorScatterRadius;
        private int mountainRoofCount;
        private HelodCasGuidanceMode guidanceMode;
        private float routeLength;
        private Pawn targetPawn;
        private HelodCasAttackKind attackKind;
        private HelodCasAircraftKind aircraftKind;
        private List<IntVec3> designatedTargets = new List<IntVec3>();
        private List<Thing> designatedTargetThings = new List<Thing>();
        private int munitionCount;

        public IntVec3 EntryCell => entryCell;
        public IntVec3 TargetCell => targetCell;
        public Vector2 ApproachDirection => new Vector2(approachX, approachZ).normalized;
        public float MajorScatterRadius => majorScatterRadius;
        public float MinorScatterRadius => minorScatterRadius;
        public int MountainRoofCount => mountainRoofCount;
        public HelodCasGuidanceMode GuidanceMode => guidanceMode;
        public float RouteLength => routeLength;
        public Pawn TargetPawn => targetPawn;
        public HelodCasAttackKind AttackKind => attackKind;
        public HelodCasAircraftKind AircraftKind => aircraftKind;
        public int DesignatedTargetCount => designatedTargets?.Count ?? 0;
        public int MunitionCount => munitionCount > 0 ? munitionCount
            : HelodCasSupportUtility.MunitionCount(attackKind);
        public float FlightRouteLength => HelodCasSupportUtility
            .UsesAttackCorridor(attackKind)
            ? Mathf.Max(0f, routeLength - HelodCasSupportUtility.StrafeLength * 0.5f)
            : routeLength;
        public Vector3 StrafeStartPosition => StrafePoint(-0.5f);
        public Vector3 StrafeEndPosition => StrafePoint(0.5f);
        public IntVec3 StrafeStart => StrafeStartCell(entryCell, targetCell);
        public IntVec3 StrafeEnd => StrafeEndCell(entryCell, targetCell);

        public HelodCasAttackPlan()
        {
        }

        public HelodCasAttackPlan(IntVec3 entryCell, IntVec3 targetCell,
            HelodCasGuidanceMode guidanceMode, Map map, float majorScatterRadius,
            float minorScatterRadius,
            HelodCasAttackKind attackKind = HelodCasAttackKind.Bombing,
            HelodCasAircraftKind aircraftKind = HelodCasAircraftKind.P47,
            IEnumerable<IntVec3> designatedTargets = null,
            IEnumerable<Thing> designatedTargetThings = null,
            int munitionCount = 0)
        {
            this.entryCell = entryCell;
            this.targetCell = targetCell;
            this.guidanceMode = guidanceMode;
            this.majorScatterRadius = majorScatterRadius;
            this.minorScatterRadius = minorScatterRadius;
            this.attackKind = attackKind;
            this.aircraftKind = aircraftKind;
            this.designatedTargets = designatedTargets?.ToList()
                ?? new List<IntVec3>();
            this.designatedTargetThings = designatedTargetThings?.ToList()
                ?? new List<Thing>();
            this.munitionCount = munitionCount > 0 ? munitionCount
                : HelodCasSupportUtility.MunitionCount(attackKind);
            Vector2 direction = new Vector2(targetCell.x - entryCell.x,
                targetCell.z - entryCell.z).normalized;
            approachX = direction.x;
            approachZ = direction.y;
            routeLength = entryCell.DistanceTo(targetCell);
            mountainRoofCount = LineCells(entryCell, targetCell)
                .Count(cell => IsMountainRoof(map, cell));
            targetPawn = HelodCasSupportUtility.UsesAreaFire(attackKind)
                ? null : map?.thingGrid?.ThingsListAtFast(targetCell)
                    .OfType<Pawn>().FirstOrDefault();
        }

        public static IEnumerable<IntVec3> LineCells(IntVec3 start, IntVec3 end)
        {
            int x0 = start.x;
            int z0 = start.z;
            int x1 = end.x;
            int z1 = end.z;
            int dx = Mathf.Abs(x1 - x0);
            int dz = Mathf.Abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int error = dx - dz;
            while (true)
            {
                yield return new IntVec3(x0, 0, z0);
                if (x0 == x1 && z0 == z1)
                {
                    yield break;
                }
                int doubled = error * 2;
                if (doubled > -dz)
                {
                    error -= dz;
                    x0 += sx;
                }
                if (doubled < dx)
                {
                    error += dx;
                    z0 += sz;
                }
            }
        }

        private Vector3 StrafePoint(float lengthFactor)
        {
            Vector3 center = targetCell.ToVector3Shifted();
            Vector2 direction = ApproachDirection;
            return center + new Vector3(direction.x, 0f, direction.y)
                * (HelodCasSupportUtility.StrafeLength * lengthFactor);
        }

        public IntVec3 CurrentAimCell(Map map)
        {
            if (guidanceMode == HelodCasGuidanceMode.TalkOn && targetPawn != null
                && targetPawn.Spawned && !targetPawn.Dead && targetPawn.Map == map)
            {
                return targetPawn.Position;
            }
            return targetCell;
        }

        public IntVec3 DesignatedTargetCell(int index, Map map)
        {
            if (designatedTargets == null || designatedTargets.Count == 0)
            {
                return CurrentAimCell(map);
            }
            int safeIndex = Mathf.Clamp(index, 0, designatedTargets.Count - 1);
            if (designatedTargetThings != null
                && safeIndex < designatedTargetThings.Count)
            {
                Thing thing = designatedTargetThings[safeIndex];
                if (thing != null && thing.Spawned && !thing.Destroyed && thing.Map == map)
                {
                    return thing.Position;
                }
            }
            return designatedTargets[safeIndex];
        }

        public Thing DesignatedTargetThing(int index)
        {
            if (designatedTargetThings == null || designatedTargetThings.Count == 0)
            {
                return null;
            }
            int safeIndex = Mathf.Clamp(index, 0, designatedTargetThings.Count - 1);
            Thing thing = designatedTargetThings[safeIndex];
            return thing != null && !thing.Destroyed ? thing : null;
        }

        public Vector3 CurrentStrafeStartPosition(Map map)
        {
            IntVec3 centerCell = CurrentAimCell(map);
            Vector3 center = centerCell.ToVector3Shifted();
            Vector2 direction = ApproachDirection;
            return center - new Vector3(direction.x, 0f, direction.y)
                * (HelodCasSupportUtility.StrafeLength * 0.5f);
        }

        public static IntVec3 StrafeStartCell(IntVec3 entry, IntVec3 center)
        {
            return StrafeBoundaryCell(entry, center, -0.5f);
        }

        public static IntVec3 StrafeEndCell(IntVec3 entry, IntVec3 center)
        {
            return StrafeBoundaryCell(entry, center, 0.5f);
        }

        private static IntVec3 StrafeBoundaryCell(IntVec3 entry, IntVec3 center,
            float lengthFactor)
        {
            Vector2 direction = new Vector2(center.x - entry.x, center.z - entry.z).normalized;
            return new IntVec3(
                Mathf.RoundToInt(center.x + direction.x
                    * HelodCasSupportUtility.StrafeLength * lengthFactor),
                0,
                Mathf.RoundToInt(center.z + direction.y
                    * HelodCasSupportUtility.StrafeLength * lengthFactor));
        }

        public static bool IsMountainRoof(Map map, IntVec3 cell)
        {
            RoofDef roof = map?.roofGrid?.RoofAt(cell);
            return roof != null && roof.isThickRoof;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref entryCell, "entryCell");
            Scribe_Values.Look(ref targetCell, "targetCell");
            Scribe_Values.Look(ref approachX, "approachX", 0f);
            Scribe_Values.Look(ref approachZ, "approachZ", 1f);
            Scribe_Values.Look(ref majorScatterRadius, "majorScatterRadius",
                HelodCasSupportUtility.MajorScatterRadius);
            Scribe_Values.Look(ref minorScatterRadius, "minorScatterRadius",
                HelodCasSupportUtility.MinorScatterRadius);
            Scribe_Values.Look(ref mountainRoofCount, "mountainRoofCount", 0);
            Scribe_Values.Look(ref guidanceMode, "guidanceMode", HelodCasGuidanceMode.TalkOn);
            Scribe_Values.Look(ref routeLength, "routeLength", 0f);
            Scribe_References.Look(ref targetPawn, "targetPawn");
            Scribe_Values.Look(ref attackKind, "attackKind", HelodCasAttackKind.Bombing);
            Scribe_Values.Look(ref aircraftKind, "aircraftKind",
                HelodCasAircraftKind.P47);
            Scribe_Collections.Look(ref designatedTargets, "designatedTargets",
                LookMode.Value);
            Scribe_Collections.Look(ref designatedTargetThings,
                "designatedTargetThings", LookMode.Reference);
            Scribe_Values.Look(ref munitionCount, "munitionCount", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                designatedTargets = designatedTargets ?? new List<IntVec3>();
                designatedTargetThings = designatedTargetThings ?? new List<Thing>();
            }
        }
    }

    public sealed class HelodCasStrike : IExposable
    {
        private HelodCasAttackPlan plan;
        private Pawn caller;
        private HelodForwardBase forwardBase;
        private Thing_M8FlareTarget flareTarget;
        private int queuedTick;
        private int nextGuidanceAttemptTick;
        private int guidanceAttempts;
        private int goAroundCount;
        private float lastGuidanceChance;
        private float lastGuidanceRoll;
        private HelodCasRunState runState;
        private HelodCasAircraftPhase aircraftPhase;
        private int currentAircraftIndex;
        private int aircraftCount = HelodCasSupportUtility.AircraftCount;
        private int aircraftEntryTick;
        private int phaseStartTick;
        private int phaseEndTick;
        private int bombReleaseTick;
        private int cancellationLockTick;
        private int nextAircraftEntryTick;
        private bool bombPairReleased;
        private int goAroundStartTick;
        private int goAroundExitTick;
        private int goAroundTurnSign = 1;
        private bool aborting;
        private Vector3 abortStartPosition;
        private Vector2 abortStartDirection;
        private int abortTurnSign = 1;
        private float abortSignedAngle;
        private int abortTurnTicks;
        private Vector2 abortExitDirection;
        private Vector3 recoveryStartPosition;
        private float recoveryInitialSpeed;
        private float recoveryStartScale = 0.92f;
        private float nextStrafeBurstTick;
        private float a10CStrafeRoundAccumulator;
        private int pendingStrafeRounds;
        private int a10CStrafeRoundsQueued;
        private int a10CStrafeSequenceIndex;
        private Vector3 strafeExitStartPosition;
        private Vector2 strafeExitStartDirection;
        private Vector2 strafeExitDirection;
        private float strafeExitSignedAngle;
        private int strafeExitTurnTicks;
        private float strafeExitStartScale = HelodCasSupportUtility.StrafeMinimumScale;
        private Vector3 fixedGuidanceDevicePosition;
        private bool fixedGuidanceDevicePositionInitialized;

        public HelodCasAttackPlan Plan => plan;
        public Pawn Caller => caller;
        public HelodForwardBase ForwardBase => forwardBase;
        public HelodCasRunState RunState => runState;
        public HelodCasAircraftPhase AircraftPhase => aircraftPhase;
        public int CurrentAircraftIndex => currentAircraftIndex;
        public int AircraftCount => aircraftCount;
        public int RecallableBombingAircraftCount => plan?.AttackKind
            == HelodCasAttackKind.Bombing && bombPairReleased
            ? Mathf.Max(0, aircraftCount - currentAircraftIndex) : 0;
        public int GuidanceAttempts => guidanceAttempts;
        public int GoAroundCount => goAroundCount;
        public float LastGuidanceChance => lastGuidanceChance;
        public float LastGuidanceRoll => lastGuidanceRoll;
        public int PhaseStartTick => phaseStartTick;
        public int PhaseEndTick => phaseEndTick;
        public int BombReleaseTick => bombReleaseTick;
        public int CancellationLockTick => cancellationLockTick;
        public bool RequiresStationaryGuidance => !aborting
            && aircraftPhase != HelodCasAircraftPhase.Complete;

        public Vector3 FixedGuidanceDevicePosition(Pawn operatorPawn,
            HelodCasLaserDesignatorExtension designator)
        {
            if (!fixedGuidanceDevicePositionInitialized)
            {
                Vector3 forward = operatorPawn.Rotation.FacingCell.ToVector3();
                Vector3 right = new Vector3(forward.z, 0f, -forward.x);
                fixedGuidanceDevicePosition = operatorPawn.DrawPos
                    + forward * designator.guidanceGraphicForwardOffset
                    + right * designator.guidanceGraphicLateralOffset;
                fixedGuidanceDevicePositionInitialized = true;
            }
            return fixedGuidanceDevicePosition;
        }

        public bool IzlidSkySignalActive(int now)
        {
            if (aborting || runState == HelodCasRunState.Attacking)
            {
                return false;
            }
            int approachStart = runState == HelodCasRunState.Approaching
                ? queuedTick : nextGuidanceAttemptTick
                    - HelodCasSupportUtility.ArrivalDelayTicks;
            return now >= approachStart && now < approachStart
                + HelodCasSupportUtility.IzlidSkySignalTicks;
        }

        public bool IzlidTargetSignalActive(int now)
        {
            if (aborting)
            {
                return false;
            }
            int approachStart = runState == HelodCasRunState.Approaching
                ? queuedTick : nextGuidanceAttemptTick
                    - HelodCasSupportUtility.ArrivalDelayTicks;
            int targetStart = approachStart
                    + HelodCasSupportUtility.IzlidSkySignalTicks
                    + HelodCasSupportUtility.IzlidSignalPauseTicks;
            return now >= targetStart && now < targetStart
                + HelodCasSupportUtility.IzlidTargetSignalTicks;
        }

        public int IzlidTargetSignalStartTick => nextGuidanceAttemptTick
            - (HelodCasSupportUtility.ArrivalDelayTicks
                - HelodCasSupportUtility.IzlidSkySignalTicks
                - HelodCasSupportUtility.IzlidSignalPauseTicks);

        public HelodCasStrike()
        {
        }

        public HelodCasStrike(HelodCasAttackPlan plan, Pawn caller,
            HelodForwardBase forwardBase, Thing_M8FlareTarget flareTarget,
            int queuedTick, int firstGuidanceTick, int aircraftCount)
        {
            this.plan = plan;
            this.caller = caller;
            this.forwardBase = forwardBase;
            this.flareTarget = flareTarget;
            this.queuedTick = queuedTick;
            this.aircraftCount = Mathf.Max(1, aircraftCount);
            nextGuidanceAttemptTick = firstGuidanceTick;
            runState = HelodCasRunState.Approaching;
            aircraftPhase = HelodCasAircraftPhase.NotStarted;
        }

        public bool GuidanceValid(Map map)
        {
            if (aborting)
            {
                return true;
            }
            if (plan == null)
            {
                return false;
            }
            if (caller == null || !caller.Spawned || caller.Map != map
                || caller.Dead || caller.Downed)
            {
                return false;
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.TalkOn)
            {
                return true;
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.Flare)
            {
                return flareTarget != null && flareTarget.IsActiveFlare && flareTarget.Map == map;
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.Laser)
            {
                return HelodCasSupportUtility.TryGetLaserDesignatorOperator(caller,
                    map, plan.CurrentAimCell(map), out _, out _);
            }
            return false;
        }

        public bool NeedsGuidanceAttempt(int now)
        {
            return runState != HelodCasRunState.Attacking
                && now >= nextGuidanceAttemptTick;
        }

        public int TicksUntilGuidanceAttempt(int now)
        {
            return Mathf.Max(0, nextGuidanceAttemptTick - now);
        }

        public float CurrentGuidanceChance(Map map)
        {
            return HelodCasSupportUtility.GuidanceSuccessChance(plan, caller, map,
                goAroundCount);
        }

        public bool TryGuidance(Map map)
        {
            lastGuidanceChance = CurrentGuidanceChance(map);
            lastGuidanceRoll = Rand.Value;
            guidanceAttempts++;
            if (lastGuidanceRoll <= lastGuidanceChance)
            {
                runState = HelodCasRunState.Attacking;
                BeginAircraft(1, Find.TickManager.TicksGame);
                return true;
            }

            runState = HelodCasRunState.GoAround;
            aircraftPhase = HelodCasAircraftPhase.NotStarted;
            goAroundCount++;
            goAroundStartTick = Find.TickManager.TicksGame;
            goAroundExitTick = goAroundStartTick + HelodCasSupportUtility.GoAroundExitTicks;
            goAroundTurnSign = Rand.Bool ? 1 : -1;
            int minimumGoAroundTicks = plan.AircraftKind == HelodCasAircraftKind.A10C
                ? HelodCasSupportUtility.A10CGoAroundMinimumTicks
                : HelodCasSupportUtility.GoAroundMinimumTicks;
            int maximumGoAroundTicks = plan.AircraftKind == HelodCasAircraftKind.A10C
                ? HelodCasSupportUtility.A10CGoAroundMaximumTicks
                : HelodCasSupportUtility.GoAroundMaximumTicks;
            nextGuidanceAttemptTick = Find.TickManager.TicksGame
                + Rand.RangeInclusive(minimumGoAroundTicks, maximumGoAroundTicks);
            return false;
        }

        public HelodCasAircraftTickEvent TickAircraft(int now, Map map)
        {
            if (runState != HelodCasRunState.Attacking)
            {
                return HelodCasAircraftTickEvent.None;
            }

            if (plan.AircraftKind == HelodCasAircraftKind.A10C
                && plan.AttackKind == HelodCasAttackKind.Strafing
                && a10CStrafeRoundsQueued >= HelodCasSupportUtility.A10CStrafeRoundCount
                && (aircraftPhase == HelodCasAircraftPhase.StrafeApproach
                    || aircraftPhase == HelodCasAircraftPhase.Strafing))
            {
                BeginStrafeExit(now, map, AircraftDrawPosition(now),
                    AircraftDrawScale(now), false);
            }

            if (aircraftPhase == HelodCasAircraftPhase.Entry && now >= phaseEndTick)
            {
                if (plan.AttackKind == HelodCasAttackKind.Strafing)
                {
                    BeginStrafeApproach(now);
                }
                else if (HelodCasSupportUtility.RequiresDive(plan.AircraftKind,
                    plan.AttackKind))
                {
                    BeginDive(now);
                }
                else
                {
                    BeginLevelAttack(now);
                }
            }

            if (aircraftPhase == HelodCasAircraftPhase.Dive)
            {
                if (!bombPairReleased && now >= bombReleaseTick)
                {
                    bombPairReleased = true;
                    return HelodCasAircraftTickEvent.ReleaseBombPair;
                }
                if (now >= phaseEndTick)
                {
                    BeginRecovery(now, plan.TargetCell.ToVector3Shifted(),
                        HelodCasSupportUtility.AircraftDiveMinimumSpeed, 0.92f);
                }
            }

            if (aircraftPhase == HelodCasAircraftPhase.LevelAttack)
            {
                if (!bombPairReleased && now >= bombReleaseTick)
                {
                    bombPairReleased = true;
                    return HelodCasAircraftTickEvent.ReleaseBombPair;
                }
                if (now >= phaseEndTick)
                {
                    BeginRecovery(now, plan.TargetCell.ToVector3Shifted(),
                        HelodCasSupportUtility.AircraftAttackSpeed, 1.15f);
                }
            }

            if (aircraftPhase == HelodCasAircraftPhase.Recovery && now >= phaseEndTick)
            {
                if (aborting
                    || currentAircraftIndex >= aircraftCount)
                {
                    aircraftPhase = HelodCasAircraftPhase.Complete;
                    return HelodCasAircraftTickEvent.Complete;
                }
                if (now >= nextAircraftEntryTick)
                {
                    BeginAircraft(currentAircraftIndex + 1, now);
                }
            }
            if (aircraftPhase == HelodCasAircraftPhase.AbortTurn && now >= phaseEndTick)
            {
                aircraftPhase = HelodCasAircraftPhase.Complete;
                return HelodCasAircraftTickEvent.Complete;
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeApproach
                && now >= phaseEndTick)
            {
                BeginStrafing(now);
            }
            else if (aircraftPhase == HelodCasAircraftPhase.StrafeApproach
                && TryQueueStrafeFire(now))
            {
                return HelodCasAircraftTickEvent.StrafeBurst;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Strafing)
            {
                if (now < phaseEndTick && TryQueueStrafeFire(now))
                {
                    return HelodCasAircraftTickEvent.StrafeBurst;
                }
                if (now >= phaseEndTick)
                {
                    BeginStrafeExit(now, map, AircraftDrawPosition(now),
                        HelodCasSupportUtility.StrafeMinimumScale, false);
                }
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeExit
                && now >= phaseEndTick)
            {
                if (aborting
                    || currentAircraftIndex >= aircraftCount)
                {
                    aircraftPhase = HelodCasAircraftPhase.Complete;
                    return HelodCasAircraftTickEvent.Complete;
                }
                if (now >= nextAircraftEntryTick)
                {
                    BeginAircraft(currentAircraftIndex + 1, now);
                }
            }
            return HelodCasAircraftTickEvent.None;
        }

        private void BeginAircraft(int aircraftIndex, int now)
        {
            currentAircraftIndex = aircraftIndex;
            aircraftPhase = HelodCasAircraftPhase.Entry;
            aircraftEntryTick = now;
            phaseStartTick = now;
            float terminalApproachDistance = plan.AttackKind == HelodCasAttackKind.Strafing
                ? HelodCasSupportUtility.StrafeApproachDistanceFor(plan.AircraftKind)
                : HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind);
            float entryDistance = Mathf.Max(0f,
                plan.FlightRouteLength - terminalApproachDistance);
            phaseEndTick = now + Mathf.Max(1,
                Mathf.CeilToInt(entryDistance / HelodCasSupportUtility.AircraftAttackSpeed));
            nextAircraftEntryTick = now + HelodCasSupportUtility.FollowupEntryIntervalTicks;
            bombPairReleased = false;
            bombReleaseTick = 0;
            cancellationLockTick = 0;
            nextStrafeBurstTick = 0f;
            a10CStrafeRoundAccumulator = 0f;
            pendingStrafeRounds = 0;
            a10CStrafeRoundsQueued = 0;
            a10CStrafeSequenceIndex = 0;
        }

        private void BeginDive(int now)
        {
            aircraftPhase = HelodCasAircraftPhase.Dive;
            phaseStartTick = now;
            float diveDistance = Mathf.Min(plan.RouteLength,
                HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind));
            float averageDiveSpeed = (HelodCasSupportUtility.AircraftAttackSpeed
                + HelodCasSupportUtility.AircraftDiveMinimumSpeed) * 0.5f;
            int diveTicks = Mathf.Max(1,
                Mathf.RoundToInt(diveDistance / averageDiveSpeed));
            phaseEndTick = now + diveTicks;
            float releaseDistance = HelodCasSupportUtility.MunitionReleaseDistance(
                plan.AttackKind);
            float distanceBeforeRelease = Mathf.Max(0f, diveDistance
                - Mathf.Min(releaseDistance, diveDistance));
            int releaseOffset = 1;
            while (releaseOffset < diveTicks
                && DiveTravelDistance(releaseOffset, diveTicks, diveDistance)
                    < distanceBeforeRelease)
            {
                releaseOffset++;
            }
            bombReleaseTick = now + releaseOffset;
            cancellationLockTick = bombReleaseTick
                - HelodCasSupportUtility.CancellationLockBeforeReleaseTicks;
        }

        private void BeginLevelAttack(int now)
        {
            aircraftPhase = HelodCasAircraftPhase.LevelAttack;
            phaseStartTick = now;
            float attackDistance = Mathf.Min(plan.RouteLength,
                HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind));
            int attackTicks = Mathf.Max(1, Mathf.RoundToInt(attackDistance
                / HelodCasSupportUtility.AircraftAttackSpeed));
            phaseEndTick = now + attackTicks;
            float releaseDistance = HelodCasSupportUtility.MunitionReleaseDistance(
                plan.AttackKind);
            float distanceBeforeRelease = Mathf.Max(0f, attackDistance
                - Mathf.Min(releaseDistance, attackDistance));
            bombReleaseTick = now + Mathf.Clamp(Mathf.RoundToInt(
                distanceBeforeRelease / HelodCasSupportUtility.AircraftAttackSpeed),
                1, attackTicks);
            cancellationLockTick = bombReleaseTick
                - HelodCasSupportUtility.CancellationLockBeforeReleaseTicks;
        }

        private void BeginRecovery(int now, Vector3 startPosition, float startSpeed,
            float startScale)
        {
            aircraftPhase = HelodCasAircraftPhase.Recovery;
            phaseStartTick = now;
            phaseEndTick = now + HelodCasSupportUtility.AircraftRecoveryTicks;
            recoveryStartPosition = startPosition;
            recoveryInitialSpeed = startSpeed;
            recoveryStartScale = startScale;
        }

        private void BeginStrafeApproach(int now)
        {
            aircraftPhase = HelodCasAircraftPhase.StrafeApproach;
            phaseStartTick = now;
            float distance = Mathf.Min(plan.FlightRouteLength,
                HelodCasSupportUtility.StrafeApproachDistanceFor(plan.AircraftKind));
            float strafeSpeed = HelodCasSupportUtility.AircraftAttackSpeed
                * HelodCasSupportUtility.StrafeSpeedFactor;
            float averageSpeed = (HelodCasSupportUtility.AircraftAttackSpeed
                + strafeSpeed) * 0.5f;
            phaseEndTick = now + Mathf.Max(1,
                Mathf.RoundToInt(distance / averageSpeed));
            nextStrafeBurstTick = now;
        }

        private bool TryQueueStrafeFire(int now)
        {
            if (now < nextStrafeBurstTick || !CanFireStrafeAt(now))
            {
                return false;
            }
            if (plan.AircraftKind == HelodCasAircraftKind.A10C)
            {
                int remainingRounds = HelodCasSupportUtility.A10CStrafeRoundCount
                    - a10CStrafeRoundsQueued;
                if (remainingRounds <= 0)
                {
                    return false;
                }
                nextStrafeBurstTick = now + 1f;
                a10CStrafeRoundAccumulator += HelodCasSupportUtility.A10CRoundsPerTick;
                pendingStrafeRounds = Mathf.Min(remainingRounds,
                    Mathf.FloorToInt(a10CStrafeRoundAccumulator));
                a10CStrafeRoundAccumulator -= pendingStrafeRounds;
                a10CStrafeRoundsQueued += pendingStrafeRounds;
                return pendingStrafeRounds > 0;
            }

            nextStrafeBurstTick += HelodCasSupportUtility.StrafeTicksPerBurst;
            pendingStrafeRounds = HelodCasSupportUtility.StrafeRoundsPerBurst;
            return true;
        }

        public int ConsumePendingStrafeRounds()
        {
            int rounds = pendingStrafeRounds;
            pendingStrafeRounds = 0;
            return rounds;
        }

        public bool NextA10CStrafeRoundIsHighExplosive(out int shotIndex)
        {
            shotIndex = a10CStrafeSequenceIndex;
            bool highExplosive = shotIndex % 5 == 4;
            a10CStrafeSequenceIndex++;
            return highExplosive;
        }

        private void BeginStrafing(int now)
        {
            aircraftPhase = HelodCasAircraftPhase.Strafing;
            phaseStartTick = now;
            float strafeSpeed = HelodCasSupportUtility.AircraftAttackSpeed
                * HelodCasSupportUtility.StrafeSpeedFactor;
            int strafeTicks = Mathf.Max(1, Mathf.RoundToInt(
                HelodCasSupportUtility.StrafeLength / strafeSpeed));
            if (plan.AircraftKind == HelodCasAircraftKind.A10C)
            {
                int remainingRounds = Mathf.Max(0,
                    HelodCasSupportUtility.A10CStrafeRoundCount
                        - a10CStrafeRoundsQueued);
                int remainingFireTicks = Mathf.CeilToInt(remainingRounds
                    / HelodCasSupportUtility.A10CRoundsPerTick) + 1;
                strafeTicks = Mathf.Max(strafeTicks, remainingFireTicks);
            }
            phaseEndTick = now + strafeTicks;
        }

        private void BeginStrafeExit(int now, Map map, Vector3 startPosition,
            float startScale, bool abortTurn)
        {
            aircraftPhase = HelodCasAircraftPhase.StrafeExit;
            phaseStartTick = now;
            strafeExitStartPosition = startPosition;
            strafeExitStartDirection = plan.ApproachDirection;
            strafeExitStartScale = startScale;
            float turnAngle = (abortTurn
                ? HelodCasSupportUtility.AbortTurnAngleDegrees
                : HelodCasSupportUtility.StrafeExitTurnAngleDegrees)
                * Mathf.Deg2Rad;
            int turnSign = ChooseTurnSign(strafeExitStartPosition,
                strafeExitStartDirection, turnAngle,
                HelodCasSupportUtility.StrafeTurnRadius, map);
            strafeExitSignedAngle = turnAngle * turnSign;
            strafeExitDirection = RotateDirection(strafeExitStartDirection,
                strafeExitSignedAngle);
            float strafeSpeed = HelodCasSupportUtility.AircraftAttackSpeed
                * HelodCasSupportUtility.StrafeSpeedFactor;
            float averageSpeed = (strafeSpeed
                + HelodCasSupportUtility.AircraftAttackSpeed) * 0.5f;
            strafeExitTurnTicks = Mathf.Max(1, Mathf.RoundToInt(
                Mathf.Abs(strafeExitSignedAngle)
                    * HelodCasSupportUtility.StrafeTurnRadius / averageSpeed));
            Vector3 turnEnd = StrafeExitTurnPosition(strafeExitTurnTicks);
            float distanceToEdge = DistanceToMapExit(turnEnd, strafeExitDirection, map);
            int straightTicks = Mathf.Max(1, Mathf.CeilToInt(distanceToEdge
                / HelodCasSupportUtility.AircraftAttackSpeed));
            phaseEndTick = now + strafeExitTurnTicks + straightTicks;
        }

        private static float TransitionTravelDistance(float elapsed, float duration,
            float totalDistance, float startSpeed, float endSpeed)
        {
            duration = Mathf.Max(1f, duration);
            float clampedElapsed = Mathf.Clamp(elapsed, 0f, duration);
            float progress = clampedElapsed / duration;
            float unscaledDistance = startSpeed * clampedElapsed
                + (endSpeed - startSpeed) * duration * SmoothStepIntegral(progress);
            float unscaledTotal = (startSpeed + endSpeed) * 0.5f * duration;
            return totalDistance * unscaledDistance / unscaledTotal;
        }

        private static float SmoothStepIntegral(float progress)
        {
            progress = Mathf.Clamp01(progress);
            return progress * progress * progress * (1f - 0.5f * progress);
        }

        private static float DiveTravelDistance(float elapsed, float duration,
            float totalDistance)
        {
            duration = Mathf.Max(1f, duration);
            float clampedElapsed = Mathf.Clamp(elapsed, 0f, duration);
            float progress = clampedElapsed / duration;
            float unscaledDistance = HelodCasSupportUtility.AircraftAttackSpeed
                    * clampedElapsed
                + (HelodCasSupportUtility.AircraftDiveMinimumSpeed
                    - HelodCasSupportUtility.AircraftAttackSpeed)
                    * duration * SmoothStepIntegral(progress);
            float unscaledTotal = (HelodCasSupportUtility.AircraftAttackSpeed
                + HelodCasSupportUtility.AircraftDiveMinimumSpeed) * 0.5f * duration;
            return totalDistance * unscaledDistance / unscaledTotal;
        }

        private static float DiveSpeed(float elapsed, float duration,
            float totalDistance)
        {
            duration = Mathf.Max(1f, duration);
            float progress = Mathf.Clamp01(elapsed / duration);
            float smoothProgress = progress * progress * (3f - 2f * progress);
            float unscaledSpeed = Mathf.Lerp(
                HelodCasSupportUtility.AircraftAttackSpeed,
                HelodCasSupportUtility.AircraftDiveMinimumSpeed, smoothProgress);
            float unscaledTotal = (HelodCasSupportUtility.AircraftAttackSpeed
                + HelodCasSupportUtility.AircraftDiveMinimumSpeed) * 0.5f * duration;
            return unscaledSpeed * totalDistance / unscaledTotal * duration;
        }

        public bool BeginAbort(int now, Map map)
        {
            if (runState != HelodCasRunState.Attacking
                || aircraftPhase == HelodCasAircraftPhase.NotStarted)
            {
                return false;
            }

            aborting = true;
            nextAircraftEntryTick = int.MaxValue;
            if (aircraftPhase == HelodCasAircraftPhase.Entry
                || aircraftPhase == HelodCasAircraftPhase.StrafeApproach)
            {
                abortStartPosition = AircraftDrawPosition(now);
                abortStartDirection = AircraftDrawDirection(now);
                float turnAngle = HelodCasSupportUtility.AbortTurnAngleDegrees
                    * Mathf.Deg2Rad;
                abortTurnSign = ChooseTurnSign(abortStartPosition,
                    abortStartDirection, turnAngle,
                    HelodCasSupportUtility.AbortTurnRadius, map);
                abortSignedAngle = turnAngle * abortTurnSign;
                abortExitDirection = RotateDirection(abortStartDirection,
                    abortSignedAngle);
                abortTurnTicks = Mathf.Max(1, Mathf.RoundToInt(turnAngle
                    * HelodCasSupportUtility.AbortTurnRadius
                    / HelodCasSupportUtility.AircraftAttackSpeed));
                aircraftPhase = HelodCasAircraftPhase.AbortTurn;
                phaseStartTick = now;
                Vector3 turnEnd = TurnArcPosition(abortStartPosition,
                    abortStartDirection, abortSignedAngle,
                    HelodCasSupportUtility.AbortTurnRadius);
                int exitTicks = Mathf.Max(1, Mathf.CeilToInt(
                    DistanceToMapExit(turnEnd, abortExitDirection, map)
                    / HelodCasSupportUtility.AircraftAttackSpeed));
                phaseEndTick = now + abortTurnTicks + exitTicks;
                return true;
            }
            if (aircraftPhase == HelodCasAircraftPhase.LevelAttack)
            {
                Vector3 startPosition = AircraftDrawPosition(now);
                bombPairReleased = true;
                BeginRecovery(now, startPosition,
                    HelodCasSupportUtility.AircraftAttackSpeed, 1.15f);
                return true;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Dive)
            {
                Vector3 startPosition = AircraftDrawPosition(now);
                float startScale = AircraftDrawScale(now);
                float diveDistance = Mathf.Min(plan.RouteLength,
                    HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind));
                float startSpeed = DiveSpeed(now - phaseStartTick,
                    phaseEndTick - phaseStartTick, diveDistance);
                bombPairReleased = true;
                BeginRecovery(now, startPosition, startSpeed, startScale);
                return true;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Strafing)
            {
                Vector3 startPosition = AircraftDrawPosition(now);
                float startScale = AircraftDrawScale(now);
                BeginStrafeExit(now, map, startPosition, startScale, true);
                return true;
            }
            return aircraftPhase == HelodCasAircraftPhase.Recovery
                || aircraftPhase == HelodCasAircraftPhase.AbortTurn
                || aircraftPhase == HelodCasAircraftPhase.StrafeExit;
        }

        public bool CanCancel(int now, out string rejection)
        {
            rejection = null;
            if (aborting)
            {
                rejection = "HD_CAS_Cancel_NoActive".Translate().ToString();
                return false;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Complete)
            {
                rejection = "HD_CAS_Cancel_NoActive".Translate().ToString();
                return false;
            }
            if ((aircraftPhase == HelodCasAircraftPhase.Dive
                || aircraftPhase == HelodCasAircraftPhase.LevelAttack)
                && now >= cancellationLockTick)
            {
                rejection = "HD_CAS_Cancel_Locked".Translate().ToString();
                return false;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Recovery
                && currentAircraftIndex >= aircraftCount)
            {
                rejection = "HD_CAS_Cancel_NoFollowup".Translate().ToString();
                return false;
            }
            return true;
        }

        public IntVec3 NextImpactCell(Map map, int munitionIndex = 0)
        {
            Vector2 direction = plan.ApproachDirection;
            Vector2 lateral = new Vector2(-direction.y, direction.x);
            float angle = Rand.Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Rand.Value);
            float along = Mathf.Cos(angle) * plan.MajorScatterRadius * radius;
            float across = Mathf.Sin(angle) * plan.MinorScatterRadius * radius;
            if (HelodCasSupportUtility.IsGuidedMissile(plan.AttackKind))
            {
                along = 0f;
                across = 0f;
            }
            else if (plan.AttackKind == HelodCasAttackKind.GBU54
                && plan.GuidanceMode == HelodCasGuidanceMode.Laser
                && HelodCasSupportUtility.TryGetLaserDesignatorOperator(caller, map,
                    plan.CurrentAimCell(map), out _,
                    out HelodCasLaserDesignatorExtension designator)
                && designator.adjustsGbu54Scatter)
            {
                along *= HelodCasSupportUtility.GBU54LaserScatterMultiplier;
                across *= HelodCasSupportUtility.GBU54LaserScatterMultiplier;
            }
            IntVec3 center = HelodCasSupportUtility.UsesSequentialTargets(
                plan.AttackKind)
                ? plan.DesignatedTargetCell(munitionIndex, map)
                : plan.GuidanceMode == HelodCasGuidanceMode.Flare
                && flareTarget?.IsActiveFlare == true
                ? flareTarget.Position : plan.CurrentAimCell(map);
            if (plan.AttackKind == HelodCasAttackKind.Hydra70)
            {
                int count = plan.MunitionCount;
                float lineProgress = count <= 1 ? 0.5f
                    : (munitionIndex + 0.5f) / count;
                float lineOffset = Mathf.Lerp(-HelodCasSupportUtility.StrafeLength * 0.5f,
                    HelodCasSupportUtility.StrafeLength * 0.5f, lineProgress);
                center = new IntVec3(
                    Mathf.RoundToInt(center.x + direction.x * lineOffset), 0,
                    Mathf.RoundToInt(center.z + direction.y * lineOffset));
                along = Rand.Range(-1.25f, 1.25f);
                across = Rand.Range(-HelodCasSupportUtility.StrafeWidth * 0.5f,
                    HelodCasSupportUtility.StrafeWidth * 0.5f);
            }
            IntVec3 cell = new IntVec3(
                Mathf.RoundToInt(center.x + direction.x * along + lateral.x * across),
                0,
                Mathf.RoundToInt(center.z + direction.y * along + lateral.y * across));
            return new IntVec3(Mathf.Clamp(cell.x, 0, map.Size.x - 1), 0,
                Mathf.Clamp(cell.z, 0, map.Size.z - 1));
        }

        public float StrafeAimDistance(int now)
        {
            Vector3 aircraft = AircraftDrawPosition(now);
            Vector3 start = plan.StrafeStartPosition;
            Vector2 direction = plan.ApproachDirection;
            float aircraftDistance = (aircraft.x - start.x) * direction.x
                + (aircraft.z - start.z) * direction.y;
            float leadDistance = HelodCasSupportUtility.StrafeBulletLeadDistanceFor(
                plan.AircraftKind);
            return Mathf.Clamp(aircraftDistance
                + leadDistance,
                0f, HelodCasSupportUtility.StrafeLength);
        }

        private bool CanFireStrafeAt(int now)
        {
            if (aircraftPhase != HelodCasAircraftPhase.StrafeApproach
                && aircraftPhase != HelodCasAircraftPhase.Strafing)
            {
                return false;
            }
            if (plan.AircraftKind == HelodCasAircraftKind.A10C)
            {
                return a10CStrafeRoundsQueued
                    < HelodCasSupportUtility.A10CStrafeRoundCount;
            }
            Vector3 aircraft = AircraftDrawPosition(now);
            Vector3 start = plan.StrafeStartPosition;
            Vector2 direction = plan.ApproachDirection;
            float aircraftDistance = (aircraft.x - start.x) * direction.x
                + (aircraft.z - start.z) * direction.y;
            float leadDistance = HelodCasSupportUtility.StrafeBulletLeadDistanceFor(
                plan.AircraftKind);
            return aircraftDistance >= -leadDistance
                && aircraftDistance <= HelodCasSupportUtility.StrafeLength
                    - leadDistance;
        }

        public Vector3 AircraftDrawPosition(int now)
        {
            Vector3 entry = plan.EntryCell.ToVector3Shifted();
            Vector3 target = plan.TargetCell.ToVector3Shifted();
            Vector2 direction = plan.ApproachDirection;
            if (runState == HelodCasRunState.GoAround)
            {
                int retryApproachStart = nextGuidanceAttemptTick
                    - HelodCasSupportUtility.ArrivalDelayTicks;
                if (now >= retryApproachStart)
                {
                    return InboundToEntryPosition(entry, direction,
                        retryApproachStart, nextGuidanceAttemptTick, now);
                }
                return GoAroundPosition(entry, direction, now);
            }
            if (runState != HelodCasRunState.Attacking
                || aircraftPhase == HelodCasAircraftPhase.NotStarted)
            {
                return InboundToEntryPosition(entry, direction, queuedTick,
                    nextGuidanceAttemptTick, now);
            }
            if (aircraftPhase == HelodCasAircraftPhase.Entry)
            {
                float terminalApproachDistance = plan.AttackKind
                    == HelodCasAttackKind.Strafing
                    ? HelodCasSupportUtility.StrafeApproachDistanceFor(
                        plan.AircraftKind)
                    : HelodCasSupportUtility.AttackApproachDistance(
                        plan.AttackKind);
                float entryDistance = Mathf.Max(0f,
                    plan.FlightRouteLength - terminalApproachDistance);
                float runDistance = Mathf.Min(entryDistance,
                    Mathf.Max(0, now - aircraftEntryTick)
                    * HelodCasSupportUtility.AircraftAttackSpeed);
                return entry + new Vector3(direction.x, 0f, direction.y) * runDistance;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Dive)
            {
                float diveDistance = Mathf.Min(plan.RouteLength,
                    HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind));
                Vector3 diveStart = target
                    - new Vector3(direction.x, 0f, direction.y) * diveDistance;
                float runDistance = DiveTravelDistance(now - phaseStartTick,
                    phaseEndTick - phaseStartTick, diveDistance);
                return diveStart
                    + new Vector3(direction.x, 0f, direction.y) * runDistance;
            }
            if (aircraftPhase == HelodCasAircraftPhase.LevelAttack)
            {
                float attackDistance = Mathf.Min(plan.RouteLength,
                    HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind));
                Vector3 attackStart = target
                    - new Vector3(direction.x, 0f, direction.y) * attackDistance;
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return attackStart + new Vector3(direction.x, 0f, direction.y)
                    * (attackDistance * progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.AbortTurn)
            {
                return AbortTurnPosition(now);
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeApproach)
            {
                Vector3 strafeStart = plan.StrafeStartPosition;
                float approachDistance = Mathf.Min(plan.FlightRouteLength,
                    HelodCasSupportUtility.StrafeApproachDistanceFor(
                        plan.AircraftKind));
                Vector3 approachStart = strafeStart
                    - new Vector3(direction.x, 0f, direction.y) * approachDistance;
                float runDistance = TransitionTravelDistance(now - phaseStartTick,
                    phaseEndTick - phaseStartTick, approachDistance,
                    HelodCasSupportUtility.AircraftAttackSpeed,
                    HelodCasSupportUtility.AircraftAttackSpeed
                        * HelodCasSupportUtility.StrafeSpeedFactor);
                return approachStart
                    + new Vector3(direction.x, 0f, direction.y) * runDistance;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Strafing)
            {
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return plan.StrafeStartPosition
                    + new Vector3(direction.x, 0f, direction.y)
                    * (HelodCasSupportUtility.StrafeLength * progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeExit)
            {
                int strafeExitElapsed = Mathf.Max(0, now - phaseStartTick);
                if (strafeExitElapsed <= strafeExitTurnTicks)
                {
                    return StrafeExitTurnPosition(strafeExitElapsed);
                }
                Vector3 turnEnd = StrafeExitTurnPosition(strafeExitTurnTicks);
                return turnEnd + new Vector3(strafeExitDirection.x, 0f,
                    strafeExitDirection.y) * (strafeExitElapsed - strafeExitTurnTicks)
                    * HelodCasSupportUtility.AircraftAttackSpeed;
            }
            float elapsed = Mathf.Max(0f, now - phaseStartTick);
            float accelerationTicks = HelodCasSupportUtility.AircraftRecoveryTicks;
            float acceleratedTime = Mathf.Min(elapsed, accelerationTicks);
            float accelerationProgress = acceleratedTime / accelerationTicks;
            float smoothStepIntegral = SmoothStepIntegral(accelerationProgress);
            float recoveryDistance = recoveryInitialSpeed
                    * acceleratedTime
                + (HelodCasSupportUtility.AircraftRecoverySpeed
                    - recoveryInitialSpeed)
                    * accelerationTicks * smoothStepIntegral;
            if (elapsed > accelerationTicks)
            {
                recoveryDistance += (elapsed - accelerationTicks)
                    * HelodCasSupportUtility.AircraftRecoverySpeed;
            }
            return recoveryStartPosition
                + new Vector3(direction.x, 0f, direction.y) * recoveryDistance;
        }

        public bool ShouldDrawAircraft(int now)
        {
            if (runState == HelodCasRunState.Attacking)
            {
                return aircraftPhase != HelodCasAircraftPhase.Complete;
            }
            if (runState == HelodCasRunState.Approaching)
            {
                return true;
            }
            if (runState == HelodCasRunState.GoAround)
            {
                return now <= goAroundExitTick || now >= nextGuidanceAttemptTick
                    - HelodCasSupportUtility.ArrivalDelayTicks;
            }
            return false;
        }

        public Vector2 AircraftDrawDirection(int now)
        {
            Vector3 current = AircraftDrawPosition(now);
            Vector3 next = AircraftDrawPosition(now + 1);
            Vector3 delta = next - current;
            Vector2 direction = new Vector2(delta.x, delta.z);
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized : plan.ApproachDirection;
        }

        private static Vector3 InboundToEntryPosition(Vector3 entry, Vector2 direction,
            int startTick, int endTick, int now)
        {
            int duration = Mathf.Max(1, endTick - startTick);
            Vector3 outside = entry - new Vector3(direction.x, 0f, direction.y)
                * (HelodCasSupportUtility.AircraftAttackSpeed * duration);
            float progress = Mathf.InverseLerp(startTick, endTick, now);
            return Vector3.Lerp(outside, entry, progress);
        }

        private Vector3 GoAroundPosition(Vector3 entry, Vector2 direction, int now)
        {
            Vector2 lateral = new Vector2(-direction.y, direction.x);
            int elapsed = Mathf.Max(0, now - goAroundStartTick);
            float turnProgress = Mathf.Clamp01(elapsed
                / (float)HelodCasSupportUtility.GoAroundTurnTicks);
            float angle = turnProgress * Mathf.PI * goAroundTurnSign;
            Vector2 initialRadius = -lateral * goAroundTurnSign
                * HelodCasSupportUtility.GoAroundTurnRadius;
            Vector2 rotatedRadius = new Vector2(
                initialRadius.x * Mathf.Cos(angle) - initialRadius.y * Mathf.Sin(angle),
                initialRadius.x * Mathf.Sin(angle) + initialRadius.y * Mathf.Cos(angle));
            Vector2 turnCenter = new Vector2(entry.x, entry.z)
                + lateral * goAroundTurnSign * HelodCasSupportUtility.GoAroundTurnRadius;
            Vector3 turnPosition = new Vector3(turnCenter.x + rotatedRadius.x,
                0f, turnCenter.y + rotatedRadius.y);
            if (elapsed <= HelodCasSupportUtility.GoAroundTurnTicks)
            {
                return turnPosition;
            }
            float exitElapsed = elapsed - HelodCasSupportUtility.GoAroundTurnTicks;
            return turnPosition - new Vector3(direction.x, 0f, direction.y)
                * exitElapsed * HelodCasSupportUtility.GoAroundExitSpeed;
        }

        private Vector3 AbortTurnPosition(int now)
        {
            Vector2 direction = abortStartDirection.sqrMagnitude > 0.0001f
                ? abortStartDirection.normalized : plan.ApproachDirection;
            int elapsed = Mathf.Max(0, now - phaseStartTick);
            float turnProgress = Mathf.Clamp01(elapsed
                / (float)Mathf.Max(1, abortTurnTicks));
            Vector3 turnPosition = TurnArcPosition(abortStartPosition, direction,
                abortSignedAngle * turnProgress,
                HelodCasSupportUtility.AbortTurnRadius);
            if (elapsed <= abortTurnTicks)
            {
                return turnPosition;
            }
            float exitElapsed = elapsed - abortTurnTicks;
            return turnPosition + new Vector3(abortExitDirection.x, 0f,
                abortExitDirection.y)
                * exitElapsed * HelodCasSupportUtility.AircraftAttackSpeed;
        }

        private Vector3 StrafeExitTurnPosition(float elapsed)
        {
            float turnTicks = Mathf.Max(1f, strafeExitTurnTicks);
            float strafeSpeed = HelodCasSupportUtility.AircraftAttackSpeed
                * HelodCasSupportUtility.StrafeSpeedFactor;
            float turnDistance = Mathf.Abs(strafeExitSignedAngle)
                * HelodCasSupportUtility.StrafeTurnRadius;
            float travelled = TransitionTravelDistance(elapsed, turnTicks,
                turnDistance, strafeSpeed, HelodCasSupportUtility.AircraftAttackSpeed);
            float angleProgress = turnDistance > 0.001f
                ? travelled / turnDistance : 1f;
            float angle = strafeExitSignedAngle * angleProgress;
            return TurnArcPosition(strafeExitStartPosition, strafeExitStartDirection,
                angle, HelodCasSupportUtility.StrafeTurnRadius);
        }

        private static float DistanceToMapExit(Vector3 start, Vector2 direction, Map map)
        {
            direction.Normalize();
            float xDistance = float.PositiveInfinity;
            float zDistance = float.PositiveInfinity;
            if (direction.x < -0.0001f)
            {
                xDistance = (-1f - start.x) / direction.x;
            }
            else if (direction.x > 0.0001f)
            {
                xDistance = (map.Size.x + 1f - start.x) / direction.x;
            }
            if (direction.y < -0.0001f)
            {
                zDistance = (-1f - start.z) / direction.y;
            }
            else if (direction.y > 0.0001f)
            {
                zDistance = (map.Size.z + 1f - start.z) / direction.y;
            }
            return Mathf.Max(0f, Mathf.Min(xDistance, zDistance));
        }

        private static int ChooseTurnSign(Vector3 start, Vector2 direction,
            float turnAngle, float radius, Map map)
        {
            Vector3 leftEnd = TurnArcPosition(start, direction, turnAngle, radius);
            Vector2 leftDirection = RotateDirection(direction, turnAngle);
            float leftExitDistance = DistanceToMapExit(leftEnd, leftDirection, map);
            Vector3 rightEnd = TurnArcPosition(start, direction, -turnAngle, radius);
            Vector2 rightDirection = RotateDirection(direction, -turnAngle);
            float rightExitDistance = DistanceToMapExit(rightEnd, rightDirection, map);
            return leftExitDistance <= rightExitDistance ? 1 : -1;
        }

        private static Vector2 RotateDirection(Vector2 direction, float angle)
        {
            return new Vector2(
                direction.x * Mathf.Cos(angle) - direction.y * Mathf.Sin(angle),
                direction.x * Mathf.Sin(angle) + direction.y * Mathf.Cos(angle))
                .normalized;
        }

        private static Vector3 TurnArcPosition(Vector3 start, Vector2 direction,
            float signedAngle, float radius)
        {
            float sign = signedAngle >= 0f ? 1f : -1f;
            Vector2 lateral = new Vector2(-direction.y, direction.x);
            Vector2 radiusVector = -lateral * sign * radius;
            Vector2 center = new Vector2(start.x, start.z) - radiusVector;
            Vector2 rotatedRadius = RotateDirection(radiusVector, signedAngle) * radius;
            return new Vector3(center.x + rotatedRadius.x, start.y,
                center.y + rotatedRadius.y);
        }

        public float AircraftDrawScale(int now)
        {
            if (runState != HelodCasRunState.Attacking)
            {
                return 1.15f;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Dive)
            {
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return Mathf.SmoothStep(1.15f, 0.92f, progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.Recovery)
            {
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return Mathf.SmoothStep(recoveryStartScale, 1.15f, progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.Strafing)
            {
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return Mathf.SmoothStep(1.15f,
                    HelodCasSupportUtility.StrafeMinimumScale,
                    progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeExit)
            {
                float progress = Mathf.InverseLerp(phaseStartTick,
                    phaseStartTick + Mathf.Max(1, strafeExitTurnTicks), now);
                return Mathf.SmoothStep(strafeExitStartScale, 1.15f, progress);
            }
            return 1.15f;
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref plan, "attackPlan");
            Scribe_References.Look(ref caller, "caller");
            Scribe_References.Look(ref forwardBase, "forwardBase");
            Scribe_References.Look(ref flareTarget, "flareTarget");
            Scribe_Values.Look(ref queuedTick, "queuedTick", 0);
            Scribe_Values.Look(ref nextGuidanceAttemptTick, "nextGuidanceAttemptTick", 0);
            Scribe_Values.Look(ref guidanceAttempts, "guidanceAttempts", 0);
            Scribe_Values.Look(ref goAroundCount, "goAroundCount", 0);
            Scribe_Values.Look(ref lastGuidanceChance, "lastGuidanceChance", 0f);
            Scribe_Values.Look(ref lastGuidanceRoll, "lastGuidanceRoll", 0f);
            Scribe_Values.Look(ref runState, "runState", HelodCasRunState.Approaching);
            Scribe_Values.Look(ref aircraftPhase, "aircraftPhase", HelodCasAircraftPhase.NotStarted);
            Scribe_Values.Look(ref currentAircraftIndex, "currentAircraftIndex", 0);
            Scribe_Values.Look(ref aircraftCount, "aircraftCount",
                HelodCasSupportUtility.AircraftCount);
            Scribe_Values.Look(ref aircraftEntryTick, "aircraftEntryTick", 0);
            Scribe_Values.Look(ref phaseStartTick, "phaseStartTick", 0);
            Scribe_Values.Look(ref phaseEndTick, "phaseEndTick", 0);
            Scribe_Values.Look(ref bombReleaseTick, "bombReleaseTick", 0);
            Scribe_Values.Look(ref cancellationLockTick, "cancellationLockTick", 0);
            Scribe_Values.Look(ref nextAircraftEntryTick, "nextAircraftEntryTick", 0);
            Scribe_Values.Look(ref bombPairReleased, "bombPairReleased", false);
            Scribe_Values.Look(ref goAroundStartTick, "goAroundStartTick", 0);
            Scribe_Values.Look(ref goAroundExitTick, "goAroundExitTick", 0);
            Scribe_Values.Look(ref goAroundTurnSign, "goAroundTurnSign", 1);
            Scribe_Values.Look(ref aborting, "aborting", false);
            Scribe_Values.Look(ref abortStartPosition, "abortStartPosition");
            Scribe_Values.Look(ref abortStartDirection, "abortStartDirection");
            Scribe_Values.Look(ref abortTurnSign, "abortTurnSign", 1);
            Scribe_Values.Look(ref abortSignedAngle, "abortSignedAngle", 0f);
            Scribe_Values.Look(ref abortTurnTicks, "abortTurnTicks", 0);
            Scribe_Values.Look(ref abortExitDirection, "abortExitDirection");
            Scribe_Values.Look(ref recoveryStartPosition, "recoveryStartPosition");
            Scribe_Values.Look(ref recoveryInitialSpeed, "recoveryInitialSpeed", 0f);
            Scribe_Values.Look(ref recoveryStartScale, "recoveryStartScale", 0.92f);
            Scribe_Values.Look(ref nextStrafeBurstTick, "nextStrafeBurstTick", 0f);
            Scribe_Values.Look(ref a10CStrafeRoundAccumulator,
                "a10CStrafeRoundAccumulator", 0f);
            Scribe_Values.Look(ref pendingStrafeRounds, "pendingStrafeRounds", 0);
            Scribe_Values.Look(ref a10CStrafeRoundsQueued,
                "a10CStrafeRoundsQueued", 0);
            Scribe_Values.Look(ref a10CStrafeSequenceIndex,
                "a10CStrafeSequenceIndex", 0);
            Scribe_Values.Look(ref strafeExitStartPosition, "strafeExitStartPosition");
            Scribe_Values.Look(ref strafeExitStartDirection, "strafeExitStartDirection");
            Scribe_Values.Look(ref strafeExitDirection, "strafeExitDirection");
            Scribe_Values.Look(ref strafeExitSignedAngle, "strafeExitSignedAngle", 0f);
            Scribe_Values.Look(ref strafeExitTurnTicks, "strafeExitTurnTicks", 0);
            Scribe_Values.Look(ref strafeExitStartScale, "strafeExitStartScale",
                HelodCasSupportUtility.StrafeMinimumScale);
            Scribe_Values.Look(ref fixedGuidanceDevicePosition,
                "fixedGuidanceDevicePosition");
            Scribe_Values.Look(ref fixedGuidanceDevicePositionInitialized,
                "fixedGuidanceDevicePositionInitialized", false);
        }
    }
}
