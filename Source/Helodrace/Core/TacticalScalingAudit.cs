using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public int speed = 3;
        [DataMember] public bool headless;
        [DataMember] public bool r7AutoSlowdownDisabled = true;
        [DataMember] public string r7ObservedTimeSpeed;
        [DataMember] public float r7TickRateMinimum = float.MaxValue, r7TickRateMaximum;
        [DataMember] public int r7RateSamples, r7QueueSamples, r7MaximumDueDelay, r7MaximumDueCommands, r7MaximumPhaseAge;
        [DataMember] public long r7SchedulerAdvances, r7SchedulerBudgetStops;
        [DataMember] public string[] r7ClaimOwners, r7PortalLeases, r7CommandLayers;
    }
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        internal static TimeSpeed AuditTimeSpeed(int multiplier)
        {
            if (multiplier == 1) return TimeSpeed.Normal;
            if (multiplier == 3) return TimeSpeed.Fast;
            throw new ArgumentException("Audit speed must be 1 or 3.");
        }
        // Reuse the existing 30-tick progress sample. No new production Tick
        // hook or pawn loop; report sampled bounds, not exact queue quantiles.
        private void SampleScaling(int tick)
        {
            if (measured < 0) return;
            result.r7RateSamples++;
            float rate = Find.TickManager.TickRateMultiplier;
            result.r7TickRateMinimum = Math.Min(result.r7TickRateMinimum, rate);
            result.r7TickRateMaximum = Math.Max(result.r7TickRateMaximum, rate);
            result.r7ObservedTimeSpeed = Find.TickManager.CurTimeSpeed.ToString();
            result.r7AutoSlowdownDisabled &= DebugViewSettings.neverForceNormalSpeed;
            var scheduler = Current.Game.GetComponent<GameComponent_TacticalCommands>();
            if (scheduler == null) return;
            result.r7SchedulerAdvances = scheduler.Advances; result.r7SchedulerBudgetStops = scheduler.BudgetStops;
            result.r7QueueSamples++;
            int due = 0;
            foreach (TacticalSquadCommand command in map.GetComponent<MapComponent_TacticalCommands>().Commands)
            {
                if (command.Terminal || command.Phase == TacticalCommandPhase.Complete) continue;
                result.r7MaximumPhaseAge = Math.Max(result.r7MaximumPhaseAge, Math.Max(0, tick - command.PhaseStarted));
                if (command.Due > tick) continue;
                due++;
                result.r7MaximumDueDelay = Math.Max(result.r7MaximumDueDelay, tick - command.Due);
            }
            result.r7MaximumDueCommands = Math.Max(result.r7MaximumDueCommands, due);
        }
        // Capture ownership only after End() has drained the profiling window.
        // No additional scan, string allocation or reflection in a tactical tick.
        private void FinalScalingDiagnostics(MapComponent_TacticalCommands service)
        {
            string Owner(TacticalSquadCommand command) => command.Id + ":" + command.Phase
                + ":opening=" + command.Plan?.Opening;
            foreach (string name in new[] { "claims", "leases" })
            {
                var owners = (IDictionary<IntVec3, TacticalSquadCommand>)AccessTools.Field(
                    typeof(MapComponent_TacticalCommands), name).GetValue(service);
                string[] entries = owners.Select(pair => pair.Key + "=" + Owner(pair.Value)).OrderBy(value => value).ToArray();
                if (name == "claims") result.r7ClaimOwners = entries; else result.r7PortalLeases = entries;
            }
            result.r7CommandLayers = service.Commands.Select(command => Owner(command)
                + ":contact=" + (command.ContactResponse == null ? "none" : command.ContactResponse.First.ToString())
                + ":contactLastSeen=" + command.ContactResponse?.LastSeen
                + ":contactRestoring=" + command.ContactRestoring
                + ":field=" + command.FieldResponse?.Stage
                + ":medical=" + command.MedicalCare?.Job?.def.defName
                + ":identifying=" + command.Link.IdentificationHolding
                + ":identifyUntil=" + command.Link.IdentifyUntil
                + ":agreement=" + command.Link.Cooperation.Stage).ToArray();
        }
    }
}
