using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class Building_Radio : Building
    {
        public bool CanUseNow => Spawned && Faction == Faction.OfPlayer
            && this.TryGetComp<CompPowerTrader>()?.PowerOn == true
            && !Map.gameConditionManager.ElectricityDisabled(Map);

        public static bool IsFrequencyAvailable(Faction faction)
            => faction != null && !faction.IsPlayer && !faction.defeated
                && HamRadioUtility.IsHamFaction(faction);

        public static IEnumerable<Faction> Frequencies
            => Find.FactionManager.AllFactionsVisibleInViewOrder.Where(IsFrequencyAvailable);
    }

    // Listening positions surround the radio; the TV driver still owns the joy ticks.
    public sealed class JoyGiver_RadioListening : JoyGiver_InteractBuilding
    {
        protected override bool CanInteractWith(Pawn pawn, Thing thing, bool inBed)
            => thing is Building_Radio radio && radio.CanUseNow
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Hearing)
                && (!inBed || (RadioListeningUtility.InRange(radio, pawn.Position)
                    && pawn.GetRoom() == radio.GetRoom()))
                && base.CanInteractWith(pawn, thing, inBed);

        protected override Job TryGivePlayJob(Pawn pawn, Thing thing)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(thing.Position, thing.def.specialDisplayRadius, false)
                .Where(c => RadioListeningUtility.CanListenAt(thing, c) && !c.IsForbidden(pawn)
                    && pawn.CanReserveSittableOrSpot(c)
                    && pawn.Map.pawnDestinationReservationManager.CanReserve(c, pawn)
                    && pawn.CanReach(c, PathEndMode.OnCell, Danger.None))
                .OrderByDescending(c => c.GetEdifice(pawn.Map)?.def.building.isSittable == true)
                .ThenBy(c => (c - pawn.Position).LengthHorizontalSquared))
            {
                Building seat = cell.GetEdifice(pawn.Map);
                if (seat?.def.building.isSittable == true && !pawn.CanReserve(seat)) continue;
                return JobMaker.MakeJob(def.jobDef, thing, cell, seat);
            }
            return null;
        }
    }

    public sealed class JoyGiver_RadioChat : JoyGiver_InteractBuildingInteractionCell
    {
        protected override bool CanInteractWith(Pawn pawn, Thing thing, bool inBed)
            => thing is Building_Radio radio && radio.CanUseNow && Building_Radio.Frequencies.Any()
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Hearing)
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                && base.CanInteractWith(pawn, thing, inBed);

        protected override Job TryGivePlayJob(Pawn pawn, Thing thing)
        {
            Job job = base.TryGivePlayJob(pawn, thing);
            if (job == null || !Building_Radio.Frequencies.TryRandomElement(out Faction frequency)) return null;
            job.commTarget = frequency;
            return job;
        }
    }
}
