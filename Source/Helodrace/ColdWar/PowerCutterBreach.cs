using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Helodrace
{
    public class CompProperties_PowerCutterBreach : CompProperties
    {
        public string gizmoIconPath = "Skill/HD_BreachPowerCutter";
        public string cuttingGraphicPath = "Weapons/ColdWar/HD_PowerCutter_Cutting";
        public float playerDeconstructSpeedFactor = 1.5f;
        public int effectIntervalTicks = 2;
        public int sparksPerEmission = 4;
        public float sparkOriginRadius = 0.04f;
        public float sparkBrightness = 2f;
        public float sparkOriginScreenUpOffset = 0.08f;
        public float sparkUpwardBias = 0.18f;
        public int smokeIntervalTicks = 4;
        public int smokePerEmission = 2;
        public float smokeScale = 0.30f;
        public int dustIntervalTicks = 3;
        public int dustPerEmission = 4;
        public float dustOriginRadius = 0.22f;
        public float dustScale = 0.34f;
        public int metalFlashIntervalTicks = 3;
        public float metalFlashScale = 0.24f;
        public float breachContactCenterOffset = 0.30f;
        public float cuttingGraphicRightReach = 0.386f;
        public SimpleCurve nonPlayerWorkTicksByHitPoints;

        public CompProperties_PowerCutterBreach()
        {
            compClass = typeof(CompPowerCutterBreach);
        }
    }

    public class CompPowerCutterBreach : ThingComp
    {
        private const string BreachJobDefName = "HD_PowerCutterBreach";
        private Graphic cuttingGraphic;

        public CompProperties_PowerCutterBreach Props => (CompProperties_PowerCutterBreach)props;

        public Pawn Wielder
        {
            get
            {
                if (parent.ParentHolder is Pawn_EquipmentTracker tracker)
                {
                    return tracker.pawn;
                }

                return null;
            }
        }

        public bool IsBreaching
        {
            get
            {
                Pawn wielder = Wielder;
                return wielder?.CurJobDef?.defName == BreachJobDefName
                    && wielder.equipment?.Primary == parent;
            }
        }

        public bool IsActivelyCutting
        {
            get
            {
                Pawn wielder = Wielder;
                return IsBreaching
                    && wielder?.jobs?.curDriver is JobDriver_PowerCutterBreach driver
                    && driver.CuttingActive;
            }
        }

        public Building BreachTarget
        {
            get
            {
                Pawn wielder = Wielder;
                return IsActivelyCutting
                    ? wielder?.CurJob?.GetTarget(TargetIndex.A).Thing as Building
                    : null;
            }
        }

        public Graphic CuttingGraphic
        {
            get
            {
                if (cuttingGraphic == null)
                {
                    Vector2 drawSize = parent.def.graphicData?.drawSize ?? Vector2.one;
                    cuttingGraphic = GraphicDatabase.Get<Graphic_Single>(
                        Props.cuttingGraphicPath,
                        ShaderDatabase.Cutout,
                        drawSize,
                        Color.white);
                }

                return cuttingGraphic;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn wielder = Wielder;
            if (wielder?.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel = "HD_PowerCutter_Breach_Label".Translate().ToString(),
                defaultDesc = "HD_PowerCutter_Breach_Desc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get(Props.gizmoIconPath, false) ?? BaseContent.BadTex,
                action = BeginTargeting
            };

            if (!CanOperate(wielder))
            {
                command.Disable("HD_PowerCutter_Breach_Unavailable".Translate().ToString());
            }

            yield return command;
        }

        private static bool CanOperate(Pawn pawn)
        {
            return pawn != null
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) == true;
        }

        private void BeginTargeting()
        {
            Pawn wielder = Wielder;
            if (!CanOperate(wielder) || wielder.equipment?.Primary != parent)
            {
                Messages.Message(
                    "HD_PowerCutter_Breach_Unavailable".Translate(),
                    parent,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetLocations = false,
                    canTargetBuildings = true,
                    canTargetItems = false,
                    canTargetPawns = false,
                    validator = target => IsValidBreachTarget(target.Thing)
                },
                target => TryStartBreach(wielder, target.Thing as Building));
        }

        private void TryStartBreach(Pawn wielder, Building target)
        {
            if (!IsValidBreachTarget(target))
            {
                Messages.Message(
                    "HD_PowerCutter_Breach_InvalidTarget".Translate(),
                    target,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (!TryFindInteractionCell(wielder, target, out IntVec3 interactionCell))
            {
                Messages.Message(
                    "HD_PowerCutter_Breach_CannotReach".Translate(),
                    target,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (!wielder.CanReserve(target, 1, -1, null, false))
            {
                Messages.Message(
                    "HD_PowerCutter_Breach_CannotReserve".Translate(),
                    target,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            JobDef breachJob = DefDatabase<JobDef>.GetNamedSilentFail(BreachJobDefName);
            if (breachJob == null)
            {
                Log.Error("[Helodrace] Missing HD_PowerCutterBreach JobDef.");
                return;
            }

            wielder.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(breachJob, target, interactionCell),
                JobTag.Misc);
        }

        private static bool TryFindInteractionCell(
            Pawn wielder,
            Building target,
            out IntVec3 interactionCell)
        {
            foreach (IntVec3 candidate in GenAdj.CellsAdjacentCardinal(target)
                .OrderBy(cell => cell.DistanceToSquared(wielder.Position)))
            {
                if (candidate.InBounds(wielder.Map)
                    && candidate.Standable(wielder.Map)
                    && !candidate.IsForbidden(wielder)
                    && wielder.CanReserveAndReach(
                        candidate,
                        PathEndMode.OnCell,
                        Danger.Deadly))
                {
                    interactionCell = candidate;
                    return true;
                }
            }

            interactionCell = IntVec3.Invalid;
            return false;
        }

        public static Vector3 CardinalTowardTarget(Pawn worker, Building target)
        {
            Vector3 direction = target.DrawPos - worker.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = worker.Rotation.FacingCell.ToVector3();
            }

            return Rot4.FromAngleFlat(direction.AngleFlat()).FacingCell.ToVector3();
        }

        public static Vector3 CuttingGraphicUpDirection(Pawn worker, Building target)
        {
            float aimAngle = CardinalTowardTarget(worker, target).AngleFlat();
            float graphicAngle = aimAngle - 90f;

            // Match PawnRenderUtility.DrawEquipmentAiming: weapons aimed to
            // the left use a horizontally flipped mesh and subtract 180
            // degrees, so the texture's local-up axis remains correct.
            if (aimAngle > 200f && aimAngle < 340f)
            {
                graphicAngle -= 180f;
            }

            return Quaternion.AngleAxis(graphicAngle, Vector3.up) * Vector3.forward;
        }

        public static bool IsValidBreachTarget(Thing thing)
        {
            if (!(thing is Building building)
                || !building.Spawned
                || building.Destroyed
                || !building.def.destroyable
                || !building.def.useHitPoints)
            {
                return false;
            }

            bool impassableWall = building.def.IsWall
                && building.def.passability == Traversability.Impassable;
            return impassableWall || building.def.IsDoor;
        }

        public int WorkTicksFor(Building target, Pawn worker)
        {
            float constructionSpeed = Mathf.Max(0.1f, worker.GetStatValue(StatDefOf.ConstructionSpeed));
            float work;

            if (target.Faction == Faction.OfPlayer)
            {
                float vanillaDeconstructWork = Mathf.Clamp(
                    target.GetStatValue(StatDefOf.WorkToBuild, true, -1),
                    20f,
                    3000f);
                work = vanillaDeconstructWork / Mathf.Max(0.01f, Props.playerDeconstructSpeedFactor);
            }
            else
            {
                SimpleCurve curve = Props.nonPlayerWorkTicksByHitPoints;
                work = curve != null
                    ? curve.Evaluate(Mathf.Max(1, target.HitPoints))
                    : FallbackNonPlayerWork(target.HitPoints);
            }

            return Mathf.Max(30, Mathf.CeilToInt(work / constructionSpeed));
        }

        private static float FallbackNonPlayerWork(int hitPoints)
        {
            // Concave fallback used only if the XML curve is absent.
            return 60f + 45f * Mathf.Sqrt(Mathf.Max(1, hitPoints));
        }
    }

    public class JobDriver_PowerCutterBreach : JobDriver
    {
        private const TargetIndex StructureInd = TargetIndex.A;
        private const TargetIndex InteractionCellInd = TargetIndex.B;
        private static readonly string[] SparkDefNames =
        {
            "HD_PowerCutterSparkShort",
            "HD_PowerCutterSparkMedium",
            "HD_PowerCutterSparkLong"
        };

        private Sustainer cuttingSustainer;
        private bool cuttingActive;

        private Building Structure => job.GetTarget(StructureInd).Thing as Building;

        private IntVec3 InteractionCell => job.GetTarget(InteractionCellInd).Cell;

        public bool CuttingActive => cuttingActive;

        private CompPowerCutterBreach CutterComp =>
            pawn.equipment?.Primary?.TryGetComp<CompPowerCutterBreach>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cuttingActive, "powerCutterCuttingActive", false);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Structure, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(InteractionCell, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(StructureInd);
            this.FailOn(() => !CompPowerCutterBreach.IsValidBreachTarget(Structure));
            this.FailOn(() => CutterComp == null);
            this.FailOn(() => !InteractionCell.IsValid
                || !InteractionCell.InBounds(pawn.Map)
                || !InteractionCell.Standable(pawn.Map));
            AddFinishAction(_ => EndCuttingSound());

            yield return Toils_Goto.GotoCell(InteractionCellInd, PathEndMode.OnCell);

            int workTicks = CutterComp?.WorkTicksFor(Structure, pawn) ?? 120;
            Toil cut = Toils_General.Wait(workTicks, StructureInd);
            cut.handlingFacing = true;
            cut.WithProgressBarToilDelay(StructureInd);
            cut.FailOn(() => pawn.Position != InteractionCell);
            cut.initAction = StartCuttingSound;
            cut.tickAction = delegate
            {
                Building target = Structure;
                if (target == null)
                {
                    return;
                }

                pawn.rotationTracker.FaceTarget(target);
                MaintainCuttingSound();
                pawn.skills?.Learn(SkillDefOf.Construction, 0.08f);

                bool metalTarget = IsMetalTarget(target);
                int interval = Mathf.Max(1, CutterComp?.Props.effectIntervalTicks ?? 2);
                if (metalTarget && Find.TickManager.TicksGame % interval == 0)
                {
                    ThrowCuttingSparks(target);
                }

                int flashInterval = Mathf.Max(
                    1,
                    CutterComp?.Props.metalFlashIntervalTicks ?? 3);
                if (metalTarget && Find.TickManager.TicksGame % flashInterval == 0)
                {
                    ThrowMetalFlash(target);
                }

                int dustInterval = Mathf.Max(1, CutterComp?.Props.dustIntervalTicks ?? 3);
                if (!metalTarget && Find.TickManager.TicksGame % dustInterval == 0)
                {
                    ThrowCuttingDust(target);
                }

                int smokeInterval = Mathf.Max(1, CutterComp?.Props.smokeIntervalTicks ?? 6);
                if (Find.TickManager.TicksGame % smokeInterval == 0)
                {
                    ThrowCuttingSmoke(target);
                }
            };
            yield return cut;

            yield return Toils_General.Do(FinishBreach);
        }

        private void StartCuttingSound()
        {
            cuttingActive = true;
            if (pawn.Map == null)
            {
                return;
            }

            MaintainCuttingSound();
        }

        private void MaintainCuttingSound()
        {
            if (pawn.Map == null)
            {
                return;
            }

            if (cuttingSustainer == null || cuttingSustainer.Ended)
            {
                SoundDef loop = DefDatabase<SoundDef>.GetNamedSilentFail("HD_PowerCutter_BreachLoop");
                if (loop != null)
                {
                    cuttingSustainer = SoundStarter.TrySpawnSustainer(
                        loop,
                        SoundInfo.InMap(new TargetInfo(pawn), MaintenanceType.PerTick));
                }
            }

            cuttingSustainer?.Maintain();
        }

        private void EndCuttingSound()
        {
            cuttingActive = false;
            if (cuttingSustainer != null && !cuttingSustainer.Ended)
            {
                cuttingSustainer.End();
            }

            cuttingSustainer = null;
        }

        private void ThrowCuttingSparks(Building target)
        {
            Map map = pawn.Map;
            if (map == null || target.Map != map)
            {
                return;
            }

            CompPowerCutterBreach cutter = CutterComp;
            int sparkCount = Mathf.Max(1, cutter?.Props.sparksPerEmission ?? 4);
            for (int i = 0; i < sparkCount; i++)
            {
                ThrowSingleCuttingSpark(target, map, cutter);
            }
        }

        private void ThrowSingleCuttingSpark(
            Building target,
            Map map,
            CompPowerCutterBreach cutter)
        {
            string sparkDefName = SparkDefNames[Rand.Range(0, SparkDefNames.Length)];
            FleckDef fleckDef = DefDatabase<FleckDef>.GetNamedSilentFail(sparkDefName);
            if (fleckDef == null)
            {
                return;
            }

            Vector3 spawnPosition = EffectOrigin(target, pawn, cutter);
            float originRadius = Mathf.Max(0f, cutter?.Props.sparkOriginRadius ?? 0.04f);
            spawnPosition.x += Rand.Range(-originRadius, originRadius);
            spawnPosition.z += Rand.Range(-originRadius, originRadius);
            if (!spawnPosition.ToIntVec3().InBounds(map))
            {
                return;
            }

            Vector3 wallToWorker =
                -CompPowerCutterBreach.CardinalTowardTarget(pawn, target);

            // Emit toward the worker, then bias the fan slightly screen-up.
            Vector3 velocityDirection = Quaternion.AngleAxis(
                Rand.Range(-20f, 20f),
                Vector3.up) * wallToWorker;
            velocityDirection += Vector3.forward
                * Mathf.Max(0f, cutter?.Props.sparkUpwardBias ?? 0.18f);
            velocityDirection.Normalize();
            float travelAngle = velocityDirection.AngleFlat();
            // Use the vanilla creation path so altitude, age and render
            // state are initialized exactly like built-in sparks.
            FleckCreationData spark = FleckMaker.GetDataStatic(
                spawnPosition,
                map,
                fleckDef,
                // 60% of the original 0.9-1.45 range.
                Rand.Range(0.4f, 0.55f));
            float brightness = Mathf.Max(0f, cutter?.Props.sparkBrightness ?? 2f);
            spark.instanceColor = new Color(
                1f * brightness,
                0.92f * brightness,
                0.62f * brightness,
                1f);
            // All active assets point up at zero rotation.
            spark.rotation = travelAngle;
            spark.rotationRate = 0f;
            // Three times the original 0.12-0.38 movement speed.
            spark.velocity = velocityDirection * Rand.Range(6.0f, 7.0f);
            map.flecks.CreateFleck(spark);
        }

        private void ThrowCuttingSmoke(Building target)
        {
            Map map = pawn.Map;
            CompPowerCutterBreach cutter = CutterComp;
            FleckDef smokeDef = DefDatabase<FleckDef>.GetNamedSilentFail("HD_PowerCutterSmoke");
            if (map == null || target?.Map != map || smokeDef == null)
            {
                return;
            }

            Vector3 wallToWorker =
                -CompPowerCutterBreach.CardinalTowardTarget(pawn, target);
            Vector3 spawnPosition = EffectOrigin(target, pawn, cutter);
            int smokeCount = Mathf.Max(1, cutter?.Props.smokePerEmission ?? 2);
            for (int i = 0; i < smokeCount; i++)
            {
                Vector3 variedPosition = spawnPosition;
                variedPosition.x += Rand.Range(-0.06f, 0.06f);
                variedPosition.z += Rand.Range(-0.06f, 0.06f);
                if (!variedPosition.ToIntVec3().InBounds(map))
                {
                    continue;
                }

                float scale = Mathf.Max(0.05f, cutter?.Props.smokeScale ?? 0.30f)
                    * Rand.Range(0.85f, 1.20f);
                FleckCreationData smoke = FleckMaker.GetDataStatic(
                    variedPosition,
                    map,
                    smokeDef,
                    scale);
                smoke.rotation = Rand.Range(0f, 360f);
                smoke.rotationRate = Rand.Range(-12f, 12f);
                smoke.velocity = Quaternion.AngleAxis(
                    Rand.Range(-34f, 34f),
                    Vector3.up) * wallToWorker * Rand.Range(0.16f, 0.32f);
                map.flecks.CreateFleck(smoke);
            }
        }

        private void ThrowCuttingDust(Building target)
        {
            Map map = pawn.Map;
            CompPowerCutterBreach cutter = CutterComp;
            FleckDef dustDef = DefDatabase<FleckDef>.GetNamedSilentFail("HD_PowerCutterDust");
            if (map == null || target?.Map != map || dustDef == null)
            {
                return;
            }

            Vector3 wallToWorker =
                -CompPowerCutterBreach.CardinalTowardTarget(pawn, target);
            Vector3 origin = EffectOrigin(target, pawn, cutter);
            int dustCount = Mathf.Max(1, cutter?.Props.dustPerEmission ?? 4);
            float radius = Mathf.Max(0f, cutter?.Props.dustOriginRadius ?? 0.22f);
            for (int i = 0; i < dustCount; i++)
            {
                Vector3 position = origin;
                position.x += Rand.Range(-radius, radius);
                position.z += Rand.Range(-radius, radius);
                if (!position.ToIntVec3().InBounds(map))
                {
                    continue;
                }

                float scale = Mathf.Max(0.05f, cutter?.Props.dustScale ?? 0.34f)
                    * Rand.Range(0.75f, 1.30f);
                Vector3 direction = Quaternion.AngleAxis(
                    Rand.Range(-70f, 70f),
                    Vector3.up) * wallToWorker;
                FleckCreationData dust = FleckMaker.GetDataStatic(
                    position,
                    map,
                    dustDef,
                    scale);
                dust.instanceColor = new Color(0.78f, 0.72f, 0.62f, 0.88f);
                dust.rotation = Rand.Range(0f, 360f);
                dust.rotationRate = Rand.Range(-24f, 24f);
                dust.velocity = direction * Rand.Range(0.30f, 0.70f);
                map.flecks.CreateFleck(dust);
            }
        }

        private void ThrowMetalFlash(Building target)
        {
            Map map = pawn.Map;
            CompPowerCutterBreach cutter = CutterComp;
            if (map == null || target?.Map != map)
            {
                return;
            }

            Vector3 origin = EffectOrigin(target, pawn, cutter);
            FleckDef flashDef = DefDatabase<FleckDef>.GetNamedSilentFail(
                "HD_PowerCutterMetalFlash");
            if (flashDef != null)
            {
                float scale = Mathf.Max(0.05f, cutter?.Props.metalFlashScale ?? 0.24f)
                    * Rand.Range(0.75f, 1.25f);
                FleckCreationData flash = FleckMaker.GetDataStatic(
                    origin,
                    map,
                    flashDef,
                    scale);
                flash.instanceColor = new Color(2.4f, 1.85f, 0.85f, 1f);
                map.flecks.CreateFleck(flash);
            }

            ThingDef lightDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                "HD_PowerCutterMetalFlashLight");
            if (lightDef != null && origin.ToIntVec3().InBounds(map))
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(lightDef), origin.ToIntVec3(), map);
            }
        }

        private static Vector3 BreachContactPoint(
            Building target,
            Pawn worker,
            CompPowerCutterBreach cutter)
        {
            Vector3 wallToWorker =
                -CompPowerCutterBreach.CardinalTowardTarget(worker, target);
            float offset = Mathf.Max(
                0f,
                cutter?.Props.breachContactCenterOffset ?? 0.30f);
            return target.DrawPos + wallToWorker * offset;
        }

        private static Vector3 EffectOrigin(
            Building target,
            Pawn worker,
            CompPowerCutterBreach cutter)
        {
            Vector3 origin = BreachContactPoint(target, worker, cutter);
            origin += CompPowerCutterBreach.CuttingGraphicUpDirection(worker, target)
                * Mathf.Max(
                0f,
                cutter?.Props.sparkOriginScreenUpOffset ?? 0.08f);
            return origin;
        }

        private static bool IsMetalTarget(Building target)
        {
            if (target?.Stuff != null)
            {
                return target.Stuff.IsMetal;
            }

            if (target?.def?.IsMetal == true)
            {
                return true;
            }

            return target?.def?.costList?.Any(cost => cost.thingDef?.IsMetal == true) == true;
        }

        private void FinishBreach()
        {
            Building target = Structure;
            if (!CompPowerCutterBreach.IsValidBreachTarget(target))
            {
                return;
            }

            target.Map?.designationManager
                ?.DesignationOn(target, DesignationDefOf.Deconstruct)
                ?.Delete();
            target.Destroy(DestroyMode.KillFinalize);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_PowerCutterBreach
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            CompPowerCutterBreach comp =
                __instance?.equipment?.Primary?.TryGetComp<CompPowerCutterBreach>();
            if (comp != null)
            {
                __result = __result.Concat(comp.CompGetGizmosExtra());
            }
        }
    }

    [HarmonyPatch(typeof(Thing), "get_Graphic")]
    public static class Patch_Thing_Graphic_PowerCutterBreach
    {
        public static bool Prefix(Thing __instance, ref Graphic __result)
        {
            if (__instance is ThingWithComps thingWithComps)
            {
                CompPowerCutterBreach comp = thingWithComps.TryGetComp<CompPowerCutterBreach>();
                if (comp?.IsActivelyCutting == true)
                {
                    __result = comp.CuttingGraphic;
                    return false;
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
    public static class Patch_PawnRenderUtility_DrawEquipmentAiming_PowerCutterBreach
    {
        public static void Prefix(Thing eq, ref Vector3 drawLoc, ref float aimAngle)
        {
            CompPowerCutterBreach comp =
                (eq as ThingWithComps)?.TryGetComp<CompPowerCutterBreach>();
            Building target = comp?.BreachTarget;
            Pawn wielder = comp?.Wielder;
            if (target == null || wielder == null)
            {
                return;
            }

            Vector3 towardTarget =
                CompPowerCutterBreach.CardinalTowardTarget(wielder, target);

            float contactOffset = Mathf.Max(0f, comp.Props.breachContactCenterOffset);
            float graphicReach = Mathf.Max(0f, comp.Props.cuttingGraphicRightReach);
            Vector3 contactPoint = target.DrawPos - towardTarget * contactOffset;
            Vector3 desiredCenter = contactPoint - towardTarget * graphicReach;
            drawLoc.x = desiredCenter.x;
            drawLoc.z = desiredCenter.z;
            aimAngle = towardTarget.AngleFlat();
        }
    }
}
