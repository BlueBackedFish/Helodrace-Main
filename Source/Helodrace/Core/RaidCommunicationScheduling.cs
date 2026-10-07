using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Helodrace.Squads;
using Verse;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalCommunications
    {
        private readonly TacticalDueQueue<string> frameWork = new TacticalDueQueue<string>();
        private readonly Dictionary<string, RaidExecutionTicket> frameTickets = new Dictionary<string, RaidExecutionTicket>();
        private int rosterAfter, blackoutAfter;
        internal long FrameBuilds, EquipmentScans;
        internal int MaximumFrameWork;
        internal int FramePending => frameWork.Count;
        private sealed class EquipmentSnapshot
        {
            internal int Tick;
            internal List<CompTacticalRadio> Items;
        }
        private readonly Dictionary<Pawn, EquipmentSnapshot> equipment = new Dictionary<Pawn, EquipmentSnapshot>();
        private readonly Dictionary<Pawn, EquipmentSnapshot> deliveryEquipment = new Dictionary<Pawn, EquipmentSnapshot>();

        private List<CompTacticalRadio> Equipment(Pawn pawn, int tick)
        {
            if (!equipment.TryGetValue(pawn, out EquipmentSnapshot snapshot) || tick < snapshot.Tick || tick - snapshot.Tick >= 600)
            {
                snapshot = new EquipmentSnapshot { Tick = tick, Items = RaidTacticalRadioUtility.Radios(pawn).ToList() };
                equipment[pawn] = snapshot; EquipmentScans++;
            }
            return snapshot.Items;
        }
        private List<CompTacticalRadio> DeliveryRadios(Pawn pawn)
        {
            if (!deliveryEquipment.TryGetValue(pawn, out EquipmentSnapshot snapshot))
            {
                snapshot = new EquipmentSnapshot { Items = RaidTacticalRadioUtility.InstalledRadios(
                    pawn.apparel?.WornApparel ?? Enumerable.Empty<Apparel>()).ToList() };
                deliveryEquipment[pawn] = snapshot;
            }
            return snapshot.Items;
        }
        private bool DeliveryRadioContact(RaidCommunicationFrame source, RaidCommunicationFrame receiver, Pawn a, Pawn b)
        {
            foreach (CompTacticalRadio first in DeliveryRadios(a))
                foreach (CompTacticalRadio second in DeliveryRadios(b))
                    if (RaidCommunicationPolicy.RadioCompatible(source.Doctrine?.tacticalRadio == true,
                        receiver.Doctrine?.tacticalRadio == true, first.Operational, second.Operational, blackout,
                        first.RadioProperties.network, second.RadioProperties.network, a.Position.DistanceToSquared(b.Position),
                        System.Math.Min(first.RadioProperties.range, second.RadioProperties.range))) return true;
            return false;
        }
        internal void InvalidateUnit(string id)
        {
            if (id == null || !frameTickets.TryGetValue(id, out RaidExecutionTicket ticket)) return;
            frames.Remove(id);
            foreach (Pawn pawn in ticket.Members) equipment.Remove(pawn);
            frameWork.Schedule(id, GenTicks.TicksGame + 1);
        }
        private void PumpFrames(int tick)
        {
            var execution = map.GetComponent<MapComponent_RaidTacticalExecution>();
            if (execution == null) return;
            if (tick >= blackoutAfter) { blackout = SCR300RadioUtility.IsBlackout(map); blackoutAfter = tick + 20; }
            if (tick >= rosterAfter)
            {
                rosterAfter = tick + 180;
                var live = new HashSet<string>();
                foreach (RaidExecutionTicket ticket in execution.CommunicationRoster)
                {
                    string id = ticket.Unit.Id; live.Add(id);
                    if (!frameTickets.ContainsKey(id)) frameWork.Schedule(id, tick + RaidCommunicationPolicy.ScanOffset(id));
                    frameTickets[id] = ticket;
                }
                foreach (string id in frameTickets.Keys.Where(id => !live.Contains(id)).ToList())
                { frameTickets.Remove(id); frames.Remove(id); frameWork.Remove(id); }
                var people = new HashSet<Pawn>(frameTickets.Values.SelectMany(ticket => ticket.Members));
                foreach (Pawn pawn in equipment.Keys.Where(pawn => !people.Contains(pawn)).ToList()) equipment.Remove(pawn);
            }
            long start = Stopwatch.GetTimestamp(), allowance = Stopwatch.Frequency / 2000;
            int processed = 0;
            while (processed < 2 && Stopwatch.GetTimestamp() - start < allowance && frameWork.TryTake(tick, out string id, out _))
            {
                if (!frameTickets.TryGetValue(id, out RaidExecutionTicket ticket) || !execution.CurrentTicket(ticket)) continue;
                processed++;
                try { RefreshFrame(ticket, execution, tick); }
                finally
                {
                    int due = tick + 120;
                    if (frames.TryGetValue(id, out RaidCommunicationFrame frame)) due = System.Math.Min(due, frame.BuiltTick + 300);
                    frameWork.Schedule(id, System.Math.Max(tick + 1, due));
                }
            }
            MaximumFrameWork = System.Math.Max(MaximumFrameWork, processed);
        }
        private void RefreshFrame(RaidExecutionTicket ticket, MapComponent_RaidTacticalExecution execution, int tick)
        {
            RaidTacticalUnit unit = ticket.Unit;
            var state = execution.StateFor(unit.Id);
            if (state?.ActivePlan?.Success != true) { frames.Remove(unit.Id); return; }
            List<Pawn> members = ticket.Members.Where(pawn => pawn?.Spawned == true && pawn.Map == map
                && !pawn.Dead && !pawn.Downed && !pawn.InMentalState && execution.ControlsPawn(pawn)
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)).ToList();
            if (members.Count == 0) { frames.Remove(unit.Id); return; }
            Pawn commander = members.Contains(unit.Commander) ? unit.Commander : null;
            var radios = unit.Organization.doctrine?.tacticalRadio == true
                ? members.ToDictionary(pawn => pawn.thingIDNumber, pawn => Equipment(pawn, tick)) : null;
            int revision = RaidPhysicalMapCache.For(map).StructureRevision;
            if (frames.TryGetValue(unit.Id, out RaidCommunicationFrame previous) && previous.State == state
                && previous.Unit.Organization.doctrine == unit.Organization.doctrine
                && previous.Reusable(members, commander, radios, tick, revision, blackout))
            { previous.Unit = unit; return; }
            var frame = new RaidCommunicationFrame { Unit = unit, State = state, Commander = commander,
                BuiltTick = tick, StructureRevision = revision, Blackout = blackout,
                Members = members.ToDictionary(pawn => pawn.thingIDNumber), Radios = radios ?? new Dictionary<int, List<CompTacticalRadio>>() };
            foreach (Pawn pawn in members) frame.Positions.Add(pawn.thingIDNumber, pawn.Position);
            frame.CommandDelays = commander != null ? RaidCommunicationPolicy.Delays(frame.Members.Keys.ToList(), commander.thingIDNumber,
                (a, b) => frame.Edge(a, b, blackout)) : new Dictionary<int, int>();
            frames[unit.Id] = frame; FrameBuilds++;
            if (Prefs.DevMode) state.Communication.Status = $"Command={commander?.LabelShort ?? "none"}; connected {frame.CommandDelays.Count}/{members.Count}; "
                + $"radio operators={frame.Radios.Count(value => value.Value.Count > 0)}; blackout={blackout}";
        }
        public override void MapRemoved()
        {
            frames.Clear(); frameTickets.Clear(); frameWork.Clear(); equipment.Clear(); deliveryEquipment.Clear(); pending.Clear();
            base.MapRemoved();
        }
    }
}
