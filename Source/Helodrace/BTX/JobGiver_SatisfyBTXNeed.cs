using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class JobGiver_SatisfyBTXNeed : ThinkNode_JobGiver
    {
        private const float NeedThreshold = 0.35f;
        private const float SearchRadius = 9999f;
        private const string PlainDogChewDefName = "HD_PlainDogChew";

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ShouldSatisfyBTXNeed(pawn))
            {
                return null;
            }

            Thing btxSource = FindBTXSource(pawn, PlainDogChewDefName)
                ?? FindBTXSource(pawn, BTXUtility.NaphthaThingDefName);
            if (btxSource == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(JobDefOf.Ingest, btxSource);
            job.count = 1;
            return job;
        }

        private static bool ShouldSatisfyBTXNeed(Pawn pawn)
        {
            if (!BTXUtility.IsHelod(pawn)
                || pawn?.Map == null
                || pawn.InMentalState
                || pawn.Downed
                || pawn.needs?.AllNeeds == null)
            {
                return false;
            }

            foreach (Need need in pawn.needs.AllNeeds)
            {
                if (need is Need_BTX)
                {
                    return need.CurLevelPercentage <= NeedThreshold;
                }
            }

            return false;
        }

        private static Thing FindBTXSource(Pawn pawn, string thingDefName)
        {
            ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);
            if (thingDef == null)
            {
                return null;
            }

            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(thingDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                SearchRadius,
                thing => !thing.IsForbidden(pawn)
                    && thing.Spawned
                    && thing.stackCount > 0
                    && pawn.CanReserve(thing)
                    && thing.def.IsIngestible);
        }
    }
}
