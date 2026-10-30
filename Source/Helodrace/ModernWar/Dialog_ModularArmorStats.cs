using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    // Conversions and part modifiers compose into an affine effect, in combat order.
    // Sampling the real conversion path also handles factors that reduce a stat to zero.
    internal struct ModularArmorStatEffect
    {
        public float Offset, Factor;

        public static ModularArmorStatEffect Resolve(CompModularArmor armor, StatDef stat)
        {
            float zero = 0f, one = 1f;
            armor.ApplyStatConversions(stat, ref zero);
            armor.ApplyStatConversions(stat, ref one);
            return new ModularArmorStatEffect { Offset = zero, Factor = one - zero };
        }
    }

    public sealed partial class Dialog_ModularArmor
    {
        private sealed class ArmorStatRow
        {
            public StatDef Stat;
            public float Value, Neutral, Scale;
            public bool Factor, Raw;
            public string Key => Stat.defName + (Factor ? ":factor" : ":offset");
            public string FormatValue(double value) => Factor
                ? "×" + value.ToString("0.###")
                : Stat.Worker.ValueToString((float)value, false,
                    Raw ? ToStringNumberSense.Absolute : ToStringNumberSense.Offset);
        }

        private readonly Dictionary<string, ModularWeaponStatMotion> statMotions =
            new Dictionary<string, ModularWeaponStatMotion>();
        private Vector2 statsScroll;
        private bool showRawStats;

        private List<ArmorStatRow> ArmorStatRows()
        {
            var rows = new List<ArmorStatRow>();
            if (showRawStats)
            {
                foreach (ModularArmorMetric metric in Enum.GetValues(typeof(ModularArmorMetric)))
                {
                    StatDef stat = DefDatabase<StatDef>.GetNamed("HD_Armor" + metric);
                    rows.Add(new ArmorStatRow { Stat = stat, Value = comp.MetricFor(metric),
                        Scale = 10f, Raw = true });
                }
                return rows;
            }

            IEnumerable<StatDef> conversions = comp.Props.statConversions?
                .Where(entry => entry?.targetStat != null).Select(entry => entry.targetStat)
                ?? Enumerable.Empty<StatDef>();
            IEnumerable<StatDef> modifiers = comp.InstalledParts
                .Where(record => record?.part?.statModifiers != null)
                .SelectMany(record => record.part.statModifiers)
                .Where(entry => entry?.stat != null).Select(entry => entry.stat);
            foreach (StatDef stat in conversions.Concat(modifiers).Distinct())
            {
                ModularArmorStatEffect effect = ModularArmorStatEffect.Resolve(comp, stat);
                float scale = stat == StatDefOf.MoveSpeed ? 0.5f : 10f;
                rows.Add(new ArmorStatRow { Stat = stat, Value = effect.Offset, Scale = scale });
                if (!Mathf.Approximately(effect.Factor, 1f))
                    rows.Add(new ArmorStatRow { Stat = stat, Value = effect.Factor,
                        Neutral = 1f, Scale = 1f, Factor = true });
            }
            return rows;
        }

        private void DrawArmorStatsPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 26f),
                "HD_ModularArmor_StatsTitle".Translate());
            Rect modeButton = new Rect(inner.x, inner.y + 32f, inner.width, 28f);
            if (Widgets.ButtonText(modeButton, (showRawStats
                ? "HD_ModularArmor_StatsModeRaw" : "HD_ModularArmor_StatsModeConverted").Translate()))
            {
                showRawStats = !showRawStats;
                statsScroll = Vector2.zero;
                statMotions.Clear();
            }
            Text.Font = GameFont.Tiny;
            string hint = (showRawStats ? "HD_ModularArmor_RawStatsHint"
                : "HD_ModularArmor_ConvertedStatsHint").Translate();
            float hintHeight = Text.CalcHeight(hint, inner.width);
            Widgets.Label(new Rect(inner.x, modeButton.yMax + 7f, inner.width, hintHeight), hint);
            float top = modeButton.yMax + hintHeight + 15f;
            Rect outRect = new Rect(inner.x, top, inner.width, Mathf.Max(1f, inner.yMax - top));
            List<ArmorStatRow> rows = ArmorStatRows();
            const float rowHeight = 76f;
            Rect view = new Rect(0f, 0f, outRect.width - 18f, Mathf.Max(50f, rows.Count * rowHeight));
            Widgets.BeginScrollView(outRect, ref statsScroll, view);
            double now = Waylith.Time.RealtimeSinceStartupAsDouble;
            for (int i = 0; i < rows.Count; i++)
            {
                ArmorStatRow row = rows[i];
                if (!statMotions.TryGetValue(row.Key, out ModularWeaponStatMotion motion))
                {
                    motion = new ModularWeaponStatMotion();
                    statMotions.Add(row.Key, motion);
                }
                motion.Retarget(row.Value, now);
                double current = motion.ValueAt(now);
                double delta = motion.Target - motion.PreviousTarget;
                bool changed = now - motion.ChangedAt < 1.8 && Math.Abs(delta) > 0.00001;
                Color color = changed ? new Color(0.55f, 0.88f, 0.95f)
                    : new Color(0.35f, 0.72f, 0.8f);
                Rect card = new Rect(0f, i * rowHeight, view.width, rowHeight - 6f);
                Widgets.DrawBoxSolidWithOutline(card, new Color(0.075f, 0.085f, 0.09f),
                    new Color(0.30f, 0.36f, 0.38f), 1);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(card.x + 7f, card.y + 4f, card.width - 14f, 22f),
                    row.Stat.LabelCap);
                Text.Font = GameFont.Small;
                GUI.color = changed ? color : Color.white;
                Widgets.Label(new Rect(card.x + 7f, card.y + 25f, card.width - 14f, 26f),
                    row.FormatValue(current) + (changed ? delta > 0 ? " ↑" : " ↓" : string.Empty));
                GUI.color = Color.white;
                Rect track = new Rect(card.x + 7f, card.yMax - 14f, card.width - 14f, 7f);
                Widgets.DrawBoxSolid(track, new Color(0.035f, 0.045f, 0.05f));
                // Signed effects extend to either side of a neutral center marker.
                float scale = Mathf.Max(row.Scale, Mathf.Abs(row.Value - row.Neutral),
                    Mathf.Abs((float)motion.PreviousTarget - row.Neutral));
                float neutral = row.Raw ? 0f : 0.5f;
                float fill = row.Raw ? Mathf.Clamp01((float)current / scale)
                    : Mathf.Clamp01(0.5f + ((float)current - row.Neutral) / (2f * scale));
                Widgets.DrawBoxSolid(new Rect(track.x + track.width * Mathf.Min(neutral, fill),
                    track.y, track.width * Mathf.Abs(fill - neutral), track.height), color);
                Widgets.DrawBoxSolid(new Rect(track.x + track.width * neutral,
                    track.y - 1f, 1f, track.height + 2f), Color.white);
                string tip = row.Stat.LabelCap + ": " + row.FormatValue(row.Value);
                if (changed)
                    tip += "\n" + "HD_ModularArmor_StatChange".Translate(
                        row.FormatValue(motion.PreviousTarget), row.FormatValue(row.Value));
                TooltipHandler.TipRegion(card, tip);
            }
            if (rows.Count == 0)
                Widgets.Label(new Rect(0f, 0f, view.width, 50f), "HD_ModularArmor_NoConversions".Translate());
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
        }
    }
}
