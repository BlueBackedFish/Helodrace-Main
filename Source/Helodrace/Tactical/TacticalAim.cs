using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Tactical
{
    public sealed class Hediff_TacticalAim : Hediff
    {
        public const float FocusHalfAngle = 45f;
        public const float FireHalfAngle = 60f;
        public const float FocusWarmupMultiplier = 0.5f;
        public const float PeripheralWarmupMultiplier = 1.5f;
        public const float MoveSpeedMultiplier = 0.65f;

        private float focusAngle;
        private IntVec3 focusCell = IntVec3.Invalid;

        public float FocusAngle
        {
            get
            {
                if (focusCell.IsValid && pawn?.Spawned == true)
                {
                    focusAngle = AngleToFocus(pawn.DrawPos, focusCell.ToVector3Shifted(), focusAngle);
                }
                return focusAngle;
            }
        }
        private static float AngleToFocus(Vector3 origin, Vector3 point, float fallback)
        {
            Vector3 direction = point - origin;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f
                ? TacticalAimUtility.NormalizeAngle(
                    (float)(Math.Atan2(direction.x, direction.z) * 180.0 / Math.PI)) : fallback;
        }
        public bool IsPeripheralAngle(float targetAngle)
        {
            return Mathf.Abs(Mathf.DeltaAngle(FocusAngle, targetAngle)) > FocusHalfAngle;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref focusAngle, "focusAngle", 0f);
            Scribe_Values.Look(ref focusCell, "focusCell", IntVec3.Invalid);
        }

        public void SetFocusPoint(IntVec3 cell, float angle)
        {
            focusCell = cell;
            focusAngle = TacticalAimUtility.NormalizeAngle(angle);
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned
                || !TacticalAimUtility.HasCQBTraining(pawn))
            {
                TacticalAimUtility.Cancel(pawn);
            }
        }

        public override string TipStringExtra
        {
            get
            {
                return "HD_TacticalAim_FocusDirection".Translate(
                    FocusAngle.ToString("0"));
            }
        }
    }

    public static class TacticalAimUtility
    {
        private const string CqbTrainingDefName = "HD_CQBTraining";
        private const string AimDefName = "HD_TacticalAim";
        private const int MessageCooldownTicks = 60;
        private const int AimPreparationTicks = 90;
        private const int AimCooldownTicks = 10 * 60;
        private static readonly Dictionary<int, int> nextMessageTick =
            new Dictionary<int, int>();
        private static readonly Dictionary<int, AimVisualState> visualStates =
            new Dictionary<int, AimVisualState>();
        private static readonly Dictionary<int, PendingAimState> pendingAims =
            new Dictionary<int, PendingAimState>();
        private static readonly Dictionary<int, int> aimCooldownEndTicks =
            new Dictionary<int, int>();

        private sealed class PendingAimState
        {
            public float angle;
            public IntVec3 cell;
            public int readyTick;
        }

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        private sealed class AimVisualState
        {
            public float angle;
            public float returnFrom;
            public int returnTick;
            public int turnTicks = RecoveryTicks;
            public int updatedTick = -1;
            public bool tracking;
        }

        private const int RecoveryTicks = 36;

        private static int FocusTurnTicks(float from, float to)
        {
            float delta = Math.Abs(NormalizeAngle(to - from + 180f) - 180f);
            // 0.1 seconds for a small adjustment, 0.6 seconds for a half-turn.
            return 6 + (int)Math.Ceiling(delta / 180f * 30f);
        }

        public static Hediff_TacticalAim Get(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.GetFirstHediffOfDef(
                DefDatabase<HediffDef>.GetNamedSilentFail(AimDefName)) as Hediff_TacticalAim;
        }

        public static bool IsAiming(Pawn pawn)
        {
            return HasCQBTraining(pawn) && Get(pawn) != null;
        }

        public static bool HasCQBTraining(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(CqbTrainingDefName);
            return pawn?.health?.hediffSet != null
                && def != null
                && pawn.health.hediffSet.HasHediff(def);
        }

        public static bool IsRangedVerb(Verb verb)
        {
            if (verb == null)
            {
                return false;
            }

            // Keep this compatible with both vanilla projectile verbs and CE's
            // separately defined shooting verb classes.
            return verb is Verb_Shoot
                || verb is Verb_LaunchProjectile
                || verb.GetType().Name.IndexOf("Shoot", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TryGetTargetAngle(Pawn pawn, LocalTargetInfo target, out float angle)
        {
            angle = 0f;
            if (pawn == null || !target.IsValid || target.Cell == pawn.Position)
            {
                return false;
            }

            Vector3 direction = (target.HasThing ? target.Thing.DrawPos
                : target.Cell.ToVector3Shifted()) - pawn.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return false;
            }

            angle = NormalizeAngle(direction.AngleFlat());
            return true;
        }

        public static bool TryGetWarmupMultiplier(Verb verb, out float multiplier)
        {
            multiplier = 1f;
            Pawn pawn = verb?.Caster as Pawn;
            Hediff_TacticalAim aim = Get(pawn);
            if (aim == null || !IsAiming(pawn) || !IsRangedVerb(verb)
                || !verb.CurrentTarget.IsValid)
            {
                return false;
            }

            if (!TryGetTargetAngle(pawn, verb.CurrentTarget, out float targetAngle))
            {
                return false;
            }

            float delta = Mathf.Abs(Mathf.DeltaAngle(aim.FocusAngle, targetAngle));
            bool moving = IsMoving(pawn);
            if (moving
                && !TacticalSuddenFireUtility.IsActive(verb)
                && delta > Hediff_TacticalAim.FireHalfAngle)
            {
                return false;
            }

            multiplier = delta <= Hediff_TacticalAim.FocusHalfAngle
                ? Hediff_TacticalAim.FocusWarmupMultiplier
                : Hediff_TacticalAim.PeripheralWarmupMultiplier;
            return true;
        }

        public static bool CanFireAt(Pawn pawn, LocalTargetInfo target)
        {
            Hediff_TacticalAim aim = Get(pawn);
            if (!IsAiming(pawn) || aim == null
                || !TryGetTargetAngle(pawn, target, out float targetAngle))
            {
                return true;
            }

            float delta = Mathf.Abs(Mathf.DeltaAngle(aim.FocusAngle, targetAngle));
            // While stationary, aiming never removes the ability to fire. The
            // focus arc only determines whether the warmup bonus or penalty is
            // applied. The 120-degree hard firing arc exists only while moving.
            if (!IsMoving(pawn) || delta <= Hediff_TacticalAim.FireHalfAngle)
            {
                return true;
            }

            NotifyOutsideArc(pawn);
            return false;
        }

        public static void BeginOrRetarget(Pawn pawn)
        {
            if (!HasCQBTraining(pawn) || pawn?.Map == null || pawn.Downed || pawn.Dead)
            {
                return;
            }

            if (TacticalHighSpeedMovementUtility.IsActive(pawn))
            {
                Messages.Message(
                    "HD_TacticalHighSpeed_AimDisabled".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (Get(pawn) == null && IsOnCooldown(pawn))
            {
                NotifyAimCooldown(pawn);
                return;
            }

            Map map = pawn.Map;
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetItems = false,
                validator = target => target.IsValid
                    && target.Cell.InBounds(map)
                    && target.Cell != pawn.Position
            }, target => QueueFocus(pawn, target.Cell));
        }

        private static void QueueFocus(Pawn pawn, IntVec3 targetCell)
        {
            if (!TryGetFocusAngle(pawn, targetCell, out float angle))
            {
                return;
            }

            if (Get(pawn) != null)
            {
                SetFocus(pawn, targetCell);
                return;
            }

            if (IsOnCooldown(pawn))
            {
                NotifyAimCooldown(pawn);
                return;
            }

            pendingAims[pawn.thingIDNumber] = new PendingAimState
            {
                angle = angle,
                cell = targetCell,
                readyTick = CurrentTick + AimPreparationTicks
            };
            Messages.Message(
                "HD_TacticalAim_Preparing".Translate(
                    AimPreparationTicks.ToStringTicksToPeriod()),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public static void SetFocus(Pawn pawn, IntVec3 targetCell)
        {
            if (!TryGetFocusAngle(pawn, targetCell, out float angle))
            {
                return;
            }

            pendingAims.Remove(pawn.thingIDNumber);
            ApplyFocus(pawn, angle, targetCell);
        }

        private static bool TryGetFocusAngle(Pawn pawn, IntVec3 targetCell, out float angle)
        {
            angle = 0f;
            if (!HasCQBTraining(pawn) || pawn?.Map == null
                || !targetCell.IsValid || targetCell == pawn.Position)
            {
                return false;
            }

            Vector3 direction = (targetCell - pawn.Position).ToVector3();
            if (direction.sqrMagnitude < 0.001f)
            {
                return false;
            }

            angle = NormalizeAngle(direction.AngleFlat());
            return true;
        }

        private static void ApplyFocus(Pawn pawn, float angle, IntVec3 targetCell)
        {
            Hediff_TacticalAim aim = Get(pawn);
            float startAngle = aim != null ? VisualAimAngle(pawn) : pawn.Rotation.AsAngle;
            if (pawn.stances?.curStance is Stance_Busy busy
                && TryGetTargetAngle(pawn, busy.focusTarg, out float liveAngle))
                startAngle = liveAngle;
            if (aim == null)
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(AimDefName);
                if (def == null)
                {
                    Log.ErrorOnce("Helodrace: missing HD_TacticalAim HediffDef.", 71420831);
                    return;
                }

                aim = pawn.health.AddHediff(def) as Hediff_TacticalAim;
            }

            aim?.SetFocusPoint(targetCell, angle);
            visualStates[pawn.thingIDNumber] = new AimVisualState
            {
                angle = startAngle,
                returnFrom = startAngle,
                returnTick = CurrentTick,
                turnTicks = FocusTurnTicks(startAngle, aim?.FocusAngle ?? angle)
            };
            Messages.Message(
                "HD_TacticalAim_Started".Translate(),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public static void Cancel(Pawn pawn)
        {
            if (pawn != null)
            {
                pendingAims.Remove(pawn.thingIDNumber);
            }

            Hediff_TacticalAim aim = Get(pawn);
            if (aim != null && pawn?.health != null)
            {
                pawn.health.RemoveHediff(aim);
                aimCooldownEndTicks[pawn.thingIDNumber] = CurrentTick + AimCooldownTicks;
            }
            if (pawn != null)
            {
                visualStates.Remove(pawn.thingIDNumber);
            }
        }

        public static void UpdatePendingAim(Pawn pawn)
        {
            if (pawn == null
                || !pendingAims.TryGetValue(pawn.thingIDNumber, out PendingAimState pending))
            {
                return;
            }

            if (pawn.Dead || pawn.Downed || !pawn.Spawned || !HasCQBTraining(pawn))
            {
                pendingAims.Remove(pawn.thingIDNumber);
                return;
            }

            if (CurrentTick < pending.readyTick)
            {
                return;
            }

            pendingAims.Remove(pawn.thingIDNumber);
            ApplyFocus(pawn, pending.angle, pending.cell);
        }

        public static bool IsOnCooldown(Pawn pawn)
        {
            if (pawn == null || !aimCooldownEndTicks.TryGetValue(
                pawn.thingIDNumber,
                out int cooldownEndTick))
            {
                return false;
            }

            if (CurrentTick >= cooldownEndTick)
            {
                aimCooldownEndTicks.Remove(pawn.thingIDNumber);
                return false;
            }

            return true;
        }

        public static int CooldownTicksRemaining(Pawn pawn)
        {
            if (!IsOnCooldown(pawn)
                || !aimCooldownEndTicks.TryGetValue(
                    pawn.thingIDNumber,
                    out int cooldownEndTick))
            {
                return 0;
            }

            return Mathf.Max(0, cooldownEndTick - CurrentTick);
        }

        private static void NotifyAimCooldown(Pawn pawn)
        {
            Messages.Message(
                "HD_TacticalAim_Cooldown".Translate(
                    CooldownTicksRemaining(pawn).ToStringTicksToPeriod()),
                pawn,
                MessageTypeDefOf.RejectInput,
                false);
        }

        public static bool TryLiveTarget(Pawn pawn, out float angle)
        {
            angle = 0f;
            Stance_Busy stance = pawn?.stances?.curStance as Stance_Busy;
            if (stance == null || stance is Patch_DrawEquipmentAndApparelExtras_TacticalAim.TacticalRenderStance
                || stance.neverAimWeapon || !stance.focusTarg.IsValid)
                return false;
            Verb verb = stance.verb
                ?? pawn.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
            // Cooldown between shots still belongs to the firing sequence.
            // Dropping tracking here makes the weapon return between every shot.
            if (!IsRangedVerb(verb))
                return false;
            if (stance.focusTarg.HasThing
                && (!stance.focusTarg.Thing.Spawned || stance.focusTarg.Thing.Destroyed
                    || stance.focusTarg.Thing.Map != pawn.Map))
                return false;
            if (!TryGetTargetAngle(pawn, stance.focusTarg, out angle)) return false;
            if (IsMoving(pawn)
                && !TacticalSuddenFireUtility.IsActive(verb)
                && Mathf.Abs(Mathf.DeltaAngle(Get(pawn).FocusAngle, angle))
                    > Hediff_TacticalAim.FireHalfAngle)
                return false;
            return verb.CanHitTarget(stance.focusTarg);
        }

        // Updated from simulation ticks, never from temporary render stances.
        public static void UpdateVisual(Pawn pawn)
        {
            if (!IsAiming(pawn) || !pawn.Spawned || pawn.Downed || pawn.Dead) return;
            AimVisualState state = GetVisualState(pawn);
            int now = Find.TickManager.TicksGame;
            if (state.updatedTick == now) return;
            state.updatedTick = now;
            if (TryLiveTarget(pawn, out float targetAngle))
            {
                state.angle = targetAngle;
                state.tracking = true;
            }
            else
            {
                if (state.tracking)
                {
                    state.returnFrom = state.angle;
                    state.returnTick = now;
                    state.turnTicks = RecoveryTicks;
                    state.tracking = false;
                }
                float t = Mathf.Clamp01((now - state.returnTick) / (float)state.turnTicks);
                float eased = (1f - Mathf.Exp(-5f * t)) / (1f - Mathf.Exp(-5f));
                state.angle = NormalizeAngle(Mathf.LerpAngle(
                    state.returnFrom, Get(pawn).FocusAngle, eased));
            }
        }

        public static float VisualAimAngle(Pawn pawn)
        {
            if (IsAiming(pawn) && TryLiveTarget(pawn, out float angle))
                return angle;
            return IsAiming(pawn) ? GetVisualState(pawn).angle : pawn.Rotation.AsAngle;
        }

        public static IntVec3 VisualAimTargetCell(Pawn pawn)
        {
            Vector3 direction = Vector3.forward.RotatedBy(VisualAimAngle(pawn));
            return pawn.Position + new IntVec3(
                Mathf.RoundToInt(direction.x * 12f), 0,
                Mathf.RoundToInt(direction.z * 12f));
        }

        public static void NotifyShot(Verb verb)
        {
            Pawn pawn = verb?.Caster as Pawn;
            if (!IsAiming(pawn) || !TryGetTargetAngle(pawn, verb.CurrentTarget, out float angle))
                return;
            AimVisualState state = GetVisualState(pawn);
            state.angle = angle;
            state.tracking = true;
        }

        public static bool TryGetEquipmentVisualAngle(Thing equipment, ref float aimAngle)
        {
            Pawn pawn = WielderOf(equipment);
            if (!IsAiming(pawn) || pawn.Dead || pawn.Downed) return false;
            aimAngle = VisualAimAngle(pawn);
            return true;
        }

        public static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            return angle < 0f ? angle + 360f : angle;
        }

        private static bool IsMoving(Pawn pawn)
        {
            return pawn?.pather?.MovingNow == true;
        }

        private static AimVisualState GetVisualState(Pawn pawn)
        {
            if (!visualStates.TryGetValue(pawn.thingIDNumber, out AimVisualState state))
            {
                state = new AimVisualState
                {
                    angle = Get(pawn).FocusAngle,
                    returnFrom = Get(pawn).FocusAngle,
                    returnTick = Find.TickManager?.TicksGame ?? 0
                };
                visualStates[pawn.thingIDNumber] = state;
            }
            return state;
        }

        private static void ClearVisualState(Pawn pawn)
        {
            if (pawn != null)
            {
                visualStates.Remove(pawn.thingIDNumber);
            }
        }

        private static Pawn WielderOf(Thing equipment)
        {
            if (equipment?.ParentHolder is Pawn_EquipmentTracker equipmentTracker)
            {
                return equipmentTracker.pawn;
            }
            if (equipment?.ParentHolder is Pawn_InventoryTracker inventoryTracker)
            {
                return inventoryTracker.pawn;
            }
            return equipment?.ParentHolder as Pawn;
        }

        private static void NotifyOutsideArc(Pawn pawn)
        {
            if (pawn == null || pawn.thingIDNumber < 0)
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (nextMessageTick.TryGetValue(pawn.thingIDNumber, out int next)
                && tick < next)
            {
                return;
            }

            nextMessageTick[pawn.thingIDNumber] = tick + MessageCooldownTicks;
            Messages.Message(
                "HD_TacticalAim_OutsideArc".Translate(),
                pawn,
                MessageTypeDefOf.RejectInput,
                false);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalAim
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (__instance?.Faction != Faction.OfPlayer || __instance.Dead
                || !TacticalAimUtility.HasCQBTraining(__instance))
            {
                return;
            }

            __result = __result.Concat(TacticalAimGizmos(__instance));
        }

        private static IEnumerable<Gizmo> TacticalAimGizmos(Pawn pawn)
        {
            Hediff_TacticalAim aim = TacticalAimUtility.Get(pawn);
            if (aim == null)
            {
                bool onCooldown = TacticalAimUtility.IsOnCooldown(pawn);
                bool highSpeed = TacticalHighSpeedMovementUtility.IsActive(pawn);
                yield return new Command_Action
                {
                    defaultLabel = "HD_TacticalAim_Command".Translate().ToString(),
                    defaultDesc = "HD_TacticalAim_CommandDesc".Translate().ToString(),
                    icon = ContentFinder<Texture2D>.Get("Skill/HD_CQB_Aiming", false)
                        ?? BaseContent.BadTex,
                    Disabled = onCooldown || highSpeed,
                    disabledReason = highSpeed
                        ? "HD_TacticalHighSpeed_AimDisabled".Translate().ToString()
                        : onCooldown
                        ? "HD_TacticalAim_Cooldown".Translate(
                            TacticalAimUtility.CooldownTicksRemaining(pawn)
                                .ToStringTicksToPeriod()).ToString()
                        : null,
                    action = () => TacticalAimUtility.BeginOrRetarget(pawn)
                };
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "HD_TacticalAim_Retarget".Translate().ToString(),
                defaultDesc = "HD_TacticalAim_RetargetDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Skill/HD_CQB_Aiming", false)
                    ?? BaseContent.BadTex,
                Disabled = TacticalHighSpeedMovementUtility.IsActive(pawn),
                disabledReason = TacticalHighSpeedMovementUtility.IsActive(pawn)
                    ? "HD_TacticalHighSpeed_AimDisabled".Translate().ToString()
                    : null,
                action = () => TacticalAimUtility.BeginOrRetarget(pawn)
            };
            yield return new Command_Action
            {
                defaultLabel = "HD_TacticalAim_Cancel".Translate().ToString(),
                defaultDesc = "HD_TacticalAim_CancelDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Skill/HD_CQB_Aiming", false)
                    ?? BaseContent.BadTex,
                action = () => TacticalAimUtility.Cancel(pawn)
            };
        }
    }

    [HarmonyPatch(typeof(StatExtension), nameof(StatExtension.GetStatValue))]
    public static class Patch_StatExtension_TacticalAimMoveSpeed
    {
        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (stat == StatDefOf.MoveSpeed && thing is Pawn pawn
                && TacticalAimUtility.IsAiming(pawn))
            {
                __result *= Hediff_TacticalAim.MoveSpeedMultiplier;
            }
        }
    }

    [HarmonyPatch(typeof(Verb), "get_WarmupTime")]
    public static class Patch_Verb_WarmupTime_TacticalAim
    {
        public static void Postfix(Verb __instance, ref float __result)
        {
            if (TacticalAimUtility.TryGetWarmupMultiplier(__instance, out float multiplier))
            {
                __result *= multiplier;
            }
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[]
    {
        typeof(LocalTargetInfo),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool)
    })]
    public static class Patch_Verb_TryStartCastOn_TacticalAim
    {
        public static bool Prefix(Verb __instance, LocalTargetInfo castTarg, ref bool __result)
        {
            Pawn pawn = __instance?.Caster as Pawn;
            if (!TacticalAimUtility.IsRangedVerb(__instance)
                || !TacticalAimUtility.IsAiming(pawn)
                || TacticalSuddenFireUtility.IsActive(__instance))
            {
                return true;
            }

            if (TacticalAimUtility.CanFireAt(pawn, castTarg))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[]
    {
        typeof(LocalTargetInfo),
        typeof(LocalTargetInfo),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool)
    })]
    public static class Patch_Verb_TryStartCastOn_TacticalAimWithDestination
    {
        public static bool Prefix(Verb __instance, LocalTargetInfo castTarg, ref bool __result)
        {
            Pawn pawn = __instance?.Caster as Pawn;
            if (!TacticalAimUtility.IsRangedVerb(__instance)
                || !TacticalAimUtility.IsAiming(pawn)
                || TacticalSuddenFireUtility.IsActive(__instance))
            {
                return true;
            }

            if (TacticalAimUtility.CanFireAt(pawn, castTarg))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot_TacticalAimVisual
    {
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (__result)
            {
                TacticalAimUtility.NotifyShot(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
    public static class Patch_DrawEquipmentAiming_TacticalAim
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(Thing eq, ref Vector3 drawLoc, ref float aimAngle)
        {
            float previousAngle = aimAngle;
            if (!TacticalAimUtility.TryGetEquipmentVisualAngle(eq, ref aimAngle)) return;
            Pawn pawn = (eq.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            if (pawn == null) return;
            Vector3 offset = drawLoc - pawn.DrawPos;
            float height = drawLoc.y;
            offset.y = 0f;
            drawLoc = pawn.DrawPos + offset.RotatedBy(Mathf.DeltaAngle(previousAngle, aimAngle));
            drawLoc.y = height;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "RotationForcedByJob")]
    public static class Patch_PawnRenderer_RotationForcedByJob_TacticalAim
    {
        public static void Postfix(Pawn ___pawn, ref Rot4 __result)
        {
            if (!TacticalAimUtility.IsAiming(___pawn))
            {
                return;
            }

            Hediff_TacticalAim aim = TacticalAimUtility.Get(___pawn);
            if (aim != null)
            {
                // Keep the body/looking direction on the planned focus line. The
                // weapon itself is allowed to lead toward a firing target.
                __result = Rot4.FromAngleFlat(TacticalAimUtility.VisualAimAngle(___pawn));
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility),
        nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    public static class Patch_DrawEquipmentAndApparelExtras_TacticalAim
    {
        public sealed class TacticalRenderStance : Stance_Busy
        {
            public TacticalRenderStance(int ticksLeft) : base(ticksLeft)
            {
            }
        }

        public sealed class RenderState
        {
            public Pawn_StanceTracker tracker;
            public Stance previous;
        }

        private static readonly FieldInfo CurrentStanceField =
            AccessTools.Field(typeof(Pawn_StanceTracker), "curStance");
        private static readonly FieldInfo FocusTargetField =
            AccessTools.Field(typeof(Stance_Busy), "focusTarg");
        private static readonly FieldInfo VerbField =
            AccessTools.Field(typeof(Stance_Busy), "verb");
        private static readonly FieldInfo NeverAimWeaponField =
            AccessTools.Field(typeof(Stance_Busy), "neverAimWeapon");
        private static readonly Dictionary<int, TacticalRenderStance> renderStances =
            new Dictionary<int, TacticalRenderStance>();

        public static void Prefix(Pawn pawn, ref Rot4 __2, ref RenderState __state)
        {
            if (pawn == null
                || !TacticalAimUtility.IsAiming(pawn)
                || pawn.equipment?.Primary == null
                || pawn.stances == null
                || CurrentStanceField == null)
            {
                return;
            }

            __2 = Rot4.FromAngleFlat(TacticalAimUtility.VisualAimAngle(pawn));
            Stance current = pawn.stances.curStance;
            // Preserve the real firing stance, including cooldown and CE aiming.
            // Only synthesize a pose when there is no valid combat target.
            if (TacticalAimUtility.TryLiveTarget(pawn, out _)) return;

            IntVec3 targetCell = TacticalAimUtility.VisualAimTargetCell(pawn);
            if (!targetCell.IsValid)
            {
                return;
            }

            if (!renderStances.TryGetValue(
                pawn.thingIDNumber,
                out TacticalRenderStance tacticalStance))
            {
                tacticalStance = new TacticalRenderStance(2);
                renderStances[pawn.thingIDNumber] = tacticalStance;
            }
            FocusTargetField?.SetValue(
                tacticalStance,
                new LocalTargetInfo(targetCell));
            VerbField?.SetValue(
                tacticalStance,
                pawn.equipment.Primary.GetComp<CompEquippable>()?.PrimaryVerb);
            NeverAimWeaponField?.SetValue(tacticalStance, false);

            CurrentStanceField.SetValue(pawn.stances, tacticalStance);
            __state = new RenderState
            {
                tracker = pawn.stances,
                previous = current
            };
        }

        public static void Postfix(RenderState __state)
        {
            Restore(__state);
        }

        public static Exception Finalizer(Exception __exception, RenderState __state)
        {
            Restore(__state);
            return __exception;
        }

        private static void Restore(RenderState state)
        {
            if (state?.tracker != null && CurrentStanceField != null)
            {
                CurrentStanceField.SetValue(state.tracker, state.previous);
            }
        }
    }
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.EquipmentTrackerTick))]
    public static class Patch_TacticalAimVisualTick
    {
        public static void Postfix(Pawn_EquipmentTracker __instance)
        {
            TacticalAimUtility.UpdatePendingAim(__instance.pawn);
            TacticalAimUtility.UpdateVisual(__instance.pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_RotationTracker), "UpdateRotation")]
    public static class Patch_TacticalAimRotation
    {
        public static void Postfix(Pawn ___pawn)
        {
            if (TacticalAimUtility.IsAiming(___pawn) && ___pawn.Spawned
                && !___pawn.Downed && !___pawn.Dead)
                ___pawn.Rotation = Rot4.FromAngleFlat(TacticalAimUtility.VisualAimAngle(___pawn));
        }
    }

}
