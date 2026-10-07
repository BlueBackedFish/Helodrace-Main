using System;
using System.Collections.Generic;

namespace Helodrace
{
    internal sealed class TacticalOpeningLeases<T>
    {
        private sealed class Request { internal string Owner; internal T Opening; }
        private readonly List<Request> requests = new List<Request>();
        internal bool Acquire(string owner, T opening, Func<T, T, bool> conflicts)
        {
            Request request = requests.Find(value => value.Owner == owner);
            if (request != null && !EqualityComparer<T>.Default.Equals(request.Opening, opening))
            { requests.Remove(request); request = null; }
            if (request == null) { request = new Request { Owner = owner, Opening = opening }; requests.Add(request); }
            foreach (Request earlier in requests)
            {
                if (earlier == request) return true;
                if (conflicts(earlier.Opening, opening)) return false;
            }
            return false;
        }
        internal bool Release(string owner) => requests.RemoveAll(value => value.Owner == owner) > 0;
        internal int Prune(Func<string, bool> live) => requests.RemoveAll(value => !live(value.Owner));
    }
}
