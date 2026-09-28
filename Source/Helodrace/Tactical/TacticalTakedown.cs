using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using HarmonyLib;

namespace Helodrace.Tactical
{
    public enum TacticalTakedownMode
    {
        InstantKill,
        Subdue
    }

    public static class TacticalTakedownUtility
    {
        private const string CqbTrainingDefName = "HD_CQBTraining";
        private const float MaximumRange = 4f;
        private const float BackArcHalfWidth = 60f;
        private const float MaximumLethalPartHealth = 25f;
        private const float NegligibleSharpArmor = 0.20f;
        private const int SubdueStunTicks = 180;

        private static readonly FieldInfo EnemyTargetField =
            AccessTools.Field(typeof(Pawn_MindState), "enemyTarget");
        private static readonly FieldInfo LastAttackedTargetField =
            AccessTools.Field(typeof(Pawn_MindState), "lastAttackedTarget");

        public static bool HasAccess(Pawn pawn)
        {
            return pawn?.Faction == Faction.OfPlayer
                && pawn.Map != null
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Drafted
                && HasCQBTraining(pawn);
        }

        public static void BeginModeSelection(Pawn pawn)
        {
            if (!HasAccess(pawn))
            {
                return;
            }

            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "HD_TacticalTakedown_InstantKill".Translate().ToString(),
                    () => BeginTargeting(pawn, TacticalTakedownMode.InstantKill)),
                new FloatMenuOption(
                    "HD_TacticalTakedown_Subdue".Translate().ToString(),
                    () => BeginTargeting(pawn, TacticalTakedownMode.Subdue))
            }));
        }

        private static void BeginTargeting(
            Pawn pawn,
            TacticalTakedownMode mode)
        {
            Map map = pawn.Map;
            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetPawns = true,
                    canTargetBuildings = false,
                    canTargetLocations = false,
                    validator = target => CanSelectTarget(
                        pawn,
                        target.Thing as Pawn)
                },
                target => Start(pawn, target.Thing as Pawn, mode),
                highlightAction: null,
                targetValidator: target => ValidateWithMessage(pawn, target.Thing as Pawn),
                caster: pawn,
                onUpdateAction: target => DrawPreview(pawn, map, target));

            Helodrace.MapComponent_PersistentTargetingOverlay.Set(
                map,
                target => DrawPreview(pawn, map, target));
        }

        private static void DrawPreview(
            Pawn pawn,
            Map map,
            LocalTargetInfo target)
        {
            if (pawn?.Map != map)
            {
                return;
            }

            GenDraw.DrawRadiusRing(pawn.Position, MaximumRange, Color.white);
            Pawn victim = target.Thing as Pawn;
            if (victim != null && victim.Map == map)
            {
                GenDraw.DrawRadiusRing(
                    victim.Position,
                    0.55f,
                    IsValidTarget(pawn, victim, out _)
                        ? Color.green
                        : Color.red);
            }
        }

        private static bool CanSelectTarget(Pawn pawn, Pawn target)
        {
            return target != null
                && target != pawn
                && target.Map == pawn.Map
                && target.Spawned
                && !target.Dead;
        }

        public static bool IsValidTarget(
            Pawn pawn,
            Pawn target,
            out BodyPartRecord lethalPart)
        {
            return RejectionReason(pawn, target, out lethalPart) == null;
        }

        private static string RejectionReason(Pawn pawn, Pawn target, out BodyPartRecord lethalPart)
        {
            lethalPart = null;
            if (!HasAccess(pawn)) return "HD_TacticalTakedown_ReasonUser".Translate();
            if (!CanSelectTarget(pawn, target)) return "HD_TacticalTakedown_InvalidTarget".Translate();
            if (target.Downed) return "HD_TacticalTakedown_ReasonDowned".Translate();
            if (pawn.Position.DistanceTo(target.Position) > MaximumRange)
                return "HD_TacticalTakedown_ReasonRange".Translate(MaximumRange);
            if (!IsBehindTarget(pawn, target)) return "HD_TacticalTakedown_ReasonBehind".Translate();
            if (HasDetectedAttacker(pawn, target)) return "HD_TacticalTakedown_ReasonDetected".Translate();
            if (IsAnomalyLike(target)) return "HD_TacticalTakedown_ReasonAnomaly".Translate();

            int pawnMelee = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
            int targetMelee = target.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
            if (pawnMelee < targetMelee + 2)
            {
                return "HD_TacticalTakedown_ReasonMelee".Translate(pawnMelee, targetMelee + 2);
            }

            lethalPart = FindExposedLethalPart(target);
            return lethalPart != null ? null : "HD_TacticalTakedown_ReasonPart".Translate(MaximumLethalPartHealth).ToString();
        }

        private static bool ValidateWithMessage(Pawn pawn, Pawn target)
        {
            string reason = RejectionReason(pawn, target, out _);
            if (reason == null) return true;
            Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
            return false;
        }

        private static bool IsBehindTarget(Pawn attacker, Pawn target)
        {
            Vector3 direction = attacker.DrawPos - target.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return false;
            }

            float toAttackerAngle = direction.AngleFlat();
            float facingDelta = Mathf.Abs(Mathf.DeltaAngle(
                target.Rotation.AsAngle,
                toAttackerAngle));
            return facingDelta >= 180f - BackArcHalfWidth;
        }

        private static bool HasDetectedAttacker(Pawn attacker, Pawn target)
        {
            object enemyTarget = EnemyTargetField?.GetValue(target.mindState);
            if (enemyTarget == attacker)
            {
                return true;
            }

            object lastAttackedTarget = LastAttackedTargetField?.GetValue(target.mindState);
            if (lastAttackedTarget is LocalTargetInfo localTarget
                && localTarget.Thing == attacker)
            {
                return true;
            }

            return target.LastAttackedTarget.Thing == attacker;
        }

        private static bool IsAnomalyLike(Pawn pawn)
        {
            string defName = pawn.def?.defName ?? string.Empty;
            string kindName = pawn.kindDef?.defName ?? string.Empty;
            string combined = defName + "/" + kindName;
            string[] anomalyMarkers =
            {
                "Anomaly",
                "Entity",
                "Sightstealer",
                "Shambler",
                "Metalhorror",
                "Devourer",
                "Gorehulk",
                "Revenant",
                "Dread"
            };
            for (int i = 0; i < anomalyMarkers.Length; i++)
            {
                if (combined.IndexOf(
                    anomalyMarkers[i],
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static BodyPartRecord FindExposedLethalPart(Pawn target)
        {
            if (target.health?.hediffSet == null)
            {
                return null;
            }

            return target.health.hediffSet.GetNotMissingParts()
                .Where(IsLethalPart)
                .Where(part => part.depth == BodyPartDepth.Outside)
                .Where(part => part.def.GetMaxHealth(target)
                    <= MaximumLethalPartHealth)
                .Where(part => !IsProtectedByApparel(target, part))
                .OrderBy(part => part.def.GetMaxHealth(target))
                .FirstOrDefault();
        }

        private static bool IsLethalPart(BodyPartRecord part)
        {
            string defName = part.def?.defName;
            return defName == "Head"
                || defName == "Neck"
                || defName == "Heart"
                || defName == "Lung"
                || defName == "Liver";
        }

        private static bool IsProtectedByApparel(
            Pawn pawn,
            BodyPartRecord part)
        {
            if (pawn.apparel?.WornApparel == null)
            {
                return false;
            }

            return pawn.apparel.WornApparel.Any(apparel =>
                apparel?.def?.apparel != null
                && HasMeaningfulProtection(apparel, part));
        }

        private static bool HasMeaningfulProtection(Apparel apparel, BodyPartRecord part)
        {
            if (apparel.def.apparel.CoversBodyPart(part)
                && IsMeaningfulArmor(apparel.GetStatValue(StatDefOf.ArmorRating_Sharp)))
                return true;

            // A weak carrier must not hide the protection of an installed plate.
            var modular = apparel.TryGetComp<Helodrace.ModernWar.CompModularArmor>();
            return modular != null && modular.InstalledParts.Any(record =>
                record?.part != null && record.EffectivePosition != null
                && record.EffectivePosition.Covers(part)
                && IsMeaningfulArmor(record.part.ArmorFor(DamageArmorCategoryDefOf.Sharp)));
        }

        private static bool IsMeaningfulArmor(float sharpArmor)
        {
            return sharpArmor > NegligibleSharpArmor;
        }

        private static void Start(
            Pawn pawn,
            Pawn target,
            TacticalTakedownMode mode)
        {
            if (!IsValidTarget(pawn, target, out BodyPartRecord lethalPart))
            {
                Messages.Message(
                    RejectionReason(pawn, target, out _),
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (!pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                Messages.Message("HD_TacticalTakedown_NoPath".Translate(), pawn,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            JobDef jobDef = DefDatabase<JobDef>.GetNamed(mode == TacticalTakedownMode.InstantKill
                ? "HD_TacticalTakedown" : "HD_TacticalSubdue");
            Job job = JobMaker.MakeJob(jobDef, target);
            job.count = target.RaceProps.body.AllParts.IndexOf(lethalPart);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public static bool CanContinueApproach(Pawn pawn, Pawn target, BodyPartRecord part)
        {
            return HasAccess(pawn) && CanSelectTarget(pawn, target) && !target.Downed
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)
                && pawn.stances?.stunner?.Stunned != true
                && !HasDetectedAttacker(pawn, target)
                && part != null && target.health.hediffSet.GetNotMissingParts().Contains(part);
        }

        public static bool CompleteTakedown(Pawn pawn, Pawn target, BodyPartRecord part,
            TacticalTakedownMode mode)
        {
            if (!CanContinueApproach(pawn, target, part)
                || !pawn.CanReachImmediate(target, PathEndMode.Touch)
                || !GenSight.LineOfSight(pawn.Position, target.Position, pawn.Map)
                || !IsValidTarget(pawn, target, out _)
                || IsProtectedByApparel(target, part)) return false;

            Map impactMap = target.Map;
            Vector3 impactPosition = target.DrawPos;
            BeginAnimation(pawn, target);
            SoundDefOf.Pawn_Melee_Punch_HitPawn.PlayOneShot(
                new TargetInfo(target.Position, target.Map));

            if (mode == TacticalTakedownMode.InstantKill)
            {
                DamageInfo damage = new DamageInfo(
                    DamageDefOf.Stab,
                    part.def.GetMaxHealth(target) + 5f,
                    2f,
                    -1f,
                    pawn,
                    part,
                    pawn.equipment?.Primary?.def,
                    DamageInfo.SourceCategory.ThingOrUnknown);
                // Eligibility has already checked armor and the selected part.
                // Do not reroll damage or redirect to a different body part.
                target.health.AddHediff(HediffDefOf.MissingBodyPart, part, damage);
                if (!target.Dead) target.Kill(damage);
            }
            else
            {
                target.equipment?.DropAllEquipment(
                    target.Position,
                    forbid: false);
                target.stances?.stunner?.StunFor(
                    SubdueStunTicks,
                    pawn,
                    true,
                    true,
                    false);
                target.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }

            FleckMaker.ThrowDustPuff(impactPosition, impactMap, 0.8f);
            return true;
        }

        private static void BeginAnimation(Pawn attacker, Pawn target)
        {
            Vector3 direction = target.DrawPos - attacker.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            attacker.Rotation = Rot4.FromAngleFlat(direction.AngleFlat());
        }

        public static Command CreateCommand(Pawn pawn)
        {
            return new Command_Action
            {
                defaultLabel = "HD_TacticalTakedown_Command".Translate().ToString(),
                defaultDesc = "HD_TacticalTakedown_CommandDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Skill/HD_CQB_Smite", false)
                    ?? BaseContent.BadTex,
                action = () => BeginModeSelection(pawn)
            };
        }

        private static bool HasCQBTraining(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(CqbTrainingDefName);
            return pawn?.health?.hediffSet != null
                && def != null
                && pawn.health.hediffSet.HasHediff(def);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalTakedown
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (__instance?.Faction == Faction.OfPlayer
                && TacticalTakedownUtility.HasAccess(__instance))
            {
                __result = __result.Concat(new[]
                {
                    TacticalTakedownUtility.CreateCommand(__instance)
                });
            }
        }
    }
}
