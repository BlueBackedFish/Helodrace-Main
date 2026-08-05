using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class Hediff_FlashbangSuppression : HediffWithComps
    {
        private const string SuppressionLightDefName = "HD_SuppressionLight";
        private const int VisualIntervalTicks = 24;
        private static FleckDef suppressionLightDef;
        private int ticksUntilVisual;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref ticksUntilVisual,
                "ticksUntilSuppressionVisual",
                0);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            ticksUntilVisual -= delta;
            if (ticksUntilVisual > 0
                || pawn == null
                || pawn.Dead
                || !pawn.Spawned
                || pawn.Map == null
                || Severity <= 0.001f)
            {
                return;
            }

            ticksUntilVisual = VisualIntervalTicks;
            Vector3 rearDirection = -pawn.Rotation.FacingCell.ToVector3();
            Vector3 position = pawn.DrawPos + rearDirection * 0.22f;
            float scale = Mathf.Lerp(0.28f, 0.55f, Mathf.Clamp01(Severity));
            FleckDef lightDef = suppressionLightDef
                ?? (suppressionLightDef = DefDatabase<FleckDef>.GetNamedSilentFail(
                    SuppressionLightDefName));
            if (lightDef == null)
            {
                return;
            }

            FleckMaker.Static(
                position,
                pawn.Map,
                lightDef,
                scale);
        }
    }
}
