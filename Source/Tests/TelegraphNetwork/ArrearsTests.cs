using System;
using System.Linq;
using Helodrace;
using Helodrace.Economy;
using RimWorld;
using Verse;

internal static partial class Program
{
    private static void Arrears()
    {
        var game = new Game(); Current.Game = game;
        var financial = new GameComponent_FinancialGoodwill(game);
        var losses = new GameComponent_CaravanLosses(game);
        game.Components[typeof(GameComponent_FinancialGoodwill)] = financial;
        game.Components[typeof(GameComponent_CaravanLosses)] = losses;
        DefDatabase<ThingDef>.Value = new ThingDef { defName = "HD_Money" };
        var provider = new Faction { PlayerGoodwill = 70 };
        financial.TrySet(provider, 80);
        var forward = new HelodForwardBase { Faction = provider };
        int messages = Messages.Count;
        forward.Default(); forward.Default();
        var entry = losses.Entries(provider).Single();
        Check(entry.Kind == CaravanLossKind.ForwardBaseOverdue && entry.Count == 81 && entry.Value == 405d,
            "Forward-base default records the unpaid note-rounded bill at its silver value");
        Check(provider.PlayerGoodwill == 55 && financial.Get(provider) == 50f,
            "Forward-base default deducts exactly 15 diplomacy and 30 finance without old penalties");
        Check(forward.WithdrawalCount == 1 && Messages.Count == messages + 1,
            "Repeated settlement checks cannot duplicate withdrawal, messages or penalties");
        Scribe.Values.Clear(); Scribe.Loading = false; forward.ExposeDefault();
        var reloaded = new HelodForwardBase { Faction = provider };
        Scribe.Loading = true; reloaded.ExposeDefault(); Scribe.Loading = false; reloaded.Default();
        Check(reloaded.WithdrawalCount == 0 && losses.Entries(provider).Count() == 1 && provider.PlayerGoodwill == 55,
            "The saved forward-base default guard prevents reapplying the loss or either goodwill penalty");
        Check(!ContractArrears.Process(provider, CaravanLossKind.ForwardBaseOverdue, entry.CaravanKey, "Pioneer", "Base", 81, 0f),
            "The ledger independently rejects processing the same overdue bill twice");

        foreach (var pair in new[] { (0f, -10f), (-20f, -30f), (-95f, -100f), (100f, 90f) })
        {
            var faction = new Faction { PlayerGoodwill = 80 };
            financial.TrySet(faction, pair.Item1);
            Check(ContractArrears.Process(faction, CaravanLossKind.LoanOverdue, Guid.NewGuid().ToString(), "NICC", "Colony", 20, 20f)
                && financial.Get(faction) == pair.Item2 && faction.PlayerGoodwill == 50,
                "Loan default deducts half of the contract credit, independent of current finance: " + pair.Item1);
        }
        Check(!ContractArrears.Process(provider, CaravanLossKind.Injury, "invalid", "", "", 80, 0f)
            && !ContractArrears.Process(provider, CaravanLossKind.LoanOverdue, "empty", "", "", 0, 20f),
            "Invalid kinds and empty bills cannot trigger loss entries or penalties");
        Check(provider.PlayerGoodwill == 55 && financial.Get(provider) == 50f, "Rejected arrears leave both goodwill balances unchanged");

        // Restore the saved ledger's indexes, then try processing an already-seen contract.
        Scribe.Values.Clear(); Scribe.Loading = false; losses.ExposeData();
        var loadedLosses = new GameComponent_CaravanLosses(game);
        Scribe.Loading = true; loadedLosses.ExposeData(); Scribe.Loading = false;
        Scribe.PostLoad = true; loadedLosses.ExposeData(); Scribe.PostLoad = false;
        game.Components[typeof(GameComponent_CaravanLosses)] = loadedLosses;
        Check(!ContractArrears.Process(provider, CaravanLossKind.ForwardBaseOverdue, entry.CaravanKey, "Pioneer", "Base", 81, 0f)
            && financial.Get(provider) == 50f, "Saved arrears keys prevent repeated financial penalties after reload");
        Check(loadedLosses.Entries(provider).Single().GoodwillPenalty == 15
            && loadedLosses.Entries(provider).Single().FinancialPenalty == 30f,
            "The loss history preserves actual additional diplomatic and financial deductions");
        var negative = new Faction { PlayerGoodwill = 20 };
        financial.TrySet(negative, -80f);
        Check(ContractArrears.Process(negative, CaravanLossKind.ForwardBaseOverdue, "NegativeBase", "Pioneer", "Base", 10, 0f)
            && negative.PlayerGoodwill == 5 && financial.Get(negative) == -100f,
            "Fixed forward-base penalties apply to negative balances while respecting the financial floor");
        Check(ContractArrears.LoanFinancialCharge(0.05f) == 0.03f,
            "Half of a one-note loan's collateral rounds consistently to two decimal places");

        // A repayment after the deadline but before the next 250-tick sweep is still overdue.
        var s = LoanSettlement();
        Check(s.loans.TryBorrow(s.actor, s.terminal, 400, 1, out _), "Repayment-boundary fixture signs a loan");
        var loan = s.loans.OpenContract(s.lender);
        loan.Account.TryDisburse(400); loan.Account.FinishDelivery(Find.TickManager.TicksGame); Notes(s.map, 400);
        Find.TickManager.TicksGame = loan.Account.DueTick + 1;
        Check(s.loans.TryRepay(s.actor, s.terminal, loan, 400, out _) && loan.Account.Closed,
            "Immediate overdue repayment can still close the loan");
        Check(s.lender.PlayerGoodwill == 0 && s.ledger.Get(s.lender) == 30f
            && GameComponent_CaravanLosses.Current.Total(s.lender, CaravanLossKind.LoanOverdue) == 2000d,
            "Repayment cannot bypass first-default recording or penalties between scheduler ticks");

        s = LoanSettlement();
        Check(s.loans.TryBorrow(s.actor, s.terminal, 800, 1, out _), "Partial-delivery fixture signs a loan");
        loan = s.loans.OpenContract(s.lender);
        loan.Account.TryDisburse(200);
        s.ledger.Adjust(s.lender, loan.Account.FinishDelivery(Find.TickManager.TicksGame));
        Notes(s.map, 200);
        Check(s.loans.TryRepay(s.actor, s.terminal, loan, 50, out _), "Partial delivery can be partly repaid before default");
        Find.TickManager.TicksGame = loan.Account.DueTick;
        s.loans.GameComponentTick();
        var partialEntry = GameComponent_CaravanLosses.Current.Entries(s.lender).Single();
        Check(partialEntry.Count == 150 && partialEntry.FinancialPenalty == 5f && s.ledger.Get(s.lender) == 27.5f,
            "Loan penalties use only the collateral actually used by the funded principal, excluding undelivered refunds and ignoring repayments");
        Current.Game = null;
    }
}
