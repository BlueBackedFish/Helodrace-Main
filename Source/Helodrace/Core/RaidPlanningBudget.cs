using System.Diagnostics;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed class MapComponent_RaidPlanningBudget : MapComponent
    {
        private bool running;
        private readonly TacticalWorkBudget budget = new TacticalWorkBudget(Stopwatch.Frequency * 3 / 1000, 2);
        public MapComponent_RaidPlanningBudget(Map map) : base(map) { }
        public override void MapComponentUpdate() { base.MapComponentUpdate(); running = true; }
        internal static bool Admit(Map map, string unit, string purpose)
        {
            var component = map?.GetComponent<MapComponent_RaidPlanningBudget>();
            return component?.running != true || component.budget.Admit(unit, Time.frameCount);
        }
        internal static void Record(Map map, long ticks)
        {
            var component = map?.GetComponent<MapComponent_RaidPlanningBudget>();
            if (component?.running == true) component.budget.Record(ticks, Time.frameCount);
        }
        internal long Deferred => budget.Deferred;
    }
}
