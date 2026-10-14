using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public static class TacticalFieldDoctrine
    {
        public const int LowRadius = 25, LowMainRadius = 25, LowFlankRadius = 6;
        public const int LowDepth = 18, LowMainDepth = 12, LowFlankDepth = 4;
        public const int RadioLife = 600, GroupLimit = 4;
        public static bool EquipmentWork(Job job) => job?.def?.defName?.IndexOf("Reload", StringComparison.OrdinalIgnoreCase) >= 0;
        public static bool Within(IntVec3 origin, IntVec3 cell, int radius) => cell.DistanceToSquared(origin) <= radius * radius;
        public static int FlankSize(int live) => live < 6 ? 0 : Math.Min(4, Math.Max(3, live / 3));
        public static bool WithinEllipse(IntVec3 origin, IntVec3 cell, IntVec3 forward, int width, int depth)
        {
            if (forward == IntVec3.Zero) forward = IntVec3.North;
            double x = cell.x - origin.x, z = cell.z - origin.z;
            double lateral = -forward.z * x + forward.x * z, along = forward.x * x + forward.z * z;
            double lengthSquared = (double)forward.x * forward.x + (double)forward.z * forward.z;
            return lateral * lateral / (width * width) + along * along / (depth * depth) <= lengthSquared;
        }
        // Forward is an observed direction vector, not a cardinal tile step.
        public static IntVec3 Facing(IntVec3 from, IntVec3 to) => from == to ? IntVec3.North : to - from;
        public static IntVec3 Offset(IntVec3 forward, double lateral, double depth)
        {
            double length = Math.Sqrt((double)forward.x * forward.x + (double)forward.z * forward.z);
            if (length == 0) return IntVec3.Zero;
            return new IntVec3((int)Math.Round((-forward.z * lateral + forward.x * depth) / length, MidpointRounding.AwayFromZero), 0,
                (int)Math.Round((forward.x * lateral + forward.z * depth) / length, MidpointRounding.AwayFromZero));
        }
    }

    // Value-only radio formation. No live peer/target references or global
    // pawn graph; membership arrives exclusively through delivered packets.
    public sealed class TacticalFieldFormation : IExposable
    {
        public string Coordinator;
        public List<string> Members = new List<string>();
        public Dictionary<string, int> Seen = new Dictionary<string, int>();
        public Dictionary<string, TacticalFieldSector> Sectors = new Dictionary<string, TacticalFieldSector>();
        public IntVec3 Origin, Forward;
        public int Started, Until, LastEngaged = -1, LastPacket = -1;
        public TacticalFieldFormation Copy() => new TacticalFieldFormation { Coordinator = Coordinator,
            Members = new List<string>(Members), Seen = new Dictionary<string, int>(Seen), Origin = Origin, Forward = Forward, Started = Started,
            Until = Until, LastEngaged = LastEngaged, LastPacket = LastPacket,
            Sectors = Sectors.ToDictionary(p => p.Key,p => p.Value.Copy()) };
        public bool Receive(TacticalFieldFormation report, string own, string sender, int sent, int tick)
        {
            if (report == null || string.IsNullOrEmpty(own) || string.IsNullOrEmpty(sender)
                || tick < sent || tick - sent >= TacticalFieldDoctrine.RadioLife || sent < LastPacket
                || !report.Members.Contains(sender) || report.Members.Count > TacticalFieldDoctrine.GroupLimit
                || !report.Members.Contains(report.Coordinator) || report.Forward == IntVec3.Zero
                || report.Started > sent || report.LastEngaged > sent
                || report.Members.Distinct().Count() != report.Members.Count
                || report.Coordinator != report.Members.OrderBy(id => id, StringComparer.Ordinal).First()) return false;
            var dates = new Dictionary<string, int>(Seen);
            foreach (var pair in report.Seen)
                if (pair.Value <= sent && tick - pair.Value < TacticalFieldDoctrine.RadioLife
                    && (!dates.TryGetValue(pair.Key, out int previous) || pair.Value > previous)) dates[pair.Key] = pair.Value;
            dates[own] = tick; dates[sender] = sent;
            var merged = Members.Concat(report.Members).Concat(new[] { own, sender }).Distinct()
                .Where(id => dates.TryGetValue(id, out int seen) && tick - seen < TacticalFieldDoctrine.RadioLife).OrderBy(x => x, StringComparer.Ordinal)
                .Take(TacticalFieldDoctrine.GroupLimit).ToList();
            if (!merged.Contains(own) || !merged.Contains(sender)) return false;
            if (!merged.Contains(Coordinator) && !merged.Contains(report.Coordinator)) return false;
            if (string.CompareOrdinal(report.Coordinator, Coordinator) < 0)
            { Coordinator = report.Coordinator; Origin = report.Origin; Forward = report.Forward; Started = report.Started; }
            Members = merged; Seen = merged.ToDictionary(id => id, id => dates[id]); LastEngaged = Math.Max(LastEngaged, report.LastEngaged);
            foreach (var pair in report.Sectors)
                if (Members.Contains(pair.Key) && pair.Value != null && pair.Value.At <= sent && tick - pair.Value.At < TacticalFieldDoctrine.RadioLife
                    && (!Sectors.TryGetValue(pair.Key,out var previous) || pair.Value.At > previous.At)) Sectors[pair.Key] = pair.Value.Copy();
            foreach (string id in Sectors.Keys.Where(id => !Members.Contains(id)).ToArray()) Sectors.Remove(id);
            Until = tick + TacticalFieldDoctrine.RadioLife; LastPacket = sent;
            return true;
        }
        public bool Expire(string own, int tick)
        {
            Seen[own] = tick;
            Members.RemoveAll(id => id != own && (!Seen.TryGetValue(id, out int seen) || tick - seen >= TacticalFieldDoctrine.RadioLife));
            foreach (string id in Seen.Keys.Where(id => !Members.Contains(id)).ToArray()) Seen.Remove(id);
            foreach (string id in Sectors.Keys.Where(id => !Members.Contains(id)).ToArray()) Sectors.Remove(id);
            return !Members.Contains(Coordinator) || Members.Count <= 1;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Coordinator, "coordinator"); Scribe_Collections.Look(ref Members, "members", LookMode.Value);
            Scribe_Collections.Look(ref Seen, "seen", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref Sectors, "sectors", LookMode.Value, LookMode.Deep);
            Scribe_Values.Look(ref Origin, "origin"); Scribe_Values.Look(ref Forward, "forward");
            Scribe_Values.Look(ref Started, "started"); Scribe_Values.Look(ref Until, "until");
            Scribe_Values.Look(ref LastEngaged, "lastEngaged", -1); Scribe_Values.Look(ref LastPacket, "lastPacket", -1);
        }
    }
}
