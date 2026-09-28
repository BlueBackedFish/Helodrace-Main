using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Preset picker.
    //
    // Deleting used to be a second batch of entries in the same float menu as loading, which
    // put "delete X" one row below "load X" - easy to hit by accident and hard to scan. Here
    // each preset is one row: click it to load, or use the small X on the right, which asks
    // for confirmation first.
    public class Window_PresetList : Window
    {
        private readonly CompWeaponModular comp;
        private readonly Action<WeaponPreset> onLoad;
        private Vector2 scroll;

        private const float RowHeight = 40f;

        public Window_PresetList(CompWeaponModular comp, Action<WeaponPreset> onLoad)
        {
            this.comp = comp;
            this.onLoad = onLoad;

            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override Vector2 InitialSize => new Vector2(460f, 420f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "LGMW_PresetLoad".Translate());
            Text.Font = GameFont.Small;

            ModularWeaponsSettings mgr = ModularPresetManager.Instance;
            List<WeaponPreset> presets = mgr != null
                ? mgr.PresetsFor(comp.parent.def)
                : new List<WeaponPreset>();

            Rect body = new Rect(0f, 40f, inRect.width, inRect.height - 40f);

            if (presets.Count == 0)
            {
                Widgets.Label(body, "LGMW_PresetNone".Translate());
                return;
            }

            Rect view = new Rect(0f, 0f, body.width - 16f, presets.Count * RowHeight);
            Widgets.BeginScrollView(body, ref scroll, view);

            float y = 0f;
            foreach (WeaponPreset preset in presets)
            {
                DrawRow(new Rect(0f, y, view.width, RowHeight - 4f), preset, mgr);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect r, WeaponPreset preset, ModularWeaponsSettings mgr)
        {
            Widgets.DrawMenuSection(r);

            // Delete sits at the far right, away from the click target that loads.
            Rect deleteRect = new Rect(r.xMax - 28f, r.y + (r.height - 20f) * 0.5f, 20f, 20f);
            if (Widgets.ButtonImage(deleteRect, TexButton.CloseXSmall))
            {
                WeaponPreset local = preset;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "LGMW_PresetDeleteConfirm".Translate(local.name),
                    () => mgr?.Delete(local),
                    true));
                return;
            }
            TooltipHandler.TipRegion(deleteRect, "LGMW_PresetDelete".Translate(preset.name));

            // Everything left of the X loads the preset.
            Rect loadRect = new Rect(r.x, r.y, r.width - 34f, r.height);
            if (Widgets.ButtonInvisible(loadRect))
            {
                onLoad?.Invoke(preset);
                Close();
            }
            if (Mouse.IsOver(loadRect)) Widgets.DrawHighlight(loadRect);

            Rect label = loadRect.ContractedBy(8f);
            Widgets.Label(new Rect(label.x, label.y, label.width, 20f), preset.name);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(label.x, label.y + 16f, label.width, 18f), preset.Summary());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
    }
}