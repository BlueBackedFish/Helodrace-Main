using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class MultiCrewTurretRole
    {
        public string id;
        public string labelKey;
        public IntVec3 interactionOffset;
        public bool required = true;
        public bool usesVanillaManning;
        public bool useShootingSkill;
        public float accuracyMultiplier = 1f;
        public float cooldownMultiplier = 1f;
        public float warmupMultiplier = 1f;

        public string Label => labelKey.NullOrEmpty()
            ? id ?? "Role"
            : labelKey.Translate().ToString();
    }

    public class CompProperties_MultiCrewTurret : CompProperties
    {
        public List<MultiCrewTurretRole> roles = new List<MultiCrewTurretRole>();

        public CompProperties_MultiCrewTurret()
        {
            compClass = typeof(CompMultiCrewTurret);
        }
    }

    /// <summary>
    /// Reusable role-based crew system for crew-served turrets. Each role has
    /// its own reservable interaction cell and Def-configured combat modifiers.
    /// </summary>
    public class CompMultiCrewTurret : ThingComp
    {
        public const string OperateJobDefName = "HD_OperateMultiCrewTurret";
        private static readonly IReadOnlyList<MultiCrewTurretRole> EmptyRoles =
            new List<MultiCrewTurretRole>();

        private CompProperties_MultiCrewTurret Props =>
            (CompProperties_MultiCrewTurret)props;

        public IReadOnlyList<MultiCrewTurretRole> Roles =>
            Props.roles != null ? (IReadOnlyList<MultiCrewTurretRole>)Props.roles : EmptyRoles;

        public bool HasAllRequiredRoles =>
            Roles.Where(role => role != null && role.required)
                .All(role => PawnAtRole(role) != null);

        public float AccuracyMultiplier =>
            ReadyRoles.Aggregate(1f, (value, role) =>
                value * Mathf.Max(0.01f, role.accuracyMultiplier)
                * ShootingSkillFactor(role));

        public float CooldownMultiplier =>
            ReadyRoles.Aggregate(1f, (value, role) =>
                value * Mathf.Max(0.01f, role.cooldownMultiplier));

        public float WarmupMultiplier =>
            ReadyRoles.Aggregate(1f, (value, role) =>
                value * Mathf.Max(0.01f, role.warmupMultiplier));

        private IEnumerable<MultiCrewTurretRole> ReadyRoles =>
            Roles.Where(role => role != null && PawnAtRole(role) != null);

        public override bool CompAllowVerbCast(Verb verb)
        {
            return base.CompAllowVerbCast(verb) && HasAllRequiredRoles;
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.CompFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            if (selPawn == null
                || selPawn.Faction != parent.Faction
                || selPawn.Map != parent.Map
                || selPawn.Dead
                || selPawn.Downed)
            {
                yield break;
            }

            for (int roleIndex = 0; roleIndex < Roles.Count; roleIndex++)
            {
                int capturedIndex = roleIndex;
                MultiCrewTurretRole role = Roles[capturedIndex];
                if (role == null)
                {
                    continue;
                }

                // CompMannable supplies the vanilla "man turret" float-menu
                // command and JobDriver_ManTurret for this role.
                if (role.usesVanillaManning)
                {
                    continue;
                }

                Pawn occupant = PawnAtRole(role);
                string label = "HD_MultiCrew_OperateAs".Translate(parent.LabelShort, role.Label);
                IntVec3 cell = InteractionCell(role);

                if (occupant != null && occupant != selPawn)
                {
                    yield return new FloatMenuOption(
                        label + ": " + "HD_MultiCrew_RoleOccupied".Translate(occupant.LabelShort),
                        null);
                    continue;
                }

                if (!cell.InBounds(parent.Map)
                    || !cell.Standable(parent.Map)
                    || !selPawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                {
                    yield return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
                    continue;
                }

                if (!selPawn.CanReserve(cell))
                {
                    yield return new FloatMenuOption(label + ": " + "Reserved".Translate(), null);
                    continue;
                }

                yield return new FloatMenuOption(label, delegate
                {
                    JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(OperateJobDefName);
                    if (jobDef == null)
                    {
                        Log.ErrorOnce(
                            "Helodrace: HD_OperateMultiCrewTurret JobDef is missing.",
                            10523001);
                        return;
                    }

                    Job job = JobMaker.MakeJob(jobDef, parent, cell);
                    job.count = capturedIndex;
                    selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                });
            }
        }

        public override string CompInspectStringExtra()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(HasAllRequiredRoles
                ? "HD_MultiCrew_Ready".Translate()
                : "HD_MultiCrew_MissingRequired".Translate());

            foreach (MultiCrewTurretRole role in Roles)
            {
                if (role == null)
                {
                    continue;
                }

                Pawn pawn = PawnAtRole(role);
                string requirement = role.required
                    ? "HD_MultiCrew_Required".Translate()
                    : "HD_MultiCrew_Optional".Translate();
                string occupant = pawn?.LabelShort ?? "HD_MultiCrew_Vacant".Translate();
                text.AppendLine("HD_MultiCrew_RoleStatus".Translate(
                    requirement,
                    role.Label,
                    occupant));
            }

            text.Append("HD_MultiCrew_Modifiers".Translate(
                AccuracyMultiplier.ToStringPercent(),
                CooldownMultiplier.ToStringPercent(),
                WarmupMultiplier.ToStringPercent()));
            return text.ToString();
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();

            if (parent.Map == null)
            {
                return;
            }

            foreach (MultiCrewTurretRole role in Roles)
            {
                if (role == null)
                {
                    continue;
                }

                IntVec3 cell = InteractionCell(role);
                if (!cell.InBounds(parent.Map))
                {
                    continue;
                }

                Pawn occupant = PawnAtRole(role);
                Color color = occupant != null
                    ? Color.green
                    : role.required
                        ? Color.red
                        : Color.yellow;

                GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, color);
            }
        }

        public IntVec3 InteractionCell(MultiCrewTurretRole role)
        {
            return role == null
                ? IntVec3.Invalid
                : parent.Position + role.interactionOffset.RotatedBy(parent.Rotation);
        }

        public MultiCrewTurretRole RoleAt(int roleIndex)
        {
            return roleIndex >= 0 && roleIndex < Roles.Count ? Roles[roleIndex] : null;
        }

        public bool PawnIsOperatingRole(Pawn pawn, int roleIndex)
        {
            MultiCrewTurretRole role = RoleAt(roleIndex);
            if (role?.usesVanillaManning == true)
            {
                Pawn manningPawn = parent.TryGetComp<CompMannable>()?.ManningPawn;
                return pawn != null
                    && pawn == manningPawn
                    && pawn.Spawned
                    && !pawn.Dead
                    && !pawn.Downed
                    && pawn.Map == parent.Map;
            }

            Job job = pawn?.CurJob;
            return role != null
                && pawn != null
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Map == parent.Map
                && job?.def?.defName == OperateJobDefName
                && job.GetTarget(TargetIndex.A).Thing == parent
                && job.count == roleIndex
                && pawn.Position == InteractionCell(role);
        }

        public Pawn PawnAtRole(MultiCrewTurretRole role)
        {
            if (role == null || parent.Map == null)
            {
                return null;
            }

            if (role.usesVanillaManning)
            {
                Pawn manningPawn = parent.TryGetComp<CompMannable>()?.ManningPawn;
                return PawnIsOperatingRole(manningPawn, Props.roles?.IndexOf(role) ?? -1)
                    ? manningPawn
                    : null;
            }

            int roleIndex = Props.roles?.IndexOf(role) ?? -1;
            if (roleIndex < 0)
            {
                return null;
            }

            IntVec3 cell = InteractionCell(role);
            Pawn pawn = cell.InBounds(parent.Map) ? cell.GetFirstPawn(parent.Map) : null;
            return PawnIsOperatingRole(pawn, roleIndex) ? pawn : null;
        }

        private float ShootingSkillFactor(MultiCrewTurretRole role)
        {
            if (!role.useShootingSkill)
            {
                return 1f;
            }

            Pawn pawn = PawnAtRole(role);
            int level = pawn?.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
            float skill = 0.75f + level * 0.025f;
            float condition = pawn == null
                ? 1f
                : Mathf.Clamp(
                    pawn.GetStatValue(StatDefOf.ShootingAccuracyPawn),
                    0.85f,
                    1.10f);
            return skill * condition;
        }
    }

    public class JobDriver_OperateMultiCrewTurret : JobDriver
    {
        private const TargetIndex TurretInd = TargetIndex.A;
        private const TargetIndex InteractionCellInd = TargetIndex.B;

        private Thing Turret => job.GetTarget(TurretInd).Thing;
        private IntVec3 InteractionCell => job.GetTarget(InteractionCellInd).Cell;
        private CompMultiCrewTurret Crew => Turret?.TryGetComp<CompMultiCrewTurret>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(InteractionCell, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TurretInd);
            this.FailOn(() => Crew?.RoleAt(job.count) == null);

            yield return Toils_Goto.GotoCell(InteractionCellInd, PathEndMode.OnCell);
            yield return new Toil
            {
                tickAction = delegate
                {
                    MultiCrewTurretRole role = Crew?.RoleAt(job.count);
                    if (role == null
                        || Turret.Map != pawn.Map
                        || InteractionCell != Crew.InteractionCell(role)
                        || pawn.Position != InteractionCell)
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        return;
                    }

                    pawn.rotationTracker.FaceTarget(Turret);
                },
                defaultCompleteMode = ToilCompleteMode.Never,
                handlingFacing = true
            };
        }
    }

    [HarmonyPatch(typeof(Building_TurretGun), "BurstCooldownTime")]
    public static class Patch_BuildingTurretGun_BurstCooldownTime_MultiCrew
    {
        public static void Postfix(Building_TurretGun __instance, ref float __result)
        {
            CompMultiCrewTurret crew = __instance?.TryGetComp<CompMultiCrewTurret>();
            if (crew != null)
            {
                __result *= crew.CooldownMultiplier;
            }
        }
    }

    [HarmonyPatch(typeof(Verb), "get_WarmupTime")]
    public static class Patch_Verb_WarmupTime_MultiCrew
    {
        public static void Postfix(Verb __instance, ref float __result)
        {
            if (__instance?.Caster is Building_TurretGun turret)
            {
                CompMultiCrewTurret crew = turret.TryGetComp<CompMultiCrewTurret>();
                if (crew != null)
                {
                    __result *= crew.WarmupMultiplier;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Building_TurretGun), "TryStartShootSomething")]
    public static class Patch_BuildingTurretGun_TryStartShootSomething_MultiCrew
    {
        public static bool Prefix(Building_TurretGun __instance)
        {
            CompMultiCrewTurret crew = __instance?.TryGetComp<CompMultiCrewTurret>();
            return crew == null || crew.HasAllRequiredRoles;
        }

        public static void Postfix(
            Building_TurretGun __instance,
            ref int ___burstWarmupTicksLeft)
        {
            CompMultiCrewTurret crew = __instance?.TryGetComp<CompMultiCrewTurret>();
            if (crew != null && ___burstWarmupTicksLeft > 0)
            {
                ___burstWarmupTicksLeft = Mathf.Max(
                    1,
                    Mathf.RoundToInt(___burstWarmupTicksLeft * crew.WarmupMultiplier));
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_ShotReport_MultiCrewAccuracy
    {
        private static readonly FieldInfo FactorFromShooterAndDistField =
            AccessTools.Field(typeof(ShotReport), "factorFromShooterAndDist");

        public static void Postfix(Thing caster, ref ShotReport __result)
        {
            CompMultiCrewTurret crew = caster?.TryGetComp<CompMultiCrewTurret>();
            if (crew == null || FactorFromShooterAndDistField == null)
            {
                return;
            }

            object boxedReport = __result;
            float current = (float)FactorFromShooterAndDistField.GetValue(boxedReport);
            FactorFromShooterAndDistField.SetValue(
                boxedReport,
                Mathf.Clamp(current * crew.AccuracyMultiplier, 0.01f, 2f));
            __result = (ShotReport)boxedReport;
        }
    }
}
