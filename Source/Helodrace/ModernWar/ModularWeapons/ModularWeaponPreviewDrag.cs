using System.Numerics;

namespace Helodrace.ModernWar
{
    internal sealed class ModularWeaponPreviewDrag
    {
        public bool Active { get; private set; }
        public bool Dragging { get; private set; }
        private Vector2 start, previous;

        public void Begin(Vector2 point)
        {
            Active = true;
            Dragging = false;
            start = previous = point;
        }

        public Vector2 Move(Vector2 point)
        {
            if (!Active) return Vector2.Zero;
            Vector2 total = point - start;
            if (!Dragging && total.X * total.X + total.Y * total.Y < 16f) return Vector2.Zero;
            Vector2 delta = point - (Dragging ? previous : start);
            previous = point;
            Dragging = true;
            return delta;
        }

        public bool End()
        {
            bool click = Active && !Dragging;
            Cancel();
            return click;
        }

        public void Cancel() { Active = false; Dragging = false; }
    }
}
