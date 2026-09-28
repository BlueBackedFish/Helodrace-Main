using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace.Tactical
{
    public static class TacticalCloseQuartersStrikeUtility
    {
        private const string CqbTrainingDefName = "HD_CQBTraining";
        private const float Range = 1.5f;
        private const int KnockbackDistance = 2;
        private const int StunTicks = 60;
        private const int ReducedStunTicks = 30;
        private const int StrikeAnimationTicks = 24;
        private const int StrikeImpactTicks = 7;
        private const float StrikeThrustDistance = 0.28f;
        private static readonly Dictionary<int, StrikeAnimationState> strikeAnimations =
            new Dictionary<int, StrikeAnimationState>();

        private sealed class StrikeAnimationState
        {
            public int startTick;
            public float targetAngle;
            public float returnAngle;
        }

        public static bool CanUse(Pawn pawn)
        {
            Verb verb = PrimaryVerb(pawn);
            return pawn?.Map != null
                && !pawn.Dead
                && !pawn.Downed
                && HasCQBTraining(pawn)
                && IsLongGun(pawn, verb);
        }

        public static bool IsValidTarget(Pawn pawn, LocalTargetInfo target)
        {
            Pawn victim = PawnAt(pawn, target);
            if (!CanUse(pawn)
                || victim == null
                || victim == pawn
                || victim.Dead
                || victim.Map != pawn.Map
                || !victim.Spawned)
            {
                return false;
            }

            // Let the targeter select an adjacent pawn even when RimWorld's
            // sight test rejects the occupied cells around it.  The action
            // still performs the sight check before applying the strike.
            if (pawn.Position.DistanceToSquared(victim.Position) > Range * Range)
            {
                return false;
            }

            return true;
        }

        public static bool IsValidTarget(Pawn pawn, TargetInfo target)
        {
            return target.IsValid
                && IsValidTarget(pawn, target.HasThing
                    ? new LocalTargetInfo(target.Thing)
                    : new LocalTargetInfo(target.Cell));
        }

        private static bool CanSelectTarget(Pawn pawn, TargetInfo target)
        {
            // Preserve the actual mouse-picked pawn, including when several
            // pawns occupy the same cell. Range is checked when clicked.
            Pawn victim = target.Thing as Pawn;
            return victim != null && victim != pawn && victim.Spawned
                && !victim.Dead && victim.Map == pawn.Map;
        }

        public static void BeginTargeting(Pawn pawn)
        {
            if (!CanUse(pawn))
            {
                return;
            }

            Map map = pawn.Map;
            Action<LocalTargetInfo> drawPreview = target =>
            {
                GenDraw.DrawRadiusRing(pawn.Position, Range, Color.white);
                Pawn victim = PawnAt(pawn, target);
                if (victim != null && victim.Map == map)
                {
                    GenDraw.DrawRadiusRing(
                        victim.Position,
                        0.55f,
                        CheckTarget(pawn, target, false) ? Color.yellow : Color.red);
                }
            };

            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetPawns = true,
                    canTargetLocations = false,
                    canTargetBuildings = false,
                    canTargetItems = false,
                    validator = target => CanSelectTarget(pawn, target)
                },
                target => Start(pawn, target),
                highlightAction: null,
                targetValidator: target => CheckTarget(pawn, target, true),
                caster: pawn,
                onUpdateAction: drawPreview);
        }

        public static void Start(Pawn pawn, LocalTargetInfo target)
        {
            if (!CheckTarget(pawn, target, true))
            {
                return;
            }

            Pawn victim = PawnAt(pawn, target);
            BeginStrikeAnimation(pawn, victim);
            SoundDefOf.Pawn_Melee_Punch_HitPawn.PlayOneShot(
                new TargetInfo(victim.Position, victim.Map));

            IntVec3 pushedTo = FindPushCell(pawn, victim, out bool hitWall);
            // Melee warmup/cooldown and an in-flight chase step must not keep
            // using the position from before the knockback.
            victim.stances?.CancelBusyStanceHard();
            if (pushedTo.IsValid)
            {
                victim.pather?.StopDead();
                victim.Position = pushedTo;
                victim.Notify_Teleported(endCurrentJob: true, resetTweenedPos: true);
            }

            int stunTicks = StunDuration(victim.BodySize, hitWall);
            if (stunTicks > 0)
            {
                victim.stances?.stunner?.StunFor(
                    stunTicks,
                    pawn,
                    true,
                    true,
                    false);
            }

            FleckMaker.ThrowDustPuff(victim.DrawPos, victim.Map, 0.8f);
        }

        private static void BeginStrikeAnimation(Pawn attacker, Pawn victim)
        {
            Vector3 direction = victim.DrawPos - attacker.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            float targetAngle = TacticalAimUtility.NormalizeAngle(direction.AngleFlat());
            float returnAngle = TacticalAimUtility.IsAiming(attacker)
                ? TacticalAimUtility.VisualAimAngle(attacker)
                : attacker.Rotation.AsAngle;
            strikeAnimations[attacker.thingIDNumber] = new StrikeAnimationState
            {
                startTick = Find.TickManager?.TicksGame ?? 0,
                targetAngle = targetAngle,
                returnAngle = returnAngle
            };
        }

        public static bool TryGetVisual(
            Pawn pawn,
            out float angle,
            out float thrust)
        {
            angle = 0f;
            thrust = 0f;
            if (pawn == null
                || !strikeAnimations.TryGetValue(
                    pawn.thingIDNumber,
                    out StrikeAnimationState state))
            {
                return false;
            }

            int elapsed = (Find.TickManager?.TicksGame ?? 0) - state.startTick;
            if (elapsed >= StrikeAnimationTicks || pawn.Dead || pawn.Downed || !pawn.Spawned)
            {
                strikeAnimations.Remove(pawn.thingIDNumber);
                return false;
            }

            if (elapsed < StrikeImpactTicks)
            {
                float progress = Mathf.Clamp01(elapsed / (float)StrikeImpactTicks);
                float eased = 1f - Mathf.Pow(1f - progress, 3f);
                angle = Mathf.LerpAngle(state.returnAngle, state.targetAngle, eased);
                thrust = Mathf.SmoothStep(0f, 1f, progress);
            }
            else
            {
                float progress = Mathf.Clamp01(
                    (elapsed - StrikeImpactTicks)
                    / (float)(StrikeAnimationTicks - StrikeImpactTicks));
                float eased = progress * progress * (3f - 2f * progress);
                angle = Mathf.LerpAngle(state.targetAngle, state.returnAngle, eased);
                thrust = 1f - Mathf.SmoothStep(0f, 1f, progress);
            }

            return true;
        }

        public static void AdjustEquipmentDraw(
            Thing equipment,
            ref Vector3 drawLoc,
            ref float aimAngle)
        {
            Pawn pawn = WielderOf(equipment);
            if (!TryGetVisual(pawn, out float visualAngle, out float thrust))
            {
                return;
            }

            float previousAngle = aimAngle;
            float height = drawLoc.y;
            Vector3 offset = drawLoc - pawn.DrawPos;
            offset.y = 0f;
            aimAngle = visualAngle;
            drawLoc = pawn.DrawPos
                + offset.RotatedBy(Mathf.DeltaAngle(previousAngle, visualAngle));
            drawLoc.y = height;
            drawLoc += Vector3.forward.RotatedBy(visualAngle)
                * (StrikeThrustDistance * thrust);
        }

        private static Pawn WielderOf(Thing equipment)
        {
            return (equipment?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
        }

        private static bool CheckTarget(Pawn pawn, LocalTargetInfo target, bool showMessage)
        {
            Pawn victim = PawnAt(pawn, target);
            string reason = null;
            if (!CanUse(pawn) || victim == null || victim == pawn
                || victim.Dead || !victim.Spawned || victim.Map != pawn.Map)
                reason = "HD_TacticalCloseQuartersStrike_InvalidTarget";
            else if (pawn.Position.DistanceToSquared(victim.Position) > Range * Range)
                reason = "HD_TacticalCloseQuartersStrike_TooFar";
            else if (!GenSight.LineOfSight(pawn.Position, victim.Position, pawn.Map))
                reason = "HD_TacticalCloseQuartersStrike_Blocked";

            if (reason == null) return true;
            if (showMessage)
                Messages.Message(reason.Translate(), pawn, MessageTypeDefOf.RejectInput, false);
            return false;
        }

        private static int StunDuration(float bodySize, bool hitWall)
        {
            int duration = bodySize >= 2f ? ReducedStunTicks : StunTicks;
            return hitWall ? duration * 2 : duration;
        }

        private static IntVec3 FindPushCell(Pawn attacker, Pawn victim, out bool hitWall)
        {
            hitWall = false;
            int knockbackDistance = victim.BodySize >= 3f
                ? 0
                : victim.BodySize >= 2f
                    ? 1
                    : KnockbackDistance;
            if (knockbackDistance <= 0)
            {
                return IntVec3.Invalid;
            }

            IntVec3 direction = PushDirection(attacker.Position, victim.Position, victim.Rotation);

            IntVec3 result = IntVec3.Invalid;
            IntVec3 previous = victim.Position;
            for (int distance = 1; distance <= knockbackDistance; distance++)
            {
                IntVec3 candidate = victim.Position + direction * distance;
                if (candidate.InBounds(victim.Map)
                    && candidate.GetEdifice(victim.Map)?.def.Fillage == FillCategory.Full
                    && !candidate.Walkable(victim.Map))
                {
                    hitWall = true;
                    break;
                }
                if (!candidate.InBounds(victim.Map)
                    || !candidate.Standable(victim.Map)
                    || candidate.GetFirstPawn(victim.Map) != null
                    || !GenSight.LineOfSight(previous, candidate, victim.Map))
                {
                    break;
                }

                result = candidate;
                previous = candidate;
            }

            return result;
        }

        private static IntVec3 PushDirection(IntVec3 attacker, IntVec3 victim, Rot4 victimFacing)
        {
            IntVec3 difference = victim - attacker;
            if (difference.x == 0 && difference.z == 0)
            {
                // Overlapping melee pawns have no cell delta. Push the victim
                // backwards relative to its attack facing instead of doing nothing.
                return (victimFacing.IsValid ? victimFacing : Rot4.South).FacingCell * -1;
            }
            return new IntVec3(Math.Sign(difference.x), 0, Math.Sign(difference.z));
        }

        private static Verb PrimaryVerb(Pawn pawn)
        {
            return pawn?.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
        }

        private static Pawn PawnAt(Pawn pawn, LocalTargetInfo target)
        {
            Pawn victim = target.Thing as Pawn;
            if (victim != null)
            {
                return victim;
            }

            if (pawn?.Map == null
                || !target.Cell.IsValid
                || !target.Cell.InBounds(pawn.Map))
            {
                return null;
            }

            return target.Cell.GetFirstPawn(pawn.Map);
        }

        private static bool IsLongGun(Pawn pawn, Verb verb)
        {
            Thing equipment = verb?.EquipmentSource
                ?? pawn?.equipment?.Primary;
            ThingDef weapon = equipment?.def;
            if (weapon == null || !TacticalAimUtility.IsRangedVerb(verb))
            {
                return false;
            }

            string defName = weapon.defName ?? string.Empty;
            if (ContainsAny(defName, "pistol", "revolver", "handgun", "sidearm"))
            {
                return false;
            }

            List<string> tags = weapon.weaponTags ?? new List<string>();
            if (tags.Any(tag => ContainsAny(
                tag,
                "pistol",
                "revolver",
                "handgun",
                "sidearm",
                "industrialgunshort",
                "rangedlight")))
            {
                return false;
            }

            if (ContainsAny(defName, "rifle", "shotgun", "carbine", "launcher", "longgun")
                || tags.Any(tag => ContainsAny(
                    tag,
                    "rifle",
                    "shotgun",
                    "carbine",
                    "launcher",
                    "longgun",
                    "advanced")))
            {
                return true;
            }

            return weapon.GetStatValueAbstract(StatDefOf.Mass) >= 1.8f
                && (verb.verbProps?.range ?? 0f) >= 10f;
        }

        private static bool ContainsAny(string value, params string[] fragments)
        {
            for (int i = 0; i < fragments.Length; i++)
            {
                if (value.IndexOf(fragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCQBTraining(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(CqbTrainingDefName);
            return pawn?.health?.hediffSet != null
                && def != null
                && pawn.health.hediffSet.HasHediff(def);
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
    public static class Patch_DrawEquipmentAiming_TacticalCloseQuartersStrike
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Thing eq, ref Vector3 drawLoc, ref float aimAngle)
        {
            TacticalCloseQuartersStrikeUtility.AdjustEquipmentDraw(
                eq,
                ref drawLoc,
                ref aimAngle);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalCloseQuartersStrike
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (__instance?.Faction != Faction.OfPlayer
                || !TacticalCloseQuartersStrikeUtility.CanUse(__instance))
            {
                return;
            }

            __result = __result.Concat(new[]
            {
                new Command_Action
                {
                    defaultLabel = "HD_TacticalCloseQuartersStrike_Command".Translate().ToString(),
                    defaultDesc = "HD_TacticalCloseQuartersStrike_CommandDesc".Translate().ToString(),
                    icon = ContentFinder<Texture2D>.Get("Skill/HD_CQB_Smite", false)
                        ?? BaseContent.BadTex,
                    action = () => TacticalCloseQuartersStrikeUtility.BeginTargeting(__instance)
                }
            });
        }
    }
}
