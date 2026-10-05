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

    public sealed partial class RaidTacticalAssignment
    {
        public Pawn Pawn;
        public string GroupId;
        public RaidTacticalTask Task;
        public IntVec3 Position;
        public int EntryOrder;
    }

    public sealed partial class RaidTacticalPlan
    {
        public string OrganizationId;
        public string UnitId;
        public string GroupId;
        public RaidTacticalDoctrine Doctrine;
        public IntVec3 Start;
        public IntVec3 Objective;
        public IntVec3 FinalObjective;
        public bool ObjectiveIsObservedEnemy;
        public bool ObjectiveIsNamedBed;
        public bool ObjectiveIsIntermediate;
        public bool ObjectiveIsRecheck;
        public bool IsDefensive;
        public IntVec3 Frontline;
        public IntVec3 Flank;
        public IntVec3 Entry;
        public Building PlannedBreach;
        public bool ReusePassage;
        public RaidCqbIntent CqbIntent;
        public int OccupiedRoom;
        public IntVec3 BreachCell = IntVec3.Invalid;
        public IntVec3 BreachInside = IntVec3.Invalid;
        public List<RaidMovementNode> MovementNodes = new List<RaidMovementNode>();
        public List<IntVec3> ApproachPath = new List<IntVec3>();
        public List<IntVec3> SafeStackCells = new List<IntVec3>();
        public HashSet<IntVec3> SafeSupportCells = new HashSet<IntVec3>();
        public HashSet<IntVec3> AvoidedTrapCells = new HashSet<IntVec3>();
        public List<RaidTacticalOption> Options = new List<RaidTacticalOption>();
        public RaidTacticalOption Selected;
        public List<RaidTacticalAssignment> Assignments = new List<RaidTacticalAssignment>();
        public string EntrySupport;
        public string EntryMethod;
        public string BreachSearch;
        public string Reason;
        public float CommandEfficiency;
        public float CasualtyFraction;
        public int EntryDelayTicks;
        public int CoordinationDelayTicks;
        public int PlannedTick;
        public long PlanningMilliseconds;
        public int BreachCandidates;
        public int DetailedBreachChecks;
        internal readonly RaidPlanningWork Work = new RaidPlanningWork();
        public bool Success => Selected != null;
    }

    public static class RaidTacticalPlanner
    {
        internal static IEnumerable<int> Build(RaidPlanningJob job)
        {
            foreach (int step in MakePlanSteps(job.Map, job.Unit, job.Objective, job.Plan)) yield return step;
            if (job.Unit != null)
            {
                foreach (RaidTacticalAssignment assignment in job.Plan.Assignments)
                {
                    assignment.GroupId = job.Unit.Groups.FirstOrDefault(group => group.Members.Contains(assignment.Pawn))?.id;
                    yield return 0;
                }
            }
            if (job.Plan.Success)
                foreach (int step in RaidNodeRoute.PrepareSteps(job.Map, job.Plan, job.Unit.Organization.doctrine)) yield return step;
        }

        private static IEnumerable<int> Profile(Map map, RaidCpuStage stage, IEnumerable<int> steps)
        {
            using (IEnumerator<int> iterator = steps.GetEnumerator())
                while (true)
                {
                    bool more;
                    using (RaidCpuProfiler.Measure(map, stage)) more = iterator.MoveNext();
                    if (!more) yield break;
                    yield return iterator.Current;
                }
        }

        private static IEnumerable<int> MakePlanSteps(Map map,
            RaidTacticalUnit unit, IntVec3? objectiveOverride, RaidTacticalPlan plan)
        {
            if (map == null || unit == null)
            {
                plan.Reason = "No map or combat unit.";
                yield break;
            }

            yield return 0;
            List<Pawn> members = unit.Members.Where(pawn => pawn.Spawned
                && pawn.Map == map && !pawn.Dead && !pawn.Downed && !pawn.Destroyed).ToList();
            List<Pawn> assaultMembers = members.Where(
                MapComponent_RaidTacticalExecution.IsTacticalRaider).ToList();
            if (assaultMembers.Count > 0) members = assaultMembers;
            if (members.Count == 0)
            {
                plan.Reason = "No available raid members on this map.";
                yield break;
            }

            RaidStructureSnapshot analysis = map.GetComponent<MapComponent_RaidTacticalPlans>()
                ?.GetStructure(unit.Organization);
            if (analysis == null)
            {
                plan.Reason = "Tactical map analysis is unavailable.";
                yield break;
            }
            plan.Doctrine = unit.Faction?.def?.defName == "HD_HelodCivilHighFaction"
                ? RaidTacticalDoctrine.High : RaidTacticalDoctrine.Low;
            yield return 0;
            HashSet<IntVec3> avoidedTraps = plan.Doctrine == RaidTacticalDoctrine.High
                ? TrapAvoidanceCells(map, unit.Faction) : new HashSet<IntVec3>();
            plan.AvoidedTrapCells = avoidedTraps;
            IntVec3 center = Center(members.Select(pawn => pawn.Position));
            List<Pawn> entryCore = EntryMembers(unit, members);
            int occupied = entryCore.GroupBy(pawn => analysis.RoomAt(pawn.Position))
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Min(pawn => pawn.Position.DistanceToSquared(center)))
                .First().Key;
            Pawn pathfinder = entryCore.Where(pawn => analysis.RoomAt(pawn.Position) == occupied)
                .OrderBy(pawn => pawn.Position.DistanceToSquared(center)).First();
            plan.OccupiedRoom = occupied;
            RaidNavigationSnapshot navigation;
            using (RaidCpuProfiler.Measure(map, RaidCpuStage.Navigation))
                navigation = new RaidNavigationSnapshot(map, pathfinder, analysis, plan.Work);
            plan.Start = pathfinder.Position;
            if (members.All(MapComponent_RaidTacticalExecution.IsDefendingRaider))
                { foreach (int step in MakeDefensivePlanSteps(map, analysis, unit, members, plan, avoidedTraps)) yield return step; yield break; }
            yield return 0;
            List<Pawn> hostiles = map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.Faction != null
                && pawn.Faction.HostileTo(unit.Faction) && !pawn.Dead && !pawn.Downed
                && members.Any(member => member.Position.DistanceToSquared(pawn.Position) <= 1600
                    && GenSight.LineOfSight(member.Position, pawn.Position, map, true))).ToList();
            Pawn contact = hostiles.Where(pawn => !pawn.Downed && members.Any(member =>
                    member.Position.DistanceTo(pawn.Position) <= 40f
                    && GenSight.LineOfSight(member.Position, pawn.Position, map, true)))
                .OrderBy(pawn => pawn.Position.DistanceToSquared(plan.Start))
                .FirstOrDefault();
            // Capture observation values before yielding. A retained Pawn reference
            // must not reveal where an unseen enemy moved while this request waited.
            IntVec3 contactPosition = contact?.Position ?? IntVec3.Invalid;
            List<IntVec3> observedPositions = hostiles.Select(pawn => pawn.Position).ToList();
            FieldThreatSnapshot fieldThreat = new FieldThreatSnapshot(map, hostiles);
            yield return 0;
            Building_Bed namedBed = map.listerThings.AllThings.OfType<Building_Bed>()
                .Where(bed => bed.Spawned && !bed.Destroyed
                    && bed.Faction == Faction.OfPlayer
                    && bed.OwnersForReading.Any(owner => owner?.Name != null
                        && owner.Faction == Faction.OfPlayer))
                .OrderBy(bed => bed.Position.DistanceToSquared(plan.Start))
                .FirstOrDefault();
            plan.ObjectiveIsNamedBed = namedBed != null
                && (!objectiveOverride.HasValue
                    || objectiveOverride.Value == namedBed.Position);
            plan.ObjectiveIsObservedEnemy = !objectiveOverride.HasValue
                && namedBed == null && contact != null;
            IntVec3 reachableAdvance = contact == null && namedBed == null && !objectiveOverride.HasValue
                ? ReachableAdvanceCell(map, pathfinder) : IntVec3.Invalid;
            plan.Objective = objectiveOverride ?? namedBed?.Position
                ?? (contact != null ? contactPosition : CentralAdvanceCell(map));
            plan.FinalObjective = namedBed?.Position ?? plan.Objective;
            if (!plan.Objective.IsValid)
            {
                plan.Reason = "No visible defender or reachable advance cell was found.";
                yield break;
            }
            int objectiveRoom = analysis.RoomAt(plan.Objective);
            bool field = objectiveRoom == 0;
            yield return 0;
            bool needsBreach = !map.reachability.CanReach(plan.Start, plan.Objective,
                PathEndMode.OnCell, TraverseParms.For(pathfinder));
            bool interiorWalk = false;
            var localRoute = new List<IntVec3>();
            yield return 0;
            if (analysis.IsIndoor(plan.Start))
            {
                var local = new RaidCqbLocalMap(map.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.StateFor(unit.Id)?.CqbKnowledge);
                local.Refresh(map, analysis, pathfinder, plan.Start, plan.PlannedTick, avoidedTraps);
                localRoute = local.Path(plan.Start, plan.Objective);
                interiorWalk = localRoute.Count > 0 || !local.Contains(plan.Objective) && !needsBreach;
                if (local.Contains(plan.Objective) && localRoute.Count == 0) needsBreach = true;
                if (RaidCqbPolicy.Intent(occupied, objectiveRoom, interiorWalk) == RaidCqbIntent.ClearCurrentRoom)
                    { MakeCurrentRoomPlan(unit, members, plan); yield break; }
                if (interiorWalk)
                {
                    plan.Entry = FindEntry(map, analysis, fieldThreat, avoidedTraps, plan.Objective, pathfinder);
                    if (objectiveRoom != analysis.RoomAt(plan.Start) && localRoute.Count > 2)
                    {
                        int inside = localRoute.FindIndex(cell => analysis.RoomAt(cell) == objectiveRoom
                            && !local.IsPortal(cell));
                        if (inside > 1 && local.IsPortal(localRoute[inside - 1]))
                        {
                            plan.BreachCell = localRoute[inside - 1];
                            plan.Entry = localRoute[inside - 2];
                            plan.BreachInside = localRoute[inside];
                            plan.ReusePassage = true;
                        }
                    }
                }
            }
            var breachRoute = new List<IntVec3>();
            if (!interiorWalk)
                foreach (int step in Profile(map, RaidCpuStage.BreachSearch, BreachSteps(map, analysis, fieldThreat,
                avoidedTraps, unit, members, pathfinder, navigation, plan,
                breachRoute))) yield return step;
            if (!interiorWalk && plan.PlannedBreach != null)
            {
                plan.ApproachPath.AddRange(breachRoute);
                objectiveRoom = analysis.RoomAt(plan.Objective);
                field = objectiveRoom == 0;
            }
            else if (!interiorWalk && plan.Work.Limited)
            {
                plan.Reason = "Breach search allowance exhausted; keep position and retry later.";
                yield break;
            }
            else if (!interiorWalk && needsBreach && !plan.ObjectiveIsNamedBed
                && !objectiveOverride.HasValue && contact == null && reachableAdvance.IsValid)
            {
                plan.Objective = reachableAdvance;
                objectiveRoom = analysis.RoomAt(plan.Objective);
                field = objectiveRoom == 0;
                plan.Entry = FindEntry(map, analysis, fieldThreat, avoidedTraps,
                    plan.Objective, pathfinder);
            }
            else if (!interiorWalk && needsBreach)
            {
                plan.Reason = "No shared, usable breach reaches the objective.";
                yield break;
            }
            else if (!interiorWalk) plan.Entry = FindEntry(map, analysis, fieldThreat, avoidedTraps,
                plan.Objective, pathfinder);
            plan.CqbIntent = RaidCqbPolicy.Intent(occupied, objectiveRoom, interiorWalk);
            if (!plan.Entry.IsValid)
            {
                plan.Reason = "No usable entry cell was found near the objective.";
                yield break;
            }

            List<IntVec3> direct = plan.PlannedBreach != null ? plan.ApproachPath
                : plan.ReusePassage ? localRoute.Take(localRoute.IndexOf(plan.Entry) + 1).ToList() : new List<IntVec3>();
            if (plan.PlannedBreach == null && !plan.ReusePassage)
                foreach (int step in RouteSteps(map, analysis, fieldThreat, plan.Start, plan.Entry,
                    avoidedTraps, pathfinder, navigation, direct)) yield return step;
            if (direct.Count == 0)
            {
                plan.Reason = plan.Work.Limited ? "Route search allowance exhausted; retry later."
                    : "No walking route reaches the objective entrance.";
                yield break;
            }
            if (plan.PlannedBreach == null && !plan.ReusePassage && !field && occupied == 0)
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
            yield return 0;
            float peakThreat = direct.Max(cell => ThreatAt(analysis, fieldThreat, cell));
            plan.Frontline = plan.BreachCell.IsValid ? plan.Entry : peakThreat > 0f
                ? direct.OrderByDescending(cell => ThreatAt(analysis, fieldThreat, cell)).First()
                : plan.Entry;
            List<IntVec3> flankRoute = new List<IntVec3>();
            plan.Flank = IntVec3.Invalid;
            if (!plan.BreachCell.IsValid && hostiles.Count > 0)
                foreach (int step in FlankSteps(map, analysis, fieldThreat, avoidedTraps, pathfinder,
                    plan.Start, plan.Entry, navigation, plan, flankRoute)) yield return step;
            float directThreat = MeanThreat(analysis, fieldThreat, direct);
            float flankThreat = MeanThreat(analysis, fieldThreat, flankRoute);
            int originalPersonnel = unit.StandardPersonnel;
            plan.CasualtyFraction = originalPersonnel == 0 ? 0f
                : Mathf.Clamp01(1f - members.Count / (float)originalPersonnel);
            plan.CommandEfficiency = unit.CommandEfficiency;

            yield return 0;
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
            bool indoor = !field;
            bool friendlyInside = indoor && map.mapPawns.AllPawnsSpawned.Any(pawn =>
                pawn.Faction == unit.Faction
                    && analysis.RoomAt(pawn.Position) == objectiveRoom);
            bool nearbyHostile = observedPositions.Any(cell => cell.DistanceTo(plan.Start) <= 18f);
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
                    ? nonlethal : lethal) && observedPositions.Any(cell =>
                        members.Any(member => member.Position.DistanceTo(cell) <= 18f)),
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
                yield break;
            }
            List<IntVec3> selectedRoute = plan.Selected.Maneuver == RaidTacticalManeuver.FlankAttack
                ? flankRoute : direct;
            if (plan.Selected.Maneuver != RaidTacticalManeuver.HoldAndCounterattack
                && plan.Selected.Maneuver != RaidTacticalManeuver.Regroup)
            {
                if (plan.PlannedBreach == null) plan.ApproachPath.AddRange(selectedRoute);

            }

            yield return 0;
            plan.EntrySupport = RaidTacticalDecision.EntrySupport(plan.Selected.Maneuver, situation);
            int entryRoom = analysis.RoomAt(plan.BreachCell.IsValid ? plan.BreachInside : plan.Objective);
            RaidEntrySupportKind entrySupport = RaidEntryObservationPolicy.Support(entryRoom == 0, analysis.RoomArea(entryRoom));
            bool entrySmoke = plan.Selected.Maneuver == RaidTacticalManeuver.CoordinatedEntry
                && entrySupport == RaidEntrySupportKind.Smoke;
            if (plan.Selected.Maneuver == RaidTacticalManeuver.CoordinatedEntry)
                plan.EntrySupport = entrySmoke ? "Observe opening, then smoke beyond it into outdoor space"
                    : entrySupport == RaidEntrySupportKind.None ? "Observe opening; save grenade in small room unless an enemy is spotted"
                    : "Observe opening; grenade at observed enemy, otherwise a blind sector";
            plan.EntryMethod = plan.PlannedBreach != null
                ? "Planned " + (plan.PlannedBreach is Building_Door ? "door" : "wall") + " breach"
                : plan.ReusePassage ? "Reuse live CQB passage"
                : plan.BreachCell.IsValid ? "Open exterior entry"
                : plan.Entry.GetEdifice(map) is Building_Door
                ? "Door" : breachTool ? "Breach equipment available" : "Open approach";
            plan.EntryDelayTicks = interiorWalk ? 15 : entrySmoke ? 30 : plan.Doctrine == RaidTacticalDoctrine.High ? 90
                : plan.Selected.Maneuver == RaidTacticalManeuver.CoordinatedEntry ? 30
                : plan.Selected.Maneuver == RaidTacticalManeuver.SmokeAdvance ? 120 : 30;
            bool separated = members.Any(pawn => pawn.Position.DistanceTo(plan.Start) > 12f);
            bool outOfSight = members.Any(pawn => pawn.Position != plan.Start
                && !GenSight.LineOfSight(plan.Start, pawn.Position, map, true));
            plan.CoordinationDelayTicks = interiorWalk ? 15 : plan.Doctrine == RaidTacticalDoctrine.High ? 30
                : 60 + (separated ? 60 : 0) + (outOfSight ? 60 : 0);
            if (plan.BreachCell.IsValid && plan.SafeStackCells.Count == 0)
                plan.SafeStackCells = WallStackCells(map, analysis, plan, avoidedTraps).ToList();
            foreach (int step in AssignmentSteps(map, analysis, fieldThreat, avoidedTraps,
                unit, members, plan)) yield return step;
            if (plan.Assignments.Count != members.Count
                || plan.Assignments.Any(assignment => !assignment.Position.IsValid))
            {
                plan.Selected = null;
                plan.Reason = "Not enough reachable staging and withdrawal cells for this group.";
                yield break;
            }
            plan.Reason = plan.Selected.Reason;
            yield break;
        }

        private static RaidTacticalPlan MakeCurrentRoomPlan(RaidTacticalUnit unit, List<Pawn> members, RaidTacticalPlan plan)
        {
            plan.CqbIntent = RaidCqbIntent.ClearCurrentRoom;
            plan.Entry = plan.Frontline = plan.Objective;
            plan.Flank = IntVec3.Invalid;
            plan.CommandEfficiency = unit.CommandEfficiency;
            plan.Selected = new RaidTacticalOption { Maneuver = RaidTacticalManeuver.DirectAssault, Score = 1,
                Reason = "Target already belongs to the occupied, reachable room; clear it without stacking or demolition." };
            plan.Options.Add(plan.Selected);
            plan.EntrySupport = "None: entry team already occupies this room";
            plan.EntryMethod = "Clear occupied room";

            List<Pawn> entry = EntryMembers(unit, members);
            foreach (Pawn pawn in members)
                plan.Assignments.Add(new RaidTacticalAssignment { Pawn = pawn, Position = pawn.Position,
                    Task = entry.Contains(pawn) ? RaidTacticalTask.Entry : RaidTacticalTask.Security,
                    EntryOrder = entry.Contains(pawn) ? entry.IndexOf(pawn) + 1 : 0 });
            plan.Reason = plan.Selected.Reason;
            return plan;
        }

        private static IEnumerable<int> MakeDefensivePlanSteps(Map map, RaidStructureSnapshot analysis,
            RaidTacticalUnit unit, List<Pawn> members, RaidTacticalPlan plan,
            HashSet<IntVec3> avoidedTraps)
        {
            Pawn leader = members[0];
            IntVec3 anchor = leader.GetLord().CurLordToil.FlagLoc;
            if (!anchor.IsValid) anchor = leader.mindState.duty.focus.Cell;
            if (!anchor.IsValid || !anchor.InBounds(map)) anchor = Center(members.Select(pawn => pawn.Position));
            plan.IsDefensive = true;
            plan.Start = plan.Entry = plan.Objective = plan.FinalObjective = plan.Frontline = anchor;
            plan.Flank = IntVec3.Invalid;
            plan.ApproachPath.Add(anchor);
            plan.CommandEfficiency = unit.CommandEfficiency;
            plan.Options = RaidTacticalDecision.Rank(new RaidTacticalSituation { Defending = true,
                Doctrine = plan.Doctrine, CommandEfficiency = plan.CommandEfficiency });
            plan.Selected = plan.Options.First(option => option.Maneuver == RaidTacticalManeuver.HoldAndCounterattack);
            plan.EntrySupport = "None (defense)";
            plan.EntryMethod = "Defend the original Lord anchor; bounded response";
            List<Pawn> observed = map.mapPawns.AllPawnsSpawned.Where(enemy => !enemy.Dead && !enemy.Downed
                && enemy.Faction != null && enemy.Faction.HostileTo(unit.Faction)
                && members.Any(member => member.Position.DistanceToSquared(enemy.Position) <= 1600
                    && GenSight.LineOfSight(member.Position, enemy.Position, map, true))).ToList();
            foreach (int step in AssignmentSteps(map, analysis, new FieldThreatSnapshot(map, observed), avoidedTraps,
                unit, members, plan)) yield return step;
            if (plan.Assignments.Count != members.Count || plan.Assignments.Any(assignment => !assignment.Position.IsValid))
            {
                plan.Selected = null;
                plan.Reason = "Not enough reachable defensive positions.";
            }
            else plan.Reason = "Defensive Lord: fixed anchor with security, support and response groups.";
            yield break;
        }

        private static IEnumerable<int> AssignmentSteps(Map map, RaidStructureSnapshot analysis,
            FieldThreatSnapshot fieldThreat, HashSet<IntVec3> avoidedTraps,
            RaidTacticalUnit unit, List<Pawn> members, RaidTacticalPlan plan)
        {
            var occupied = MapComponent_RaidTacticalExecution.OtherFormationCells(map, plan, members);
            List<Pawn> wounded = members.Where(pawn => pawn.health?.summaryHealth
                ?.SummaryHealthPercent < 0.35f).ToList();
            foreach (Pawn pawn in wounded)
            {
                yield return 0;
                IntVec3 cell = FindRetreatCell(map, analysis, fieldThreat, avoidedTraps, pawn,
                    plan.Start, plan.Objective, occupied);
                if (cell.IsValid) occupied.Add(cell);
                plan.Assignments.Add(new RaidTacticalAssignment
                { Pawn = pawn, Task = RaidTacticalTask.Withdraw, Position = cell });
            }
            members = members.Except(wounded).ToList();
            if (members.Count == 0) yield break;
            if (plan.Selected.Maneuver == RaidTacticalManeuver.HoldAndCounterattack
                || plan.Selected.Maneuver == RaidTacticalManeuver.Regroup)
            {
                bool hold = plan.Selected.Maneuver == RaidTacticalManeuver.HoldAndCounterattack;
                var snipers = new HashSet<Pawn>();
                var sniperCompanions = new Dictionary<Pawn, Pawn>();
                foreach (CombatGroup group in hold ? unit.Groups
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
                List<Pawn> response = members.Where(pawn => !IsFireSupport(unit, pawn)
                        && !sniperCompanions.ContainsKey(pawn))
                    .Take(responseCount).ToList();
                Pawn nearestEnemy = map.mapPawns.AllPawnsSpawned
                    .Where(pawn => !pawn.Dead && !pawn.Downed && pawn.Faction != null
                        && pawn.Faction.HostileTo(unit.Faction))
                    .Where(pawn => members.Any(member => member.Position.DistanceToSquared(pawn.Position) <= 1600
                        && GenSight.LineOfSight(member.Position, pawn.Position, map, true)))
                    .OrderBy(pawn => pawn.Position.DistanceToSquared(plan.Start))
                    .FirstOrDefault();
                var sniperPositions = new Dictionary<Pawn, IntVec3>();
                foreach (Pawn pawn in members.OrderBy(pawn => snipers.Contains(pawn) ? 0
                    : sniperCompanions.ContainsKey(pawn) ? 1 : 2))
                {
                yield return 0;
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
                            : IsFireSupport(unit, pawn)
                                || sniperCompanions.ContainsKey(pawn)
                                ? RaidTacticalTask.FireSupport
                            : RaidTacticalTask.Security,
                        Position = cell
                    });
                }
                yield break;
            }
            List<Pawn> entry = EntryMembers(unit, members);
            List<Pawn> support = members.Except(entry).ToList();
            IntVec3 stagingRear = plan.BreachCell.IsValid
                && plan.ApproachPath.Count > 1
                ? plan.ApproachPath[Math.Max(0, plan.ApproachPath.Count - 4)]
                : plan.Start;

            for (int i = 0; i < entry.Count; i++)
            {
                yield return 0;
                IntVec3 entryAnchor = plan.Selected.Maneuver == RaidTacticalManeuver.FlankAttack
                    ? plan.Flank : plan.Entry;
                IntVec3 cell = plan.BreachCell.IsValid
                    ? FindWallStackCell(map, analysis, fieldThreat, avoidedTraps,
                        entry[i], plan, occupied, i, plan.SafeStackCells)
                    : IntVec3.Invalid;
                if (!cell.IsValid && !plan.BreachCell.IsValid)
                    cell = FindStagingCell(map, analysis, fieldThreat, avoidedTraps,
                        entry[i], entryAnchor, stagingRear, occupied,
                        plan.BreachCell.IsValid ? 2 : 3, 8,
                        behindAnchor: plan.BreachCell.IsValid);
                if (cell.IsValid) occupied.Add(cell);
                plan.Assignments.Add(new RaidTacticalAssignment
                { Pawn = entry[i], Task = RaidTacticalTask.Entry, Position = cell, EntryOrder = i + 1 });
            }
            yield return 0;
            List<IntVec3> rankedSupportCells = plan.BreachCell.IsValid
                ? RankedStagingCells(map, analysis, fieldThreat, avoidedTraps,
                    plan.Entry, stagingRear, 3, 18, false, true, plan)
                : null;
            if (rankedSupportCells != null)
                plan.SafeSupportCells = new HashSet<IntVec3>(rankedSupportCells);
            foreach (Pawn pawn in support)
            {
                yield return 0;
                IntVec3 supportAnchor = plan.BreachCell.IsValid
                    ? plan.Entry : plan.Frontline;
                IntVec3 cell = FindStagingCell(map, analysis, fieldThreat,
                    avoidedTraps, pawn, supportAnchor, stagingRear, occupied,
                    3, plan.BreachCell.IsValid ? 18 : 11,
                    !plan.BreachCell.IsValid, plan.BreachCell.IsValid,
                    plan.BreachCell.IsValid ? plan : null, rankedSupportCells);
                if (cell.IsValid) occupied.Add(cell);
                plan.Assignments.Add(new RaidTacticalAssignment
                {
                    Pawn = pawn,
                    Task = IsFireSupport(unit, pawn) ? RaidTacticalTask.FireSupport
                        : RaidTacticalTask.Security,
                    Position = cell
                });
            }
        }

        private static List<Pawn> EntryMembers(RaidTacticalUnit unit,
            List<Pawn> members)
        {
            Pawn leader = unit.Commander;
            if (!members.Contains(leader)) leader = null;
            List<Pawn> entry = members.Where(pawn => pawn != leader
                && !IsFireSupport(unit, pawn))
                .OrderBy(pawn => pawn.thingIDNumber).ToList();
            if (leader != null) entry.Insert(Math.Min(1, entry.Count), leader);
            if (entry.Count == 0) entry.Add(members[0]);
            int securityCount = Math.Max(1, members.Count / 4);
            while (entry.Count > 2 && members.Count - entry.Count < securityCount)
                entry.RemoveAt(entry.Count - 1);
            if (entry.Count > 8) entry.RemoveRange(8, entry.Count - 8);
            return entry;
        }

        private static bool IsFireSupport(RaidTacticalUnit unit, Pawn pawn)
        {
            string function = unit.Groups.SelectMany(group => group.roleAssignments)
                .FirstOrDefault(assignment => assignment.pawn == pawn)?.combatRole?.combatFunction;
            return function == "MachineGun" || function == "Sniper" || function == "Bazooka"
                || function == "RocketSupply";
        }

        private static IntVec3 FindSniperCell(Map map,
            RaidStructureSnapshot analysis, FieldThreatSnapshot fieldThreat,
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

        private static IntVec3 FindEntry(Map map, RaidStructureSnapshot analysis,
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

        private static IEnumerable<int> BreachSteps(Map map,
            RaidStructureSnapshot analysis, FieldThreatSnapshot fieldThreat,
            HashSet<IntVec3> avoidedTraps, RaidTacticalUnit unit,
            List<Pawn> members, Pawn pathfinder, RaidNavigationSnapshot navigation,
            RaidTacticalPlan plan, List<IntVec3> route)
        {
            yield return 0;
            Building bestTarget = null;
            IntVec3 bestOutside = IntVec3.Invalid;
            IntVec3 bestInside = IntVec3.Invalid;
            List<IntVec3> bestStackCells = null;
            bool bestInsideReachesObjective = false;
            int nearbyStructures = 0;
            int equippedTargets = 0;
            int outsideRoutes = 0;
            var candidates = new List<BreachCandidate>();
            List<BreachOperator> operators = members.Select(pawn =>
                new BreachOperator(pawn)).Where(value => value.HasEquipment).ToList();
            List<Pawn> available = members.Where(pawn =>
                !(pawn.health?.summaryHealth?.SummaryHealthPercent < 0.35f))
                .ToList();
            if (available.Count == 0 || operators.Count == 0) yield break;
            List<Pawn> entryMembers = EntryMembers(unit, available);
            bool interior = analysis.IsIndoor(plan.Start);
            var cleared = new HashSet<int>((map.GetComponent<MapComponent_RaidTacticalExecution>()?.StateFor(unit.Id)
                ?.ClearedRoomCells ?? new List<IntVec3>()).Select(analysis.RoomAt).Where(room => room > 0));
            var allowedRooms = new HashSet<int>(cleared) { plan.OccupiedRoom };
            RaidCqbLocalMap local = null;
            HashSet<IntVec3> reachableStaging = null, reachableInterior = null;
            if (interior)
            {
                local = new RaidCqbLocalMap(map.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.StateFor(unit.Id)?.CqbKnowledge);
                local.Refresh(map, analysis, pathfinder, plan.Start, plan.PlannedTick, avoidedTraps);
                // Two local floods per plan replace two paths per wall side.
                reachableStaging = local.Reachable(plan.Start, allowedRooms);
                reachableInterior = local.Reachable(plan.Start, null);
            }
            IEnumerable<Building> structures = interior
                ? GenRadial.RadialCellsAround(plan.Start, RaidCqbLocalMap.Radius, true)
                    .Where(cell => cell.InBounds(map)).Select(cell => cell.GetEdifice(map) as Building)
                    .Where(building => building != null && (building.def.IsWall || building is Building_Door)).Distinct()
                : analysis.CachedBreachStructures;
            List<Building> ordered = structures
                .Where(building => building.Spawned && !building.Destroyed
                    && building.Faction != null
                    && building.Faction.HostileTo(pathfinder.Faction)
                    && LineDeviation(building.Position, plan.Start, plan.Objective) <= 12f)
                .OrderBy(building => LineDeviation(building.Position,
                    plan.Start, plan.Objective))
                .ThenBy(building => building.Position.DistanceToSquared(plan.Start))
                .ToList();
            MapComponent_RaidTacticalPlans planOwner = map.GetComponent<MapComponent_RaidTacticalPlans>();
            int window = planOwner.BreachWindow(unit.Id, plan.Objective, plan.OccupiedRoom,
                analysis.Version.Id, ordered.Count);
            foreach (Building target in ordered.Skip(window).Take(RaidPlanningWork.StructureLimit))
            {
                yield return 0;
                if (!target.Spawned || target.Destroyed) continue;
                nearbyStructures++;
                List<BreachOperator> breachers = operators.Where(value =>
                    value.CanBreach(target)).ToList();
                if (breachers.Count == 0) continue;
                equippedTargets++;
                foreach (IntVec3 outside in GenAdj.CellsAdjacentCardinal(target))
                {
                    IntVec3 inside = target.Position + (target.Position - outside);
                    if (interior && !RaidCqbPolicy.BreachDestination(plan.OccupiedRoom, analysis.RoomAt(inside),
                        reachableStaging.Contains(outside), reachableInterior.Contains(inside), cleared)) continue;
                    if (outside.DistanceTo(plan.Start) >= inside.DistanceTo(plan.Start)
                        || inside.DistanceTo(plan.Objective)
                            >= outside.DistanceTo(plan.Objective)
                        || !(interior ? HasLiveWallContour(map, target, outside) : HasWallContour(analysis, target, outside)))
                        continue;
                    if (!navigation.CanWalk(outside)
                        || !navigation.CanWalk(inside)
                        || avoidedTraps.Contains(outside) || avoidedTraps.Contains(inside)
                        || !navigation.Connected(plan.Start, outside)
                        || !breachers.Any(value => navigation.Connected(
                            value.Pawn.Position, outside)))
                        continue;
                    bool insideReachesObjective = navigation.Connected(inside,
                        plan.Objective);
                    if (!insideReachesObjective && !plan.ObjectiveIsNamedBed) continue;
                    float deviation = LineDeviation(outside, plan.Start, plan.Objective);
                    float doorPreference = target is Building_Door
                        ? breachers.Any(value => value.Sledge)
                            ? -18f : 6f : 0f;
                    candidates.Add(new BreachCandidate
                    {
                        Target = target,
                        Outside = outside,
                        Inside = inside,
                        ReachesObjective = insideReachesObjective,
                        Breachers = breachers,
                        PreliminaryScore = outside.DistanceTo(plan.Start) * 1.5f
                            + inside.DistanceTo(plan.Objective) + deviation * 4f
                            + (insideReachesObjective ? 0f : 15f)
                            + doorPreference
                    });
                }
            }
            plan.BreachCandidates = candidates.Count;
            List<BreachCandidate> shortlist = candidates.OrderBy(value => value.PreliminaryScore).ToList();
            int detailWindow = planOwner.BreachDetailWindow(unit.Id, shortlist.Count);
            foreach (BreachCandidate candidate in shortlist.Skip(detailWindow))
            {
                yield return 0;
                if (!candidate.Target.Spawned || candidate.Target.Destroyed) continue;
                if (!plan.Work.TryBreachCheck()) break;
                plan.DetailedBreachChecks++;
                var stackPlan = new RaidTacticalPlan
                {
                    BreachCell = candidate.Target.Position,
                    BreachInside = candidate.Inside,
                    Entry = candidate.Outside
                };
                candidate.StackCells = WallStackCells(map, analysis,
                    stackPlan, avoidedTraps);
                if (candidate.StackCells.Count < entryMembers.Count) continue;
                if (!map.reachability.CanReach(plan.Start, candidate.Outside,
                        PathEndMode.OnCell, TraverseParms.For(pathfinder))
                    || !candidate.Breachers.Any(value => value.Pawn.CanReach(candidate.Outside,
                        PathEndMode.OnCell, Danger.Deadly))) continue;
                var occupied = new HashSet<IntVec3>();
                bool canStack = true;
                for (int order = 0; order < entryMembers.Count; order++)
                {
                    yield return 0;
                    IntVec3 cell = FindWallStackCell(map, analysis, fieldThreat,
                        avoidedTraps, entryMembers[order], stackPlan, occupied,
                        order, candidate.StackCells);
                    if (!cell.IsValid) { canStack = false; break; }
                    occupied.Add(cell);
                }
                if (!canStack) continue;
                var routeCandidate = new List<IntVec3>();
                foreach (int step in RouteSteps(map, analysis, fieldThreat,
                    plan.Start, candidate.Outside, avoidedTraps, pathfinder,
                    navigation, routeCandidate)) yield return step;
                IntVec3 outward = candidate.Outside - candidate.Target.Position;
                if (routeCandidate.Count == 0 && plan.Work.RouteSteps >= RaidPlanningWork.RouteLimit) break;
                if (routeCandidate.Count == 0 || routeCandidate.Any(cell =>
                    (cell.x - candidate.Target.Position.x) * outward.x
                    + (cell.z - candidate.Target.Position.z) * outward.z < 0)
                    || routeCandidate.Skip(1).Any(cell =>
                        analysis.CachedAt(cell).ExteriorAccess)) continue;
                outsideRoutes++;
                bestTarget = candidate.Target;
                bestOutside = candidate.Outside;
                bestInside = candidate.Inside;
                bestStackCells = candidate.StackCells;
                bestInsideReachesObjective = candidate.ReachesObjective;
                route.AddRange(routeCandidate);
                // A usable breach is sufficient. Do not repeat staging and
                // route searches just to improve an already valid choice.
                break;
            }
            if (bestTarget == null)
            {
                if (ordered.Count > RaidPlanningWork.StructureLimit) plan.Work.MarkLimited();
                planOwner.AdvanceBreachWindow(unit.Id, nearbyStructures, ordered.Count,
                    plan.Work.BreachChecks, shortlist.Count);
                plan.BreachSearch = $"No wall breach: {nearbyStructures} nearby structures, "
                    + $"{equippedTargets} with tools, {outsideRoutes} exterior routes."
                    + (plan.Work.Limited ? " Search allowance exhausted." : "");
                yield break;
            }
            planOwner.ClearBreachWindow(unit.Id);
            plan.PlannedBreach = bestTarget;
            plan.BreachCell = bestTarget.Position;
            plan.Entry = bestOutside;
            plan.BreachInside = bestInside;
            plan.SafeStackCells = bestStackCells;
            if (!bestInsideReachesObjective)
            {
                plan.Objective = bestInside;
                plan.ObjectiveIsNamedBed = false;
                plan.ObjectiveIsIntermediate = true;
            }
            plan.BreachSearch = $"Wall breach at {bestTarget.Position}, "
                + $"outside={bestOutside}, route={route.Count} cells.";
            yield break;
        }

        private sealed class BreachCandidate
        {
            public Building Target;
            public IntVec3 Outside;
            public IntVec3 Inside;
            public bool ReachesObjective;
            public List<BreachOperator> Breachers;
            public List<IntVec3> StackCells;
            public float PreliminaryScore;
        }

        private sealed class BreachOperator
        {
            public readonly Pawn Pawn;
            public readonly bool Sledge;
            private readonly bool cutter;
            private readonly int c4;

            public bool HasEquipment => Sledge || cutter || c4 > 0;

            public BreachOperator(Pawn pawn)
            {
                Pawn = pawn;
                Sledge = CompSledgehammerBreach.WornBy(pawn) != null;
                cutter = pawn.equipment?.Primary
                    ?.TryGetComp<CompPowerCutterBreach>() != null;
                if (BreachExplosiveUtility.CanOperate(pawn)
                    && BreachExplosiveUtility.FindIgniter(pawn,
                        BreachInitiationMode.ShockTube, false) != null)
                    c4 = BreachExplosiveUtility.CountInInventory(pawn,
                        BreachExplosiveUtility.C4Def);
            }

            public bool CanBreach(Building target)
            {
                return Sledge && CompSledgehammerBreach.IsValidTarget(Pawn, target)
                    || cutter && CompPowerCutterBreach.IsValidBreachTarget(target)
                    || target.def.IsWall && c4 > 0
                        && c4 >= BreachExplosiveUtility.RequiredC4For(target);
            }
        }

        private static bool HasWallContour(RaidStructureSnapshot analysis,
            Building target,
            IntVec3 outside)
        {
            IntVec3 outward = outside - target.Position;
            IntVec3 along = new IntVec3(-outward.z, 0, outward.x);
            int span = 1;
            foreach (int side in new[] { -1, 1 })
                for (int offset = 1; offset <= 5; offset++)
                {
                    if (!analysis.CachedAt(target.Position
                            + along * (side * offset)).WallLine)
                        break;
                    span++;
                }
            return span >= 3;
        }

        private static bool HasLiveWallContour(Map map, Building target, IntVec3 outside)
        {
            if (target is Building_Door) return true;
            IntVec3 outward = outside - target.Position;
            IntVec3 along = new IntVec3(-outward.z, 0, outward.x);
            int span = 1;
            foreach (int side in new[] { -1, 1 })
                for (int offset = 1; offset <= 5; offset++)
                {
                    IntVec3 cell = target.Position + along * (side * offset);
                    if (!cell.InBounds(map)) break;
                    Building adjacent = cell.GetEdifice(map) as Building;
                    if (!(adjacent is Building_Door) && adjacent?.def.IsWall != true) break;
                    span++;
                }
            return span >= 3;
        }

        private static IEnumerable<int> FlankSteps(Map map, RaidStructureSnapshot analysis,
            FieldThreatSnapshot fieldThreat, HashSet<IntVec3> avoidedTraps,
            Pawn pathfinder, IntVec3 start, IntVec3 entry,
            RaidNavigationSnapshot navigation, RaidTacticalPlan plan, List<IntVec3> bestRoute)
        {
            yield return 0;
            float dx = entry.x - start.x;
            float dz = entry.z - start.z;
            float length = Mathf.Max(1f, Mathf.Sqrt(dx * dx + dz * dz));
            IntVec3 best = IntVec3.Invalid;
            foreach (int side in new[] { -1, 1 })
                foreach (int distance in new[] { 8, 12, 16 })
                {
                    yield return 0;
                    var cell = new IntVec3(Mathf.RoundToInt((start.x + entry.x) / 2f - side * dz / length * distance),
                        0, Mathf.RoundToInt((start.z + entry.z) / 2f + side * dx / length * distance));
                    if (!cell.InBounds(map) || !cell.Standable(map)
                        || avoidedTraps.Contains(cell)) continue;
                    if (!map.reachability.CanReach(start, cell, PathEndMode.OnCell,
                        TraverseParms.For(pathfinder))
                        || !map.reachability.CanReach(cell, entry, PathEndMode.OnCell,
                            TraverseParms.For(pathfinder))) continue;
                    var first = new List<IntVec3>();
                    foreach (int step in RouteSteps(map, analysis, fieldThreat,
                        start, cell, avoidedTraps, pathfinder, navigation, first)) yield return step;
                    if (first.Count == 0) continue;
                    var second = new List<IntVec3>();
                    foreach (int step in RouteSteps(map, analysis, fieldThreat,
                        cell, entry, avoidedTraps, pathfinder, navigation, second)) yield return step;
                    if (first.Count == 0 || second.Count == 0) continue;
                    List<IntVec3> route = first.Concat(second.Skip(1)).ToList();
                    best = cell;
                    bestRoute.AddRange(route); plan.Flank = best;
                    yield break;
                }
            yield break;
        }

        private static IntVec3 FindWallStackCell(Map map,
            RaidStructureSnapshot analysis, FieldThreatSnapshot fieldThreat,
            HashSet<IntVec3> avoidedTraps, Pawn pawn, RaidTacticalPlan plan,
            HashSet<IntVec3> occupied, int order,
            IReadOnlyList<IntVec3> safeCells = null)
        {
            IntVec3 outward = plan.Entry - plan.BreachCell;
            IntVec3 along = new IntVec3(-outward.z, 0, outward.x);
            var candidates = new List<(IntVec3 Cell, float Score)>();
            foreach (IntVec3 cell in safeCells ?? WallStackCells(map, analysis,
                plan, avoidedTraps))
            {
                if (occupied.Contains(cell) || !cell.Standable(map)) continue;
                IntVec3 offset = cell - plan.BreachCell;
                int depth = offset.x * outward.x + offset.z * outward.z;
                int signedLateral = offset.x * along.x + offset.z * along.z;
                int lateral = Math.Abs(signedLateral);
                int side = signedLateral >= 0 ? 1 : -1;
                float score = lateral + (depth - 1) * 5f
                    + ThreatAt(analysis, fieldThreat, cell) * 0.3f
                    + (side == (order % 2 == 0 ? -1 : 1) ? 0f : 2f);
                candidates.Add((cell, score));
            }
            // Rank the cheap geometry first; a successful first candidate avoids
            // checking every stack cell for every member at every breach site.
            foreach (var candidate in candidates.OrderBy(value => value.Score))
                if (map.reachability.CanReach(pawn.Position, candidate.Cell,
                    PathEndMode.OnCell, TraverseParms.For(pawn))) return candidate.Cell;
            return IntVec3.Invalid;
        }

        private static List<IntVec3> WallStackCells(Map map,
            RaidStructureSnapshot analysis, RaidTacticalPlan plan,
            HashSet<IntVec3> avoidedTraps, bool checkGrenadeSafety = true)
        {
            IntVec3 outward = plan.Entry - plan.BreachCell;
            IntVec3 along = new IntVec3(-outward.z, 0, outward.x);
            int outsideRoom = analysis.RoomAt(plan.Entry);
            var strip = new List<IntVec3>();
            for (int depth = 1; depth <= 2; depth++)
                for (int lateral = -8; lateral <= 8; lateral++)
                {
                    IntVec3 cell = plan.BreachCell + along * lateral + outward * depth;
                    if (cell.InBounds(map) && cell.Standable(map)
                        && analysis.RoomAt(cell) == outsideRoom
                        && !avoidedTraps.Contains(cell)) strip.Add(cell);
                }
            HashSet<IntVec3> connected = RaidFormationTopology.Connected(strip,
                plan.Entry, CardinalNeighbors,
                cell => cell == plan.Entry || GenSight.LineOfSight(plan.Entry, cell, map, true));
            var result = new List<IntVec3>();
            foreach (int depth in new[] { 1, 2 })
                for (int lateral = 2; lateral <= 8; lateral++)
                    foreach (int side in new[] { -1, 1 })
                    {
                        IntVec3 wall = plan.BreachCell + along * (side * lateral);
                        IntVec3 cell = wall + outward * depth;
                        if (connected.Contains(cell)
                            && analysis.CachedAt(wall).WallLine
                            && (!checkGrenadeSafety
                                || SafeFromEntryGrenade(map, plan, cell)))
                            result.Add(cell);
                    }
            return result;
        }

        private static IEnumerable<IntVec3> CardinalNeighbors(IntVec3 cell)
        {
            yield return cell + IntVec3.North;
            yield return cell + IntVec3.South;
            yield return cell + IntVec3.East;
            yield return cell + IntVec3.West;
        }

        private static bool SafeFromEntryGrenade(Map map,
            RaidTacticalPlan plan, IntVec3 cell)
        {
            if (!plan.BreachCell.IsValid) return true;
            IntVec3 inward = plan.BreachInside - plan.BreachCell;
            IntVec3 target = plan.BreachInside + inward * 2;
            if (cell.DistanceTo(target) <= 3.5f) return false;
            // The wall still blocks the ray during planning. Starting at the
            // opening tests the sight line that will exist after demolition.
            return !GenSight.LineOfSight(plan.BreachCell, cell, map, true);
        }

        private static IntVec3 FindStagingCell(Map map, RaidStructureSnapshot analysis,
            FieldThreatSnapshot fieldThreat, HashSet<IntVec3> avoidedTraps,
            Pawn pawn, IntVec3 anchor, IntVec3 rear,
            HashSet<IntVec3> occupied,
            int minimumRadius, int maximumRadius, bool guardAnchor = false,
            bool behindAnchor = false, RaidTacticalPlan stackPlan = null,
            IReadOnlyList<IntVec3> rankedCells = null)
        {
            if (!anchor.IsValid) return IntVec3.Invalid;
            IEnumerable<IntVec3> candidates = rankedCells
                ?? RankedStagingCells(map, analysis, fieldThreat, avoidedTraps,
                    anchor, rear, minimumRadius, maximumRadius, guardAnchor,
                    behindAnchor, stackPlan);
            foreach (IntVec3 cell in candidates.Where(cell => !occupied.Contains(cell))
                .Take(24))
                if (map.reachability.CanReach(pawn.Position, cell, PathEndMode.OnCell,
                    TraverseParms.For(pawn))) return cell;
            return IntVec3.Invalid;
        }

        private static List<IntVec3> RankedStagingCells(Map map,
            RaidStructureSnapshot analysis, FieldThreatSnapshot fieldThreat,
            HashSet<IntVec3> avoidedTraps, IntVec3 anchor, IntVec3 rear,
            int minimumRadius, int maximumRadius, bool guardAnchor,
            bool behindAnchor, RaidTacticalPlan stackPlan)
        {
            IEnumerable<IntVec3> candidates = GenRadial.RadialCellsAround(anchor,
                maximumRadius, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && !avoidedTraps.Contains(cell)
                    && cell.DistanceTo(anchor) >= minimumRadius
                    && (stackPlan == null
                        || SafeFromEntryGrenade(map, stackPlan, cell)))
                .Where(cell => !guardAnchor || anchor.GetEdifice(map) is Building_Door
                    || GenSight.LineOfSight(cell, anchor, map, true));
            if (behindAnchor)
                candidates = candidates.Where(cell =>
                    (cell.x - anchor.x) * (rear.x - anchor.x)
                    + (cell.z - anchor.z) * (rear.z - anchor.z) >= 0);
            if (stackPlan != null)
            {
                int room = analysis.RoomAt(anchor);
                HashSet<IntVec3> connected = RaidFormationTopology.Connected(
                    GenRadial.RadialCellsAround(anchor, maximumRadius, true)
                        .Where(cell => cell.InBounds(map) && cell.Standable(map)
                            && analysis.RoomAt(cell) == room && !avoidedTraps.Contains(cell)),
                    anchor, CardinalNeighbors,
                    cell => cell == anchor || GenSight.LineOfSight(anchor, cell, map, true));
                candidates = candidates.Where(connected.Contains);
            }
            return candidates.OrderBy(cell =>
                {
                    TacticalCellData data = analysis.CachedAt(cell);
                    return ThreatAt(analysis, fieldThreat, cell) * 0.18f + data.TotalThreat * 0.5f
                        + cell.DistanceTo(anchor) * 0.6f + cell.DistanceTo(rear) * 0.12f
                        + (behindAnchor ? LineDeviation(cell, rear, anchor) * 0.3f : 0f);
                }).ToList();
        }

        private static IntVec3 FindRetreatCell(Map map, RaidStructureSnapshot analysis,
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

        private static IEnumerable<int> RouteSteps(Map map,
            RaidStructureSnapshot analysis, FieldThreatSnapshot fieldThreat,
            IntVec3 from, IntVec3 to, HashSet<IntVec3> avoidedTraps,
            Pawn pathfinder, RaidNavigationSnapshot navigation, List<IntVec3> result)
        {
            foreach (int step in Profile(map, RaidCpuStage.RouteSearch,
                RouteCoreSteps(map, analysis, fieldThreat, from, to, avoidedTraps, pathfinder, navigation, result))) yield return step;
        }
        private static IEnumerable<int> RouteCoreSteps(Map map,
            RaidStructureSnapshot analysis, FieldThreatSnapshot fieldThreat,
            IntVec3 from, IntVec3 to, HashSet<IntVec3> avoidedTraps,
            Pawn pathfinder, RaidNavigationSnapshot navigation, List<IntVec3> result)
        {
            if (!(navigation?.CanWalk(from) ?? CanWalkRouteCell(map, from, pathfinder))
                || !(navigation?.CanWalk(to) ?? CanWalkRouteCell(map, to, pathfinder)))
                yield break;
            var frontier = new SortedDictionary<int, Queue<IntVec3>>();
            var distance = new Dictionary<IntVec3, int> { [from] = 0 };
            var previous = new Dictionary<IntVec3, IntVec3>();
            Enqueue(from, 100 * (Math.Abs(from.x - to.x)
                + Math.Abs(from.z - to.z)));
            IntVec3[] directions = { IntVec3.North, IntVec3.East,
                IntVec3.South, IntVec3.West };
            int slice = 0;
            while (frontier.Count > 0 && navigation.Work.TryRouteStep())
            {
                if (++slice >= 32) { slice = 0; yield return 0; }
                KeyValuePair<int, Queue<IntVec3>> first = frontier.First();
                IntVec3 cell = first.Value.Dequeue();
                if (first.Value.Count == 0) frontier.Remove(first.Key);
                int cost = distance[cell];
                if (first.Key != cost + 100 * (Math.Abs(cell.x - to.x)
                    + Math.Abs(cell.z - to.z))) continue;
                if (cell == to)
                {
                    result.Add(to);
                    while (cell != from)
                    {
                        if (result.Count % 32 == 0) yield return 0;
                        cell = previous[cell];
                        result.Add(cell);
                    }
                    result.Reverse();
                    yield break;
                }
                foreach (IntVec3 direction in directions)
                {
                    IntVec3 next = cell + direction;
                    if (!(navigation?.CanWalk(next)
                            ?? CanWalkRouteCell(map, next, pathfinder))
                        || avoidedTraps.Contains(next)) continue;
                    // Exterior approach may bend along walls. Only currently observed
                    // enemies contribute exposure; hidden positions are not consulted.
                    bool outside = analysis.RoomAt(next) == 0;
                    bool wallCover = outside && GenAdj.CardinalDirections.Any(offset =>
                        (next + offset).InBounds(map) && analysis.Version.Geometry.Input.Cells[
                            map.cellIndices.CellToIndex(next + offset)].Has(TacticalRawFlags.Edifice));
                    int nextCost = cost + 100 + Mathf.RoundToInt(LineDeviation(next, from, to) * 2f)
                        + (outside && !wallCover ? 35 : 0)
                        + (outside ? Mathf.RoundToInt(fieldThreat.At(next) * 6f) : 0);
                    if (distance.TryGetValue(next, out int oldCost) && nextCost >= oldCost) continue;
                    distance[next] = nextCost;
                    previous[next] = cell;
                    Enqueue(next, nextCost + 100 * (Math.Abs(next.x - to.x)
                        + Math.Abs(next.z - to.z)));
                }
            }
            yield break;

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

        private static float MeanThreat(RaidStructureSnapshot analysis,
            FieldThreatSnapshot fieldThreat, IEnumerable<IntVec3> cells)
        {
            List<IntVec3> list = cells.ToList();
            return list.Count == 0 ? 0f : list.Average(cell => ThreatAt(analysis, fieldThreat, cell));
        }

        private static float ThreatAt(RaidStructureSnapshot analysis,
            FieldThreatSnapshot fieldThreat, IntVec3 cell)
        {
            return analysis.CachedAt(cell).TotalThreat + (fieldThreat?.At(cell) ?? 0f);
        }

        // Reuse worker-built regions and connect only live doors/breach holes.
        // Route cell validation and vanilla reachability still use the live map.
        private sealed class RaidNavigationSnapshot
        {
            private readonly Map map;
            private readonly Pawn pathfinder;
            private readonly TacticalNavigationGraph graph;
            private readonly Dictionary<int, bool> walkable = new Dictionary<int, bool>();
            internal readonly RaidPlanningWork Work;

            public RaidNavigationSnapshot(Map map, Pawn pathfinder, RaidStructureSnapshot structure, RaidPlanningWork work)
            {
                Work = work;
                this.map = map;
                this.pathfinder = pathfinder;
                MapComponent_TacticalMapAnalysis analysis = map.GetComponent<MapComponent_TacticalMapAnalysis>();
                TacticalGeometryResult geometry = (analysis?.Completed ?? structure.Version).Geometry;
                var openings = new HashSet<int>();
                IEnumerable<int> changed = analysis?.Completed != null
                    ? analysis.NavigationChanges : geometry.Breaches;
                foreach (int index in geometry.Doors.Concat(changed).Distinct())
                {
                    IntVec3 cell = map.cellIndices.IndexToCell(index);
                    Building_Door door = cell.GetEdifice(map) as Building_Door;
                    if ((cell.Standable(map) || door != null)
                        && (door == null || door.Open || door.PawnCanOpen(pathfinder))) openings.Add(index);
                }
                graph = new TacticalNavigationGraph(geometry, openings);
            }

            public bool CanWalk(IntVec3 cell)
            {
                if (!cell.InBounds(map)) return false;
                int index = map.cellIndices.CellToIndex(cell);
                if (walkable.TryGetValue(index, out bool value)) return value;
                value = CanWalkRouteCell(map, cell, pathfinder);
                walkable[index] = value;
                return value;
            }

            public bool Connected(IntVec3 first, IntVec3 second)
            {
                return CanWalk(first) && CanWalk(second)
                    && graph.Connected(map.cellIndices.CellToIndex(first), map.cellIndices.CellToIndex(second));
            }
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
        private sealed class BreachSearchWindow
        {
            internal IntVec3 Objective;
            internal int Room, Version;
            internal readonly RaidCandidateWindow Window = new RaidCandidateWindow();
            internal readonly RaidCandidateWindow Details = new RaidCandidateWindow();
        }
        private readonly Dictionary<string, BreachSearchWindow> breachWindows = new Dictionary<string, BreachSearchWindow>();
        private readonly Dictionary<string, string> signatures = new Dictionary<string, string>();
        private readonly Dictionary<string, int> decisionStructureVersions = new Dictionary<string, int>();
        private readonly Dictionary<string, RaidStructureSnapshot> structures =
            new Dictionary<string, RaidStructureSnapshot>();
        private List<RaidStructureSnapshot> savedStructures;
        private List<TacticalStructureVersion> savedVersions;

        public MapComponent_RaidTacticalPlans(Map map) : base(map) { }

        public IReadOnlyCollection<RaidTacticalPlan> Plans => plans.Values;

        internal bool DecisionQueuedFor(string id) => id != null && (map?.GetComponent<MapComponent_RaidPlanningService>()?.Pending(id) == true
            || plans.TryGetValue(id, out RaidTacticalPlan plan) && !plan.Success && plan.Work.Limited);

        internal int BreachWindow(string id, IntVec3 objective, int room, int version, int count)
        {
            if (!breachWindows.TryGetValue(id, out BreachSearchWindow value)
                || value.Objective != objective || value.Room != room || value.Version != version)
                breachWindows[id] = value = new BreachSearchWindow { Objective = objective, Room = room, Version = version };
            return value.Window.Start(count);
        }
        internal int BreachDetailWindow(string id, int count) => breachWindows[id].Details.Start(count);
        internal void AdvanceBreachWindow(string id, int visited, int count, int detailVisited, int detailCount)
        {
            if (!breachWindows.TryGetValue(id, out BreachSearchWindow value)) return;
            if (value.Details.Start(detailCount) + detailVisited < detailCount)
                value.Details.Advance(detailVisited, detailCount);
            else
            {
                value.Details.Clear();
                value.Window.Advance(visited, count);
            }
        }
        internal void ClearBreachWindow(string id) => breachWindows.Remove(id);

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedStructures = structures.Values.ToList();
                savedVersions = savedStructures.Select(snapshot => snapshot.Version)
                    .Distinct().OrderBy(version => version.Id).ToList();
            }
            Scribe_Collections.Look(ref savedVersions, "raidStructureVersions", LookMode.Deep);
            Scribe_Collections.Look(ref savedStructures, "raidStructures", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedStructures = null;
                savedVersions = null;
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                structures.Clear();
                var versions = new Dictionary<int, TacticalStructureVersion>();
                MapComponent_TacticalMapAnalysis analysis = map.GetComponent<MapComponent_TacticalMapAnalysis>();
                foreach (TacticalStructureVersion version in savedVersions ?? new List<TacticalStructureVersion>())
                    if (version.Restore(map))
                    {
                        versions[version.Id] = version;
                        analysis?.ReserveVersion(version.Id);
                    }
                foreach (RaidStructureSnapshot snapshot in savedStructures
                    ?? new List<RaidStructureSnapshot>())
                    if (snapshot?.OrganizationId != null && snapshot.Restore(map, versions))
                        structures[snapshot.OrganizationId] = snapshot;
                savedStructures = null;
                savedVersions = null;
            }
        }

        internal bool StructureReadyFor(string id) => structures.ContainsKey(id)
            || map.GetComponent<MapComponent_TacticalMapAnalysis>()?.CurrentReady == true;

        public RaidStructureSnapshot GetStructure(CombatOrganization organization)
        {
            if (organization == null) return null;
            if (structures.TryGetValue(organization.id, out RaidStructureSnapshot snapshot))
                return snapshot;
            MapComponent_TacticalMapAnalysis analysis = map.GetComponent<MapComponent_TacticalMapAnalysis>();
            if (analysis == null) return null;
            analysis.RequestAnalysis();
            if (!analysis.CurrentReady) return null;
            snapshot = new RaidStructureSnapshot(map, analysis.Completed, organization.id);
            structures.Add(organization.id, snapshot);
            return snapshot;
        }

        public RaidStructureSnapshot GetStructure(string organizationId) =>
            organizationId != null && structures.TryGetValue(organizationId,
                out RaidStructureSnapshot snapshot) ? snapshot : null;

        public void InvalidateDecision(string unitId)
        {
            map?.GetComponent<MapComponent_RaidPlanningService>()?.Cancel(unitId);
            plans.Remove(unitId);
            signatures.Remove(unitId);
            decisionStructureVersions.Remove(unitId);
        }

        public RaidTacticalPlan GetPlan(RaidTacticalUnit unit, bool force = false)
        {
            if (unit == null) return null;
            // A cache still being collected is not a failed tactical decision.
            // Retry as soon as it is ready rather than caching failure for 900 ticks.
            if (!StructureReadyFor(unit.OrganizationId)) return null;
            RaidTacticalPlan active = force ? null
                : map.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.ActivePlanFor(unit.Id);
            if (active != null && unit.Members.Where(pawn => pawn.Spawned
                    && pawn.Map == map && !pawn.Dead && !pawn.Downed
                    && MapComponent_RaidTacticalExecution.IsTacticalRaider(pawn))
                .All(pawn => active.Assignments.Any(assignment => assignment.Pawn == pawn)))
            {
                var service = map.GetComponent<MapComponent_RaidPlanningService>();
                if (service.InitialReady(unit.Id))
                {
                    RaidTacticalPlan refreshed = service.Request(unit, "initial");
                    if (refreshed != null) { plans[unit.Id] = refreshed; return refreshed; }
                }
                plans[unit.Id] = active;
                return active;
            }
            string signature = Signature(unit);
            int tick = Find.TickManager?.TicksGame ?? 0;
            int geometryVersion = map.GetComponent<MapComponent_TacticalMapAnalysis>()?.Completed?.Id ?? 0;
            if (force || !plans.TryGetValue(unit.Id, out RaidTacticalPlan plan)
                || !signatures.TryGetValue(unit.Id, out string previous)
                || previous != signature || tick - plan.PlannedTick >= (!plan.Success && plan.Work.Limited ? 180 : 900)
                || !plan.Success && (!decisionStructureVersions.TryGetValue(unit.Id, out int plannedVersion)
                    || plannedVersion != geometryVersion))
            {
                plan = map.GetComponent<MapComponent_RaidPlanningService>().Request(unit, "initial");
                if (plan == null) return plans.TryGetValue(unit.Id, out RaidTacticalPlan previousPlan) ? previousPlan : null;
                plans[unit.Id] = plan;
                signatures[unit.Id] = signature;
                decisionStructureVersions[unit.Id] = geometryVersion;
            }

            return plan;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick % 120 != 0) return;
            GameComponent_CombatOrganizations registry = OrganizationAPI.Registry;
            if (registry == null) return;
            var active = registry.Organizations.SelectMany(RaidTacticalUnit.ForOrganization)
                .Where(unit => unit.Members.Any(pawn => pawn.Spawned
                    && pawn.Map == map && !pawn.Dead && !pawn.Downed
                    && MapComponent_RaidTacticalExecution.IsTacticalRaider(pawn)))
                .ToList();
            var activeIds = new HashSet<string>(active.Select(unit => unit.Id));
            map.GetComponent<MapComponent_RaidPlanningService>().Prune(activeIds);
            foreach (string id in breachWindows.Keys.Where(id => !activeIds.Contains(id)).ToList())
                breachWindows.Remove(id);

            foreach (RaidTacticalUnit unit in active) GetPlan(unit);
            foreach (string id in plans.Keys.Where(id => !activeIds.Contains(id)).ToList())
            {
                plans.Remove(id);
                signatures.Remove(id);
                decisionStructureVersions.Remove(id);
            }
            var organizationIds = new HashSet<string>(active.Select(unit => unit.OrganizationId));
            foreach (string id in structures.Keys.Where(id => !organizationIds.Contains(id)).ToList())
                structures.Remove(id);
        }

        internal string Signature(RaidTacticalUnit unit)
        {
            IEnumerable<Pawn> members = unit.Members.Where(pawn => pawn.Spawned && pawn.Map == map);
            return string.Join(",", members.Where(pawn => !pawn.Dead && !pawn.Downed)
                .Select(pawn => pawn.thingIDNumber + ":"
                    + (pawn.health?.summaryHealth?.SummaryHealthPercent < 0.35f ? "W" : "A")
                    + ":" + pawn.GetLord()?.LordJob?.GetType().Name + ":" + pawn.mindState?.duty?.def?.defName)
                .OrderBy(id => id)) + "|"
                + string.Join(",", unit.Groups.Select(group =>
                    (group.EffectiveCommander?.thingIDNumber ?? -1) + ":"
                    + Mathf.RoundToInt(group.CommandEfficiency * 100f))) + "|"
                + string.Join(",", members.SelectMany(InventoryGrenadeUtility.GrenadeStacks)
                    .Select(item => item.def.defName + ":" + item.stackCount).OrderBy(value => value)) + "|"
                + string.Join(",", members.Where(pawn => CompSledgehammerBreach.CanOperate(pawn)
                    && CompSledgehammerBreach.WornBy(pawn) != null)
                    .Select(pawn => pawn.thingIDNumber).OrderBy(value => value)) + "|"
                + string.Join(",", members.SelectMany(pawn =>
                    (pawn.equipment?.AllEquipmentListForReading ?? new List<ThingWithComps>()).Cast<Thing>()
                    .Concat((pawn.apparel?.WornApparel ?? new List<Apparel>()).Cast<Thing>())
                    .Concat((pawn.inventory?.innerContainer?.AsEnumerable() ?? Enumerable.Empty<Thing>())
                        .Where(item => item.TryGetComp<CompBreachIgniter>() != null || item.def == BreachExplosiveUtility.C4Def)))
                    .Select(item => item.thingIDNumber + ":" + item.stackCount).OrderBy(value => value));
        }
    }
}
