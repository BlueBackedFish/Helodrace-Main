using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed class JobDriver_TacticalContactGuard : TacticalJobDriver
    {
        private int nextShot;
        private readonly TacticalContactState sight = new TacticalContactState();
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner(); job.canUseRangedWeapon = false;
            yield return AdmitMovement();
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil guard = Hold(job.targetB.Cell);
            guard.tickAction = TickGuard;
            yield return guard;
        }
        private void TickGuard()
        {
                int tick = GenTicks.TicksGame;
                if (tick < nextShot) return;
                nextShot = tick + 30 + pawn.thingIDNumber % 7;
                pawn.rotationTracker.FaceCell(job.targetB.Cell);
                if (pawn.stances.FullBodyBusy || pawn.equipment?.Primary == null) return;
                // The last observed tile is the sole query. A moved/hidden
                // enemy is never recovered through a remembered Pawn reference.
                Pawn enemy = job.targetB.Cell.GetFirstPawn(pawn.Map);
                if (enemy == null || enemy.Dead || enemy.Downed || !enemy.HostileTo(pawn)) return;
                GameComponent_TacticalCommands scheduler = Current.Game.GetComponent<GameComponent_TacticalCommands>();
                if (scheduler == null || !scheduler.WorkBudget.TryObserve(tick)) return;
                if (!TacticalContactSight.CanSee(pawn.Map, pawn.Position, enemy, sight)) return;
                Verb verb = pawn.equipment.PrimaryEq.PrimaryVerb;
                if (verb != null && !verb.IsMeleeAttack && verb.Available() && verb.CanHitTarget(enemy))
                    verb.TryStartCastOn(enemy);
        }
    }
}
