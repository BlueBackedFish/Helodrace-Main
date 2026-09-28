using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    public class HediffCompProperties_Dazzle : HediffCompProperties
    {
        // Ticks without any light before the effect starts dropping. Two ticks of slack
        // absorbs the fact that the lighting pawn and the victim tick in different orders.
        public int graceTicks = 3;

        // Severity lost per tick once unlit. The debuff is meant to vanish almost the moment
        // the beam leaves, not linger like an injury.
        public float fadePerTick = 0.15f;

        public HediffCompProperties_Dazzle()
        {
            compClass = typeof(HediffComp_Dazzle);
        }
    }

    // Severity IS the number of lights currently on this pawn.
    //
    // Rather than ramping up while a beam is held, each light registers itself every tick and
    // severity is set to that count directly - so one light is instantly stage 1, three lights
    // are instantly stage 3, and everything drops off the moment the beams move away. Stages
    // in the HediffDef then read as "lit by N lights" instead of "has been lit for N seconds".
    public class HediffComp_Dazzle : HediffComp
    {
        private int lastLitTick = -9999;
        private int litTick = -9999;
        private float accumulated;

        public HediffCompProperties_Dazzle Props => (HediffCompProperties_Dazzle)props;

        // Called once per light, per tick, by Patch_EquipmentTrackerTick_Flashlight.
        public void Notify_Lit(float intensity)
        {
            int now = Find.TickManager.TicksGame;

            // First registration of this tick starts a fresh count; later ones stack onto it.
            if (litTick != now)
            {
                litTick = now;
                accumulated = 0f;
            }

            accumulated += intensity;
            lastLitTick = now;

            // Applied immediately so the penalty lands on the same tick the beam does.
            parent.Severity = Mathf.Min(accumulated, parent.def.maxSeverity);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            int sinceLit = Find.TickManager.TicksGame - lastLitTick;
            if (sinceLit <= Props.graceTicks) return;

            severityAdjustment -= Props.fadePerTick;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref lastLitTick, "lastLitTick", -9999);
        }

        public override string CompDebugString()
        {
            return "lights this tick: " + accumulated.ToString("0.##")
                 + "\nlast lit: " + (Find.TickManager.TicksGame - lastLitTick) + " ticks ago";
        }
    }
}
