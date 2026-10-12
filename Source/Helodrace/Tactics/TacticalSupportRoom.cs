using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    // One sealed, roof-matched room footprint per opening. Native Room can
    // merge an opened interior with the entire outdoor map, so it cannot alone
    // decide which nonhostile pawns are actually inside the grenade's room.
    public sealed class TacticalSupportRoomSurvey
    {
        public const int CellLimit = 256, ProbesPerSlice = 64;
        public readonly HashSet<IntVec3> Cells = new HashSet<IntVec3>();
        public bool Finished, Truncated;
        private readonly Queue<IntVec3> pending = new Queue<IntVec3>();
        private readonly HashSet<IntVec3> seen = new HashSet<IntVec3>();
        private readonly Func<IntVec3,bool> floor;
        private int direction;
        public TacticalSupportRoomSurvey(IntVec3 seed, Func<IntVec3,bool> floor)
        {
            this.floor = floor; seen.Add(seed);
            if (floor(seed)) { Cells.Add(seed); pending.Enqueue(seed); } else Finished = true;
        }
        public bool Step()
        {
            int probes = 0;
            while (!Finished && pending.Count > 0 && probes < ProbesPerSlice)
            {
                IntVec3 next = pending.Peek() + GenAdj.CardinalDirections[direction++];
                if (direction == 4) { pending.Dequeue(); direction = 0; }
                if (!seen.Add(next)) continue;
                probes++;
                if (!floor(next)) continue;
                if (Cells.Count == CellLimit) { Finished = Truncated = true; pending.Clear(); break; }
                Cells.Add(next); pending.Enqueue(next);
            }
            Finished |= pending.Count == 0;
            return Finished;
        }
    }
}
