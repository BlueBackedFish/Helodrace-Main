using System;
using System.Collections.Generic;

namespace Helodrace
{
    public enum RaidReportKind { Contact, Passage, RoomChecked }
    public enum RaidCommunicationMode { Voice, Radio }
    public enum RaidTransmissionAction { Wait, Deliver, Acknowledge, Interrupt }

    public static class RaidCommunicationPolicy
    {
        public const int Lifetime = 1200, MaxHops = 8, LedgerCapacity = 64;
        public const int TickInterval = 20, PendingCapacity = 128, PairBudget = 8, QueueBudget = 16;

        public static bool Fresh(int observed, int now) => observed >= 0 && observed <= now && now - observed < Lifetime;
        public static int ScanOffset(string unit)
        {
            int hash = 0;
            foreach (char value in unit ?? "") hash = unchecked(hash * 31 + value) & int.MaxValue;
            return hash % TickInterval;
        }
        public static RaidTransmissionAction Transmission(bool connected, int observed, int started,
            int due, bool awaitingAck, int tick) => !connected || !Fresh(observed, tick) || tick - started > 600
                ? RaidTransmissionAction.Interrupt : tick < due ? RaidTransmissionAction.Wait
                : awaitingAck ? RaidTransmissionAction.Acknowledge : RaidTransmissionAction.Deliver;
        public static bool Newer(int incomingTick, bool incomingDirect, int knownTick, bool knownDirect) =>
            incomingTick > knownTick || incomingTick == knownTick && incomingDirect && !knownDirect;
        public static bool CanRelay(IList<string> route, string receiver, bool internalRelay) =>
            route != null && route.Count > 0 && route.Count <= MaxHops
            && (internalRelay ? route[route.Count - 1] == receiver
                : route.Count < MaxHops && !route.Contains(receiver));
        public static bool RadioCompatible(bool enabledA, bool enabledB, bool availableA, bool availableB,
            bool blackout, string networkA, string networkB, int distanceSquared, int range) =>
            enabledA && enabledB && availableA && availableB && !blackout && range > 0
            && !string.IsNullOrEmpty(networkA) && networkA == networkB && distanceSquared <= range * range;

        // A tiny personnel graph; no global map/path search or worker game-object access.
        public static Dictionary<int, int> Delays(IList<int> members, int commander,
            Func<int, int, int> edgeDelay)
        {
            var distances = new Dictionary<int, int>();
            if (!members.Contains(commander)) return distances;
            distances[commander] = 0;
            var done = new HashSet<int>();
            while (done.Count < members.Count)
            {
                int next = 0, cost = int.MaxValue;
                foreach (int id in members)
                    if (!done.Contains(id) && distances.TryGetValue(id, out int found) && found < cost)
                    { next = id; cost = found; }
                if (cost == int.MaxValue) break;
                done.Add(next);
                foreach (int id in members)
                {
                    if (done.Contains(id)) continue;
                    int delay = edgeDelay(next, id);
                    if (delay < 0) continue;
                    int total = cost + delay;
                    if (!distances.TryGetValue(id, out int old) || total < old) distances[id] = total;
                }
            }
            return distances;
        }
    }
}
