using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    internal sealed class Dialog_FluxRaiderCinematicControls : Window
    {
        private sealed class Control
        {
            public string key;
            public string label;
            public string description;
            public Action action;
            public bool enabled;
        }

        private Pawn pawn;
        private bool arranging;
        private string draggingKey;
        private static readonly Color NormalTile = new Color(0.16f, 0.19f, 0.22f);
        private static readonly Color DragTile = new Color(0.30f, 0.40f, 0.48f);

        public Dialog_FluxRaiderCinematicControls(Pawn pawn)
        {
            this.pawn = pawn;
            doCloseX = true;
            draggable = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
        }

        public static void Open(Pawn pawn)
        {
            if (Find.WindowStack.IsOpen<Dialog_FluxRaiderCinematicControls>()) return;
            Find.WindowStack.Add(new Dialog_FluxRaiderCinematicControls(pawn));
        }

        public override Vector2 InitialSize => new Vector2(650f, 520f);

        public override void PostClose()
        {
            HelodraceBase.Instance?.WriteSettings();
            base.PostClose();
        }

        public override void DoWindowContents(Rect bounds)
        {
            if (!Prefs.DevMode)
            {
                Close();
                return;
            }
            Pawn selected = Find.Selector.SingleSelectedThing as Pawn;
            if (selected?.Faction == Faction.OfPlayer
                && FluxRaiderCinematic.IsFluxRaider(selected)) pawn = selected;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(bounds.x, bounds.y, bounds.width - 225f, 30f),
                "Flux Raider filming · " + (pawn?.LabelShort ?? "none"));
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(bounds.xMax - 211f, bounds.y + 34f, 105f, 27f),
                arranging ? "Done" : "Arrange"))
            {
                arranging = !arranging;
                draggingKey = null;
                if (!arranging) HelodraceBase.Instance?.WriteSettings();
            }
            if (Widgets.ButtonText(new Rect(bounds.xMax - 101f, bounds.y + 34f, 95f, 27f),
                "Reset layout"))
            {
                draggingKey = null;
                FluxRaiderReloadTimeline.Settings.ButtonOrder.Clear();
                HelodraceBase.Instance?.WriteSettings();
            }

            if (pawn == null || !FluxRaiderCinematic.IsFluxRaider(pawn))
            {
                Widgets.Label(new Rect(bounds.x, bounds.y + 70f, bounds.width, 45f),
                    "Select a player pawn carrying a Flux Raider P320.");
                return;
            }

            bool active = FluxRaiderCinematic.Active(pawn);
            Widgets.Label(new Rect(bounds.x, bounds.y + 35f, bounds.width - 225f, 27f),
                arranging ? "Drag a button onto another button to swap their positions."
                    : active ? "Drag the title to move this window."
                        : "Start filming to enable the controls.");

            List<Control> controls = BuildControls(pawn, active);
            List<string> order = FluxRaiderReloadTimeline.Settings.ButtonOrder;
            foreach (Control control in controls)
                if (!order.Contains(control.key)) order.Add(control.key);
            controls = controls.OrderBy(control => order.IndexOf(control.key)).ToList();

            Rect grid = new Rect(bounds.x, bounds.y + 70f,
                bounds.width, bounds.height - 72f);
            HandleDrop(grid, controls, order);
            for (int i = 0; i < controls.Count; i++)
                DrawControl(TileRect(grid, i), controls[i]);
        }

        private void HandleDrop(Rect grid, List<Control> controls,
            List<string> order)
        {
            Event current = Event.current;
            if (!arranging || draggingKey == null) return;
            if (current.type == EventType.MouseDrag)
            {
                current.Use();
                return;
            }
            if (current.type != EventType.MouseUp || current.button != 0) return;
            for (int i = 0; i < controls.Count; i++)
            {
                if (!TileRect(grid, i).Contains(current.mousePosition)) continue;
                int from = order.IndexOf(draggingKey);
                int to = order.IndexOf(controls[i].key);
                if (from >= 0 && to >= 0 && from != to)
                {
                    string moved = order[from];
                    order[from] = order[to];
                    order[to] = moved;
                    HelodraceBase.Instance?.WriteSettings();
                }
                break;
            }
            draggingKey = null;
            current.Use();
        }

        private static Rect TileRect(Rect grid, int index)
        {
            const int columns = 3;
            const float gap = 6f;
            const float height = 50f;
            float width = (grid.width - gap * (columns - 1)) / columns;
            return new Rect(grid.x + (index % columns) * (width + gap),
                grid.y + (index / columns) * (height + gap), width, height);
        }

        private void DrawControl(Rect rect, Control control)
        {
            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            if (arranging)
            {
                Widgets.DrawBoxSolidWithOutline(rect,
                    draggingKey == control.key ? DragTile : NormalTile,
                    Color.gray, 1);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(rect.ContractedBy(3f), control.label);
                if (Event.current.type == EventType.MouseDown
                    && Event.current.button == 0
                    && rect.Contains(Event.current.mousePosition))
                {
                    draggingKey = control.key;
                    Event.current.Use();
                }
            }
            else
            {
                Color previous = GUI.color;
                if (!control.enabled) GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                bool clicked = Widgets.ButtonText(rect, control.label);
                GUI.color = previous;
                if (clicked && control.enabled) control.action();
            }
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
            TooltipHandler.TipRegion(rect, control.description);
        }

        private static List<Control> BuildControls(Pawn pawn, bool active)
        {
            List<Control> result = new List<Control>();
            void Add(string key, string label, string description, Action action,
                bool enabled = true) => result.Add(new Control
                {
                    key = key,
                    label = label,
                    description = description,
                    action = action,
                    enabled = enabled
                });

            Add("session", active ? "Stop filming" : "Start filming",
                "Toggle the filming controls for this pawn.",
                () => FluxRaiderCinematic.ToggleSession(pawn));
            Add("low_ready", "Low ready", "Lower the weapon.",
                () => FluxRaiderCinematic.SetPose(pawn, FluxCinematicPose.LowReady), active);
            Add("aim_stock_first", "Curved aim\nstock first",
                "Extend the stock, then raise the weapon on a curved path.",
                () => FluxRaiderCinematic.SetAiming(pawn, true), active);
            Add("aim_together", "Curved aim\ntogether",
                "Extend the stock while raising the weapon on a curved path.",
                () => FluxRaiderCinematic.SetAiming(pawn, false), active);
            Add("target_stock_first", "Target aim\nstock first",
                "Pick a firing direction, then extend the stock before aiming.",
                () => FluxRaiderCinematic.BeginVanillaAiming(pawn, true), active);
            Add("target_together", "Target aim\ntogether",
                "Pick a firing direction and extend the stock while aiming.",
                () => FluxRaiderCinematic.BeginVanillaAiming(pawn, false), active);
            Add("tune_low_ready", "Tune low ready",
                "Adjust the low-ready screen position and angle.", () =>
                {
                    FluxRaiderCinematic.SetPose(pawn, FluxCinematicPose.LowReady);
                    Find.WindowStack.Add(new Dialog_FluxRaiderLowReady(pawn));
                }, active);
            foreach (FluxCinematicPose pose in new[]
                { FluxCinematicPose.Lean, FluxCinematicPose.Prone })
                foreach (Rot4 direction in new[] { Rot4.East, Rot4.West })
                {
                    FluxCinematicPose choice = pose;
                    Rot4 facing = direction;
                    Add((pose == FluxCinematicPose.Lean ? "lean_" : "prone_")
                        + direction.ToString().ToLowerInvariant(),
                        pose + " " + direction,
                        "Face " + direction + " and enter the " + pose + " pose.",
                        () =>
                        {
                            FluxRaiderCinematic.FaceForPose(pawn, facing);
                            FluxRaiderCinematic.SetPose(pawn, choice);
                        }, active);
                }
            bool locked = FluxRaiderCinematic.TryLockedDirection(pawn, out Rot4 facingLock);
            Add("facing_current", locked ? "Unlock facing" : "Lock current facing",
                locked ? "Release facing lock (" + facingLock + ")."
                    : "Keep the pawn facing its current direction.",
                () =>
                {
                    if (FluxRaiderCinematic.TryLockedDirection(pawn, out _))
                        FluxRaiderCinematic.UnlockDirection(pawn);
                    else FluxRaiderCinematic.LockDirection(pawn, pawn.Rotation);
                }, active);
            foreach (Rot4 direction in new[]
                { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                Rot4 facing = direction;
                Add("facing_" + direction.ToString().ToLowerInvariant(),
                    "Lock " + direction, "Face " + direction + " and hold that direction.",
                    () => FluxRaiderCinematic.LockDirection(pawn, facing), active);
            }
            Add("fire", "Fire (visual)",
                "Play one gunshot and its animation without a projectile.",
                () => FluxRaiderCinematic.FireVisual(pawn), active);
            Add("reload", "Reload (visual)",
                "Play one visual gunshot, then reload with the slide held back.",
                () => FluxRaiderCinematic.StartReload(pawn), active);
            Add("keyframes", "Reload keyframes",
                "Edit magazine paths and scrub the reload preview.",
                () => Find.WindowStack.Add(new Dialog_FluxRaiderReloadKeyframes(pawn)), active);
            Add("reset_magazines", "Reset magazines",
                "Return the magazines to their initial filming positions.",
                () => FluxRaiderCinematic.ResetReload(pawn),
                active && FluxRaiderCinematic.HasReloadPlayed(pawn));
            Add("lights", FluxRaiderCinematic.EmittersOn(pawn)
                    ? "Lights off" : "Lights on",
                "Toggle attached laser and flashlight effects.",
                () => FluxRaiderCinematic.ToggleEmitters(pawn), active);
            return result;
        }
    }
}
