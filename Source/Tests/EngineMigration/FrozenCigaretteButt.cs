// Frozen game-vector body from 336e112a; scalar Mathf resolves to the same native API.
using UnityEngine;
using RimWorld;
using Verse;

namespace Helodrace.Tests.Frozen
{
    public class Thing_CigaretteButt : Filth
    {
        public string texturePath;
        public string fireTexturePath;

        private Vector3 startDrawOffset;
        private int flightTicksLeft;
        private int flightTicksTotal;
        private float arcHeight;
        private float spinRate;
        private float angle;
        private int emberTicksLeft;
        private int emberTicksTotal;

        public override Graphic Graphic => GraphicDatabase.Get<Graphic_Single>(
            texturePath.NullOrEmpty()
                ? "Items/Cigarette/OnHand/HD_Cigarette131"
                : texturePath,
            ShaderDatabase.Cutout,
            new Vector2(0.385f, 0.385f),
            Color.white);

        public void BeginFlight(Vector3 origin, int duration, float height, float degreesPerTick)
        {
            startDrawOffset = origin - Position.ToVector3Shifted();
            startDrawOffset.y = 0f;
            flightTicksLeft = flightTicksTotal = Mathf.Max(1, duration);
            arcHeight = height;
            spinRate = degreesPerTick;
            angle = 0f;
        }

        public void BeginEmber(int duration)
        {
            emberTicksLeft = emberTicksTotal = Mathf.Max(0, duration);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref texturePath, "cigaretteTexturePath");
            Scribe_Values.Look(ref fireTexturePath, "cigaretteFireTexturePath");
            Scribe_Values.Look(ref startDrawOffset, "startDrawOffset");
            Scribe_Values.Look(ref flightTicksLeft, "flightTicksLeft");
            Scribe_Values.Look(ref flightTicksTotal, "flightTicksTotal");
            Scribe_Values.Look(ref arcHeight, "arcHeight");
            Scribe_Values.Look(ref spinRate, "spinRate");
            Scribe_Values.Look(ref angle, "angle");
            Scribe_Values.Look(ref emberTicksLeft, "emberTicksLeft");
            Scribe_Values.Look(ref emberTicksTotal, "emberTicksTotal");
        }

        protected override void Tick()
        {
            base.Tick();
            if (flightTicksLeft > 0)
            {
                flightTicksLeft--;
                angle += spinRate;
            }
            if (emberTicksLeft > 0)
            {
                emberTicksLeft--;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float progress = flightTicksTotal > 0
                ? 1f - flightTicksLeft / (float)flightTicksTotal
                : 1f;
            if (flightTicksLeft > 0)
            {
                drawLoc += Vector3.Lerp(startDrawOffset, Vector3.zero, progress);
                drawLoc.y = AltitudeLayer.MoteOverhead.AltitudeFor()
                    + Mathf.Sin(progress * Mathf.PI) * arcHeight;
            }
            Graphic.Draw(drawLoc, Rot4.North, this, angle);
            if (emberTicksLeft > 0 && !fireTexturePath.NullOrEmpty())
            {
                float life = emberTicksTotal > 0
                    ? emberTicksLeft / (float)emberTicksTotal
                    : 0f;
                // Ten fixed levels produce a smooth-looking fade while keeping
                // the number of cached glow materials strictly bounded.
                float fadeStep = Mathf.Ceil(life * 10f) / 10f;
                float intensity = Mathf.Lerp(0.08f, 0.85f, fadeStep);
                Graphic emberGraphic = GraphicDatabase.Get<Graphic_Single>(
                    fireTexturePath, ShaderDatabase.MoteGlow,
                    new Vector2(0.385f, 0.385f),
                    new Color(intensity, intensity * 0.82f,
                        intensity * 0.48f, intensity));
                Vector3 emberLoc = drawLoc;
                emberLoc.y += 0.001f;
                emberGraphic.Draw(emberLoc, Rot4.North, this, angle);
            }
        }
    }

}
