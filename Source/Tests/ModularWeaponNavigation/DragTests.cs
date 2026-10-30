using System;
using Helodrace.ModernWar;
using System.Numerics;

internal static class DragTests
{
    public static void Run()
    {
        var drag = new ModularWeaponPreviewDrag();
        drag.Begin(new Vector2(100, 100));
        Equal(Vector2.Zero, drag.Move(new Vector2(102, 101)));
        if (!drag.End()) throw new Exception("Small hand motion must still select a part as a click.");
        drag.Begin(new Vector2(100, 100));
        drag.Move(new Vector2(102, 101));
        Equal(new Vector2(8, 4), drag.Move(new Vector2(108, 104)));
        Equal(new Vector2(3, -2), drag.Move(new Vector2(111, 102)));
        if (drag.End()) throw new Exception("Panning must not also activate a part or rail on release.");
        drag.Begin(new Vector2(100, 100));
        Equal(new Vector2(-600, 900), drag.Move(new Vector2(-500, 1000)));
        drag.Cancel();
        if (drag.Active || drag.End()) throw new Exception("Losing capture must cancel selection and dragging.");
        Equal(Vector2.Zero, drag.Move(new Vector2(10, 20)));
        drag.Begin(Vector2.Zero);
        if (!drag.End()) throw new Exception("A fresh click after dragging must remain selectable.");
        Console.WriteLine("PASS: left-button click/drag separation, movement threshold, capture cancellation and outside-view movement.");
    }
    private static void Equal(Vector2 expected, Vector2 actual)
    {
        if (Math.Abs(expected.X - actual.X) > 0.001f || Math.Abs(expected.Y - actual.Y) > 0.001f)
            throw new Exception("Preview dragging must preserve screen-space movement without jumps.");
    }
}
