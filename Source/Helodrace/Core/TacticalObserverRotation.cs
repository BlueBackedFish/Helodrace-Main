using System;
using System.Collections.Generic;

namespace Helodrace
{
    internal static class TacticalObserverRotation
    {
        // Command and explicit opening observation remain present; the other
        // positions rotate rather than giving every pawn an expensive scan.
        internal static List<T> Select<T>(IList<T> members, Func<T, bool> priority, ref int cursor, int limit)
        {
            var result = new List<T>();
            foreach (T member in members)
                if (priority(member) && !result.Contains(member) && result.Count < limit) result.Add(member);
            if (members.Count == 0) { cursor = 0; return result; }
            int start = cursor % members.Count, checkedMembers = 0;
            while (checkedMembers < members.Count && result.Count < limit)
            {
                T member = members[(start + checkedMembers++) % members.Count];
                if (!result.Contains(member)) result.Add(member);
            }
            cursor = (start + checkedMembers) % members.Count;
            return result;
        }
    }
}
