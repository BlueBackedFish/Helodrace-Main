using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    // Physical connection to existing history, not a new room-clear verdict.
    // Ignore synthetic gap boundaries; retain real doors and the candidate's
    // entire mouth so a wide NEW opening cannot certify its own far room.
    public sealed class TacticalSecuredRegionProbe
    {
        public const int CellLimit = 128, Slice = 32;
        private readonly Queue<IntVec3> pending = new Queue<IntVec3>();
        private readonly HashSet<IntVec3> seen = new HashSet<IntVec3>();
        private readonly IntVec3 opening, inward;
        private readonly bool openMouth;
        public TacticalStructureClass Result { get; private set; }
        public TacticalSecuredRegionProbe(IntVec3 opening, IntVec3 inward, bool openMouth)
        {
            this.opening = opening; this.inward = inward; this.openMouth = openMouth;
            IntVec3 seed = opening + inward; seen.Add(seed); pending.Enqueue(seed);
        }
        public TacticalStructureClass Step(Func<IntVec3, bool> floor, Func<IntVec3, bool> secured, ref int allowance)
        {
            while (Result == TacticalStructureClass.Pending && pending.Count > 0 && allowance > 0)
            {
                IntVec3 cell = pending.Dequeue(); allowance--;
                if (!floor(cell)) continue;
                if (secured(cell)) { Result = TacticalStructureClass.Target; break; }
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction, delta = next - opening;
                    int depth = delta.x * inward.x + delta.z * inward.z;
                    int lateral = delta.x * -inward.z + delta.z * inward.x;
                    if (next == opening || openMouth && depth == 0 && Math.Abs(lateral) <= 3) continue;
                    if (seen.Count < CellLimit && seen.Add(next)) pending.Enqueue(next);
                }
            }
            if (Result == TacticalStructureClass.Pending && pending.Count == 0) Result = TacticalStructureClass.Incidental;
            return Result;
        }
    }

    public sealed partial class TacticalSquadCommand
    {
        internal readonly Dictionary<Tuple<IntVec3, IntVec3>, TacticalSecuredRegionProbe> SecuredRegionProbes
            = new Dictionary<Tuple<IntVec3, IntVec3>, TacticalSecuredRegionProbe>();
        internal int SecuredRegionAllowance, SecuredRegionExpiry;
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private TacticalStructureClass SecuredRegion(TacticalSquadCommand command, IntVec3 opening, IntVec3 inward)
        {
            IntVec3 inside = opening + inward;
            if (command.SecuredCells.Contains(inside)) return TacticalStructureClass.Target;
            if (command.SecuredCells.Count == 0 || !inside.InBounds(map) || !inside.Roofed(map))
                return TacticalStructureClass.Incidental;
            var key = Tuple.Create(opening, inward);
            if (!command.SecuredRegionProbes.TryGetValue(key, out TacticalSecuredRegionProbe probe))
            {
                if (command.SecuredRegionProbes.Count >= 64) return TacticalStructureClass.Incidental;
                Building barrier = opening.GetEdifice(map);
                command.SecuredRegionProbes.Add(key, probe = new TacticalSecuredRegionProbe(opening, inward, barrier == null));
            }
            return probe.Step(cell => cell.InBounds(map) && cell.Roofed(map) && cell.Walkable(map)
                && !(cell.GetEdifice(map) is Building_Door), command.SecuredCells.Contains, ref command.SecuredRegionAllowance);
        }
    }
}
