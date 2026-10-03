using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace
{
    internal static class RaidLocalMapState
    {
        // Small live overlay: includes new doors and excludes removed doors.
        // Never enumerates all buildings just to inspect one room's neighborhood.
        public static IEnumerable<Building_Door> Doors(Map map, IntVec3 center, float radius)
        {
            var seen = new HashSet<int>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
                if (cell.InBounds(map) && cell.GetEdifice(map) is Building_Door door
                    && door.Spawned && !door.Destroyed && seen.Add(door.thingIDNumber)) yield return door;
        }
        public static string DoorSignature(Map map, IntVec3 center) => string.Join(",",
            Doors(map, center, 12f).OrderBy(door => door.thingIDNumber)
                .Select(door => door.thingIDNumber + ":" + (door.Open ? "1" : "0")));
    }
}
