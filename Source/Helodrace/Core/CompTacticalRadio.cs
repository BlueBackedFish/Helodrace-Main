using System.Collections.Generic;
using System.Linq;
using Helodrace.ModernWar;
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
            return InstalledRadios(pawn.apparel?.WornApparel ?? Enumerable.Empty<Apparel>());
        }

        internal static IEnumerable<CompTacticalRadio> InstalledRadios(IEnumerable<Apparel> worn) => worn
            .Where(armor => !armor.Destroyed && (!armor.def.useHitPoints || armor.HitPoints > 0))
            .Select(armor => armor.TryGetComp<CompModularArmor>()).Where(armor => armor != null)
            .SelectMany(armor => armor.InstalledParts)
            .Select(part => part?.InstalledItem?.TryGetComp<CompTacticalRadio>())
            .Where(radio => radio?.Operational == true);
    }
}
