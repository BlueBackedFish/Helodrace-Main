using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Squads;
using Helodrace.Tactical;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public static class RaidGrenadePreparation
    {
        public const string JobDefName = "HD_RaidPrepareGrenade";
        public const int PreparationTicks = 30;
        public const int ReleaseTicks = 18;

        internal static bool IsThrowJob(Pawn pawn) => pawn?.CurJobDef?.defName == JobDefName
            || pawn?.CurJobDef?.defName == "HD_ThrowInventoryGrenadeClose"
            || pawn?.CurJobDef?.defName == "HD_ThrowInventoryGrenadeNormal";

        internal static JobDriver_RaidPrepareGrenade Active(Pawn pawn) => pawn?.CurJobDef?.defName == JobDefName
            && pawn.jobs.curDriver is JobDriver_RaidPrepareGrenade driver && !driver.ended
            && driver.job == pawn.CurJob && !driver.Released ? driver : null;

        internal static bool BlocksGun(Verb verb) => (Active(verb?.CasterPawn) != null
                || RaidEntryObservation.Active(verb?.CasterPawn) != null)
            && verb.EquipmentSource != null && !verb.IsMeleeAttack;

        internal static bool Start(Pawn pawn, Thing grenade, IntVec3 target, IntVec3 position, bool close)
        {
            if (pawn?.Spawned != true || pawn.Faction == RimWorld.Faction.OfPlayer
                || !InventoryGrenadeUtility.CanUseGrenades(pawn) || pawn.inventory?.Contains(grenade) != true
                || MapComponent_RaidTacticalOrders.Protected(pawn)
                || pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>()?.ControlsPawn(pawn) != true) return false;
            JobDef definition = DefDatabase<JobDef>.GetNamedSilentFail(JobDefName);
            if (definition == null || !position.InBounds(pawn.Map) || !position.Standable(pawn.Map)
                || !pawn.CanReserveAndReach(position, PathEndMode.OnCell, Danger.Deadly)) return false;
            Job job = JobMaker.MakeJob(definition, target, grenade, position);
            job.count = close ? 1 : 0;
            job.canUseRangedWeapon = false;
            job.locomotionUrgency = LocomotionUrgency.Jog;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return pawn.CurJob == job;
        }
    }

    public sealed class JobDriver_RaidPrepareGrenade : JobDriver
    {
        public bool Prepared;
        public bool Released;
        private string organizationId;
        private int planTick = -1;
        private bool ownerCaptured;
        public Thing Grenade => job.targetB.Thing;
        private bool CloseThrow => job.count == 1;
        private float Range => CloseThrow ? InventoryGrenadeUtility.CloseThrowRange : InventoryGrenadeUtility.NormalThrowRange;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Prepared, "prepared");
            Scribe_Values.Look(ref Released, "released");
            Scribe_Values.Look(ref organizationId, "organizationId");
            Scribe_Values.Look(ref planTick, "planTick", -1);
            Scribe_Values.Look(ref ownerCaptured, "ownerCaptured");
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetC, job, 1, -1, null, errorOnFailed);

        private bool OwnerStillValid()
        {
            if (!ownerCaptured) return true; // Initial toil captures the owner.
            var state = pawn.Map?.GetComponent<MapComponent_RaidTacticalExecution>()?.StateFor(organizationId);
            return state?.ActivePlan?.PlannedTick == planTick && (state.Phase == RaidExecutionPhase.Support
                && (state.Thrower == pawn || !state.SupportIssued)
                || state.Phase == RaidExecutionPhase.SecureRoom || state.ApproachSmokeActive && state.ApproachSmokeThrower == pawn);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Faction == RimWorld.Faction.OfPlayer || pawn.Dead || pawn.Downed
                || !InventoryGrenadeUtility.CanUseGrenades(pawn) || Grenade == null || Grenade.Destroyed
                || pawn.inventory?.Contains(Grenade) != true || !OwnerStillValid());
            this.FailOn(() => !job.targetA.Cell.InBounds(pawn.Map) || !job.targetC.Cell.InBounds(pawn.Map)
                || !job.targetC.Cell.Standable(pawn.Map));

            Toil prepare = Toils_General.Wait(RaidGrenadePreparation.PreparationTicks);
            prepare.initAction += () => {
                organizationId = OrganizationAPI.GetOrganization(pawn)?.id;
                var state = pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>()?.StateFor(organizationId);
                planTick = state?.ActivePlan?.PlannedTick ?? -1;
                ownerCaptured = true;
                TacticalAimUtility.Cancel(pawn);
                foreach (Verb verb in pawn.equipment?.AllEquipmentVerbs ?? new List<Verb>()) verb.Reset();
                pawn.stances.CancelBusyStanceHard();
                MapComponent_RaidTacticalTrace.Record(pawn, "AI grenade: preparing; gun hidden and unavailable");
            };
            prepare.WithProgressBarToilDelay(TargetIndex.B);
            yield return prepare;
            yield return new Toil { initAction = () => Prepared = true, defaultCompleteMode = ToilCompleteMode.Instant };
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            Toil release = Toils_General.Wait(RaidGrenadePreparation.ReleaseTicks, TargetIndex.A);
            release.FailOn(() => !InventoryGrenadeUtility.CanThrowAt(pawn, job.targetA.Cell, Range)
                || !MapComponent_RaidTacticalExecution.SafeSupportThrow(pawn, Grenade, job.targetA.Cell, CloseThrow));
            release.WithProgressBarToilDelay(TargetIndex.A);
            yield return release;
            yield return new Toil {
                initAction = () => {
                    InventoryGrenadeUtility.Launch(pawn, Grenade, job.targetA, CloseThrow);
                    Released = true;
                }, defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] {
        typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_RaidGrenade_NoGunCast
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            if (!RaidGrenadePreparation.BlocksGun(__instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.Available))]
    public static class Patch_RaidGrenade_NoGunAvailable
    {
        public static void Postfix(Verb __instance, ref bool __result)
        {
            if (RaidGrenadePreparation.BlocksGun(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    public static class Patch_RaidGrenade_DrawHeld
    {
        public static void Postfix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            JobDriver_RaidPrepareGrenade preparation = RaidGrenadePreparation.Active(pawn);
            Thing grenade = preparation?.Prepared == true ? preparation.Grenade : null;
            if (grenade?.Graphic == null || flags.HasFlag(PawnRenderFlags.Portrait) || pawn.Dead || pawn.Downed) return;
            Vector3 hand = drawPos + new Vector3(0.23f, 0.045f, 0.25f).RotatedBy(facing.AsAngle);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(hand, facing.AsQuat, new Vector3(0.42f, 1f, 0.42f)),
                grenade.Graphic.MatAt(facing, grenade), 0);
        }
    }
}
