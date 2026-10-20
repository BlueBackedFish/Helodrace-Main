using System;
using Helodrace.Economy;
using RimWorld;
using Verse;
using Verse.AI;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string description)
    {
        checks++;
        if (!condition) throw new Exception(description);
    }

    private static void Main()
    {
        var faction = new Faction { Hostile = true };
        var other = new Faction { Hostile = true };
        var kind = new TraderKindDef();
        var actor = new Pawn { Faction = Faction.OfPlayer };
        var trader = new Pawn { Faction = faction, TraderKind = kind, Protected = true };
        Check(!FactionUtility.CanTradeWith(actor, faction, kind).Accepted, "The native faction check reproduces the hostile rejection labelled as missing title");
        Check(FinancialCaravanAccess.CanTradeWith(actor, faction, kind, trader).Accepted, "An authorized merchant passes the menu's second hostility check");
        Check(FactionUtility.HostileTo(faction, Faction.OfPlayer), "Opening trade never changes live faction hostility");
        Check(!FinancialCaravanScope.Covers(faction, Faction.OfPlayer), "The trade-menu scope ends before returning");

        actor.CanSocial = false;
        Check(!FinancialCaravanAccess.CanTradeWith(actor, faction, kind, trader).Accepted, "Safe conduct does not bypass social incapacity");
        actor.CanSocial = true; kind.RequiresPermit = true;
        Check(!FinancialCaravanAccess.CanTradeWith(actor, faction, kind, trader).Accepted, "A real royal trading permit remains required");
        actor.HasPermit = true;
        Check(FinancialCaravanAccess.CanTradeWith(actor, faction, kind, trader).Accepted, "A genuine permit still works under safe conduct");
        kind.RequiresPermit = false;
        Check(!FinancialCaravanAccess.CanTradeWith(actor, other, kind, trader).Accepted, "A merchant cannot authorize another faction's trade");
        Check(!FinancialCaravanAccess.CanTradeWith(actor, faction, new TraderKindDef(), trader).Accepted, "A protected merchant cannot authorize another trader kind");
        Check(!FinancialCaravanAccess.CanTradeWith(new Pawn { Faction = other }, faction, kind, trader).Accepted, "Safe conduct only authorizes player negotiators");
        trader.Protected = false;
        Check(!FinancialCaravanAccess.CanTradeWith(actor, faction, kind, trader).Accepted, "Unrelated hostile traders remain unavailable");
        trader.Protected = true; trader.Spawned = false;
        Check(!FinancialCaravanAccess.CanTradeWith(actor, faction, kind, trader).Accepted, "A departed merchant cannot authorize trade");
        trader.Spawned = true;

        foreach (string member in new[] { "merchant", "guard", "carrier" })
        {
            var pawn = new Pawn { Faction = faction, Protected = true };
            Check(FinancialCaravanAccess.DoorMachinesLike(Faction.OfPlayer, pawn), "A protected " + member + " passes player-door faction checks");
            Check(!FinancialCaravanAccess.DoorMachinesLike(other, pawn), "Safe conduct grants no access to unrelated enemy doors: " + member);
        }
        Check(FactionUtility.HostileTo(faction, Faction.OfPlayer) && !FinancialCaravanScope.Covers(faction, Faction.OfPlayer),
            "Door checks do not leave the faction temporarily friendly");
        trader.Prisoner = true;
        Check(!FinancialCaravanAccess.DoorMachinesLike(Faction.OfPlayer, trader), "Native prisoner restrictions remain in effect");
        trader.Prisoner = false; trader.Protected = false;
        Check(!FinancialCaravanAccess.DoorMachinesLike(Faction.OfPlayer, trader), "Breaching safe conduct immediately removes special door access");
        Check(!FinancialCaravanAccess.CanTradeWith(actor, faction, kind, trader).Accepted, "Breaching safe conduct immediately removes special trade access");
        faction.Hostile = false;
        Check(FinancialCaravanAccess.CanTradeWith(actor, faction, kind, trader).Accepted
            && FinancialCaravanAccess.DoorMachinesLike(Faction.OfPlayer, trader), "Ordinary neutral traders keep native trade and door behavior");
        faction.Hostile = true; trader.Protected = true;

        using (FinancialCaravanScope.Enter(other))
        {
            Check(FinancialCaravanAccess.CanTradeWith(actor, faction, kind, trader).Accepted, "Trade checks can nest inside another scoped operation");
            Check(FinancialCaravanScope.Covers(other, Faction.OfPlayer) && !FinancialCaravanScope.Covers(faction, Faction.OfPlayer),
                "The outer scope is restored after trade checks");
            Check(FinancialCaravanAccess.DoorMachinesLike(Faction.OfPlayer, trader), "Door checks can nest inside another scoped operation");
            Check(FinancialCaravanScope.Covers(other, Faction.OfPlayer) && !FinancialCaravanScope.Covers(faction, Faction.OfPlayer),
                "The outer scope is restored after door checks");
        }
        FactionUtility.ThrowDuringTrade = true;
        try { FinancialCaravanAccess.CanTradeWith(actor, faction, kind, trader); throw new Exception("Expected native failure"); }
        catch (InvalidOperationException) { }
        FactionUtility.ThrowDuringTrade = false;
        Check(!FinancialCaravanScope.Covers(faction, Faction.OfPlayer), "A failed native trade check cannot leak temporary friendship");
        GenAI.ThrowDuringDoorCheck = true;
        try { FinancialCaravanAccess.DoorMachinesLike(Faction.OfPlayer, trader); throw new Exception("Expected native failure"); }
        catch (InvalidOperationException) { }
        GenAI.ThrowDuringDoorCheck = false;
        Check(!FinancialCaravanScope.Covers(faction, Faction.OfPlayer), "A failed native door check cannot leak temporary friendship");
        Console.WriteLine("PASS: " + checks + " safe-conduct access checks.");
    }
}
