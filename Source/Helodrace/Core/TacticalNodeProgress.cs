using System;
using System.Collections.Generic;

namespace Helodrace
{
    // Value-only policy: no Map/Pawn access and no distance leash to a moving leader.
    internal static class TacticalNodeProgress
    {
        public static bool WalkLine(int fromX, int fromZ, int toX, int toZ, Func<int, int, bool> standable)
        {
            int steps = Math.Max(Math.Abs(toX - fromX), Math.Abs(toZ - fromZ));
            int previousX = fromX, previousZ = fromZ;
            for (int i = 0; i <= steps; i++)
            {
                int x = steps == 0 ? fromX : fromX + (int)Math.Round((toX - fromX) * (double)i / steps);
                int z = steps == 0 ? fromZ : fromZ + (int)Math.Round((toZ - fromZ) * (double)i / steps);
                if (!standable(x, z) || x != previousX && z != previousZ
                    && (!standable(previousX, z) || !standable(x, previousZ))) return false;
                previousX = x; previousZ = z;
            }
            return true;
        }

        public static List<int> Select(int count, Func<int, bool> mandatory,
            Func<int, int, bool> shortcut, int maximumSpan = 16)
        {
            var result = new List<int>();
            if (count == 0) return result;
            result.Add(0);
            int current = 0;
            while (current < count - 1)
            {
                int limit = Math.Min(count - 1, current + maximumSpan);
                for (int i = current + 1; i <= limit; i++)
                    if (mandatory(i)) { limit = i; break; }
                int next = current + 1;
                for (int i = limit; i > current + 1; i--)
                    if (shortcut(current, i)) { next = i; break; }
                result.Add(next);
                current = next;
            }
            return result;
        }

        public static bool Advance(bool final, int active, int arrived)
            => active == 0 || (final ? arrived >= active : arrived > 0);
        public static int OutsideSince(bool outside, int since, int tick) => outside ? since < 0 ? tick : since : -1;
        public static bool NeedsCorrection(int since, int tick, int delay) => since >= 0 && tick - since >= delay;

        public static int ForwardJoin(int passed, int target, Func<int, bool> required, Func<int, bool> connected)
        {
            int result = passed;
            for (int i = passed + 1; i <= target; i++)
            {
                if (required(i)) break;
                if (i > passed + 1 && connected(i)) result = i - 1;
            }
            return result;
        }

        public static bool AllowsStep(int currentRoom, int nextRoom, bool door,
            bool selectedDoor, Func<int, bool> allowedRoom)
            => (!door || selectedDoor || !allowedRoom(currentRoom))
                && (nextRoom == currentRoom || allowedRoom(nextRoom));

        public static int Arrive(int completed, int target, int count, Func<int, bool> arrived)
        {
            int next = completed + 1;
            while (next <= target && next < count && arrived(next)) completed = next++;
            return completed;
        }
    }
}
