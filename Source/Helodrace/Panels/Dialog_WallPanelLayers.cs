using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Rect = Waylith.Math.Rect;
using Vector2 = System.Numerics.Vector2;

namespace Helodrace.Panels
{
    // Plan of one wall or door: the support in the centre and, on each side
    // (map north up), three slot boxes from the wall outward: insulation,
    // shielding, reinforcement. A hit from that side passes through them from
    // the outside in. Panels are not selectable on the map; they are
    // inspected, removed and cancelled here.
    public sealed class Dialog_WallPanelLayers : Window
    {
        private const float Box = 84f, Gap = 6f, TitleHeight = 36f, LegendHeight = 44f;
        private const int Grid = 7;   // centre + 3 slots on each side
        private static readonly PanelSlot[] WallOutward = { PanelSlot.Insulation, PanelSlot.Shielding, PanelSlot.Reinforcement };
        private readonly Building host;

        public Dialog_WallPanelLayers(Building host)
        {
            this.host = host;
            doCloseX = true;
            closeOnClickedOutside = true;
            draggable = true;
            preventCameraMotion = false;
        }

        private static float GridSize => Grid * Box + (Grid - 1) * Gap;
        public override UnityEngine.Vector2 InitialSize => new Vector2(GridSize + 36f, TitleHeight + GridSize + LegendHeight + 44f).ToGame();

        public override void DoWindowContents(UnityEngine.Rect gameRect)
        {
            if (!host.Spawned) { Close(); return; }
            Rect inRect = gameRect.ToNative();
            Map map = host.Map;
            Building_WallPanel[] panels = map.GetComponent<MapComponent_WallPanels>().For(host);
            Thing[] orders = PanelRules.Orders(host).ToArray();
            HashSet<Rot4> faces = new(PanelRules.Faces(host));

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, TitleHeight).ToGame(), "HD_Panel_LayersTitle".Translate(host.LabelCap));
            Text.Font = GameFont.Small;
            float left = inRect.x + (inRect.width - GridSize) / 2f, top = inRect.y + TitleHeight;
            Rect Cell(int gx, int gy) => new Rect(left + gx * (Box + Gap), top + gy * (Box + Gap), Box, Box);

            // Support in the centre.
            Rect centre = Cell(3, 3);
            Widgets.DrawBoxSolid(centre.ToGame(), new Color(.22f, .22f, .22f));
            Widgets.DefIcon(UiMath.Inset(centre, 10f).ToGame(), host.def, host.Stuff);
            TooltipHandler.TipRegion(centre.ToGame(), host.LabelCap);

            foreach (Rot4 face in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                IntVec3 step = face.FacingCell;
                bool allowed = faces.Contains(face);
                bool buried = allowed && PanelRules.FaceBuried(host.Position, face, map);
                for (int i = 0; i < WallOutward.Length; i++)
                {
                    PanelSlot slot = WallOutward[i];
                    // Map north is up on screen: +z is -y in the grid.
                    Rect box = Cell(3 + step.x * (i + 1), 3 - step.z * (i + 1));
                    Thing thing = panels.FirstOrDefault(p => p.Rotation == face && p.Properties.slot == slot) as Thing
                        ?? orders.FirstOrDefault(o => o.Rotation == face && PanelRules.Properties(o.def).slot == slot);
                    DrawSlot(box, face, slot, thing, allowed, buried);
                }
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x, top + GridSize + 8f, inRect.width, LegendHeight).ToGame(), "HD_Panel_LayersLegend".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawSlot(Rect box, Rot4 face, PanelSlot slot, Thing thing, bool allowed, bool buried)
        {
            string where = "HD_Panel_FaceHeader".Translate(face.ToStringWord()) + " · " + ("HD_Panel_Slot_" + slot).Translate();
            if (!allowed) return;   // door hinge sides take no lining
            Widgets.DrawMenuSection(box.ToGame());
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;
            if (thing == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(UiMath.Inset(box, 4f).ToGame(), (buried ? "HD_Panel_FaceBlocked" : "HD_Panel_Slot_" + slot).Translate());
                GUI.color = Color.white;
                TooltipHandler.TipRegion(box.ToGame(), where + "\n" + "HD_Panel_EmptySlot".Translate());
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(box.ToGame());
                ThingDef built = PanelRules.BuiltDef(thing.def);
                var panel = thing as Building_WallPanel;
                bool removing = panel != null && panel.Map.designationManager.DesignationOn(panel, DesignationDefOf.Deconstruct) != null;
                if (panel == null) GUI.color = new Color(1f, 1f, 1f, .45f);
                Widgets.DefIcon(new Rect(box.x + 18f, box.y + 6f, box.width - 36f, box.height - 36f).ToGame(), built);
                GUI.color = removing ? new Color(1f, .45f, .4f) : Color.white;
                Widgets.Label(UiMath.Inset(box, 4f).ToGame(), Short(thing));
                GUI.color = Color.white;
                if (removing) Widgets.DrawBox(box.ToGame(), 2);
                TooltipHandler.TipRegion(box.ToGame(), where + "\n" + built.LabelCap + " " + Status(thing) +
                    (panel != null ? "\n\n" + panel.GetInspectString() : ""));
                if (Widgets.ButtonInvisible(box.ToGame())) Find.WindowStack.Add(new FloatMenu(Options(thing)));
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static List<FloatMenuOption> Options(Thing thing)
        {
            var options = new List<FloatMenuOption>();
            if (thing is Building_WallPanel panel)
            {
                DesignationManager designations = panel.Map.designationManager;
                Designation removal = designations.DesignationOn(panel, DesignationDefOf.Deconstruct);
                if (removal != null) options.Add(new FloatMenuOption("HD_Panel_CancelRemove".Translate(), () => designations.RemoveDesignation(removal)));
                else options.Add(new FloatMenuOption("HD_Panel_Remove".Translate(), () =>
                {
                    if (DebugSettings.godMode) panel.Destroy(DestroyMode.Deconstruct);
                    else designations.AddDesignation(new Designation(panel, DesignationDefOf.Deconstruct));
                }));
            }
            else options.Add(new FloatMenuOption("HD_Panel_CancelOrder".Translate(), () => thing.Destroy(DestroyMode.Cancel)));
            return options;
        }

        private static string Short(Thing thing)
        {
            if (thing is Blueprint) return "HD_Panel_StatusOrdered".Translate();
            if (thing is Frame) return "HD_Panel_StatusBuilding".Translate();
            var panel = (Building_WallPanel)thing;
            return "HD_Panel_StatusHitPoints".Translate(panel.HitPoints, panel.MaxHitPoints);
        }

        private static string Status(Thing thing)
        {
            if (thing is not Building_WallPanel panel) return Short(thing);
            string text = Short(thing) + " · " + (PanelRules.IsActive(panel) ? "HD_Panel_Attached" : "HD_Panel_Inactive").Translate();
            if (panel.Map.designationManager.DesignationOn(panel, DesignationDefOf.Deconstruct) != null)
                text += " · " + "HD_Panel_StatusRemoving".Translate();
            return text;
        }
    }
}
