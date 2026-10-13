using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Helodrace.Tactics
{
    public abstract class TacticalJobDriver : JobDriver
    {
        private int nextEntryShot, movementQuietUntil;
        public bool AtPost;
        public bool Crossed;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override void Notify_DamageTaken(DamageInfo dinfo)
        {
            base.Notify_DamageTaken(dinfo);
            if (dinfo.Instigator is Pawn attacker && dinfo.Def.harmsHealth)
            {
                pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.NotifyEntryDamage(pawn, job, attacker);
                pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.NotifyCloseAttack(pawn, job, attacker);
            }
        }
        protected void BindOwner()
        {
            MapComponent_TacticalCommands owner = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            Job owned = job;
            AddFinishAction(condition => owner?.JobFinished(pawn, owned, condition));
            this.FailOn(() => pawn.Downed || pawn.InMentalState);
        }
        protected Toil AdmitMovement()
        {
            MapComponent_TacticalCommands owner = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            var gate = ToilMaker.MakeToil("TacticalMovementBudget");
            gate.defaultCompleteMode = ToilCompleteMode.Never;
            gate.initAction = () => pawn.pather.StopDead();
            gate.tickAction = () =>
            {
                // A gathering job may still be waiting here when contact pauses
                // its squad. Do not start its old path before a guard replaces it.
                if (job.def.defName == "HD_NewTacticalPost" && owner?.StackMovementPaused(pawn, job) == true) return;
                GameComponent_TacticalCommands scheduler = Current.Game.GetComponent<GameComponent_TacticalCommands>();
                if (scheduler == null || scheduler.WorkBudget.TryPath(GenTicks.TicksGame)) ReadyForNextToil();
            };
            return gate;
        }
        protected Toil Hold(IntVec3 face, bool entryCombat = false)
        {
            var hold = ToilMaker.MakeToil("TacticalHold");
            hold.defaultCompleteMode = ToilCompleteMode.Never;
            hold.handlingFacing = true;
            hold.initAction = () =>
            {
                AtPost = true; pawn.pather.StopDead(); pawn.rotationTracker.FaceCell(face);
                pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.Wake(pawn);
            };
            // Only post jobs opt into stack fire. Observation/throw/charge
            // drivers also use Hold, but retain exclusive action ownership.
            if (entryCombat || job.def.defName == "HD_NewTacticalPost") hold.tickAction = () =>
            {
                int tick = GenTicks.TicksGame;
                if (tick < nextEntryShot) return;
                var owner = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
                bool checkedNow = (entryCombat ? owner?.TryEntryFire(pawn, job, tick) : owner?.TryStackFire(pawn, job, tick)) != false;
                nextEntryShot = tick + TacticalPostFirePolicy.CheckDelay(checkedNow, pawn.thingIDNumber);
                if (!pawn.stances.FullBodyBusy) pawn.rotationTracker.FaceCell(face);
            };
            return hold;
        }
        protected IEnumerable<Toil> CombatMove(TargetIndex destination)
        {
            var owner = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            Toil gate = AdmitMovement();
            Toil move = Toils_Goto.GotoCell(destination, PathEndMode.OnCell);
            Toil done = Toils_General.Do(() => { });
            Toil contact = ToilMaker.MakeToil("TacticalIndividualContact");
            contact.defaultCompleteMode = ToilCompleteMode.Never;
            contact.handlingFacing = true;
            contact.initAction = () => { AtPost = false; pawn.pather.StopDead(); };
            bool Check(int tick, out bool engaged)
            {
                engaged = false;
                if (tick < nextEntryShot) return false;
                bool checkedNow = owner == null || owner.TryMovementFire(pawn, job, tick, out engaged);
                nextEntryShot = tick + TacticalPostFirePolicy.CheckDelay(checkedNow, pawn.thingIDNumber);
                if (engaged) movementQuietUntil = tick + TacticalEntryCombatPolicy.QuietTicks;
                return checkedNow;
            }
            move.AddPreTickAction(() =>
            {
                if (Check(GenTicks.TicksGame, out bool engaged) && engaged) JumpToToil(contact);
            });
            contact.tickAction = () =>
            {
                int tick = GenTicks.TicksGame;
                if (TacticalExplosiveEvasionPolicy.CanReturn(tick, movementQuietUntil,
                    Check(tick, out bool engaged), engaged, pawn.stances.FullBodyBusy)) ReadyForNextToil();
            };
            // Jumping between toils preserves the owned job and gun warmup.
            // Followers have independent drivers and keep their original path.
            yield return gate;
            yield return move;
            yield return Toils_Jump.Jump(done);
            yield return contact;
            yield return Toils_Jump.Jump(gate);
            yield return done;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref AtPost, "tacticalAtPost");
            Scribe_Values.Look(ref Crossed, "tacticalCrossed");
            Scribe_Values.Look(ref movementQuietUntil, "movementQuietUntil");
        }
    }
    public sealed class JobDriver_TacticalPost : TacticalJobDriver
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner();
            job.canUseRangedWeapon = false;
            foreach (Toil toil in CombatMove(TargetIndex.A)) yield return toil;
            yield return Hold(job.targetB.Cell, job.count == 1);
        }
    }
    public sealed class JobDriver_TacticalIngress : TacticalJobDriver
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner();
            job.canUseRangedWeapon = false;
            // Unsecured entry stages outside first. Cleared transit skips that
            // leg but still physically crosses the chosen opening and inside
            // mouth, including rear security; it cannot take an old detour.
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
            if (job.targetQueueA.Count > 1)
            {
                // Clear the mouth into the unoccupied inner lane before turning
                // toward the wall posts. No hold between these movement legs.
                yield return AdmitMovement();
                yield return Toils_Goto.GotoCell(job.targetQueueA[1].Cell, PathEndMode.OnCell);
            }
            // Fire while moving toward an interior post only after clearing
            // the mouth/lane. A stopped shooter must not plug the entrance.
            foreach (Toil toil in CombatMove(TargetIndex.C)) yield return toil;
            yield return Hold(job.targetB.Cell, entryCombat: true);
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
