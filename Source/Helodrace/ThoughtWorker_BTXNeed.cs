using RimWorld;
using Verse;

namespace Helodrace
{
    public class ThoughtWorker_BTXNeed : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!BTXUtility.IsHelod(p) || p?.needs?.AllNeeds == null)
            {
                return ThoughtState.Inactive;
            }

            Need_BTX btxNeed = null;
            foreach (Need need in p.needs.AllNeeds)
            {
                if (need is Need_BTX found)
                {
                    btxNeed = found;
                    break;
                }
            }

            if (btxNeed == null)
            {
                return ThoughtState.Inactive;
            }

            float level = btxNeed.CurLevelPercentage;
            if (level <= 0.001f)
            {
                return ThoughtState.ActiveAtStage(2);
            }

            if (level < 0.20f)
            {
                return ThoughtState.ActiveAtStage(1);
            }

            if (level < 0.50f)
            {
                return ThoughtState.ActiveAtStage(0);
            }

            return ThoughtState.Inactive;
        }
    }
}
