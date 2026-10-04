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
        public static Dictionary<int, bool> KnownDoors(string signature)
        {
            var known = new Dictionary<int, bool>();
            foreach (string entry in (signature ?? "").Split(','))
            {
                string[] parts = entry.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int id)) known[id] = parts[1] == "1";
            }
            return known;
        }

        public static string ObservedDoorSignature(string previous, IEnumerable<KeyValuePair<int, bool>> observations)
        {
            Dictionary<int, bool> known = KnownDoors(previous);
            foreach (KeyValuePair<int, bool> observation in observations) known[observation.Key] = observation.Value;
            return string.Join(",", known.OrderBy(pair => pair.Key).Select(pair => pair.Key + ":" + (pair.Value ? "1" : "0")));
        }

        public static string DoorSignature(Map map, IntVec3 center, string previous,
            System.Func<IntVec3, bool> observed) => ObservedDoorSignature(previous,
                Doors(map, center, 12f).Where(door => observed(door.Position))
                    .Select(door => new KeyValuePair<int, bool>(door.thingIDNumber, door.Open)));
    }
}
