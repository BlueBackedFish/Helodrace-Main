using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace
{
    // Detached snapshots only. Receiving or forwarding never reads an enemy's live position.
    public sealed class RaidTacticalReport : IExposable
    {
        public string Id, OriginUnit;
        public int Revision, ObservedTick, ReceivedTick, ObserverId, StructureVersion, Room, EnemyId;
        public RaidReportKind Kind;
        public IntVec3 Position = IntVec3.Invalid, Portal = IntVec3.Invalid, Direction = IntVec3.Zero;
        public string Label;
        public bool Armed, ConfirmedEmpty, Usable, IsPortal;
        public float Range;
        public int Building;
        public List<string> Route = new List<string>();

        public RaidTacticalReport Copy() => new RaidTacticalReport {
            Id = Id, OriginUnit = OriginUnit, Revision = Revision, ObservedTick = ObservedTick,
            ReceivedTick = ReceivedTick, ObserverId = ObserverId, StructureVersion = StructureVersion,
            Room = Room, EnemyId = EnemyId, Kind = Kind, Position = Position, Portal = Portal,
            Direction = Direction, Label = Label, Armed = Armed, ConfirmedEmpty = ConfirmedEmpty,
            Usable = Usable, IsPortal = IsPortal, Range = Range, Building = Building,
            Route = new List<string>(Route)
        };

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref OriginUnit, "origin");
            Scribe_Values.Look(ref Revision, "revision"); Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref ObservedTick, "observed"); Scribe_Values.Look(ref ReceivedTick, "received");
            Scribe_Values.Look(ref ObserverId, "observer"); Scribe_Values.Look(ref StructureVersion, "version");
            Scribe_Values.Look(ref Room, "room"); Scribe_Values.Look(ref EnemyId, "enemy");
            Scribe_Values.Look(ref Position, "position", IntVec3.Invalid);
            Scribe_Values.Look(ref Portal, "portal", IntVec3.Invalid);
            Scribe_Values.Look(ref Direction, "direction", IntVec3.Zero);
            Scribe_Values.Look(ref Label, "label"); Scribe_Values.Look(ref Armed, "armed");
            Scribe_Values.Look(ref ConfirmedEmpty, "empty"); Scribe_Values.Look(ref Range, "range");
            Scribe_Values.Look(ref Usable, "usable"); Scribe_Values.Look(ref IsPortal, "isPortal");
            Scribe_Values.Look(ref Building, "building");
            Scribe_Collections.Look(ref Route, "route", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Route == null) Route = new List<string>();
        }
    }

    public sealed class RaidReportLedger : IExposable
    {
        public List<RaidTacticalReport> Reports = new List<RaidTacticalReport>();
        public bool Knows(RaidTacticalReport report) => Reports.Any(known => known.Id == report.Id && known.Revision >= report.Revision);
        public bool Receive(RaidTacticalReport report, string receiver, int tick, bool internalRelay)
        {
            if (report == null || string.IsNullOrEmpty(report.Id) || !report.Position.IsValid
                || !RaidCommunicationPolicy.Fresh(report.ObservedTick, tick)
                || !RaidCommunicationPolicy.CanRelay(report.Route, receiver, internalRelay) || Knows(report)) return false;
            RaidTacticalReport copy = report.Copy();
            if (!internalRelay) copy.Route.Add(receiver);
            copy.ReceivedTick = tick;
            return Store(copy);
        }
        public void Publish(RaidTacticalReport report) => Store(report.Copy());
        private bool Store(RaidTacticalReport report)
        {
            Reports.RemoveAll(known => known.Id == report.Id);
            Reports.Add(report);
            if (Reports.Count > RaidCommunicationPolicy.LedgerCapacity)
            {
                RaidTacticalReport oldest = Reports.OrderBy(known => known.ObservedTick).First();
                Reports.Remove(oldest);
                // A delayed incoming report can itself be the discarded entry.
                // Its caller may only apply intelligence that the ledger retained.
                return oldest != report;
            }
            return true;
        }
        public void Prune(int tick) => Reports.RemoveAll(report => !RaidCommunicationPolicy.Fresh(report.ObservedTick, tick));
        public void ExposeData()
        {
            Scribe_Collections.Look(ref Reports, "reports", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Reports == null) Reports = new List<RaidTacticalReport>();
        }
    }

    public sealed class RaidObserverMemory : IExposable
    {
        public int PawnId;
        public RaidContactMemory Contacts = new RaidContactMemory();
        public RaidReportLedger Reports = new RaidReportLedger();
        public void ExposeData()
        {
            Scribe_Values.Look(ref PawnId, "pawnId");
            Scribe_Deep.Look(ref Contacts, "contacts"); Scribe_Deep.Look(ref Reports, "reports");
        }
    }

    public sealed class RaidReportReceipt : IExposable
    {
        public string Peer, ReportId, Status;
        public int Revision, Tick;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Peer, "peer"); Scribe_Values.Look(ref ReportId, "report");
            Scribe_Values.Look(ref Revision, "revision"); Scribe_Values.Look(ref Tick, "tick");
            Scribe_Values.Look(ref Status, "status");
        }
    }

    public sealed class RaidCommunicationState : IExposable
    {
        public RaidReportLedger Knowledge = new RaidReportLedger();
        public List<RaidObserverMemory> Observers = new List<RaidObserverMemory>();
        public List<RaidReportReceipt> Receipts = new List<RaidReportReceipt>();
        public string Status = "No connection checked";
        public RaidObserverMemory For(int pawnId)
        {
            RaidObserverMemory result = Observers.FirstOrDefault(value => value.PawnId == pawnId);
            if (result == null) Observers.Add(result = new RaidObserverMemory { PawnId = pawnId });
            return result;
        }
        public void Receipt(string peer, RaidTacticalReport report, string status, int tick)
        {
            Receipts.RemoveAll(value => value.Peer == peer && value.ReportId == report.Id);
            Receipts.Add(new RaidReportReceipt { Peer = peer, ReportId = report.Id, Revision = report.Revision, Status = status, Tick = tick });
            if (Receipts.Count > 64) Receipts.RemoveAt(0);
        }
        public void ExposeData()
        {
            Scribe_Deep.Look(ref Knowledge, "knowledge");
            Scribe_Collections.Look(ref Observers, "observers", LookMode.Deep);
            Scribe_Collections.Look(ref Receipts, "receipts", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Status = "Rechecking saved connections";
        }
    }
}
