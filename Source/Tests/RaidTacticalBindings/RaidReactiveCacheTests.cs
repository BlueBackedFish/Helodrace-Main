using System;
using System.Collections;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class RaidReactiveCacheTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        var execution = new MapComponent_RaidTacticalExecution(null);
        var method = AccessTools.Method(execution.GetType(), "ScoresFor");
        var plan = new RaidTacticalPlan();
        object Scores(RaidTacticalPlan p, int x, int tick) => method.Invoke(execution,
            new object[] { p, new IntVec3(x,0,20), tick });
        object first = Scores(plan,8,100);
        Check(ReferenceEquals(first,Scores(plan,11,219)),
            "Sibling pawns reuse physical facts for the same plan and approximate threat within the cache lifetime.");
        Check(!ReferenceEquals(first,Scores(plan,8,220)),
            "Expiry refreshes physical cover/LOS facts even if the plan and threat are unchanged.");
        object fresh = Scores(plan,8,220);
        Check(!ReferenceEquals(fresh,Scores(plan,8,219)), "A tick rewind cannot retain future tactical facts.");
        Check(!ReferenceEquals(Scores(plan,8,221),Scores(plan,12,221)),
            "A threat crossing its spatial bucket cannot reuse the previous direction.");
        Check(!ReferenceEquals(Scores(plan,8,221),Scores(new RaidTacticalPlan(),8,221)),
            "Separate committed plans do not share room-specific physical scores.");
        for (int i=0;i<100;i++) Scores(new RaidTacticalPlan(),8,300+i);
        var contexts = (IDictionary)AccessTools.Field(execution.GetType(), "reactiveScores").GetValue(execution);
        Check(contexts.Count <= 64, "Many completed/replaced plans cannot grow the physical score table without bound.");
        Console.WriteLine($"PASS: {checks} native reactive score sharing, expiry and capacity checks.");
    }
}
