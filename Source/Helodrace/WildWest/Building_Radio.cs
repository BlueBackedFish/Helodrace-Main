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

        public static bool IsChannelAvailable(Faction faction)
            => faction != null && !faction.IsPlayer && !faction.defeated
                && HamRadioUtility.IsHamFaction(faction);

        public static IEnumerable<Faction> Channels
            => Find.FactionManager.AllFactionsVisibleInViewOrder.Where(IsChannelAvailable);
    }

    // Reuse television viewing positions, shared reservations, beds and joy ticks.
    public sealed class JoyGiver_RadioListening : JoyGiver_WatchBuilding
    {
        protected override bool CanInteractWith(Pawn pawn, Thing thing, bool inBed)
            => thing is Building_Radio radio && radio.CanUseNow
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Hearing)
                && base.CanInteractWith(pawn, thing, inBed);
    }

    public sealed class JoyGiver_RadioChat : JoyGiver_InteractBuildingInteractionCell
    {
        protected override bool CanInteractWith(Pawn pawn, Thing thing, bool inBed)
            => thing is Building_Radio radio && radio.CanUseNow && Building_Radio.Channels.Any()
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Hearing)
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                && base.CanInteractWith(pawn, thing, inBed);

        protected override Job TryGivePlayJob(Pawn pawn, Thing thing)
        {
            Job job = base.TryGivePlayJob(pawn, thing);
            if (job == null || !Building_Radio.Channels.TryRandomElement(out Faction channel)) return null;
            job.commTarget = channel;
            return job;
        }
    }
}
