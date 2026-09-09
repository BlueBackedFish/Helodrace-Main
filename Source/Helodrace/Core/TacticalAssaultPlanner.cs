using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Future;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public enum TacticalAssaultMode
    {
        Heliborne,
        Sabotage
    }

    [Flags]
    public enum TacticalBreachCapability
    {
        None = 0,
        ExplosiveCharge = 1,
        PowerCutter = 2
    }

    public enum TacticalBreachMethod
    {
        None,
        ExplosiveCharge,
        PowerCutter
    }

    [Flags]
    public enum TacticalSupportCapability
    {
        None = 0,
        OffensiveGrenade = 1,
        Flashbang = 2,
        CsGrenade = 4,
        SmokeGrenade = 8
    }

    public enum TacticalEntryStrategy
    {
        Direct,
        SingleFlankBreach,
        SimultaneousExplosiveFlank,
        SmokeCoveredAssault,
        AvoidKillzone
    }

    public sealed class TacticalBreachStep
    {
        public IntVec3 Cell { get; internal set; }
        public Building Target { get; internal set; }
        public TacticalBreachMethod Method { get; internal set; }
        public float Cost { get; internal set; }
        public int RequiredC4 { get; internal set; }
    }

    public sealed class TacticalAssaultPlan
    {
        public TacticalAssaultMode Mode { get; internal set; }
        public IntVec3 AttackCell { get; internal set; }
        public IntVec3 InsertionCell { get; internal set; }
        public IntVec3 HelicopterHoverCell { get; internal set; }
        public IntVec3 ObjectiveCell { get; internal set; }
        public IntVec3 RouteEndCell { get; internal set; }
        public StrategicTarget Objective { get; internal set; }
        public List<IntVec3> Route { get; internal set; } = new List<IntVec3>();
        public List<TacticalBreachStep> Breaches { get; internal set; } = new List<TacticalBreachStep>();
        public float TotalCost { get; internal set; }
        public float ThreatExposure { get; internal set; }
        public KillzoneKind CrossedKillzones { get; internal set; }
        public AssaultRecommendation Recommendation { get; internal set; }
        public TacticalEntryStrategy EntryStrategy { get; internal set; }
        public TacticalSupportCapability SuggestedSupport { get; internal set; }
        public TacticalSupportCapability UsableSupport { get; internal set; }
        public TacticalSupportCapability PreferredInteriorSupport { get; internal set; }
        public TacticalSupportCapability SelectedInteriorSupport { get; internal set; }
        public List<TacticalBreachStep> CoordinatedBreaches { get; internal set; } = new List<TacticalBreachStep>();
        public int InteriorHostileCount { get; internal set; }
        public float InteriorPersonnelThreat { get; internal set; }
        public Vector2 DominantThreatDirection { get; internal set; }
        public float ThreatDirectionality { get; internal set; }
        public float MitigatedThreatExposure { get; internal set; }
        public string TacticalRationale { get; internal set; }
        public int ExpandedNodes { get; internal set; }
        public string FailureReason { get; internal set; }
        public bool Success => Route.Count > 0 && FailureReason.NullOrEmpty();
    }

    public static class TacticalAssaultPlanner
    {
        private const int EdgeCandidateLimit = 6;
        private const float ThreatCostFactor = 0.055f;
        private static CompProperties_PowerCutterBreach cachedPowerCutterProperties;

        public static TacticalAssaultPlan MakePlan(
            Map map,
            TacticalAssaultMode mode,
            TacticalBreachCapability capabilities,
            TacticalSupportCapability supportCapabilities,
            IntVec3 requestedStart,
            IntVec3 requestedObjective,
            Faction attacker = null)
        {
            TacticalAssaultPlan failed = new TacticalAssaultPlan { Mode = mode };
            if (map == null)
            {
                failed.FailureReason = "No map.";
                return failed;
            }

            MapComponent_TacticalMapAnalysis analysis = map.GetComponent<MapComponent_TacticalMapAnalysis>();
            analysis.RequestAnalysis(attacker, 3600);
            StrategicTarget objective = requestedObjective.IsValid
                ? FindObjectiveAt(analysis, requestedObjective)
                : SelectObjective(analysis, mode, requestedStart, attacker);
            IntVec3 objectiveCell = requestedObjective.IsValid
                ? requestedObjective
                : objective?.Cell ?? IntVec3.Invalid;
            if (!objectiveCell.IsValid || !objectiveCell.InBounds(map))
            {
                failed.FailureReason = "No valid strategic objective was found.";
                return failed;
            }

            IntVec3 routeEnd = ResolveStandableGoal(map, objectiveCell);
            if (!routeEnd.IsValid)
            {
                failed.FailureReason = "No reachable cell exists beside the objective.";
                return failed;
            }

            List<StartCandidate> starts = mode == TacticalAssaultMode.Heliborne
                ? HeliborneStarts(map, analysis, objectiveCell, requestedStart, attacker)
                : SabotageStarts(map, analysis, objectiveCell, requestedStart, attacker);
            if (starts.Count == 0)
            {
                failed.FailureReason = mode == TacticalAssaultMode.Heliborne
                    ? "No valid helicopter hover and rope landing pair was found."
                    : "No valid sabotage ingress cell was found.";
                return failed;
            }

            TacticalAssaultPlan best = FindRoute(
                map,
                analysis,
                starts,
                routeEnd,
                capabilities,
                attacker,
                out StartCandidate selectedStart);
            if (best?.Success == true)
            {
                best.Mode = mode;
                best.InsertionCell = selectedStart.Landing;
                best.HelicopterHoverCell = selectedStart.Hover;
                best.ObjectiveCell = objectiveCell;
                best.RouteEndCell = routeEnd;
                best.Objective = objective;
                EvaluateEntryTactics(
                    map,
                    analysis,
                    best,
                    capabilities,
                    supportCapabilities,
                    attacker);
                best.AttackCell = SelectAttackCell(map, analysis, best);
            }

            if (best?.Success != true)
            {
                failed.Objective = objective;
                failed.ObjectiveCell = objectiveCell;
                failed.RouteEndCell = routeEnd;
                failed.ExpandedNodes = best?.ExpandedNodes ?? 0;
                failed.FailureReason = capabilities == TacticalBreachCapability.None
                    ? "No walking route exists. Select a breach capability and retry."
                    : "No route exists with the selected breach capabilities.";
                return failed;
            }
            return best;
        }

        public static TacticalAssaultPlan MakePlan(
            Map map,
            TacticalAssaultMode mode,
            TacticalBreachCapability capabilities,
            IntVec3 requestedStart,
            IntVec3 requestedObjective,
            Faction attacker = null)
        {
            return MakePlan(
                map,
                mode,
                capabilities,
                TacticalSupportCapability.None,
                requestedStart,
                requestedObjective,
                attacker);
        }

        private static void EvaluateEntryTactics(
            Map map,
            MapComponent_TacticalMapAnalysis analysis,
            TacticalAssaultPlan plan,
            TacticalBreachCapability breachCapabilities,
            TacticalSupportCapability supportCapabilities,
            Faction attacker)
        {
            List<KillzoneRecord> crossed = analysis.Killzones
                .Where(zone => plan.Route.Any(cell => zone.Contains(map, cell)))
                .OrderByDescending(zone => zone.ExpectedThreat)
                .ToList();
            KillzoneRecord dominant = crossed.FirstOrDefault();
            if (dominant != null)
            {
                plan.DominantThreatDirection = dominant.DominantThreatDirection;
                plan.ThreatDirectionality = dominant.Directionality;
            }

            Room objectiveRoom = ResolveEntryRoom(map, plan) ?? plan.RouteEndCell.GetRoom(map);
            List<Pawn> interiorHostiles = map.mapPawns.AllPawnsSpawned
                .Where(pawn => !pawn.Dead && !pawn.Downed
                    && IsHostileDefender(pawn, attacker)
                    && IsInsideObjectiveArea(pawn, objectiveRoom, plan.ObjectiveCell))
                .ToList();
            plan.InteriorHostileCount = interiorHostiles.Count;
            plan.InteriorPersonnelThreat = interiorHostiles.Sum(PersonnelThreat);
            bool enclosed = objectiveRoom != null && !objectiveRoom.PsychologicallyOutdoors;
            int roomCells = objectiveRoom?.CellCount ?? 9999;

            TacticalSupportCapability suggested = TacticalSupportCapability.None;
            if (enclosed && interiorHostiles.Count >= 2 && roomCells <= 90)
            {
                suggested |= TacticalSupportCapability.OffensiveGrenade;
            }
            if (enclosed && interiorHostiles.Count >= 1)
            {
                suggested |= TacticalSupportCapability.Flashbang;
            }
            if (enclosed && interiorHostiles.Count >= 2 && roomCells >= 12 && roomCells <= 260)
            {
                suggested |= TacticalSupportCapability.CsGrenade;
            }
            bool rangedKillzone = dominant != null
                && (dominant.Kinds & (KillzoneKind.Barrel | KillzoneKind.Shooting)) != 0;
            bool smokeUnsafe = plan.CrossedKillzones.HasFlag(KillzoneKind.Temperature)
                || plan.CrossedKillzones.HasFlag(KillzoneKind.Melee);
            if (rangedKillzone && !smokeUnsafe)
            {
                suggested |= TacticalSupportCapability.SmokeGrenade;
            }
            plan.SuggestedSupport = suggested;
            plan.UsableSupport = suggested & supportCapabilities;
            plan.PreferredInteriorSupport = PreferredInteriorSupport(
                plan,
                suggested,
                roomCells);
            plan.SelectedInteriorSupport = SelectAvailableInteriorSupport(
                plan.PreferredInteriorSupport,
                plan.UsableSupport);

            bool stronglyDirectional = dominant != null && dominant.Directionality >= 0.55f;
            if (stronglyDirectional
                && (breachCapabilities & TacticalBreachCapability.ExplosiveCharge) != 0)
            {
                plan.CoordinatedBreaches = FindCoordinatedFlankBreaches(
                    map,
                    analysis,
                    dominant,
                    plan.ObjectiveCell,
                    attacker);
            }

            if (plan.CoordinatedBreaches.Count >= 2)
            {
                plan.EntryStrategy = TacticalEntryStrategy.SimultaneousExplosiveFlank;
                plan.TacticalRationale = "Directional fire is bypassed through separated, simultaneous flank charges.";
            }
            else if ((plan.UsableSupport & TacticalSupportCapability.SmokeGrenade) != 0)
            {
                plan.EntryStrategy = TacticalEntryStrategy.SmokeCoveredAssault;
                plan.TacticalRationale = "Smoke can cut the ranged killzone's effective exposure; thermal and melee traps were not detected.";
            }
            else if (stronglyDirectional && plan.Breaches.Count > 0)
            {
                plan.EntryStrategy = TacticalEntryStrategy.SingleFlankBreach;
                plan.TacticalRationale = "The selected breach avoids the strongest firing axis, but only one entry is available.";
            }
            else if (dominant != null && dominant.ExpectedThreat >= 55f)
            {
                plan.EntryStrategy = TacticalEntryStrategy.AvoidKillzone;
                plan.TacticalRationale = "The killzone is strong and no effective flank or obscuration package is available.";
            }
            else
            {
                plan.EntryStrategy = TacticalEntryStrategy.Direct;
                plan.TacticalRationale = "No dominant directional killzone requires a special entry method.";
            }

            float mitigation = plan.EntryStrategy == TacticalEntryStrategy.SmokeCoveredAssault ? 0.48f
                : plan.EntryStrategy == TacticalEntryStrategy.SimultaneousExplosiveFlank ? 0.58f
                : plan.EntryStrategy == TacticalEntryStrategy.SingleFlankBreach ? 0.78f : 1f;
            plan.MitigatedThreatExposure = plan.ThreatExposure * mitigation;
        }

        private static Room ResolveEntryRoom(Map map, TacticalAssaultPlan plan)
        {
            TacticalBreachStep first = plan.Breaches.FirstOrDefault();
            if (first == null) return null;
            int index = plan.Route.IndexOf(first.Cell);
            if (index >= 0 && index + 1 < plan.Route.Count)
            {
                return plan.Route[index + 1].GetRoom(map);
            }
            return null;
        }

        private static TacticalSupportCapability PreferredInteriorSupport(
            TacticalAssaultPlan plan,
            TacticalSupportCapability suggested,
            int roomCells)
        {
            bool preservesObjective = plan.Objective != null && plan.Objective.Score >= 75f;
            if ((suggested & TacticalSupportCapability.OffensiveGrenade) != 0
                && plan.InteriorPersonnelThreat >= 55f
                && roomCells <= 65
                && !preservesObjective)
            {
                return TacticalSupportCapability.OffensiveGrenade;
            }
            if ((suggested & TacticalSupportCapability.Flashbang) != 0
                && (preservesObjective || roomCells < 45))
            {
                return TacticalSupportCapability.Flashbang;
            }
            if ((suggested & TacticalSupportCapability.CsGrenade) != 0)
            {
                return TacticalSupportCapability.CsGrenade;
            }
            return (suggested & TacticalSupportCapability.Flashbang) != 0
                ? TacticalSupportCapability.Flashbang
                : TacticalSupportCapability.None;
        }

        private static TacticalSupportCapability SelectAvailableInteriorSupport(
            TacticalSupportCapability preferred,
            TacticalSupportCapability usable)
        {
            if ((usable & preferred) != 0) return preferred;
            if ((usable & TacticalSupportCapability.Flashbang) != 0) return TacticalSupportCapability.Flashbang;
            if ((usable & TacticalSupportCapability.CsGrenade) != 0) return TacticalSupportCapability.CsGrenade;
            if ((usable & TacticalSupportCapability.OffensiveGrenade) != 0) return TacticalSupportCapability.OffensiveGrenade;
            return TacticalSupportCapability.None;
        }

        private static List<TacticalBreachStep> FindCoordinatedFlankBreaches(
            Map map,
            MapComponent_TacticalMapAnalysis analysis,
            KillzoneRecord killzone,
            IntVec3 objective,
            Faction attacker)
        {
            CellRect search = killzone.Bounds.ExpandedBy(10).ClipInsideMap(map);
            Vector2 threatDirection = killzone.DominantThreatDirection;
            List<KeyValuePair<Building, float>> ranked = new List<KeyValuePair<Building, float>>();
            foreach (Building building in map.listerThings.AllThings.OfType<Building>())
            {
                if (!search.Contains(building.Position)
                    || !BreachExplosiveUtility.IsValidWall(building)) continue;
                if (building.Faction != null
                    && (attacker == null
                        ? building.Faction != Faction.OfPlayer
                        : !building.HostileTo(attacker))) continue;
                if (!TryBreachNormal(map, building.Position, out Vector2 normal)) continue;

                float lateralQuality = 1f - Mathf.Abs(Vector2.Dot(normal, threatDirection));
                if (lateralQuality < 0.45f) continue;
                float localThreat = MeanCachedThreatAround(map, analysis, building.Position, 2);
                float objectiveDistance = building.Position.DistanceTo(objective);
                float score = lateralQuality * 55f - localThreat * 0.65f - objectiveDistance * 0.22f;
                ranked.Add(new KeyValuePair<Building, float>(building, score));
            }

            List<TacticalBreachStep> selected = new List<TacticalBreachStep>();
            foreach (KeyValuePair<Building, float> candidate in ranked.OrderByDescending(pair => pair.Value))
            {
                if (selected.Any(step => step.Cell.DistanceToSquared(candidate.Key.Position) < 36f)) continue;
                int c4 = BreachExplosiveUtility.RequiredC4For(candidate.Key);
                selected.Add(new TacticalBreachStep
                {
                    Cell = candidate.Key.Position,
                    Target = candidate.Key,
                    Method = TacticalBreachMethod.ExplosiveCharge,
                    RequiredC4 = c4,
                    Cost = c4 * 7f
                });
                if (selected.Count >= 3) break;
            }
            return selected;
        }

        private static bool TryBreachNormal(Map map, IntVec3 wall, out Vector2 normal)
        {
            if ((wall + IntVec3.North).InBounds(map) && (wall + IntVec3.South).InBounds(map)
                && (wall + IntVec3.North).Standable(map) && (wall + IntVec3.South).Standable(map))
            {
                normal = Vector2.up;
                return true;
            }
            if ((wall + IntVec3.East).InBounds(map) && (wall + IntVec3.West).InBounds(map)
                && (wall + IntVec3.East).Standable(map) && (wall + IntVec3.West).Standable(map))
            {
                normal = Vector2.right;
                return true;
            }
            normal = Vector2.zero;
            return false;
        }

        private static float MeanCachedThreatAround(
            Map map,
            MapComponent_TacticalMapAnalysis analysis,
            IntVec3 center,
            int radius)
        {
            List<IntVec3> cells = GenRadial.RadialCellsAround(center, radius, true)
                .Where(cell => cell.InBounds(map))
                .ToList();
            return cells.Count == 0 ? 0f : cells.Average(analysis.CachedThreatAt);
        }

        private static bool IsInsideObjectiveArea(Pawn pawn, Room objectiveRoom, IntVec3 objective)
        {
            return objectiveRoom != null
                ? pawn.Position.GetRoom(pawn.Map) == objectiveRoom
                : pawn.Position.DistanceToSquared(objective) <= 100f;
        }

        private static bool IsHostileDefender(Pawn pawn, Faction attacker)
        {
            return pawn.Faction != null
                && (attacker == null ? pawn.Faction == Faction.OfPlayer : pawn.HostileTo(attacker));
        }

        private static float PersonnelThreat(Pawn pawn)
        {
            float value = 5f;
            value += pawn.equipment?.Primary != null ? 10f : 0f;
            value += (pawn.skills?.GetSkill(SkillDefOf.Shooting).Level ?? 0) * 0.75f;
            value += (pawn.skills?.GetSkill(SkillDefOf.Melee).Level ?? 0) * 0.45f;
            return value;
        }

        private static IntVec3 SelectAttackCell(
            Map map,
            MapComponent_TacticalMapAnalysis analysis,
            TacticalAssaultPlan plan)
        {
            TacticalBreachStep firstBreach = plan.Breaches.FirstOrDefault();
            if (firstBreach != null) return firstBreach.Cell;

            foreach (IntVec3 cell in plan.Route)
            {
                if (analysis.Killzones.Any(zone => zone.Contains(map, cell))) return cell;
                Room room = cell.GetRoom(map);
                if (room != null && !room.PsychologicallyOutdoors) return cell;
            }
            return plan.RouteEndCell.IsValid ? plan.RouteEndCell : plan.InsertionCell;
        }

        private static StrategicTarget SelectObjective(
            MapComponent_TacticalMapAnalysis analysis,
            TacticalAssaultMode mode,
            IntVec3 from,
            Faction attacker)
        {
            return analysis.StrategicTargets
                .Where(target => target.Cell.IsValid)
                .OrderByDescending(target => ObjectiveScore(target, mode, from, analysis, attacker))
                .FirstOrDefault();
        }

        private static float ObjectiveScore(
            StrategicTarget target,
            TacticalAssaultMode mode,
            IntVec3 from,
            MapComponent_TacticalMapAnalysis analysis,
            Faction attacker)
        {
            float multiplier = 1f;
            if (mode == TacticalAssaultMode.Sabotage)
            {
                switch (target.Kind)
                {
                    case StrategicTargetKind.PowerGeneration: multiplier = 1.65f; break;
                    case StrategicTargetKind.PowerStorage: multiplier = 1.4f; break;
                    case StrategicTargetKind.Communications: multiplier = 1.55f; break;
                    case StrategicTargetKind.DefensiveControl: multiplier = 1.3f; break;
                    case StrategicTargetKind.Bedroom: multiplier = 0.35f; break;
                    case StrategicTargetKind.Warehouse: multiplier = 0.8f; break;
                }
            }
            else
            {
                switch (target.Kind)
                {
                    case StrategicTargetKind.Communications: multiplier = 1.35f; break;
                    case StrategicTargetKind.DefensiveControl: multiplier = 1.2f; break;
                    case StrategicTargetKind.Medical: multiplier = 1.15f; break;
                }
            }

            float distance = from.IsValid ? from.DistanceTo(target.Cell) * 0.25f : 0f;
            return target.Score * multiplier - analysis.CachedThreatAt(target.Cell) * 0.45f - distance;
        }

        private static StrategicTarget FindObjectiveAt(MapComponent_TacticalMapAnalysis analysis, IntVec3 cell)
        {
            return analysis.StrategicTargets
                .Where(target => target.Bounds.Contains(cell))
                .OrderByDescending(target => target.Score)
                .FirstOrDefault();
        }

        private static List<StartCandidate> HeliborneStarts(
            Map map,
            MapComponent_TacticalMapAnalysis analysis,
            IntVec3 objective,
            IntVec3 requestedStart,
            Faction attacker)
        {
            List<StartCandidate> result = new List<StartCandidate>();
            if (requestedStart.IsValid)
            {
                IntVec3 hover = requestedStart + IntVec3.North * Mh60HeliborneUtility.RopeLandingDistance;
                if (requestedStart.InBounds(map) && requestedStart.Standable(map)
                    && Mh60HeliborneUtility.IsValidHoverCell(hover, map))
                {
                    result.Add(new StartCandidate { Landing = requestedStart, Hover = hover });
                }
                return result;
            }

            if (analysis.TryFindAirInsertionCell(
                attacker,
                objective,
                7,
                32,
                landing => Mh60HeliborneUtility.IsValidHoverCell(
                    landing + IntVec3.North * Mh60HeliborneUtility.RopeLandingDistance,
                    map),
                out IntVec3 selectedLanding))
            {
                result.Add(new StartCandidate
                {
                    Landing = selectedLanding,
                    Hover = selectedLanding + IntVec3.North * Mh60HeliborneUtility.RopeLandingDistance
                });
            }
            return result;
        }

        private static List<StartCandidate> SabotageStarts(
            Map map,
            MapComponent_TacticalMapAnalysis analysis,
            IntVec3 objective,
            IntVec3 requestedStart,
            Faction attacker)
        {
            if (requestedStart.IsValid)
            {
                return requestedStart.InBounds(map) && requestedStart.Standable(map)
                    ? new List<StartCandidate> { new StartCandidate { Landing = requestedStart, Hover = IntVec3.Invalid } }
                    : new List<StartCandidate>();
            }

            return map.AllCells
                .Where(cell => cell.Standable(map)
                    && (cell.x <= 2 || cell.z <= 2 || cell.x >= map.Size.x - 3 || cell.z >= map.Size.z - 3))
                .Select(cell => new
                {
                    Cell = cell,
                    Score = cell.DistanceTo(objective) + analysis.CachedThreatAt(cell) * 0.8f
                })
                .OrderBy(candidate => candidate.Score)
                .Take(EdgeCandidateLimit)
                .Select(candidate => new StartCandidate { Landing = candidate.Cell, Hover = IntVec3.Invalid })
                .ToList();
        }

        private static TacticalAssaultPlan FindRoute(
            Map map,
            MapComponent_TacticalMapAnalysis analysis,
            List<StartCandidate> starts,
            IntVec3 goal,
            TacticalBreachCapability capabilities,
            Faction attacker,
            out StartCandidate selectedStart)
        {
            selectedStart = default(StartCandidate);
            TacticalAssaultPlan result = new TacticalAssaultPlan();
            int cellCount = map.cellIndices.NumGridCells;
            float[] costs = new float[cellCount];
            int[] previous = new int[cellCount];
            TacticalBreachMethod[] breachMethods = new TacticalBreachMethod[cellCount];
            float[] breachCosts = new float[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                costs[i] = float.PositiveInfinity;
                previous[i] = -1;
            }

            int goalIndex = map.cellIndices.CellToIndex(goal);
            MinHeap open = new MinHeap();
            HashSet<int> startIndices = new HashSet<int>();
            foreach (StartCandidate start in starts)
            {
                int startIndex = map.cellIndices.CellToIndex(start.Landing);
                if (!startIndices.Add(startIndex)) continue;
                costs[startIndex] = 0f;
                open.Push(startIndex, Heuristic(start.Landing, goal));
            }
            int expanded = 0;

            while (open.Count > 0 && expanded < cellCount * 2)
            {
                HeapEntry entry = open.Pop();
                int currentIndex = entry.Index;
                IntVec3 current = map.cellIndices.IndexToCell(currentIndex);
                float expectedPriority = costs[currentIndex] + Heuristic(current, goal);
                if (entry.Priority > expectedPriority + 0.01f) continue;
                expanded++;
                if (currentIndex == goalIndex) break;

                foreach (IntVec3 direction in CardinalDirections)
                {
                    IntVec3 next = current + direction;
                    if (!next.InBounds(map)) continue;
                    if (!TryCellCost(map, analysis, next, capabilities, attacker,
                        out float stepCost, out TacticalBreachMethod method, out float breachCost)) continue;
                    int nextIndex = map.cellIndices.CellToIndex(next);
                    float newCost = costs[currentIndex] + stepCost;
                    if (newCost >= costs[nextIndex]) continue;
                    costs[nextIndex] = newCost;
                    previous[nextIndex] = currentIndex;
                    breachMethods[nextIndex] = method;
                    breachCosts[nextIndex] = breachCost;
                    open.Push(nextIndex, newCost + Heuristic(next, goal));
                }
            }

            result.ExpandedNodes = expanded;
            if (float.IsPositiveInfinity(costs[goalIndex]))
            {
                result.FailureReason = "Path search exhausted the map.";
                return result;
            }

            List<IntVec3> reversed = new List<IntVec3>();
            int cursor = goalIndex;
            while (cursor >= 0)
            {
                reversed.Add(map.cellIndices.IndexToCell(cursor));
                if (startIndices.Contains(cursor)) break;
                cursor = previous[cursor];
            }
            reversed.Reverse();
            IntVec3 selectedLanding = reversed[0];
            selectedStart = starts.First(start => start.Landing == selectedLanding);
            result.Route = reversed;
            result.TotalCost = costs[goalIndex];
            result.ThreatExposure = reversed.Sum(analysis.CachedThreatAt);

            foreach (IntVec3 cell in reversed)
            {
                int index = map.cellIndices.CellToIndex(cell);
                TacticalBreachMethod method = breachMethods[index];
                if (method == TacticalBreachMethod.None) continue;
                Building building = cell.GetEdifice(map);
                result.Breaches.Add(new TacticalBreachStep
                {
                    Cell = cell,
                    Target = building,
                    Method = method,
                    Cost = breachCosts[index],
                    RequiredC4 = method == TacticalBreachMethod.ExplosiveCharge
                        ? BreachExplosiveUtility.RequiredC4For(building)
                        : 0
                });
            }

            foreach (KillzoneRecord zone in analysis.Killzones)
            {
                if (reversed.Any(zoneCell => zone.Contains(map, zoneCell)))
                {
                    result.CrossedKillzones |= zone.Kinds;
                }
            }
            float totalBreachCost = result.Breaches.Sum(step => step.Cost);
            result.Recommendation = analysis.AssessApproach(
                reversed,
                goal,
                totalBreachCost <= 0f ? 9999f : totalBreachCost,
                attacker).Recommendation;
            return result;
        }

        private static bool TryCellCost(
            Map map,
            MapComponent_TacticalMapAnalysis analysis,
            IntVec3 cell,
            TacticalBreachCapability capabilities,
            Faction attacker,
            out float cost,
            out TacticalBreachMethod method,
            out float breachCost)
        {
            method = TacticalBreachMethod.None;
            breachCost = 0f;
            Building edifice = cell.GetEdifice(map);
            if (cell.Standable(map))
            {
                float movementCost = 1f;
                if (edifice is Building_Door door && door.Faction != null
                    && (attacker == null ? door.Faction == Faction.OfPlayer : door.HostileTo(attacker)))
                {
                    float forcedDoorCost = 12f + door.HitPoints / 20f;
                    if (TryBreachCost(door, capabilities, out float equipmentCost,
                        out TacticalBreachMethod equipmentMethod)
                        && equipmentCost < forcedDoorCost)
                    {
                        movementCost += equipmentCost;
                        breachCost = equipmentCost;
                        method = equipmentMethod;
                    }
                    else
                    {
                        movementCost += forcedDoorCost;
                    }
                }
                cost = movementCost + analysis.CachedThreatAt(cell) * ThreatCostFactor;
                return true;
            }

            if (edifice == null)
            {
                cost = 0f;
                return false;
            }

            if (!TryBreachCost(edifice, capabilities, out float best, out method))
            {
                cost = 0f;
                return false;
            }

            breachCost = best;
            cost = best + MeanAdjacentThreat(map, analysis, cell, attacker) * ThreatCostFactor;
            return true;
        }

        private static bool TryBreachCost(
            Building building,
            TacticalBreachCapability capabilities,
            out float best,
            out TacticalBreachMethod method)
        {
            best = float.PositiveInfinity;
            method = TacticalBreachMethod.None;
            if ((capabilities & TacticalBreachCapability.ExplosiveCharge) != 0
                && BreachExplosiveUtility.IsValidWall(building))
            {
                int c4 = BreachExplosiveUtility.RequiredC4For(building);
                CompProperties_InstalledBreachCharge props = BreachExplosiveUtility.ChargeProps;
                float workTicks = Mathf.Max(props?.minimumWorkTicks ?? 150,
                    (props?.workTicksPerC4 ?? 120f) * c4);
                best = 10f + workTicks / 60f + c4 * 7f;
                method = TacticalBreachMethod.ExplosiveCharge;
            }
            if ((capabilities & TacticalBreachCapability.PowerCutter) != 0
                && CompPowerCutterBreach.IsValidBreachTarget(building))
            {
                SimpleCurve curve = PowerCutterProperties()?.nonPlayerWorkTicksByHitPoints;
                float cutterWorkTicks = curve != null
                    ? curve.Evaluate(Mathf.Max(1, building.HitPoints))
                    : 60f + 45f * Mathf.Sqrt(Mathf.Max(1, building.HitPoints));
                float cutter = 8f + cutterWorkTicks / 60f;
                if (cutter < best)
                {
                    best = cutter;
                    method = TacticalBreachMethod.PowerCutter;
                }
            }
            return !float.IsPositiveInfinity(best);
        }

        private static CompProperties_PowerCutterBreach PowerCutterProperties()
        {
            if (cachedPowerCutterProperties == null)
            {
                cachedPowerCutterProperties = DefDatabase<ThingDef>.AllDefsListForReading
                    .Select(def => def.GetCompProperties<CompProperties_PowerCutterBreach>())
                    .FirstOrDefault(properties => properties != null);
            }
            return cachedPowerCutterProperties;
        }

        private static float MeanAdjacentThreat(
            Map map,
            MapComponent_TacticalMapAnalysis analysis,
            IntVec3 cell,
            Faction attacker)
        {
            List<IntVec3> adjacent = CardinalDirections
                .Select(direction => cell + direction)
                .Where(candidate => candidate.InBounds(map))
                .ToList();
            return adjacent.Count == 0 ? 0f : adjacent.Average(analysis.CachedThreatAt);
        }

        private static IntVec3 ResolveStandableGoal(Map map, IntVec3 objective)
        {
            if (objective.Standable(map)) return objective;
            return GenRadial.RadialCellsAround(objective, 8f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map))
                .OrderBy(cell => cell.DistanceToSquared(objective))
                .DefaultIfEmpty(IntVec3.Invalid)
                .First();
        }

        private static float Heuristic(IntVec3 from, IntVec3 to)
        {
            return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.z - to.z);
        }

        private static readonly IntVec3[] CardinalDirections =
        {
            IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West
        };

        private struct StartCandidate
        {
            public IntVec3 Landing;
            public IntVec3 Hover;
        }

        private struct HeapEntry
        {
            public int Index;
            public float Priority;
        }

        private sealed class MinHeap
        {
            private readonly List<HeapEntry> entries = new List<HeapEntry>();
            public int Count => entries.Count;

            public void Push(int index, float priority)
            {
                entries.Add(new HeapEntry { Index = index, Priority = priority });
                int child = entries.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (entries[parent].Priority <= entries[child].Priority) break;
                    HeapEntry swap = entries[parent];
                    entries[parent] = entries[child];
                    entries[child] = swap;
                    child = parent;
                }
            }

            public HeapEntry Pop()
            {
                HeapEntry root = entries[0];
                int last = entries.Count - 1;
                entries[0] = entries[last];
                entries.RemoveAt(last);
                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1;
                    if (left >= entries.Count) break;
                    int right = left + 1;
                    int smallest = right < entries.Count
                        && entries[right].Priority < entries[left].Priority ? right : left;
                    if (entries[parent].Priority <= entries[smallest].Priority) break;
                    HeapEntry swap = entries[parent];
                    entries[parent] = entries[smallest];
                    entries[smallest] = swap;
                    parent = smallest;
                }
                return root;
            }
        }
    }

    public static class TacticalSupportDefNames
    {
        public const string OffensiveGrenade = "HD_Grenade_M111_Item";
        public const string Flashbang = "HD_Grenade_M84_Item";
        public const string CsGrenade = "HD_Grenade_M7A2_Item";
        public const string SmokeGrenade = "HD_Grenade_M8_Item";

        public static string For(TacticalSupportCapability capability)
        {
            switch (capability)
            {
                case TacticalSupportCapability.OffensiveGrenade: return OffensiveGrenade;
                case TacticalSupportCapability.Flashbang: return Flashbang;
                case TacticalSupportCapability.CsGrenade: return CsGrenade;
                case TacticalSupportCapability.SmokeGrenade: return SmokeGrenade;
                default: return null;
            }
        }
    }
}
