using System;
using System.Collections.Generic;
using System.Linq;

namespace Helodrace.Squads
{
    public sealed class FormationPlan
    {
        public readonly List<FormationDef> roots = new List<FormationDef>();
        public float initialRaidPoints;
        public float formationPointsSpent;
        public float SupportPointsRemaining => Math.Max(0f, initialRaidPoints - formationPointsSpent);
        public float PointOverrun => Math.Max(0f, formationPointsSpent - initialRaidPoints);
        public int Personnel => roots.Sum(formation => formation.StandardPersonnel);
    }

    public static class FormationPlanner
    {
        // Prefer doctrine priorities, then larger complete units. Tolerance is a single
        // allowance on the ORIGINAL raid budget, never applied afresh to every unit.
        public static FormationPlan Plan(float points, DoctrineDef doctrine)
        {
            if (doctrine == null) throw new ArgumentNullException(nameof(doctrine));
            if (points < 0 || float.IsNaN(points) || float.IsInfinity(points))
                throw new ArgumentOutOfRangeException(nameof(points));
            var result = new FormationPlan { initialRaidPoints = points };
            var candidates = doctrine.availableFormations.Distinct()
                .Where(formation => formation != null && !formation.ConfigErrors().Any())
                .Select(formation => new { formation, cost = formation.FormationCost })
                .OrderByDescending(item => item.formation.selectionPriority)
                .ThenByDescending(item => item.cost)
                .ThenBy(item => item.formation.defName, StringComparer.Ordinal).ToList();
            double limit = (double)points * (1d + doctrine.formationPointTolerance);
            double spent = 0;
            while (true)
            {
                var next = candidates.FirstOrDefault(item => spent + item.cost <= limit + 0.0001d);
                if (next == null) break;
                result.roots.Add(next.formation);
                spent += next.cost;
            }
            result.formationPointsSpent = (float)spent;
            return result;
        }
    }
}
