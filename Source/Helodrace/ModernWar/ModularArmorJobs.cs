using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public sealed class JobDriver_InstallModularArmorPart : JobDriver
    {
        private const TargetIndex PartItemInd = TargetIndex.A;
        private const TargetIndex ArmorInd = TargetIndex.B;
        private const TargetIndex InstallDataInd = TargetIndex.C;

        private Thing PartItem => job.GetTarget(PartItemInd).Thing;
        private ThingWithComps Armor => job.GetTarget(ArmorInd).Thing as ThingWithComps;
        private LocalTargetInfo InstallData => job.GetTarget(InstallDataInd);
        private CompModularArmor ArmorComp => Armor?.TryGetComp<CompModularArmor>();
        private Thing Workbench => job.targetQueueB?.FirstOrDefault().Thing;
        private ModularArmorPartDef Part => ArmorComp?.PartForInstallJob(InstallData);

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            job.count = 1;
            if (!CompClothingWorkbench.CanWorkAt(pawn, Workbench, ArmorComp)
                || !pawn.Reserve(Workbench, job, 1, 1, null, errorOnFailed)) return false;
            if (PartItem?.Spawned == true)
            {
                return pawn.Reserve(PartItem, job, 1, 1, null, errorOnFailed);
            }

            return pawn.inventory?.innerContainer?.Contains(PartItem) == true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(PartItemInd);
            this.FailOn(() => ArmorComp?.Wearer != pawn);
            this.FailOn(() => !CompClothingWorkbench.CanUse(Workbench, ArmorComp)
                || Workbench.IsForbidden(pawn));
            this.FailOn(() => Part == null || PartItem?.def != Part.RequiredThingDef);

            Toil takeFromInventory = Toils_General.Do(delegate
            {
                Thing item = PartItem;
                ThingOwner<Thing> inventory = pawn.inventory?.innerContainer;
                if (item == null || inventory?.Contains(item) != true)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Thing taken = inventory.Take(item, 1);
                if (taken == null || !pawn.carryTracker.innerContainer.TryAdd(taken, false))
                {
                    if (taken != null)
                    {
                        inventory.TryAdd(taken, false);
                    }
                    EndJobWith(JobCondition.Incompletable);
                }
            });

            int workTicks = ArmorComp.InstallWorkTicks(InstallData);
            Toil install = Toils_General.Wait(workTicks, ArmorInd);
            install.handlingFacing = true;
            install.WithProgressBarToilDelay(ArmorInd);
            Toil goToWorkbench = Toils_Goto.GotoCell(Workbench?.InteractionCell ?? IntVec3.Invalid, PathEndMode.OnCell);
            Toil faceWorkbench = Toils_General.Do(() => pawn.rotationTracker.FaceTarget(Workbench));

            yield return Toils_Jump.JumpIf(
                takeFromInventory,
                () => pawn.inventory?.innerContainer?.Contains(PartItem) == true);
            yield return Toils_Goto.GotoThing(PartItemInd, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(PartItemInd, false, true, false);
            yield return Toils_Jump.Jump(goToWorkbench);
            yield return takeFromInventory;
            yield return goToWorkbench;
            yield return faceWorkbench;
            yield return install;
            yield return Toils_General.Do(FinishInstallation);
        }

        private void FinishInstallation()
        {
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried == null || carried.def != Part?.RequiredThingDef)
            {
                return;
            }

            Thing supplied = pawn.carryTracker.innerContainer.Take(carried, 1);
            if (supplied == null)
            {
                return;
            }

            if (ArmorComp?.CompleteInstallJob(supplied, InstallData) == true)
            {
                Messages.Message(
                    "HD_ModularArmor_InstallComplete".Translate(Part.LabelCap),
                    pawn,
                    MessageTypeDefOf.PositiveEvent,
                    false);
                return;
            }

            ReturnToPawn(supplied);
            Messages.Message(
                "HD_ModularArmor_InstallFailed".Translate(),
                pawn,
                MessageTypeDefOf.RejectInput,
                false);
        }

        private void ReturnToPawn(Thing item)
        {
            if (item == null || item.Destroyed)
            {
                return;
            }

            if (pawn.inventory?.innerContainer?.TryAdd(item, false) == true)
            {
                return;
            }

            if (pawn.Spawned)
            {
                GenPlace.TryPlaceThing(
                    item,
                    pawn.Position,
                    pawn.Map,
                    ThingPlaceMode.Near);
            }
        }
    }

    public sealed class JobDriver_SwapModularArmorFrontRear : JobDriver
    {
        private const TargetIndex ArmorInd = TargetIndex.A;
        private const TargetIndex BenchInd = TargetIndex.B;
        private const int SwapWorkTicks = 300;

        private ThingWithComps Armor => job.GetTarget(ArmorInd).Thing as ThingWithComps;
        private CompModularArmor ArmorComp => Armor?.TryGetComp<CompModularArmor>();
        private Thing Workbench => job.GetTarget(BenchInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return CompClothingWorkbench.CanWorkAt(pawn, Workbench, ArmorComp)
                && pawn.Reserve(Workbench, job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => ArmorComp?.Wearer != pawn);
            this.FailOnDespawnedNullOrForbidden(BenchInd);
            this.FailOn(() => !CompClothingWorkbench.CanUse(Workbench, ArmorComp));
            this.FailOn(() => !ArmorComp.InstalledParts
                .Any(record => record?.CanSwapFrontRearPlates == true));

            Toil swap = Toils_General.Wait(SwapWorkTicks, ArmorInd);
            swap.handlingFacing = true;
            swap.WithProgressBarToilDelay(ArmorInd);
            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);
            yield return Toils_General.Do(() => pawn.rotationTracker.FaceTarget(Workbench));
            yield return swap;
            yield return Toils_General.Do(delegate
            {
                if (ArmorComp?.CompleteFrontRearSwap() == true)
                {
                    Messages.Message(
                        "HD_ModularArmor_SwapComplete".Translate(),
                        pawn,
                        MessageTypeDefOf.PositiveEvent,
                        false);
                }
            });
        }
    }
}
