using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.Squads;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class MapComponent_RaidTacticalTrace : MapComponent
    {
        public static bool Enabled;
        private readonly Queue<KeyValuePair<string, string>> events =
            new Queue<KeyValuePair<string, string>>();

        public MapComponent_RaidTacticalTrace(Map map) : base(map) { }

        public static void Record(Pawn pawn, string detail)
        {
            if (!Enabled || !Prefs.DevMode || pawn?.Spawned != true) return;
            string organization = OrganizationAPI.GetOrganization(pawn)?.id;
            if (organization == null) return;
            var trace = pawn.Map.GetComponent<MapComponent_RaidTacticalTrace>();
            if (trace.events.Count >= 128) trace.events.Dequeue();
            string phase = pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>()
                ?.Status(organization);
            trace.events.Enqueue(new KeyValuePair<string, string>(organization,
                $"{GenTicks.TicksGame} {pawn.LabelShort} [{phase}] at {pawn.Position}: {detail}"));
        }

        public string Report(string organization)
        {
            if (!Enabled) return "Job trace disabled (enable in this window).";
            return string.Join("\n", events.Where(value => value.Key == organization)
                .Select(value => value.Value).Reverse().Take(24));
        }

        internal static string Describe(Job job) => job == null ? "none"
            : $"{job.def.defName} A={job.targetA} source={job.jobGiver?.GetType().Name ?? "direct"}";
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_RaidTacticalTrace_StartJob
    {
        public static void Prefix(Pawn ___pawn, Job newJob,
            JobCondition lastJobEndCondition, bool cancelBusyStances, ThinkNode jobGiver)
        {
            if (!MapComponent_RaidTacticalTrace.Enabled || !Prefs.DevMode) return;
            MapComponent_RaidTacticalTrace.Record(___pawn,
                $"start {MapComponent_RaidTacticalTrace.Describe(___pawn.CurJob)} -> "
                + $"{MapComponent_RaidTacticalTrace.Describe(newJob)} "
                + $"condition={lastJobEndCondition} cancelBusy={cancelBusyStances} "
                + $"issuer={jobGiver?.GetType().Name ?? "direct"} "
                + $"busy={___pawn.stances?.FullBodyBusy == true}");
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_RaidTacticalTrace_EndJob
    {
        public static void Prefix(Pawn ___pawn, JobCondition condition)
        {
            MapComponent_RaidTacticalOrders.Ended(___pawn, condition);
            if (!MapComponent_RaidTacticalTrace.Enabled || !Prefs.DevMode) return;
            MapComponent_RaidTacticalTrace.Record(___pawn,
                $"end {MapComponent_RaidTacticalTrace.Describe(___pawn.CurJob)} condition={condition}");
        }
    }
}
