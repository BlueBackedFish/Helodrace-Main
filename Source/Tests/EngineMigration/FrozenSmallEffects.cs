using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace Helodrace.Tests.FrozenSmallEffects
{
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
public static class Patch_CigaretteCarriedDrawing
    {

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
public sealed class M79 : ThingComp
{
private bool loaded;private float casingAngle;private int casingDueTick=-1;
public bool Loaded=>loaded;
private static readonly HashSet<M79> pendingDelayedEffects=new();
public CompProperties_M79Launcher Props=>(CompProperties_M79Launcher)props;
public Pawn Wielder=>(parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;
public void NotifyShotFired(Verb verb)
        {
            loaded = false;
            Pawn wielder = Wielder;
            if (wielder == null)
            {
                return;
            }

            Vector3 direction = verb.CurrentTarget.CenterVector3 - wielder.DrawPos;
            casingAngle = direction.AngleFlat() + 90f + Rand.Range(-12f, 12f);
            casingDueTick = Find.TickManager.TicksGame + Mathf.Max(1, Props.casingDelayTicks);
            pendingDelayedEffects.Add(this);
        }
private void SpawnCasing()
        {
            Pawn wielder = Wielder;
            if (wielder?.Map == null || Props.casingMoteDef == null)
            {
                return;
            }

            MoteThrown casing = ThingMaker.MakeThing(Props.casingMoteDef) as MoteThrown;
            if (casing == null)
            {
                return;
            }

            Vector3 origin = wielder.DrawPos;
            casing.exactPosition = origin + new Vector3(0f, 0f, 0.18f);
            casing.exactRotation = Rand.Range(0f, 360f);
            casing.rotationRate = Rand.Range(-720f, 720f);
            casing.Scale = 0.8f;
            casing.SetVelocity(casingAngle, Rand.Range(1.5f, 2.2f));
            GenSpawn.Spawn(casing, origin.ToIntVec3(), wielder.Map);
        }
}
public class Steam : ThingComp
{
public CompProperties_SteamEngineSmoke Props=>(CompProperties_SteamEngineSmoke)props;
private void ThrowSmoke()
        {
            // Rotate the physical outlet first. Its apparent height always points
            // upward in the sprite, including East/West, rather than rotating sideways.
            Vector3 offset = Props.smokeOffset.RotatedBy(parent.Rotation.AsAngle);
            float elevation = 64f * Mathf.Deg2Rad;
            Vector3 loc = parent.DrawPos;
            loc.x += offset.x;
            loc.z += offset.z * Mathf.Sin(elevation) + offset.y * Mathf.Cos(elevation);
            FleckMaker.ThrowSmoke(loc, this.parent.Map, 1.5f);
        }
}
public class CompOilFieldDevelopment : ThingComp
    {
        public float currentProgressDays = 0f;
        public bool needsMaintenance = false;
        public bool developmentComplete = false;

        public CompProperties_OilFieldDevelopment Props => (CompProperties_OilFieldDevelopment)this.props;

        public bool IsPowered => OilMachineOperation.IsPowered(parent);
        public bool IsWorking => !developmentComplete && !needsMaintenance && IsPowered;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            IntVec3 targetPos = parent.Position + Props.floorOffset;
            MapComponent_OilFields oilFields = parent.Map?.GetComponent<MapComponent_OilFields>();
            if (oilFields?.FieldAt(targetPos) != null)
            {
                developmentComplete = true;
                needsMaintenance = false;
                currentProgressDays = Props.totalDaysNeeded;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentProgressDays, "currentProgressDays", 0f);
            Scribe_Values.Look(ref needsMaintenance, "needsMaintenance", false);
            Scribe_Values.Look(ref developmentComplete, "developmentComplete", false);
        }

        public override void CompTick()
        {
            base.CompTick();

            // Fuel is consumed only while actually drilling, never while the
            // rig is awaiting maintenance or has finished developing its field.
            if (IsWorking) parent.GetComp<CompRefuelable>()?.Notify_UsedThisTick();

            // Normal tickers (fuel/explosives) still simulate progress at the
            // same rare-tick interval as mechanically driven machines.
            if (parent.IsHashIntervalTick(GenTicks.TickRareInterval))
            {
                TickDevelopment(GenTicks.TickRareInterval);
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            TickDevelopment(GenTicks.TickRareInterval);
        }

        private void TickDevelopment(int intervalTicks)
        {

            if (IsWorking)
            {
                // Progress
                float dayDelta = intervalTicks / 60000f;
                currentProgressDays += dayDelta;

                // Spawn a burst of dust particles to maintain visual feedback at rare tick rate
                if (this.parent.Map != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 loc = (this.parent.Position + Props.floorOffset).ToVector3Shifted();
                        loc += new Vector3(Rand.Range(-0.4f, 0.4f), 0, Rand.Range(-0.4f, 0.4f));
                        FleckMaker.ThrowDustPuff(loc, this.parent.Map, Rand.Range(0.8f, 1.3f));
                    }
                }

                // A lucky strike can complete development early.
                if (Rand.MTBEventOccurs(Props.mtbDaysConfirmation, 60000f, intervalTicks))
                {
                    Messages.Message("HD_OilFieldStruckEarly".Translate(this.parent.Label), this.parent, MessageTypeDefOf.PositiveEvent);
                    FinishDevelopment();
                    return;
                }

                // Stop drilling until a colonist services the rig.
                if (Rand.MTBEventOccurs(Props.mtbDaysMaintenance, 60000f, intervalTicks))
                {
                    needsMaintenance = true;
                    Messages.Message("HD_OilFieldNeedsMaintenance".Translate(this.parent.Label), this.parent, MessageTypeDefOf.CautionInput);
                }

                // The configured drilling time guarantees completion.
                if (currentProgressDays >= Props.totalDaysNeeded)
                {
                    FinishDevelopment();
                }
            }
        }

        public void Notify_WorkPerformed()
        {
            needsMaintenance = false;
        }

        private void FinishDevelopment()
        {
            IntVec3 targetPos = this.parent.Position + Props.floorOffset;
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamed(Props.floorDefName, false);
            if (floor != null && targetPos.InBounds(this.parent.Map))
            {
                TerrainDef previousTerrain = targetPos.GetTerrain(parent.Map);
                if (previousTerrain != null && !previousTerrain.layerable)
                {
                    parent.Map.terrainGrid.SetUnderTerrain(targetPos, previousTerrain);
                }
                this.parent.Map.terrainGrid.SetTerrain(targetPos, floor);
                float quality = Rand.Range(Props.minimumQuality, Props.maximumQuality);
                this.parent.Map.GetComponent<MapComponent_OilFields>()
                    .CreateOrReplace(targetPos, quality, Props.initialPressure);
                Messages.Message("HD_OilFieldDevelopmentComplete".Translate(this.parent.Label), this.parent, MessageTypeDefOf.PositiveEvent);
            }
            currentProgressDays = Props.totalDaysNeeded;
            needsMaintenance = false;
            developmentComplete = true;
        }

        public override string CompInspectStringExtra()
        {
            if (developmentComplete)
            {
                return "HD_OilFieldDevelopment_CompleteInspect".Translate();
            }

            string s = "HD_OilFieldDevelopment_Progress".Translate(
                currentProgressDays.ToString("F2"),
                Props.totalDaysNeeded.ToString("F0"));
            if (needsMaintenance)
            {
                s += "\n<color=orange>" + "HD_OilFieldDevelopment_NeedsMaintenance".Translate() + "</color>";
            }
            else if (IsPowered)
            {
                s += "\n" + "HD_OilFieldDevelopment_Drilling".Translate();
            }
            else
            {
                s += "\n" + "HD_OilFieldDevelopment_NeedsPower".Translate();
            }
            return s;
        }
    }
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
internal static class OilMachineOperation
{public static bool IsPowered(ThingWithComps machine)=>(bool)typeof(CompProperties_OilFieldDevelopment).Assembly.GetType("Helodrace.OilMachineOperation",true).GetMethod("IsPowered",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{machine});}
}
