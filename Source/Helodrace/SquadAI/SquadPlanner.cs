using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.SquadAI
{
    // Geometry for posts, flank zones and bounds. Each call evaluates a bounded
    // set of cells once; nothing is cached between plans.
    public static class SquadPlanner
    {
        private struct Candidate { public IntVec3 Cell; public float Score; }
        private static readonly List<Candidate> candidates = new List<Candidate>(128);
        private static readonly List<IntVec3> chosen = new List<IntVec3>(16);

        public static double Distance(IntVec3 a, IntVec3 b)
        {
            double x = a.x - b.x, z = a.z - b.z;
            return Math.Sqrt(x * x + z * z);
        }

        // A point on the line from origin through toward at the given distance from origin.
        public static IntVec3 Along(IntVec3 origin, IntVec3 toward, double distance)
        {
            double x = toward.x - origin.x, z = toward.z - origin.z, length = Math.Sqrt(x * x + z * z);
            if (length < .001) return origin;
            return new IntVec3((int)Math.Round(origin.x + x / length * distance), 0, (int)Math.Round(origin.z + z / length * distance));
        }

        // Rotates the focus->from direction by degrees and returns the point at radius from focus.
        public static IntVec3 Around(IntVec3 focus, IntVec3 from, double degrees, double radius)
        {
            double x = from.x - focus.x, z = from.z - focus.z, length = Math.Sqrt(x * x + z * z);
            if (length < .001) { x = 0; z = 1; length = 1; }
            double angle = degrees * Math.PI / 180, cos = Math.Cos(angle), sin = Math.Sin(angle);
            double rx = (x * cos - z * sin) / length, rz = (x * sin + z * cos) / length;
            return new IntVec3((int)Math.Round(focus.x + rx * radius), 0, (int)Math.Round(focus.z + rz * radius));
        }

        // Sideways offset, perpendicular to the focus->center axis.
        public static IntVec3 Lateral(IntVec3 center, IntVec3 focus, double offset)
        {
            double x = center.x - focus.x, z = center.z - focus.z, length = Math.Sqrt(x * x + z * z);
            if (length < .001 || Math.Abs(offset) < .001) return center;
            return new IntVec3((int)Math.Round(center.x - z / length * offset), 0, (int)Math.Round(center.z + x / length * offset));
        }

        // Lateral distance of cell from the ray focus->post. A shot at post that
        // misses travels along this ray, so a squadmate near it is exposed.
        public static double LateralFromRay(IntVec3 focus, IntVec3 post, IntVec3 cell)
        {
            double ax = post.x - focus.x, az = post.z - focus.z, length = Math.Sqrt(ax * ax + az * az);
            if (length < .001) return 0;
            double bx = cell.x - focus.x, bz = cell.z - focus.z;
            if (ax * bx + az * bz <= 0) return double.MaxValue;
            return Math.Abs(ax * bz - az * bx) / length;
        }

        public static bool PostAllowed(IntVec3 cell, IntVec3 focus, IList<IntVec3> taken, int spacing, bool overlap)
        {
            int spacingSquared = spacing * spacing;
            foreach (IntVec3 other in taken)
            {
                if (cell.DistanceToSquared(other) < spacingSquared) return false;
                // Both directions: a miss at either post must not run onto the other.
                if (overlap && (LateralFromRay(focus, other, cell) < SquadPolicy.LineOverlapLateral
                    || LateralFromRay(focus, cell, other) < SquadPolicy.LineOverlapLateral)) return false;
            }
            return true;
        }

        private static bool Usable(IntVec3 cell, Map map) => cell.InBounds(map) && cell.Standable(map)
            && cell.GetDoor(map) == null && !cell.ContainsStaticFire(map);

        public static IntVec3 NearestUsable(IntVec3 cell, Map map, float radius = 4f)
        {
            foreach (IntVec3 near in GenRadial.RadialCellsAround(cell, radius, true))
                if (Usable(near, map)) return near;
            return IntVec3.Invalid;
        }

        // Spreads the team's posts over covered cells near center that can see focus.
        // taken holds posts already given to other teams and squads on this map.
        public static void PlaceTeam(SquadTeam team, IntVec3 center, IntVec3 focus, float range, Map map,
            List<IntVec3> taken, int tick, TeamTask task)
        {
            team.Posts.Clear(); team.Task = task; team.TaskStarted = tick; team.Zone = center;
            if (team.Members.Count == 0 || !center.InBounds(map)) return;
            candidates.Clear();
            double ideal = range * SquadPolicy.BaseStandoffFactor;
            // A large team needs a wider zone to keep its spacing.
            float radius = Math.Max(SquadPolicy.PostRadius, 2.6f * (float)Math.Sqrt(team.Members.Count));
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!Usable(cell, map)) continue;
                float cover = CoverUtility.CalculateOverallBlockChance(cell, focus, map);
                bool sight = GenSight.LineOfSight(cell, focus, map, true);
                double score = (sight ? 1.0 : -1.5) + 2.5 * cover - .04 * Distance(cell, center)
                    - .02 * Math.Abs(Distance(cell, focus) - ideal);
                candidates.Add(new Candidate { Cell = cell, Score = (float)score });
            }
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            chosen.Clear();
            // Strict pass, then relaxed spacing without the overlap rule.
            for (int pass = 0; pass < 2 && chosen.Count < team.Members.Count; pass++)
                foreach (Candidate candidate in candidates)
                {
                    if (chosen.Count >= team.Members.Count) break;
                    if (chosen.Contains(candidate.Cell)) continue;
                    int spacing = pass == 0 ? SquadPolicy.PostSpacing : 2;
                    if (PostAllowed(candidate.Cell, focus, taken, spacing, pass == 0)
                        && PostAllowed(candidate.Cell, focus, chosen, spacing, pass == 0)) chosen.Add(candidate.Cell);
                }
            // Pair posts with the nearest member who can actually walk there.
            var unassigned = new List<Pawn>(team.Members);
            foreach (IntVec3 post in chosen)
            {
                Pawn best = null; int bestDistance = int.MaxValue;
                foreach (Pawn pawn in unassigned)
                {
                    int distance = pawn.Position.DistanceToSquared(post);
                    if (distance < bestDistance && map.reachability.CanReach(pawn.Position, post, PathEndMode.OnCell,
                        TraverseParms.For(pawn, Danger.Deadly))) { best = pawn; bestDistance = distance; }
                }
                if (best == null) continue;
                team.Posts[best] = post; unassigned.Remove(best); taken.Add(post);
            }
            // Anyone left over shares the zone; vanilla picks a cell inside it.
            foreach (Pawn pawn in unassigned) team.Posts[pawn] = center;
        }

        // A zone off to one side of the base of fire that still sees the focus,
        // reached by a route that does not pass close to the enemy.
        public static IntVec3 FlankZone(IntVec3 focus, IntVec3 baseCenter, IntVec3 teamCenter, float range, Pawn leader, Map map)
        {
            double radius = Math.Max(12, Math.Min(range * .8, Distance(baseCenter, focus)));
            IntVec3 best = IntVec3.Invalid; double bestScore = double.MinValue;
            foreach (double degrees in new[] { 65.0, -65.0, 85.0, -85.0 })
            {
                IntVec3 zone = NearestUsable(Around(focus, baseCenter, degrees, radius), map);
                if (!zone.IsValid || Distance(zone, baseCenter) < 10) continue;
                if (!GenSight.LineOfSight(zone, focus, map, true)) continue;
                if (SegmentDistance(focus, teamCenter, zone) < .45 * Distance(focus, teamCenter)) continue;
                if (!map.reachability.CanReach(leader.Position, zone, PathEndMode.OnCell, TraverseParms.For(leader, Danger.Deadly))) continue;
                double score = -Distance(zone, teamCenter) * .1 + CoverUtility.TotalSurroundingCoverScore(zone, map);
                if (score > bestScore) { bestScore = score; best = zone; }
            }
            return best;
        }

        public static double SegmentDistance(IntVec3 point, IntVec3 a, IntVec3 b)
        {
            double dx = b.x - a.x, dz = b.z - a.z, length = dx * dx + dz * dz;
            double t = length < .001 ? 0 : Math.Max(0, Math.Min(1, ((point.x - a.x) * dx + (point.z - a.z) * dz) / length));
            double x = a.x + t * dx - point.x, z = a.z + t * dz - point.z;
            return Math.Sqrt(x * x + z * z);
        }
    }
}
