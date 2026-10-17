using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularWeapon
    {
        private sealed class StatBar
        {
            public string Key, Label, Format;
            public StatDef Stat;
            public float Value, Maximum;
            public bool LowerIsBetter;

            public string FormatValue(double value) => Stat != null
                ? Stat.Worker.ValueToString((float)value, false, ToStringNumberSense.Absolute)
                : value.ToString(Format, CultureInfo.InvariantCulture);
        }

        private readonly Dictionary<string, ModularWeaponStatMotion> statMotions =
            new Dictionary<string, ModularWeaponStatMotion>();
        private Vector2 statsScroll;

        private List<StatBar> WeaponStatBars()
        {
            // Read the same final instance stats used by combat, after part factors
            // and compatibility patches. Authoring/control scores stay out of the UI.
            List<StatBar> bars = new List<StatBar>
            {
                FinalStatBar(StatDefOf.Mass, 15f, true),
                FinalStatBar(StatDefOf.RangedWeapon_WarmupMultiplier, 2f, true),
                FinalStatBar(StatDefOf.RangedWeapon_Cooldown, 4f, true),
                Bar("RoundsPerMinute", root.EffectiveRoundsPerMinute, 1800f, "0.#' RPM'"),
                Bar("CycleDelay", root.EffectiveFireDelaySeconds, 4f, "0.###' s'", true)
            };
            if (CombatExtendedActive)
            {
                AddFinalStatBar(bars, "Recoil", 8f, true);
                AddFinalStatBar(bars, "ShotSpread", 0.24f, true);
                AddFinalStatBar(bars, "SwayFactor", 1.8f, true);
                AddFinalStatBar(bars, "SightsEfficiency", 1.25f);
                AddFinalStatBar(bars, "Bulk", 30f, true);
            }
            else
            {
                foreach (StatDef stat in new[] { StatDefOf.AccuracyTouch, StatDefOf.AccuracyShort,
                    StatDefOf.AccuracyMedium, StatDefOf.AccuracyLong })
                    bars.Add(FinalStatBar(stat, 1f));
            }
            bars.Add(Bar("IdentificationDistance", root.ConvertedStats.IdentificationDistanceCells, 80f,
                "0.#' " + "HD_ModularWeapon_Cells".Translate().ToString() + "'", false,
                "HD_ModularWeapon_IdentificationDistance".Translate()));
            return bars;
        }

        private StatBar FinalStatBar(StatDef stat, float maximum, bool lowerIsBetter = false)
            => new StatBar { Key = stat.defName, Label = stat.LabelCap, Stat = stat,
                Value = root.parent.GetStatValue(stat), Maximum = maximum, LowerIsBetter = lowerIsBetter };

        private void AddFinalStatBar(List<StatBar> bars, string name, float maximum, bool lowerIsBetter = false)
        {
            StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(name);
            if (stat != null) bars.Add(FinalStatBar(stat, maximum, lowerIsBetter));
        }

        private static StatBar Bar(string key, float value, float maximum, string format,
            bool lowerIsBetter = false, string label = null)
            => new StatBar { Key = key, Label = label ?? ("HD_ModularWeapon_" + key).Translate().ToString(),
                Value = value, Maximum = maximum, Format = format, LowerIsBetter = lowerIsBetter };

        private void DrawWeaponStatsPanel(Rect rect, List<ModularRenderNode> snapshot)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 25f),
                "HD_ModularWeapon_StatsTitle".Translate());
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, inner.y + 26f, inner.width, 22f), root.parent.LabelCap);
            ModularWeaponFunctionStatus function = root.FunctionStatus;
            string status = function.CanFire
                ? "HD_ModularWeapon_FunctionOperational".Translate().ToString()
                : "HD_ModularWeapon_FunctionBlocked".Translate(function.MissingRequiredLabels).ToString();
            if (function.missingFunctionalParts.Count > 0)
                status += "\n" + "HD_ModularWeapon_FunctionDegraded".Translate(function.MissingFunctionalLabels);
            float statusHeight = Text.CalcHeight(status, inner.width);
            GUI.color = function.CanFire ? new Color(0.7f, 0.9f, 0.75f) : new Color(1f, 0.6f, 0.35f);
            Widgets.Label(new Rect(inner.x, inner.y + 49f, inner.width, statusHeight), status);
            GUI.color = Color.white;
            float top = 57f + statusHeight;
            DrawAmmunitionBadge(new Rect(inner.x, inner.y + top, inner.width, 63f), snapshot);
            top += 71f;
            Rect outRect = new Rect(inner.x, inner.y + top, inner.width, Mathf.Max(1f, inner.height - top));
            List<StatBar> bars = WeaponStatBars();
            const float rowHeight = 43f;
            string details = WeaponStatDetails();
            float detailHeight = Text.CalcHeight(details, outRect.width - 22f) + 10f;
            float gasHeight = root.HasAdjustableGasSystem ? 90f : 0f;
            Rect view = new Rect(0f, 0f, outRect.width - 18f,
                bars.Count * rowHeight + detailHeight + gasHeight + 8f);
            Widgets.BeginScrollView(outRect, ref statsScroll, view);
            double now = Time.realtimeSinceStartup;
            Text.Font = GameFont.Small;
            for (int i = 0; i < bars.Count; i++)
            {
                StatBar bar = bars[i];
                ModularWeaponStatMotion motion;
                if (!statMotions.TryGetValue(bar.Key, out motion))
                {
                    motion = new ModularWeaponStatMotion();
                    statMotions.Add(bar.Key, motion);
                }
                motion.Retarget(bar.Value, now);
                double current = Math.Max(0, motion.ValueAt(now));
                double delta = motion.Target - motion.PreviousTarget;
                bool changed = now - motion.ChangedAt < 1.8 && Math.Abs(delta) > 0.00001;
                bool improved = bar.LowerIsBetter ? delta < 0 : delta > 0;
                Color color = changed ? improved ? new Color(0.43f, 0.85f, 0.53f)
                    : new Color(1f, 0.53f, 0.38f) : new Color(0.35f, 0.72f, 0.8f);
                float y = i * rowHeight;
                Widgets.Label(new Rect(0f, y, view.width * 0.6f, 24f),
                    bar.Label + (changed ? delta > 0 ? " ↑" : " ↓" : string.Empty));
                Text.Anchor = TextAnchor.UpperRight;
                if (changed) GUI.color = color;
                Widgets.Label(new Rect(view.width * 0.58f, y, view.width * 0.42f, 24f),
                    bar.FormatValue(current));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                Rect track = new Rect(0f, y + 24f, view.width, 9f);
                Widgets.DrawBoxSolid(track, new Color(0.055f, 0.07f, 0.08f));
                Widgets.DrawBoxSolid(new Rect(track.x, track.y,
                    track.width * Mathf.Clamp01((float)current / bar.Maximum), track.height), color);
                float target = Mathf.Clamp01(bar.Value / bar.Maximum);
                Widgets.DrawBoxSolid(new Rect(Mathf.Clamp(track.x + track.width * target - 1f,
                    track.x, track.xMax - 2f), track.y - 2f, 2f, track.height + 4f), Color.white);
                string tip = bar.Label + ": " + bar.FormatValue(bar.Value);
                if (changed)
                    tip += "\n" + "HD_ModularWeapon_StatChange".Translate(
                        bar.FormatValue(motion.PreviousTarget), bar.FormatValue(bar.Value));
                TooltipHandler.TipRegion(new Rect(0f, y, view.width, rowHeight), tip);
            }
            float detailTop = bars.Count * rowHeight + 3f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, detailTop, view.width, detailHeight), details);
            if (root.HasAdjustableGasSystem)
                DrawGasTubeControl(new Rect(0f, detailTop + detailHeight, view.width, 82f));
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
        }

        private string WeaponStatDetails()
        {
            Thing weapon = root.parent;
            string text = "HD_ModularWeapon_Balance".Translate() + ": "
                + (root.ConvertedStats.CenterOfMassMeters * 100f)
                    .ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " cm";
            VerbProperties verb = weapon.def.Verbs?.FirstOrDefault(v => v.isPrimary) ?? weapon.def.Verbs?.FirstOrDefault();
            if (verb != null) text += "\n" + "HD_ModularWeapon_BurstShots".Translate(root.ApplyBurstShotCount(verb.burstShotCount));
            return text;
        }
    }
}
