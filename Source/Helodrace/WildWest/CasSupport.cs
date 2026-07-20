using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace
{
    public enum HelodCasAttackKind
    {
        Bombing,
        Strafing
    }

    public enum HelodCasGuidanceMode
    {
        TalkOn,
        Flare,
        Laser
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
        Complete
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
        public const int PlaytimeDecayTicks = 2 * 2500;
        public const int GuidanceFailuresPerPlaytime = 3;
        public const int ArrivalDelayTicks = 5 * 60;
        public const int GoAroundMinimumTicks = 25 * 60;
        public const int GoAroundMaximumTicks = 35 * 60;
        public const int GoAroundTurnTicks = 45;
        public const int GoAroundExitTicks = 180;
        public const float GoAroundTurnRadius = 4f;
        public const float AbortTurnRadius = 14.32f;
        public const float AbortTurnAngleDegrees = 225f;
        public const float GoAroundExitSpeed = 0.60f;
        public const float AircraftAttackSpeed = 1.00f;
        public const float AircraftDiveMinimumSpeed = AircraftAttackSpeed * 0.5f;
        public const float AircraftRecoverySpeed = AircraftAttackSpeed * 0.8660254f;
        public const int AircraftRecoveryTicks = 3 * 60;
        public const float DiveDistance = 60f;
        public const float BombReleaseDistance = 10f;
        public const float StrafeLength = 30f;
        public const float StrafeWidth = 2.5f;
        public const float GroundReferenceRadius = 10f;
        public const float StrafeApproachDistance = 15f;
        public const float StrafeSpeedFactor = 0.9063078f;
        public const float StrafeMinimumScale = 0.90f;
        public const float StrafeTurnRadius = 14.32f;
        public const float StrafeExitTurnAngleDegrees = 210f;
        public const int StrafeRoundsPerBurst = 8;
        public const float StrafeRoundsPerMinute = 750f;
        public const float StrafeTicksPerBurst = 60f * 60f / StrafeRoundsPerMinute;
        public const float StrafeBulletLeadDistance = 15f;
        public const string StrafeProjectileDefName = "HD_Bullet_M2HB_CAS_Proj";
        public const string StrafeSoundDefName = "HD_M2Fire";
        public const int FollowupEntryIntervalTicks = 12 * 60;
        public const int CancellationLockBeforeReleaseTicks = 60;
        public const int BombFallTicks = 75;
        public const float BombExplosionRadius = 6.2f;
        public const int BombDamage = 180;
        public const float BombArmorPenetration = 0.45f;
        public const float TalkOnGoAroundBonus = 0.12f;
        public const float FlareGoAroundBonus = 0.08f;
        public const float TalkOnFlareAssistRadius = 10f;
        public const float SimilarPawnRadius = 8f;
        public const string AircraftTexturePath = "Effects/CAS/HD_P47_CAS";
        public const string BombTexturePath = "Effects/CAS/Bombs/HD_ANM64_proj";

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
            Pawn caller, HelodCasAttackKind attackKind = HelodCasAttackKind.Bombing)
        {
            BeginRouteTargeting(map, forwardBase, caller, HelodCasGuidanceMode.TalkOn,
                attackKind);
        }

        public static void BeginFlareTargeting(Map map, HelodForwardBase forwardBase,
            Pawn caller, HelodCasAttackKind attackKind = HelodCasAttackKind.Bombing)
        {
            if (!CasFlareTargetUtility.ActiveFlares(map).Any())
            {
                Messages.Message("HD_CAS_NoActiveFlare".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            BeginRouteTargeting(map, forwardBase, caller, HelodCasGuidanceMode.Flare,
                attackKind);
        }

        private static void BeginRouteTargeting(Map map, HelodForwardBase forwardBase,
            Pawn caller, HelodCasGuidanceMode guidanceMode, HelodCasAttackKind attackKind)
        {
            if (!CanUseBase(map, forwardBase) || caller == null || caller.Map != map
                || SCR300RadioUtility.IsBlackout(map))
            {
                Messages.Message("HD_CAS_Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (guidanceMode == HelodCasGuidanceMode.Laser)
            {
                Messages.Message("HD_CAS_LaserUnavailable".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WorldTargeter.StopTargeting();
            Find.Targeter.StopTargeting();
            Find.World.renderer.wantedMode = WorldRenderMode.None;
            Current.Game.CurrentMap = map;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            CameraJumper.TryJump(new TargetInfo(caller.Position, map));
            map.GetComponent<MapComponent_HelodCasSupport>()
                .BeginRouteTargeting(forwardBase, caller, guidanceMode, attackKind);
            string prompt = attackKind == HelodCasAttackKind.Strafing
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
            if (plan.GuidanceMode == HelodCasGuidanceMode.Flare)
            {
                float flareChance = 0.88f + social * 0.005f
                    + goAroundCount * FlareGoAroundBonus;
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
                + flareBonus - similarPawnPenalty + retryBonus, 0.05f, 0.98f);
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
                || !IsEntryCell(map, plan.EntryCell) || !CanUseBase(map, forwardBase))
            {
                Messages.Message("HD_CAS_Unavailable".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            if (plan.AttackKind == HelodCasAttackKind.Strafing
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
                Messages.Message("HD_CAS_LaserUnavailable".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            MapComponent_HelodCasSupport support = map
                .GetComponent<MapComponent_HelodCasSupport>();
            if (!support.HasPlaytime(forwardBase))
            {
                Messages.Message("HD_CAS_PlaytimeExhausted".Translate(),
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

            if (!support.TryConsumePlaytime(forwardBase, Find.TickManager.TicksGame,
                plan.AttackKind, out int aircraftCount, out _))
            {
                Messages.Message("HD_CAS_PlaytimeExhausted".Translate(),
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
        private IntVec3 dragStart = IntVec3.Invalid;
        private IntVec3 dragEnd = IntVec3.Invalid;
        private bool routeTargeting;
        private static Material aircraftMaterial;
        private static Material bombMaterial;

        public MapComponent_HelodCasSupport(Map map) : base(map)
        {
        }

        public void BeginRouteTargeting(HelodForwardBase forwardBase, Pawn caller,
            HelodCasGuidanceMode guidanceMode, HelodCasAttackKind attackKind)
        {
            routeBase = forwardBase;
            routeCaller = caller;
            routeGuidanceMode = guidanceMode;
            routeAttackKind = attackKind;
            dragStart = IntVec3.Invalid;
            dragEnd = IntVec3.Invalid;
            routeTargeting = true;
        }

        public void QueueStrike(HelodCasAttackPlan plan, Pawn caller,
            HelodForwardBase forwardBase, Thing_M8FlareTarget flareTarget,
            int aircraftCount)
        {
            strikes.Add(new HelodCasStrike(plan, caller, forwardBase, flareTarget,
                Find.TickManager.TicksGame,
                Find.TickManager.TicksGame + HelodCasSupportUtility.ArrivalDelayTicks,
                aircraftCount));
        }

        public bool HasPlaytime(HelodForwardBase forwardBase)
        {
            return GetPlaytimeState(forwardBase, true).IsActive;
        }

        public bool CanRequestFlight(HelodForwardBase forwardBase)
        {
            HelodCasPlaytimeState state = GetPlaytimeState(forwardBase, true);
            return !state.IsActive && !strikes.Any(strike => strike.ForwardBase == forwardBase);
        }

        public bool TryRequestFlight(HelodForwardBase forwardBase, int now)
        {
            return CanRequestFlight(forwardBase)
                && GetPlaytimeState(forwardBase, true).RequestFlight(now);
        }

        public void GetPlaytimeStatus(HelodForwardBase forwardBase,
            out bool flightRequested, out int remainingPlaytime,
            out int reservedAircraftCount)
        {
            HelodCasPlaytimeState state = GetPlaytimeState(forwardBase, true);
            flightRequested = state.FlightRequested;
            remainingPlaytime = state.RemainingPlaytime;
            reservedAircraftCount = state.ReservedAircraftCount;
        }

        public bool TryConsumePlaytime(HelodForwardBase forwardBase, int now,
            HelodCasAttackKind attackKind, out int aircraftCount, out int remaining)
        {
            HelodCasPlaytimeState state = GetPlaytimeState(forwardBase, true);
            bool consumed = state.TryConsumeAction(now, attackKind, out aircraftCount);
            remaining = state.RemainingPlaytime;
            return consumed;
        }

        private HelodCasPlaytimeState GetPlaytimeState(HelodForwardBase forwardBase,
            bool create)
        {
            HelodCasPlaytimeState state = playtimeStates.FirstOrDefault(
                item => item.ForwardBase == forwardBase);
            if (state == null && create)
            {
                state = new HelodCasPlaytimeState(forwardBase);
                playtimeStates.Add(state);
            }
            return state;
        }

        private void ReserveBombingAircraft(HelodForwardBase forwardBase, int count)
        {
            if (forwardBase != null && count > 0)
            {
                GetPlaytimeState(forwardBase, true).ReserveAircraft(count);
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
            HelodCasPlaytimeState state = GetPlaytimeState(strike.ForwardBase, true);
            if (state.ConsumePenalty())
            {
                Messages.Message("HD_CAS_PlaytimeGuidancePenalty".Translate(
                    strike.ForwardBase.LabelCap),
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
                    Messages.Message("HD_CAS_PlaytimeTimePenalty".Translate(
                        state.ForwardBase.LabelCap, consumed),
                        MessageTypeDefOf.CautionInput);
                }
            }
        }

        public bool HasActiveStrike(Pawn caller)
        {
            return FindStrike(caller) != null;
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

            if (routeAttackKind == HelodCasAttackKind.Strafing
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

            HelodCasAttackPlan plan = new HelodCasAttackPlan(entry, target,
                routeGuidanceMode, map, HelodCasSupportUtility.MajorScatterRadius,
                HelodCasSupportUtility.MinorScatterRadius, routeAttackKind);
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

            IntVec3 end = dragEnd.IsValid ? dragEnd : mouseCell;
            if (!end.InBounds(map))
            {
                return;
            }
            GenDraw.DrawLineBetween(dragStart.ToVector3Shifted(), end.ToVector3Shifted(),
                SimpleColor.White);
            if (routeAttackKind == HelodCasAttackKind.Strafing)
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
                    DropBombPair(strike);
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

        private void DropBombPair(HelodCasStrike strike)
        {
            int now = Find.TickManager.TicksGame;
            Vector3 releasePosition = strike.AircraftDrawPosition(now);
            for (int bomb = 0; bomb < HelodCasSupportUtility.BombsPerAircraft; bomb++)
            {
                IntVec3 impact = strike.NextImpactCell(map);
                if (!impact.InBounds(map))
                {
                    continue;
                }
                fallingBombs.Add(new HelodCasFallingBomb(releasePosition, impact,
                    strike.Caller, now, now + HelodCasSupportUtility.BombFallTicks,
                    strike.Plan.ApproachDirection));
            }
        }

        private void FireStrafeBurst(HelodCasStrike strike, int now)
        {
            ThingDef projectileDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                HelodCasSupportUtility.StrafeProjectileDefName);
            Vector3 origin = strike.AircraftDrawPosition(now);
            IntVec3 originCell = origin.ToIntVec3();
            if (projectileDef == null || !originCell.InBounds(map))
            {
                return;
            }
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail(
                HelodCasSupportUtility.StrafeSoundDefName);
            sound?.PlayOneShot(new TargetInfo(originCell, map));

            Vector2 direction = strike.Plan.ApproachDirection;
            Vector2 lateral = new Vector2(-direction.y, direction.x);
            float aimDistance = strike.StrafeAimDistance(now);
            for (int round = 0; round < HelodCasSupportUtility.StrafeRoundsPerBurst;
                round++)
            {
                float lateralOffset = Rand.Range(
                    -HelodCasSupportUtility.StrafeWidth * 0.5f,
                    HelodCasSupportUtility.StrafeWidth * 0.5f);
                float longitudinalOffset = Rand.Range(-1.25f, 1.25f);
                float roundAimDistance = Mathf.Clamp(aimDistance + longitudinalOffset,
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
            }
        }

        private void ImpactBomb(HelodCasFallingBomb bomb)
        {
            if (bomb == null || !bomb.ImpactCell.InBounds(map))
            {
                return;
            }
            FleckMaker.ThrowSmoke(bomb.ImpactCell.ToVector3Shifted(), map, 1.8f);
            GenExplosion.DoExplosion(bomb.ImpactCell, map,
                HelodCasSupportUtility.BombExplosionRadius, DamageDefOf.Bomb,
                bomb.Caller, HelodCasSupportUtility.BombDamage,
                HelodCasSupportUtility.BombArmorPenetration);
        }

        private static Material AircraftMaterial
        {
            get
            {
                if (aircraftMaterial == null
                    && ContentFinder<Texture2D>.Get(HelodCasSupportUtility.AircraftTexturePath, false) != null)
                {
                    aircraftMaterial = MaterialPool.MatFrom(
                        HelodCasSupportUtility.AircraftTexturePath, ShaderDatabase.Cutout);
                }
                return aircraftMaterial;
            }
        }

        private static Material BombMaterial
        {
            get
            {
                if (bombMaterial == null
                    && ContentFinder<Texture2D>.Get(HelodCasSupportUtility.BombTexturePath, false) != null)
                {
                    bombMaterial = MaterialPool.MatFrom(
                        HelodCasSupportUtility.BombTexturePath, ShaderDatabase.Cutout);
                }
                return bombMaterial;
            }
        }

        private static void DrawAircraft(HelodCasStrike strike, int now)
        {
            Material material = AircraftMaterial;
            if (material == null || strike?.Plan == null || !strike.ShouldDrawAircraft(now))
            {
                return;
            }
            Vector3 position = strike.AircraftDrawPosition(now);
            position.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            Vector2 direction = strike.AircraftDrawDirection(now);
            float rotation = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
            float size = 6.5f * strike.AircraftDrawScale(now);
            Matrix4x4 matrix = Matrix4x4.TRS(position,
                Quaternion.AngleAxis(rotation, Vector3.up), new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }

        private static void DrawFallingBomb(HelodCasFallingBomb bomb, int now)
        {
            Material material = BombMaterial;
            if (material == null || bomb == null)
            {
                return;
            }
            float progress = bomb.Progress(now);
            float smoothProgress = progress * progress * (3f - 2f * progress);
            Vector3 position = Vector3.Lerp(bomb.ReleasePosition,
                bomb.ImpactCell.ToVector3Shifted(), smoothProgress);
            position.y = Mathf.Lerp(AltitudeLayer.MoteOverhead.AltitudeFor(),
                AltitudeLayer.Projectile.AltitudeFor(), smoothProgress);
            float size = Mathf.Lerp(1.05f, 0.5f, smoothProgress);
            float rotation = Mathf.Atan2(bomb.ApproachDirection.x,
                bomb.ApproachDirection.y) * Mathf.Rad2Deg;
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

        public Vector3 ReleasePosition => new Vector3(releaseX, 0f, releaseZ);
        public IntVec3 ImpactCell => impactCell;
        public Pawn Caller => caller;
        public int ImpactTick => impactTick;
        public Vector2 ApproachDirection => new Vector2(approachX, approachZ).normalized;

        public HelodCasFallingBomb()
        {
        }

        public HelodCasFallingBomb(Vector3 releasePosition, IntVec3 impactCell,
            Pawn caller, int releaseTick, int impactTick, Vector2 approachDirection)
        {
            releaseX = releasePosition.x;
            releaseZ = releasePosition.z;
            this.impactCell = impactCell;
            this.caller = caller;
            this.releaseTick = releaseTick;
            this.impactTick = impactTick;
            approachX = approachDirection.x;
            approachZ = approachDirection.y;
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
        }
    }

    public sealed class HelodCasPlaytimeState : IExposable
    {
        private HelodForwardBase forwardBase;
        private int remainingPlaytime;
        private int nextDecayTick;
        private int reservedAircraftCount;
        private bool flightRequested;

        public HelodForwardBase ForwardBase => forwardBase;
        public int RemainingPlaytime => remainingPlaytime;
        public int ReservedAircraftCount => reservedAircraftCount;
        public bool FlightRequested => flightRequested;
        public bool IsActive => flightRequested && remainingPlaytime > 0;

        public HelodCasPlaytimeState()
        {
        }

        public HelodCasPlaytimeState(HelodForwardBase forwardBase)
        {
            this.forwardBase = forwardBase;
        }

        public bool RequestFlight(int now)
        {
            if (IsActive)
            {
                return false;
            }
            flightRequested = true;
            remainingPlaytime = HelodCasSupportUtility.P47Playtime;
            nextDecayTick = now + HelodCasSupportUtility.PlaytimeDecayTicks;
            reservedAircraftCount = 0;
            return true;
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
            aircraftCount = attackKind == HelodCasAttackKind.Bombing
                && reservedAircraftCount > 0
                ? reservedAircraftCount : HelodCasSupportUtility.AircraftCount;
            if (attackKind == HelodCasAttackKind.Bombing)
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
        public float FlightRouteLength => attackKind == HelodCasAttackKind.Strafing
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
            HelodCasAttackKind attackKind = HelodCasAttackKind.Bombing)
        {
            this.entryCell = entryCell;
            this.targetCell = targetCell;
            this.guidanceMode = guidanceMode;
            this.majorScatterRadius = majorScatterRadius;
            this.minorScatterRadius = minorScatterRadius;
            this.attackKind = attackKind;
            Vector2 direction = new Vector2(targetCell.x - entryCell.x,
                targetCell.z - entryCell.z).normalized;
            approachX = direction.x;
            approachZ = direction.y;
            routeLength = entryCell.DistanceTo(targetCell);
            mountainRoofCount = LineCells(entryCell, targetCell)
                .Count(cell => IsMountainRoof(map, cell));
            targetPawn = map?.thingGrid?.ThingsListAtFast(targetCell)
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
        private float recoveryStartScale = 0.72f;
        private float nextStrafeBurstTick;
        private Vector3 strafeExitStartPosition;
        private Vector2 strafeExitStartDirection;
        private Vector2 strafeExitDirection;
        private float strafeExitSignedAngle;
        private int strafeExitTurnTicks;
        private float strafeExitStartScale = HelodCasSupportUtility.StrafeMinimumScale;

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
            if (plan.GuidanceMode == HelodCasGuidanceMode.TalkOn)
            {
                return true;
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.Flare)
            {
                return flareTarget != null && flareTarget.IsActiveFlare && flareTarget.Map == map;
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
            nextGuidanceAttemptTick = Find.TickManager.TicksGame + Rand.RangeInclusive(
                HelodCasSupportUtility.GoAroundMinimumTicks,
                HelodCasSupportUtility.GoAroundMaximumTicks);
            return false;
        }

        public HelodCasAircraftTickEvent TickAircraft(int now, Map map)
        {
            if (runState != HelodCasRunState.Attacking)
            {
                return HelodCasAircraftTickEvent.None;
            }

            if (aircraftPhase == HelodCasAircraftPhase.Entry && now >= phaseEndTick)
            {
                if (plan.AttackKind == HelodCasAttackKind.Strafing)
                {
                    BeginStrafeApproach(now);
                }
                else
                {
                    BeginDive(now);
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
                        HelodCasSupportUtility.AircraftDiveMinimumSpeed, 0.72f);
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
                && now >= nextStrafeBurstTick && CanFireStrafeAt(now))
            {
                nextStrafeBurstTick += HelodCasSupportUtility.StrafeTicksPerBurst;
                return HelodCasAircraftTickEvent.StrafeBurst;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Strafing)
            {
                if (now >= nextStrafeBurstTick && now < phaseEndTick
                    && CanFireStrafeAt(now))
                {
                    nextStrafeBurstTick += HelodCasSupportUtility.StrafeTicksPerBurst;
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
                ? HelodCasSupportUtility.StrafeApproachDistance
                : HelodCasSupportUtility.DiveDistance;
            float entryDistance = Mathf.Max(0f,
                plan.FlightRouteLength - terminalApproachDistance);
            phaseEndTick = now + Mathf.Max(1,
                Mathf.CeilToInt(entryDistance / HelodCasSupportUtility.AircraftAttackSpeed));
            nextAircraftEntryTick = now + HelodCasSupportUtility.FollowupEntryIntervalTicks;
            bombPairReleased = false;
            bombReleaseTick = 0;
            cancellationLockTick = 0;
            nextStrafeBurstTick = 0f;
        }

        private void BeginDive(int now)
        {
            aircraftPhase = HelodCasAircraftPhase.Dive;
            phaseStartTick = now;
            float diveDistance = Mathf.Min(plan.RouteLength,
                HelodCasSupportUtility.DiveDistance);
            float averageDiveSpeed = (HelodCasSupportUtility.AircraftAttackSpeed
                + HelodCasSupportUtility.AircraftDiveMinimumSpeed) * 0.5f;
            int diveTicks = Mathf.Max(1,
                Mathf.RoundToInt(diveDistance / averageDiveSpeed));
            phaseEndTick = now + diveTicks;
            float distanceBeforeRelease = Mathf.Max(0f, diveDistance
                - Mathf.Min(HelodCasSupportUtility.BombReleaseDistance, diveDistance));
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
                HelodCasSupportUtility.StrafeApproachDistance);
            float strafeSpeed = HelodCasSupportUtility.AircraftAttackSpeed
                * HelodCasSupportUtility.StrafeSpeedFactor;
            float averageSpeed = (HelodCasSupportUtility.AircraftAttackSpeed
                + strafeSpeed) * 0.5f;
            phaseEndTick = now + Mathf.Max(1,
                Mathf.RoundToInt(distance / averageSpeed));
            nextStrafeBurstTick = now;
        }

        private void BeginStrafing(int now)
        {
            aircraftPhase = HelodCasAircraftPhase.Strafing;
            phaseStartTick = now;
            float strafeSpeed = HelodCasSupportUtility.AircraftAttackSpeed
                * HelodCasSupportUtility.StrafeSpeedFactor;
            phaseEndTick = now + Mathf.Max(1,
                Mathf.RoundToInt(HelodCasSupportUtility.StrafeLength / strafeSpeed));
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
            if (aircraftPhase == HelodCasAircraftPhase.Dive)
            {
                Vector3 startPosition = AircraftDrawPosition(now);
                float startScale = AircraftDrawScale(now);
                float diveDistance = Mathf.Min(plan.RouteLength,
                    HelodCasSupportUtility.DiveDistance);
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
            if (aircraftPhase == HelodCasAircraftPhase.Dive
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

        public IntVec3 NextImpactCell(Map map)
        {
            Vector2 direction = plan.ApproachDirection;
            Vector2 lateral = new Vector2(-direction.y, direction.x);
            float angle = Rand.Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Rand.Value);
            float along = Mathf.Cos(angle) * plan.MajorScatterRadius * radius;
            float across = Mathf.Sin(angle) * plan.MinorScatterRadius * radius;
            IntVec3 center = plan.GuidanceMode == HelodCasGuidanceMode.Flare
                && flareTarget?.IsActiveFlare == true
                ? flareTarget.Position : plan.CurrentAimCell(map);
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
            return Mathf.Clamp(aircraftDistance
                + HelodCasSupportUtility.StrafeBulletLeadDistance,
                0f, HelodCasSupportUtility.StrafeLength);
        }

        private bool CanFireStrafeAt(int now)
        {
            if (aircraftPhase != HelodCasAircraftPhase.StrafeApproach
                && aircraftPhase != HelodCasAircraftPhase.Strafing)
            {
                return false;
            }
            Vector3 aircraft = AircraftDrawPosition(now);
            Vector3 start = plan.StrafeStartPosition;
            Vector2 direction = plan.ApproachDirection;
            float aircraftDistance = (aircraft.x - start.x) * direction.x
                + (aircraft.z - start.z) * direction.y;
            return aircraftDistance >= -HelodCasSupportUtility.StrafeBulletLeadDistance
                && aircraftDistance <= HelodCasSupportUtility.StrafeLength
                    - HelodCasSupportUtility.StrafeBulletLeadDistance;
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
                    ? HelodCasSupportUtility.StrafeApproachDistance
                    : HelodCasSupportUtility.DiveDistance;
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
                    HelodCasSupportUtility.DiveDistance);
                Vector3 diveStart = target
                    - new Vector3(direction.x, 0f, direction.y) * diveDistance;
                float runDistance = DiveTravelDistance(now - phaseStartTick,
                    phaseEndTick - phaseStartTick, diveDistance);
                return diveStart
                    + new Vector3(direction.x, 0f, direction.y) * runDistance;
            }
            if (aircraftPhase == HelodCasAircraftPhase.AbortTurn)
            {
                return AbortTurnPosition(now);
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeApproach)
            {
                Vector3 strafeStart = plan.StrafeStartPosition;
                float approachDistance = Mathf.Min(plan.FlightRouteLength,
                    HelodCasSupportUtility.StrafeApproachDistance);
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
                return Mathf.SmoothStep(1.15f, 0.72f, progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.Recovery)
            {
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                float scaleProgress = 1f - Mathf.Pow(1f - progress, 3f);
                return Mathf.Lerp(recoveryStartScale, 1.20f, scaleProgress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.Strafing)
            {
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return Mathf.Lerp(1.15f, HelodCasSupportUtility.StrafeMinimumScale,
                    progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeExit)
            {
                float progress = Mathf.InverseLerp(phaseStartTick,
                    phaseStartTick + Mathf.Max(1, strafeExitTurnTicks), now);
                float scaleProgress = 1f - Mathf.Pow(1f - progress, 3f);
                return Mathf.Lerp(strafeExitStartScale, 1.15f,
                    scaleProgress);
            }
            return aircraftPhase == HelodCasAircraftPhase.Complete ? 1.20f : 1.15f;
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
            Scribe_Values.Look(ref recoveryStartScale, "recoveryStartScale", 0.72f);
            Scribe_Values.Look(ref nextStrafeBurstTick, "nextStrafeBurstTick", 0f);
            Scribe_Values.Look(ref strafeExitStartPosition, "strafeExitStartPosition");
            Scribe_Values.Look(ref strafeExitStartDirection, "strafeExitStartDirection");
            Scribe_Values.Look(ref strafeExitDirection, "strafeExitDirection");
            Scribe_Values.Look(ref strafeExitSignedAngle, "strafeExitSignedAngle", 0f);
            Scribe_Values.Look(ref strafeExitTurnTicks, "strafeExitTurnTicks", 0);
            Scribe_Values.Look(ref strafeExitStartScale, "strafeExitStartScale",
                HelodCasSupportUtility.StrafeMinimumScale);
        }
    }
}
