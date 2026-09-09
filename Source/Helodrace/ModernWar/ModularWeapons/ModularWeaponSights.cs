using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class ModularSightGroupStatus
    {
        public int index;
        public float axisHeight;
        public int sightCount;
        public bool isActive;
        public bool hasIronFront;
        public bool hasIronRear;
        public bool hasCompleteIronSight;
        public bool ironOrderValid;
        public bool hasMagnifier;
        public bool magnifierBehindPrimary;
        public bool magnifierAheadPrimary;
        public float efficiency;
        public float occlusion;
        public int blockerCount;
        public int belowWeaponSightCount;
        public Thing primary;
        public Thing magnifier;
        public ModularSightKind? primaryKind;
    }

    internal sealed class ModularSightResolution
    {
        public readonly List<ModularSightGroupStatus> groups =
            new List<ModularSightGroupStatus>();
        public readonly Dictionary<int, float> performanceWeights =
            new Dictionary<int, float>();
    }

    internal static class ModularWeaponSightResolver
    {
        private const float OrderEpsilon = 0.002f;
        private const float MinimumUsableAxisHeight = 0f;

        private sealed class SightRecord
        {
            public ModularRenderNode node;
            public ModularWeaponSightProperties props;
            public Vector2 axis;
        }

        private sealed class OccupancyRecord
        {
            public ModularRenderNode node;
            public float centerX;
            public float minZ;
            public float maxZ;
        }

        private sealed class SightGroup
        {
            public readonly List<SightRecord> records = new List<SightRecord>();
            public float heightSum;
            public float AxisHeight => records.Count == 0
                ? 0f
                : heightSum / records.Count;

            public void Add(SightRecord record)
            {
                records.Add(record);
                heightSum += record.axis.y;
            }
        }

        private sealed class Candidate
        {
            public SightRecord primary;
            public SightRecord ironFront;
            public SightRecord ironRear;
            public SightRecord magnifier;
            public float axisX;
            public float axisHeight;
            public float aimRadius;
            public float priority;
            public float efficiency;
            public float score;
            public int blockerCount;
            public bool magnifierAhead;
            public bool IsIron => ironFront != null && ironRear != null;
            public ModularSightKind Kind => IsIron
                ? ModularSightKind.IronRear
                : primary.props.kind;
        }

        public static ModularSightResolution Resolve(
            CompModularWeaponNode root,
            List<ModularRenderNode> nodes)
        {
            ModularSightResolution result = new ModularSightResolution();
            if (root == null || nodes.NullOrEmpty()) return result;

            List<SightRecord> sights = BuildSightRecords(nodes);
            if (sights.Count == 0) return result;
            List<OccupancyRecord> occupancies = BuildOccupancies(nodes);
            List<SightGroup> groups = BuildGroups(
                sights,
                Mathf.Max(0.0001f, root.Props.sightGroupHeightTolerance));
            List<Candidate> winners = new List<Candidate>();

            for (int i = 0; i < groups.Count; i++)
            {
                SightGroup group = groups[i];
                Candidate winner = BestCandidate(group, occupancies);
                winners.Add(winner);
                result.groups.Add(BuildStatus(i, group, winner));
            }

            int activeIndex = -1;
            Candidate active = null;
            for (int i = 0; i < winners.Count; i++)
            {
                Candidate candidate = winners[i];
                if (candidate == null || candidate.score <= 0f) continue;
                if (active == null
                    || candidate.score > active.score + 0.0001f
                    || (Mathf.Approximately(candidate.score, active.score)
                        && candidate.priority > active.priority))
                {
                    active = candidate;
                    activeIndex = i;
                }
            }

            if (active == null) return result;
            result.groups[activeIndex].isActive = true;
            AddWeight(result, active.primary, active.efficiency);
            AddWeight(result, active.ironFront, active.efficiency);
            AddWeight(result, active.ironRear, active.efficiency);
            AddWeight(result, active.magnifier, active.efficiency);
            return result;
        }

        private static List<SightRecord> BuildSightRecords(
            List<ModularRenderNode> nodes)
        {
            List<SightRecord> result = new List<SightRecord>();
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                ModularWeaponSightProperties sight = node.Props.sight;
                if (sight == null || sight.aimRadius <= 0f) continue;
                Vector3 local = node.Props.graphicOffset + sight.axisOffset;
                result.Add(new SightRecord
                {
                    node = node,
                    props = sight,
                    axis = node.transform.TransformPoint(local)
                });
            }
            result.Sort((a, b) => a.axis.y.CompareTo(b.axis.y));
            return result;
        }

        private static List<OccupancyRecord> BuildOccupancies(
            List<ModularRenderNode> nodes)
        {
            List<OccupancyRecord> result = new List<OccupancyRecord>();
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                float height = node.Props.verticalOccupancy;
                if (height <= 0f) continue;
                Vector3 low = node.Props.graphicOffset + new Vector3(
                    0f, 0f, node.Props.verticalOccupancyOffset - height * 0.5f);
                Vector3 high = node.Props.graphicOffset + new Vector3(
                    0f, 0f, node.Props.verticalOccupancyOffset + height * 0.5f);
                Vector2 a = node.transform.TransformPoint(low);
                Vector2 b = node.transform.TransformPoint(high);
                float centerX = (a.x + b.x) * 0.5f;
                if (node.Props.sight != null)
                {
                    Vector3 axisLocal = node.Props.graphicOffset
                        + node.Props.sight.axisOffset;
                    centerX = node.transform.TransformPoint(axisLocal).x;
                }
                else if (node.attachedToRail)
                {
                    centerX = (node.occupiedRailStart.x
                        + node.occupiedRailEnd.x) * 0.5f;
                }
                result.Add(new OccupancyRecord
                {
                    node = node,
                    centerX = centerX,
                    minZ = Mathf.Min(a.y, b.y),
                    maxZ = Mathf.Max(a.y, b.y)
                });
            }
            return result;
        }

        private static List<SightGroup> BuildGroups(
            List<SightRecord> sights,
            float tolerance)
        {
            List<SightGroup> groups = new List<SightGroup>();
            for (int i = 0; i < sights.Count; i++)
            {
                SightRecord sight = sights[i];
                SightGroup best = null;
                float bestDistance = float.MaxValue;
                for (int j = 0; j < groups.Count; j++)
                {
                    float distance = Mathf.Abs(groups[j].AxisHeight - sight.axis.y);
                    if (distance <= tolerance && distance < bestDistance)
                    {
                        best = groups[j];
                        bestDistance = distance;
                    }
                }
                if (best == null)
                {
                    best = new SightGroup();
                    groups.Add(best);
                }
                best.Add(sight);
            }
            groups.Sort((a, b) => a.AxisHeight.CompareTo(b.AxisHeight));
            return groups;
        }

        private static Candidate BestCandidate(
            SightGroup group,
            List<OccupancyRecord> occupancies)
        {
            List<Candidate> candidates = new List<Candidate>();
            List<SightRecord> fronts = group.records
                .Where(r => IsUsable(r)
                    && r.props.kind == ModularSightKind.IronFront)
                .OrderByDescending(r => r.axis.x).ToList();
            List<SightRecord> rears = group.records
                .Where(r => IsUsable(r)
                    && r.props.kind == ModularSightKind.IronRear)
                .OrderBy(r => r.axis.x).ToList();

            if (fronts.Count > 0 && rears.Count > 0
                && fronts[0].axis.x > rears[0].axis.x + OrderEpsilon)
            {
                SightRecord front = fronts[0];
                SightRecord rear = rears[0];
                candidates.Add(new Candidate
                {
                    primary = rear,
                    ironFront = front,
                    ironRear = rear,
                    axisX = rear.axis.x,
                    axisHeight = (front.axis.y + rear.axis.y) * 0.5f,
                    aimRadius = Mathf.Min(front.props.aimRadius, rear.props.aimRadius),
                    priority = Mathf.Max(
                        front.props.selectionPriority,
                        rear.props.selectionPriority)
                });
            }

            for (int i = 0; i < group.records.Count; i++)
            {
                SightRecord record = group.records[i];
                if (!IsUsable(record)
                    || record.props.kind == ModularSightKind.Magnifier
                    || record.props.kind == ModularSightKind.IronFront
                    || record.props.kind == ModularSightKind.IronRear)
                    continue;
                candidates.Add(new Candidate
                {
                    primary = record,
                    axisX = record.axis.x,
                    axisHeight = record.axis.y,
                    aimRadius = record.props.aimRadius,
                    priority = record.props.selectionPriority
                });
            }

            Candidate best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                ResolveMagnifier(candidate, group);
                candidate.efficiency = Visibility(
                    candidate,
                    group,
                    occupancies,
                    out candidate.blockerCount);
                candidate.score = candidate.priority * candidate.efficiency;
                if (best == null
                    || candidate.score > best.score + 0.0001f
                    || (Mathf.Approximately(candidate.score, best.score)
                        && candidate.priority > best.priority))
                    best = candidate;
            }
            return best;
        }

        private static void ResolveMagnifier(Candidate candidate, SightGroup group)
        {
            bool compatible = candidate.Kind == ModularSightKind.Holographic
                || candidate.Kind == ModularSightKind.Reflex;
            SightRecord closestBehind = null;
            for (int i = 0; i < group.records.Count; i++)
            {
                SightRecord record = group.records[i];
                if (!IsUsable(record)
                    || record.props.kind != ModularSightKind.Magnifier)
                    continue;
                if (record.axis.x > candidate.axisX + OrderEpsilon)
                    candidate.magnifierAhead = true;
                if (!compatible || record.axis.x >= candidate.axisX - OrderEpsilon)
                    continue;
                if (closestBehind == null || record.axis.x > closestBehind.axis.x)
                    closestBehind = record;
            }
            candidate.magnifier = closestBehind;
        }

        private static float Visibility(
            Candidate candidate,
            SightGroup group,
            List<OccupancyRecord> occupancies,
            out int blockerCount)
        {
            blockerCount = 0;
            float minAim = candidate.axisHeight - candidate.aimRadius;
            float maxAim = candidate.axisHeight + candidate.aimRadius;
            if (maxAim <= minAim) return 0f;

            HashSet<int> excluded = new HashSet<int>();
            AddExcluded(excluded, candidate.primary);
            AddExcluded(excluded, candidate.ironFront);
            AddExcluded(excluded, candidate.ironRear);

            // Complete co-witness irons in the same optical-height group are a valid backup,
            // not an obstruction to a holographic/reflex/scope primary.
            List<SightRecord> ironFronts = group.records.Where(
                r => IsUsable(r)
                    && r.props.kind == ModularSightKind.IronFront).ToList();
            List<SightRecord> ironRears = group.records.Where(
                r => IsUsable(r)
                    && r.props.kind == ModularSightKind.IronRear).ToList();
            bool completeIronPair = ironFronts.Count > 0
                && ironRears.Count > 0
                && ironFronts.Max(r => r.axis.x)
                    > ironRears.Min(r => r.axis.x) + OrderEpsilon;
            if (completeIronPair)
            {
                for (int i = 0; i < group.records.Count; i++)
                {
                    if (!IsUsable(group.records[i])) continue;
                    ModularSightKind kind = group.records[i].props.kind;
                    if (kind == ModularSightKind.IronFront
                        || kind == ModularSightKind.IronRear)
                        AddExcluded(excluded, group.records[i]);
                }
            }

            List<Vector2> intervals = new List<Vector2>();
            for (int i = 0; i < occupancies.Count; i++)
            {
                OccupancyRecord blocker = occupancies[i];
                if (excluded.Contains(blocker.node.thing.thingIDNumber)
                    || blocker.centerX <= candidate.axisX + OrderEpsilon)
                    continue;
                float low = Mathf.Max(minAim, blocker.minZ);
                float high = Mathf.Min(maxAim, blocker.maxZ);
                if (high <= low) continue;
                intervals.Add(new Vector2(low, high));
                blockerCount++;
            }
            if (intervals.Count == 0) return 1f;

            intervals.Sort((a, b) => a.x.CompareTo(b.x));
            float covered = 0f;
            float currentLow = intervals[0].x;
            float currentHigh = intervals[0].y;
            for (int i = 1; i < intervals.Count; i++)
            {
                Vector2 interval = intervals[i];
                if (interval.x <= currentHigh)
                {
                    currentHigh = Mathf.Max(currentHigh, interval.y);
                    continue;
                }
                covered += currentHigh - currentLow;
                currentLow = interval.x;
                currentHigh = interval.y;
            }
            covered += currentHigh - currentLow;
            return Mathf.Clamp01(1f - covered / (maxAim - minAim));
        }

        private static ModularSightGroupStatus BuildStatus(
            int index,
            SightGroup group,
            Candidate winner)
        {
            bool hasFront = group.records.Any(
                r => IsUsable(r)
                    && r.props.kind == ModularSightKind.IronFront);
            bool hasRear = group.records.Any(
                r => IsUsable(r)
                    && r.props.kind == ModularSightKind.IronRear);
            bool ironOrder = false;
            if (hasFront && hasRear)
            {
                float frontX = group.records
                    .Where(r => IsUsable(r)
                        && r.props.kind == ModularSightKind.IronFront)
                    .Max(r => r.axis.x);
                float rearX = group.records
                    .Where(r => IsUsable(r)
                        && r.props.kind == ModularSightKind.IronRear)
                    .Min(r => r.axis.x);
                ironOrder = frontX > rearX + OrderEpsilon;
            }

            ModularSightGroupStatus status = new ModularSightGroupStatus
            {
                index = index,
                axisHeight = group.AxisHeight,
                sightCount = group.records.Count,
                belowWeaponSightCount = group.records.Count(r => !IsUsable(r)),
                hasIronFront = hasFront,
                hasIronRear = hasRear,
                hasCompleteIronSight = hasFront && hasRear && ironOrder,
                ironOrderValid = ironOrder,
                hasMagnifier = group.records.Any(
                    r => IsUsable(r)
                        && r.props.kind == ModularSightKind.Magnifier)
            };
            if (winner == null) return status;
            status.primary = winner.primary?.node.thing;
            status.primaryKind = winner.Kind;
            status.magnifier = winner.magnifier?.node.thing;
            status.magnifierBehindPrimary = winner.magnifier != null;
            status.magnifierAheadPrimary = winner.magnifierAhead;
            status.efficiency = winner.efficiency;
            status.occlusion = 1f - winner.efficiency;
            status.blockerCount = winner.blockerCount;
            return status;
        }

        private static bool IsUsable(SightRecord record)
        {
            return record != null
                && record.axis.y >= MinimumUsableAxisHeight;
        }

        private static void AddExcluded(HashSet<int> excluded, SightRecord record)
        {
            if (record?.node?.thing != null)
                excluded.Add(record.node.thing.thingIDNumber);
        }

        private static void AddWeight(
            ModularSightResolution result,
            SightRecord record,
            float weight)
        {
            if (record?.node?.thing == null) return;
            int id = record.node.thing.thingIDNumber;
            float current;
            result.performanceWeights.TryGetValue(id, out current);
            result.performanceWeights[id] = Mathf.Max(current, Mathf.Clamp01(weight));
        }
    }
}
