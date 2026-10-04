using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace Helodrace
{
    public enum RaidContactConfidence { Visible, Recent, Area, Fading, Expired }

    // No enemy Pawn reference: unseen movement cannot update this snapshot.
    public sealed class RaidEnemyContact : IExposable
    {
        public int EnemyId, ObserverId, Room, SeenTick, LostTick = -1;
        public string Label;
        public IntVec3 Position = IntVec3.Invalid, Previous = IntVec3.Invalid;
        public IntVec3 Portal = IntVec3.Invalid, Direction = IntVec3.Zero;
        public bool Visible, PositionConfirmedEmpty, Armed;
        public float Range;
        public RaidContactConfidence Confidence(int tick) => tick - SeenTick >= 1200 ? RaidContactConfidence.Expired
            : Visible && tick - SeenTick <= 30 ? RaidContactConfidence.Visible
            : !PositionConfirmedEmpty && tick - SeenTick <= 120 ? RaidContactConfidence.Recent
            : tick - SeenTick < 600 ? RaidContactConfidence.Area : RaidContactConfidence.Fading;
        public IntVec3 WatchPoint => Portal.IsValid ? Portal : Position;
        public void ExposeData()
        {
            Scribe_Values.Look(ref EnemyId, "enemyId");
            Scribe_Values.Look(ref ObserverId, "observerId");
            Scribe_Values.Look(ref Label, "label");
            Scribe_Values.Look(ref Room, "room");
            Scribe_Values.Look(ref SeenTick, "seenTick");
            Scribe_Values.Look(ref LostTick, "lostTick", -1);
            Scribe_Values.Look(ref Position, "position", IntVec3.Invalid);
            Scribe_Values.Look(ref Previous, "previous", IntVec3.Invalid);
            Scribe_Values.Look(ref Portal, "portal", IntVec3.Invalid);
            Scribe_Values.Look(ref Direction, "direction", IntVec3.Zero);
            Scribe_Values.Look(ref Visible, "visible");
            Scribe_Values.Look(ref PositionConfirmedEmpty, "positionConfirmedEmpty");
            Scribe_Values.Look(ref Armed, "armed");
            Scribe_Values.Look(ref Range, "range");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Visible = false;
        }
    }

    public sealed class RaidContactMemory : IExposable
    {
        public const int Capacity = 16, ScanTicks = 20, Radius = 24;
        public List<RaidEnemyContact> Entries = new List<RaidEnemyContact>();
        internal int ScanTick = -ScanTicks;
        public RaidEnemyContact Observe(int id, string label, IntVec3 position, int room, int observer,
            int tick, IntVec3 portal, bool armed, float range)
        {
            RaidEnemyContact contact = Entries.FirstOrDefault(value => value.EnemyId == id);
            if (contact == null)
            {
                contact = new RaidEnemyContact { EnemyId = id };
                Entries.Add(contact);
            }
            bool continuous = contact.Position.IsValid && tick - contact.SeenTick <= 40 && contact.LostTick < 0;
            contact.Previous = continuous ? contact.Position : IntVec3.Invalid;
            contact.Direction = continuous ? position - contact.Position : IntVec3.Zero;
            // An unobserved interval cannot establish a route through an old doorway.
            if (!continuous) contact.Portal = IntVec3.Invalid;
            if (portal.IsValid) contact.Portal = portal;
            contact.Label = label; contact.Position = position; contact.Room = room;
            contact.ObserverId = observer; contact.SeenTick = tick; contact.LostTick = -1;
            contact.Visible = true; contact.PositionConfirmedEmpty = false;
            contact.Armed = armed; contact.Range = range;
            if (Entries.Count > Capacity)
                Entries.Remove(Entries.Where(value => value != contact).OrderBy(value => value.SeenTick).First());
            return contact;
        }
        public void FinishScan(int tick, ISet<int> observed)
        {
            foreach (RaidEnemyContact contact in Entries)
                if (!observed.Contains(contact.EnemyId) && contact.SeenTick != tick)
                {
                    if (contact.Visible) contact.LostTick = tick;
                    contact.Visible = false;
                }
            Entries.RemoveAll(contact => contact.Confidence(tick) == RaidContactConfidence.Expired);
        }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref Entries, "entries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Entries == null) Entries = new List<RaidEnemyContact>();
                ScanTick = -ScanTicks;
            }
        }
        public bool CanTarget(int enemyId, IntVec3 target, int tick) => target.IsValid && Entries.Any(contact => contact.EnemyId == enemyId
            && contact.Position == target && !contact.PositionConfirmedEmpty && contact.Confidence(tick) <= RaidContactConfidence.Recent);
        public string Report(int tick)
        {
            var text = new StringBuilder();
            foreach (RaidEnemyContact contact in Entries.OrderByDescending(value => value.SeenTick))
                text.AppendLine($"  #{contact.EnemyId} {contact.Label}: {contact.Confidence(tick)} "
                    + $"last={contact.Position} R{contact.Room} age={(tick - contact.SeenTick) / 60f:0.0}s "
                    + $"portal={contact.Portal} direction={contact.Direction} observer=#{contact.ObserverId} "
                    + $"last cell empty={contact.PositionConfirmedEmpty}");
            return text.Length == 0 ? "  No observed contacts\n" : text.ToString();
        }
    }
}
