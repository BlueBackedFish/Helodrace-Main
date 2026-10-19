using System;
using Helodrace;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        var ham = new HamRadioSchedule();
        Check(ham.CanCheck(0), "The first day can receive a mission");
        Check(ham.CheckDay(0, 0.14999f), "A roll below 15 percent receives mission traffic");
        Check(!ham.CheckDay(0, 0f), "Repeated use cannot receive another mission that day");
        Check(!ham.CheckDay(1, 0.15f), "A roll at the threshold is ordinary chatter");
        Check(!ham.CheckDay(1, 0f), "A failed daily roll cannot be rerolled on another console");
        Check(ham.CheckDay(2, 0f), "A later day can receive another mission");
        Check(!ham.CheckDay(1, 0f), "Clock rollback does not reopen a checked day");
        var restored = new HamRadioSchedule { LastCheckedDay = ham.LastCheckedDay };
        Check(!restored.CanCheck(2), "Persisted day prevents a same-day retry after loading");
        Check(restored.CanCheck(3), "Persisted state permits the next day");
        Console.WriteLine("PASS: " + checks + " communications checks.");
    }
}
