using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public static class TacticalFieldDoctrine
    {
        public const int LowRadius = 24, LowMainRadius = 14, LowFlankRadius = 4;
        public const int FlankInterval = 900, FlankLifetime = 600, FlankGatherTicks = 240;
        public const int RetreatDelay = 600, RetreatInterval = 300, SoloSteps = 6, GroupSteps = 12;
        public const int RadioLife = 600, GroupLimit = 4, GroupSpacing = 15;
        public static bool EquipmentWork(Job job) => job?.def?.defName?.IndexOf("Reload", StringComparison.OrdinalIgnoreCase) >= 0;
        public static bool Within(IntVec3 origin, IntVec3 cell, int radius) => cell.DistanceToSquared(origin) <= radius * radius;
        public static int FlankSize(int live) => live < 6 ? 0 : Math.Min(4, Math.Max(3, live / 3));
        public static bool FlankOpportunity(int tick, int next, bool defensive, bool support, int live, bool automatic, int seed) =>
            tick >= next && !defensive && !support && FlankSize(live) > 0 && automatic && (uint)seed % 3 == 0;
        public static IntVec3 FlankPoint(IntVec3 anchor, IntVec3 forward, int side, bool attack) =>
            anchor + new IntVec3(-forward.z, 0, forward.x) * (side * 16) + forward * (attack ? 12 : 0);
        public static IntVec3 LinePoint(IntVec3 anchor, IntVec3 forward, int point, int seed) =>
            anchor + Offset(forward, (point - 1) * (((7 + (int)((uint)seed % 3)) * 5 + 1) / 2), 0);
        // Forward is an observed direction vector, not a cardinal tile step.
        public static IntVec3 Facing(IntVec3 from, IntVec3 to) => from == to ? IntVec3.North : to - from;
        public static IntVec3 Offset(IntVec3 forward, double lateral, double depth)
        {
            double length = Math.Sqrt((double)forward.x * forward.x + (double)forward.z * forward.z);
            if (length == 0) return IntVec3.Zero;
            return new IntVec3((int)Math.Round((-forward.z * lateral + forward.x * depth) / length, MidpointRounding.AwayFromZero), 0,
                (int)Math.Round((forward.x * lateral + forward.z * depth) / length, MidpointRounding.AwayFromZero));
        }
        public static IntVec3 WithoutAdvance(IntVec3 previous, IntVec3 next, IntVec3 forward)
        {
            double dot = (double)(next.x - previous.x) * forward.x + (double)(next.z - previous.z) * forward.z;
            if (dot <= 0) return next;
            double squared = (double)forward.x * forward.x + (double)forward.z * forward.z;
            // Round each correction outward so tile quantization cannot leave an advance.
            return next - new IntVec3(Math.Sign(forward.x) * (int)Math.Ceiling(Math.Abs(forward.x * dot / squared)), 0,
                Math.Sign(forward.z) * (int)Math.Ceiling(Math.Abs(forward.z * dot / squared)));
        }
        public static int RetreatDepth(int step, int slot, int slots)
        {
            if (slots <= 3) return Math.Min(SoloSteps, step) * (slot == 1 ? 2 : 1);
            int middle = slots - 1, distance = Math.Abs(2 * slot - middle);
            // Global centre withdraws up to 24 cells; outer groups only six.
            return Math.Min(GroupSteps, step) * (4 * middle - 3 * distance) / (2 * middle);
        }
        public static bool RetreatDue(int tick, int started, int next, int lastEngaged, int step, bool grouped) =>
            lastEngaged >= 0 && tick >= lastEngaged && tick - lastEngaged <= 180 && tick - started >= RetreatDelay
                && tick >= next && step < (grouped ? GroupSteps : SoloSteps);
    }

    // Value-only radio formation. No live peer/target references or global
    // pawn graph; membership arrives exclusively through delivered packets.
    public sealed class TacticalFieldFormation : IExposable
    {
        public string Coordinator;
        public List<string> Members = new List<string>();
        public Dictionary<string, int> Seen = new Dictionary<string, int>();
        public IntVec3 Origin, Forward;
        public int Started, Step, Until, LastEngaged = -1, LastPacket = -1;
        public TacticalFieldFormation Copy() => new TacticalFieldFormation { Coordinator = Coordinator,
            Members = new List<string>(Members), Seen = new Dictionary<string, int>(Seen), Origin = Origin, Forward = Forward, Started = Started,
            Step = Step, Until = Until, LastEngaged = LastEngaged, LastPacket = LastPacket };
        public bool Receive(TacticalFieldFormation report, string own, string sender, int sent, int tick)
        {
            if (report == null || string.IsNullOrEmpty(own) || string.IsNullOrEmpty(sender)
                || tick < sent || tick - sent >= TacticalFieldDoctrine.RadioLife || sent < LastPacket
                || !report.Members.Contains(sender) || report.Members.Count > TacticalFieldDoctrine.GroupLimit
                || !report.Members.Contains(report.Coordinator) || report.Forward == IntVec3.Zero
                || report.Started > sent || report.LastEngaged > sent || report.Step < 0 || report.Step > TacticalFieldDoctrine.GroupSteps
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
            { Coordinator = report.Coordinator; Origin = report.Origin; Forward = report.Forward; Started = report.Started; Step = report.Step; }
            else if (report.Coordinator == Coordinator) Step = Math.Max(Step, report.Step);
            Members = merged; Seen = merged.ToDictionary(id => id, id => dates[id]); LastEngaged = Math.Max(LastEngaged, report.LastEngaged);
            Until = tick + TacticalFieldDoctrine.RadioLife; LastPacket = sent;
            return true;
        }
        public bool Expire(string own, int tick)
        {
            Seen[own] = tick;
            Members.RemoveAll(id => id != own && (!Seen.TryGetValue(id, out int seen) || tick - seen >= TacticalFieldDoctrine.RadioLife));
            foreach (string id in Seen.Keys.Where(id => !Members.Contains(id)).ToArray()) Seen.Remove(id);
            return !Members.Contains(Coordinator) || Members.Count <= 1;
        }
        public IntVec3 Point(string squad, int point)
        {
            int rank = Members.IndexOf(squad); if (rank < 0) rank = 0;
            int slots = Members.Count * 3, slot = rank * 3 + point;
            int lateral = slot * TacticalFieldDoctrine.GroupSpacing - (slots - 1) * TacticalFieldDoctrine.GroupSpacing / 2;
            return Origin + TacticalFieldDoctrine.Offset(Forward, lateral, -TacticalFieldDoctrine.RetreatDepth(Step, slot, slots));
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Coordinator, "coordinator"); Scribe_Collections.Look(ref Members, "members", LookMode.Value);
            Scribe_Collections.Look(ref Seen, "seen", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref Origin, "origin"); Scribe_Values.Look(ref Forward, "forward");
            Scribe_Values.Look(ref Started, "started"); Scribe_Values.Look(ref Step, "step"); Scribe_Values.Look(ref Until, "until");
            Scribe_Values.Look(ref LastEngaged, "lastEngaged", -1); Scribe_Values.Look(ref LastPacket, "lastPacket", -1);
        }
    }
}
