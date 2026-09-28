using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public static class TacticalMovementRunAndGunUtility
    {
        public static bool IsMovementJob(Pawn pawn)
        {
            return pawn?.jobs?.curJob?.def == JobDefOf.Goto
                && pawn.pather?.Moving == true;
        }

        public static bool IsMobileFireWindow(Pawn pawn, Verb verb)
        {
            return pawn?.Spawned == true
                && (pawn.Drafted || TacticalMultiTargetFireUtility.IsActive(verb))
                && TacticalAimUtility.IsRangedVerb(verb)
                && IsMovementJob(pawn)
                && (TacticalAimUtility.IsAiming(pawn)
                    || TacticalSuddenFireUtility.IsActive(verb)
                    || TacticalMultiTargetFireUtility.IsActive(verb)
                    || TacticalMovementFireUtility.IsActive(pawn));
        }

        public static bool IsMobileFireTarget(
            Pawn pawn,
            Verb verb,
            LocalTargetInfo target)
        {
            if (!IsMobileFireWindow(pawn, verb)
                || !target.IsValid
                || !verb.CanHitTarget(target))
            {
                return false;
            }

            if (TacticalSuddenFireUtility.IsActive(verb))
            {
                return TacticalSuddenFireUtility.IsValidTarget(pawn, target);
            }

            if (TacticalMultiTargetFireUtility.IsActive(verb))
            {
                return TacticalAimUtility.CanFireAt(pawn, target);
            }

            if (TacticalMovementFireUtility.IsActive(pawn))
            {
                return TacticalMovementFireUtility.IsCurrentTarget(
                    pawn,
                    verb,
                    target);
            }

            return TacticalAimUtility.CanFireAt(pawn, target);
        }

        public static bool IsTacticalStance(Stance stance)
        {
            return stance is Stance_TacticalMovementWarmup
                || stance is Stance_TacticalMovementCooldown;
        }

        public static Stance_Cooldown KeepMovingCooldown(
            Pawn_StanceTracker stanceTracker,
            Stance_Cooldown stance)
        {
            Pawn pawn = stanceTracker?.pawn;
            if (pawn == null
                || !IsTacticalStance(stanceTracker.curStance)
                || !IsMobileFireWindow(pawn, stance.verb))
            {
                return stance;
            }

            return new Stance_TacticalMovementCooldown(
                TacticalMovementFireUtility.IsActive(stance.verb)
                    ? Mathf.Max(1, Mathf.CeilToInt(stance.ticksLeft * 0.5f))
                    : stance.ticksLeft,
                stance.focusTarg,
                stance.verb);
        }

        public static bool TryStartAimedAutoAttack(Pawn pawn)
        {
            if (TacticalMultiTargetFireUtility.IsSequenceActive(pawn)) return false;
            if (TacticalMovementFireUtility.IsActive(pawn))
            {
                return TacticalMovementFireUtility.TryStartCurrentAttack(pawn);
            }

            if (pawn == null
                || !TacticalAimUtility.IsAiming(pawn)
                || !IsMovementJob(pawn)
                || pawn.stances?.curStance == null
                || pawn.stances.curStance.StanceBusy
                || IsTacticalStance(pawn.stances.curStance))
            {
                return false;
            }

            Verb verb = pawn.TryGetAttackVerb(null);
            if (!TacticalAimUtility.IsRangedVerb(verb)
                || verb.Bursting)
            {
                return false;
            }

            TargetScanFlags targetScanFlags =
                TargetScanFlags.NeedLOSToPawns
                | TargetScanFlags.NeedLOSToNonPawns
                | TargetScanFlags.NeedThreat
                | TargetScanFlags.NeedAutoTargetable;
            if (verb.IsIncendiary_Ranged())
            {
                targetScanFlags |= TargetScanFlags.NeedNonBurning;
            }

            Predicate<Thing> validator = thing =>
            {
                if (thing == null || thing == pawn || !thing.Spawned)
                {
                    return false;
                }

                return IsMobileFireTarget(
                    pawn,
                    verb,
                    new LocalTargetInfo(thing));
            };

            Thing target = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(
                pawn,
                targetScanFlags,
                validator,
                0f,
                9999f);
            if (target == null)
            {
                return false;
            }

            pawn.TryStartAttack(target);
            return true;
        }
    }

    public sealed class Stance_TacticalMovementWarmup : Stance_Warmup
    {
        public override bool StanceBusy => false;

        public Stance_TacticalMovementWarmup()
        {
        }

        public Stance_TacticalMovementWarmup(
            int ticks,
            LocalTargetInfo focusTarg,
            Verb verb)
            : base(ticks, focusTarg, verb)
        {
        }
    }

    public sealed class Stance_TacticalMovementCooldown : Stance_Cooldown
    {
        private static readonly FieldInfo BurstShotsLeftField =
            AccessTools.Field(typeof(Verb), "burstShotsLeft");
        private readonly int ticksBetweenBurst;

        public override bool StanceBusy =>
            Pawn?.jobs?.curJob == null
                || Pawn.jobs.curJob.def != JobDefOf.Goto;

        public Stance_TacticalMovementCooldown()
        {
            ticksBetweenBurst = 0;
        }

        public Stance_TacticalMovementCooldown(
            int ticks,
            LocalTargetInfo focusTarg,
            Verb verb)
            : base(ticks, focusTarg, verb)
        {
            ticksBetweenBurst = ticks;
        }

        protected override void Expire()
        {
            int burstsLeft = BurstShotsLeftField?.GetValue(verb) as int? ?? 0;
            if (burstsLeft > 0
                && Pawn != null
                && TacticalMovementRunAndGunUtility.IsMobileFireWindow(Pawn, verb))
            {
                stanceTracker.SetStance(
                    new Stance_TacticalMovementWarmup(
                        ticksBetweenBurst,
                        focusTarg,
                        verb));
                return;
            }

            base.Expire();
        }
    }

    [HarmonyPatch(typeof(JobDriver), "SetupToils")]
    public static class Patch_JobDriver_SetupToils_TacticalMovement
    {
        public static void Postfix(JobDriver __instance, List<Toil> ___toils)
        {
            if (!(__instance is JobDriver_Goto jobDriver)
                || ___toils == null
                || !___toils.Any())
            {
                return;
            }

            Toil firstToil = ___toils[0];
            firstToil.AddPreTickAction(() =>
            {
                Pawn pawn = jobDriver.pawn;
                if (pawn == null
                    || !pawn.IsHashIntervalTick(2)
                    || pawn.IsBurning()
                    || !pawn.Drafted
                    || pawn.Downed
                    || (!TacticalAimUtility.IsAiming(pawn)
                        && !TacticalMovementFireUtility.IsActive(pawn)))
                {
                    return;
                }

                TacticalMovementRunAndGunUtility.TryStartAimedAutoAttack(pawn);
            });
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
    public static class Patch_Verb_TryStartCastOn_TacticalMovement
    {
        public static bool Prefix(
            Verb __instance,
            LocalTargetInfo castTarg,
            bool surpriseAttack,
            bool canHitNonTargetPawns,
            ref bool ___surpriseAttack,
            ref bool ___canHitNonTargetPawnsNow,
            ref LocalTargetInfo ___currentTarget,
            ref bool __result)
        {
            Pawn pawn = __instance?.Caster as Pawn;
            if (!TacticalMovementRunAndGunUtility.IsMobileFireTarget(
                    pawn,
                    __instance,
                    castTarg))
            {
                return true;
            }

            if (__instance.state == VerbState.Bursting)
            {
                return true;
            }

            ___surpriseAttack = surpriseAttack;
            ___canHitNonTargetPawnsNow = canHitNonTargetPawns;
            ___currentTarget = castTarg;

            ShootLine shootLine;
            if (!__instance.TryFindShootLineFromTo(
                    __instance.caster.Position,
                    castTarg,
                    out shootLine))
            {
                __result = false;
                return false;
            }

            pawn.Drawer.Notify_WarmingCastAlongLine(
                shootLine,
                __instance.caster.Position);

            float aimingDelay = pawn.GetStatValue(
                StatDefOf.AimingDelayFactor,
                true);
            int ticks = Mathf.Max(
                1,
                (__instance.WarmupTime * aimingDelay).SecondsToTicks());
            if (TacticalMovementFireUtility.IsActive(__instance))
            {
                ticks = Mathf.Max(1, Mathf.CeilToInt(ticks * 0.35f));
            }
            pawn.stances.SetStance(
                new Stance_TacticalMovementWarmup(
                    ticks,
                    castTarg,
                    __instance));
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Patch_Verb_TryCastNextBurstShot_TacticalMovement
    {
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator il)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            MethodInfo setStance = AccessTools.Method(
                typeof(Pawn_StanceTracker),
                nameof(Pawn_StanceTracker.SetStance));
            MethodInfo keepMovingCooldown = AccessTools.Method(
                typeof(TacticalMovementRunAndGunUtility),
                nameof(TacticalMovementRunAndGunUtility.KeepMovingCooldown));
            LocalBuilder stanceTemp = il.DeclareLocal(typeof(Stance));

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Callvirt
                    || !(code[i].operand is MethodInfo method)
                    || method != setStance)
                {
                    continue;
                }

                code.Insert(i++, new CodeInstruction(OpCodes.Stloc, stanceTemp));
                code.Insert(i++, new CodeInstruction(OpCodes.Dup));
                code.Insert(i++, new CodeInstruction(OpCodes.Ldloc, stanceTemp));
                code.Insert(i++, new CodeInstruction(OpCodes.Call, keepMovingCooldown));
            }

            return code;
        }
    }
}
