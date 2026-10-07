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
                else if ((tool as ThingWithComps)?.TryGetComp<CompPowerCutterBreach>() != null)
                {
                    ThingWithComps primary = pawn.equipment.Primary;
                    if (primary != null && !pawn.equipment.TryDropEquipment(primary, out _, pawn.Position, false))
                    { EndJobWith(JobCondition.Incompletable); return; }
                    if (tool.ParentHolder is Pawn_EquipmentTracker holder) holder.Remove((ThingWithComps)tool);
                    if (tool.Spawned) tool.DeSpawn();
                    pawn.equipment.AddEquipment((ThingWithComps)tool);
                }
                else
                {
                    // Death can scatter inventory onto the map. Unlike Wear,
                    // ThingOwner transfer does not pick up a spawned item.
                    if (tool.Spawned) tool.DeSpawn();
                    if (!pawn.inventory.innerContainer.TryAddOrTransfer(tool))
                    {
                        if (!tool.Spawned && tool.holdingOwner == null) GenPlace.TryPlaceThing(tool, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                        EndJobWith(JobCondition.Incompletable); return;
                    }
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

    public sealed class JobDriver_TacticalInstallCharge : JobDriver_InstallBreachCharge
    {
        protected override BreachInitiationMode Mode => (job.targetC.Thing as ThingWithComps)?.TryGetComp<CompBreachIgniter>()
            ?.Supports(BreachInitiationMode.ShockTube) == true ? BreachInitiationMode.ShockTube : BreachInitiationMode.TimeFuse;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            MapComponent_TacticalCommands owner = pawn.Map?.GetComponent<MapComponent_TacticalCommands>(); Job owned = job;
            AddFinishAction(condition => owner?.JobFinished(pawn, owned, condition)); job.canUseRangedWeapon = false;
            this.FailOn(() => pawn.Downed || pawn.InMentalState || !BreachExplosiveUtility.CanOperate(pawn));
            var gate = ToilMaker.MakeToil("TacticalChargeMovementBudget"); gate.defaultCompleteMode = ToilCompleteMode.Never;
            gate.initAction = () => pawn.pather.StopDead();
            gate.tickAction = () =>
            { if (Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.TryPath(GenTicks.TicksGame)) ReadyForNextToil(); };
            yield return gate;
            foreach (Toil toil in base.MakeNewToils()) yield return toil;
        }
    }

    public sealed class JobDriver_TacticalTriggerCharge : TacticalJobDriver
    {
        private bool triggered;
        private CompInstalledBreachCharge Charge => (job.targetA.Thing as ThingWithComps)?.TryGetComp<CompInstalledBreachCharge>();
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner(); job.canUseRangedWeapon = false;
            this.FailOn(() => !triggered && (Charge?.OperatorPawn != pawn || !Charge.CanTrigger(out _)));
            yield return Toils_General.Wait(Charge?.Props.triggerWorkTicks ?? 60, TargetIndex.A);
            yield return Toils_General.Do(() =>
            {
                triggered = pawn.Map.GetComponent<MapComponent_TacticalCommands>().TriggerCharge(pawn, job);
                if (!triggered) EndJobWith(JobCondition.Incompletable);
            });
            yield return Hold(job.targetA.Cell);
        }
        public override void ExposeData()
        { base.ExposeData(); Scribe_Values.Look(ref triggered, "tacticalChargeTriggered"); }
    }
}
