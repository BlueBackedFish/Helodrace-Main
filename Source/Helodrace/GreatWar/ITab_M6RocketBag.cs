using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class ITab_M6RocketBag : ITab
    {
        private Vector2 scrollPosition = Vector2.zero;

        public ITab_M6RocketBag()
        {
            size = new Vector2(520f, 320f);
            labelKey = "HD_ITab_M6RocketBag_Title";
        }

        public override bool IsVisible => SelectedBag != null;

        private Pawn SelectedPawn => Find.Selector.SingleSelectedThing as Pawn;

        private CompM6RocketBag SelectedBag
        {
            get
            {
                return SelectedPawn?.apparel?.WornApparel?
                    .Select(apparel => apparel.TryGetComp<CompM6RocketBag>())
                    .FirstOrDefault(comp => comp != null);
            }
        }

        protected override void FillTab()
        {
            CompM6RocketBag bag = SelectedBag;
            Rect outRect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);

            if (SelectedPawn == null || bag == null)
            {
                Widgets.Label(outRect, "HD_ITab_M6RocketBag_NoBag".Translate().Resolve());
                return;
            }

            float viewHeight = 78f + (bag.AllowedAmmoDefs.Count() * 44f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, viewHeight));

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect, true);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, viewRect.width, 30f), "HD_ITab_M6RocketBag_Title".Translate().Resolve());
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(0f, 34f, viewRect.width, 24f), "HD_ITab_M6RocketBag_Stored".Translate(bag.TotalStoredRoundsForUI, bag.MaxStoredRoundsForUI).Resolve());

            float y = 70f;
            foreach (ThingDef ammoDef in bag.AllowedAmmoDefs)
            {
                DrawAmmoRow(bag, ammoDef, new Rect(0f, y, viewRect.width, 38f));
                y += 44f;
            }

            Widgets.EndScrollView();
        }

        private static void DrawAmmoRow(CompM6RocketBag bag, ThingDef ammoDef, Rect rect)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            int stored = bag.StoredCountFor(ammoDef);

            Rect labelRect = new Rect(rect.x, rect.y + 4f, rect.width - 182f, 30f);
            Widgets.Label(labelRect, "HD_ITab_M6RocketBag_AmmoRow".Translate(ammoDef.label, stored).Resolve());

            Rect mapRect = new Rect(rect.xMax - 176f, rect.y + 4f, 82f, 30f);
            Rect dropRect = new Rect(rect.xMax - 88f, rect.y + 4f, 82f, 30f);

            bool full = bag.TotalStoredRoundsForUI >= bag.MaxStoredRoundsForUI;
            if (Widgets.ButtonText(mapRect, "HD_ITab_M6RocketBag_LoadMapShort".Translate().Resolve()))
            {
                if (!full)
                {
                    bag.TryStartLoadAmmoJobFromMap(ammoDef);
                }
            }

            if (Widgets.ButtonText(dropRect, "HD_ITab_M6RocketBag_DropShort".Translate().Resolve()) && stored > 0)
            {
                bag.DropAmmo(ammoDef);
            }
        }
    }
}
