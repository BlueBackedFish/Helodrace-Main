using System;
using System.Linq;
using System.Runtime.Serialization;
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
    }
}
