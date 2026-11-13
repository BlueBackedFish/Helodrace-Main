using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rect = Waylith.Math.Rect;
using Vector2 = System.Numerics.Vector2;

namespace Helodrace.Panels
{
    // Every face of one wall or door with its lining layers, outermost first,
    // in the order a hit passes through them. Panels are not selectable on the
    // map; they are inspected, removed and cancelled here.
    public sealed class Dialog_WallPanelLayers : Window
    {
        private const float HeaderHeight = 28f, RowHeight = 32f, FaceGap = 10f, TitleHeight = 40f, ButtonWidth = 130f;
        private static readonly PanelSlot[] OuterFirst = { PanelSlot.Reinforcement, PanelSlot.Shielding, PanelSlot.Insulation };
        private readonly Building host;

        public Dialog_WallPanelLayers(Building host)
        {
            this.host = host;
            doCloseX = true;
            closeOnClickedOutside = true;
            draggable = true;
            preventCameraMotion = false;
        }

        public override UnityEngine.Vector2 InitialSize =>
            new Vector2(600f, TitleHeight + 60f + PanelRules.Faces(host).Count() * (HeaderHeight + OuterFirst.Length * RowHeight + FaceGap)).ToGame();

        public override void DoWindowContents(UnityEngine.Rect gameRect)
        {
            if (!host.Spawned) { Close(); return; }
            Rect inRect = gameRect.ToNative();
            Map map = host.Map;
            Building_WallPanel[] panels = map.GetComponent<MapComponent_WallPanels>().For(host);
            Thing[] orders = PanelRules.Orders(host).ToArray();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, TitleHeight).ToGame(), "HD_Panel_LayersTitle".Translate(host.LabelCap));
            Text.Font = GameFont.Small;
            float y = inRect.y + TitleHeight;
            foreach (Rot4 face in PanelRules.Faces(host))
            {
                bool buried = PanelRules.FaceBuried(host.Position, face, map);
                string header = "HD_Panel_FaceHeader".Translate(face.ToStringWord());
                if (buried) header += "  " + "HD_Panel_FaceBlocked".Translate();
                Widgets.Label(new Rect(inRect.x, y, inRect.width, HeaderHeight).ToGame(), header);
                Widgets.DrawLineHorizontal(inRect.x, y + HeaderHeight - 2f, inRect.width);
                y += HeaderHeight;
                foreach (PanelSlot slot in OuterFirst)
                {
                    Rect row = new Rect(inRect.x, y, inRect.width, RowHeight - 2f);
                    Thing panel = panels.FirstOrDefault(p => p.Rotation == face && p.Properties.slot == slot);
                    Thing order = orders.FirstOrDefault(o => o.Rotation == face && PanelRules.Properties(o.def).slot == slot);
                    DrawRow(row, slot, panel ?? order);
                    y += RowHeight;
                }
                y += FaceGap;
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawRow(Rect row, PanelSlot slot, Thing thing)
        {
            Widgets.DrawHighlightIfMouseover(row.ToGame());
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x + 6f, row.y, 120f, row.height).ToGame(), ("HD_Panel_Slot_" + slot).Translate());
            if (thing == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(row.x + 130f, row.y, row.width - 130f, row.height).ToGame(), "HD_Panel_EmptySlot".Translate());
                GUI.color = Color.white;
                return;
            }
            ThingDef built = PanelRules.BuiltDef(thing.def);
            Widgets.DefIcon(new Rect(row.x + 130f, row.y + 2f, row.height - 4f, row.height - 4f).ToGame(), built);
            Widgets.Label(new Rect(row.x + 164f, row.y, row.width - 164f - ButtonWidth - 8f, row.height).ToGame(), built.LabelCap + "  " + Status(thing));
            Rect button = new Rect(row.xMax - ButtonWidth, row.y + 2f, ButtonWidth, row.height - 4f);
            Text.Anchor = TextAnchor.UpperLeft;
            if (thing is Building_WallPanel panel)
            {
                TooltipHandler.TipRegion(row.ToGame(), panel.GetInspectString());
                DesignationManager designations = panel.Map.designationManager;
                Designation removal = designations.DesignationOn(panel, DesignationDefOf.Deconstruct);
                if (Widgets.ButtonText(button.ToGame(), (removal != null ? "HD_Panel_CancelRemove" : "HD_Panel_Remove").Translate()))
                {
                    if (removal != null) designations.RemoveDesignation(removal);
                    else if (DebugSettings.godMode) panel.Destroy(DestroyMode.Deconstruct);
                    else designations.AddDesignation(new Designation(panel, DesignationDefOf.Deconstruct));
                }
            }
            else if (Widgets.ButtonText(button.ToGame(), "HD_Panel_CancelOrder".Translate()))
                thing.Destroy(DestroyMode.Cancel);
        }

        private static string Status(Thing thing)
        {
            if (thing is Blueprint) return "HD_Panel_StatusOrdered".Translate();
            if (thing is Frame) return "HD_Panel_StatusBuilding".Translate();
            var panel = (Building_WallPanel)thing;
            string state = (PanelRules.IsActive(panel) ? "HD_Panel_Attached" : "HD_Panel_Inactive").Translate();
            string text = "HD_Panel_StatusHitPoints".Translate(panel.HitPoints, panel.MaxHitPoints) + " · " + state;
            if (panel.Map.designationManager.DesignationOn(panel, DesignationDefOf.Deconstruct) != null)
                text += " · " + "HD_Panel_StatusRemoving".Translate();
            return text;
        }
    }
}
