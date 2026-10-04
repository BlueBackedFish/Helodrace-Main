using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed class CompProperties_TacticalRadio : CompProperties
    {
        public string network = "ModernInfantry";
        public int range = 300;
        public CompProperties_TacticalRadio() { compClass = typeof(CompTacticalRadio); }
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (string.IsNullOrEmpty(network) || range < 1 || range > 600)
                yield return "Tactical radio requires a network and range between 1 and 600.";
        }
    }

    public sealed class CompTacticalRadio : ThingComp
    {
        public CompProperties_TacticalRadio RadioProperties => (CompProperties_TacticalRadio)props;
        public bool Operational => !parent.Destroyed && (!parent.def.useHitPoints || parent.HitPoints > 0)
            && parent.TryGetComp<CompBreakdownable>()?.BrokenDown != true;
    }

    public static class RaidTacticalRadioUtility
    {
        public static bool OperatorAvailable(Pawn pawn) => pawn?.Spawned == true && !pawn.Dead && !pawn.Downed
            && !pawn.InMentalState && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);

        public static IEnumerable<CompTacticalRadio> Radios(Pawn pawn)
        {
            if (!OperatorAvailable(pawn)) return Enumerable.Empty<CompTacticalRadio>();
            return (pawn.inventory?.innerContainer.Cast<Thing>() ?? Enumerable.Empty<Thing>())
                .Concat(pawn.apparel?.WornApparel.Cast<Thing>() ?? Enumerable.Empty<Thing>())
                .Select(thing => thing.TryGetComp<CompTacticalRadio>()).Where(radio => radio?.Operational == true);
        }
    }
}
