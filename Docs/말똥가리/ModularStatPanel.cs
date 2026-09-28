using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Before/after stat readout for the customization window.
    //
    // Values are derived from the weapon's ACTUAL current stat (so quality, materials and
    // anything another mod contributes are included), then the comp's own contribution is
    // divided back out and the pending contribution applied. That keeps the "before" column
    // honest instead of recomputing a clean-room number that would not match the info card.
    public static class ModularStatPanel
    {
        private struct Row
        {
            public string label;
            public float current;
            public float pending;
            public bool lowerIsBetter;
            public string format;
            public string suffix;
        }

        private static readonly Dictionary<StatDef, float> CurOffsets = new Dictionary<StatDef, float>();
        private static readonly Dictionary<StatDef, float> CurFactors = new Dictionary<StatDef, float>();
        private static readonly Dictionary<StatDef, float> PendOffsets = new Dictionary<StatDef, float>();
        private static readonly Dictionary<StatDef, float> PendFactors = new Dictionary<StatDef, float>();

        public static void Draw(Rect rect, CompWeaponModular comp,
            Dictionary<WeaponPartSlotDef, WeaponPartDef> pending, List<WeaponPartSlotDef> pendingSlots)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);

            int curBurstOff, pendBurstOff;
            float curBurstFac, curBurstSpd, pendBurstFac, pendBurstSpd;

            comp.CurrentModifiers(CurOffsets, CurFactors,
                out curBurstOff, out curBurstFac, out curBurstSpd);
            comp.ComputeModifiers(pending, pendingSlots, PendOffsets, PendFactors,
                out pendBurstOff, out pendBurstFac, out pendBurstSpd);

            List<Row> rows = BuildRows(comp,
                curBurstOff, curBurstFac, curBurstSpd,
                pendBurstOff, pendBurstFac, pendBurstSpd);

            float y = inner.y;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, y, inner.width, 24f), comp.parent.LabelCap);
            y += 26f;

            Text.Font = GameFont.Tiny;
            foreach (Row row in rows)
            {
                DrawRow(new Rect(inner.x, y, inner.width, 20f), row);
                y += 20f;
            }
            Text.Font = GameFont.Small;
        }

        private static void DrawRow(Rect r, Row row)
        {
            Widgets.Label(new Rect(r.x, r.y, r.width * 0.58f, r.height), row.label);

            bool changed = !Mathf.Approximately(row.current, row.pending);
            string text = row.pending.ToString(row.format) + row.suffix;

            if (changed)
            {
                // Green when the change helps, red when it hurts - direction depends on the
                // stat, since a lower cooldown or lighter weapon is an improvement.
                bool better = row.lowerIsBetter
                    ? row.pending < row.current
                    : row.pending > row.current;

                GUI.color = better ? ColorLibrary.Green : ColorLibrary.RedReadable;

                float delta = row.pending - row.current;
                text += "  (" + (delta > 0f ? "+" : "") + delta.ToString(row.format) + ")";
            }

            Rect valueRect = new Rect(r.x + r.width * 0.58f, r.y, r.width * 0.42f, r.height);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static List<Row> BuildRows(CompWeaponModular comp,
            int curBurstOff, float curBurstFac, float curBurstSpd,
            int pendBurstOff, float pendBurstFac, float pendBurstSpd)
        {
            var rows = new List<Row>();
            Thing weapon = comp.parent;
            VerbProperties verb = PrimaryVerb(weapon.def);

            // --- range and handling ---
            if (verb != null)
            {
                float curRangeMul = Value(weapon, StatDefOf.RangedWeapon_RangeMultiplier, true);
                float pendRangeMul = Value(weapon, StatDefOf.RangedWeapon_RangeMultiplier, false);
                rows.Add(NewRow("LGMW_Stat_Range".Translate(),
                    verb.range * curRangeMul, verb.range * pendRangeMul, false, "F0"));

                float curWarmMul = Value(weapon, StatDefOf.RangedWeapon_WarmupMultiplier, true);
                float pendWarmMul = Value(weapon, StatDefOf.RangedWeapon_WarmupMultiplier, false);
                rows.Add(NewRow("LGMW_Stat_Warmup".Translate(),
                    verb.warmupTime * curWarmMul, verb.warmupTime * pendWarmMul, true, "F2", "s"));

                int curBurst = Mathf.Max(1,
                    Mathf.CeilToInt((verb.burstShotCount + curBurstOff) * curBurstFac));
                int pendBurst = Mathf.Max(1,
                    Mathf.CeilToInt((verb.burstShotCount + pendBurstOff) * pendBurstFac));
                rows.Add(NewRow("LGMW_Stat_Burst".Translate(), curBurst, pendBurst, false, "F0"));

                float curCadence = Mathf.Max(1f, verb.ticksBetweenBurstShots / Mathf.Max(0.01f, curBurstSpd));
                float pendCadence = Mathf.Max(1f, verb.ticksBetweenBurstShots / Mathf.Max(0.01f, pendBurstSpd));
                if (!Mathf.Approximately(curCadence, pendCadence))
                    rows.Add(NewRow("LGMW_Stat_Cadence".Translate(),
                        curCadence, pendCadence, true, "F0", "t"));
            }

            rows.Add(StatRow(weapon, StatDefOf.RangedWeapon_Cooldown,
                "LGMW_Stat_Cooldown".Translate(), true, "F2", "s"));

            // --- accuracy ---
            rows.Add(StatRow(weapon, StatDefOf.AccuracyTouch, "LGMW_Stat_AccTouch".Translate(), false, "P0"));
            rows.Add(StatRow(weapon, StatDefOf.AccuracyShort, "LGMW_Stat_AccShort".Translate(), false, "P0"));
            rows.Add(StatRow(weapon, StatDefOf.AccuracyMedium, "LGMW_Stat_AccMedium".Translate(), false, "P0"));
            rows.Add(StatRow(weapon, StatDefOf.AccuracyLong, "LGMW_Stat_AccLong".Translate(), false, "P0"));

            // --- carry ---
            rows.Add(StatRow(weapon, StatDefOf.Mass, "LGMW_Stat_Mass".Translate(), true, "F2", "kg"));

            return rows;
        }

        private static VerbProperties PrimaryVerb(ThingDef def)
        {
            if (def.Verbs.NullOrEmpty()) return null;
            foreach (VerbProperties v in def.Verbs)
                if (v.isPrimary) return v;
            return def.Verbs[0];
        }

        private static Row StatRow(Thing weapon, StatDef stat, string label,
            bool lowerIsBetter, string format, string suffix = "")
        {
            return NewRow(label, Value(weapon, stat, true), Value(weapon, stat, false),
                lowerIsBetter, format, suffix);
        }

        // current=true returns the live stat; current=false swaps this comp's contribution
        // for the pending one, leaving quality and every other source untouched.
        private static float Value(Thing weapon, StatDef stat, bool current)
        {
            float live = weapon.GetStatValue(stat);
            if (current) return live;

            float curOff = CurOffsets.TryGetValue(stat, out var co) ? co : 0f;
            float curFac = CurFactors.TryGetValue(stat, out var cf) ? cf : 1f;
            float pendOff = PendOffsets.TryGetValue(stat, out var po) ? po : 0f;
            float pendFac = PendFactors.TryGetValue(stat, out var pf) ? pf : 1f;

            if (Mathf.Approximately(curFac, 0f)) return live;

            float withoutComp = (live / curFac) - curOff;
            return (withoutComp + pendOff) * pendFac;
        }

        private static Row NewRow(string label, float current, float pending,
            bool lowerIsBetter, string format, string suffix = "")
        {
            return new Row
            {
                label = label,
                current = current,
                pending = pending,
                lowerIsBetter = lowerIsBetter,
                format = format,
                suffix = suffix
            };
        }
    }
}
