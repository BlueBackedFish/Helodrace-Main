using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace
{
    public sealed class RaidRoomSecurityRecord : IExposable
    {
        public int Room, LastThreatTick = -1, LastCheckedTick = -1, NextAttemptTick;
        public IntVec3 Concern = IntVec3.Invalid;
        public bool NeedsRecheck => LastThreatTick > LastCheckedTick;
        public bool RecentConcern(int tick) => NeedsRecheck && tick - LastThreatTick < 1200;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Room, "room");
            Scribe_Values.Look(ref LastThreatTick, "lastThreatTick", -1);
            Scribe_Values.Look(ref LastCheckedTick, "lastCheckedTick", -1);
            Scribe_Values.Look(ref NextAttemptTick, "nextAttemptTick");
            Scribe_Values.Look(ref Concern, "concern", IntVec3.Invalid);
        }
    }

    public sealed class RaidRoomSecurity : IExposable
    {
        public List<RaidRoomSecurityRecord> Rooms = new List<RaidRoomSecurityRecord>();
        public void Observe(int room, IntVec3 position, int tick)
        {
            if (room <= 0) return;
            RaidRoomSecurityRecord record = For(room);
            if (record == null) Rooms.Add(record = new RaidRoomSecurityRecord { Room = room });
            record.LastThreatTick = tick; record.Concern = position;
        }
        public RaidRoomSecurityRecord For(int room) => Rooms.FirstOrDefault(record => record.Room == room);
        public void Checked(int room, int tick)
        {
            RaidRoomSecurityRecord record = For(room);
            if (record != null) record.LastCheckedTick = tick;
        }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref Rooms, "rooms", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Rooms == null) Rooms = new List<RaidRoomSecurityRecord>();
        }
    }
}
