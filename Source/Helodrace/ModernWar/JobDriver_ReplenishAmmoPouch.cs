using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public sealed class JobDriver_ReplenishAmmoPouch : JobDriver
    {
        private Thing ArmorThing => job.GetTarget(TargetIndex.A).Thing;
        private Thing Bench => job.GetTarget(TargetIndex.B).Thing;
        private Thing CarbonSteel => job.GetTarget(TargetIndex.C).Thing;
        private Thing PouchThing => job.targetQueueA?.FirstOrDefault().Thing;
        private CompModularArmor Armor => ArmorThing?.TryGetComp<CompModularArmor>();
        private CompAmmoPouch Pouch => PouchThing?.TryGetComp<CompAmmoPouch>();

        private int CurrentCapacity => AmmoPouchUtility.CapacityFor(pawn.equipment?.Primary);

        private bool PouchStillInstalled => Armor?.InstalledParts.Any(part =>
            part?.InstalledItem == PouchThing) == true;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!CompClothingWorkbench.CanWorkAt(pawn, Bench, Armor)
                || Pouch == null || !PouchStillInstalled
                || CurrentCapacity <= 0 || job.count <= 0
                || CarbonSteel?.def?.defName != "HD_CarbonSteel"
                || CarbonSteel.stackCount < job.count
                || !pawn.Reserve(Bench, job, 1, 1, null, errorOnFailed))
                return false;

            if (CarbonSteel.Spawned)
                return pawn.Reserve(CarbonSteel, job, 1, job.count, null, errorOnFailed);
            return pawn.inventory?.innerContainer?.Contains(CarbonSteel) == true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOn(() => Armor?.Wearer != pawn || !PouchStillInstalled
                || !CompClothingWorkbench.CanUse(Bench, Armor)
                || Bench.IsForbidden(pawn));
            Toil takeInventory = Toils_General.Do(() =>
            {
                Thing item = pawn.inventory?.innerContainer?.Take(CarbonSteel, job.count);
                if (item == null || !pawn.carryTracker.innerContainer.TryAdd(item, false))
                {
                    if (item != null)
                        pawn.inventory.innerContainer.TryAdd(item, false);
                    EndJobWith(JobCondition.Incompletable);
                }
            });
            Toil atBench = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
            yield return Toils_Jump.JumpIf(takeInventory,
                () => pawn.inventory?.innerContainer?.Contains(CarbonSteel) == true);
            yield return Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.C, false, true, false);
            yield return Toils_Jump.Jump(atBench);
            yield return takeInventory;
            yield return atBench;
            var work = Toils_General.Wait(120, TargetIndex.B);
            work.handlingFacing = true;
            work.WithProgressBarToilDelay(TargetIndex.B);
            yield return work;
            yield return Toils_General.Do(() =>
            {
                int capacity = CurrentCapacity;
                int neededSteel = Pouch?.SteelNeeded ?? 0;
                int suppliedSteel = System.Math.Min(neededSteel, job.count);
                Thing carried = pawn.carryTracker?.CarriedThing;
                if (!PouchStillInstalled || capacity <= 0 || suppliedSteel <= 0
                    || carried?.def?.defName != "HD_CarbonSteel"
                    || carried.stackCount < suppliedSteel)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                Thing consumed = carried.SplitOff(suppliedSteel);
                consumed.Destroy(DestroyMode.Vanish);
                Pouch.AddCarbonSteel(suppliedSteel);
                Thing leftover = pawn.carryTracker?.CarriedThing;
                if (leftover?.def?.defName == "HD_CarbonSteel")
                {
                    Thing returned = pawn.carryTracker.innerContainer.Take(
                        leftover, leftover.stackCount);
                    if (returned != null
                        && pawn.inventory?.innerContainer?.TryAdd(returned, false) != true)
                        GenPlace.TryPlaceThing(returned, pawn.Position, pawn.Map,
                            ThingPlaceMode.Near);
                }
                Messages.Message("HD_AmmoPouch_Replenished".Translate(), pawn,
                    MessageTypeDefOf.PositiveEvent, false);
            });
        }
    }
}
