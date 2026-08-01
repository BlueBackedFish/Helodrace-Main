using RimWorld;
using Verse;

namespace Helodrace
{
    public class Gene_BTXAddiction : Gene
    {
        private const string AddictionHediffDefName = "HD_BTXAddiction";

        public override void PostAdd()
        {
            base.PostAdd();
            EnsureAddiction();
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            EnsureAddiction();
        }

        public override void PostRemove()
        {
            RemoveAddiction();
            base.PostRemove();
        }

        private void EnsureAddiction()
        {
            if (!Active)
            {
                RemoveAddiction();
                return;
            }

            HediffDef addictionDef = DefDatabase<HediffDef>.GetNamedSilentFail(AddictionHediffDefName);
            if (addictionDef == null || pawn?.health?.hediffSet == null
                || pawn.health.hediffSet.HasHediff(addictionDef))
            {
                return;
            }

            pawn.health.AddHediff(addictionDef);
        }

        private void RemoveAddiction()
        {
            HediffDef addictionDef = DefDatabase<HediffDef>.GetNamedSilentFail(AddictionHediffDefName);
            Hediff addiction = addictionDef == null
                ? null
                : pawn?.health?.hediffSet?.GetFirstHediffOfDef(addictionDef);
            if (addiction != null)
            {
                pawn.health.RemoveHediff(addiction);
            }
        }
    }
}
