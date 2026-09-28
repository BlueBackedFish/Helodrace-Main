using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Dev tool: live-adjust where each fitted part is drawn, watch it update on the weapon
    // in real time (ground AND held), then copy the finished placement out as XML.
    //
    // Deliberately does NOT pause the game or absorb input, so you can draft a pawn, have
    // them aim in different directions, and check the overlay follows correctly - including
    // the mirrored west-facing case and recoil.
    public class Window_PartOffsetTuner : Window
    {
        private readonly CompWeaponModular comp;
        private Vector2 scroll;
        private float step = 0.01f;
        private Vector3 bodyOffset;

        // Working values, seeded from whatever the weapon currently resolves to.
        private readonly Dictionary<WeaponPartDef, PartDrawData> working =
            new Dictionary<WeaponPartDef, PartDrawData>();

        public Window_PartOffsetTuner(CompWeaponModular comp)
        {
            this.comp = comp;

            forcePause = false;            // watch the pawn move/shoot while tuning
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            draggable = true;
            doCloseX = true;
            closeOnClickedOutside = false;
            resizeable = true;

            // ActiveSlots, not Props.slots: slots opened by a fitted part (a handguard's
            // rails, a barrel's bayonet lug) only exist in the resolved list, so the static
            // one silently hid those parts from the tuner while they rendered fine in game.
            foreach (var slot in comp.ActiveSlots)
            {
                var part = comp.PartInSlot(slot);
                if (part == null) continue;
                working[part] = comp.ResolveDrawData(part);
            }
            bodyOffset = comp.BodyOffsetRaw;
            PushToComp();
        }

        // Clamped so the enlarged preview can't push the window off-screen at low resolutions.
        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(1180f, UI.screenWidth - 40f),
            Mathf.Min(820f, UI.screenHeight - 40f));

        protected override float Margin => 12f;

        private bool ConfigurationChanged()
        {
            int fitted = 0;
            foreach (var slot in comp.ActiveSlots)
            {
                var part = comp.PartInSlot(slot);
                if (part == null) continue;
                fitted++;
                if (!working.ContainsKey(part)) return true;
            }
            return fitted != working.Count;
        }

        private void ReseedFromComp()
        {
            working.Clear();
            comp.ClearTuning();

            foreach (var slot in comp.ActiveSlots)
            {
                var part = comp.PartInSlot(slot);
                if (part == null) continue;
                working[part] = comp.ResolveDrawData(part);
            }
            PushToComp();
        }

        private void PushToComp()
        {
            foreach (var kv in working)
                comp.SetTuning(kv.Key, kv.Value);
            comp.SetBodyOffsetTuning(bodyOffset);
        }

        public override void PreClose()
        {
            base.PreClose();
            comp.ClearTuning(); // tuning is a preview, never persisted
        }

        public override void DoWindowContents(Rect inRect)
        {
            // If the weapon was rebuilt at the bench while this window sat open, the tuned
            // values belong to parts that may no longer be fitted - re-seed rather than
            // pushing stale offsets back onto the new configuration every frame.
            if (ConfigurationChanged()) ReseedFromComp();

            Text.Font = GameFont.Small;

            Rect header = new Rect(0f, 0f, inRect.width, 28f);
            Widgets.Label(header, "Part offset tuner - " + comp.parent.LabelCap);

            // step size selector
            Rect stepRow = new Rect(0f, 30f, inRect.width, 26f);
            Widgets.Label(new Rect(stepRow.x, stepRow.y, 70f, stepRow.height), "Step:");
            float x = stepRow.x + 70f;
            foreach (float s in new[] { 0.001f, 0.01f, 0.05f })
            {
                Rect b = new Rect(x, stepRow.y, 70f, stepRow.height - 2f);
                if (Widgets.ButtonText(b, s.ToString("0.###"), true, true, !Mathf.Approximately(step, s)))
                    step = s;
                x += 74f;
            }

            float bodyTop = 62f;
            float footer = 70f;

            // Live preview column, so you can judge placement without hunting for the weapon
            // on the map. Uses the same ResolveDrawData the world renderer does.
            // 3x the old size - you cannot judge a 2px nudge on a small preview.
            // Bounded by the window so a narrow screen still leaves room for the sliders.
            float previewW = Mathf.Min(600f, inRect.width - 380f);
            previewW = Mathf.Min(previewW, inRect.height - bodyTop - footer);
            Rect previewRect = new Rect(0f, bodyTop, previewW, previewW);
            DrawPreview(previewRect);

            Rect body = new Rect(previewW + 12f, bodyTop,
                inRect.width - previewW - 12f, inRect.height - bodyTop - footer);

            // Whole-weapon offset, applies to body + every attachment together.
            Rect bodyRow = new Rect(body.x, body.y, body.width, 44f);
            float by = bodyRow.y;
            Widgets.Label(new Rect(bodyRow.x, by, bodyRow.width, 20f), "Whole weapon offset");
            by += 20f;
            float bhalf = (bodyRow.width - 8f) * 0.5f;
            float yx = by, yz = by;
            float nx = SliderRow(new Rect(bodyRow.x, by, bhalf, 24f), ref yx, "Body X", bodyOffset.x, -1f, 1f);
            float nz = SliderRow(new Rect(bodyRow.x + bhalf + 8f, by, bhalf, 24f), ref yz, "Body Z", bodyOffset.z, -1f, 1f);
            if (nx != bodyOffset.x || nz != bodyOffset.z)
            {
                bodyOffset = new Vector3(nx, 0f, nz);
                PushToComp();
            }
            body = new Rect(body.x, bodyRow.yMax + 6f, body.width, body.height - 50f);

            if (working.Count == 0)
            {
                Widgets.Label(body, "No fitted part has a usable graphic.\n\n"
                    + "Either nothing is installed (check the customization window), or the "
                    + "installed parts have no <graphicData> / their texture failed to load. "
                    + "Use the 'DEV: dump modular state' gizmo for a per-slot breakdown.");
            }
            else
            {
                const float rowH = 408f;
                Rect view = new Rect(0f, 0f, body.width - 16f, working.Count * rowH);
                Widgets.BeginScrollView(body, ref scroll, view);
                float y = 0f;
                foreach (var part in working.Keys.ToList())
                {
                    DrawPartBlock(new Rect(0f, y, view.width, rowH - 6f), part);
                    y += rowH;
                }
                Widgets.EndScrollView();
            }

            DrawFooter(new Rect(0f, inRect.height - footer + 6f, inRect.width, footer - 6f));
        }

        private readonly PreviewView view = new PreviewView();

        private void DrawPreview(Rect r)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(10f);

            // Lay out in WORLD CELLS, convert once. This is what makes a part scaled through
            // its graphicData drawSize preview with the right proportions instead of being
            // squashed into a square.
            Graphic gunGraphic = comp.parent.Graphic;
            Vector2 gunSize = gunGraphic != null ? gunGraphic.drawSize : Vector2.one;
            float cells = Mathf.Max(gunSize.x, gunSize.y, 0.01f);
            Vector2 origin = view.HandleInput(r, new Vector2(inner.center.x, inner.center.y - 10f));
            float px = inner.width * view.zoom / cells;   // pixels per world cell

            var ordered = working.ToList();
            ordered.Sort((a, b) => a.Value.layer.CompareTo(b.Value.layer));

            // Negative layers go BEFORE the weapon body, positives after - same under/over
            // split the world renderer uses, so what you tune is what you get.
            DrawParts(ordered, origin, px, true);

            // Actual weapon graphic, not def.uiIcon - see the note in Window_ModularWeapon.
            Texture baseTex = gunGraphic?.MatSingle?.mainTexture ?? comp.parent.def.uiIcon;
            if (baseTex != null)
                GUI.DrawTexture(CenteredRect(origin, gunSize * px), baseTex, ScaleMode.StretchToFill);

            DrawParts(ordered, origin, px, false);

            DrawLaserPreview(origin, px);
            DrawFlashlightPreview(origin, px);

            view.DrawControls(r);

            Rect help = new Rect(r.x + 8f, r.yMax - 22f, r.width - 16f, 18f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(help, "LGMW_PreviewHelp".Translate());
            Text.Font = GameFont.Small;
        }

        // Preview of the light.
        //
        // Drawn as plain tinted bars rather than by blitting the beam texture: the texture is
        // authored vertically (bottom = emitter) while the preview points right, so drawing
        // it directly came out rotated and offset. The bars show the SHAPE, which is what the
        // sliders actually control.
        private void DrawFlashlightPreview(Vector2 origin, float px)
        {
            foreach (var kv in working)
            {
                FlashlightProps light = kv.Key.flashlight;
                if (light == null) continue;

                PartDrawData d = kv.Value;

                Vector2 start = new Vector2(
                    origin.x + (d.offset.x + d.lightOffset.x) * px,
                    origin.y - (d.offset.z + d.lightOffset.z) * px);

                float length = px * 2.2f;
                float near = Mathf.Max(1f, d.lightCone * px);
                float far = Mathf.Max(2f, d.lightRadius * px);

                Color old = GUI.color;
                GUI.color = new Color(light.color.r, light.color.g, light.color.b,
                    Mathf.Max(0.25f, light.color.a));

                const int Steps = 12;
                for (int i = 0; i < Steps; i++)
                {
                    float t = (float)i / Steps;
                    float w = Mathf.Lerp(near, far, t);
                    GUI.DrawTexture(
                        new Rect(start.x + length * t, start.y - w * 0.5f,
                            length / Steps + 1f, w),
                        BaseContent.WhiteTex);
                }

                if (light.drawCircle && d.lightCircle > 0f)
                {
                    float dia = d.lightCircle * 2f * px;
                    GUI.DrawTexture(new Rect(start.x + length - dia * 0.5f,
                        start.y - dia * 0.5f, dia, dia), BaseContent.WhiteTex);
                }

                GUI.color = old;
            }
        }

        // Draws a short beam from each emitter so the origin can be lined up here rather
        // than by drafting a pawn and squinting at the map.
        private void DrawLaserPreview(Vector2 origin, float px)
        {
            foreach (var kv in working)
            {
                LaserSightProps laser = kv.Key.laser;
                if (laser == null) continue;

                PartDrawData d = kv.Value;

                // Anchored to the part's own drawn position, matching the world renderer:
                // laserOffset is measured from the sight body, not from the weapon centre.
                Vector2 start = new Vector2(
                    origin.x + (d.offset.x + d.laserOffset.x) * px,
                    origin.y - (d.offset.z + d.laserOffset.z) * px);

                // Beam points right, matching the sprite's forward direction in this preview.
                Vector2 end = new Vector2(start.x + px * 1.6f, start.y);

                Color old = GUI.color;
                GUI.color = laser.color;
                Widgets.DrawLine(start, end, laser.color, Mathf.Max(1f, laser.beamWidth * px));

                if (laser.drawDot)
                {
                    float dot = Mathf.Max(3f, d.laserDot * px);
                    GUI.DrawTexture(new Rect(end.x - dot * 0.5f, end.y - dot * 0.5f, dot, dot),
                        BaseContent.WhiteTex);
                }
                GUI.color = old;
            }
        }

        private void DrawParts(List<KeyValuePair<WeaponPartDef, PartDrawData>> ordered,
            Vector2 origin, float px, bool under)
        {
            foreach (var kv in ordered)
            {
                PartDrawData d = kv.Value;
                if ((d.layer < 0f) != under) continue;

                WeaponPartDef part = kv.Key;
                Texture tex = part.Graphic?.MatSingle?.mainTexture;
                if (tex == null) continue;

                // See Window_ModularWeapon.DrawPreview: the tint is on the material.
                Color prevColor = GUI.color;
                GUI.color = part.Graphic.Color;

                Vector2 sizePx = part.Graphic.drawSize * d.scale * px;
                Vector2 centre = new Vector2(
                    origin.x + d.offset.x * px,
                    origin.y - d.offset.z * px);

                Rect o = CenteredRect(centre, sizePx);

                Matrix4x4 m = GUI.matrix;
                if (!Mathf.Approximately(d.angleOffset, 0f))
                    UI.RotateAroundPivot(-d.angleOffset, o.center);
                GUI.DrawTexture(o, tex, ScaleMode.StretchToFill);
                GUI.matrix = m;
                GUI.color = prevColor;
            }
        }

        private static Rect CenteredRect(Vector2 centre, Vector2 size)
        {
            return new Rect(centre.x - size.x * 0.5f, centre.y - size.y * 0.5f, size.x, size.y);
        }

        private void DrawPartOverlay(Rect gunRect, float size,
            KeyValuePair<WeaponPartDef, PartDrawData> kv)
        {
            Texture tex = kv.Key.Graphic?.MatSingle?.mainTexture;
            if (tex == null) return;

            PartDrawData d = kv.Value;
            float pw = size * d.scale;
            Rect o = new Rect(
                gunRect.center.x - pw * 0.5f + d.offset.x * size,
                gunRect.center.y - pw * 0.5f - d.offset.z * size,
                pw, pw);

            Matrix4x4 m = GUI.matrix;
            if (!Mathf.Approximately(d.angleOffset, 0f))
                UI.RotateAroundPivot(-d.angleOffset, o.center);
            GUI.DrawTexture(o, tex, ScaleMode.ScaleToFit);
            GUI.matrix = m;
        }

        private void DrawPartBlock(Rect r, WeaponPartDef part)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(8f);
            var data = working[part];

            string where = data.layer < 0f
                ? "UNDER gun".Colorize(ColorLibrary.RedReadable)
                : "over gun".Colorize(ColorLibrary.Green);

            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f),
                part.LabelCap.ToString().Colorize(ColorLibrary.Yellow)
                + "  (" + part.slot.defName + ")   " + where);

            float rowY = inner.y + 24f;
            data.offset.x = SliderRow(inner, ref rowY, "X (along barrel)", data.offset.x, -1.5f, 1.5f);
            data.offset.z = SliderRow(inner, ref rowY, "Z (up on sprite)", data.offset.z, -1.5f, 1.5f);
            data.scale = SliderRow(inner, ref rowY, "Scale", data.scale, 0.2f, 3f);
            data.angleOffset = SliderRow(inner, ref rowY, "Angle", data.angleOffset, -180f, 180f);
            data.layer = SliderRow(inner, ref rowY, "Layer (draw order)", data.layer, -10f, 10f);

            // Emitter origin, only for parts that actually shoot a beam.
            if (part.laser != null)
            {
                data.laserOffset.x = SliderRow(inner, ref rowY, "Laser X (from part)",
                    data.laserOffset.x, -0.6f, 0.6f);
                data.laserOffset.z = SliderRow(inner, ref rowY, "Laser Z (from part)",
                    data.laserOffset.z, -0.6f, 0.6f);
                data.laserWidth = SliderRow(inner, ref rowY, "Beam width",
                    data.laserWidth, 0.01f, 0.3f);
                data.laserDot = SliderRow(inner, ref rowY, "Dot size",
                    data.laserDot, 0.02f, 0.6f);
            }

            if (part.flashlight != null)
            {
                data.lightOffset.x = SliderRow(inner, ref rowY, "Light X (from part)",
                    data.lightOffset.x, -0.6f, 0.6f);
                data.lightOffset.z = SliderRow(inner, ref rowY, "Light Z (from part)",
                    data.lightOffset.z, -0.6f, 0.6f);
                data.lightCone = SliderRow(inner, ref rowY, "Beam width (near)",
                    data.lightCone, 0.02f, 1.5f);
                data.lightRadius = SliderRow(inner, ref rowY, "Beam width (far)",
                    data.lightRadius, 0.05f, 4f);
                data.lightCircle = SliderRow(inner, ref rowY, "Pool radius",
                    data.lightCircle, 0.05f, 2f);
            }

            working[part] = data;
            comp.SetTuning(part, data); // live update, no apply button needed
        }

        private float SliderRow(Rect area, ref float y, string label, float value, float min, float max)
        {
            Rect row = new Rect(area.x, y, area.width, 24f);
            y += 25f;

            Widgets.Label(new Rect(row.x, row.y, 120f, row.height), label);

            // nudge buttons use the selected step for precise work
            if (Widgets.ButtonText(new Rect(row.x + 122f, row.y + 2f, 24f, 20f), "-"))
                value = Mathf.Clamp(value - step, min, max);
            if (Widgets.ButtonText(new Rect(row.x + 148f, row.y + 2f, 24f, 20f), "+"))
                value = Mathf.Clamp(value + step, min, max);

            Rect sliderRect = new Rect(row.x + 178f, row.y + 3f, row.width - 178f - 62f, 18f);
            value = Widgets.HorizontalSlider(sliderRect, value, min, max);

            Widgets.Label(new Rect(row.xMax - 58f, row.y, 58f, row.height), value.ToString("0.###"));
            return value;
        }

        private void DrawFooter(Rect r)
        {
            float bw = (r.width - 16f) / 3f;

            if (Widgets.ButtonText(new Rect(r.x, r.y, bw, 32f), "Reset"))
            {
                foreach (var part in working.Keys.ToList())
                {
                    comp.ClearTuning();
                    working[part] = comp.ResolveDrawData(part);
                }
                PushToComp();
            }

            if (Widgets.ButtonText(new Rect(r.x + bw + 8f, r.y, bw, 32f), "Copy XML"))
            {
                string xml = BuildXml();
                GUIUtility.systemCopyBuffer = xml;
                Log.Message(xml); // also to console, in case the clipboard is unavailable
                Messages.Message("drawOverrides XML copied to clipboard (also logged).",
                    MessageTypeDefOf.TaskCompletion, false);
            }

            if (Widgets.ButtonText(new Rect(r.x + (bw + 8f) * 2f, r.y, bw, 32f), "Close"))
                Close();
        }

        // Emits a block that can be pasted straight into the weapon's
        // CompProperties_WeaponModular. Values matching the default are omitted.
        private string BuildXml()
        {
            StringBuilder sb = new StringBuilder();
            if (bodyOffset != Vector3.zero)
                sb.AppendLine("<bodyDrawOffset>(" + bodyOffset.x.ToString("0.###")
                              + ", 0, " + bodyOffset.z.ToString("0.###") + ")</bodyDrawOffset>");
            sb.AppendLine("<drawOverrides>");
            foreach (var kv in working)
            {
                PartDrawData d = kv.Value;
                sb.AppendLine("  <li>");
                sb.AppendLine("    <part>" + kv.Key.defName + "</part>");
                sb.AppendLine("    <offset>(" + d.offset.x.ToString("0.###") + ", 0, "
                              + d.offset.z.ToString("0.###") + ")</offset>");
                if (!Mathf.Approximately(d.scale, 1f))
                    sb.AppendLine("    <scale>" + d.scale.ToString("0.###") + "</scale>");
                if (!Mathf.Approximately(d.angleOffset, 0f))
                    sb.AppendLine("    <angleOffset>" + d.angleOffset.ToString("0.###") + "</angleOffset>");
                if (!Mathf.Approximately(d.layer, 0f))
                    sb.AppendLine("    <layer>" + d.layer.ToString("0.###") + "</layer>");
                if (kv.Key.flashlight != null)
                {
                    sb.AppendLine("    <lightOffset>(" + d.lightOffset.x.ToString("0.###")
                                  + ", 0, " + d.lightOffset.z.ToString("0.###") + ")</lightOffset>");
                    sb.AppendLine("    <lightCone>" + d.lightCone.ToString("0.#") + "</lightCone>");
                    sb.AppendLine("    <lightRadius>" + d.lightRadius.ToString("0.###") + "</lightRadius>");
                    sb.AppendLine("    <lightCircle>" + d.lightCircle.ToString("0.###") + "</lightCircle>");
                }
                if (kv.Key.laser != null)
                {
                    sb.AppendLine("    <laserOffset>(" + d.laserOffset.x.ToString("0.###")
                                  + ", 0, " + d.laserOffset.z.ToString("0.###") + ")</laserOffset>");
                    sb.AppendLine("    <laserWidth>" + d.laserWidth.ToString("0.###") + "</laserWidth>");
                    sb.AppendLine("    <laserDot>" + d.laserDot.ToString("0.###") + "</laserDot>");
                }
                sb.AppendLine("  </li>");
            }
            sb.AppendLine("</drawOverrides>");
            return sb.ToString();
        }
    }
}