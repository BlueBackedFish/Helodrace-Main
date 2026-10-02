using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace
{
    public enum RaidTacticalTask
    {
        Entry,
        Security,
        FireSupport,
        Withdraw,
        Response
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
        public bool ObjectiveIsObservedEnemy;
        public IntVec3 Frontline;
        public IntVec3 Flank;
        public IntVec3 Entry;
        public Building PlannedBreach;
        public IntVec3 BreachCell = IntVec3.Invalid;
        public IntVec3 BreachInside = IntVec3.Invalid;
        public List<IntVec3> ApproachNodes = new List<IntVec3>();
        public List<IntVec3> ApproachPath = new List<IntVec3>();
        public HashSet<IntVec3> AvoidedTrapCells = new HashSet<IntVec3>();
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
            List<Pawn> assaultMembers = members.Where(
                MapComponent_RaidTacticalExecution.IsAssaultRaider).ToList();
            if (assaultMembers.Count > 0) members = assaultMembers;
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
            analysis.RequestAnalysis();
            plan.Doctrine = organization.faction?.def?.defName == "HD_HelodCivilHighFaction"
                ? RaidTacticalDoctrine.High : RaidTacticalDoctrine.Low;
            HashSet<IntVec3> avoidedTraps = plan.Doctrine == RaidTacticalDoctrine.High
                ? TrapAvoidanceCells(map, organization.faction) : new HashSet<IntVec3>();
            plan.AvoidedTrapCells = avoidedTraps;
            IntVec3 center = Center(members.Select(pawn => pawn.Position));
            Pawn pathfinder = members.OrderBy(pawn => pawn.Position.DistanceToSquared(center)).First();
            plan.Start = pathfinder.Position;
            List<Pawn> hostiles = map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.Faction != null
                && pawn.Faction.HostileTo(organization.faction) && !pawn.Dead).ToList();
            Pawn contact = hostiles.Where(pawn => !pawn.Downed && members.Any(member =>
                    member.Position.DistanceTo(pawn.Position) <= 40f
                    && GenSight.LineOfSight(member.Position, pawn.Position, map, true)))
                .OrderBy(pawn => pawn.Position.DistanceToSquared(plan.Start))
                .FirstOrDefault();
            plan.ObjectiveIsObservedEnemy = contact != null;
            IntVec3 reachableAdvance = contact == null
                ? ReachableAdvanceCell(map, pathfinder) : IntVec3.Invalid;
            plan.Objective = contact?.Position ?? CentralAdvanceCell(map);
            if (!plan.Objective.IsValid)
            {
                plan.Reason = "No visible defender or reachable advance cell was found.";
                return plan;
            }
            Room objectiveRoom = plan.Objective.GetRoom(map);
            bool field = objectiveRoom == null || objectiveRoom.PsychologicallyOutdoors;
            FieldThreatSnapshot fieldThreat = new FieldThreatSnapshot(map, hostiles);
            bool needsBreach = !map.reachability.CanReach(plan.Start, plan.Objective,
                PathEndMode.OnCell, TraverseParms.For(pathfinder));
            if ((needsBreach || !field) && TryFindPlannedBreach(map, analysis, fieldThreat,
                avoidedTraps, members, pathfinder, plan, out List<IntVec3> breachRoute))
            {
                plan.ApproachPath.AddRange(breachRoute);
            }
            else if (needsBreach && contact == null && reachableAdvance.IsValid)
            {
                plan.Objective = reachableAdvance;
                objectiveRoom = plan.Objective.GetRoom(map);
                field = objectiveRoom == null || objectiveRoom.PsychologicallyOutdoors;
                fieldThreat = new FieldThreatSnapshot(map, hostiles);
                plan.Entry = FindEntry(map, analysis, fieldThreat, avoidedTraps,
                    plan.Objective, pathfinder);
            }
            else if (needsBreach)
            {
                plan.Reason = "No shared, usable breach reaches the objective.";
                return plan;
            }
            else plan.Entry = FindEntry(map, analysis, fieldThreat, avoidedTraps,
                plan.Objective, pathfinder);
            if (!plan.Entry.IsValid)
            {
                plan.Reason = "No usable entry cell was found near the objective.";
                return plan;
            }

            List<IntVec3> direct = plan.PlannedBreach != null ? plan.ApproachPath
                : FindRoute(map, analysis, fieldThreat, plan.Start, plan.Entry,
                    avoidedTraps, pathfinder);
            if (direct.Count == 0)
            {
                plan.Reason = "No walking route reaches the objective entrance.";
                return plan;
            }
            if (plan.PlannedBreach == null && !field)
            {
                int opening = direct.FindIndex(1, direct.Count - 1,
                    cell => analysis.CachedAt(cell).ExteriorAccess);
                if (opening > 0 && opening < direct.Count - 1)
                {
                    plan.BreachCell = direct[opening];
                    plan.BreachInside = direct[opening + 1];
                    plan.Entry = direct[opening - 1];
                    direct = direct.Take(opening).ToList();
                }
            }
            float peakThreat = direct.Max(cell => ThreatAt(analysis, fieldThreat, cell));
            plan.Frontline = plan.BreachCell.IsValid ? plan.Entry : peakThreat > 0f
                ? direct.OrderByDescending(cell => ThreatAt(analysis, fieldThreat, cell)).First()
                : plan.Entry;
            List<IntVec3> flankRoute = new List<IntVec3>();
            plan.Flank = !plan.BreachCell.IsValid
                ? FindFlank(map, analysis, fieldThreat, avoidedTraps, pathfinder,
                    plan.Start, plan.Entry, out flankRoute) : IntVec3.Invalid;
            float directThreat = MeanThreat(analysis, fieldThreat, direct);
            float flankThreat = MeanThreat(analysis, fieldThreat, flankRoute);
            int originalPersonnel = organization.rootGroups.Sum(root => root.formation?.StandardPersonnel ?? 0);
            plan.CasualtyFraction = originalPersonnel == 0 ? 0f
                : Mathf.Clamp01(1f - members.Count / (float)originalPersonnel);
            plan.CommandEfficiency = organization.rootGroups.Count == 0 ? 1f
                : organization.AllGroups.Min(group => group.CommandEfficiency);

            List<Thing> grenades = members.SelectMany(InventoryGrenadeUtility.GrenadeStacks).ToList();
            bool smoke = grenades.Any(item => item.def.defName == "HD_Grenade_M8_Item"
                || item.def.weaponTags?.Contains("GrenadeSmoke") == true);
            bool lethal = grenades.Any(item => item.def.defName == "HD_Grenade_MKII"
                || item.def.defName == "HD_Grenade_MKIII"
                || item.def.weaponTags?.Contains("GrenadeDestructive") == true);
            bool nonlethal = grenades.Any(item => item.def.defName == "HD_Grenade_M84_Item"
                || item.def.defName == "HD_Grenade_M7A2_Item");
            bool breachTool = members.Any(pawn => CompSledgehammerBreach.WornBy(pawn) != null)
                || members.Any(pawn => pawn.equipment?.Primary
                ?.TryGetComp<CompPowerCutterBreach>() != null)
                || members.Any(pawn => BreachExplosiveUtility.CanOperate(pawn)
                    && BreachExplosiveUtility.FindIgniter(pawn,
                        BreachInitiationMode.ShockTube, false) != null
                    && BreachExplosiveUtility.CountInInventory(pawn,
                        BreachExplosiveUtility.C4Def) > 0);
            Room room = objectiveRoom;
            bool indoor = !field;
            bool friendlyInside = indoor && map.mapPawns.AllPawnsSpawned.Any(pawn =>
                pawn.Faction == organization.faction && pawn.Position.GetRoom(map) == room);
            bool nearbyHostile = map.mapPawns.AllPawnsSpawned.Any(pawn => pawn.Faction != null
                && pawn.Faction.HostileTo(organization.faction)
                && pawn.Position.DistanceTo(plan.Start) <= 18f);
            var situation = new RaidTacticalSituation
            {
                Doctrine = plan.Doctrine,
                Defending = plan.CasualtyFraction >= 0.35f
                    || (nearbyHostile && directThreat >= 18f),
                FlankAvailable = plan.Flank.IsValid,
                EntryAvailable = indoor || plan.PlannedBreach != null,
                BreachToolAvailable = breachTool,
                SmokeAvailable = smoke,
                FieldGrenadeAvailable = field && (plan.Doctrine == RaidTacticalDoctrine.High
                    ? nonlethal : lethal) && hostiles.Any(hostile => !hostile.Downed
                    && members.Any(member => member.Position.DistanceTo(hostile.Position) <= 18f)),
                LethalGrenadeAvailable = lethal,
                NonlethalGrenadeAvailable = nonlethal,
                FriendlyInsideObjective = friendlyInside,
                IndoorObjective = indoor || plan.PlannedBreach != null,
                DirectThreat = directThreat,
                FlankThreat = flankThreat,
                CasualtyFraction = plan.CasualtyFraction,
                CommandEfficiency = plan.CommandEfficiency
            };
            plan.Options = RaidTacticalDecision.Rank(situation);
            plan.Selected = RaidTacticalDecision.Select(plan.Options, plan.CommandEfficiency);
            if (plan.BreachCell.IsValid)
            {
                RaidTacticalOption entryOption = plan.Options.FirstOrDefault(option =>
                    option.Maneuver == RaidTacticalManeuver.CoordinatedEntry);
                if (entryOption == null)
                {
                    entryOption = new RaidTacticalOption
                    {
                        Maneuver = RaidTacticalManeuver.CoordinatedEntry,
                        Score = plan.Options.Count > 0 ? plan.Options[0].Score + 1f : 1f,
                        Reason = "Gather outside one breach and cross it together."
                    };
                    plan.Options.Insert(0, entryOption);
                }
                plan.Selected = entryOption;
            }
            if (plan.Selected == null)
            {
                plan.Reason = "No viable maneuver was found.";
                return plan;
            }
            List<IntVec3> selectedRoute = plan.Selected.Maneuver == RaidTacticalManeuver.FlankAttack
                ? flankRoute : direct;
            if (plan.Selected.Maneuver != RaidTacticalManeuver.HoldAndCounterattack
                && plan.Selected.Maneuver != RaidTacticalManeuver.Regroup)
            {
                if (plan.PlannedBreach == null) plan.ApproachPath.AddRange(selectedRoute);
                plan.ApproachNodes.AddRange(RouteTurns(selectedRoute));
            }
            else plan.ApproachNodes.Add(plan.Start);
            plan.EntrySupport = RaidTacticalDecision.EntrySupport(plan.Selected.Maneuver, situation);
            plan.EntryMethod = plan.PlannedBreach != null
                ? "Planned " + (plan.PlannedBreach is Building_Door ? "door" : "wall") + " breach"
                : plan.BreachCell.IsValid ? "Open exterior entry"
                : plan.Entry.GetEdifice(map) is Building_Door
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
            AssignPositions(map, analysis, fieldThreat, avoidedTraps,
                organization, members, plan);
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
            FieldThreatSnapshot fieldThreat, HashSet<IntVec3> avoidedTraps,
            CombatOrganization organization, List<Pawn> members, RaidTacticalPlan plan)
        {
            var occupied = new HashSet<IntVec3>();
            List<Pawn> wounded = members.Where(pawn => pawn.health?.summaryHealth
                ?.SummaryHealthPercent < 0.35f).ToList();
            foreach (Pawn pawn in wounded)
            {
                IntVec3 cell = FindRetreatCell(map, analysis, fieldThreat, avoidedTraps, pawn,
                    plan.Start, plan.Objective, occupied);
                if (cell.IsValid) occupied.Add(cell);
                plan.Assignments.Add(new RaidTacticalAssignment
                { Pawn = pawn, Task = RaidTacticalTask.Withdraw, Position = cell });
            }
            members = members.Except(wounded).ToList();
            if (members.Count == 0) return;
            if (plan.Selected.Maneuver == RaidTacticalManeuver.HoldAndCounterattack
                || plan.Selected.Maneuver == RaidTacticalManeuver.Regroup)
            {
                bool hold = plan.Selected.Maneuver == RaidTacticalManeuver.HoldAndCounterattack;
                var snipers = new HashSet<Pawn>();
                var sniperCompanions = new Dictionary<Pawn, Pawn>();
                foreach (CombatGroup group in hold ? organization.AllGroups
                    : Enumerable.Empty<CombatGroup>())
                {
                    Pawn sniper = group.roleAssignments.FirstOrDefault(assignment =>
                        assignment.combatRole?.combatFunction == "Sniper")?.pawn;
                    if (sniper == null || !members.Contains(sniper)) continue;
                    snipers.Add(sniper);
                    foreach (Pawn companion in group.Members.Where(pawn =>
                        pawn != sniper && members.Contains(pawn)))
                        sniperCompanions[companion] = sniper;
                }
                int responseCount = hold ? Math.Max(1, members.Count / 3) : 0;
                List<Pawn> response = members.Where(pawn => !IsFireSupport(organization, pawn)
                        && !sniperCompanions.ContainsKey(pawn))
                    .Take(responseCount).ToList();
                Pawn nearestEnemy = map.mapPawns.AllPawnsSpawned
                    .Where(pawn => !pawn.Dead && !pawn.Downed && pawn.Faction != null
                        && pawn.Faction.HostileTo(organization.faction))
                    .OrderBy(pawn => pawn.Position.DistanceToSquared(plan.Start))
                    .FirstOrDefault();
                var sniperPositions = new Dictionary<Pawn, IntVec3>();
                foreach (Pawn pawn in members.OrderBy(pawn => snipers.Contains(pawn) ? 0
                    : sniperCompanions.ContainsKey(pawn) ? 1 : 2))
                {
                    IntVec3 cell = snipers.Contains(pawn) && nearestEnemy != null
                        ? FindSniperCell(map, analysis, fieldThreat, avoidedTraps,
                            pawn, plan.Start, nearestEnemy.Position, occupied)
                        : IntVec3.Invalid;
                    if (!cell.IsValid && sniperCompanions.TryGetValue(pawn,
                            out Pawn pairedSniper)
                        && sniperPositions.TryGetValue(pairedSniper, out IntVec3 anchor))
                        cell = FindStagingCell(map, analysis, fieldThreat, avoidedTraps,
                            pawn, anchor, plan.Start, occupied, 1, 4);
                    if (!cell.IsValid)
                        cell = FindStagingCell(map, analysis, fieldThreat, avoidedTraps,
                            pawn, plan.Start, plan.Start, occupied, 2, 9);
                    if (cell.IsValid) occupied.Add(cell);
                    if (snipers.Contains(pawn)) sniperPositions[pawn] = cell;
                    plan.Assignments.Add(new RaidTacticalAssignment
                    {
                        Pawn = pawn,
                        Task = response.Contains(pawn) ? RaidTacticalTask.Response
                            : IsFireSupport(organization, pawn)
                                || sniperCompanions.ContainsKey(pawn)
                                ? RaidTacticalTask.FireSupport
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
            IntVec3 stagingRear = plan.BreachCell.IsValid
                && plan.ApproachPath.Count > 1
                ? plan.ApproachPath[Math.Max(0, plan.ApproachPath.Count - 4)]
                : plan.Start;

            for (int i = 0; i < entry.Count; i++)
            {
                IntVec3 entryAnchor = plan.Selected.Maneuver == RaidTacticalManeuver.FlankAttack
                    ? plan.Flank : plan.Entry;
                IntVec3 cell = plan.BreachCell.IsValid
                    ? FindWallStackCell(map, analysis, fieldThreat, avoidedTraps,
                        entry[i], plan, occupied, i)
                    : IntVec3.Invalid;
                if (!cell.IsValid)
                    cell = FindStagingCell(map, analysis, fieldThreat, avoidedTraps,
                        entry[i], entryAnchor, stagingRear, occupied,
                        plan.BreachCell.IsValid ? 2 : 3, 8,
                        behindAnchor: plan.BreachCell.IsValid);
                if (cell.IsValid) occupied.Add(cell);
                plan.Assignments.Add(new RaidTacticalAssignment
                { Pawn = entry[i], Task = RaidTacticalTask.Entry, Position = cell, EntryOrder = i + 1 });
            }
            foreach (Pawn pawn in support)
            {
                IntVec3 supportAnchor = plan.BreachCell.IsValid
                    ? plan.Entry : plan.Frontline;
                IntVec3 cell = FindStagingCell(map, analysis, fieldThreat, avoidedTraps, pawn,
                    supportAnchor, stagingRear, occupied, 3, 11, true,
                    plan.BreachCell.IsValid);
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

        private static IntVec3 FindSniperCell(Map map,
            MapComponent_TacticalMapAnalysis analysis, FieldThreatSnapshot fieldThreat,
            HashSet<IntVec3> avoidedTraps, Pawn sniper, IntVec3 center,
            IntVec3 target, HashSet<IntVec3> occupied)
        {
            float range = sniper.equipment?.Primary?.GetComp<CompEquippable>()
                ?.PrimaryVerb?.verbProps?.range ?? 25f;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 20f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && !occupied.Contains(cell) && !avoidedTraps.Contains(cell)
                    && cell.DistanceTo(target) >= 8f
                    && cell.DistanceTo(target) <= range * 0.85f
                    && GenSight.LineOfSight(cell, target, map, true))
                .OrderBy(cell => cell.DistanceTo(center) * 0.22f
                    - cell.DistanceTo(target) * 0.55f
                    + ThreatAt(analysis, fieldThreat, cell) * 0.18f)
                .Take(32))
                if (sniper.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    return cell;
            return IntVec3.Invalid;
        }

        private static IntVec3 FindEntry(Map map, MapComponent_TacticalMapAnalysis analysis,
            FieldThreatSnapshot fieldThreat, HashSet<IntVec3> avoidedTraps,
            IntVec3 objective, Pawn pathfinder)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(objective, 9f, true)
                .Where(cell => IsTraversable(map, cell) && !avoidedTraps.Contains(cell))
                .OrderBy(cell => cell.DistanceTo(objective) * 4f
                    + cell.DistanceTo(pathfinder.Position) * 0.3f
                    + ThreatAt(analysis, fieldThreat, cell) * 0.7f
                    + (analysis.CachedAt(cell).ExteriorAccess ? 8f : 0f))
                .Take(64))
                if (map.reachability.CanReach(pathfinder.Position, cell,
                    PathEndMode.OnCell, TraverseParms.For(pathfinder)))
                    return cell;
            return IntVec3.Invalid;
        }

        private static bool TryFindPlannedBreach(Map map,
            MapComponent_TacticalMapAnalysis analysis, FieldThreatSnapshot fieldThreat,
            HashSet<IntVec3> avoidedTraps, List<Pawn> members, Pawn pathfinder,
            RaidTacticalPlan plan, out List<IntVec3> route)
        {
            route = new List<IntVec3>();
            float bestScore = float.MaxValue;
            Building bestTarget = null;
            IntVec3 bestOutside = IntVec3.Invalid;
            IntVec3 bestInside = IntVec3.Invalid;
            Room objectiveRoom = plan.Objective.GetRoom(map);
            bool indoorObjective = objectiveRoom != null
                && !objectiveRoom.PsychologicallyOutdoors;
            foreach (Building target in map.listerThings.AllThings.OfType<Building>()
                .Where(building => (building.def.IsWall || building is Building_Door)
                    && building.Faction != null
                    && building.Faction.HostileTo(pathfinder.Faction))
                .OrderBy(building => building.Position.DistanceToSquared(plan.Objective))
                .Take(160))
            {
                List<Pawn> breachers = members.Where(pawn => CanBreach(pawn, target)).ToList();
                if (breachers.Count == 0) continue;
                foreach (IntVec3 outside in GenAdj.CellsAdjacentCardinal(target))
                {
                    IntVec3 inside = target.Position + (target.Position - outside);
                    if (outside.DistanceTo(plan.Start) >= inside.DistanceTo(plan.Start)
                        || inside.DistanceTo(plan.Objective)
                            >= outside.DistanceTo(plan.Objective)
                        || (indoorObjective && inside.GetRoom(map) != objectiveRoom)
                        || !IsOuterBoundary(map, target, outside, inside))
                        continue;
                    if (!CanWalkRouteCell(map, outside, pathfinder)
                        || !CanWalkRouteCell(map, inside, pathfinder)
                        || avoidedTraps.Contains(outside) || avoidedTraps.Contains(inside)
                        || !map.reachability.CanReach(plan.Start, outside,
                            PathEndMode.OnCell, TraverseParms.For(pathfinder))
                        || !breachers.Any(pawn => map.reachability.CanReach(pawn.Position, outside,
                                PathEndMode.OnCell, TraverseParms.For(pawn)))
                        || !map.reachability.CanReach(inside, plan.Objective,
                            PathEndMode.OnCell, TraverseParms.For(pathfinder))) continue;
                    float deviation = LineDeviation(outside, plan.Start, plan.Objective);
                    if (outside.DistanceTo(plan.Start)
                        + inside.DistanceTo(plan.Objective) + deviation * 4f
                        >= bestScore) continue;
                    List<IntVec3> candidate = FindRoute(map, analysis, fieldThreat,
                        plan.Start, outside, avoidedTraps, pathfinder);
                    if (candidate.Count == 0) continue;
                    float meanExposure = MeanThreat(analysis, fieldThreat, candidate);
                    float peakExposure = candidate.Max(cell =>
                        fieldThreat.At(cell));
                    float score = candidate.Count + inside.DistanceTo(plan.Objective)
                        + deviation * 4f
                        + meanExposure * 0.5f + peakExposure * 0.7f
                        + fieldThreat.At(outside) * 0.5f
                        + (target is Building_Door ? 6f : 0f);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    bestTarget = target;
                    bestOutside = outside;
                    bestInside = inside;
                    route = candidate;
                }
            }
            if (bestTarget == null) return false;
            plan.PlannedBreach = bestTarget;
            plan.BreachCell = bestTarget.Position;
            plan.Entry = bestOutside;
            plan.BreachInside = bestInside;
            return true;
        }

        private static bool IsOuterBoundary(Map map, Building target,
            IntVec3 outside, IntVec3 inside)
        {
            Room outsideRoom = outside.GetRoom(map);
            if (outsideRoom == inside.GetRoom(map)
                || outsideRoom?.PsychologicallyOutdoors == false
                    && map.roofGrid.RoofAt(outside) != null) return false;
            IntVec3 outward = outside - target.Position;
            IntVec3 along = new IntVec3(-outward.z, 0, outward.x);
            int span = 1;
            foreach (int side in new[] { -1, 1 })
                for (int offset = 1; offset <= 5; offset++)
                {
                    if (!IsWallLineCell(map, target.Position + along * (side * offset)))
                        break;
                    span++;
                }
            return span >= 3;
        }

        private static bool CanBreach(Pawn pawn, Building target)
        {
            if (CompSledgehammerBreach.WornBy(pawn) != null
                && CompSledgehammerBreach.IsValidTarget(pawn, target)) return true;
            if (pawn.equipment?.Primary?.TryGetComp<CompPowerCutterBreach>() != null
                && CompPowerCutterBreach.IsValidBreachTarget(target)) return true;
            return target.def.IsWall && BreachExplosiveUtility.CanOperate(pawn)
                && BreachExplosiveUtility.FindIgniter(pawn,
                    BreachInitiationMode.ShockTube, false) != null
                && BreachExplosiveUtility.CountInInventory(pawn,
                    BreachExplosiveUtility.C4Def)
                    >= BreachExplosiveUtility.RequiredC4For(target);
        }

        private static IntVec3 FindFlank(Map map, MapComponent_TacticalMapAnalysis analysis,
            FieldThreatSnapshot fieldThreat, HashSet<IntVec3> avoidedTraps,
            Pawn pathfinder, IntVec3 start, IntVec3 entry,
            out List<IntVec3> bestRoute)
        {
            bestRoute = new List<IntVec3>();
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
                    if (!cell.InBounds(map) || !cell.Standable(map)
                        || avoidedTraps.Contains(cell)) continue;
                    if (!map.reachability.CanReach(start, cell, PathEndMode.OnCell,
                        TraverseParms.For(pathfinder))
                        || !map.reachability.CanReach(cell, entry, PathEndMode.OnCell,
                            TraverseParms.For(pathfinder))) continue;
                    List<IntVec3> first = FindRoute(map, analysis, fieldThreat,
                        start, cell, avoidedTraps, pathfinder);
                    List<IntVec3> second = FindRoute(map, analysis, fieldThreat,
                        cell, entry, avoidedTraps, pathfinder);
                    if (first.Count == 0 || second.Count == 0) continue;
                    List<IntVec3> route = first.Concat(second.Skip(1)).ToList();
                    float score = MeanThreat(analysis, fieldThreat, route)
                        + route.Count * 0.25f;
                    if (score >= bestScore) continue;
                    best = cell;
                    bestScore = score;
                    bestRoute = route;
                }
            return best;
        }

        private static IntVec3 FindWallStackCell(Map map,
            MapComponent_TacticalMapAnalysis analysis, FieldThreatSnapshot fieldThreat,
            HashSet<IntVec3> avoidedTraps, Pawn pawn, RaidTacticalPlan plan,
            HashSet<IntVec3> occupied, int order)
        {
            IntVec3 outward = plan.Entry - plan.BreachCell;
            IntVec3 along = new IntVec3(-outward.z, 0, outward.x);
            Room outsideRoom = plan.Entry.GetRoom(map);
            var candidates = new List<(IntVec3 Cell, float Score)>();
            foreach (int depth in new[] { 1, 2 })
                for (int lateral = 1; lateral <= 8; lateral++)
                    foreach (int side in new[] { -1, 1 })
                    {
                        IntVec3 wall = plan.BreachCell + along * (side * lateral);
                        IntVec3 cell = wall + outward * depth;
                        if (!cell.InBounds(map) || !cell.Standable(map)
                            || cell.GetRoom(map) != outsideRoom
                            || occupied.Contains(cell) || avoidedTraps.Contains(cell)
                            || !IsWallLineCell(map, wall)
                            || !map.reachability.CanReach(pawn.Position, cell,
                                PathEndMode.OnCell, TraverseParms.For(pawn))) continue;
                        float score = lateral + (depth - 1) * 5f
                            + ThreatAt(analysis, fieldThreat, cell) * 0.3f
                            + (side == (order % 2 == 0 ? -1 : 1) ? 0f : 2f);
                        candidates.Add((cell, score));
                    }
            return candidates.OrderBy(value => value.Score)
                .Select(value => value.Cell).DefaultIfEmpty(IntVec3.Invalid).First();
        }

        private static bool IsWallLineCell(Map map, IntVec3 cell)
        {
            Building wall = cell.InBounds(map) ? cell.GetEdifice(map) as Building : null;
            return wall != null && (wall.def.IsWall || wall is Building_Door);
        }

        private static IntVec3 FindStagingCell(Map map, MapComponent_TacticalMapAnalysis analysis,
            FieldThreatSnapshot fieldThreat, HashSet<IntVec3> avoidedTraps,
            Pawn pawn, IntVec3 anchor, IntVec3 rear,
            HashSet<IntVec3> occupied,
            int minimumRadius, int maximumRadius, bool guardAnchor = false,
            bool behindAnchor = false)
        {
            if (!anchor.IsValid) return IntVec3.Invalid;
            IEnumerable<IntVec3> candidates = GenRadial.RadialCellsAround(anchor,
                maximumRadius, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && !occupied.Contains(cell) && !avoidedTraps.Contains(cell)
                    && cell.DistanceTo(anchor) >= minimumRadius)
                .Where(cell => !guardAnchor || anchor.GetEdifice(map) is Building_Door
                    || GenSight.LineOfSight(cell, anchor, map, true));
            if (behindAnchor)
                candidates = candidates.Where(cell =>
                    (cell.x - anchor.x) * (rear.x - anchor.x)
                    + (cell.z - anchor.z) * (rear.z - anchor.z) >= 0);
            foreach (IntVec3 cell in candidates
                .OrderBy(cell =>
                {
                    TacticalCellData data = analysis.CachedAt(cell);
                    return ThreatAt(analysis, fieldThreat, cell) * 0.18f + data.TotalThreat * 0.5f
                        + cell.DistanceTo(anchor) * 0.6f + cell.DistanceTo(rear) * 0.12f
                        + (behindAnchor ? LineDeviation(cell, rear, anchor) * 0.3f : 0f);
                }).Take(24))
                if (map.reachability.CanReach(pawn.Position, cell, PathEndMode.OnCell,
                    TraverseParms.For(pawn))) return cell;
            return IntVec3.Invalid;
        }

        private static IntVec3 FindRetreatCell(Map map, MapComponent_TacticalMapAnalysis analysis,
            FieldThreatSnapshot fieldThreat, HashSet<IntVec3> avoidedTraps,
            Pawn pawn, IntVec3 start, IntVec3 objective,
            HashSet<IntVec3> occupied)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(start, 14f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && !occupied.Contains(cell) && !avoidedTraps.Contains(cell)
                    && cell.DistanceTo(start) >= 5f)
                .OrderByDescending(cell => cell.DistanceTo(objective)
                    - ThreatAt(analysis, fieldThreat, cell) * 0.25f
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

        private static IntVec3 ReachableAdvanceCell(Map map, Pawn pathfinder)
        {
            IntVec3 center = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 40f, true)
                .Where(cell => IsTraversable(map, cell))
                .OrderBy(cell => cell.DistanceToSquared(center)))
                if (map.reachability.CanReach(pathfinder.Position, cell,
                    PathEndMode.OnCell, TraverseParms.For(pathfinder)))
                    return cell;

            // A sealed perimeter can put every reachable cell farther than 40
            // cells from center. Advance to its near side instead of losing the plan.
            int steps = Math.Max(Math.Abs(center.x - pathfinder.Position.x),
                Math.Abs(center.z - pathfinder.Position.z));
            for (int step = 0; step <= steps; step++)
            {
                float progress = steps == 0 ? 0f : step / (float)steps;
                IntVec3 cell = new IntVec3(
                    Mathf.RoundToInt(Mathf.Lerp(center.x, pathfinder.Position.x, progress)),
                    0,
                    Mathf.RoundToInt(Mathf.Lerp(center.z, pathfinder.Position.z, progress)));
                if (IsTraversable(map, cell) && map.reachability.CanReach(
                    pathfinder.Position, cell, PathEndMode.OnCell,
                    TraverseParms.For(pathfinder))) return cell;
            }
            return IntVec3.Invalid;
        }

        private static IntVec3 CentralAdvanceCell(Map map)
        {
            IntVec3 center = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            return GenRadial.RadialCellsAround(center, 40f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map))
                .OrderBy(cell => cell.DistanceToSquared(center))
                .DefaultIfEmpty(IntVec3.Invalid).First();
        }

        private static List<IntVec3> FindRoute(Map map,
            MapComponent_TacticalMapAnalysis analysis, FieldThreatSnapshot fieldThreat,
            IntVec3 from, IntVec3 to, HashSet<IntVec3> avoidedTraps, Pawn pathfinder)
        {
            if (!CanWalkRouteCell(map, from, pathfinder)
                || !CanWalkRouteCell(map, to, pathfinder))
                return new List<IntVec3>();
            var frontier = new SortedDictionary<int, Queue<IntVec3>>();
            var distance = new Dictionary<IntVec3, int> { [from] = 0 };
            var previous = new Dictionary<IntVec3, IntVec3>();
            Enqueue(from, 100 * (Math.Abs(from.x - to.x)
                + Math.Abs(from.z - to.z)));
            IntVec3[] directions = { IntVec3.North, IntVec3.East,
                IntVec3.South, IntVec3.West };
            while (frontier.Count > 0)
            {
                KeyValuePair<int, Queue<IntVec3>> first = frontier.First();
                IntVec3 cell = first.Value.Dequeue();
                if (first.Value.Count == 0) frontier.Remove(first.Key);
                int cost = distance[cell];
                if (first.Key != cost + 100 * (Math.Abs(cell.x - to.x)
                    + Math.Abs(cell.z - to.z))) continue;
                if (cell == to)
                {
                    var result = new List<IntVec3> { to };
                    while (cell != from)
                    {
                        cell = previous[cell];
                        result.Add(cell);
                    }
                    result.Reverse();
                    return result;
                }
                foreach (IntVec3 direction in directions)
                {
                    IntVec3 next = cell + direction;
                    if (!CanWalkRouteCell(map, next, pathfinder)
                        || avoidedTraps.Contains(next)) continue;
                    // Prefer the shortest near-straight route. Threat informs
                    // the breach choice, not a winding approach into a doorway.
                    int nextCost = cost + 100 + Mathf.RoundToInt(
                        LineDeviation(next, from, to) * 8f);
                    if (distance.TryGetValue(next, out int oldCost) && nextCost >= oldCost) continue;
                    distance[next] = nextCost;
                    previous[next] = cell;
                    Enqueue(next, nextCost + 100 * (Math.Abs(next.x - to.x)
                        + Math.Abs(next.z - to.z)));
                }
            }
            return new List<IntVec3>();

            void Enqueue(IntVec3 cell, int priority)
            {
                if (!frontier.TryGetValue(priority, out Queue<IntVec3> bucket))
                    frontier.Add(priority, bucket = new Queue<IntVec3>());
                bucket.Enqueue(cell);
            }
        }

        private static float LineDeviation(IntVec3 cell, IntVec3 from, IntVec3 to)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            float lengthSquared = dx * dx + dz * dz;
            if (lengthSquared <= 0f) return cell.DistanceTo(from);
            float progress = Mathf.Clamp01(((cell.x - from.x) * dx
                + (cell.z - from.z) * dz) / lengthSquared);
            float offX = cell.x - (from.x + progress * dx);
            float offZ = cell.z - (from.z + progress * dz);
            return Mathf.Sqrt(offX * offX + offZ * offZ);
        }

        private static bool IsTraversable(Map map, IntVec3 cell)
        {
            return cell.InBounds(map)
                && (cell.Standable(map) || cell.GetEdifice(map) is Building_Door);
        }

        private static bool CanWalkRouteCell(Map map, IntVec3 cell, Pawn pathfinder)
        {
            if (!IsTraversable(map, cell)) return false;
            Building_Door door = cell.GetEdifice(map) as Building_Door;
            return door == null || door.Open || door.PawnCanOpen(pathfinder);
        }

        private static HashSet<IntVec3> TrapAvoidanceCells(Map map, Faction attacker)
        {
            var result = new HashSet<IntVec3>();
            foreach (Building_Trap trap in map.listerThings.AllThings.OfType<Building_Trap>()
                .Where(trap => trap.Faction == null || trap.Faction.HostileTo(attacker)))
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(trap.Position, 1.5f, true))
                    if (cell.InBounds(map)) result.Add(cell);
            return result;
        }

        private static float MeanThreat(MapComponent_TacticalMapAnalysis analysis,
            FieldThreatSnapshot fieldThreat, IEnumerable<IntVec3> cells)
        {
            List<IntVec3> list = cells.ToList();
            return list.Count == 0 ? 0f : list.Average(cell => ThreatAt(analysis, fieldThreat, cell));
        }

        private static float ThreatAt(MapComponent_TacticalMapAnalysis analysis,
            FieldThreatSnapshot fieldThreat, IntVec3 cell)
        {
            return analysis.CachedAt(cell).TotalThreat + (fieldThreat?.At(cell) ?? 0f);
        }

        private static IEnumerable<IntVec3> RouteTurns(List<IntVec3> route)
        {
            if (route.Count == 0) yield break;
            yield return route[0];
            for (int i = 1; i < route.Count - 1; i++)
            {
                IntVec3 before = route[i] - route[i - 1];
                IntVec3 after = route[i + 1] - route[i];
                if (before != after) yield return route[i];
            }
            if (route.Count > 1) yield return route[route.Count - 1];
        }

        private sealed class FieldThreatSnapshot
        {
            private readonly Map map;
            private readonly List<FieldThreatSource> sources;
            private readonly Dictionary<IntVec3, float> cache = new Dictionary<IntVec3, float>();

            public FieldThreatSnapshot(Map map, IEnumerable<Pawn> hostiles)
            {
                this.map = map;
                sources = hostiles.Where(pawn => !pawn.Downed && !pawn.Destroyed)
                    .Select(pawn =>
                    {
                        VerbProperties verb = pawn.equipment?.Primary
                            ?.GetComp<CompEquippable>()?.PrimaryVerb?.verbProps
                            ?? pawn.equipment?.Primary?.def?.Verbs
                                ?.FirstOrDefault(candidate => candidate.isPrimary);
                        return new FieldThreatSource
                        {
                            Position = pawn.Position,
                            Range = verb != null && verb.range > 2f ? verb.range : 2.9f,
                            MinimumRange = verb != null && verb.range > 2f ? verb.minRange : 0f,
                            Strength = verb != null && verb.range > 2f
                                ? 8f + (pawn.skills?.GetSkill(SkillDefOf.Shooting).Level ?? 0) * 0.7f
                                : 9f + (pawn.skills?.GetSkill(SkillDefOf.Melee).Level ?? 0) * 0.5f
                        };
                    }).ToList();
            }

            public float At(IntVec3 cell)
            {
                if (cache.TryGetValue(cell, out float cached)) return cached;
                float score = 0f;
                foreach (FieldThreatSource source in sources)
                {
                    float distance = source.Position.DistanceTo(cell);
                    if (distance < source.MinimumRange || distance > source.Range
                        || !GenSight.LineOfSight(source.Position, cell, map, true)) continue;
                    score += source.Strength * Mathf.Lerp(1f, 0.42f,
                        distance / Mathf.Max(1f, source.Range));
                }
                cache.Add(cell, score);
                return score;
            }

            private struct FieldThreatSource
            {
                public IntVec3 Position;
                public float Range;
                public float MinimumRange;
                public float Strength;
            }
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
                    + (pawn.health?.summaryHealth?.SummaryHealthPercent < 0.35f ? "W" : "A")
                    + ":" + pawn.GetLord()?.LordJob?.GetType().Name)
                .OrderBy(id => id)) + "|"
                + string.Join(",", organization.AllGroups.Select(group =>
                    (group.EffectiveCommander?.thingIDNumber ?? -1) + ":"
                    + Mathf.RoundToInt(group.CommandEfficiency * 100f))) + "|"
                + string.Join(",", members.SelectMany(InventoryGrenadeUtility.GrenadeStacks)
                    .Select(item => item.def.defName + ":" + item.stackCount).OrderBy(value => value));
        }
    }
}
