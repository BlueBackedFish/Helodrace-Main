using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// Transparent armour clouds where it is hit (Docs/Ballistic_Protection_System.md 6.5, phase 11). The wearer sees
    /// worse while they wear that face protection; taking it off clears the hediff. [game]
    /// </summary>
    public static class VisorCrazingUtility
    {
        public static bool Transparent(string materialId) =>
            DefDatabase<ArmorMaterialDef>.GetNamedSilentFail(materialId)?.transparent == true;

        public static void Craze(Pawn pawn, Apparel host, bool perforated)
        {
            if (pawn?.health == null || host == null) return;
            HediffDef def = HediffDef.Named("HD_VisorCrazed");
            BodyPartRecord head = pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.def == BodyPartDefOf.Head);
            if (head == null) return;
            var hediff = pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.def == def) as HediffWithComps;
            if (hediff == null)
            {
                hediff = (HediffWithComps)HediffMaker.MakeHediff(def, pawn, head);
                hediff.Severity = 0.001f;
                pawn.health.AddHediff(hediff, head);
            }
            hediff.Severity = VisorCrazing.Grow(hediff.Severity, perforated);
            HediffComp_VisorCrazed comp = hediff.TryGetComp<HediffComp_VisorCrazed>();
            if (comp != null) comp.apparelId = host.thingIDNumber;
        }
    }

    public class HediffCompProperties_VisorCrazed : HediffCompProperties
    {
        public HediffCompProperties_VisorCrazed() { compClass = typeof(HediffComp_VisorCrazed); }
    }

    /// <summary>Removes the crazing once the pawn no longer wears the apparel whose visor took the hits.</summary>
    public class HediffComp_VisorCrazed : HediffComp
    {
        public int apparelId = -1;

        public override bool CompShouldRemove =>
            Pawn.apparel == null || !Pawn.apparel.WornApparel.Any(a => a.thingIDNumber == apparelId);

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref apparelId, "visorApparel", -1);
        }
    }
}
