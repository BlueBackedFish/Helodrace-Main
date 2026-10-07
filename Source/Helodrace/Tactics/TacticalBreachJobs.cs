using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed class JobDriver_TacticalRecoverTool : TacticalJobDriver
    {
        private bool recovered;
        private Thing Tool => job.targetA.Thing;
        private Thing Source => job.targetB.Thing;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(Source, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner();
            this.FailOn(() => !recovered && (Source?.Spawned != true
                || TacticalBreachTools.SourceFor(Tool) != Source || !TacticalBreachTools.CanRecover(pawn, Tool)));
            yield return AdmitMovement();
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);
            yield return Toils_General.Wait(60, TargetIndex.B);
            yield return Toils_General.Do(() =>
            {
                Thing tool = Tool;
                if (tool is Apparel apparel)
                {
                    if (apparel.ParentHolder is Pawn_ApparelTracker holder) holder.Remove(apparel);
                    pawn.apparel.Wear(apparel, true);
                    if (CompSledgehammerBreach.WornBy(pawn)?.parent != apparel)
                    { EndJobWith(JobCondition.Incompletable); return; }
                }
                else
                {
                    ThingWithComps primary = pawn.equipment.Primary;
                    if (primary != null && !pawn.equipment.TryDropEquipment(primary, out _, pawn.Position, false))
                    { EndJobWith(JobCondition.Incompletable); return; }
                    if (tool.ParentHolder is Pawn_EquipmentTracker holder) holder.Remove((ThingWithComps)tool);
                    if (tool.Spawned) tool.DeSpawn();
                    pawn.equipment.AddEquipment((ThingWithComps)tool);
                }
                recovered = true;
                pawn.Map.GetComponent<MapComponent_TacticalCommands>().ToolRecovered(pawn, tool);
            });
            yield return Hold(job.targetB.Cell);
        }
        public override void ExposeData()
        { base.ExposeData(); Scribe_Values.Look(ref recovered, "tacticalToolRecovered"); }
    }

    public sealed class JobDriver_TacticalCut : JobDriver_PowerCutterBreach
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            MapComponent_TacticalCommands owner = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            Job owned = job;
            AddFinishAction(condition => owner?.JobFinished(pawn, owned, condition));
            this.FailOn(() => pawn.Downed || pawn.InMentalState || !BreachExplosiveUtility.CanOperate(pawn));
            var gate = ToilMaker.MakeToil("TacticalCutterMovementBudget");
            gate.defaultCompleteMode = ToilCompleteMode.Never;
            gate.initAction = () => pawn.pather.StopDead();
            gate.tickAction = () =>
            {
                GameComponent_TacticalCommands scheduler = Current.Game.GetComponent<GameComponent_TacticalCommands>();
                if (scheduler == null || scheduler.WorkBudget.TryPath(GenTicks.TicksGame)) ReadyForNextToil();
            };
            yield return gate;
            foreach (Toil toil in base.MakeNewToils()) yield return toil;
        }
    }
}
