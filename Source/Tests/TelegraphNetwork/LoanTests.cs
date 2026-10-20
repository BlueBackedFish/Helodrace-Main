using System;
using System.Linq;
using System.Xml.Linq;
using Helodrace;
using Helodrace.Economy;
using RimWorld;
using Verse;
using Verse.AI.Group;

internal static partial class Program
{
    private static void Loans()
    {
        Check(LoanRules.CanBorrow(30, 40f, 800, 1), "Combined goodwill 70 and exact pledged credit permit a loan");
        Check(!LoanRules.CanBorrow(29, 40f, 800, 1), "Combined goodwill below 70 is rejected");
        Check(!LoanRules.CanBorrow(100, 39.99f, 800, 1), "High diplomatic goodwill cannot bypass financial collateral");
        Check(LoanRules.CanBorrow(100, 0.05f, 1, 60), "Fractional collateral and the maximum term are supported");
        Check(!LoanRules.CanBorrow(100, 0f, 1, 1) && !LoanRules.CanBorrow(100, -1f, 1, 1), "Loans never make finance negative");
        Check(LoanRules.CanBorrow(-30, 100f, 2000, 60), "The combined rule also works with hostile diplomatic goodwill");
        foreach (int amount in new[] { -1, 0, 2001, int.MaxValue })
            Check(!LoanRules.CanBorrow(100, 100f, amount, 15), "Invalid amounts cannot overflow or bypass collateral: " + amount);
        foreach (int days in new[] { -1, 0, 61, int.MaxValue })
            Check(!LoanRules.CanBorrow(100, 100f, 20, days), "Invalid repayment terms are rejected: " + days);
        Check(!LoanRules.CanBorrow(100, float.NaN, 20, 15) && !LoanRules.CanBorrow(100, float.PositiveInfinity, 20, 15),
            "Non-finite credit cannot authorize a loan");

        var setup = LoanSettlement();
        var loans = setup.loans; var ledger = setup.ledger; var terminal = setup.terminal;
        var actor = setup.actor; var lender = setup.lender; var map = setup.map;
        int now = Find.TickManager.TicksGame;
        terminal.Cells = 0;
        Check(!loans.TryBorrow(actor, terminal, 800, 5, out _) && ledger.Get(lender) == 40f,
            "A failed transmission cannot deduct collateral or schedule delivery");
        terminal.Cells = 10;
        actor.Downed = true;
        Check(!loans.TryBorrow(actor, terminal, 800, 5, out _) && terminal.Cells == 10, "An unavailable operator cannot sign");
        actor.Downed = false;
        Check(loans.TryBorrow(actor, terminal, 800, 5, out _), "An eligible loan signs through the production component");
        var contract = loans.OpenContract(lender);
        Check(ledger.Get(lender) == 0f && terminal.Cells == 9 && Balance(map) == 0, "Signing reserves credit and one cell without minting immediate cash");
        Check(contract.DispatchTick == now + 60000 && contract.Account.FundedTick == -1, "Delivery is scheduled independently of the repayment clock");
        Check(!loans.TryBorrow(actor, terminal, 1, 1, out _) && terminal.Cells == 9 && ledger.Get(lender) == 0f,
            "Duplicate requests cannot reuse another settlement's pledged credit");
        Check(!loans.TryRepay(actor, terminal, contract, 1, out _), "Undelivered cash cannot be repaid for a credit exploit");

        loans = ReloadLoans(loans);
        contract = loans.OpenContract(lender);
        Check(contract.Lender == lender && contract.Destination == map && contract.Account.ApprovedAmount == 800
            && contract.Account.DurationDays == 5, "Saving pending delivery preserves faction, map, amount and term");
        IncidentDefOf.TraderCaravanArrival.Worker.Execute = parms =>
        {
            Check(FinancialCaravanScope.Current == lender && parms.faction == lender
                && parms.traderKind == DefDatabase<TraderKindDef>.Value, "Delivery requests the financial caravan with scoped safe conduct");
            var courier = new Pawn { Map = map, Faction = lender, trader = new Pawn_TraderTracker { traderKind = parms.traderKind },
                Lord = new Lord { LordJob = new LordJob_FinancialCaravan(), CurLordToil = new LordToil() } };
            map.mapPawns.AllPawnsSpawned.Add(courier);
            return true;
        };
        Find.TickManager.TicksGame = contract.DispatchTick - 250; loans.GameComponentTick();
        Check(IncidentDefOf.TraderCaravanArrival.Worker.Attempts == 0 && Balance(map) == 0, "No caravan or cash appears prematurely");
        Find.TickManager.TicksGame += 250; loans.GameComponentTick();
        Check(contract.Dispatched && contract.Courier != null && Balance(map) == 0 && FinancialCaravanScope.Current == null,
            "The real incident path binds its merchant and clears the temporary generation scope");
        loans = ReloadLoans(loans); contract = loans.OpenContract(lender);
        Check(contract.Dispatched && contract.Courier == map.mapPawns.AllPawnsSpawned[0], "In-transit courier references survive saving");
        Find.TickManager.TicksGame += 250; loans.GameComponentTick();
        Check(Balance(map) == 0, "The courier must reach its meeting spot before cash is dropped");
        contract.Courier.Lord.CurLordToil = new LordToil_DefendTraderCaravan();
        GenPlace.PlacesBeforeFailure = 2;
        GenPlace.PartialPlacement = 33;
        Find.TickManager.TicksGame += 250; loans.GameComponentTick();
        Check(Balance(map) == 33 && contract.Account.Disbursed == 33 && !contract.Account.DeliveryFinished,
            "A failed placement that partially merged into an existing stack records only the actual amount");
        loans = ReloadLoans(loans); contract = loans.OpenContract(lender);
        GenPlace.PlacesBeforeFailure = int.MaxValue;
        Find.TickManager.TicksGame += 250; loans.GameComponentTick();
        Check(Balance(map) == 800 && contract.Account.Disbursed == 800 && contract.Account.DeliveryFinished,
            "Resuming a partial delivery after reload never duplicates money");
        Check(contract.Account.DueTick == Find.TickManager.TicksGame + 5 * 60000, "Repayment clock begins at completed cash delivery");
        loans.GameComponentTick();
        Check(Balance(map) == 800 && IncidentDefOf.TraderCaravanArrival.Worker.Attempts == 1, "Completed delivery cannot respawn cash or a second caravan");
        Check(!loans.TryRepay(actor, terminal, contract, 801, out _) && Balance(map) == 800 && ledger.Get(lender) == 0f,
            "Overpayment is rejected before consuming cash or restoring credit");
        lender.PlayerGoodwill = -80; lender.Hostile = true;
        Check(loans.TryRepay(actor, terminal, contract, 201, out _) && Balance(map) == 599
            && ledger.Get(lender) == 10.05f && contract.Account.Outstanding == 599,
            "Partial repayment restores exactly principal / 20, without service rewards or an eligibility recheck");
        Check(lender.PlayerGoodwill == -80 && terminal.Cells == 9, "Repayment does not alter diplomacy or consume a second transmission cell");
        foreach (Thing note in map.listerThings.AllThings.Where(t => t.def == DefDatabase<ThingDef>.Value)) note.Forbidden = true;
        Check(!loans.TryRepay(actor, terminal, contract, 1, out _) && ledger.Get(lender) == 10.05f && contract.Account.Outstanding == 599,
            "Unavailable cash cannot restore credit or reduce the loan");
        foreach (Thing note in map.listerThings.AllThings) note.Forbidden = false;
        loans = ReloadLoans(loans); contract = loans.OpenContract(lender);
        Check(contract.Account.Repaid == 201 && contract.Account.Outstanding == 599, "Partial repayments survive save/load");
        int letters = Find.LetterStack.Count;
        lender.PlayerGoodwill = 20;
        Find.TickManager.TicksGame = contract.Account.DueTick; loans.GameComponentTick(); loans.GameComponentTick();
        Check(Find.LetterStack.Count == letters + 1 && contract.OverdueNotified, "The due boundary sends exactly one overdue letter");
        var losses = GameComponent_CaravanLosses.Current;
        var overdueEntry = losses.Entries(lender).Single(e => e.Kind == CaravanLossKind.LoanOverdue);
        Check(overdueEntry.Count == 599 && overdueEntry.Value == 2995d && overdueEntry.GoodwillPenalty == 30,
            "Only outstanding principal at default is recorded, converted from Sthaler to silver");
        Check(lender.PlayerGoodwill == -10 && ledger.Get(lender) == -9.95f && overdueEntry.FinancialPenalty.ToString("0.##") == "20",
            "Loan default deducts half the original contract collateral even after partial repayment, once");
        var otherLender = new Faction { def = lender.def, PlayerGoodwill = 100 };
        ledger.TrySet(otherLender, 100f);
        Check(loans.BorrowFailure(otherLender, 20, 15) == "HD_Loan_OverdueBlock", "An overdue loan prevents new borrowing across lenders");
        loans = ReloadLoans(loans); contract = loans.OpenContract(lender); loans.GameComponentTick();
        Check(Find.LetterStack.Count == letters + 1 && losses.Entries(lender).Count() == 1 && ledger.Get(lender) == -9.95f,
            "Reloading cannot duplicate an overdue notice, loss record or penalty");
        Check(loans.TryRepay(actor, terminal, contract, 599, out _) && contract.Account.Closed && ledger.Get(lender) == 20f && Balance(map) == 0,
            "Full overdue repayment restores collateral but keeps the default penalty and closes the debt");
        Check(loans.OpenContract(lender) == null && loans.BorrowFailure(otherLender, 20, 15) == null,
            "Clearing overdue principal removes the global borrowing restriction");
        Check(!loans.TryRepay(actor, terminal, contract, 1, out _) && ledger.Get(lender) == 20f, "Closed contracts cannot restore collateral twice");
        Check(losses.Total(lender, CaravanLossKind.LoanOverdue) == 2995d, "Repaying arrears does not erase their historical loss record");
        ledger.TrySet(lender, 0f);
        for (int i = 0; i < 800; i++) ledger.Adjust(lender, LoanRules.CreditFor(1));
        Check(ledger.Get(lender) == 40f && ledger.TrySpend(lender, 40f) && ledger.Get(lender) == 0f,
            "Repeated one-note repayments cannot accumulate rounding errors or leave unspendable collateral");

        DeliveryFailures();
        var english = XDocument.Load("Languages/English/Keyed/Loans.xml").Root.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);
        var korean = XDocument.Load("Languages/Korean (한국어)/Keyed/Loans.xml").Root.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);
        Check(english.Keys.OrderBy(k => k).SequenceEqual(korean.Keys.OrderBy(k => k)), "Loan contract and notification keys match between languages");
        foreach (string key in english.Keys)
            Check(System.Text.RegularExpressions.Regex.Matches(english[key], @"\{\d+\}").Select(m => m.Value).OrderBy(v => v)
                .SequenceEqual(System.Text.RegularExpressions.Regex.Matches(korean[key], @"\{\d+\}").Select(m => m.Value).OrderBy(v => v)),
                "Loan translation placeholders match: " + key);
        Current.Game = null;
    }

    private static void DeliveryFailures()
    {
        var s = LoanSettlement();
        Check(s.loans.TryBorrow(s.actor, s.terminal, 800, 1, out _), "A new settlement can request a loan");
        var contract = s.loans.OpenContract(s.lender);
        IncidentDefOf.TraderCaravanArrival.Worker.Execute = _ => false;
        Find.TickManager.TicksGame = contract.DispatchTick; s.loans.GameComponentTick();
        Check(!contract.Dispatched && !contract.Account.DeliveryFinished && s.ledger.Get(s.lender) == 0f,
            "Unavailable caravan entry retries without a second collateral charge");
        Check(contract.DispatchTick == Find.TickManager.TicksGame + 2500, "An unsuccessful caravan arrival retries after one hour");
        Find.TickManager.TicksGame = contract.ExpiryTick; s.loans.GameComponentTick(); s.loans.GameComponentTick();
        Check(contract.Account.Closed && s.ledger.Get(s.lender) == 40f && Balance(s.map) == 0,
            "Expired undelivered loans return collateral exactly once with no outstanding principal");

        Check(s.loans.TryBorrow(s.actor, s.terminal, 800, 1, out _), "A cancelled delivery does not block a later contract");
        contract = s.loans.OpenContract(s.lender);
        contract.Dispatched = true;
        contract.Account.TryDisburse(200); Notes(s.map, 200);
        // A missing saved courier must cancel, never generate a replacement convoy or more cash.
        int attempts = IncidentDefOf.TraderCaravanArrival.Worker.Attempts;
        Find.TickManager.TicksGame += 250; s.loans.GameComponentTick();
        Check(contract.Account.DeliveryFinished && contract.Account.Outstanding == 200 && s.ledger.Get(s.lender) == 30f
            && IncidentDefOf.TraderCaravanArrival.Worker.Attempts == attempts,
            "A lost courier returns only undelivered collateral and retains debt for cash already handed over");
        Check(s.loans.TryRepay(s.actor, s.terminal, contract, 200, out _) && s.ledger.Get(s.lender) == 40f,
            "A partially cancelled delivery still permits exact repayment without excess collateral restoration");
        Check(s.loans.TryBorrow(s.actor, s.terminal, 800, 1, out _), "Further borrowing is allowed after full repayment");
        Find.Maps.Clear(); Find.TickManager.TicksGame += 250; s.loans.GameComponentTick();
        Check(s.loans.OpenContract(s.lender) == null && s.ledger.Get(s.lender) == 40f, "Removing the destination cancels an unfunded loan and returns collateral");
    }

    private static GameComponent_Loans ReloadLoans(GameComponent_Loans loans)
    {
        Scribe.Values.Clear(); Scribe.Loading = false; loans.ExposeData();
        var loaded = new GameComponent_Loans(Current.Game);
        Scribe.Loading = true; loaded.ExposeData(); Scribe.Loading = false;
        Current.Game.Components[typeof(GameComponent_Loans)] = loaded;
        return loaded;
    }

    private static (Map map, Faction lender, Pawn actor, CompTelegraphTable terminal, GameComponent_Loans loans, GameComponent_FinancialGoodwill ledger) LoanSettlement()
    {
        var game = new Game(); Current.Game = game;
        var ledger = new GameComponent_FinancialGoodwill(game);
        var loans = new GameComponent_Loans(game);
        game.Components[typeof(GameComponent_FinancialGoodwill)] = ledger;
        game.Components[typeof(GameComponent_Loans)] = loans;
        game.Components[typeof(GameComponent_CaravanLosses)] = new GameComponent_CaravanLosses(game);
        Find.TickManager.TicksGame = 1000; GenDate.LocalHour = 12; Rand.UseMaximum = false;
        DefDatabase<ThingDef>.Value = new ThingDef { defName = "HD_Money" };
        DefDatabase<TraderKindDef>.Value = new TraderKindDef();
        var s = Settlement(); Notes(s.map, 500);
        Check(s.network.TryRequestInstallation(s.trader, s.actor, out _), "Loan fixture has an installed telegraph");
        Check(ledger.Get(s.trader.Faction) == 25f, "Successful installation records its actual 500-note service payment");
        Find.TickManager.TicksGame += 120000;
        Find.Maps.Clear(); Find.Maps.Add(s.map);
        s.trader.Faction.PlayerGoodwill = 30; ledger.TrySet(s.trader.Faction, 40f);
        var terminal = new CompTelegraphTable { parent = s.map.listerThings.AllThings[0], Network = s.network };
        terminal.parent.Comp = terminal;
        IncidentDefOf.TraderCaravanArrival.Worker.Attempts = 0;
        GenPlace.PlacesBeforeFailure = int.MaxValue;
        GenPlace.PartialPlacement = 0;
        return (s.map, s.trader.Faction, s.actor, terminal, loans, ledger);
    }
}
