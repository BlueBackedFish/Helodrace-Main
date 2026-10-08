using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    // Value-only knowledge: no enemy Pawn reference or future job/destination.
    public sealed class TacticalContact
    {
        public int EnemyId, SeenTick;
        public IntVec3 Position, Area;
        public bool Door;
    }

    public sealed class TacticalContactMemory
    {
        public const int Limit = 8, FreshTicks = 240, RetentionTicks = 1800;
        private readonly List<TacticalContact> entries = new List<TacticalContact>(Limit);
        public IReadOnlyList<TacticalContact> Entries => entries;
        public void Remember(int enemyId, IntVec3 position, IntVec3 area, bool door, int tick)
        {
            TacticalContact contact = entries.Find(value => value.EnemyId == enemyId);
            if (contact == null)
            {
                if (entries.Count == Limit)
                {
                    int oldest = 0;
                    for (int i = 1; i < entries.Count; i++)
                        if (entries[i].SeenTick < entries[oldest].SeenTick) oldest = i;
                    entries.RemoveAt(oldest);
                }
                entries.Add(contact = new TacticalContact { EnemyId = enemyId });
            }
            contact.Position = position; contact.Area = area; contact.Door = door; contact.SeenTick = tick;
        }
        public void Expire(int tick) => entries.RemoveAll(value => tick - value.SeenTick >= RetentionTicks);
        public static bool Rear(IntVec3 anchor, IntVec3 forward, IntVec3 target) =>
            (target.x - anchor.x) * forward.x + (target.z - anchor.z) * forward.z < 0;
        public static bool Opposed(IntVec3 anchor, IntVec3 a, IntVec3 b)
        {
            IntVec3 x = a - anchor, y = b - anchor;
            long dot = (long)x.x * y.x + (long)x.z * y.z;
            return dot < 0 && dot * dot * 4 >= (long)x.LengthHorizontalSquared * y.LengthHorizontalSquared;
        }
        public static bool Fresh(TacticalContact contact, int tick) => tick - contact.SeenTick < FreshTicks;
    }

    public sealed class TacticalContactState
    {
        public const int ScanInterval = 45, CandidateLimit = 16, Radius = 28;
        public readonly TacticalContactMemory Memory = new TacticalContactMemory();
        public readonly List<IntVec3> Destinations = new List<IntVec3>(5), Sources = new List<IntVec3>(5);
        public int NextScan, ObserverCursor, CandidateCursor;
    }

    public static class TacticalContactSight
    {
        // Native pawn target/source lean geometry, independent of weapon range.
        // Buffers belong to a command/driver and are reused across candidates.
        public static bool CanSee(Map map, IntVec3 source, Pawn target, TacticalContactState state, int radius = TacticalContactState.Radius)
        {
            if (target?.Spawned != true || target.Map != map || source.DistanceToSquared(target.Position) > radius * radius) return false;
            bool From(IntVec3 from)
            {
                state.Destinations.Clear(); ShootLeanUtility.CalcShootableCellsOf(state.Destinations, target, from);
                foreach (IntVec3 cell in state.Destinations)
                    if (GenSight.LineOfSight(from, cell, map, true)) return true;
                return false;
            }
            if (From(source)) return true;
            state.Sources.Clear(); ShootLeanUtility.LeanShootingSourcesFromTo(source, target.Position, map, state.Sources);
            foreach (IntVec3 cell in state.Sources) if (cell != source && From(cell)) return true;
            return false;
        }
    }
}
