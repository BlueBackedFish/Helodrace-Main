using System;
using Helodrace.Tactics;

internal static class TacticalWorkBudgetTests
{
    internal static void Run()
    {
        var budget = new TacticalWorkBudget(100);
        // Different command/map callers share this same instance and tick.
        if (!budget.TryJob(1, true) || budget.TryJob(1, true)
            || !budget.TryJob(1) || budget.CanJob(1) || budget.TryJob(1))
            throw new Exception("Return and issue limits must be shared across squads/maps.");
        if (!budget.TryObserve(1) || budget.TryObserve(1)) throw new Exception("Observation scans must be bounded globally.");
        if (!budget.TryPlan(1) || budget.TryPlan(1))
            throw new Exception("Cold planning must be limited independently of job count.");
        for (int i = 0; i < TacticalWorkBudget.PathLimit; i++)
            if (!budget.TryPath(1)) throw new Exception("Movement was denied before its limit.");
        if (budget.TryPath(1)) throw new Exception("Too many movement stages were admitted.");
        if (!budget.TryJob(2, true) || !budget.TryPlan(2) || !budget.TryPath(2) || !budget.TryObserve(2))
            throw new Exception("A new tick must replenish every budget.");
        budget.Account(2, 100);
        if (budget.CanJob(2) || budget.TryJob(2) || budget.TryPlan(2) || budget.TryPath(2) || budget.TryObserve(2))
            throw new Exception("A long synchronous operation must defer subsequent work.");
        if (!budget.TryJob(3) || !budget.TryPath(3) || !budget.TryPlan(3))
            throw new Exception("Elapsed accounting must not starve future ticks.");
        Console.WriteLine("PASS: global tactical job/return/path/plan limits, tick reset and elapsed deferral.");
    }
}
