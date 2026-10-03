using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Helodrace
{
    public sealed class SledgehammerProtectedDoorExtension : DefModExtension { }

    public sealed class CompProperties_SledgehammerBreach : CompProperties
    {
        public int hitIntervalTicks = 75;
        public float hitDamage = 35f;
        public string gizmoIconPath = "Skill/HD_BreachSledgeHammer";

        public CompProperties_SledgehammerBreach()
        {
            compClass = typeof(CompSledgehammerBreach);
        }
    }

    public sealed class CompSledgehammerBreach : ThingComp
    {
        public const string JobDefName = "HD_SledgehammerBreach";

        public CompProperties_SledgehammerBreach Props =>
            (CompProperties_SledgehammerBreach)props;
        public Pawn Wearer => (parent.ParentHolder as Pawn_ApparelTracker)?.pawn;

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra()) yield return gizmo;
            Pawn wearer = Wearer;
            if (wearer?.Faction != Faction.OfPlayer) yield break;

            Command_Action command = new Command_Action
            {
                defaultLabel = "HD_Sledgehammer_Breach_Label".Translate(),
                defaultDesc = "HD_Sledgehammer_Breach_Desc".Translate(),
                icon = ContentFinder<Texture2D>.Get(Props.gizmoIconPath, false)
                    ?? BaseContent.BadTex,
                action = BeginTargeting
            };
            if (!CanOperate(wearer))
                command.Disable("HD_Sledgehammer_Breach_Unavailable".Translate());
            yield return command;
        }

        public static CompSledgehammerBreach WornBy(Pawn pawn)
        {
            return pawn?.apparel?.WornApparel.Select(apparel =>
                apparel.TryGetComp<CompSledgehammerBreach>())
                .FirstOrDefault(comp => comp != null);
        }

        public static bool CanOperate(Pawn pawn)
        {
            return pawn?.Spawned == true && !pawn.Dead && !pawn.Downed
                && pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) == true;
        }

        public static bool IsValidTarget(Pawn worker, Building target)
        {
            if (!CanOperate(worker) || target == null || !target.Spawned
                || target.Destroyed || !target.def.destroyable
                || !target.def.useHitPoints) return false;
            if (target is Building_Door door)
                return !door.Open && door.Faction != null && worker.Faction != null
                    && door.Faction != worker.Faction
                    && !door.PawnCanOpen(worker) && !IsProtectedDoor(door.def);
            return target.def.IsWall
                && target.def.passability == Traversability.Impassable;
        }

        private static bool IsProtectedDoor(ThingDef def)
        {
            if (def.GetModExtension<SledgehammerProtectedDoorExtension>() != null)
                return true;
            string name = def.defName ?? string.Empty;
            return new[] { "Security", "Vault", "Blast", "Armored", "Armoured",
                "Reinforced" }.Any(part => name.IndexOf(part,
                    StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static bool TryFindInteractionCell(Pawn worker, Building target,
            out IntVec3 interactionCell)
        {
            if (worker?.Map == null || target == null)
            {
                interactionCell = IntVec3.Invalid;
                return false;
            }
            foreach (IntVec3 cell in GenAdj.CellsAdjacentCardinal(target)
                .OrderBy(candidate => candidate.DistanceToSquared(worker.Position)))
                if (cell.InBounds(worker.Map) && cell.Standable(worker.Map)
                    && !cell.IsForbidden(worker) && worker.CanReserveAndReach(cell,
                        PathEndMode.OnCell, Danger.Deadly))
                {
                    interactionCell = cell;
                    return true;
                }
            interactionCell = IntVec3.Invalid;
            return false;
        }

        private void BeginTargeting()
        {
            Pawn wearer = Wearer;
            if (!CanOperate(wearer)) return;
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = false,
                canTargetBuildings = true,
                canTargetItems = false,
                canTargetPawns = false,
                validator = target => IsValidTarget(wearer, target.Thing as Building)
            }, target => TryStartBreach(wearer, target.Thing as Building));
        }

        private void TryStartBreach(Pawn wearer, Building target)
        {
            if (!IsValidTarget(wearer, target))
            {
                Messages.Message("HD_Sledgehammer_Breach_InvalidTarget".Translate(),
                    target, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!TryFindInteractionCell(wearer, target, out IntVec3 cell))
            {
                Messages.Message("HD_Sledgehammer_Breach_CannotReach".Translate(),
                    target, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!wearer.CanReserve(target, 1, -1, null, false))
            {
                Messages.Message("HD_Sledgehammer_Breach_CannotReserve".Translate(),
                    target, MessageTypeDefOf.RejectInput, false);
                return;
            }
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(JobDefName);
            if (def == null) return;
            wearer.jobs.TryTakeOrderedJob(JobMaker.MakeJob(def, target, cell, parent),
                JobTag.Misc);
        }
    }

    public sealed class JobDriver_SledgehammerBreach : JobDriver
    {
        private Building Target => job.GetTarget(TargetIndex.A).Thing as Building;
        private IntVec3 WorkCell => job.GetTarget(TargetIndex.B).Cell;
        private CompSledgehammerBreach Tool =>
            (job.GetTarget(TargetIndex.C).Thing as ThingWithComps)
                ?.TryGetComp<CompSledgehammerBreach>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Target, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(WorkCell, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Tool?.Wearer != pawn
                || !CompSledgehammerBreach.IsValidTarget(pawn, Target));
            this.FailOn(() => !WorkCell.IsValid || !WorkCell.InBounds(pawn.Map)
                || !WorkCell.Standable(pawn.Map));
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil strike = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            strike.handlingFacing = true;
            strike.tickAction = () =>
            {
                Building barrier = Target;
                if (barrier == null || pawn.Position != WorkCell) return;
                pawn.rotationTracker.FaceTarget(barrier);
                int interval = Mathf.Max(1, Tool?.Props.hitIntervalTicks ?? 75);
                if (Find.TickManager.TicksGame % interval != 0) return;
                SoundDefOf.Pawn_Melee_Punch_HitBuilding_Generic.PlayOneShot(
                    new TargetInfo(barrier.Position, pawn.Map));
                barrier.TakeDamage(new DamageInfo(DamageDefOf.Blunt,
                    Mathf.Max(1f, Tool?.Props.hitDamage ?? 35f), 0f, -1f, pawn));
                if (barrier.Destroyed || !barrier.Spawned)
                    EndJobWith(JobCondition.Succeeded);
            };
            strike.FailOn(() => pawn.Position != WorkCell);
            yield return strike;
        }
    }
}
