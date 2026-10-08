using System.Diagnostics;

namespace Helodrace.Tactics
{
    // Global limits shared by every map, command and tactical JobDriver.
    // Accounting is for completed synchronous operations, not preemption.
    public sealed class TacticalWorkBudget
    {
        public const int JobLimit = 2, ReturnLimit = 1, PathLimit = 4, PlanLimit = 1, ObserveLimit = 1;
        private int tick = int.MinValue, jobs, returns, paths, plans, observations, communications;
        private long elapsed;
        private readonly long elapsedLimit;
        public TacticalWorkBudget(long elapsedLimitTicks = 0)
        { elapsedLimit = elapsedLimitTicks > 0 ? elapsedLimitTicks : (long)(Stopwatch.Frequency * .0015); }
        private void BeginTick(int now)
        {
            if (tick == now) return;
            tick = now; jobs = returns = paths = plans = observations = communications = 0; elapsed = 0;
        }
        public bool TryJob(int now, bool returning = false)
        {
            if (!CanJob(now, returning)) return false;
            jobs++; if (returning) returns++; return true;
        }
        public bool CanJob(int now, bool returning = false)
        { BeginTick(now); return elapsed < elapsedLimit && jobs < JobLimit && (!returning || returns < ReturnLimit); }
        public bool TryPath(int now)
        {
            BeginTick(now);
            if (elapsed >= elapsedLimit || paths >= PathLimit) return false;
            paths++; return true;
        }
        public bool TryPlan(int now)
        {
            BeginTick(now);
            if (elapsed >= elapsedLimit || plans >= PlanLimit) return false;
            plans++; return true;
        }
        public void Account(int now, long cost)
        { BeginTick(now); if (cost > 0) elapsed += cost; }
        public bool TryObserve(int now)
        {
            BeginTick(now);
            if (elapsed >= elapsedLimit || observations >= ObserveLimit) return false;
            observations++; return true;
        }
        public bool TryCommunication(int now)
        {
            BeginTick(now);
            if (elapsed >= elapsedLimit || communications >= 2) return false;
            communications++; return true;
        }
    }
}
