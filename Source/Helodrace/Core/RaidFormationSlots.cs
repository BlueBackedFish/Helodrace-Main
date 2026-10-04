using System;
using System.Collections.Generic;

namespace Helodrace
{
    // Stable ownership prevents two units from repeatedly displacing each other.
    public sealed class RaidFormationSlots<T>
    {
        private readonly Dictionary<T, int> owners = new Dictionary<T, int>();

        public void Claim(T cell, int pawnId)
        {
            if (!owners.TryGetValue(cell, out int owner) || pawnId < owner)
                owners[cell] = pawnId;
        }

        public bool Available(T cell, int pawnId) =>
            !owners.TryGetValue(cell, out int owner) || owner == pawnId;

        public void Release(T cell, int pawnId)
        {
            if (owners.TryGetValue(cell, out int owner) && owner == pawnId)
                owners.Remove(cell);
        }

        public static bool Ready(T current, T assigned, bool occupied) =>
            EqualityComparer<T>.Default.Equals(current, assigned) && !occupied;

        public bool TryAssign(IEnumerable<T> candidates, int pawnId,
            Func<T, bool> free, Func<T, bool> reachable, out T result)
        {
            int attempts = 0;
            foreach (T cell in candidates)
            {
                if (!Available(cell, pawnId) || !free(cell)) continue;
                if (++attempts > 32) break;
                if (!reachable(cell)) continue;
                Claim(cell, pawnId);
                result = cell;
                return true;
            }
            result = default(T);
            return false;
        }
    }
}
