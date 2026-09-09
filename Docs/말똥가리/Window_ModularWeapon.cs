using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace LGModularWeapons
{
    // Staged customization: the player builds a configuration, sees a live preview of the
    // resulting weapon, and only on "Apply" is the total cost calculated and consumed.
    // Nothing touches the weapon until then, so cancelling is free.
    public class Window_ModularWeapon : Window
    {
        private readonly CompWeaponModular comp;

        // Pending (unconfirmed) configuration. Starts as a copy of what's fitted now.
        private readonly Dictionary<WeaponPartSlotDef, WeaponPartDef> pending =
            new Dictionary<WeaponPartSlotDef, WeaponPartDef>();

        private Vector2 scroll;
        private readonly PreviewView view = new PreviewView();

        private const float PreviewSize = 480f; // 3x - part placement is unreadable smaller
        private const float RowHeight = 58f;

        // Bench mode: the change is queued as an order and the chosen pawn hauls the
        // materials and does the work. Null bench = the old immediate-apply path.
        private readonly CompModularBench bench;
        private readonly Pawn worker;

        private bool BenchMode => bench != null && worker != null;

        public Window_ModularWeapon(CompWeaponModular comp)
            : this(comp, null, null)
        {
        }

        public Window_ModularWeapon(CompWeaponModular comp, CompModularBench bench, Pawn worker)
        {
            this.comp = comp;
            this.bench = bench;
            this.worker = worker;
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;

            foreach (var slot in comp.ActiveSlots)
                pending[slot] = comp.PartInSlot(slot);

            RefreshPendingSlots();
        }

        // Slots available for the PENDING configuration - recomputed whenever the player
        // changes something, since fitting a handguard can expose new rail slots and
        // removing it takes them (and anything in them) away again.
        private List<WeaponPartSlotDef> pendingSlots = new List<WeaponPartSlotDef>();

        private void RefreshPendingSlots()
        {
            pendingSlots = comp.ComputeActiveSlots(pending);

            // Drop staged parts whose slot just disappeared, and parts whose prerequisites
            // stopped being met (swapping the barrel can invalidate the muzzle device).
            // Removing one can invalidate another, so repeat until it settles.
            for (int pass = 0; pass < 8; pass++)
            {
                bool changed = CompWeaponModular.PruneOrphans(pending, pendingSlots);
                changed |= comp.PruneInvalid(pending, pendingSlots);
                if (!changed) break;
                pendingSlots = comp.ComputeActiveSlots(pending);
            }

            // make sure every available slot has an entry so lookups stay simple
            foreach (var slot in pendingSlots)
                if (!pending.ContainsKey(slot)) pending[slot] = null;
        }

        // Clamped so the enlarged preview can't push the window off-screen at low resolutions.
        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(1320f, UI.screenWidth - 40f),
            Mathf.Min(780f, UI.screenHeight - 40f));

        private bool Dirty
        {
            get
            {
                // a slot appearing or disappearing is itself a change
                if (pendingSlots.Count != comp.ActiveSlots.Count) return true;
                foreach (var slot in pendingSlots)
                {
                    pending.TryGetValue(slot, out var p);
                    if (p != comp.PartInSlot(slot)) return true;
                }
                return false;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f),
                "LGMW_WindowTitle".Translate(comp.parent.LabelCap));
            Text.Font = GameFont.Small;

            float top = 42f;
            float bottomBar = 42f;
            float bodyH = inRect.height - top - bottomBar - 8f;

            // ---- left: stat comparison, current vs pending ----
            const float StatWidth = 250f;
            Rect statCol = new Rect(0f, top, StatWidth, bodyH);
            ModularStatPanel.Draw(statCol, comp, pending, pendingSlots);

            // ---- centre: live preview of the configured weapon ----
            Rect midCol = new Rect(statCol.xMax + 10f, top, PreviewSize, bodyH);
            DrawPreview(new Rect(midCol.x, midCol.y, PreviewSize, PreviewSize));

            // ---- right: preset bar + slot list ----
            Rect rightCol = new Rect(midCol.xMax + 12f, top,
                inRect.width - midCol.xMax - 12f, bodyH);

            Rect presetBar = new Rect(rightCol.x, rightCol.y, rightCol.width, 30f);
            DrawPresetBar(presetBar);

            DrawSlotList(new Rect(rightCol.x, presetBar.yMax + 6f,
                rightCol.width, rightCol.height - presetBar.height - 6f));

            // ---- bottom: cost + apply ----
            DrawFooter(new Rect(0f, inRect.height - bottomBar, inRect.width, bottomBar));
        }

        private void DrawPreview(Rect r)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(8f);

            // Everything is laid out in WORLD CELLS and converted once, so the preview
            // honours each part's graphicData drawSize instead of forcing square quads.
            // Without this, a part scaled via drawSize (e.g. 1.6 x 0.4) previewed as a
            // square while the world drew it correctly.
            Graphic gunGraphic = comp.parent.Graphic;
            Vector2 gunSize = gunGraphic != null ? gunGraphic.drawSize : Vector2.one;
            float cells = Mathf.Max(gunSize.x, gunSize.y, 0.01f);

            Vector2 origin = view.HandleInput(r, inner.center);
            float px = inner.width * view.zoom / cells;   // pixels per world cell

            // order by resolved layer so the preview stacks like the world does
            var previewParts = new List<KeyValuePair<WeaponPartDef, PartDrawData>>();
            foreach (var slot in pendingSlots)
            {
                pending.TryGetValue(slot, out var pp);
                if (pp?.Graphic == null) continue;
                // Resolve against the PENDING config, so a barrel swap repositions the muzzle
                // device in the preview right away.
                previewParts.Add(new KeyValuePair<WeaponPartDef, PartDrawData>(
                    pp, comp.ResolveDrawData(pp, pending, pendingSlots)));
            }
            previewParts.Sort((a, b) => a.Value.layer.CompareTo(b.Value.layer));

            // Negative layers must be drawn BEFORE the weapon body, positives after -
            // exactly like the map-mesh under/over passes. Drawing the gun first and every
            // part after it is why negative layers looked like they did nothing here.
            DrawPreviewParts(previewParts, origin, px, true);

            // Use the actual weapon GRAPHIC, not def.uiIcon. uiIcon is a separately baked
            // (and possibly trimmed/atlased) texture whose extents don't match graphicData
            // drawSize, so parts drawn underneath were being masked against the wrong shape.
            Texture baseTex = gunGraphic?.MatSingle?.mainTexture ?? comp.parent.def.uiIcon;
            if (baseTex != null)
                GUI.DrawTexture(CenteredRect(origin, gunSize * px), baseTex, ScaleMode.StretchToFill);

            DrawPreviewParts(previewParts, origin, px, false);

            view.DrawControls(r);
            TooltipHandler.TipRegion(r, "LGMW_PreviewTip".Translate());
        }

        private void DrawPreviewParts(List<KeyValuePair<WeaponPartDef, PartDrawData>> parts,
            Vector2 origin, float px, bool under)
        {
            foreach (var pair in parts)
            {
                if ((pair.Value.layer < 0f) != under) continue;

                var part = pair.Key;
                var tex = part.Graphic?.MatSingle?.mainTexture;
                if (tex == null) continue;

                // graphicData <color> lives on the MATERIAL, not the texture, so pulling the
                // raw mainTexture drops the tint - the part looked untinted here while the
                // world showed it correctly. Apply the graphic's colour to GUI.color.
                Color prevColor = GUI.color;
                GUI.color = part.Graphic.Color;

                // Same placement the world renderer will use, so the preview is truthful.
                PartDrawData data = pair.Value;

                Vector2 sizePx = part.Graphic.drawSize * data.scale * px;
                Vector2 centre = new Vector2(
                    origin.x + data.offset.x * px,
                    origin.y - data.offset.z * px);   // world z maps to screen -y

                Rect o = CenteredRect(centre, sizePx);

                Matrix4x4 m = GUI.matrix;
                if (!Mathf.Approximately(data.angleOffset, 0f))
                    UI.RotateAroundPivot(-data.angleOffset, o.center);
                GUI.DrawTexture(o, tex, ScaleMode.StretchToFill);
                GUI.matrix = m;
                GUI.color = prevColor;
            }
        }

        private static Rect CenteredRect(Vector2 centre, Vector2 size)
        {
            return new Rect(centre.x - size.x * 0.5f, centre.y - size.y * 0.5f, size.x, size.y);
        }


        private void DrawPresetBar(Rect r)
        {
            float half = (r.width - 6f) * 0.5f;

            if (Widgets.ButtonText(new Rect(r.x, r.y, half, r.height), "LGMW_PresetLoad".Translate()))
                OpenPresetMenu();

            if (Widgets.ButtonText(new Rect(r.x + half + 6f, r.y, half, r.height),
                    "LGMW_PresetSaveCurrent".Translate()))
            {
                Find.WindowStack.Add(new Dialog_NamePreset(SuggestedPresetName(), name =>
                {
                    ModularWeaponsSettings mgr = ModularPresetManager.Instance;
                    if (mgr == null) return;

                    // Saves the PENDING build, not the installed one - what you see in the
                    // preview is what gets stored, so you can design a loadout and keep it
                    // even if you cancel the order.
                    mgr.Save(name, comp.parent.def, pending);
                    Messages.Message("LGMW_PresetSaved".Translate(name),
                        MessageTypeDefOf.TaskCompletion, false);
                }));
            }
        }

        private string SuggestedPresetName()
        {
            ModularWeaponsSettings mgr = ModularPresetManager.Instance;
            int n = mgr != null ? mgr.PresetsFor(comp.parent.def).Count + 1 : 1;
            return "LGMW_PresetDefaultName".Translate(n);
        }

        private void OpenPresetMenu()
        {
            Find.WindowStack.Add(new Window_PresetList(comp, ApplyPreset));
        }

        // Loads a preset into the PENDING build. Parts that are no longer valid (research
        // lost, conflicting with something the preset does not cover) are simply skipped,
        // so a stale preset degrades instead of producing an impossible configuration.
        private void ApplyPreset(WeaponPreset preset)
        {
            pending.Clear();
            // Resolved here, not at settings-load time: defs do not exist yet when the mod
            // settings file is read.
            foreach (var kv in preset.BuildConfig())
                pending[kv.Key] = kv.Value;

            RefreshPendingSlots();

            List<WeaponPartSlotDef> slots = new List<WeaponPartSlotDef>(pendingSlots);
            foreach (WeaponPartSlotDef slot in slots)
            {
                pending.TryGetValue(slot, out var part);
                if (part == null) continue;

                string reason;
                if (!comp.CanFitPart(part, pending, pendingSlots, out reason))
                {
                    pending[slot] = null;
                    Messages.Message("LGMW_PresetPartSkipped".Translate(part.LabelCap, reason),
                        MessageTypeDefOf.CautionInput, false);
                }
            }

            RefreshPendingSlots();
        }

        private void DrawSlotList(Rect r)
        {
            var slots = pendingSlots.OrderBy(s => s.uiOrder).ToList();
            Rect view = new Rect(0f, 0f, r.width - 16f, slots.Count * RowHeight);
            Widgets.BeginScrollView(r, ref scroll, view);

            float y = 0f;
            foreach (var slot in slots)
            {
                DrawSlotRow(new Rect(0f, y, view.width, RowHeight - 6f), slot);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawSlotRow(Rect r, WeaponPartSlotDef slot)
        {
            Widgets.DrawMenuSection(r);
            Rect inner = r.ContractedBy(8f);

            pending.TryGetValue(slot, out var part);
            string label;
            if (part != null)
            {
                label = part.LabelCap.ToString();
                if (part != comp.PartInSlot(slot))
                    label = label.Colorize(ColorLibrary.Yellow); // staged change
            }
            else if (slot.required && slot.defaultPart == null)
            {
                label = "LGMW_RequiredMissing".Translate().Colorize(ColorLibrary.RedReadable);
            }
            else
            {
                label = "LGMW_Empty".Translate().ToString();
            }

            Widgets.Label(new Rect(inner.x, inner.y + 4f, inner.width - 150f, inner.height),
                slot.LabelCap + ": " + label);

            if (Widgets.ButtonText(new Rect(inner.xMax - 140f, inner.y + 4f, 140f, inner.height - 8f),
                    "LGMW_Change".Translate()))
                OpenPartMenu(slot);
        }

        private void OpenPartMenu(WeaponPartSlotDef slot)
        {
            var options = new List<FloatMenuOption>();

            if (!slot.required)
                options.Add(new FloatMenuOption("LGMW_Remove".Translate(), () =>
                {
                    pending[slot] = null;
                    RefreshPendingSlots();
                    ModularSounds.PlayInstallUI();
                }));

            foreach (var part in DefDatabase<WeaponPartDef>.AllDefsListForReading)
            {
                if (part.slot != slot) continue;
                if (!part.AppliesTo(comp.parent.def)) continue;

                // Blocked parts stay VISIBLE but disabled, with the reason attached - a
                // silently missing entry just looks like the mod is broken.
                string blockReason;
                if (!comp.CanFitPart(part, pending, pendingSlots, out blockReason))
                {
                    options.Add(new FloatMenuOption(
                        part.LabelCap + " - " + blockReason, null));
                    continue;
                }

                string label = part.LabelCap;
                if (!part.costList.NullOrEmpty())
                    label += " (" + CostString(part.costList) + ")";

                var localPart = part;
                options.Add(new FloatMenuOption(label, () =>
                {
                    pending[slot] = localPart;
                    RefreshPendingSlots();
                    ModularSounds.PlayInstallUI();
                }));
            }

            if (options.Count == 0)
                options.Add(new FloatMenuOption("LGMW_NoParts".Translate(), null));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        // Total cost of the pending configuration: only parts that are being NEWLY fitted
        // are charged. Keeping a part you already had is free.
        private Dictionary<ThingDef, int> PendingCost()
        {
            var total = new Dictionary<ThingDef, int>();
            foreach (var slot in pendingSlots)
            {
                pending.TryGetValue(slot, out var newPart);
                if (newPart == null || newPart == comp.PartInSlot(slot)) continue;
                if (newPart.costList.NullOrEmpty()) continue;

                foreach (var c in newPart.costList)
                    total[c.thingDef] = (total.TryGetValue(c.thingDef, out var n) ? n : 0) + c.count;
            }
            return total;
        }

        // Half the cost of every part being REMOVED comes back, rounded up so an odd cost
        // never rounds down to nothing (a 5-steel part refunds 3, a 3-steel part refunds 2).
        private Dictionary<ThingDef, int> PendingRefund()
        {
            var total = new Dictionary<ThingDef, int>();

            foreach (var slot in pendingSlots)
            {
                WeaponPartDef old = comp.PartInSlot(slot);
                if (old == null) continue;

                pending.TryGetValue(slot, out var replacement);
                if (replacement == old) continue;   // untouched
                if (old.costList.NullOrEmpty()) continue;

                foreach (ThingDefCountClass c in old.costList)
                {
                    int back = Mathf.CeilToInt(c.count * 0.5f);
                    if (back <= 0) continue;
                    total[c.thingDef] = (total.TryGetValue(c.thingDef, out var n) ? n : 0) + back;
                }
            }
            return total;
        }

        private void DrawFooter(Rect r)
        {
            var cost = PendingCost();
            Map map = comp.parent.MapHeld;
            bool affordable = ModularResourceUtility.HasResources(map, cost);
            bool incomplete = comp.TryGetMissingRequiredSlots(pending, pendingSlots, out var missing);

            // Bench-side blockers (power lost, pawn dropped the weapon...) are re-checked
            // live, so the window can't queue an order that would silently never run.
            string benchBlock = null;
            if (BenchMode && !bench.CanAcceptOrder(worker, out benchBlock))
                benchBlock = benchBlock ?? "LGMW_Fail_Generic".Translate();
            else
                benchBlock = null;

            string costText;
            if (benchBlock != null)
            {
                costText = benchBlock;
            }
            else if (incomplete)
            {
                // A weapon missing a required part can't be finished - say which slots.
                costText = "LGMW_MissingRequiredNamed".Translate(
                    string.Join(", ", missing.Select(m => m.LabelCap.ToString())));
            }
            else if (cost.Count == 0)
            {
                // NOTE: if/else rather than a ternary on purpose - Translate() returns
                // TaggedString, and mixing it with string in a ternary fails on C# 7.3 (CS8957).
                costText = "LGMW_NoCost".Translate();
            }
            else
            {
                costText = "LGMW_TotalCost".Translate() + ": " +
                    string.Join(", ", cost.Select(kv => kv.Value + "x " + kv.Key.LabelCap));
            }

            GUI.color = (affordable && !incomplete && benchBlock == null)
                ? Color.white : ColorLibrary.RedReadable;
            Widgets.Label(new Rect(r.x, r.y + 8f, r.width - 320f, r.height), costText);
            GUI.color = Color.white;

            if (Widgets.ButtonText(new Rect(r.xMax - 310f, r.y + 4f, 140f, r.height - 8f),
                    "LGMW_Revert".Translate()))
            {
                pending.Clear();
                foreach (var slot in comp.ActiveSlots)
                    pending[slot] = comp.PartInSlot(slot);
                RefreshPendingSlots();
            }

            bool canApply = Dirty && affordable && !incomplete && benchBlock == null;
            GUI.enabled = canApply;
            string applyLabel = BenchMode ? "LGMW_QueueOrder".Translate() : "LGMW_Apply".Translate();
            if (Widgets.ButtonText(new Rect(r.xMax - 160f, r.y + 4f, 160f, r.height - 8f),
                    applyLabel))
                Apply(cost, map);
            GUI.enabled = true;
        }

        private void Apply(Dictionary<ThingDef, int> cost, Map map)
        {
            if (BenchMode)
            {
                QueueOrder(cost);
                return;
            }

            // Consume once, at confirmation time - not per part selection.
            if (!ModularResourceUtility.TryConsume(map, cost))
            {
                Messages.Message("LGMW_NoResources".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            // Apply the whole configuration at once. Applying slot-by-slot re-validated
            // after every single part, which pruned parts whose prerequisites hadn't been
            // installed yet - they'd appear to apply, then vanish.
            comp.SetConfiguration(pending);

            ModularSounds.PlayCompleteAt(comp.parent);

            Messages.Message("LGMW_Applied".Translate(comp.parent.LabelCap),
                comp.parent, MessageTypeDefOf.PositiveEvent, false);
            Close();
        }

        // Nothing is consumed and nothing changes on the weapon yet: the pawn has to walk
        // over, carry the materials to the bench and put in the work first.
        private void QueueOrder(Dictionary<ThingDef, int> cost)
        {
            ModOrder order = new ModOrder
            {
                pawn = worker,
                weapon = comp.parent as ThingWithComps
            };

            int changed = 0;
            foreach (var slot in pendingSlots)
            {
                pending.TryGetValue(slot, out var p);
                if (p != comp.PartInSlot(slot)) changed++;
                if (p != null) order.config[slot] = p;
            }

            foreach (var kv in cost)
                order.cost.Add(new ThingDefCountClass(kv.Key, kv.Value));

            foreach (var kv in PendingRefund())
                order.refund.Add(new ThingDefCountClass(kv.Key, kv.Value));

            order.workAmount = Mathf.Max(1, changed) * bench.Props.workPerPart;

            bench.AddOrder(order);
            // Order placed - the completion sound plays later, at the bench.
            ModularSounds.PlayInstallUI();
            Messages.Message("LGMW_OrderQueued".Translate(worker.LabelShortCap,
                comp.parent.LabelCap), worker, MessageTypeDefOf.TaskCompletion, false);
            Close();
        }

        // "steel x12, component x1" - naming the shortfall is far more useful than a flat
        // "not enough resources".
        private static string MissingText(Dictionary<ThingDef, int> cost, Map map)
        {
            var parts = new List<string>();
            foreach (var kv in cost)
            {
                int have = ModularResourceUtility.CountAvailable(map, kv.Key);
                int shortfall = kv.Value - have;
                if (shortfall > 0)
                    parts.Add(kv.Key.LabelCap + " x" + shortfall);
            }
            return string.Join(", ", parts);
        }

        private static string CostString(List<ThingDefCountClass> cost)
            => string.Join(", ", cost.Select(c => c.count + "x " + c.thingDef.LabelCap));
    }
}