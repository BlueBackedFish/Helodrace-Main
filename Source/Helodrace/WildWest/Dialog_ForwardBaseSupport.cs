using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class Dialog_ForwardBaseSupport : Window
    {
        private readonly Faction faction;
        private readonly Dialog_TelegraphTable telegraphDialog;
        private readonly CompTelegraphTable telegraphComp;
        private HelodForwardBase selectedBase;
        private Map selectedMap;
        public override Vector2 InitialSize => new Vector2(760f, 620f);

        public Dialog_ForwardBaseSupport(Faction faction, Dialog_TelegraphTable telegraphDialog = null, CompTelegraphTable telegraphComp = null) { this.faction = faction; this.telegraphDialog = telegraphDialog; this.telegraphComp = telegraphComp; doCloseX = true; absorbInputAroundWindow = true; }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), "HD_TelegraphTable_Support_Title".Translate());
            Text.Font = GameFont.Small;
            List<HelodForwardBase> bases = Find.WorldObjects.AllWorldObjects.OfType<HelodForwardBase>().Where(x => (faction == null || x.Faction == faction) && (x.HasService(HelodForwardBaseService.InfantrySniperSupport) || x.HasService(HelodForwardBaseService.InfantryMortarSupport))).ToList();
            float y = rect.y + 42f;
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "HD_TelegraphTable_Support_Base".Translate()); y += 28f;
            string baseLabel = selectedBase == null ? "HD_TelegraphTable_Support_SelectBaseOnMap".Translate().ToString() : selectedBase.LabelCap + " (" + selectedBase.Tile + ")";
            GUI.color = bases.Count > 0 ? Color.white : Color.gray;
            if (Widgets.ButtonText(new Rect(rect.x, y, rect.width, 36f), baseLabel) && bases.Count > 0) BeginBaseTargeting();
            GUI.color = Color.white;
            y += 48f; Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "HD_TelegraphTable_Support_TargetMap".Translate()); y += 28f;
            string mapLabel = selectedMap == null ? "HD_TelegraphTable_Support_SelectTargetOnMap".Translate().ToString() : (selectedMap.IsPlayerHome ? "HD_TelegraphTable_Support_Settlement".Translate() : "HD_TelegraphTable_Support_Encounter".Translate()) + ": " + selectedMap.Parent.LabelCap + " (" + selectedMap.Tile + ")";
            GUI.color = selectedBase != null ? Color.white : Color.gray;
            if (Widgets.ButtonText(new Rect(rect.x, y, rect.width, 36f), mapLabel) && selectedBase != null) BeginTargetMapTargeting();
            GUI.color = Color.white; y += 52f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, y, rect.width, 28f), "HD_TelegraphTable_Support_Catalog".Translate());
            Text.Font = GameFont.Small;
            y += 34f;
            bool sniper = selectedMap != null && HelodSniperSupportUtility.CanUseBase(selectedMap, selectedBase);
            bool mortar = selectedMap != null && HelodMortarSupportUtility.CanUseBase(selectedMap, selectedBase) && HelodForwardBaseServiceUtility.AvailableMortarShells().Count > 0;
            float cardWidth = (rect.width - 10f) / 2f;
            if (selectedBase == null) Widgets.Label(new Rect(rect.x, y, rect.width, 90f), "HD_TelegraphTable_Support_SelectFirst".Translate());
            else
            {
                int cardIndex = 0;
                if (selectedBase.HasService(HelodForwardBaseService.InfantrySniperSupport))
                {
                    DrawServiceCard(new Rect(rect.x, y, cardWidth, 105f), "HD_TelegraphTable_Support_Sniper".Translate(), "HD_TelegraphTable_Support_SniperDesc".Translate(), sniper, OpenSniperModes);
                    cardIndex++;
                }
                if (selectedBase.HasService(HelodForwardBaseService.InfantryMortarSupport))
                {
                    DrawServiceCard(new Rect(rect.x + cardIndex * (cardWidth + 10f), y, cardWidth, 105f), "HD_TelegraphTable_Support_MortarCatalog".Translate(), "HD_TelegraphTable_Support_MortarDesc".Translate(), mortar, OpenMortarAmmoMenu);
                }
            }
        }

        private void BeginBaseTargeting()
        {
            telegraphDialog?.Close(false);
            Close(false);
            CameraJumper.TryJump(new GlobalTargetInfo(selectedBase?.Tile ?? Find.AnyPlayerHomeMap?.Tile ?? 0));
            Find.WorldTargeter.BeginTargeting(target =>
            {
                HelodForwardBase chosen = Find.WorldObjects.AllWorldObjects.OfType<HelodForwardBase>().FirstOrDefault(x => x.Tile == target.Tile && (faction == null || x.Faction == faction) && (x.HasService(HelodForwardBaseService.InfantrySniperSupport) || x.HasService(HelodForwardBaseService.InfantryMortarSupport)));
                if (chosen == null)
                {
                    Messages.Message("HD_TelegraphTable_Support_InvalidBase".Translate(), MessageTypeDefOf.RejectInput);
                    Reopen();
                    return false;
                }
                selectedBase = chosen; selectedMap = null; Reopen(); return true;
            }, true);
        }

        private void BeginTargetMapTargeting()
        {
            telegraphDialog?.Close(false);
            Close(false);
            CameraJumper.TryJump(new GlobalTargetInfo(selectedBase.Tile));
            Find.WorldTargeter.BeginTargeting(target =>
            {
                Map chosen = Find.Maps.FirstOrDefault(x => x.Parent != null && x.Tile == target.Tile);
                if (chosen == null || !InRange(selectedBase, chosen))
                {
                    Messages.Message("HD_TelegraphTable_Support_InvalidTargetMap".Translate(), MessageTypeDefOf.RejectInput);
                    Reopen();
                    return false;
                }
                selectedMap = chosen; Reopen(); return true;
            }, true);
        }

        private void Reopen()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Find.WindowStack.Add(new Dialog_ForwardBaseSupport(faction, telegraphDialog, telegraphComp) { selectedBase = selectedBase, selectedMap = selectedMap });
        }

        private void OpenSniperModes()
        {
            Map map = selectedMap; HelodForwardBase b = selectedBase;
            CloseForServiceSelection();
            Find.WindowStack.Add(new Dialog_MessageBox("HD_SniperSupport_ModePrompt".Translate(), "HD_SniperSupport_Suppress".Translate(), () => HelodSniperSupportUtility.BeginTargeting(map, HelodSniperSupportMode.Suppress, b, telegraphComp), "HD_SniperSupport_Kill".Translate(), () => HelodSniperSupportUtility.BeginTargeting(map, HelodSniperSupportMode.Kill, b, telegraphComp)));
        }

        private static void DrawServiceCard(Rect rect, string title, string description, bool available, System.Action action)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(inner.x, inner.y, inner.width, 26f), title); Text.Font = GameFont.Small;
            GUI.color = Color.gray; Widgets.Label(new Rect(inner.x, inner.y + 30f, inner.width, 32f), description); GUI.color = Color.white;
            GUI.color = available ? Color.white : Color.gray;
            if (Widgets.ButtonText(new Rect(inner.x, inner.yMax - 28f, inner.width, 28f), available ? "HD_TelegraphTable_Support_Select".Translate() : "HD_TelegraphTable_Support_UnavailableShort".Translate()) && available) action();
            GUI.color = Color.white;
        }

        private void OpenMortarAmmoMenu()
        {
            Map map = selectedMap; HelodForwardBase b = selectedBase;
            CloseForServiceSelection();
            Find.WindowStack.Add(new Dialog_MortarAmmoSelection(map, b, telegraphComp));
        }

        private void CloseForServiceSelection()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Close(false);
            telegraphDialog?.Close(false);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }

        private static bool InRange(HelodForwardBase b, Map map)
        {
            float range = Mathf.Max(b.HasService(HelodForwardBaseService.InfantrySniperSupport) ? HelodForwardBaseServiceUtility.SupportRange(HelodForwardBaseService.InfantrySniperSupport) : 0f, b.HasService(HelodForwardBaseService.InfantryMortarSupport) ? HelodForwardBaseServiceUtility.SupportRange(HelodForwardBaseService.InfantryMortarSupport) : 0f);
            return Find.WorldGrid.ApproxDistanceInTiles(b.Tile, map.Tile) <= range;
        }
    }

    public class Dialog_MortarAmmoSelection : Window
    {
        private readonly Map targetMap;
        private readonly HelodForwardBase forwardBase;
        private readonly CompTelegraphTable telegraphComp;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(680f, 520f);

        public Dialog_MortarAmmoSelection(Map targetMap, HelodForwardBase forwardBase, CompTelegraphTable telegraphComp)
        {
            this.targetMap = targetMap;
            this.forwardBase = forwardBase;
            this.telegraphComp = telegraphComp;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 30f), "HD_MortarSupport_SelectAmmoTitle".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, rect.y + 36f, rect.width, 28f), "HD_MortarSupport_SelectAmmoDesc".Translate(targetMap?.Parent?.LabelCap ?? "-"));
            List<ThingDef> shells = HelodForwardBaseServiceUtility.AvailableMortarShells();
            Rect outRect = new Rect(rect.x, rect.y + 72f, rect.width, rect.height - 72f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, Mathf.Max(outRect.height, shells.Count * 92f));
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            for (int i = 0; i < shells.Count; i++)
            {
                ThingDef shell = shells[i];
                Rect row = new Rect(0f, i * 92f, viewRect.width, 84f);
                Widgets.DrawMenuSection(row);
                Rect inner = row.ContractedBy(10f);
                Widgets.ThingIcon(new Rect(inner.x, inner.y, 52f, 52f), shell);
                Text.Font = GameFont.Medium; Widgets.Label(new Rect(inner.x + 62f, inner.y, inner.width - 182f, 26f), shell.LabelCap); Text.Font = GameFont.Small;
                string price = PriceLabel(shell);
                Widgets.Label(new Rect(inner.x + 62f, inner.y + 30f, inner.width - 182f, 24f), price);
                if (Widgets.ButtonText(new Rect(inner.xMax - 110f, inner.y + 12f, 110f, 34f), "HD_TelegraphTable_Support_Select".Translate())) Select(shell, price);
            }
            Widgets.EndScrollView();
        }

        private string PriceLabel(ThingDef shell)
        {
            return forwardBase.ContractCostKind == HelodForwardBaseCostKind.FFP
                ? "HD_MortarSupport_Pricing_Included".Translate().ToString()
                : "HD_MortarSupport_Pricing_PerCall".Translate(HelodForwardBaseServiceUtility.FormatSthalerValue(HelodForwardBaseServiceUtility.MortarCallCostGoldStandard(shell, HelodMortarSupportUtility.VolleyCount * HelodMortarSupportUtility.ShellsPerVolley))).ToString();
        }

        private void Select(ThingDef shell, string price)
        {
            Close(false);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Find.WindowStack.Add(new Dialog_MessageBox("HD_MortarSupport_AmmoPrompt".Translate(shell.LabelCap, HelodMortarSupportUtility.ScatterRadius, HelodMortarSupportUtility.ShellsPerVolley, HelodMortarSupportUtility.VolleyCount, price), "Confirm".Translate(), () => HelodMortarSupportUtility.BeginTargeting(targetMap, forwardBase, shell, telegraphComp), "CancelButton".Translate(), null));
        }
    }
}
