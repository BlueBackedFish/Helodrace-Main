using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Helodrace.Tactics
{
    public abstract class TacticalJobDriver : JobDriver
    {
        public bool AtPost;
        public bool Crossed;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected void BindOwner()
        {
            MapComponent_TacticalCommands owner = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            Job owned = job;
            AddFinishAction(condition => owner?.JobFinished(pawn, owned, condition));
            this.FailOn(() => pawn.Downed || pawn.InMentalState);
        }
        protected Toil AdmitMovement()
        {
            var gate = ToilMaker.MakeToil("TacticalMovementBudget");
            gate.defaultCompleteMode = ToilCompleteMode.Never;
            gate.initAction = () => pawn.pather.StopDead();
            gate.tickAction = () =>
            {
                GameComponent_TacticalCommands scheduler = Current.Game.GetComponent<GameComponent_TacticalCommands>();
                if (scheduler == null || scheduler.WorkBudget.TryPath(GenTicks.TicksGame)) ReadyForNextToil();
            };
            return gate;
        }
        protected Toil Hold(IntVec3 face)
        {
            var hold = ToilMaker.MakeToil("TacticalHold");
            hold.defaultCompleteMode = ToilCompleteMode.Never;
            hold.handlingFacing = true;
            hold.initAction = () =>
            {
                AtPost = true; pawn.pather.StopDead(); pawn.rotationTracker.FaceCell(face);
                pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.Wake(pawn);
            };
            // No per-pawn per-tick tactical lookup or correction callback.
            return hold;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref AtPost, "tacticalAtPost");
            Scribe_Values.Look(ref Crossed, "tacticalCrossed");
        }
    }
    public sealed class JobDriver_TacticalPost : TacticalJobDriver
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner();
            yield return AdmitMovement();
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Hold(job.targetB.Cell);
        }
    }
    public sealed class JobDriver_TacticalIngress : TacticalJobDriver
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner();
            // Mandatory outside -> actual opening -> inside, then ONE final
            // near-wall destination. Every pawn including rear security uses it.
            Crossed |= job.count >= 2;
            if (job.count < 1)
            {
                yield return AdmitMovement();
                yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            }
            if (job.count < 2)
            {
                yield return AdmitMovement();
                yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
                yield return Toils_General.Do(() =>
                {
                    pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.PassedOpening(pawn, job, pawn.Position);
                });
                yield return AdmitMovement();
                yield return Toils_Goto.GotoCell(job.targetQueueA[0].Cell, PathEndMode.OnCell);
                yield return Toils_General.Do(() =>
                {
                    Crossed = true;
                    pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.CrossedInside(pawn, job);
                });
            }
            yield return AdmitMovement();
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            yield return Hold(job.targetB.Cell);
        }
    }
    public sealed class JobDriver_TacticalBreach : TacticalJobDriver
    {
        private Building Barrier => job.targetA.Thing as Building;
        private CompSledgehammerBreach Tool => (job.targetC.Thing as ThingWithComps)?.TryGetComp<CompSledgehammerBreach>();
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner();
            this.FailOn(() => Tool?.Wearer != pawn || !CompSledgehammerBreach.CanOperate(pawn));
            yield return AdmitMovement();
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil wait = Toils_General.Wait(Barrier is Building_Door ? Tool.Props.doorWorkTicks : Tool.Props.hitIntervalTicks);
            wait.handlingFacing = true;
            wait.initAction = () => pawn.rotationTracker.FaceTarget(job.targetA);
            wait.AddEndCondition(() => Barrier == null || Barrier.Destroyed || !Barrier.Spawned
                ? JobCondition.Succeeded : JobCondition.Ongoing);
            yield return wait;
            yield return Toils_General.Do(() =>
            {
                Building target = Barrier;
                if (target == null || !target.Spawned || target.Destroyed) { EndJobWith(JobCondition.Succeeded); return; }
                if (pawn.Position != job.targetB.Cell || !CompSledgehammerBreach.IsValidTarget(pawn, target))
                { EndJobWith(JobCondition.Incompletable); return; }
                SoundDefOf.Pawn_Melee_Punch_HitBuilding_Generic.PlayOneShot(new TargetInfo(target.Position, pawn.Map));
                if (target is Building_Door door)
                {
                    bool jammed = door.GetComp<CompDoorBreachFault>()?.Jam(pawn, (int)Tool.Props.hitDamage) == true;
                    EndJobWith(jammed ? JobCondition.Succeeded : JobCondition.Incompletable);
                }
                else
                {
                    target.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Tool.Props.hitDamage, 0f, -1f, pawn));
                    if (target.Destroyed || !target.Spawned) EndJobWith(JobCondition.Succeeded);
                }
            });
            yield return Toils_Jump.Jump(wait);
        }
    }
}
