using RimWorld;
using Verse;

namespace Helodrace
{
    public static class RaidTacticalSpeech
    {
        public static void Say(Pawn pawn, string key)
        {
            if (pawn?.Spawned == true && pawn.Map == Find.CurrentMap)
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, key.Translate(), 1.7f);
        }
    }
}
