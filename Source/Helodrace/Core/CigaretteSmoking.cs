using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    /// <summary>
    /// Visual state for HD_Cigarette.  It is deliberately derived from the
    /// pawn id and the current ingest toil, so it remains stable across saves
    /// without adding another pawn component or save entry.
    /// </summary>
    public static class CigaretteSmokingUtility
    {
        public const string CigaretteDefName = "HD_Cigarette";

        private const float LightingFraction = 0.045f;
        private const string OnHandTextureRoot = "Items/Cigarette/OnHand/HD_Cigarette";
        private const string FireTextureRoot = "Items/Cigarette/OnHand/HD_CigaretteFire";

        private static readonly AccessTools.FieldRef<JobDriver, List<Toil>> ToilsRef =
            AccessTools.FieldRefAccess<JobDriver, List<Toil>>("toils");
        private static readonly AccessTools.FieldRef<JobDriver_Ingest, Toil> ChewingToilRef =
            AccessTools.FieldRefAccess<JobDriver_Ingest, Toil>("chewing");
        private static readonly Dictionary<int, int> LastDiagnosticTick = new Dictionary<int, int>();
        private static readonly Dictionary<int, string> LastTexturePath = new Dictionary<int, string>();
        private static readonly Dictionary<int, int> LastPuffStage = new Dictionary<int, int>();

        public enum SmokingHabit
        {
            Neat,
            DeepDrags,
            LongAsh,
            QuickPuffs,
            ContinuousDrag
        }

        public static SmokingHabit HabitFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return SmokingHabit.Neat;
            }

            // Do not use Rand here: the pawn must keep the same habit forever.
            uint mixed = unchecked((uint)pawn.thingIDNumber * 2654435761u);
            mixed ^= mixed >> 16;
            return (SmokingHabit)(mixed % 5u);
        }

        public static void LogPatchStatus()
        {
            MethodInfo target = AccessTools.Method(typeof(PawnRenderUtility),
                nameof(PawnRenderUtility.DrawCarriedThing));
            Patches patches = target == null ? null : Harmony.GetPatchInfo(target);
            bool active = patches?.Prefixes != null
                && patches.Prefixes.Any(patch => patch.owner == "YourName.Helodrace");
            Log.Message("[HD Cigarette] diagnostics enabled. DrawCarriedThing prefix active="
                + active + ", assembly=" + typeof(CigaretteSmokingUtility).Assembly.Location);
        }

        public static bool TryGetSmokingProgress(Thing cigarette, out Pawn pawn,
            out JobDriver_Ingest ingestDriver, out float progress)
        {
            pawn = null;
            ingestDriver = null;
            progress = 0f;

            if (cigarette?.def?.defName != CigaretteDefName
                || !(cigarette.ParentHolder is Pawn_CarryTracker carryTracker))
            {
                return false;
            }

            pawn = carryTracker.pawn;
            return TryGetSmokingProgress(pawn, cigarette, out ingestDriver, out progress);
        }

        public static bool TryGetSmokingProgress(Pawn pawn, Thing cigarette,
            out JobDriver_Ingest ingestDriver, out float progress)
        {
            ingestDriver = null;
            progress = 0f;

            if (pawn == null || cigarette?.def?.defName != CigaretteDefName
                || pawn.carryTracker?.CarriedThing != cigarette)
            {
                return false;
            }

            ingestDriver = pawn?.jobs?.curDriver as JobDriver_Ingest;
            if (ingestDriver == null)
            {
                return false;
            }

            Toil toil = CurrentToil(ingestDriver);
            // GainingNutritionNow is false for non-nutritious ingestibles such
            // as cigarettes, even while their chewing toil is running.
            if (toil == null || toil != ChewingToilRef(ingestDriver))
            {
                return false;
            }

            int duration = toil.defaultDuration;
            if (duration <= 0)
            {
                duration = Mathf.Max(1, cigarette.def.ingestible?.baseIngestTicks ?? 1);
            }
            if (duration <= 0)
            {
                return false;
            }

            progress = Mathf.Clamp01(1f - ingestDriver.ticksLeftThisToil / (float)duration);
            return true;
        }

        public static string TexturePathFor(Pawn pawn, float rawProgress)
        {
            if (rawProgress < LightingFraction)
            {
                return OnHandTextureRoot + "001";
            }

            // Texture naming describes two independent lengths:
            //   first two digits = total cigarette length (01 long -> 14 short)
            //   final digit      = ash length (1 short -> 5 long)
            // Therefore frames with the same sum have the same unburned length,
            // e.g. 035 == 071 and 024 == 042.
            DisplayedBurnStateFor(pawn, rawProgress,
                out int unburnedProgress, out int ashLength);

            // Growing ash consumes tobacco without shortening the whole stick.
            // Flicking the ash resets ashLength and advances totalLengthStage,
            // which makes the cigarette visibly shorter at that instant.
            int totalLengthStage = Mathf.Clamp(unburnedProgress - ashLength, 1, 13);

            return OnHandTextureRoot + totalLengthStage.ToString("00") + ashLength;
        }

        public static string FireTexturePathFor(Pawn pawn, float rawProgress)
        {
            if (rawProgress < LightingFraction)
            {
                return null;
            }
            DisplayedBurnStateFor(pawn, rawProgress,
                out int unburnedProgress, out int _);
            return FireTextureRoot + unburnedProgress.ToString("00");
        }

        public static Color EmberColorFor(Pawn pawn, float rawProgress)
        {
            if (rawProgress < LightingFraction)
            {
                return Color.clear;
            }

            SmokingHabit habit = HabitFor(pawn);
            float inhaleStrength;
            if (habit == SmokingHabit.ContinuousDrag)
            {
                inhaleStrength = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(LightingFraction,
                        LightingFraction + 0.06f, rawProgress));
            }
            else
            {
                BurnTimelineFor(pawn, rawProgress, out int _, out float stagePhase);
                float inhaleEnd = habit == SmokingHabit.DeepDrags ? 0.68f : 0.55f;
                float rise = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.08f, 0.20f, stagePhase));
                float fall = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(inhaleEnd - 0.15f, inhaleEnd, stagePhase));
                inhaleStrength = Mathf.Min(rise, fall);
            }

            // Quantize the smooth curve to 16 reusable materials. This remains
            // visually gradual without creating a new cached material per tick.
            float level = Mathf.Round(inhaleStrength * 16f) / 16f;
            return new Color(
                Mathf.Lerp(0.42f, 1f, level),
                Mathf.Lerp(0.20f, 0.94f, level),
                Mathf.Lerp(0.07f, 0.72f, level),
                Mathf.Lerp(0.20f, 1f, level));
        }

        public static void LogRejectedRender(Pawn pawn, Thing cigarette)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (LastDiagnosticTick.TryGetValue(pawnId, out int lastTick)
                && tick - lastTick < 120)
            {
                return;
            }
            LastDiagnosticTick[pawnId] = tick;

            JobDriver driver = pawn?.jobs?.curDriver;
            JobDriver_Ingest ingest = driver as JobDriver_Ingest;
            Toil current = ingest == null ? null : CurrentToil(ingest);
            Toil chewing = ingest == null ? null : ChewingToilRef(ingest);
            Log.Message("[HD Cigarette] render rejected: tick=" + tick
                + ", pawn=" + (pawn?.LabelShort ?? "null") + "#" + pawnId
                + ", carried=" + (pawn?.carryTracker?.CarriedThing?.def?.defName ?? "null")
                + ", argument=" + (cigarette?.def?.defName ?? "null")
                + ", driver=" + (driver?.GetType().FullName ?? "null")
                + ", toilIndex=" + (driver?.CurToilIndex.ToString() ?? "null")
                + ", current=" + DescribeToil(current)
                + ", chewing=" + DescribeToil(chewing)
                + ", sameToil=" + (current != null && current == chewing)
                + ", ticksLeft=" + (driver?.ticksLeftThisToil.ToString() ?? "null"));
        }

        public static void LogSelectedFrame(Pawn pawn, JobDriver_Ingest driver,
            float progress, string texturePath, string fireTexturePath)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (LastTexturePath.TryGetValue(pawnId, out string previous)
                && previous == texturePath)
            {
                return;
            }
            LastTexturePath[pawnId] = texturePath;
            Log.Message("[HD Cigarette] frame: tick=" + (Find.TickManager?.TicksGame ?? 0)
                + ", pawn=" + (pawn?.LabelShort ?? "null") + "#" + pawnId
                + ", habit=" + HabitFor(pawn)
                + ", progress=" + progress.ToString("0.000")
                + ", ticksLeft=" + driver.ticksLeftThisToil
                + ", texture=" + texturePath
                + ", fire=" + (fireTexturePath ?? "none"));
        }

        private static string DescribeToil(Toil toil)
        {
            return toil == null ? "null"
                : "{" + (toil.debugName ?? "unnamed")
                    + ",duration=" + toil.defaultDuration
                    + ",mode=" + toil.defaultCompleteMode + "}";
        }

        private static void BurnTimelineFor(Pawn pawn, float rawProgress,
            out int stage, out float stagePhase)
        {
            float burnProgress = Mathf.InverseLerp(LightingFraction, 1f, rawProgress);
            SmokingHabit habit = HabitFor(pawn);
            switch (habit)
            {
                case SmokingHabit.DeepDrags:
                    burnProgress = Mathf.Pow(burnProgress, 0.86f);
                    break;
                case SmokingHabit.LongAsh:
                    burnProgress = Mathf.Pow(burnProgress, 1.12f);
                    break;
                case SmokingHabit.QuickPuffs:
                    burnProgress = Mathf.Pow(burnProgress, 0.94f);
                    break;
                case SmokingHabit.ContinuousDrag:
                    burnProgress = Mathf.Pow(burnProgress, 0.82f);
                    break;
            }

            int finalStage = FinalBurnStageFor(habit);
            float timeline = burnProgress * (finalStage - 1.001f);
            int wholeStage = Mathf.FloorToInt(timeline);
            stage = Mathf.Clamp(2 + wholeStage, 2, finalStage);
            stagePhase = Mathf.Clamp01(timeline - wholeStage);
        }

        private static void DisplayedBurnStateFor(Pawn pawn, float rawProgress,
            out int unburnedProgress, out int ashLength)
        {
            BurnTimelineFor(pawn, rawProgress, out int timelineStage, out float stagePhase);
            SmokingHabit habit = HabitFor(pawn);
            int ashCycle = AshCycleFor(habit);
            int cycleOffset = habit == SmokingHabit.QuickPuffs
                ? Mathf.Abs(pawn.thingIDNumber >> 2) % ashCycle
                : 0;

            int currentAsh = AshLengthForStage(habit, timelineStage, ashCycle, cycleOffset);
            int previousAsh = timelineStage > 2
                ? AshLengthForStage(habit, timelineStage - 1, ashCycle, cycleOffset)
                : currentAsh;
            bool ashFlick = habit != SmokingHabit.ContinuousDrag
                && timelineStage > 2 && currentAsh == 1 && previousAsh == ashCycle;
            float transitionPhase = ashFlick ? 0.62f : 0.22f;

            if (timelineStage > 2 && stagePhase < transitionPhase)
            {
                unburnedProgress = timelineStage - 1;
                ashLength = previousAsh;
            }
            else
            {
                unburnedProgress = timelineStage;
                ashLength = currentAsh;
            }
        }

        private static int AshLengthForStage(SmokingHabit habit, int stage,
            int ashCycle, int cycleOffset)
        {
            int ash = habit == SmokingHabit.ContinuousDrag
                ? Mathf.Min(5, stage - 1)
                : 1 + ((stage - 2 + cycleOffset) % ashCycle);
            return Mathf.Min(ash, stage - 1);
        }

        private static int FinalBurnStageFor(SmokingHabit habit)
        {
            switch (habit)
            {
                case SmokingHabit.Neat: return 10;
                case SmokingHabit.LongAsh: return 12;
                case SmokingHabit.DeepDrags: return 13;
                default: return 14;
            }
        }

        public static void TickEffects(JobDriver_Ingest driver)
        {
            Pawn pawn = driver?.pawn;
            Thing cigarette = pawn?.carryTracker?.CarriedThing;
            if (!TryGetSmokingProgress(pawn, cigarette,
                    out JobDriver_Ingest _, out float progress))
            {
                return;
            }

            Pawn smokingPawn = pawn;

            Toil toil = CurrentToil(driver);
            if (toil == null)
            {
                return;
            }
            int elapsed = Mathf.Max(0, toil.defaultDuration - driver.ticksLeftThisToil);
            SmokingHabit habit = HabitFor(smokingPawn);
            bool shouldExhale = false;
            if (habit == SmokingHabit.ContinuousDrag)
            {
                shouldExhale = progress >= LightingFraction && elapsed % 42 == 24;
            }
            else if (progress >= LightingFraction)
            {
                BurnTimelineFor(smokingPawn, progress, out int stage, out float stagePhase);
                int pawnId = smokingPawn.thingIDNumber;
                if (stagePhase >= 0.18f
                    && (!LastPuffStage.TryGetValue(pawnId, out int lastStage)
                        || lastStage != stage))
                {
                    LastPuffStage[pawnId] = stage;
                    shouldExhale = true;
                }
            }

            if (shouldExhale)
            {
                float size = habit == SmokingHabit.DeepDrags ? 0.32f
                    : habit == SmokingHabit.ContinuousDrag ? 0.22f
                    : habit == SmokingHabit.QuickPuffs ? 0.20f
                    : habit == SmokingHabit.LongAsh ? 0.24f
                    : 0.16f;
                ThrowDirectionalSmoke(smokingPawn, size);
            }
        }

        private static void ThrowDirectionalSmoke(Pawn pawn, float size)
        {
            Vector3 direction = pawn.Rotation.FacingCell.ToVector3();
            Vector3 spawnPosition = EffectPosition(pawn, 0.24f);
            FleckCreationData data = new FleckCreationData
            {
                def = FleckDefOf.Smoke,
                spawnPosition = spawnPosition,
                scale = size,
                rotation = Rand.Range(0f, 360f),
                rotationRate = Rand.Range(-18f, 18f),
                velocity = direction * Rand.Range(0.28f, 0.42f)
            };
            pawn.Map.flecks.CreateFleck(data);
        }

        private static int AshCycleFor(SmokingHabit habit)
        {
            switch (habit)
            {
                case SmokingHabit.Neat: return 2;
                case SmokingHabit.DeepDrags: return 3;
                case SmokingHabit.LongAsh: return 5;
                case SmokingHabit.ContinuousDrag: return 5;
                default: return 4;
            }
        }

        private static Toil CurrentToil(JobDriver driver)
        {
            List<Toil> toils = ToilsRef(driver);
            int index = driver.CurToilIndex;
            return toils != null && index >= 0 && index < toils.Count ? toils[index] : null;
        }

        private static Vector3 EffectPosition(Pawn pawn, float forwardDistance)
        {
            Vector3 position = pawn.DrawPos;
            switch (pawn.Rotation.AsInt)
            {
                case 0: position.z += forwardDistance; break; // north
                case 1: position.x += forwardDistance; break; // east
                case 2: position.z -= forwardDistance * 0.35f; break; // south
                case 3: position.x -= forwardDistance; break; // west
            }
            position.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            return position;
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawCarriedThing))]
    public static class Patch_CigaretteCarriedDrawing
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, ref Vector3 drawLoc, Thing carriedThing)
        {
            bool isCigarette = carriedThing?.def?.defName
                == CigaretteSmokingUtility.CigaretteDefName;
            if (!CigaretteSmokingUtility.TryGetSmokingProgress(pawn, carriedThing,
                    out JobDriver_Ingest driver, out float progress))
            {
                if (isCigarette)
                {
                    CigaretteSmokingUtility.LogRejectedRender(pawn, carriedThing);
                }
                return true;
            }

            PawnRenderUtility.CalculateCarriedDrawPos(pawn, carriedThing, ref drawLoc, out bool flip);
            Vector2 drawSize = carriedThing.def.graphicData?.drawSize ?? new Vector2(0.385f, 0.385f);
            string texturePath = CigaretteSmokingUtility.TexturePathFor(pawn, progress);
            string fireTexturePath = CigaretteSmokingUtility.FireTexturePathFor(pawn, progress);
            CigaretteSmokingUtility.LogSelectedFrame(pawn, driver, progress,
                texturePath, fireTexturePath);
            Graphic graphic = GraphicDatabase.Get<Graphic_Single>(
                texturePath,
                ShaderDatabase.Cutout, drawSize, Color.white);
            Rot4 rotation = flip ? carriedThing.Rotation.Opposite : carriedThing.Rotation;
            graphic.Draw(drawLoc, rotation, carriedThing, 0f);

            if (!fireTexturePath.NullOrEmpty())
            {
                Color emberColor = CigaretteSmokingUtility.EmberColorFor(pawn, progress);
                Graphic emberGraphic = GraphicDatabase.Get<Graphic_Single>(
                    fireTexturePath, ShaderDatabase.MoteGlow, drawSize,
                    emberColor);
                Vector3 emberLoc = drawLoc;
                emberLoc.y += 0.001f;
                emberGraphic.Draw(emberLoc, rotation, carriedThing, 0f);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.DriverTick))]
    public static class Patch_CigaretteSmokingEffects
    {
        [HarmonyPostfix]
        public static void Postfix(JobDriver __instance)
        {
            if (__instance is JobDriver_Ingest ingestDriver)
            {
                CigaretteSmokingUtility.TickEffects(ingestDriver);
            }
        }
    }
}
