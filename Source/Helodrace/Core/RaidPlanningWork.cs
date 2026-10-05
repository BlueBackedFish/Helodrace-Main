using System.Collections.Generic;

namespace Helodrace
{
    internal sealed class RaidCandidateWindow
    {
        private int offset;
        internal void Clear() => offset = 0;
        internal int Start(int count) => count > 0 ? offset %= count : offset = 0;
        internal void Advance(int visited, int count)
            => offset = count > 0 ? (offset + visited) % count : 0;
    }

    // One allowance for the entire plan, including alternative routes. Failed
    // candidates cannot each start another full-map search with a fresh allowance.
    internal sealed class RaidPlanningWork
    {
        internal const int StructureLimit = 48;
        internal const int BreachCheckLimit = 8;
        internal const int RouteLimit = 4096;
        internal int RouteSteps { get; private set; }
        internal int BreachChecks { get; private set; }
        internal bool Limited { get; private set; }
        internal void MarkLimited() => Limited = true;
        internal bool TryRouteStep()
        {
            if (RouteSteps >= RouteLimit) { Limited = true; return false; }
            RouteSteps++; return true;
        }
        internal bool TryBreachCheck()
        {
            if (BreachChecks >= BreachCheckLimit) { Limited = true; return false; }
            BreachChecks++; return true;
        }
    }

    // A failed room remains pending but does not monopolize every update. This
    // is derived runtime data; rebuilding it after loading is sufficient.
    internal sealed class RaidRoomPlanAttempts<T>
    {
        internal const int RetryTicks = 180;
        private readonly Dictionary<T, int> failures = new Dictionary<T, int>();
        internal void Clear() => failures.Clear();
        internal void Failed(T target, int tick) => failures[target] = tick + RetryTicks;
        internal bool TrySelect(IEnumerable<T> targets, int tick, out T selected, out bool pending)
        {
            pending = false;
            foreach (T target in targets)
            {
                pending = true;
                if (failures.TryGetValue(target, out int after) && tick < after) continue;
                selected = target; return true;
            }
            selected = default(T); return false;
        }
    }
}
