using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class RaidBreachToolRecoveryTarget : IExposable
    {
        public string OrganizationId;
        public Apparel Tool;

        public void ExposeData()
        {
            Scribe_Values.Look(ref OrganizationId, "organizationId");
            Scribe_References.Look(ref Tool, "tool");
        }
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        // Keep the actual equipment reference independently of membership and
        // plans. Pawn.Kill passes the deceased to WorldPawns and detaches them.
        private List<RaidBreachToolRecoveryTarget> recoveryTargets = new List<RaidBreachToolRecoveryTarget>();

        internal void RememberBreachTools(string organizationId, Pawn donor)
        {
            if (organizationId == null || donor?.apparel == null) return;
            foreach (Apparel tool in donor.apparel.WornApparel)
                if (tool.TryGetComp<CompSledgehammerBreach>() != null
                    && !recoveryTargets.Any(target => target.Tool == tool))
                    recoveryTargets.Add(new RaidBreachToolRecoveryTarget {
                        OrganizationId = organizationId, Tool = tool });
        }

        internal void PruneBreachTools(ISet<string> organizationsOnMap)
        {
            recoveryTargets.RemoveAll(target => target == null
                || !organizationsOnMap.Contains(target.OrganizationId)
                || RaidBreachToolRecovery.Resolved(target.Tool, map));
        }

        private IEnumerable<Apparel> BreachToolsFor(string organizationId) => recoveryTargets
            .Where(target => target.OrganizationId == organizationId).Select(target => target.Tool);
    }

    public static class RaidBreachToolRecovery
    {
        public const string JobName = "HD_RecoverSledgehammer";

        public static bool Pending(IEnumerable<Pawn> members) =>
            members.Any(pawn => pawn.CurJobDef?.defName == JobName);

        internal static bool Resolved(Apparel tool, Map map) => tool == null || tool.Destroyed
            || tool.SpawnedParentOrMe?.Map != map || tool.ParentHolder is Pawn_ApparelTracker holder
                && !holder.pawn.Dead && !holder.pawn.Downed;

        internal static Thing SourceFor(Apparel tool)
        {
            if (tool == null || tool.Destroyed) return null;
            if (tool.Spawned) return tool;
            if (!(tool.ParentHolder is Pawn_ApparelTracker holder)) return null;
            Pawn donor = holder.pawn;
            return donor.Dead ? (Thing)donor.Corpse : donor.Downed ? donor : null;
        }

        public static bool TryStart(List<Pawn> members, IEnumerable<Apparel> tools, Map map)
        {
            if (Pending(members)) return true;
            if (members.Any(pawn => CompSledgehammerBreach.CanOperate(pawn)
                && CompSledgehammerBreach.WornBy(pawn) != null)) return false;
            JobDef jobDef = DefDatabase<JobDef>.GetNamed(JobName);
            foreach (Apparel tool in tools)
            {
                Thing source = SourceFor(tool);
                if (source?.Spawned != true || source.Map != map
                    || tool.TryGetComp<CompSledgehammerBreach>() == null) continue;
                foreach (Pawn pawn in members.OrderBy(value =>
                    value.Position.DistanceToSquared(source.Position)))
                {
                    if (!CompSledgehammerBreach.CanOperate(pawn) || pawn.apparel == null
                        || pawn.CurJob?.playerForced == true || pawn.stances.FullBodyBusy
                        || MapComponent_RaidTacticalOrders.Protected(pawn)
                        || !ApparelUtility.HasPartsToWear(pawn, tool.def)
                        || !tool.PawnCanWear(pawn, true)
                        || CompBiocodable.IsBiocoded(tool) && !CompBiocodable.IsBiocodedFor(tool, pawn)
                        || pawn.Position.DistanceTo(source.Position) > 32f
                        || !pawn.CanReserveAndReach(source,
                            PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                    pawn.jobs.StartJob(JobMaker.MakeJob(jobDef, tool, source),
                        JobCondition.InterruptForced);
                    MapComponent_RaidTacticalTrace.Record(pawn, "Recovering fallen breacher's sledgehammer");
                    return true;
                }
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
                || !Tool.PawnCanWear(pawn, true)
                || CompBiocodable.IsBiocoded(Tool) && !CompBiocodable.IsBiocodedFor(Tool, pawn)
                || RaidBreachToolRecovery.SourceFor(Tool) != Source
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
