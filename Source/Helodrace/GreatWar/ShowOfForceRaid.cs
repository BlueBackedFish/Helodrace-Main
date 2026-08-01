using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace
{
    public class RaidStrategyWorker_ShowOfForce : RaidStrategyWorker_ImmediateAttack
    {
        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            return parms.faction?.def?.defName == "HD_HelodCivilLowFaction"
                && parms.target is Map
                && parms.points >= 300f
                && base.CanUseWith(parms, groupKind);
        }

        protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
        {
            return new LordJob_HelodShowOfForce();
        }
    }

    public class LordJob_HelodBreachingAssault : LordJob_AssaultColony
    {
        private List<int> casualtyIds = new List<int>();

        public LordJob_HelodBreachingAssault() { }

        public LordJob_HelodBreachingAssault(Faction faction)
            : base(faction, false, false, false, false, false, true, false) { }

        public override void LordJobTick()
        {
            base.LordJobTick();
            RegisterCurrentCasualties();
            if (HelodRaidMoraleUtility.ShouldRetreat(lord, casualtyIds.Count))
            {
                lord.SetJob(new LordJob_HelodRaidRetreat(false));
            }
        }

        public override void Notify_PawnLost(Pawn pawn, PawnLostCondition condition)
        {
            base.Notify_PawnLost(pawn, condition);
            if (HelodRaidMoraleUtility.IsCasualty(condition))
            {
                HelodRaidMoraleUtility.AddCasualty(casualtyIds, pawn);
            }
        }

        private void RegisterCurrentCasualties()
        {
            foreach (Pawn pawn in lord.ownedPawns.Where(pawn => pawn.Downed || pawn.Dead))
            {
                HelodRaidMoraleUtility.AddCasualty(casualtyIds, pawn);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref casualtyIds, "helodBreachingCasualtyIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && casualtyIds == null)
            {
                casualtyIds = new List<int>();
            }
        }
    }

    public class LordJob_HelodShowOfForce : LordJob
    {
        private const int DemonstrationDurationTicks = 20000;
        private List<int> casualtyIds = new List<int>();
        private int startTick = -1;
        private int patrolWaypointIndex;
        private int nextWaypointTick;
        private IntVec3 patrolWaypoint = IntVec3.Invalid;

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            LordToil_HelodShowOfForce patrol = new LordToil_HelodShowOfForce();
            graph.AddToil(patrol);
            graph.StartingToil = patrol;
            return graph;
        }

        public override void LordJobTick()
        {
            if (startTick < 0)
            {
                startTick = Find.TickManager.TicksGame;
            }

            foreach (Pawn pawn in lord.ownedPawns.Where(pawn => pawn.Downed || pawn.Dead))
            {
                HelodRaidMoraleUtility.AddCasualty(casualtyIds, pawn);
            }

            bool timedOut = Find.TickManager.TicksGame - startTick >= DemonstrationDurationTicks;
            if (timedOut || HelodRaidMoraleUtility.ShouldRetreat(lord, casualtyIds.Count))
            {
                lord.SetJob(new LordJob_HelodRaidRetreat(true));
            }
        }

        public override void Notify_PawnLost(Pawn pawn, PawnLostCondition condition)
        {
            base.Notify_PawnLost(pawn, condition);
            if (HelodRaidMoraleUtility.IsCasualty(condition))
            {
                HelodRaidMoraleUtility.AddCasualty(casualtyIds, pawn);
            }
        }

        public IntVec3 CurrentPatrolWaypoint(Map map)
        {
            bool advance = patrolWaypoint.IsValid && Find.TickManager.TicksGame >= nextWaypointTick;
            if (advance)
            {
                patrolWaypointIndex = (patrolWaypointIndex + 1) % 8;
            }
            if (!patrolWaypoint.IsValid || advance)
            {
                nextWaypointTick = Find.TickManager.TicksGame + 900;

                List<IntVec3> colonyCells = map.listerBuildings.allBuildingsColonist
                    .Where(building => building.Spawned)
                    .Select(building => building.Position)
                    .Concat(map.mapPawns.FreeColonistsSpawned.Select(pawn => pawn.Position))
                    .ToList();
                IntVec3 center = colonyCells.Count > 0
                    ? new IntVec3((int)colonyCells.Average(cell => cell.x), 0, (int)colonyCells.Average(cell => cell.z))
                    : map.Center;
                int extent = colonyCells.Count > 0
                    ? colonyCells.Max(cell => Math.Max(Math.Abs(cell.x - center.x), Math.Abs(cell.z - center.z)))
                    : 12;
                int availableRadius = Math.Min(
                    Math.Min(center.x - 8, map.Size.x - 9 - center.x),
                    Math.Min(center.z - 8, map.Size.z - 9 - center.z));
                int radius = Math.Max(18, Math.Min(Math.Max(18, availableRadius), extent + 12));
                IntVec3[] directions =
                {
                    new IntVec3(1, 0, 0), new IntVec3(1, 0, 1), new IntVec3(0, 0, 1), new IntVec3(-1, 0, 1),
                    new IntVec3(-1, 0, 0), new IntVec3(-1, 0, -1), new IntVec3(0, 0, -1), new IntVec3(1, 0, -1)
                };
                IntVec3 direction = directions[patrolWaypointIndex];
                IntVec3 desired = center + direction * radius;
                desired.x = Math.Max(8, Math.Min(map.Size.x - 9, desired.x));
                desired.z = Math.Max(8, Math.Min(map.Size.z - 9, desired.z));
                patrolWaypoint = desired.Walkable(map)
                    ? desired
                    : CellFinder.RandomClosewalkCellNear(desired, map, 8);
            }

            return patrolWaypoint;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref casualtyIds, "helodShowOfForceCasualtyIds", LookMode.Value);
            Scribe_Values.Look(ref startTick, "helodShowOfForceStartTick", -1);
            Scribe_Values.Look(ref patrolWaypointIndex, "helodShowOfForceWaypointIndex", 0);
            Scribe_Values.Look(ref nextWaypointTick, "helodShowOfForceNextWaypointTick", 0);
            Scribe_Values.Look(ref patrolWaypoint, "helodShowOfForcePatrolWaypoint", IntVec3.Invalid);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && casualtyIds == null)
            {
                casualtyIds = new List<int>();
            }
        }
    }

    public class LordToil_HelodShowOfForce : LordToil
    {
        private IntVec3 waypoint = IntVec3.Invalid;

        public override void UpdateAllDuties()
        {
            UpdateWaypointAndDuties();
        }

        public override void LordToilTick()
        {
            base.LordToilTick();
            if (!Map.IsHashIntervalTick(30))
            {
                return;
            }

            LordJob_HelodShowOfForce job = lord.LordJob as LordJob_HelodShowOfForce;
            IntVec3 nextWaypoint = job?.CurrentPatrolWaypoint(Map) ?? waypoint;
            if (nextWaypoint.IsValid && nextWaypoint != waypoint)
            {
                waypoint = nextWaypoint;
                UpdateWaypointAndDuties();
            }

            TryAssignKidnapper();
            IssueVisibleTargetFire();
        }

        private void UpdateWaypointAndDuties()
        {
            LordJob_HelodShowOfForce job = lord.LordJob as LordJob_HelodShowOfForce;
            waypoint = job?.CurrentPatrolWaypoint(Map) ?? Map.Center;
            foreach (Pawn pawn in lord.ownedPawns.Where(IsMobile))
            {
                if (pawn.mindState.duty?.def == DutyDefOf.Kidnap || pawn.CurJobDef == JobDefOf.Kidnap)
                {
                    continue;
                }

                pawn.mindState.duty = new PawnDuty(DutyDefOf.TravelOrWait, waypoint)
                {
                    locomotion = LocomotionUrgency.Walk,
                    maxDanger = Danger.Deadly
                };
                if (pawn.CurJob != null && pawn.CurJobDef != JobDefOf.AttackStatic)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
        }

        private void IssueVisibleTargetFire()
        {
            List<Pawn> targets = Map.mapPawns.FreeColonistsSpawned
                .Where(target => !target.Dead && !target.Downed)
                .ToList();
            foreach (Pawn raider in lord.ownedPawns.Where(IsMobile))
            {
                if (raider.mindState.duty?.def == DutyDefOf.Kidnap
                    || raider.CurJobDef == JobDefOf.Kidnap
                    || raider.CurJobDef == JobDefOf.AttackStatic)
                {
                    continue;
                }

                Pawn target = targets
                    .OrderBy(candidate => raider.Position.DistanceToSquared(candidate.Position))
                    .FirstOrDefault(candidate => CanShoot(raider, candidate));
                if (target == null)
                {
                    continue;
                }

                Job attack = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                attack.expiryInterval = 180;
                attack.checkOverrideOnExpire = true;
                raider.jobs.TryTakeOrderedJob(attack, JobTag.Misc);
            }
        }

        private void TryAssignKidnapper()
        {
            if (!Map.mapPawns.FreeColonistsSpawned.Any(pawn => pawn.Downed && !pawn.Dead))
            {
                return;
            }

            Pawn carrier = lord.ownedPawns
                .Where(IsMobile)
                .Where(pawn => pawn.mindState.duty?.def != DutyDefOf.Kidnap && pawn.CurJobDef != JobDefOf.Kidnap)
                .OrderBy(pawn => pawn.Position.DistanceToSquared(Map.Center))
                .FirstOrDefault();
            if (carrier == null)
            {
                return;
            }

            carrier.mindState.duty = new PawnDuty(DutyDefOf.Kidnap)
            {
                locomotion = LocomotionUrgency.Jog,
                maxDanger = Danger.Deadly
            };
            if (carrier.CurJob != null)
            {
                carrier.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        private static bool CanShoot(Pawn shooter, Pawn target)
        {
            Verb verb = shooter.TryGetAttackVerb(target, false, false);
            return verb != null && verb.CanHitTarget(target);
        }

        private static bool IsMobile(Pawn pawn)
        {
            return pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving);
        }
    }

    public class LordJob_HelodRaidRetreat : LordJob
    {
        private bool kidnapColonists;

        public LordJob_HelodRaidRetreat() { }

        public LordJob_HelodRaidRetreat(bool kidnapColonists)
        {
            this.kidnapColonists = kidnapColonists;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            LordToil_HelodRaidRetreat retreat = new LordToil_HelodRaidRetreat(kidnapColonists);
            graph.AddToil(retreat);
            graph.StartingToil = retreat;
            return graph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref kidnapColonists, "helodRaidRetreatKidnapColonists", false);
        }
    }

    public class LordToil_HelodRaidRetreat : LordToil
    {
        private readonly bool kidnapColonists;

        public LordToil_HelodRaidRetreat() { }

        public LordToil_HelodRaidRetreat(bool kidnapColonists)
        {
            this.kidnapColonists = kidnapColonists;
        }

        public override bool AllowSatisfyLongNeeds => false;

        public override void UpdateAllDuties()
        {
            AssignRetreatTasks();
        }

        public override void LordToilTick()
        {
            base.LordToilTick();
            if (Map.IsHashIntervalTick(60))
            {
                AssignRetreatTasks();
            }
        }

        private void AssignRetreatTasks()
        {
            List<Pawn> mobile = lord.ownedPawns.Where(IsMobile).ToList();
            JobDef evacuateDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_EvacuateRaidCasualty");
            HashSet<Pawn> busy = new HashSet<Pawn>(mobile.Where(pawn => pawn.CurJobDef == evacuateDef));
            HashSet<Pawn> assignedCasualties = new HashSet<Pawn>(busy
                .Select(pawn => pawn.CurJob?.targetA.Pawn)
                .Where(pawn => pawn != null));

            if (evacuateDef != null)
            {
                foreach (Pawn casualty in Map.mapPawns.AllPawnsSpawned
                    .Where(pawn => pawn.Faction == lord.faction && pawn.Spawned && pawn.Downed && !pawn.Dead))
                {
                    if (assignedCasualties.Contains(casualty))
                    {
                        continue;
                    }

                    Pawn carrier = mobile
                        .Where(pawn => !busy.Contains(pawn) && pawn.CanReserveAndReach(casualty, PathEndMode.Touch, Danger.Deadly))
                        .OrderBy(pawn => pawn.Position.DistanceToSquared(casualty.Position))
                        .FirstOrDefault();
                    if (carrier == null || !TryFindExitCell(carrier, out IntVec3 exitCell))
                    {
                        continue;
                    }

                    busy.Add(carrier);
                    assignedCasualties.Add(casualty);
                    carrier.jobs.TryTakeOrderedJob(JobMaker.MakeJob(evacuateDef, casualty, exitCell), JobTag.Misc);
                }
            }

            bool colonistAvailable = kidnapColonists
                && Map.mapPawns.FreeColonistsSpawned.Any(pawn => pawn.Downed && !pawn.Dead);
            foreach (Pawn pawn in mobile.Where(pawn => !busy.Contains(pawn)))
            {
                CompSharpshooterWeapon mode = pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
                if (mode != null && mode.altModeActive)
                {
                    mode.PerformSwitch();
                }

                DutyDef desiredDuty = colonistAvailable ? DutyDefOf.Kidnap : DutyDefOf.ExitMapBest;
                if (pawn.mindState.duty?.def == desiredDuty)
                {
                    continue;
                }

                pawn.mindState.duty = new PawnDuty(desiredDuty)
                {
                    locomotion = LocomotionUrgency.Sprint,
                    maxDanger = Danger.Deadly
                };
                if (pawn.CurJob != null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
        }

        private bool TryFindExitCell(Pawn pawn, out IntVec3 exitCell)
        {
            exitCell = Map.AllCells
                .Where(cell => cell.OnEdge(Map) && cell.Standable(Map)
                    && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .OrderBy(cell => pawn.Position.DistanceToSquared(cell))
                .FirstOrDefault();
            return exitCell.IsValid;
        }

        private static bool IsMobile(Pawn pawn)
        {
            return pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving);
        }
    }

    public static class HelodRaidMoraleUtility
    {
        public static bool IsCasualty(PawnLostCondition condition)
        {
            return condition == PawnLostCondition.Incapped
                || condition == PawnLostCondition.Killed
                || condition == PawnLostCondition.MadePrisoner;
        }

        public static void AddCasualty(List<int> casualtyIds, Pawn pawn)
        {
            if (pawn != null && !casualtyIds.Contains(pawn.thingIDNumber))
            {
                casualtyIds.Add(pawn.thingIDNumber);
            }
        }

        public static bool ShouldRetreat(Lord lord, int casualtyCount)
        {
            int currentCount = lord.ownedPawns.Count;
            int estimatedInitialCount = currentCount + casualtyCount;
            int threshold = Math.Max(1, (int)Math.Floor(estimatedInitialCount * 0.20f));
            return casualtyCount >= threshold;
        }
    }
}
