using RimWorld;
using UnityEngine;
using Waylith.Math;
using Rect = Waylith.Math.Rect;
using Vector2 = System.Numerics.Vector2;
using Color = Waylith.Math.Color;
using Verse;

namespace Helodrace.Economy
{
    public sealed class Dialog_LoanContract : Window
    {
        private readonly Pawn actor;
        private readonly CompTelegraphTable terminal;
        private LoanContract contract;
        private int amount = 100;
        private int days = 15;
        private int repayment;
        private string amountBuffer = "100", daysBuffer = "15", repaymentBuffer;
        private Vector2 scroll;
        public override UnityEngine.Vector2 InitialSize => (new Vector2(760f, Mathf.Min(1000f, UI.screenHeight - 60f))).ToGame();

        public Dialog_LoanContract(Pawn actor, CompTelegraphTable terminal)
        {
            this.actor = actor;
            this.terminal = terminal;
            contract = GameComponent_Loans.Current.OpenContract(terminal.Provider);
            repayment = contract?.Account.Outstanding ?? 0;
            repaymentBuffer = repayment.ToString();
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = true;
        }

        public override void DoWindowContents(UnityEngine.Rect gameRect)
        {
            Rect inRect = gameRect.ToNative();
            var loans = GameComponent_Loans.Current;
            Faction lender = contract?.Lender ?? terminal.Provider;
            float financial = GameComponent_FinancialGoodwill.Current?.Get(lender) ?? 0f;
            Rect viewport = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 48f);
            Text.Font = GameFont.Small;
            float contentWidth = viewport.width - 78f;
            string terms = (contract == null ? "HD_Loan_Terms" : "HD_Loan_RepaymentTerms").Translate(terminal.TransmissionCostLabel);
            float termsHeight = Text.CalcHeight(terms, contentWidth) + 8f;
            string draftFailure = contract == null
                ? terminal.TransmissionFailure(lender, true) ?? loans.BorrowFailure(lender, amount, days) : null;
            float failureHeight = draftFailure == null ? 0f : Text.CalcHeight(draftFailure, contentWidth) + 8f;
            float draftSignY = 535f + termsHeight + 18f + failureHeight + 18f;
            float repayY = 548f + termsHeight + 18f;
            float repaySignY = repayY + 124f;
            float pageHeight = Mathf.Max(920f, (contract == null ? draftSignY : repaySignY) + 80f);
            Rect paper = new Rect(0f, 0f, viewport.width - 18f, pageHeight);
            UnityEngine.Vector2 gameScroll = scroll.ToGame();
            Widgets.BeginScrollView(viewport.ToGame(), ref gameScroll, paper.ToGame());
            scroll = gameScroll.ToNative();
            Widgets.DrawBoxSolid(paper.ToGame(), (new Color(0.97f, 0.96f, 0.92f)).ToGame());
            Widgets.DrawBox(paper.ToGame());
            Rect content = UiMath.Inset(paper, 30f);
            GUI.color = Color.black.ToGame();
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Medium;
            Widgets.Label((new Rect(content.x, 32f, content.width, 42f)).ToGame(), TelegraphCompanies.Name(TelegraphCompany.NationalIndustrialCredit));
            Text.Font = GameFont.Small;
            Widgets.Label((new Rect(content.x, 80f, content.width, 25f)).ToGame(), "HD_Loan_Form".Translate());
            Widgets.DrawLineHorizontal(content.x, 116f, content.width);
            Text.Font = GameFont.Medium;
            Widgets.Label((new Rect(content.x, 135f, content.width, 36f)).ToGame(), "HD_Loan_Title".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label((new Rect(content.x, 188f, content.width, 72f)).ToGame(),
                "HD_Loan_Parties".Translate(lender?.Name ?? "?", actor?.Faction?.Name ?? "?", terminal.parent.Map?.Parent.LabelCap ?? "?"));
            Widgets.Label((new Rect(content.x, 274f, content.width, 54f)).ToGame(),
                "HD_Loan_Balances".Translate(lender?.PlayerGoodwill ?? 0, financial.ToString("0.##"), LoanRules.RequiredCombinedGoodwill));

            if (contract == null)
            {
                DrawNumber(content, 344f, "HD_Loan_Amount", ref amount, ref amountBuffer, 1, LoanRules.MaximumAmount);
                DrawNumber(content, 400f, "HD_Loan_Days", ref days, ref daysBuffer, LoanRules.MinimumDays, LoanRules.MaximumDays);
                Widgets.Label((new Rect(content.x, 465f, content.width, 54f)).ToGame(),
                    "HD_Loan_CreditQuote".Translate(LoanRules.CreditFor(amount).ToString("0.##"),
                        (financial - LoanRules.CreditFor(amount)).ToString("0.##")));
                Widgets.Label((new Rect(content.x, 535f, content.width, termsHeight)).ToGame(), terms);
                // Revalidate after numeric fields have processed this GUI event.
                string failure = terminal.TransmissionFailure(lender, true) ?? loans.BorrowFailure(lender, amount, days);
                if (draftFailure != null) Widgets.Label((new Rect(content.x, 535f + termsHeight + 18f, content.width, failureHeight)).ToGame(), draftFailure);
                GUI.color = Color.white.ToGame();
                GUI.enabled = failure == null;
                if (Widgets.ButtonText((new Rect(content.x, draftSignY, content.width, 42f)).ToGame(), "HD_Loan_Sign".Translate()))
                {
                    if (!loans.TryBorrow(actor, terminal, amount, days, out string reason))
                        Messages.Message(reason, MessageTypeDefOf.RejectInput);
                    else contract = loans.OpenContract(lender);
                }
                GUI.enabled = true;
            }
            else
            {
                LoanAccount account = contract.Account;
                Widgets.Label((new Rect(content.x, 344f, content.width, 88f)).ToGame(),
                    "HD_Loan_SignedTerms".Translate(account.ApprovedAmount, account.DurationDays,
                        LoanRules.CreditFor(account.ApprovedAmount).ToString("0.##")));
                string status = !account.DeliveryFinished ? "HD_Loan_Pending".Translate().ToString()
                    : account.Closed ? (account.Disbursed == 0 ? "HD_Loan_Cancelled" : "HD_Loan_PaidOff").Translate().ToString()
                    : account.Overdue(Find.TickManager.TicksGame) ? "HD_Loan_OverdueStatus".Translate().ToString()
                    : "HD_Loan_DueIn".Translate((account.DueTick - Find.TickManager.TicksGame).ToStringTicksToPeriod()).ToString();
                Widgets.Label((new Rect(content.x, 446f, content.width, 80f)).ToGame(),
                    "HD_Loan_Account".Translate(account.Disbursed, account.Repaid, account.Outstanding, status));
                Widgets.Label((new Rect(content.x, 548f, content.width, termsHeight)).ToGame(), terms);
                if (account.DeliveryFinished && !account.Closed)
                {
                    if (repayment <= 0 || repayment > account.Outstanding)
                    {
                        repayment = account.Outstanding;
                        repaymentBuffer = repayment.ToString();
                    }
                    DrawNumber(content, repayY, "HD_Loan_Repayment", ref repayment, ref repaymentBuffer, 1, account.Outstanding);
                    Widgets.Label((new Rect(content.x, repayY + 60f, content.width, 48f)).ToGame(),
                        "HD_Loan_RestoreQuote".Translate(LoanRules.CreditFor(repayment).ToString("0.##")));
                    string failure = terminal.TransmissionFailure(lender, true);
                    GUI.color = Color.white.ToGame();
                    GUI.enabled = failure == null;
                    if (Widgets.ButtonText((new Rect(content.x, repaySignY, content.width, 42f)).ToGame(), "HD_Loan_Repay".Translate()))
                        if (!loans.TryRepay(actor, terminal, contract, repayment, out string reason))
                            Messages.Message(reason, MessageTypeDefOf.RejectInput);
                    GUI.enabled = true;
                    if (failure != null) TooltipHandler.TipRegion((new Rect(content.x, repaySignY, content.width, 42f)).ToGame(), failure);
                }
            }
            GUI.color = Color.white.ToGame();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.EndScrollView();
            if (Widgets.ButtonText((new Rect(inRect.xMax - 140f, inRect.yMax - 38f, 140f, 38f)).ToGame(), "CloseButton".Translate())) Close();
        }

        private static void DrawNumber(Rect content, float y, string key, ref int value, ref string buffer, int min, int max)
        {
            GUI.color = Color.black.ToGame();
            Widgets.Label((new Rect(content.x, y, content.width - 210f, 40f)).ToGame(), key.Translate());
            GUI.color = Color.white.ToGame();
            Widgets.TextFieldNumeric((new Rect(content.xMax - 190f, y, 190f, 36f)).ToGame(), ref value, ref buffer, min, max);
            GUI.color = Color.black.ToGame();
        }
    }
}
