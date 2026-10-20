using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Economy
{
    public sealed class Dialog_CaravanLosses : Window
    {
        private Faction faction;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1040f, UI.screenWidth - 60f), Mathf.Min(760f, UI.screenHeight - 60f));
        public Dialog_CaravanLosses(Faction faction)
        {
            this.faction = faction;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var ledger = GameComponent_CaravanLosses.Current;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 210f, 34f), "HD_CaravanLoss_Title".Translate(
                faction?.Name ?? "HD_CaravanLoss_AllFactions".Translate().ToString()));
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(inRect.xMax - 190f, inRect.y, 150f, 30f), "HD_CaravanLoss_SelectFaction".Translate()))
            {
                var choices = ledger.RecordedFactions.Select(f => new FloatMenuOption(f.Name, () => { faction = f; scroll = Vector2.zero; })).ToList();
                choices.Insert(0, new FloatMenuOption("HD_CaravanLoss_AllFactions".Translate(), () => { faction = null; scroll = Vector2.zero; }));
                Find.WindowStack.Add(new FloatMenu(choices));
            }
            Widgets.Label(new Rect(inRect.x, inRect.y + 42f, inRect.width, 54f), "HD_CaravanLoss_Totals".Translate(
                ledger.Total(faction).ToString("0.##"), ledger.Total(faction, CaravanLossKind.DroppedItem).ToString("0.##"),
                ledger.Total(faction, CaravanLossKind.Injury).ToString("0.##"), ledger.Total(faction, CaravanLossKind.Death).ToString("0.##")));
            string explanation = "HD_CaravanLoss_Explanation".Translate();
            float explanationHeight = Text.CalcHeight(explanation, inRect.width);
            Widgets.Label(new Rect(inRect.x, inRect.y + 100f, inRect.width, explanationHeight), explanation);
            float listY = inRect.y + 112f + explanationHeight;
            var entries = ledger.Entries(faction).Reverse().ToList();
            Rect list = new Rect(inRect.x, listY, inRect.width, inRect.yMax - listY - 50f);
            Rect view = new Rect(0f, 0f, list.width - 18f, Mathf.Max(list.height, entries.Count * 116f));
            Widgets.BeginScrollView(list, ref scroll, view);
            if (entries.Count == 0) Widgets.Label(view.ContractedBy(12f), "HD_CaravanLoss_Empty".Translate());
            int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / 116f));
            int last = Mathf.Min(entries.Count, Mathf.CeilToInt((scroll.y + list.height) / 116f) + 1);
            for (int index = first; index < last; index++)
            {
                CaravanLossEntry entry = entries[index];
                float y = index * 116f;
                Rect row = new Rect(0f, y, view.width, 108f);
                Widgets.DrawMenuSection(row);
                string type = ("HD_CaravanLoss_Kind_" + entry.Kind).Translate();
                string date = "HD_CaravanLoss_Time".Translate(entry.Tick / 60000 + 1, entry.Tick % 60000 / 2500);
                Widgets.Label(new Rect(10f, y + 6f, view.width - 180f, 24f), date + "  /  " + type + "  /  " + entry.PawnName);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(view.width - 166f, y + 6f, 156f, 24f), "HD_CaravanLoss_Value".Translate(entry.Value.ToString("0.##")));
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(10f, y + 32f, view.width - 20f, 44f), entry.Kind == CaravanLossKind.DroppedItem
                    ? "HD_CaravanLoss_Item".Translate(entry.Subject, entry.Count, entry.UnitValue.ToString("0.##"))
                    : "HD_CaravanLoss_Pawn".Translate(entry.Subject, entry.UnitValue.ToString("0.##")));
                Widgets.Label(new Rect(10f, y + 80f, view.width - 20f, 22f), (entry.Faction?.Name ?? entry.FactionName)
                    + "  /  " + entry.MapName + "  /  " + entry.CaravanKey);
            }
            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(inRect.xMax - 140f, inRect.yMax - 38f, 140f, 38f), "CloseButton".Translate())) Close();
        }
    }
}
