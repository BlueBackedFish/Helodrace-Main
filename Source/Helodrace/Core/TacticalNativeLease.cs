using System.Collections.Generic;

namespace Helodrace
{
    // Cancelling a request does not complete its scheduled Unity job. Both
    // request ownership and a completed native-read barrier are required.
    internal sealed class TacticalNativeLease<T> where T : class
    {
        private readonly HashSet<T> requests = new HashSet<T>();
        public bool Reading { get; private set; }
        public int Requests => requests.Count;
        public bool CanRetire => requests.Count == 0 && !Reading;
        public void Acquire(T request) { if (request != null) requests.Add(request); }
        public void Release(T request) { if (request != null) requests.Remove(request); }
        public void BeginRead() => Reading = true;
        public void CompleteReads() => Reading = false;
    }
}
