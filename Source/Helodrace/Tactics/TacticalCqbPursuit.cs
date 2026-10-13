using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    // Value-only lead: a last sighting, its bounded room and one-hop exits.
    // It never looks up an unseen enemy Pawn or changes a live assault plan.
    public sealed class TacticalCqbPursuit : IExposable
    {
        public int EnemyId = -1, SeenTick = -1, Version;
        public IntVec3 Target = IntVec3.Invalid;
        public bool RoomReady, Truncated, Entered;
        public HashSet<IntVec3> Room = new HashSet<IntVec3>(), AdjacentDoors = new HashSet<IntVec3>();
        internal TacticalSupportRoomSurvey Survey;
        public bool Active(int tick) => Target.IsValid && SeenTick <= tick && tick-SeenTick < TacticalContactMemory.RetentionTicks;
        public void Observe(int id, IntVec3 position, int seen)
        {
            if (seen < SeenTick || !position.IsValid) return;
            if (id == EnemyId && position == Target || RoomReady && Room.Contains(position))
            { EnemyId = id; Target = position; SeenTick = seen; return; }
            EnemyId = id; Target = position; SeenTick = seen;
            Room.Clear(); AdjacentDoors.Clear(); RoomReady = Truncated = Entered = false; Survey = null; Version++;
        }
        public void Expire(int tick)
        {
            if (!Target.IsValid || Active(tick)) return;
            Target = IntVec3.Invalid; EnemyId = SeenTick = -1; Room.Clear(); AdjacentDoors.Clear();
            RoomReady = Truncated = Entered = false; Survey = null; Version++;
        }
        internal void CompleteRoom(HashSet<IntVec3> cells, bool truncated)
        { Room = cells; RoomReady = true; Truncated = truncated; Survey = null; Version++; }
        internal void AssignedLead(int id, IntVec3 position, int seen)
        {
            // A communicated task lead does not replace local combat memory.
            if (position==Target || RoomReady && Room.Contains(position))
            { EnemyId=id; Target=position; SeenTick=seen; return; }
            SeenTick=-1; Observe(id,position,seen);
        }
        public void RecordEntered(ISet<IntVec3> cells, IEnumerable<TacticalRoomFrontier> frontiers, int tick)
        {
            if (!Active(tick) || !cells.Contains(Target)) return;
            RecordClearedLead(frontiers);
        }
        internal void RecordClearedLead(IEnumerable<TacticalRoomFrontier> frontiers)
        {
            AdjacentDoors.Clear();
            foreach (var frontier in frontiers)
                if (!frontier.Excluded && !frontier.Outdoors)
                { AdjacentDoors.Add(frontier.Opening); if (AdjacentDoors.Count == 16) break; }
            Entered = true; Version++;
        }
        public int Priority(TacticalRoomFrontier frontier, int tick, bool follow = true)
        {
            if (!Active(tick)) return 2;
            if (!follow) return (RoomReady && Room.Contains(frontier.Inside) || frontier.Inside==Target
                || Entered && AdjacentDoors.Contains(frontier.Opening)) ? 3 : 2;
            if (!Entered && (RoomReady && Room.Contains(frontier.Inside) || frontier.Inside == Target)) return 0;
            return Entered && AdjacentDoors.Contains(frontier.Opening) ? 1 : 2;
        }
        public static bool PreferSample(int id, int seen, int selectedId, int selectedTick, int currentId) =>
            seen > selectedTick || seen == selectedTick && selectedId != currentId && id == currentId;
        public void ExposeData()
        {
            Scribe_Values.Look(ref EnemyId,"enemyId",-1); Scribe_Values.Look(ref SeenTick,"seenTick",-1);
            Scribe_Values.Look(ref Target,"target",IntVec3.Invalid); Scribe_Values.Look(ref Version,"version");
            Scribe_Values.Look(ref RoomReady,"roomReady"); Scribe_Values.Look(ref Truncated,"truncated");
            Scribe_Values.Look(ref Entered,"entered");
            Scribe_Collections.Look(ref Room,"room",LookMode.Value);
            Scribe_Collections.Look(ref AdjacentDoors,"adjacentDoors",LookMode.Value);
        }
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private bool PrepareCqbPursuit(TacticalSquadCommand command, int tick, bool ownsPlanAllowance)
        {
            TacticalCommunications.RefreshPursuitDivision(command.CqbParent ?? command,tick);
            var pursuit = command.Contacts.Pursuit;
            pursuit.Expire(tick);
            var root=command.CqbParent ?? command;
            var assignment=root.Link.PursuitDivision.Assignment;
            bool assigned=root.Link.PursuitDivision.Role(root.Id,root.Goal,tick)==TacticalPursuitRole.Clear
                && assignment.Lead.IsValid && assignment.EnemyId>=0 && assignment.LeadTick<=tick
                && tick-assignment.LeadTick<TacticalContactMemory.RetentionTicks;
            if (assigned) pursuit.AssignedLead(assignment.EnemyId,assignment.Lead,assignment.LeadTick);
            int selectedId=-1, selectedTick=-1;
            IntVec3 selected=IntVec3.Invalid;
            void Consider(int id, IntVec3 area, int seen)
            {
                if (seen < pursuit.SeenTick || seen < selectedTick || seen > tick
                    || tick-seen >= TacticalContactMemory.RetentionTicks || !area.InBounds(map) || !area.Roofed(map)) return;
                // Equal-time samples keep the current identity instead of
                // resetting a room survey once for every remembered enemy.
                if (!TacticalCqbPursuit.PreferSample(id,seen,selectedId,selectedTick,pursuit.EnemyId)) return;
                selectedId=id; selectedTick=seen; selected=area;
            }
            // Only remembered visible samples nominate a room. A report's
            // door Area already points to its observed adjacent floor.
            if (!assigned)
            {
                foreach (var contact in command.Contacts.Memory.Entries)
                    Consider(contact.EnemyId,contact.Area,contact.SeenTick);
                foreach (var threat in command.Contacts.Melee.Entries)
                    if (threat.Urgency > 0) Consider(threat.EnemyId,threat.Position,threat.SeenTick);
            }
            if (selected.IsValid) pursuit.Observe(selectedId,selected,selectedTick);
            void SharedLeadExits()
            {
                if (!assigned || pursuit.Entered || !command.SecuredKnowledge.Cleared(pursuit.Target)) return;
                var exits=command.SecuredKnowledge.ClearedFrontiersAt(pursuit.Target);
                if (exits.Count>0) pursuit.RecordClearedLead(exits);
            }
            if (!pursuit.Active(tick)) return true;
            if (pursuit.RoomReady) { SharedLeadExits(); return true; }
            var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!ownsPlanAllowance && !budget.TryPlan(tick)) return false;
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (pursuit.Survey == null)
                {
                    var kinds = new Dictionary<IntVec3,byte>();
                    byte Kind(IntVec3 cell)
                    {
                        if (kinds.TryGetValue(cell,out byte kind)) return kind;
                        if (!cell.InBounds(map)) kind=0;
                        else if (cell.GetEdifice(map) is Building_Door) kind=3;
                        else if (cell.GetEdifice(map)?.def.IsWall == true) kind=2;
                        else kind=cell.Walkable(map) ? cell.Roofed(map) ? (byte)1 : (byte)4 : (byte)0;
                        kinds[cell]=kind; return kind;
                    }
                    bool Floor(IntVec3 cell) => Kind(cell)==1 && (cell==pursuit.Target
                        || !TacticalPortalGeometry.IsGap(cell,c=>Kind(c)==2,c=>Kind(c)==1 || Kind(c)==4));
                    pursuit.Survey=new TacticalSupportRoomSurvey(pursuit.Target,Floor);
                }
                if (!pursuit.Survey.Step()) return false;
                pursuit.CompleteRoom(pursuit.Survey.Cells,pursuit.Survey.Truncated);
                SharedLeadExits();
                return true;
            }
            finally { if (!ownsPlanAllowance) budget.Account(tick,Stopwatch.GetTimestamp()-started); }
        }
    }
}
