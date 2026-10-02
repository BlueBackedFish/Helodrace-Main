using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public enum RaidTacticalTask
    {
        Entry,
        Security,
        FireSupport,
        Withdraw,
        Response,
        Recon
    }

    public sealed class RaidTacticalAssignment
    {
        public Pawn Pawn;
        public RaidTacticalTask Task;
        public IntVec3 Position;
        public int EntryOrder;
    }

    public sealed class RaidTacticalPlan
    {
        public string OrganizationId;
        public RaidTacticalDoctrine Doctrine;
        public IntVec3 Start;
        public IntVec3 Objective;
        public IntVec3 Frontline;
        public IntVec3 Flank;
        public IntVec3 Entry;
        public List<IntVec3> ApproachNodes = new List<IntVec3>();
        public List<RaidTacticalOption> Options = new List<RaidTacticalOption>();
        public RaidTacticalOption Selected;
        public List<RaidTacticalAssignment> Assignments = new List<RaidTacticalAssignment>();
        public string EntrySupport;
        public string EntryMethod;
        public string Reason;
        public float CommandEfficiency;
        public float CasualtyFraction;
        public int EntryDelayTicks;
        public int CoordinationDelayTicks;
        public int PlannedTick;
        public bool Success => Selected != null;
    }

    public static class RaidTacticalPlanner
    {
        public static RaidTacticalPlan MakePlan(Map map, CombatOrganization organization)
        {
            var plan = new RaidTacticalPlan { OrganizationId = organization?.id,
                PlannedTick = Find.TickManager?.TicksGame ?? 0 };
            if (map == null || organization == null)
            {
                plan.Reason = "No map or combat organization.";
                return plan;
            }

            List<Pawn> members = organization.AllMembers.Where(pawn => pawn.Spawned
                && pawn.Map == map && !pawn.Dead && !pawn.Downed && !pawn.Destroyed).ToList();
            if (members.Count == 0)
            {
                plan.Reason = "No available raid members on this map.";
                return plan;
            }

            MapComponent_TacticalMapAnalysis analysis = map.GetComponent<MapComponent_TacticalMapAnalysis>();
            if (analysis == null)
            {
                plan.Reason = "Tactical map analysis is unavailable.";
                return plan;
            }
            analysis.RequestAnalysis(organization.faction, 1800);
            plan.Doctrine = organization.faction?.def?.defName == "HD_HelodCivilHighFaction"
                ? RaidTacticalDoctrine.High : RaidTacticalDoctrine.Low;
            IntVec3 center = Center(members.Select(pawn => pawn.Position));
            Pawn pathfinder = members.OrderBy(pawn => pawn.Position.DistanceToSquared(center)).First();
            plan.Start = pathfinder.Position;
            StrategicTarget target = analysis.BestObjective(organization.faction, plan.Start);
            List<Pawn> hostiles = map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.Faction != null
                && pawn.Faction.HostileTo(organization.faction) && !pawn.Dead).ToList();
            plan.Objective = target?.Cell ?? (hostiles.Count > 0
                ? Center(hostiles.Select(pawn => pawn.Position)) : IntVec3.Invalid);
            if (!plan.Objective.IsValid)
            {
                plan.Reason = "No defender or strategic objective was found.";
                return plan;
            }
            bool detectTraps = plan.Doctrine == RaidTacticalDoctrine.High;
            plan.Entry = FindEntry(map, analysis, plan.Objective, pathfinder, detectTraps);
            if (!plan.Entry.IsValid)
            {
                plan.Reason = "No usable entry cell was found near the objective.";
                return plan;
            }

            List<IntVec3> direct = SampleLine(map, plan.Start, plan.Entry);
            if (direct.Count == 0)
            {
                plan.Reason = "No walking route reaches the objective entrance.";
                return plan;
            }
            plan.Frontline = direct.OrderByDescending(cell =>
                    VisibleThreat(analysis.CachedAt(cell), detectTraps))
                .FirstOrDefault();
            if (!plan.Frontline.IsValid) plan.Frontline = plan.Entry;
            plan.Flank = FindFlank(map, analysis, pathfinder, plan.Start, plan.Entry, detectTraps);
            List<IntVec3> flankRoute = plan.Flank.IsValid
                ? SampleLine(map, plan.Start, plan.Flank)
                    .Concat(SampleLine(map, plan.Flank, plan.Entry)).Distinct().ToList()
                : new List<IntVec3>();
            float directThreat = MeanThreat(analysis, direct, detectTraps);
            float flankThreat = MeanThreat(analysis, flankRoute, detectTraps);
            int originalPersonnel = organization.rootGroups.Sum(root => root.formation?.StandardPersonnel ?? 0);
            plan.CasualtyFraction = originalPersonnel == 0 ? 0f
                : Mathf.Clamp01(1f - members.Count / (float)originalPersonnel);
            plan.CommandEfficiency = organization.rootGroups.Count == 0 ? 1f
                : organization.rootGroups.Min(root => root.CommandEfficiency);

            List<Thing> grenades = members.SelectMany(InventoryGrenadeUtility.GrenadeStacks).ToList();
            bool smoke = grenades.Any(item => item.def.defName == "HD_Grenade_M8_Item"
                || item.def.weaponTags?.Contains("GrenadeSmoke") == true);
            bool lethal = grenades.Any(item => item.def.defName == "HD_Grenade_MKII"
                || item.def.defName == "HD_Grenade_MKIII"
                || item.def.weaponTags?.Contains("GrenadeDestructive") == true);
            bool nonlethal = grenades.Any(item => item.def.defName == "HD_Grenade_M84_Item"
                || item.def.defName == "HD_Grenade_M7A2_Item");
            bool breachTool = members.Any(pawn => pawn.equipment?.Primary
                ?.TryGetComp<CompPowerCutterBreach>() != null)
                || members.Any(pawn => pawn.inventory?.innerContainer
                    ?.Any(item => item.def.defName == "HD_C4_Charge") == true);
            Room room = plan.Objective.GetRoom(map);
            bool indoor = room != null && !room.PsychologicallyOutdoors;
            bool friendlyInside = indoor && map.mapPawns.AllPawnsSpawned.Any(pawn =>
                pawn.Faction == organization.faction && pawn.Position.GetRoom(map) == room);
            bool mineWarning = detectTraps
                && direct.Any(cell => analysis.CachedAt(cell).TrapThreat > 1f);
            bool nearbyHostile = map.mapPawns.AllPawnsSpawned.Any(pawn => pawn.Faction != null
                && pawn.Faction.HostileTo(organization.faction)
                && pawn.Position.DistanceTo(plan.Start) <= 18f);
            var situation = new RaidTacticalSituation
            {
                Doctrine = plan.Doctrine,
                Defending = plan.CasualtyFraction >= 0.35f
                    || (nearbyHostile && directThreat >= 50f),
                FlankAvailable = plan.Flank.IsValid,
                EntryAvailable = indoor,
                BreachToolAvailable = breachTool,
                SmokeAvailable = smoke,
                LethalGrenadeAvailable = lethal,
                NonlethalGrenadeAvailable = nonlethal,
                FriendlyInsideObjective = friendlyInside,
                IndoorObjective = indoor,
                MineWarning = mineWarning,
                DirectThreat = directThreat,
                FlankThreat = flankThreat,
                CasualtyFraction = plan.CasualtyFraction,
                CommandEfficiency = plan.CommandEfficiency
            };
            plan.Options = RaidTacticalDecision.Rank(situation);
            plan.Selected = RaidTacticalDecision.Select(plan.Options, plan.CommandEfficiency);
            if (plan.Selected == null)
            {
                plan.Reason = "No viable maneuver was found.";
                return plan;
            }
            plan.ApproachNodes.Add(plan.Start);
            if (plan.Selected.Maneuver == RaidTacticalManeuver.FlankAttack)
                plan.ApproachNodes.Add(plan.Flank);
            else if (plan.Frontline != plan.Start && plan.Frontline != plan.Entry)
                plan.ApproachNodes.Add(plan.Frontline);
            if (plan.Selected.Maneuver != RaidTacticalManeuver.HoldAndCounterattack
                && plan.Selected.Maneuver != RaidTacticalManeuver.Regroup)
            {
                plan.ApproachNodes.Add(plan.Entry);
                plan.ApproachNodes.Add(plan.Objective);
            }
            plan.EntrySupport = RaidTacticalDecision.EntrySupport(plan.Selected.Maneuver, situation);
            plan.EntryMethod = plan.Entry.GetEdifice(map) is Building_Door
                ? "Door" : breachTool ? "Breach equipment available" : "Open approach";
            plan.EntryDelayTicks = plan.Doctrine == RaidTacticalDoctrine.High ? 90
                : plan.Selected.Maneuver == RaidTacticalManeuver.CoordinatedEntry
                    && lethal && !friendlyInside ? 300
                : plan.Selected.Maneuver == RaidTacticalManeuver.SmokeAdvance ? 120 : 30;
            bool separated = members.Any(pawn => pawn.Position.DistanceTo(plan.Start) > 12f);
            bool outOfSight = members.Any(pawn => pawn.Position != plan.Start
                && !GenSight.LineOfSight(plan.Start, pawn.Position, map, true));
            plan.CoordinationDelayTicks = plan.Doctrine == RaidTacticalDoctrine.High ? 30
                : 60 + (separated ? 60 : 0) + (outOfSight ? 60 : 0);
            AssignPositions(map, analysis, organization, members, plan);
            if (plan.Assignments.Count != members.Count
                || plan.Assignments.Any(assignment => !assignment.Position.IsValid))
            {
                plan.Selected = null;
                plan.Reason = "Not enough reachable staging and withdrawal cells for this group.";
                return plan;
            }
            plan.Reason = plan.Selected.Reason;
            return plan;
        }

        private static void AssignPositions(Map map, MapComponent_TacticalMapAnalysis analysis,
            CombatOrganization organization, List<Pawn> members, RaidTacticalPlan plan)
        {
            var occupied = new HashSet<IntVec3>();
            List<Pawn> wounded = members.Where(pawn => pawn.health?.summaryHealth
                ?.SummaryHealthPercent < 0.35f).ToList();
            foreach (Pawn pawn in wounded)
            {
                IntVec3 cell = FindRetreatCell(map, analysis, pawn,
                    plan.Start, plan.Objective, occupied,
                    plan.Doctrine == RaidTacticalDoctrine.High);
                if (cell.IsValid) occupied.Add(cell);
                plan.Assignments.Add(new RaidTacticalAssignment
                { Pawn = pawn, Task = RaidTacticalTask.Withdraw, Position = cell });
            }
            members = members.Except(wounded).ToList();
            if (members.Count == 0) return;
            if (plan.Selected.Maneuver == RaidTacticalManeuver.HoldAndCounterattack
                || plan.Selected.Maneuver == RaidTacticalManeuver.Regroup
                || plan.Selected.Maneuver == RaidTacticalManeuver.ReconAndClear)
            {
                bool hold = plan.Selected.Maneuver == RaidTacticalManeuver.HoldAndCounterattack;
                bool recon = plan.Selected.Maneuver == RaidTacticalManeuver.ReconAndClear;
                int responseCount = hold ? Math.Max(1, members.Count / 3) : 0;
                List<Pawn> response = members.Where(pawn => !IsFireSupport(organization, pawn))
                    .Take(responseCount).ToList();
                Pawn scout = recon ? members.FirstOrDefault(pawn => !IsFireSupport(organization, pawn))
                    : null;
                foreach (Pawn pawn in members)
                {
                    IntVec3 cell = FindStagingCell(map, analysis,
                        pawn, pawn == scout ? plan.Frontline : plan.Start, plan.Start,
                        occupied, plan.Doctrine == RaidTacticalDoctrine.High, 2, 9);
                    if (cell.IsValid) occupied.Add(cell);
                    plan.Assignments.Add(new RaidTacticalAssignment
                    {
                        Pawn = pawn,
                        Task = pawn == scout ? RaidTacticalTask.Recon
                            : response.Contains(pawn) ? RaidTacticalTask.Response
                            : IsFireSupport(organization, pawn) ? RaidTacticalTask.FireSupport
                            : RaidTacticalTask.Security,
                        Position = cell
                    });
                }
                return;
            }
            Pawn leader = organization.rootGroups.Select(root => root.EffectiveCommander)
                .FirstOrDefault(pawn => pawn != null && members.Contains(pawn));
            List<Pawn> entry = members.Where(pawn => pawn != leader &&
                !IsFireSupport(organization, pawn)).OrderBy(pawn => pawn.thingIDNumber).ToList();
            if (leader != null) entry.Insert(Math.Min(1, entry.Count), leader);
            if (entry.Count == 0) entry.Add(members[0]);
            List<Pawn> support = members.Except(entry).ToList();
            int securityCount = Math.Max(1, members.Count / 4);
            while (entry.Count > 2 && support.Count < securityCount)
            {
                Pawn rear = entry[entry.Count - 1];
                entry.RemoveAt(entry.Count - 1);
                support.Add(rear);
            }

            for (int i = 0; i < entry.Count; i++)
            {
                IntVec3 cell = FindStagingCell(map, analysis, entry[i], plan.Entry, plan.Start,
                    occupied, plan.Doctrine == RaidTacticalDoctrine.High, 3, 8);
                if (cell.IsValid) occupied.Add(cell);
                plan.Assignments.Add(new RaidTacticalAssignment
                { Pawn = entry[i], Task = RaidTacticalTask.Entry, Position = cell, EntryOrder = i + 1 });
            }
            foreach (Pawn pawn in support)
            {
                IntVec3 cell = FindStagingCell(map, analysis, pawn, plan.Frontline,
                    plan.Start, occupied, plan.Doctrine == RaidTacticalDoctrine.High, 4, 11);
                if (cell.IsValid) occupied.Add(cell);
                plan.Assignments.Add(new RaidTacticalAssignment
                {
                    Pawn = pawn,
                    Task = IsFireSupport(organization, pawn) ? RaidTacticalTask.FireSupport
                        : RaidTacticalTask.Security,
                    Position = cell
                });
            }
        }

        private static bool IsFireSupport(CombatOrganization organization, Pawn pawn)
        {
            string function = organization.AllGroups.SelectMany(group => group.roleAssignments)
                .FirstOrDefault(assignment => assignment.pawn == pawn)?.combatRole?.combatFunction;
            return function == "MachineGun" || function == "Sniper" || function == "Bazooka"
                || function == "RocketSupply";
        }

        private static IntVec3 FindEntry(Map map, MapComponent_TacticalMapAnalysis analysis,
            IntVec3 objective, Pawn pathfinder, bool detectTraps)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(objective, 9f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map))
                .OrderBy(cell => cell.DistanceTo(objective) * 4f
                    + cell.DistanceTo(pathfinder.Position) * 0.3f
                    + VisibleThreat(analysis.CachedAt(cell), detectTraps) * 0.12f
                    - (cell.GetEdifice(map) is Building_Door ? 18f : 0f))
                .Take(64))
                if (map.reachability.CanReach(pathfinder.Position, cell,
                    PathEndMode.OnCell, TraverseParms.For(pathfinder)))
                    return cell;
            return IntVec3.Invalid;
        }

        private static IntVec3 FindFlank(Map map, MapComponent_TacticalMapAnalysis analysis,
            Pawn pathfinder, IntVec3 start, IntVec3 entry, bool detectTraps)
        {
            float dx = entry.x - start.x;
            float dz = entry.z - start.z;
            float length = Mathf.Max(1f, Mathf.Sqrt(dx * dx + dz * dz));
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MaxValue;
            foreach (int side in new[] { -1, 1 })
                foreach (int distance in new[] { 8, 12, 16 })
                {
                    var cell = new IntVec3(Mathf.RoundToInt((start.x + entry.x) / 2f - side * dz / length * distance),
                        0, Mathf.RoundToInt((start.z + entry.z) / 2f + side * dx / length * distance));
                    if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                    if (!map.reachability.CanReach(start, cell, PathEndMode.OnCell,
                        TraverseParms.For(pathfinder))
                        || !map.reachability.CanReach(cell, entry, PathEndMode.OnCell,
                            TraverseParms.For(pathfinder))) continue;
                    List<IntVec3> first = SampleLine(map, start, cell);
                    List<IntVec3> second = SampleLine(map, cell, entry);
                    float score = MeanThreat(analysis, first.Concat(second), detectTraps)
                        + (first.Count + second.Count) * 0.25f;
                    if (score >= bestScore) continue;
                    best = cell;
                    bestScore = score;
                }
            return best;
        }

        private static IntVec3 FindStagingCell(Map map, MapComponent_TacticalMapAnalysis analysis,
            Pawn pawn, IntVec3 anchor, IntVec3 rear, HashSet<IntVec3> occupied, bool avoidTraps,
            int minimumRadius, int maximumRadius)
        {
            if (!anchor.IsValid) return IntVec3.Invalid;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(anchor, maximumRadius, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && !occupied.Contains(cell) && cell.DistanceTo(anchor) >= minimumRadius)
                .Where(cell => !avoidTraps || analysis.CachedAt(cell).TrapThreat <= 1f)
                .OrderBy(cell =>
                {
                    TacticalCellData data = analysis.CachedAt(cell);
                    return VisibleThreat(data, avoidTraps) * 0.18f + data.ChokeThreat * 0.5f
                        + cell.DistanceTo(anchor) * 0.6f + cell.DistanceTo(rear) * 0.12f;
                }).Take(24))
                if (map.reachability.CanReach(pawn.Position, cell, PathEndMode.OnCell,
                    TraverseParms.For(pawn))) return cell;
            return IntVec3.Invalid;
        }

        private static IntVec3 FindRetreatCell(Map map, MapComponent_TacticalMapAnalysis analysis,
            Pawn pawn, IntVec3 start, IntVec3 objective, HashSet<IntVec3> occupied,
            bool detectTraps)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(start, 14f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && !occupied.Contains(cell) && cell.DistanceTo(start) >= 5f)
                .OrderByDescending(cell => cell.DistanceTo(objective)
                    - VisibleThreat(analysis.CachedAt(cell), detectTraps) * 0.25f
                    - cell.DistanceTo(start) * 0.3f)
                .Take(24))
                if (map.reachability.CanReach(pawn.Position, cell, PathEndMode.OnCell,
                    TraverseParms.For(pawn))) return cell;
            return IntVec3.Invalid;
        }

        private static IntVec3 Center(IEnumerable<IntVec3> cells)
        {
            List<IntVec3> list = cells.ToList();
            return new IntVec3(Mathf.RoundToInt((float)list.Average(cell => cell.x)), 0,
                Mathf.RoundToInt((float)list.Average(cell => cell.z)));
        }

        private static List<IntVec3> SampleLine(Map map, IntVec3 from, IntVec3 to)
        {
            int count = Math.Max(Math.Abs(to.x - from.x), Math.Abs(to.z - from.z));
            var cells = new List<IntVec3>();
            for (int i = 0; i <= count; i++)
            {
                float t = count == 0 ? 0f : i / (float)count;
                var cell = new IntVec3(Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t)), 0,
                    Mathf.RoundToInt(Mathf.Lerp(from.z, to.z, t)));
                if (cell.InBounds(map) && (cells.Count == 0 || cells[cells.Count - 1] != cell))
                    cells.Add(cell);
            }
            return cells;
        }

        private static float MeanThreat(MapComponent_TacticalMapAnalysis analysis,
            IEnumerable<IntVec3> cells, bool detectTraps)
        {
            List<IntVec3> list = cells.ToList();
            return list.Count == 0 ? 0f : list.Average(cell =>
                VisibleThreat(analysis.CachedAt(cell), detectTraps));
        }

        private static float VisibleThreat(TacticalCellData data, bool detectTraps)
        {
            return data.TotalThreat - (detectTraps ? 0f : data.TrapThreat);
        }
    }

    public sealed class MapComponent_RaidTacticalPlans : MapComponent
    {
        private readonly Dictionary<string, RaidTacticalPlan> plans = new Dictionary<string, RaidTacticalPlan>();
        private readonly Dictionary<string, string> signatures = new Dictionary<string, string>();

        public MapComponent_RaidTacticalPlans(Map map) : base(map) { }

        public IReadOnlyCollection<RaidTacticalPlan> Plans => plans.Values;

        public RaidTacticalPlan GetPlan(CombatOrganization organization, bool force = false)
        {
            if (organization == null) return null;
            string signature = Signature(organization);
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (force || !plans.TryGetValue(organization.id, out RaidTacticalPlan plan)
                || !signatures.TryGetValue(organization.id, out string previous)
                || previous != signature || tick - plan.PlannedTick >= 900)
            {
                plan = RaidTacticalPlanner.MakePlan(map, organization);
                plans[organization.id] = plan;
                signatures[organization.id] = signature;
            }
            return plan;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick % 120 != 0) return;
            var active = OrganizationAPI.Registry?.Organizations
                .Where(organization => organization.AllMembers.Any(pawn => pawn.Spawned && pawn.Map == map))
                .ToList() ?? new List<CombatOrganization>();
            foreach (CombatOrganization organization in active) GetPlan(organization);
            var activeIds = new HashSet<string>(active.Select(organization => organization.id));
            foreach (string id in plans.Keys.Where(id => !activeIds.Contains(id)).ToList())
            {
                plans.Remove(id);
                signatures.Remove(id);
            }
        }

        private string Signature(CombatOrganization organization)
        {
            IEnumerable<Pawn> members = organization.AllMembers.Where(pawn => pawn.Spawned && pawn.Map == map);
            return string.Join(",", members.Where(pawn => !pawn.Dead && !pawn.Downed)
                .Select(pawn => pawn.thingIDNumber + ":"
                    + (pawn.health?.summaryHealth?.SummaryHealthPercent < 0.35f ? "W" : "A"))
                .OrderBy(id => id)) + "|"
                + string.Join(",", organization.rootGroups.Select(root =>
                    root.EffectiveCommander?.thingIDNumber ?? -1)) + "|"
                + string.Join(",", members.SelectMany(InventoryGrenadeUtility.GrenadeStacks)
                    .Select(item => item.def.defName + ":" + item.stackCount).OrderBy(value => value));
        }
    }
}
