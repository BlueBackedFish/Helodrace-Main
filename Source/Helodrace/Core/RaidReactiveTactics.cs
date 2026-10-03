using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using Helodrace.ModernWar;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public enum RaidReactionKind { Explosion, Sniper, Defense, Screen }

    public sealed class RaidReactivePosition : IExposable
    {
        public Pawn Pawn;
        public Thing Danger;
        public IntVec3 Destination = IntVec3.Invalid;
        public RaidReactionKind Kind;
        public int Until;
        public int SearchAfter;
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_References.Look(ref Danger, "danger");
            Scribe_Values.Look(ref Destination, "destination", IntVec3.Invalid);
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Until, "until");
            Scribe_Values.Look(ref SearchAfter, "searchAfter");
        }
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private int explosiveScanTick = -1;
        private List<Projectile> observedExplosives = new List<Projectile>();
        private static readonly AccessTools.FieldRef<Projectile, bool> ProjectileLanded =
            AccessTools.FieldRefAccess<Projectile, bool>("landed");
        private static readonly AccessTools.FieldRef<Projectile, UnityEngine.Vector3> ProjectileDestination =
            AccessTools.FieldRefAccess<Projectile, UnityEngine.Vector3>("destination");

        internal bool TryEmergencyFleeDestination(Pawn pawn, out IntVec3 destination)
        {
            string id = OrganizationAPI.GetOrganization(pawn)?.id;
            RaidReactivePosition reaction = id != null && states.TryGetValue(id, out ExecutionState state)
                ? state.Reactions.FirstOrDefault(value => value.Pawn == pawn
                    && value.Kind == RaidReactionKind.Explosion && value.Until > GenTicks.TicksGame) : null;
            destination = reaction?.Destination ?? IntVec3.Invalid;
            return destination.IsValid;
        }

        internal void NotifySupportRequested(Pawn caller, IntVec3 aim)
        {
            string id = OrganizationAPI.GetOrganization(caller)?.id;
            if (id == null || !states.TryGetValue(id, out ExecutionState state)
                || state.ActivePlan?.Success != true || !ControlsPawn(caller)) return;
            state.DefenseCaller = caller;
            state.DefenseAim = aim;
            state.DefenseUntil = GenTicks.TicksGame + 600;
            state.Reactions.RemoveAll(value => value.Kind == RaidReactionKind.Defense);
            MapComponent_RaidTacticalTrace.Record(caller, "Support accepted; prepare local cover defense");
        }

        private bool FieldDefense(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (state.DefenseUntil == 0 && state.ExternalSupportKind == RaidExternalSupportKind.None) return false;
            bool waiting = WaitingForExternalSupport(state, members, tick)
                || state.DefenseCaller != null && (map.GetComponent<MapComponent_HelodCasSupport>()
                    ?.HasActiveStrike(state.DefenseCaller) == true
                    || map.GetComponent<MapComponent_HelodMortarSupport>()?.HasActiveStrike(state.DefenseCaller) == true);
            List<Pawn> observed = VisibleArmedEnemies(members);
            bool engaging = observed.Any(enemy => enemy.Position.DistanceTo(members[0].Position) <= 36f
                && (members.Contains(enemy.mindState.enemyTarget as Pawn)
                    || enemy.CurJobDef == JobDefOf.Goto && members.Any(pawn =>
                        enemy.CurJob.targetA.IsValid && enemy.CurJob.targetA.Cell.DistanceToSquared(pawn.Position)
                            + 16 < enemy.Position.DistanceToSquared(pawn.Position))));
            if (!RaidReactivePolicy.DefenseActive(tick, ref state.DefenseUntil, waiting, engaging))
            {
                state.DefenseUntil = 0;
                state.DefenseCaller = null;
                state.Reactions.RemoveAll(value => value.Kind == RaidReactionKind.Defense);
                PauseReaction(state, tick);
                return false;
            }
            IntVec3 threat = observed.FirstOrDefault()?.Position ?? state.DefenseAim;
            CoverReactiveTeam(members, plan, state, RaidReactionKind.Defense, threat, tick, retreat: false);
            PauseReaction(state, tick);
            return true;
        }

        private List<Pawn> VisibleArmedEnemies(List<Pawn> members) => map.mapPawns.AllPawnsSpawned
            .Where(enemy => !enemy.Dead && !enemy.Downed && enemy.HostileTo(members[0])
                && enemy.equipment?.Primary != null
                && members.Any(pawn => pawn.Position.DistanceToSquared(enemy.Position) <= 4900
                    && GenSight.LineOfSight(pawn.Position, enemy.Position, map, true)))
            .OrderBy(enemy => members.Min(pawn => pawn.Position.DistanceToSquared(enemy.Position))).Take(8).ToList();

        private void CoverReactiveTeam(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state,
            RaidReactionKind kind, IntVec3 threat, int tick, bool retreat)
        {
            var occupied = new HashSet<IntVec3>(state.Reactions.Where(value => value.Kind == kind)
                .Select(value => value.Destination));
            foreach (Pawn pawn in members)
            {
                if (pawn.CurJob?.playerForced == true || IsTaserOperation(pawn)
                    || map.GetComponent<MapComponent_HelodCasSupport>()?.RequiresStationaryGuidance(pawn) == true) continue;
                RaidReactivePosition reaction = state.Reactions.FirstOrDefault(value => value.Pawn == pawn && value.Kind == kind);
                if (reaction == null)
                {
                    reaction = new RaidReactivePosition { Pawn = pawn, Kind = kind };
                    state.Reactions.Add(reaction);
                }
                if (!ValidReactiveCell(reaction.Destination) && tick >= reaction.SearchAfter)
                {
                    occupied.Remove(reaction.Destination);
                    reaction.Destination = FindReactivePosition(pawn, plan, threat, occupied, retreat);
                    reaction.SearchAfter = tick + 120;
                    if (!reaction.Destination.IsValid) reaction.Destination = pawn.Position;
                    occupied.Add(reaction.Destination);
                }
                MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Fight, reaction.Destination,
                    sprint: retreat, radius: 2f, reactive: true);
            }
        }

        private bool EmergencyReactions(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (explosiveScanTick != tick)
            {
                explosiveScanTick = tick;
                observedExplosives = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile)
                    .OfType<Projectile>().Where(projectile => !RaidSmokeUtility.IsScreeningProjectile(projectile.def)
                        && (projectile.def.projectile.explosionDelay > 0
                            || projectile.def.GetModExtension<ModernGrenadeProjectileExtension>() != null)).ToList();
            }
            state.Reactions.RemoveAll(value => !members.Contains(value.Pawn)
                || value.Kind == RaidReactionKind.Explosion && value.Until <= tick);
            var occupied = new HashSet<IntVec3>(state.Reactions.Select(value => value.Destination));
            bool evading = false;
            foreach (Pawn pawn in members)
            {
                if (pawn.CurJob?.playerForced == true && pawn.CurJobDef?.defName != "HD_CASStationaryGuidance") continue;
                List<Thing> dangers = observedExplosives.Where(projectile => projectile.Spawned
                    && projectile.Launcher?.HostileTo(pawn) == true
                    && GenSight.LineOfSight(pawn.Position, projectile.Position, map, true))
                    .Cast<Thing>().ToList();
                Thing known = pawn.mindState.knownExploder;
                if (known?.Spawned == true && !RaidSmokeUtility.IsScreeningProjectile(known.def)) dangers.Add(known);
                Thing danger = dangers.Where(value => ExposedToExplosion(pawn.Position, value))
                    .OrderBy(value => pawn.Position.DistanceToSquared(ExplosionAim(value))).FirstOrDefault();
                RaidReactivePosition reaction = state.Reactions.FirstOrDefault(value => value.Pawn == pawn
                    && value.Kind == RaidReactionKind.Explosion);
                if (danger != null)
                {
                    if (reaction == null)
                    {
                        reaction = new RaidReactivePosition { Pawn = pawn, Kind = RaidReactionKind.Explosion };
                        state.Reactions.Add(reaction);
                    }
                    if (reaction.Danger != danger || tick >= reaction.SearchAfter
                        && (!ValidReactiveCell(reaction.Destination) || ExposedToExplosion(reaction.Destination, danger)))
                    {
                        occupied.Remove(reaction.Destination);
                        reaction.Danger = danger;
                        reaction.SearchAfter = tick + 90;
                        reaction.Destination = FindReactivePosition(pawn, plan, ExplosionAim(danger), occupied,
                            retreat: true, dangers: dangers, radius: 14f);
                        if (reaction.Destination.IsValid) occupied.Add(reaction.Destination);
                        MapComponent_RaidTacticalTrace.Record(pawn, $"Grenade escape to {reaction.Destination}");
                    }
                    reaction.Until = tick + 90;
                }
                else if (reaction?.Danger?.Spawned == true) reaction.Until = tick + 90;
                if (reaction == null || reaction.Until <= tick) continue;
                if (ValidReactiveCell(reaction.Destination))
                    MapComponent_RaidTacticalOrders.Escape(pawn, reaction.Destination);
                // Even an obstructed escape must never be overwritten with a stack order.
                evading = true;
            }
            if (evading) PauseReaction(state, tick);
            return evading;
        }

        private static void PauseReaction(ExecutionState state, int tick)
        {
            state.ReadySince = -1;
            state.ApproachProgressTick = tick;
            state.PhaseStarted = tick;
        }

        private bool ValidReactiveCell(IntVec3 cell) => cell.IsValid && cell.InBounds(map) && cell.Standable(map);

        internal static IntVec3 ExplosionAim(Thing danger) => danger is Projectile projectile && !ProjectileLanded(projectile)
            ? ProjectileDestination(projectile).ToIntVec3() : danger.Position;

        internal static float ExplosionRadius(ThingDef def)
        {
            FragmentationGrenadeExtension fragments = def.GetModExtension<FragmentationGrenadeExtension>();
            return Math.Max(9f, Math.Max((def.projectile?.explosionRadius ?? 0f) + 2f,
                fragments == null ? 0f : Math.Max(fragments.radius, fragments.longRangeRadius) + 2f));
        }

        private bool ExposedToExplosion(IntVec3 cell, Thing danger) => danger.Spawned
            && cell.DistanceToSquared(ExplosionAim(danger)) <= Math.Pow(ExplosionRadius(danger.def), 2)
            && GenSight.LineOfSight(ExplosionAim(danger), cell, map, true);

        // Small local search, same frozen room, cardinally connected walkable cells.
        // The committed result is retained; no full-map plan/NativeArray is built.
        private IntVec3 FindReactivePosition(Pawn pawn, RaidTacticalPlan plan, IntVec3 threat,
            HashSet<IntVec3> occupied, bool retreat, List<Thing> dangers = null, float radius = 8f)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            int room = structure?.RoomAt(pawn.Position) ?? 0;
            var candidates = new HashSet<IntVec3>(GenRadial.RadialCellsAround(pawn.Position, radius, true)
                .Where(cell => ValidReactiveCell(cell) && !plan.AvoidedTrapCells.Contains(cell)
                    && (structure?.RoomAt(cell) ?? 0) == room));
            var connected = RaidFormationTopology.Connected(candidates, pawn.Position,
                cell => GenAdj.CardinalDirections.Select(direction => cell + direction), cell => true);
            float distance = pawn.Position.DistanceTo(threat);
            return connected.Where(cell => !occupied.Contains(cell)
                    && map.pawnDestinationReservationManager.CanReserve(cell, pawn))
                .Select(cell => new
                {
                    Cell = cell,
                    Safe = dangers == null || dangers.All(value => !ExposedToExplosion(cell, value)),
                    Score = (!GenSight.LineOfSight(threat, cell, map, true) ? 28f : 0f)
                        + CoverUtility.CalculateOverallBlockChance(cell, threat, map) * 16f
                        + (retreat ? Math.Max(-8f, Math.Min(8f, cell.DistanceTo(threat) - distance)) * 2f : 0f)
                        - pawn.Position.DistanceTo(cell) * 1.2f - cell.GetTerrain(map).pathCost * 0.08f
                }).OrderByDescending(value => value.Safe).ThenByDescending(value => value.Score)
                .Take(12).Where(value => pawn.CanReach(value.Cell, PathEndMode.OnCell, Danger.Deadly))
                .Select(value => value.Cell).DefaultIfEmpty(IntVec3.Invalid).First();
        }
    }
}
