using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{

    public sealed class Hediff_CSGasExposure : HediffWithComps
    {
        private const int RecoveryDelayTicks = 150;
        private const float RecoveryPerTick = 0.00035f;

        private int lastExposureTick = -1;

        public override bool ShouldRemove => base.ShouldRemove || Severity <= 0f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastExposureTick, "lastCSExposureTick", -1);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (lastExposureTick < 0)
            {
                lastExposureTick = Find.TickManager.TicksGame;
                return;
            }

            if (Find.TickManager.TicksGame - lastExposureTick >= RecoveryDelayTicks)
            {
                Severity = Mathf.Max(0f, Severity - RecoveryPerTick * delta);
            }
        }

        public void AddDose(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            Severity = Mathf.Min(def.maxSeverity, Severity + amount);
            lastExposureTick = Find.TickManager.TicksGame;
        }
    }
}
