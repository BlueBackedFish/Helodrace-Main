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
                var owner = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
                if (owner?.OpeningOwnsShot(pawn, job.targetB.Cell) == true)
                {
                    pawn.stances.CancelBusyStanceHard();
                    if (pawn.equipment != null) foreach (Verb equipmentVerb in pawn.equipment.AllEquipmentVerbs) equipmentVerb.Reset();
                    return;
                }
                if (pawn.stances.FullBodyBusy) return;
                // The last observed tile is the sole query. A moved/hidden
                // enemy is never recovered through a remembered Pawn reference.
                Pawn enemy = job.targetB.Cell.GetFirstPawn(pawn.Map);
                if (enemy == null || enemy.Dead || enemy.Downed || !enemy.HostileTo(pawn)) return;
                GameComponent_TacticalCommands scheduler = Current.Game.GetComponent<GameComponent_TacticalCommands>();
                if (scheduler == null || !scheduler.WorkBudget.TryObserve(tick)) return;
                if (!TacticalContactSight.CanSee(pawn.Map, pawn.Position, enemy, sight,
                    pawn.Position.Roofed(pawn.Map) ? TacticalContactState.Radius : TacticalFieldPolicy.SightRadius)) return;
                owner?.ObserveGuardContact(pawn, job, enemy, tick);
                if (owner?.OpeningOwnsShot(pawn, enemy.Position) == true) return;
                // A real close-attack emergency may name a melee-only enemy.
                // Native melee owns reachability, cooldown and attack selection;
                // do not wait for a gun that cannot fire at an adjacent target.
                if (pawn.Position.DistanceToSquared(enemy.Position) <= 2 && pawn.meleeVerbs.TryMeleeAttack(enemy)) return;
                if (pawn.equipment?.Primary == null) return;
                Verb verb = pawn.equipment.PrimaryEq.PrimaryVerb;
                if (verb != null && !verb.IsMeleeAttack && verb.Available() && verb.CanHitTarget(enemy))
                    if (verb.TryStartCastOn(enemy)) pawn.Map.GetComponent<MapComponent_TacticalCommands>()?.ContactShot(pawn);
        }
    }
}
