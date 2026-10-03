using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public static class RaidBreachToolRecovery
    {
        public const string JobName = "HD_RecoverSledgehammer";

        public static bool Pending(IEnumerable<Pawn> members) =>
            members.Any(pawn => pawn.CurJobDef?.defName == JobName);

        public static bool TryStart(List<Pawn> members, IEnumerable<Pawn> roster, Map map)
        {
            if (Pending(members)) return true;
            if (members.Any(pawn => CompSledgehammerBreach.CanOperate(pawn)
                && CompSledgehammerBreach.WornBy(pawn) != null)) return false;
            JobDef jobDef = DefDatabase<JobDef>.GetNamed(JobName);
            List<Pawn> fallen = roster.Where(pawn => pawn != null && (pawn.Downed || pawn.Dead)
                && pawn.MapHeld == map).ToList();
            var tools = new List<(Apparel Tool, Thing Source)>();
            foreach (Pawn donor in fallen)
            {
                Apparel tool = CompSledgehammerBreach.WornBy(donor)?.parent as Apparel;
                Thing source = donor.Dead ? (Thing)donor.Corpse : donor;
                if (tool != null && source?.Spawned == true) tools.Add((tool, source));
            }
            ThingDef toolDef = DefDatabase<ThingDef>.GetNamed("HD_Apparel_GW_Sledgehammer");
            foreach (Apparel tool in map.listerThings.ThingsOfDef(toolDef).OfType<Apparel>())
                if (fallen.Any(donor => donor.PositionHeld.DistanceTo(tool.Position) <= 4f))
                    tools.Add((tool, tool));
            foreach (var candidate in tools)
                foreach (Pawn pawn in members.OrderBy(value =>
                    value.Position.DistanceToSquared(candidate.Source.Position)))
                {
                    if (!CompSledgehammerBreach.CanOperate(pawn) || pawn.apparel == null
                        || pawn.CurJob?.playerForced == true || pawn.stances.FullBodyBusy
                        || MapComponent_RaidTacticalOrders.Protected(pawn)
                        || !ApparelUtility.HasPartsToWear(pawn, candidate.Tool.def)
                        || !candidate.Tool.PawnCanWear(pawn, true)
                        || pawn.Position.DistanceTo(candidate.Source.Position) > 32f
                        || !pawn.CanReserveAndReach(candidate.Source,
                            PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                    pawn.jobs.StartJob(JobMaker.MakeJob(jobDef, candidate.Tool, candidate.Source),
                        JobCondition.InterruptForced);
                    MapComponent_RaidTacticalTrace.Record(pawn, "Recovering fallen breacher's sledgehammer");
                    return true;
                }
            return false;
        }
    }

    public sealed class JobDriver_RecoverSledgehammer : JobDriver
    {
        private Apparel Tool => job.targetA.Thing as Apparel;
        private Thing Source => job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(Source, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.FailOn(() => Tool == null || Tool.Destroyed || !CompSledgehammerBreach.CanOperate(pawn)
                || Tool.TryGetComp<CompSledgehammerBreach>() == null
                || !ApparelUtility.HasPartsToWear(pawn, Tool.def)
                || Tool.ParentHolder is Pawn_ApparelTracker holder
                    && holder.pawn != pawn && !holder.pawn.Downed && !holder.pawn.Dead);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);
            yield return Toils_General.Wait(60, TargetIndex.B);
            yield return Toils_General.Do(() =>
            {
                Apparel tool = Tool;
                if (tool.ParentHolder is Pawn_ApparelTracker holder)
                    holder.Remove(tool);
                pawn.apparel.Wear(tool, true);
                if (CompSledgehammerBreach.WornBy(pawn)?.parent != tool)
                    EndJobWith(JobCondition.Incompletable);
                else MapComponent_RaidTacticalTrace.Record(pawn, "Recovered sledgehammer; resuming breach plan");
            });
        }
    }
}
