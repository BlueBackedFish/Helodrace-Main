using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Waylith.Math;
using Vector2 = System.Numerics.Vector2;
using Rect = Waylith.Math.Rect;
using Color = Waylith.Math.Color;
using Verse;

namespace Helodrace
{
    public sealed class Dialog_WeaponLoadout : Window
    {
        private readonly CompM6RocketBag loadout;
        private readonly Dictionary<ThingDef, string> countBuffers = new Dictionary<ThingDef, string>();
        private readonly HashSet<string> expandedGroups = new HashSet<string>();
        private static readonly Regex CaliberPattern = new Regex(
            @"\b\d+(?:\.\d+)?\s*(?:x|×)\s*\d+(?:\.\d+)?\s*mm\b|\b\d+(?:\.\d+)?\s*mm\b|\b\d+(?:\.\d+)?(?:-|\s)?inch\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private Vector2 scrollPosition;
        private string search = string.Empty;

        public override UnityEngine.Vector2 InitialSize => new UnityEngine.Vector2(760f, 620f);

        public Dialog_WeaponLoadout(CompM6RocketBag loadout)
        {
            this.loadout = loadout;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        public override void DoWindowContents(UnityEngine.Rect gameRect)
        {
            Rect inRect = gameRect.ToNative();
            if (loadout?.Wearer == null)
            {
                Widgets.Label(inRect.ToGame(), "HD_WeaponLoadout_NotEquipped".Translate());
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label((new Rect(inRect.x, inRect.y, inRect.width, 32f)).ToGame(), "HD_WeaponLoadout_Title".Translate());
            Text.Font = GameFont.Small;

            float totalMass = loadout.AllowedAmmoDefs.Sum(def =>
                loadout.DesiredCountFor(def) * def.GetStatValueAbstract(StatDefOf.Mass));
            Widgets.Label((new Rect(inRect.x, inRect.y + 38f, inRect.width * 0.55f, 28f)).ToGame(),
                "HD_WeaponLoadout_TotalMass".Translate(totalMass.ToStringMass()));

            Rect searchRect = new Rect(inRect.x + inRect.width - 280f, inRect.y + 34f, 280f, 30f);
            search = Widgets.TextField(searchRect.ToGame(), search ?? string.Empty);
            if (search.NullOrEmpty())
            {
                GUI.color = Color.gray.ToGame();
                Widgets.Label((new Rect(searchRect.x + 5f, searchRect.y + 3f, searchRect.width - 10f, searchRect.height - 6f)).ToGame(), "HD_WeaponLoadout_Search".Translate());
                GUI.color = Color.white.ToGame();
            }

            Rect header = new Rect(inRect.x, inRect.y + 72f, inRect.width - 16f, 28f);
            DrawHeader(header);

            List<IGrouping<string, ThingDef>> ammoGroups = loadout.AllowedAmmoDefs
                .GroupBy(CaliberGroupLabel)
                .Where(group => search.NullOrEmpty()
                    || group.Key.IndexOf(search, System.StringComparison.CurrentCultureIgnoreCase) >= 0
                    || group.Any(def => def.label.IndexOf(search, System.StringComparison.CurrentCultureIgnoreCase) >= 0))
                .OrderBy(group => group.Key)
                .ToList();

            Rect outRect = new Rect(inRect.x, inRect.y + 102f, inRect.width, inRect.height - 102f);
            float contentHeight = ammoGroups.Sum(group => 48f + (expandedGroups.Contains(group.Key) ? group.Count() * 48f : 0f));
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, contentHeight));
            UnityEngine.Vector2 gameScroll = scrollPosition.ToGame();
            Widgets.BeginScrollView(outRect.ToGame(), ref gameScroll, viewRect.ToGame());
            scrollPosition = gameScroll.ToNative();

            float y = 0f;
            foreach (IGrouping<string, ThingDef> group in ammoGroups)
            {
                DrawGroupRow(group, new Rect(0f, y, viewRect.width, 44f));
                y += 48f;

                if (!expandedGroups.Contains(group.Key))
                {
                    continue;
                }

                foreach (ThingDef ammoDef in group.OrderBy(def => def.label))
                {
                    DrawAmmoRow(ammoDef, new Rect(24f, y, viewRect.width - 24f, 44f));
                    y += 48f;
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawGroupRow(IGrouping<string, ThingDef> group, Rect rect)
        {
            bool expanded = expandedGroups.Contains(group.Key);
            Widgets.DrawMenuSection(rect.ToGame());
            Widgets.DrawHighlightIfMouseover(rect.ToGame());

            if (Widgets.ButtonInvisible(rect.ToGame()))
            {
                if (expanded)
                {
                    expandedGroups.Remove(group.Key);
                }
                else
                {
                    expandedGroups.Add(group.Key);
                }
            }

            Text.Font = GameFont.Medium;
            Widgets.Label((new Rect(rect.x + 10f, rect.y + 7f, 28f, 30f)).ToGame(), expanded ? "−" : "+");
            Widgets.Label((new Rect(rect.x + 42f, rect.y + 7f, 268f, 30f)).ToGame(), group.Key);
            Text.Font = GameFont.Small;

            int current = group.Sum(def => InventoryAmmoUtility.Count(loadout.Wearer, def));
            int desired = group.Sum(def => loadout.DesiredCountFor(def));
            float desiredMass = group.Sum(def => loadout.DesiredCountFor(def) * def.GetStatValueAbstract(StatDefOf.Mass));
            Widgets.Label((new Rect(rect.x + 320f, rect.y + 11f, 100f, 28f)).ToGame(), current.ToString());
            Widgets.Label((new Rect(rect.x + 457f, rect.y + 11f, 86f, 28f)).ToGame(), desired.ToString());
            Widgets.Label((new Rect(rect.x + 560f, rect.y + 11f, 160f, 28f)).ToGame(), desiredMass.ToStringMass());
        }

        private static string CaliberGroupLabel(ThingDef ammoDef)
        {
            ThingCategoryDef category = ammoDef?.FirstThingCategory;
            if (category?.defName == "HD_InventoryGrenades")
            {
                return "HD_WeaponLoadout_HandGrenades".Translate().ToString();
            }

            AmmoCaliberExtension extension = category?.GetModExtension<AmmoCaliberExtension>();
            string caliber = extension?.caliber;

            if (caliber.NullOrEmpty())
            {
                string source = (category?.label ?? string.Empty) + " " + (ammoDef?.label ?? string.Empty);
                Match match = CaliberPattern.Match(source);
                if (match.Success)
                {
                    caliber = Regex.Replace(match.Value, @"\s+", string.Empty)
                        .Replace("x", "×")
                        .Replace("X", "×");
                }
            }

            return !caliber.NullOrEmpty()
                ? "HD_WeaponLoadout_CaliberGroup".Translate(caliber).ToString()
                : "HD_WeaponLoadout_UnspecifiedCaliberGroup".Translate(
                    category?.LabelCap ?? "HD_WeaponLoadout_OtherAmmo".Translate()).ToString();
        }

        private static void DrawHeader(Rect rect)
        {
            GUI.color = Color.gray.ToGame();
            Widgets.Label((new Rect(rect.x + 42f, rect.y, 270f, rect.height)).ToGame(), "HD_WeaponLoadout_Ammo".Translate());
            Widgets.Label((new Rect(rect.x + 320f, rect.y, 105f, rect.height)).ToGame(), "HD_WeaponLoadout_Current".Translate());
            Widgets.Label((new Rect(rect.x + 435f, rect.y, 105f, rect.height)).ToGame(), "HD_WeaponLoadout_Desired".Translate());
            Widgets.Label((new Rect(rect.x + 560f, rect.y, 160f, rect.height)).ToGame(), "HD_WeaponLoadout_Mass".Translate());
            GUI.color = Color.white.ToGame();
        }

        private void DrawAmmoRow(ThingDef ammoDef, Rect rect)
        {
            Widgets.DrawHighlightIfMouseover(rect.ToGame());
            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
            Widgets.ThingIcon((new Rect(rect.x + 4f, rect.y + 4f, 36f, 36f)).ToGame(), ammoDef);

            Rect labelRect = new Rect(rect.x + 46f, rect.y + 11f, 264f, 28f);
            Widgets.Label(labelRect.ToGame(), ammoDef.LabelCap);
            TooltipHandler.TipRegion(labelRect.ToGame(), ammoDef.description);

            int current = InventoryAmmoUtility.Count(loadout.Wearer, ammoDef);
            Widgets.Label((new Rect(rect.x + 320f, rect.y + 11f, 100f, 28f)).ToGame(), current.ToString());

            int desired = loadout.DesiredCountFor(ammoDef);
            if (!countBuffers.TryGetValue(ammoDef, out string buffer))
            {
                buffer = desired.ToString();
            }

            Rect minusRect = new Rect(rect.x + 425f, rect.y + 7f, 28f, 30f);
            Rect countRect = new Rect(rect.x + 457f, rect.y + 7f, 54f, 30f);
            Rect plusRect = new Rect(rect.x + 515f, rect.y + 7f, 28f, 30f);

            if (Widgets.ButtonText(minusRect.ToGame(), "−"))
            {
                desired = Mathf.Max(0, desired - 1);
                buffer = desired.ToString();
            }

            Widgets.TextFieldNumeric(countRect.ToGame(), ref desired, ref buffer, 0, 9999);

            if (Widgets.ButtonText(plusRect.ToGame(), "+"))
            {
                desired = Mathf.Min(9999, desired + 1);
                buffer = desired.ToString();
            }

            countBuffers[ammoDef] = buffer;
            loadout.SetDesiredCount(ammoDef, desired);

            float unitMass = ammoDef.GetStatValueAbstract(StatDefOf.Mass);
            Widgets.Label((new Rect(rect.x + 560f, rect.y + 11f, 160f, 28f)).ToGame(),
                "HD_WeaponLoadout_MassValue".Translate(unitMass.ToStringMass(), (unitMass * desired).ToStringMass()));
        }
    }
}
