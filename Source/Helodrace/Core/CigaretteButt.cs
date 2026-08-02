using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public enum CigaretteButtHabit
    {
        Throw,
        Flick,
        Drop,
        Pocket
    }

    public static class CigaretteButtUtility
    {
        private const string ButtDefName = "HD_CigaretteButt";

        public static CigaretteButtHabit HabitFor(Pawn pawn)
        {
            uint mixed = unchecked((uint)(pawn?.thingIDNumber ?? 0) * 2246822519u + 3266489917u);
            mixed ^= mixed >> 15;
            mixed *= 3266489917u;
            mixed ^= mixed >> 16;
            return (CigaretteButtHabit)(mixed & 3u);
        }

        public static void DisposeButt(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            CigaretteButtHabit habit = HabitFor(pawn);
            if (habit == CigaretteButtHabit.Pocket)
            {
                // Pocketing leaves no inventory item and no cleanable filth.
                Log.Message("[HD Cigarette] butt: pawn=" + pawn.LabelShort
                    + ", habit=Pocket, no filth left");
                return;
            }

            ThingDef buttDef = DefDatabase<ThingDef>.GetNamedSilentFail(ButtDefName);
            if (buttDef == null)
            {
                Log.Error("[HD Cigarette] Missing ThingDef " + ButtDefName);
                return;
            }

            Thing_CigaretteButt butt = ThingMaker.MakeThing(buttDef) as Thing_CigaretteButt;
            if (butt == null)
            {
                return;
            }
            butt.texturePath = CigaretteSmokingUtility.TexturePathFor(pawn, 1f);
            butt.fireTexturePath = CigaretteSmokingUtility.FireTexturePathFor(pawn, 1f);

            if (pawn.Map == null || !pawn.Spawned)
            {
                butt.Destroy();
                return;
            }

            IntVec3 landingCell = LandingCellFor(pawn, habit, out int flightDistance);
            GenSpawn.Spawn(butt, landingCell, pawn.Map);

            if (habit == CigaretteButtHabit.Throw)
            {
                int duration = Mathf.Max(12, Mathf.RoundToInt(
                    34f * (flightDistance / 2f) * Rand.Range(0.85f, 1.15f)));
                butt.BeginFlight(pawn.DrawPos, duration,
                    Rand.Range(0.18f, 0.42f), Rand.Range(10f, 18f));
                butt.BeginEmber(Rand.RangeInclusive(300, 600));
            }
            else if (habit == CigaretteButtHabit.Flick)
            {
                float speedFactor = Rand.Range(1f, 2f);
                butt.BeginFlight(pawn.DrawPos,
                    Mathf.Max(12, Mathf.RoundToInt(
                        48f * (flightDistance / 4f) / speedFactor)),
                    Rand.Range(0.35f, 0.70f), 32f * speedFactor);
                butt.BeginEmber(Rand.RangeInclusive(300, 600));
            }

            Log.Message("[HD Cigarette] butt: pawn=" + pawn.LabelShort
                + ", habit=" + habit + ", landing=" + landingCell
                + ", texture=" + butt.texturePath);
        }

        private static IntVec3 LandingCellFor(Pawn pawn, CigaretteButtHabit habit,
            out int distance)
        {
            if (habit == CigaretteButtHabit.Drop || habit == CigaretteButtHabit.Pocket)
            {
                distance = 0;
                return pawn.Position;
            }

            distance = habit == CigaretteButtHabit.Flick
                ? Rand.RangeInclusive(3, 5)
                : Rand.RangeInclusive(1, 3);
            int lateral = Rand.RangeInclusive(-1, 1);
            IntVec3 target = pawn.Position
                + pawn.Rotation.FacingCell * distance
                + pawn.Rotation.RighthandCell * lateral;
            return target.InBounds(pawn.Map) ? target : pawn.Position;
        }
    }

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

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class Patch_CigaretteButtOnIngested
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, Pawn ingester)
        {
            if (__instance?.def?.defName == CigaretteSmokingUtility.CigaretteDefName)
            {
                CigaretteButtUtility.DisposeButt(ingester);
            }
        }
    }
}
