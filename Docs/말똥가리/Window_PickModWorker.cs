using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Lists the colonists who can actually have their weapon modified, with the weapon they
    // are carrying. Anyone who would be rejected never appears, and anyone blocked for a
    // transient reason (no power, unreachable) is shown greyed out with the reason.
    public class Window_PickModWorker : Window
    {
        private readonly CompModularBench bench;
        private readonly List<Pawn> candidates;
        private Vector2 scroll;

        private const float RowHeight = 62f;

        public Window_PickModWorker(CompModularBench bench, List<Pawn> candidates)
        {
            this.bench = bench;
            this.candidates = candidates;

            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override Vector2 InitialSize => new Vector2(560f, 480f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "LGMW_PickWorker".Translate());
            Text.Font = GameFont.Small;

            Rect outRect = new Rect(0f, 40f, inRect.width, inRect.height - 40f);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, candidates.Count * RowHeight);

            Widgets.BeginScrollView(outRect, ref scroll, view);
            float y = 0f;
            foreach (Pawn p in candidates)
            {
                DrawRow(new Rect(0f, y, view.width, RowHeight - 6f), p);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect r, Pawn p)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(6f);

            Rect portrait = new Rect(inner.x, inner.y, inner.height, inner.height);
            Widgets.ThingIcon(portrait, p.equipment.Primary);

            string reason;
            bool ok = bench.CanAcceptOrder(p, out reason);

            Rect label = new Rect(portrait.xMax + 8f, inner.y, inner.width - portrait.width - 130f,
                inner.height);
            // Name always on top; the weapon when the pawn can work, the blocking reason
            // when they can't. The row stays visible either way so the player can see who
            // was considered and why they were passed over.
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(label.x, label.y, label.width, 22f), p.LabelShortCap);

            Text.Font = GameFont.Tiny;
            Rect sub = new Rect(label.x, label.y + 20f, label.width, label.height - 20f);
            if (ok)
                Widgets.Label(sub, p.equipment.Primary.LabelCap.ToString().Colorize(ColorLibrary.Yellow));
            else
                Widgets.Label(sub, reason.Colorize(ColorLibrary.RedReadable));
            Text.Font = GameFont.Small;

            Rect btn = new Rect(inner.xMax - 120f, inner.y + 8f, 120f, inner.height - 16f);
            GUI.enabled = ok;
            if (Widgets.ButtonText(btn, "LGMW_Select".Translate()))
            {
                Close();
                bench.OpenFor(p);
            }
            GUI.enabled = true;
        }
    }
}