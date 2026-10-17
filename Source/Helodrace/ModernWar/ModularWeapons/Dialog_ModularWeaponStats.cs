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
            public float Value, Maximum;
            public bool LowerIsBetter;
        }

        private readonly Dictionary<string, ModularWeaponStatMotion> statMotions =
            new Dictionary<string, ModularWeaponStatMotion>();
        private Vector2 statsScroll;

        private List<StatBar> WeaponStatBars()
        {
            ModularWeaponConvertedStats value = root.ConvertedStats;
            return new List<StatBar>
            {
                Bar("Mass", value.MassKg, 15f, "0.##' kg'", true),
                Bar("Ergonomics", value.Ergonomics, 1f, "P0"),
                Bar("RecoilControl", value.RecoilControl, 1f, "P0"),
                Bar("MuzzleControl", value.MuzzleControl, 1f, "P0"),
                Bar("Handling", value.Handling, 1f, "P0"),
                Bar("TargetAcquisition", value.TargetAcquisition, 1f, "P0"),
                Bar("AimingPrecision", value.AimingPrecision, 1f, "P0"),
                Bar("Reliability", value.OperatingReliability, 1f, "P1"),
                Bar("IdentificationDistance", value.IdentificationDistanceCells, 80f,
                    "0.#' " + "HD_ModularWeapon_Cells".Translate().ToString() + "'", false,
                    "HD_ModularWeapon_IdentificationDistance".Translate()),
                Bar("RoundsPerMinute", root.EffectiveRoundsPerMinute, 1800f, "0.#' RPM'"),
                Bar("CycleDelay", root.EffectiveFireDelaySeconds, 4f, "0.###' s'", true),
                Bar("GasEfficiency", value.GasEfficiency, 1.35f, "P0")
            };
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
                if (bar.Format.StartsWith("P", StringComparison.Ordinal)) current = Math.Min(bar.Maximum, current);
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
                    current.ToString(bar.Format, CultureInfo.InvariantCulture));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                Rect track = new Rect(0f, y + 24f, view.width, 9f);
                Widgets.DrawBoxSolid(track, new Color(0.055f, 0.07f, 0.08f));
                Widgets.DrawBoxSolid(new Rect(track.x, track.y,
                    track.width * Mathf.Clamp01((float)current / bar.Maximum), track.height), color);
                float target = Mathf.Clamp01(bar.Value / bar.Maximum);
                Widgets.DrawBoxSolid(new Rect(Mathf.Clamp(track.x + track.width * target - 1f,
                    track.x, track.xMax - 2f), track.y - 2f, 2f, track.height + 4f), Color.white);
                string tip = bar.Label + ": " + bar.Value.ToString(bar.Format, CultureInfo.InvariantCulture);
                if (changed)
                    tip += "\n" + "HD_ModularWeapon_StatChange".Translate(
                        motion.PreviousTarget.ToString(bar.Format, CultureInfo.InvariantCulture),
                        bar.Value.ToString(bar.Format, CultureInfo.InvariantCulture));
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
            text += "\n" + StatDefOf.RangedWeapon_Cooldown.LabelCap + ": "
                + weapon.GetStatValue(StatDefOf.RangedWeapon_Cooldown).ToString("0.###", CultureInfo.InvariantCulture) + " s";
            text += "\n" + StatDefOf.RangedWeapon_WarmupMultiplier.LabelCap + ": ×"
                + weapon.GetStatValue(StatDefOf.RangedWeapon_WarmupMultiplier).ToString("0.###", CultureInfo.InvariantCulture);
            VerbProperties verb = weapon.def.Verbs?.FirstOrDefault(v => v.isPrimary) ?? weapon.def.Verbs?.FirstOrDefault();
            if (verb != null) text += "\n" + "HD_ModularWeapon_BurstShots".Translate(root.ApplyBurstShotCount(verb.burstShotCount));
            if (CombatExtendedActive)
            {
                foreach (string name in new[] { "SightsEfficiency", "ShotSpread", "SwayFactor", "Recoil" })
                {
                    StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(name);
                    if (stat != null) text += "\n" + stat.LabelCap + ": "
                        + weapon.GetStatValue(stat).ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
            else
                foreach (StatDef stat in new[] { StatDefOf.AccuracyShort, StatDefOf.AccuracyMedium, StatDefOf.AccuracyLong })
                    text += "\n" + stat.LabelCap + ": " + weapon.GetStatValue(stat).ToString("P0", CultureInfo.InvariantCulture);
            return text;
        }
    }
}
