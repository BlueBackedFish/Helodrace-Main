using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public enum TacticalManeuverPhase { Planning, Moving, Strike, Recover }
    public sealed class TacticalFieldManeuver : IExposable
    {
        public TacticalManeuverPhase Phase;
        public int Started, PhaseAt, CandidateCursor, Leg, Side = 1, BestSide = 1, ReadyShots, Team = -1, LastStrikeAt = -1;
        public TacticalFieldBound Bound;
        public int MissionEpoch, MissionNumber;
        public float BestScore = float.MinValue;
        public bool WholeSquad;
        public IntVec3 Best = IntVec3.Invalid, Origin, Focus;
        public List<int> Party = new List<int>(4);
        public List<IntVec3> Waypoints = new List<IntVec3>(3);
        public void ExposeData()
        {
            Scribe_Values.Look(ref Phase,"phase"); Scribe_Values.Look(ref Started,"started"); Scribe_Values.Look(ref PhaseAt,"phaseAt");
            Scribe_Values.Look(ref CandidateCursor,"candidateCursor"); Scribe_Values.Look(ref Leg,"leg"); Scribe_Deep.Look(ref Bound,"bound");
            Scribe_Values.Look(ref Side,"side",1); Scribe_Values.Look(ref ReadyShots,"readyShots");
            Scribe_Values.Look(ref BestSide,"bestSide",1);
            Scribe_Values.Look(ref Team,"team",-1);
            Scribe_Values.Look(ref LastStrikeAt,"lastStrikeAt",-1);
            Scribe_Values.Look(ref WholeSquad,"wholeSquad");
            Scribe_Values.Look(ref MissionEpoch,"missionEpoch"); Scribe_Values.Look(ref MissionNumber,"missionNumber");
            Scribe_Values.Look(ref BestScore,"bestScore",float.MinValue); Scribe_Values.Look(ref Best,"best",IntVec3.Invalid);
            Scribe_Values.Look(ref Origin,"origin"); Scribe_Values.Look(ref Focus,"focus");
            Scribe_Collections.Look(ref Party,"party",LookMode.Value); Scribe_Collections.Look(ref Waypoints,"waypoints",LookMode.Value);
        }
    }
    public sealed partial class TacticalFieldResponse
    {
        public TacticalFieldManeuver Maneuver;
        public int ManeuverCheckAt, NextSortieAt;
    }
    public static class TacticalFieldManeuverPolicy
    {
        public const int CandidateLimit = 24, CandidateBatch = 4, MoveTimeout = 1800;
        public static IntVec3 Candidate(IntVec3 anchor, IntVec3 focus, int side, int index, bool high)
        {
            double distance = Math.Sqrt(anchor.DistanceToSquared(focus));
            double width = Math.Max(12, Math.Min(high ? 32 : 22, distance * .6)) * (.7 + index % 3 * .2);
            double depth = Math.Max(4, Math.Min(high ? 24 : 14, distance * .35)) * (.5 + index / 3 * .2);
            return anchor + TacticalFieldDoctrine.Offset(focus - anchor, side * width, depth);
        }
        public static double Angle(IntVec3 anchor, IntVec3 focus, IntVec3 flank)
        {
            IntVec3 a = anchor - focus, b = flank - focus;
            double dot = (double)a.x * b.x + (double)a.z * b.z, cross = (double)a.x * b.z - (double)a.z * b.x;
            return Math.Abs(Math.Atan2(cross, dot) * 180 / Math.PI);
        }
        public static IntVec3 Waypoint(IntVec3 origin, IntVec3 final, IntVec3 forward, int leg)
        {
            IntVec3 side = final - origin;
            double length = Math.Sqrt(forward.LengthHorizontalSquared);
            double lateral = length == 0 ? 0 : (-forward.z * (double)side.x + forward.x * (double)side.z) / length;
            double depth = length == 0 ? 0 : (forward.x * (double)side.x + forward.z * (double)side.z) / length;
            return leg == 2 ? final : origin + TacticalFieldDoctrine.Offset(forward, lateral * (leg + 1) / 3,
                leg == 0 ? 0 : depth / 2);
        }
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private bool Hammer(TacticalSquadCommand command, Pawn pawn) =>
            command.FieldResponse.FireGroup.Contains(command.Members.FindIndex(m => m.Pawn == pawn));
        private bool FieldHammerMoving(TacticalSquadCommand command, TacticalMemberCommand member)
        {
            var m = command.FieldResponse.Maneuver;
            if (m == null || !Hammer(command, member.Pawn)) return false;
            if (m.Phase == TacticalManeuverPhase.Recover) return true;
            if (m.Phase == TacticalManeuverPhase.Strike) return !FieldAtWaypoint(command, member);
            return m.Phase == TacticalManeuverPhase.Moving && TacticalFieldBoundPolicy.Moving(m.Bound,
                m.Bound?.First.Contains(command.Members.IndexOf(member)) == true);
        }
        private bool FieldAtWaypoint(TacticalSquadCommand command, TacticalMemberCommand member) =>
            TacticalFieldDoctrine.WithinEllipse(command.FieldResponse.FirePoints[1], member.Pawn.Position,
                command.FieldResponse.Forward, command.FieldResponse.FlankWidth, command.FieldResponse.FlankDepth)
            && (command.FieldResponse.Maneuver?.Phase == TacticalManeuverPhase.Recover
                || command.FieldResponse.Areas[1].Prepared && command.FieldResponse.Areas[1].Cells.Contains(member.Pawn.Position)
                    && FieldArrivalCover(command,member,GenTicks.TicksGame));
        private bool MainFireReady(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick, ICollection<int> excluded)
        {
            int ready = 0;
            foreach (var member in active)
                if (!excluded.Contains(command.Members.IndexOf(member)) && FieldSupportReady(command, member, tick) && ++ready >= 2) return true;
            return false;
        }
        private void AdvanceFieldManeuver(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalFieldResponse field, int tick, bool support)
        {
            var m = field.Maneuver;
            var order = field.MissionOrder;
            bool assigned = order != null && order.Assignee == command.Id && order.Fresh(tick)
                && (order.Stage == TacticalFieldMissionStage.Active
                    || order.Stage == TacticalFieldMissionStage.Committed && order.Coordinator != order.Assignee);
            if(assigned && m?.Phase==TacticalManeuverPhase.Strike && (m.MissionEpoch!=order.Epoch || m.MissionNumber!=order.Number))
            {
                foreach(int index in field.FireGroup) CaptureFieldPost(command.Members[index],field,tick);
                field.FireGroup.Clear(); field.Maneuver=m=null; field.LowFlank=field.LowFlankAttack=false;
            }
            if (m == null)
            {
                if (tick < field.ManeuverCheckAt || tick < field.NextSortieAt || command.Defensive || support || active.Count < (assigned ? 3 : 6)) return;
                if(!assigned && field.LastEngaged<0 && field.Focus.DistanceToSquared(field.Anchor)>field.WeaponRange*field.WeaponRange) return;
                field.ManeuverCheckAt = tick + 90;
                bool highFaction = command.Link.Unit.Faction.def.defName == "HD_HelodCivilHighFaction";
                if (highFaction && !field.High && !assigned) return;
                // Only membership received through real voice/radio packets
                // restricts sorties. Never inspect live peer pawn states here.
                if (field.Formation != null)
                {
                    if (field.Formation.Expire(command.Id, tick)) field.Formation = null;
                    else if (field.Formation.Members.Count > 1 && !assigned) return;
                }
                var party = new List<int>(4);
                if (assigned)
                {
                    foreach (var member in active)
                        if (member.Pawn.equipment?.PrimaryEq?.PrimaryVerb?.IsMeleeAttack == false)
                            party.Add(command.Members.IndexOf(member));
                }
                else if (field.High)
                {
                    foreach (var member in active)
                        if (member.Fireteam == 1 && member.Pawn != command.Link.Unit.Commander) party.Add(command.Members.IndexOf(member));
                }
                else
                {
                    int automatic = active.Count(member => member.AutomaticWeapon);
                    foreach (var member in active.OrderBy(member => LowAutomatic(member) && automatic > 1 ? 0 : 1)
                        .ThenBy(member => member.Pawn.Position.DistanceToSquared(field.Anchor)))
                    {
                        if (member.Medic || member.Pawn == command.Link.Unit.Commander || member.CombatFunction == "MachineGun"
                            || member.CombatFunction == "Bazooka" || member.CombatFunction == "RocketSupply"
                            || LowAutomatic(member) && automatic <= 1 || member.Pawn.equipment?.PrimaryEq?.PrimaryVerb?.IsMeleeAttack != false) continue;
                        party.Add(command.Members.IndexOf(member)); if (party.Count == TacticalFieldDoctrine.FlankSize(active.Count)) break;
                    }
                }
                if (party.Count < 3)
                { if (assigned) { order.Stage = TacticalFieldMissionStage.Declined; field.NextSortieAt = tick + 900; } return; }
                if (!assigned && !MainFireReady(command, active, tick, party)) return;
                field.Maneuver = m = new TacticalFieldManeuver { Started = tick, PhaseAt = tick, Origin = assigned ? order.Origin : field.Anchor,
                    Focus = assigned ? order.Focus : field.Focus, WholeSquad = assigned,
                    MissionEpoch=assigned ? order.Epoch : 0, MissionNumber=assigned ? order.Number : 0,
                    Party = party, Team = !assigned && field.High ? 1 : -1,
                    Side = assigned ? order.Side : (field.EnemyId ^ field.Started) % 2 == 0 ? -1 : 1 };
                if (assigned) order.Stage = TacticalFieldMissionStage.Active;
            }
            // Contact expiry prevents a new leg; it must not cancel an already
            // delivered start or send the whole squad back on a short outage.
            int live = active.Count(member => m.Party.Contains(command.Members.IndexOf(member)));
            if (live < 3) { FinishFieldManeuver(command, field, tick); return; }
            if (m.Phase == TacticalManeuverPhase.Planning)
            {
                if (!m.WholeSquad && tick - field.LastSeen >= TacticalContactMemory.FreshTicks || tick - m.Started >= 600)
                { FinishFieldManeuver(command, field, tick); return; }
                var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (!budget.TryPlan(tick)) return;
                long started = Stopwatch.GetTimestamp();
                try
                {
                    var ranges = active.Where(member => m.Party.Contains(command.Members.IndexOf(member)))
                        .Select(member => member.Pawn.equipment?.PrimaryEq?.PrimaryVerb?.EffectiveRange ?? 0).OrderByDescending(r => r).ToArray();
                    float range = ranges[Math.Min(1, ranges.Length - 1)]; // two useful shooters, not the shortest weapon
                    for (int visit = 0; visit < TacticalFieldManeuverPolicy.CandidateBatch && m.CandidateCursor < TacticalFieldManeuverPolicy.CandidateLimit; visit++)
                    {
                        int index = m.CandidateCursor++, side = index < 12 ? m.Side : -m.Side;
                        FieldPostCandidates++;
                        IntVec3 candidate = TacticalFieldManeuverPolicy.Candidate(m.Origin, m.Focus, side, index % 12, field.High || m.WholeSquad);
                        if (!FieldCombatFloor(command, candidate) || candidate.DistanceToSquared(m.Focus) > range * range
                            || !GenSight.LineOfSight(candidate, m.Focus, map, true)) continue;
                        int covered = 0;
                        foreach (IntVec3 offset in GenAdj.CardinalDirections)
                            if (FieldCoverAt(command, candidate + offset * 3, m.Focus)) covered++;
                        if (FieldCoverAt(command, candidate, m.Focus)) covered++;
                        double angle = TacticalFieldManeuverPolicy.Angle(m.Origin, m.Focus, candidate);
                        if (covered < 2 || angle < 20 || angle > 100) continue;
                        float score = covered * 20 - (float)Math.Abs(60 - angle) - (float)Math.Sqrt(candidate.DistanceToSquared(m.Origin)) * .2f
                            - FieldPeerSectorCost(command,candidate,tick);
                        if (score > m.BestScore) { m.BestScore = score; m.Best = candidate; m.BestSide = side; }
                    }
                    if (m.CandidateCursor < TacticalFieldManeuverPolicy.CandidateLimit) return;
                    if (!m.Best.IsValid) { FinishFieldManeuver(command, field, tick); return; }
                    Pawn guide = active.Find(member => m.Party.Contains(command.Members.IndexOf(member))).Pawn;
                    if (!map.reachability.CanReach(guide.Position, m.Best, PathEndMode.OnCell, TraverseParms.For(guide, Danger.Some)))
                    { FinishFieldManeuver(command, field, tick); return; }
                    m.Side = m.BestSide;
                    for (int leg = 0; leg < 3; leg++)
                    {
                        IntVec3 waypoint = TacticalFieldManeuverPolicy.Waypoint(m.Origin, m.Best, field.Forward, leg);
                        if (!FieldCombatFloor(command, waypoint)) { FinishFieldManeuver(command, field, tick); return; }
                        m.Waypoints.Add(waypoint);
                    }
                    field.FireGroup.Clear(); foreach (int index in m.Party)
                    { field.FireGroup.Add(index); command.Members[index].ResetFieldPost(); }
                    field.LowFlank = !field.High; field.LowFlankAttack = false;
                    m.Phase = TacticalManeuverPhase.Moving; m.PhaseAt = tick; m.Started = tick;
                    SetFieldWaypoint(command, field, m.Waypoints[0], tick);
                    PrepareFieldBound(command,field,active,tick);
                    return;
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            }
            if (m.Phase == TacticalManeuverPhase.Moving)
            {
                if (tick - m.PhaseAt >= TacticalFieldManeuverPolicy.MoveTimeout)
                { BeginFieldRecovery(command, field, tick); return; }
                if (!AdvanceFieldBound(command,active,field,tick)) return;
                if(m.WholeSquad && m.Leg+1<m.Waypoints.Count && RemoteFieldSupport(field,tick)<2) return;
                if (++m.Leg < m.Waypoints.Count)
                { m.PhaseAt = tick; SetFieldWaypoint(command, field, m.Waypoints[m.Leg], tick);
                    PrepareFieldBound(command,field,active,tick); return; }
                m.Phase = TacticalManeuverPhase.Strike; m.PhaseAt = tick;
                field.Stage = TacticalFieldStage.Defending; field.LowFlankAttack = true; ResetHammerDecisions(command, field);
            }
            if (m.Phase == TacticalManeuverPhase.Strike)
            {
                // Wait_Combat provides actual target choice and firing; marker
                // arrival alone is not counted as established crossfire.
                m.ReadyShots = active.Count(member => m.Party.Contains(command.Members.IndexOf(member))
                    && member.FieldShotAt >= m.PhaseAt && FieldSupportReady(command, member, tick));
                if (TacticalFieldPolicy.Ready(live, m.ReadyShots)) m.LastStrikeAt = tick;
                if (m.WholeSquad && TacticalFieldPolicy.Ready(live,m.ReadyShots) && field.MissionOrder?.Assignee==command.Id
                    && field.MissionOrder.Epoch==m.MissionEpoch && field.MissionOrder.Number==m.MissionNumber)
                    field.MissionOrder.Stage = TacticalFieldMissionStage.Finished;
                if (field.High && m.Team == 1 && TacticalFieldPolicy.Ready(live, m.ReadyShots)
                    && StartFieldReserve(command, active, field, tick)) return;
                // A successful flank holds its useful angle; a fixed strike
                // timer must not send actual crossfire back to the original line.
                if (tick - Math.Max(m.PhaseAt, m.LastStrikeAt) >= TacticalFieldPolicy.ContactGrace) BeginFieldRecovery(command, field, tick);
            }
            if (m.Phase == TacticalManeuverPhase.Recover)
            {
                int arrived = active.Count(member => m.Party.Contains(command.Members.IndexOf(member)) && FieldAtWaypoint(command, member));
                if (TacticalFieldPolicy.Ready(live, arrived) || tick - m.PhaseAt >= 600) FinishFieldManeuver(command, field, tick);
            }
        }
        private bool StartFieldReserve(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick)
        {
            if (tick - field.LastSeen >= TacticalContactMemory.FreshTicks) return false;
            var reserve = active.Where(member => member.Fireteam == 2 && member.Pawn != command.Link.Unit.Commander
                && member.FieldPostState == TacticalFieldPostState.Fixed).Select(member => command.Members.IndexOf(member)).ToList();
            if (reserve.Count < 3) return false;
            // Once the first team has real crossfire, it becomes stationary
            // support at those actual covers. The reserve is the sole new mover.
            // This avoids two independent planners and shared-slot collisions.
            foreach (int index in field.FireGroup) CaptureFieldPost(command.Members[index], field, tick);
            int side = -field.Maneuver.Side;
            field.FireGroup.Clear(); field.LowFlank = field.LowFlankAttack = false;
            field.Maneuver = new TacticalFieldManeuver { Started = tick, PhaseAt = tick, Team = 2,
                Party = reserve, Origin = field.FirePoints[2], Focus = field.Focus, Side = side };
            return true;
        }
        private void ResetHammerDecisions(TacticalSquadCommand command, TacticalFieldResponse field)
        { foreach (int index in field.FireGroup) command.Members[index].FieldDecisionAt = 0; }
        private void SetFieldWaypoint(TacticalSquadCommand command, TacticalFieldResponse field, IntVec3 point, int tick)
        {
            field.FirePoints[1] = point; field.Areas[1] = new TacticalFireArea { Started = tick };
            FieldBounds++;
            field.Stage = TacticalFieldStage.Moving; ResetHammerDecisions(command, field);
        }
        private void BeginFieldRecovery(TacticalSquadCommand command, TacticalFieldResponse field, int tick)
        {
            field.Maneuver.Phase = TacticalManeuverPhase.Recover; field.Maneuver.PhaseAt = tick;
            SetFieldWaypoint(command, field, field.Maneuver.Origin, tick);
        }
        private void FinishFieldManeuver(TacticalSquadCommand command, TacticalFieldResponse field, int tick)
        {
            if (field.Maneuver?.WholeSquad == true && field.MissionOrder?.Assignee==command.Id
                && field.MissionOrder.Epoch==field.Maneuver.MissionEpoch && field.MissionOrder.Number==field.Maneuver.MissionNumber
                && field.MissionOrder?.Stage != TacticalFieldMissionStage.Finished
                && field.MissionOrder != null) field.MissionOrder.Stage = TacticalFieldMissionStage.Declined;
            foreach (int index in field.FireGroup) command.Members[index].ResetFieldPost();
            field.FireGroup.Clear(); field.LowFlank = field.LowFlankAttack = false; field.Maneuver = null;
            field.FirePoints[1] = field.Anchor; field.Areas[1] = new TacticalFireArea { Started = tick };
            field.NextSortieAt = tick + 900; field.Stage = TacticalFieldStage.Defending;
        }
        private Job FieldManeuverJob(TacticalSquadCommand command, TacticalMemberCommand member, int tick)
        {
            var field = command.FieldResponse;
            if (!FieldHammerMoving(command, member) || FieldAtWaypoint(command, member))
            {
                if(!FieldAtWaypoint(command,member) && !FieldCoverAt(command,member.Pawn.Position,field.Focus))
                {
                    IntVec3 previous=field.Maneuver.Leg>0 ? field.Maneuver.Waypoints[field.Maneuver.Leg-1] : field.Maneuver.Origin;
                    Job shelter=TacticalFieldFight.Instance.BuildCovered(member.Pawn,c=>TacticalFieldDoctrine.WithinEllipse(previous,c,
                        field.Forward,field.FlankWidth,field.FlankDepth));
                    if(shelter?.def==JobDefOf.Goto && !Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.TryPath(tick))
                    { JobMaker.ReturnToPool(shelter); shelter=null; }
                    if(shelter!=null) { shelter.expiryInterval=600; shelter.checkOverrideOnExpire=false; return shelter; }
                }
                Job hold = JobMaker.MakeJob(JobDefOf.Wait_Combat); hold.expiryInterval = 600; hold.checkOverrideOnExpire = false; return hold;
            }
            if (!field.Areas[1].Prepared) return null;
            IntVec3 destination = IntVec3.Invalid;
            // Share the squad's prepared cover footprint, at most one selected
            // reachability check per actor. Existing native reservations spread
            // soldiers without imposing exact permanent slots.
            var cells = field.Areas[1].Cells;
            float best = float.MaxValue;
            foreach (IntVec3 cell in cells)
                if (cell.Standable(map) && cell.GetFirstPawn(map) == null && map.pawnDestinationReservationManager.CanReserve(cell, member.Pawn, false)
                    && FieldSpacingCost(command, member.Pawn, cell, true) < float.MaxValue)
                {
                    float score = (float)Math.Sqrt(cell.DistanceToSquared(member.Pawn.Position)) + FieldSpacingCost(command, member.Pawn, cell);
                    if (score < best) { destination = cell; best = score; }
                }
            if (!destination.IsValid)
            {
                int visited = 0;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(field.FirePoints[1], 4, true))
                {
                    if (++visited > 24) break;
                    if (!TacticalFieldDoctrine.WithinEllipse(field.FirePoints[1],cell,field.Forward,
                            field.FlankWidth,field.FlankDepth)
                        || !FieldCombatFloor(command,cell) || cell.GetFirstPawn(map) != null
                        || !FieldCoverAt(command,cell,field.Focus) || FieldSpacingCost(command,member.Pawn,cell,true) == float.MaxValue
                        || !map.pawnDestinationReservationManager.CanReserve(cell,member.Pawn,false)) continue;
                    destination = cell; break;
                }
            }
            if (!destination.IsValid || !map.pawnDestinationReservationManager.CanReserve(destination, member.Pawn, false)) return null;
            // Validate the selected cached point only. A destroyed cover is
            // removed for every actor instead of rescanning all 64 per pawn.
            if(!FieldCoverAt(command,destination,field.Focus))
            { field.Areas[1].Cells.Remove(destination); return null; }
            var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryPath(tick)) return null;
            if (!map.reachability.CanReach(member.Pawn.Position, destination, PathEndMode.OnCell, TraverseParms.For(member.Pawn, Danger.Some))) return null;
            Job job = JobMaker.MakeJob(JobDefOf.Goto, destination); job.expiryInterval = 600; job.checkOverrideOnExpire = false; return job;
        }
    }
}
