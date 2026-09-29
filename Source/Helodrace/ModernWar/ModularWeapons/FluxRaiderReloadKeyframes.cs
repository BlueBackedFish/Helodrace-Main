using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class FluxMagazineKeyframe : IExposable
    {
        public float time;
        public float x;
        public float z;
        public float angle;
        // The spare magazine's path from the front holder (0) to the feed well (1).
        public float travel;

        public FluxMagazineKeyframe() { }

        public FluxMagazineKeyframe(float time, float x, float z, float angle,
            float travel = 0f)
        {
            this.time = time;
            this.x = x;
            this.z = z;
            this.angle = angle;
            this.travel = travel;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref time, "time");
            Scribe_Values.Look(ref x, "x");
            Scribe_Values.Look(ref z, "z");
            Scribe_Values.Look(ref angle, "angle");
            Scribe_Values.Look(ref travel, "travel");
        }
    }

    public sealed class FluxRaiderCinematicSettings : ModSettings
    {
        public int reloadDurationTicks = 72;
        public List<FluxMagazineKeyframe> droppedMagazine;
        public List<FluxMagazineKeyframe> spareMagazine;

        public List<FluxMagazineKeyframe> Dropped
        {
            get
            {
                EnsureDefaults();
                return droppedMagazine;
            }
        }

        public List<FluxMagazineKeyframe> Spare
        {
            get
            {
                EnsureDefaults();
                return spareMagazine;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref reloadDurationTicks, "fluxCinematicReloadDurationTicks", 72);
            Scribe_Collections.Look(ref droppedMagazine, "fluxCinematicDroppedMagazine",
                LookMode.Deep);
            Scribe_Collections.Look(ref spareMagazine, "fluxCinematicSpareMagazine",
                LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                reloadDurationTicks = Mathf.Clamp(reloadDurationTicks, 12, 240);
                EnsureDefaults();
            }
        }

        public void Reset()
        {
            reloadDurationTicks = 72;
            droppedMagazine = DefaultDropped();
            spareMagazine = DefaultSpare();
        }

        private void EnsureDefaults()
        {
            if (droppedMagazine == null || droppedMagazine.Count < 2)
                droppedMagazine = DefaultDropped();
            if (spareMagazine == null || spareMagazine.Count < 2)
                spareMagazine = DefaultSpare();
            spareMagazine[0].travel = 0f;
            spareMagazine[spareMagazine.Count - 1].travel = 1f;
        }

        private static List<FluxMagazineKeyframe> DefaultDropped() =>
            new List<FluxMagazineKeyframe>
            {
                new FluxMagazineKeyframe(0f, 0f, 0f, 0f),
                new FluxMagazineKeyframe(0.27f, -0.02f, -0.07f, 0f),
                new FluxMagazineKeyframe(0.65f, -0.11f, -0.40f, 0f),
                new FluxMagazineKeyframe(1f, -0.05f, -1.07f, 8.93f)
            };

        private static List<FluxMagazineKeyframe> DefaultSpare() =>
            new List<FluxMagazineKeyframe>
            {
                new FluxMagazineKeyframe(0f, 0f, 0f, 0f, 0f),
                new FluxMagazineKeyframe(0.35f, 0f, 0f, 0f, 0f),
                new FluxMagazineKeyframe(0.51f, 0.03f, -0.11f, 0f, 0.35f),
                new FluxMagazineKeyframe(0.66f, 0.03f, -0.26f, 0f, 0.62f),
                new FluxMagazineKeyframe(0.80f, -0.04f, -0.19f, 16.86f, 0.90f),
                new FluxMagazineKeyframe(0.88f, -0.03f, -0.06f, 7.93f, 0.90f),
                new FluxMagazineKeyframe(0.93f, 0f, 0f, 0f, 1f),
                new FluxMagazineKeyframe(1f, 0f, 0f, 0f, 1f)
            };
    }

    internal static class FluxRaiderReloadTimeline
    {
        private static readonly FluxRaiderCinematicSettings fallback =
            new FluxRaiderCinematicSettings();

        public static FluxRaiderCinematicSettings Settings =>
            HelodraceBase.CinematicSettings ?? fallback;

        public static FluxMagazineKeyframe Sample(
            List<FluxMagazineKeyframe> frames, float progress)
        {
            if (frames == null || frames.Count == 0) return new FluxMagazineKeyframe();
            progress = Mathf.Clamp01(progress);
            if (progress <= frames[0].time) return Copy(frames[0], progress);
            for (int i = 1; i < frames.Count; i++)
            {
                if (progress > frames[i].time) continue;
                FluxMagazineKeyframe before = frames[i - 1];
                FluxMagazineKeyframe after = frames[i];
                float t = Mathf.InverseLerp(before.time, after.time, progress);
                return new FluxMagazineKeyframe(progress,
                    Mathf.Lerp(before.x, after.x, t),
                    Mathf.Lerp(before.z, after.z, t),
                    Mathf.Lerp(before.angle, after.angle, t),
                    Mathf.Lerp(before.travel, after.travel, t));
            }
            return Copy(frames[frames.Count - 1], progress);
        }

        private static FluxMagazineKeyframe Copy(FluxMagazineKeyframe source, float time) =>
            new FluxMagazineKeyframe(time, source.x, source.z,
                source.angle, source.travel);
    }

    internal sealed class Dialog_FluxRaiderReloadKeyframes : Window
    {
        private readonly Pawn pawn;
        private bool editingSpare;
        private int selectedFrame;
        private float preview;

        public Dialog_FluxRaiderReloadKeyframes(Pawn pawn)
        {
            this.pawn = pawn;
            doCloseX = true;
            draggable = true;
            preview = 0f;
            FluxRaiderCinematic.SetReloadPreview(pawn, preview);
        }

        public override Vector2 InitialSize => new Vector2(620f, 465f);

        public override void PostClose()
        {
            FluxRaiderCinematic.ClearReloadPreview(pawn);
            HelodraceBase.Instance?.WriteSettings();
            base.PostClose();
        }

        public override void DoWindowContents(Rect bounds)
        {
            if (!FluxRaiderCinematic.Active(pawn))
            {
                Close();
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(bounds.x, bounds.y, bounds.width, 30f),
                "Flux Raider reload keyframes");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(bounds.x, bounds.y + 36f, 100f, 24f),
                "Preview " + preview.ToString("P0"));
            preview = Widgets.HorizontalSlider(
                new Rect(bounds.x + 105f, bounds.y + 39f, bounds.width - 110f, 18f),
                preview, 0f, 1f, true);
            FluxRaiderCinematic.SetReloadPreview(pawn, preview);

            float tabY = bounds.y + 70f;
            if (Widgets.ButtonText(new Rect(bounds.x, tabY, 140f, 28f),
                editingSpare ? "Dropped magazine" : "[Dropped magazine]"))
            {
                editingSpare = false;
                selectedFrame = 0;
            }
            if (Widgets.ButtonText(new Rect(bounds.x + 145f, tabY, 140f, 28f),
                editingSpare ? "[Spare magazine]" : "Spare magazine"))
            {
                editingSpare = true;
                selectedFrame = 0;
            }
            Widgets.Label(new Rect(bounds.x + 299f, tabY + 2f, 70f, 24f),
                "Ticks " + FluxRaiderReloadTimeline.Settings.reloadDurationTicks);
            FluxRaiderReloadTimeline.Settings.reloadDurationTicks = Mathf.RoundToInt(
                Widgets.HorizontalSlider(
                    new Rect(bounds.x + 371f, tabY + 5f, bounds.width - 374f, 18f),
                    FluxRaiderReloadTimeline.Settings.reloadDurationTicks,
                    12f, 240f, true));

            List<FluxMagazineKeyframe> frames = editingSpare
                ? FluxRaiderReloadTimeline.Settings.Spare
                : FluxRaiderReloadTimeline.Settings.Dropped;
            selectedFrame = Mathf.Clamp(selectedFrame, 0, frames.Count - 1);
            float listY = bounds.y + 108f;
            for (int i = 0; i < frames.Count; i++)
            {
                string label = (i == selectedFrame ? "> " : "  ")
                    + i + "  " + frames[i].time.ToString("P0");
                if (Widgets.ButtonText(new Rect(bounds.x, listY + i * 29f, 142f, 26f), label))
                {
                    selectedFrame = i;
                    preview = frames[i].time;
                }
            }

            FluxMagazineKeyframe frame = frames[selectedFrame];
            Rect panel = new Rect(bounds.x + 160f, listY,
                bounds.width - 160f, 250f);
            float y = panel.y;
            if (selectedFrame > 0 && selectedFrame < frames.Count - 1)
            {
                float minimum = frames[selectedFrame - 1].time + 0.01f;
                float maximum = frames[selectedFrame + 1].time - 0.01f;
                if (maximum > minimum)
                {
                    float previousTime = frame.time;
                    frame.time = Mathf.Clamp(
                        SliderRow(panel, ref y, "Time", frame.time,
                            minimum, maximum), minimum, maximum);
                    if (!Mathf.Approximately(previousTime, frame.time))
                        preview = frame.time;
                }
                else
                {
                    Widgets.Label(new Rect(panel.x, y, panel.width, 24f),
                        "Time: " + frame.time.ToString("P0") + " (no room)");
                    y += 35f;
                }
            }
            else
            {
                Widgets.Label(new Rect(panel.x, y, panel.width, 24f),
                    "Time: " + frame.time.ToString("P0") + " (endpoint)");
                y += 35f;
            }
            frame.x = SliderRow(panel, ref y, "X offset", frame.x, -1.2f, 1.2f);
            frame.z = SliderRow(panel, ref y, "Z offset", frame.z, -1.2f, 1.2f);
            frame.angle = SliderRow(panel, ref y, "Angle", frame.angle, -180f, 180f);
            if (editingSpare)
            {
                if (selectedFrame == 0 || selectedFrame == frames.Count - 1)
                {
                    frame.travel = selectedFrame == 0 ? 0f : 1f;
                    Widgets.Label(new Rect(panel.x, y, panel.width, 24f),
                        selectedFrame == 0 ? "At front holder" : "Seated in magwell");
                    y += 35f;
                }
                else
                    frame.travel = SliderRow(panel, ref y, "To magwell", frame.travel, 0f, 1f);
            }

            float buttonY = bounds.yMax - 31f;
            if (Widgets.ButtonText(new Rect(bounds.x, buttonY, 100f, 27f), "Add frame")
                && frames.Count < 9)
            {
                int next = Mathf.Min(selectedFrame + 1, frames.Count - 1);
                float time = (frames[selectedFrame].time + frames[next].time) * 0.5f;
                if (next == selectedFrame)
                    time = (frames[selectedFrame - 1].time + frames[selectedFrame].time) * 0.5f;
                if (time - frames[next - 1].time >= 0.01f
                    && frames[next].time - time >= 0.01f)
                {
                    FluxMagazineKeyframe added = FluxRaiderReloadTimeline.Sample(frames, time);
                    frames.Insert(next, added);
                    selectedFrame = next;
                    preview = time;
                }
            }
            if (Widgets.ButtonText(new Rect(bounds.x + 106f, buttonY, 115f, 27f),
                "Delete frame") && selectedFrame > 0 && selectedFrame < frames.Count - 1)
            {
                frames.RemoveAt(selectedFrame);
                selectedFrame = Mathf.Min(selectedFrame, frames.Count - 1);
            }
            if (Widgets.ButtonText(new Rect(bounds.x + 227f, buttonY, 88f, 27f), "Reset all"))
            {
                FluxRaiderReloadTimeline.Settings.Reset();
                selectedFrame = 0;
                preview = 0f;
            }
            if (Widgets.ButtonText(new Rect(bounds.xMax - 186f, buttonY, 88f, 27f), "Play"))
            {
                FluxRaiderCinematic.ClearReloadPreview(pawn);
                FluxRaiderCinematic.StartReload(pawn);
                Close();
            }
            if (Widgets.ButtonText(new Rect(bounds.xMax - 92f, buttonY, 88f, 27f), "Close"))
                Close();
        }

        private static float SliderRow(Rect bounds, ref float y, string label,
            float value, float minimum, float maximum)
        {
            Widgets.Label(new Rect(bounds.x, y, 100f, 24f), label);
            value = Widgets.HorizontalSlider(
                new Rect(bounds.x + 100f, y + 3f, bounds.width - 170f, 18f),
                value, minimum, maximum, true);
            value = Mathf.Round(value * 100f) / 100f;
            Widgets.Label(new Rect(bounds.xMax - 66f, y, 66f, 24f),
                value.ToString("0.00"));
            y += 35f;
            return value;
        }
    }
}
