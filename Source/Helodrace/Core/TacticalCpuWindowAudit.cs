using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Helodrace.Profiling;
using Helodrace.Tactics;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public double? r7ColdMainCpuMs, r7ColdProcessCpuMs, r7ColdWallMs;
        [DataMember] public ProfileCpuCheckpoint[] r7CpuCheckpoints;
        [DataMember] public int r7FirstVerifiedMissionTick = -1;
    }
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private readonly bool cpuWindows;
        private List<ProfileCpuCheckpoint> cpuWindowPoints;
        private int nextCpuWindow = 500;
        private long cpuWindowStarted;
        private bool RecordCpuWindow(string label, int offset)
        {
            if (!cpuWindows || measured < 0 || cpuWindowPoints.Count >= 128) return false;
            var commands = map.GetComponent<MapComponent_TacticalCommands>()?.Commands;
            int active = TacticalEngineSelection.Kind == TacticalEngineKind.New
                ? commands.Count(c => !c.Terminal && c.Phase != TacticalCommandPhase.Complete) : -1;
            string phases = Phases();
            ProfileCpuCheckpoint point;
            if (GenCommandLine.TryGetCommandLineArg("hdMethodProfile", out _))
            {
                point = AgentMethodProfiler.CpuCheckpoint(label, offset, active, phases);
                if (point == null) return false; // Do not invent half-written tick totals.
            }
            else
                point = new ProfileCpuCheckpoint { label = label, tick = GenTicks.TicksGame, frame = Time.frameCount,
                    requestedOffset = offset, activeCommands = active, phases = phases,
                    mainCpuMs = uninstrumentedMainStart >= 0 ? (windowClock.Cpu100ns() - uninstrumentedMainStart) / 10000.0 : (double?)null,
                    processCpuMs = uninstrumentedMainStart >= 0 ? (windowClock.ProcessCpu100ns() - uninstrumentedProcessStart) / 10000.0 : (double?)null,
                    wallSeconds = (windowClock.Timestamp() - cpuWindowStarted) / (double)windowClock.Frequency };
            cpuWindowPoints.Add(point); return true;
        }
        private void SampleCpuWindows(int tick)
        {
            if (!cpuWindows || measured < 0 || tick - measured < nextCpuWindow) return;
            if (RecordCpuWindow("fixed", nextCpuWindow)) nextCpuWindow += 500;
        }
        private void MissionCpuWindow(int tick)
        {
            if (!cpuWindows || measured < 0 || result.r7FirstVerifiedMissionTick >= 0 || result.units == 0
                || TacticalEngineSelection.Kind != TacticalEngineKind.New) return;
            TacticalSquadCommand[] commands = map.GetComponent<MapComponent_TacticalCommands>().Commands.ToArray();
            if (commands.Length != result.units || commands.Any(c => c.Phase != TacticalCommandPhase.Complete
                || c.ContactRestoring || c.Link.IdentificationHolding || c.Members.Any(m => !m.EntryAssignmentDone))) return;
            if (MultiRoomFixture)
            {
                var rooms = new[] { new IntVec3(108,0,110), new IntVec3(120,0,110), new IntVec3(120,0,128) };
                if (TimedCqbFixture ? !TimedCqbMissionCoverage(commands, rooms)
                    : commands.Any(c => !TacticalRoomProgress.CoversGoal(c, rooms))) return;
            }
            else if (commands.Any(c => !c.GoalSecured || !c.SecuredCells.Contains(c.Goal))) return;
            if (RecordCpuWindow("first-verified-mission", tick - measured)) result.r7FirstVerifiedMissionTick = tick - started;
            // A first completion is a milestone, not a claim that all later
            // fixed inputs or cooperation expiry can never reactivate a squad.
        }
    }
}
