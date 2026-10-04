using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private int personalJoinRevision;

        // Frozen floors plus known usable interior passages. No global live
        // reachability and no permission for every exterior room-zero tile.
        internal static RaidMovementNode KnownIndoorJoin(Map map, RaidStructureSnapshot structure,
            IntVec3 source, IntVec3 target, ISet<int> allowedRooms,
            Func<IntVec3, bool> knownPassage, ICollection<IntVec3> avoided)
        {
            const int radius = 24;
            if (!source.InBounds(map) || !target.InBounds(map))
                return null;
            int minX = Math.Max(0, source.x - radius), minZ = Math.Max(0, source.z - radius);
            int width = Math.Min(map.Size.x, source.x + radius + 1) - minX;
            int height = Math.Min(map.Size.z, source.z + radius + 1) - minZ;
            var rooms = new int[width * height]; var usable = new bool[rooms.Length]; var portals = new bool[rooms.Length];
            IntVec3 Cell(int index) => new IntVec3(minX + index % width, 0, minZ + index / width);
            int Index(IntVec3 cell) => (cell.z - minZ) * width + cell.x - minX;
            for (int i = 0; i < rooms.Length; i++)
            {
                IntVec3 cell = Cell(i);
                int globalIndex = map.cellIndices.CellToIndex(cell);
                TacticalRawCell raw = structure.Version.Geometry.Input.Cells[globalIndex];
                rooms[i] = raw.Room;
                portals[i] = raw.Has(TacticalRawFlags.WallLine) || raw.Has(TacticalRawFlags.Door);
                bool interiorPortal = portals[i] && !structure.Version.Geometry.Cells[globalIndex].ExteriorAccess
                    && (structure.RoomAt(cell + IntVec3.North) > 0 && structure.RoomAt(cell + IntVec3.South) > 0
                        || structure.RoomAt(cell + IntVec3.East) > 0 && structure.RoomAt(cell + IntVec3.West) > 0);
                usable[i] = !avoided.Contains(cell) && (portals[i] ? interiorPortal && knownPassage(cell)
                    : raw.Room > 0 && allowedRooms.Contains(raw.Room) && raw.Has(TacticalRawFlags.Standable));
            }
            var topology = new CqbLocalTopology(width, height, rooms, usable, portals);
            int[] distances = topology.Distances(Index(source), out int[] previous);
            int targetIndex = target.x >= minX && target.x < minX + width
                && target.z >= minZ && target.z < minZ + height ? Index(target) : -1;
            if (targetIndex < 0 || distances[targetIndex] < 0)
                targetIndex = Enumerable.Range(0, rooms.Length)
                    .Where(index => distances[index] >= 0 && !portals[index] && rooms[index] > 0
                        && rooms[index] == structure.RoomAt(target))
                    .OrderBy(index => Cell(index).DistanceToSquared(target)).DefaultIfEmpty(-1).First();
            if (targetIndex < 0) return null;
            var path = new List<int>();
            for (int index = targetIndex; index >= 0; index = previous[index]) path.Add(index);
            var selectedPortals = new HashSet<int>(path.Where(index => portals[index]));
            var selectedRooms = new HashSet<int>(path.Where(index => !portals[index]).Select(index => rooms[index]));
            return new RaidMovementNode {
                Center = Cell(targetIndex), StructureVersion = structure.Version.Id,
                AllowedRooms = selectedRooms.ToList(), AllowedPortals = selectedPortals.Select(Cell).ToList(),
                GuidanceCells = Enumerable.Range(0, rooms.Length)
                    .Where(index => usable[index] && !portals[index] && rooms[index] == rooms[targetIndex]
                        && Cell(index).DistanceToSquared(Cell(targetIndex)) <= 9).Select(Cell).ToList(),
                RestrictedCells = new HashSet<IntVec3>(Enumerable.Range(0, rooms.Length)
                    .Where(index => usable[index] && (portals[index] ? selectedPortals.Contains(index)
                        : selectedRooms.Contains(rooms[index]))).Select(Cell))
            };
        }
    }
}
