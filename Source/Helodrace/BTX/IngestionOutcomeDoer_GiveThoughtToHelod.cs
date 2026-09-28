using RimWorld;
using Verse;

namespace Helodrace
{
    public class IngestionOutcomeDoer_GiveThoughtToHelod : IngestionOutcomeDoer
    {
        public ThoughtDef thoughtDef;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            if (!BTXUtility.HasBTXDependency(pawn) || thoughtDef == null)
            {
                return;
            }

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(thoughtDef);
        }
    }
}
