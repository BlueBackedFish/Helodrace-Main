using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Helodrace.Tactical;
namespace Helodrace.Tests.FrozenTactical
{
internal static class AimGeometry
{
private static float AngleToFocus(Vector3 origin, Vector3 point, float fallback)
        {
            Vector3 direction = point - origin;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f
                ? TacticalAimUtility.NormalizeAngle(
                    (float)(Math.Atan2(direction.x, direction.z) * 180.0 / Math.PI)) : fallback;
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

            angle = TacticalAimUtility.NormalizeAngle(direction.AngleFlat());
            return true;
        }
private static bool TryGetFocusAngle(Pawn pawn, IntVec3 targetCell, out float angle)
        {
            angle = 0f;
            if (!TacticalAimUtility.HasCQBTraining(pawn) || pawn?.Map == null
                || !targetCell.IsValid || targetCell == pawn.Position)
            {
                return false;
            }

            Vector3 direction = (targetCell - pawn.Position).ToVector3();
            if (direction.sqrMagnitude < 0.001f)
            {
                return false;
            }

            angle = TacticalAimUtility.NormalizeAngle(direction.AngleFlat());
            return true;
        }
public static IntVec3 VisualAimTargetCell(Pawn pawn)
        {
            Vector3 direction = Vector3.forward.RotatedBy(TacticalAimUtility.VisualAimAngle(pawn));
            return pawn.Position + new IntVec3(
                Mathf.RoundToInt(direction.x * 12f), 0,
                Mathf.RoundToInt(direction.z * 12f));
        }
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
internal static class StrikeGeometry
{
private const int StrikeAnimationTicks=24, StrikeImpactTicks=7; private const float StrikeThrustDistance=.28f;
private static readonly Dictionary<int,StrikeAnimationState> strikeAnimations=new();
private sealed class StrikeAnimationState
        {
            public int startTick;
            public float targetAngle;
            public float returnAngle;
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
                angle = AngleMath.Interpolate(state.returnAngle, state.targetAngle, eased);
                thrust = Mathf.SmoothStep(0f, 1f, progress);
            }
            else
            {
                float progress = Mathf.Clamp01(
                    (elapsed - StrikeImpactTicks)
                    / (float)(StrikeAnimationTicks - StrikeImpactTicks));
                float eased = progress * progress * (3f - 2f * progress);
                angle = AngleMath.Interpolate(state.targetAngle, state.returnAngle, eased);
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
}
internal static class CoordinatedGeometry
{
internal static object FixtureState(Pawn shooter,Pawn partner,Verb verb)=>new State{shooter=shooter,partner=partner,verb=verb,shooterOrigin=shooter.Position,partnerOrigin=partner.Position,target=null,part=null,shotCount=0,expiryTick=0};
private const float TargetHalfAngle=30;private static readonly Dictionary<int,State> states=new();
private sealed class State
        {
            public Pawn shooter;
            public Pawn partner;
            public Verb verb;
            public IntVec3 shooterOrigin;
            public IntVec3 partnerOrigin;
            public Pawn target;
            public BodyPartRecord part;
            public int shotCount;
            public int expiryTick;
        }
private static bool TargetInPartnerArc(
            State state,
            LocalTargetInfo target)
        {
            if (!target.IsValid
                || !target.Cell.InBounds(state.shooter.Map)
                || state.verb == null
                || !state.verb.CanHitTarget(target))
            {
                return false;
            }

            Vector3 direction = target.HasThing
                ? target.Thing.DrawPos - state.shooter.DrawPos
                : target.Cell.ToVector3Shifted() - state.shooter.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return false;
            }

            float targetAngle = direction.AngleFlat();
            float referenceAngle = (state.partner.DrawPos - state.shooter.DrawPos).AngleFlat();
            return IsWithinArc(referenceAngle, targetAngle)
                && state.shooter.Position.DistanceTo(target.Cell)
                    <= state.verb.EffectiveRange
                && GenSight.LineOfSight(
                    state.shooter.Position,
                    target.Cell,
                    state.shooter.Map);
        }
private static bool IsWithinArc(float referenceAngle, float targetAngle)
        {
            return Math.Abs(TacticalAimUtility.NormalizeAngle(targetAngle - referenceAngle + 180f)
                - 180f) <= TargetHalfAngle;
        }
private static bool IsFormationValid(State state)
        {
            return state.shooter != null
                && state.partner != null
                && state.shooter.Spawned
                && state.partner.Spawned
                && !state.shooter.Dead
                && !state.partner.Dead
                && !state.shooter.Downed
                && state.shooter.Drafted
                && HasHighTraining(state.shooter)
                && PrimaryVerb(state.shooter) == state.verb
                && !state.partner.Downed
                && state.shooter.Map != null
                && state.shooter.Map == state.partner.Map
                && state.shooter.Position == state.shooterOrigin
                && state.partner.Position == state.partnerOrigin
                && CanSelectPartner(state.shooter, state.partner);
        }
private static bool CanSelectPartner(Pawn pawn, Pawn partner)
        {
            return pawn != null
                && partner != null
                && partner != pawn
                && partner.Map == pawn.Map
                && partner.Spawned
                && !partner.Dead
                && !partner.Downed
                && AreAdjacent(pawn.Position, partner.Position)
                && GenSight.LineOfSight(
                    pawn.Position,
                    partner.Position,
                    pawn.Map);
        }
private static bool AreAdjacent(IntVec3 shooter, IntVec3 partner)
        {
            int distance = shooter.DistanceToSquared(partner);
            return distance > 0 && distance <= 2;
        }
private static Verb PrimaryVerb(Pawn pawn)
        {
            return pawn?.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
        }
private static bool HasHighTraining(Pawn pawn)
        {
            return TacticalAimUtility.HasAdvancedCQBTraining(pawn);
        }
public static void DrawActive(Map map)
        {
            foreach (State state in states.Values)
            {
                if (state.shooter?.Map != map || !IsFormationValid(state))
                {
                    continue;
                }

                Vector3 start = state.shooter.DrawPos;
                float referenceAngle = (state.partner.DrawPos - start).AngleFlat();
                Vector3 left = start + Vector3.forward.RotatedBy(
                    referenceAngle - TargetHalfAngle)
                    * state.verb.EffectiveRange;
                Vector3 right = start + Vector3.forward.RotatedBy(
                    referenceAngle + TargetHalfAngle)
                    * state.verb.EffectiveRange;
                GenDraw.DrawLineBetween(state.shooter.DrawPos, state.partner.DrawPos, SimpleColor.Cyan, 0.08f);
                GenDraw.DrawLineBetween(start, left, SimpleColor.Cyan, 0.04f);
                GenDraw.DrawLineBetween(start, right, SimpleColor.Cyan, 0.04f);
            }
        }
}
internal static class AngleMath
{internal static float Interpolate(float from,float to,float fraction)=>from+Waylith.Math.Mathf.DeltaAngle(from,to)*Waylith.Math.Mathf.Clamp01(fraction);}
}
