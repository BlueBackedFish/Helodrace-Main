using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public enum BreachInitiationMode
    {
        TimeFuse,
        ShockTube
    }

    public class CompProperties_BreachIgniter : CompProperties
    {
        public bool supportsTimeFuse = true;
        public bool supportsShockTube;
        public float timeFuseRange = 12f;
        public float shockTubeRange = 30f;
        public float shockTubeInstallWorkFactor = 1.2f;

        public CompProperties_BreachIgniter()
        {
            compClass = typeof(CompBreachIgniter);
        }
    }

    public class CompBreachIgniter : ThingComp
    {
        public CompProperties_BreachIgniter Props =>
            (CompProperties_BreachIgniter)props;

        public bool Supports(BreachInitiationMode mode)
        {
            return mode == BreachInitiationMode.ShockTube
                ? Props.supportsShockTube
                : Props.supportsTimeFuse;
        }

        public float RangeFor(BreachInitiationMode mode)
        {
            return mode == BreachInitiationMode.ShockTube
                ? Mathf.Max(1f, Props.shockTubeRange)
                : Mathf.Max(1f, Props.timeFuseRange);
        }

        public float InstallWorkFactorFor(BreachInitiationMode mode)
        {
            return mode == BreachInitiationMode.ShockTube
                ? Mathf.Max(0.1f, Props.shockTubeInstallWorkFactor)
                : 1f;
        }
    }

    public class CompProperties_InstalledBreachCharge : CompProperties
    {
        public string c4DefName = "HD_C4";
        public float hitPointsPerC4 = 500f;
        public float workTicksPerC4 = 120f;
        public int minimumWorkTicks = 150;
        public float explosionRadiusBase = 1.5f;
        public float explosionRadiusPerC4 = 0.3f;
        public float explosionArmorPenetration = 0.35f;
        public string beyondSuppressionProjectileDefName = "HD_Projectile_MKIII";
        public string beyondFragmentProjectileDefName = "HD_Projectile_MKIIFragment";
        public int beyondFragmentCount = 18;
        public float beyondFragmentRadius = 8f;
        public float beyondFragmentMinimumRangeFactor = 0.25f;
        public float beyondFragmentConeDegrees = 120f;
        public int triggerWorkTicks = 60;
        public float fuseTicksPerCell = 60f;
        public string installGizmoIconPath = "Skill/HD_SetBreachCharge";
        public string detonateGizmoIconPath = "Skill/HD_BreachExplosive";

        public CompProperties_InstalledBreachCharge()
        {
            compClass = typeof(CompInstalledBreachCharge);
        }
    }

    public class Thing_InstalledBreachCharge : ThingWithComps
    {
        public override Vector3 DrawPos
        {
            get
            {
                Vector3 drawPos = base.DrawPos;
                Vector3 outward = Rotation.FacingCell.ToVector3();
                drawPos += outward * 0.5f;
                return drawPos;
            }
        }
    }

    public class CompInstalledBreachCharge : ThingComp
    {
        private Building targetWall;
        private Pawn operatorPawn;
        private ThingDef igniterDef;
        private BreachInitiationMode initiationMode;
        private int c4Count;
        private float tetherRange;
        private bool triggered;
        private int ticksToDetonation;

        public CompProperties_InstalledBreachCharge Props =>
            (CompProperties_InstalledBreachCharge)props;

        public Building TargetWall => targetWall;
        public Pawn OperatorPawn => operatorPawn;
        public ThingDef IgniterDef => igniterDef;
        public BreachInitiationMode InitiationMode => initiationMode;
        public int C4Count => c4Count;
        public float TetherRange => tetherRange;
        public bool Triggered => triggered;

        public bool IsActive => parent.Spawned
            && targetWall != null
            && targetWall.Spawned
            && !targetWall.Destroyed;

        public bool RequiresOperatorControl => IsActive && !triggered;

        public void Initialize(
            Building wall,
            Pawn operatorPawn,
            ThingDef igniterDef,
            BreachInitiationMode mode,
            int c4Count,
            float tetherRange)
        {
            targetWall = wall;
            this.operatorPawn = operatorPawn;
            this.igniterDef = igniterDef;
            initiationMode = mode;
            this.c4Count = Mathf.Max(1, c4Count);
            this.tetherRange = Mathf.Max(1f, tetherRange);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref targetWall, "breachChargeTargetWall");
            Scribe_References.Look(ref operatorPawn, "breachChargeOperator");
            Scribe_Defs.Look(ref igniterDef, "breachChargeIgniterDef");
            Scribe_Values.Look(
                ref initiationMode,
                "breachChargeInitiationMode",
                BreachInitiationMode.TimeFuse);
            Scribe_Values.Look(ref c4Count, "breachChargeC4Count", 1);
            Scribe_Values.Look(ref tetherRange, "breachChargeTetherRange", 12f);
            Scribe_Values.Look(ref triggered, "breachChargeTriggered", false);
            Scribe_Values.Look(
                ref ticksToDetonation,
                "breachChargeTicksToDetonation",
                0);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (triggered)
            {
                ticksToDetonation--;
                if (ticksToDetonation <= 0)
                {
                    DetonateNow();
                }

                return;
            }

            if (Find.TickManager.TicksGame % 250 == 0
                && (!IsActive
                || operatorPawn == null
                || operatorPawn.Destroyed
                || !operatorPawn.Spawned
                || operatorPawn.Map != parent.Map))
            {
                Abandon(false);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!triggered)
            {
                return base.CompInspectStringExtra();
            }

            return "HD_BreachExplosive_FuseCountdown".Translate(
                Mathf.Max(0f, ticksToDetonation / 60f).ToString("0.0"));
        }

        public bool IsWithinTether(IntVec3 cell)
        {
            if (!parent.Spawned || !cell.IsValid)
            {
                return true;
            }

            return cell.DistanceToSquared(parent.Position)
                <= tetherRange * tetherRange;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            foreach (Gizmo gizmo in GetControlGizmos())
            {
                yield return gizmo;
            }
        }

        public IEnumerable<Gizmo> GetControlGizmos()
        {
            if (operatorPawn?.Faction != Faction.OfPlayer || triggered)
            {
                yield break;
            }

            Command_Action detonate = new Command_Action
            {
                defaultLabel = "HD_BreachExplosive_Detonate_Label".Translate().ToString(),
                defaultDesc = "HD_BreachExplosive_Detonate_Desc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get(
                    Props.detonateGizmoIconPath,
                    false) ?? BaseContent.BadTex,
                action = TryStartTriggerJob
            };

            if (!CanTrigger(out string reason))
            {
                detonate.Disable(reason);
            }

            yield return detonate;

            yield return new Command_Action
            {
                defaultLabel = "HD_BreachExplosive_Abandon_Label".Translate().ToString(),
                defaultDesc = "HD_BreachExplosive_Abandon_Desc".Translate().ToString(),
                icon = TexCommand.ClearPrioritizedWork,
                action = () => Abandon(true)
            };
        }

        public bool CanTrigger(out string reason)
        {
            if (!RequiresOperatorControl)
            {
                reason = "HD_BreachExplosive_ChargeInvalid".Translate().ToString();
                return false;
            }

            if (operatorPawn == null
                || operatorPawn.Dead
                || operatorPawn.Downed
                || !operatorPawn.Spawned
                || operatorPawn.Map != parent.Map
                || operatorPawn.health?.capacities
                    ?.CapableOf(PawnCapacityDefOf.Manipulation) != true)
            {
                reason = "HD_BreachExplosive_OperatorUnavailable".Translate().ToString();
                return false;
            }

            if (!IsWithinTether(operatorPawn.Position))
            {
                reason = "HD_BreachExplosive_OutOfRange".Translate(
                    tetherRange.ToString("0.#")).ToString();
                return false;
            }

            reason = null;
            return true;
        }

        private void TryStartTriggerJob()
        {
            if (!CanTrigger(out string reason))
            {
                Messages.Message(
                    reason,
                    parent,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(
                BreachExplosiveUtility.TriggerJobDefName);
            if (jobDef == null)
            {
                Log.Error("[Helodrace] Missing HD_TriggerBreachCharge JobDef.");
                return;
            }

            operatorPawn.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(jobDef, parent),
                JobTag.Misc);
        }

        public void Trigger()
        {
            if (!CanTrigger(out string reason))
            {
                Messages.Message(
                    reason,
                    parent,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (initiationMode == BreachInitiationMode.ShockTube)
            {
                DetonateNow();
                return;
            }

            float distance = operatorPawn.Position.DistanceTo(parent.Position);
            ticksToDetonation = Mathf.Max(
                1,
                Mathf.CeilToInt(distance * Mathf.Max(1f, Props.fuseTicksPerCell)));
            triggered = true;
            Messages.Message(
                "HD_BreachExplosive_FuseLit".Translate(
                    Mathf.Max(0f, ticksToDetonation / 60f).ToString("0.0")),
                parent,
                MessageTypeDefOf.CautionInput,
                false);
        }

        private void DetonateNow()
        {
            if (parent.Destroyed || !parent.Spawned)
            {
                return;
            }

            Map map = parent.Map;
            IntVec3 position = parent.Position;
            Pawn instigator = operatorPawn;
            Building wall = targetWall;
            int chargeCount = Mathf.Max(1, c4Count);
            int targetHitPoints = wall != null && !wall.Destroyed
                ? Mathf.Max(1, wall.HitPoints)
                : 1;
            IntVec3 beyondDirection = parent.Rotation.Opposite.FacingCell;
            IntVec3 beyondCell = position + beyondDirection;

            if (beyondCell.InBounds(map))
            {
                ApplyBeyondSuppressionBeforeBreach(
                    beyondCell,
                    map,
                    instigator);
                ThrowBeyondFragments(
                    beyondCell,
                    beyondDirection,
                    map,
                    instigator);
            }

            if (wall != null && wall.Spawned && !wall.Destroyed)
            {
                map?.designationManager
                    ?.DesignationOn(wall, DesignationDefOf.Deconstruct)
                    ?.Delete();
                wall.Destroy(DestroyMode.KillFinalize);
            }
            parent.Destroy(DestroyMode.Vanish);

            if (map != null && position.InBounds(map))
            {
                float radius = Props.explosionRadiusBase
                    + Mathf.Sqrt(chargeCount) * Props.explosionRadiusPerC4;
                GenExplosion.DoExplosion(
                    position,
                    map,
                    Mathf.Max(0.1f, radius),
                    DamageDefOf.Bomb,
                    instigator,
                    targetHitPoints,
                    armorPenetration: Mathf.Max(0f, Props.explosionArmorPenetration),
                    damageFalloff: true);
            }
        }

        private void ApplyBeyondSuppressionBeforeBreach(
            IntVec3 beyondCell,
            Map map,
            Thing instigator)
        {
            try
            {
                ThingDef sourceDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                    Props.beyondSuppressionProjectileDefName);
                ModernWar.FlashbangProjectileExtension suppression = sourceDef
                    ?.GetModExtension<ModernWar.FlashbangProjectileExtension>();

                // This runs while the target wall or door still exists, so
                // GetRoom sees the far-side room exactly as it was before breach.
                ModernWar.FlashbangUtility.ApplySuppression(
                    beyondCell,
                    map,
                    suppression,
                    instigator);
            }
            catch (System.Exception exception)
            {
                Log.ErrorOnce(
                    $"Helodrace breach suppression failed; continuing detonation. {exception}",
                    parent.thingIDNumber ^ 0x42B1);
            }
        }

        private void ThrowBeyondFragments(
            IntVec3 beyondCell,
            IntVec3 beyondDirection,
            Map map,
            Thing instigator)
        {
            try
            {
                ThingDef fragmentDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                    Props.beyondFragmentProjectileDefName);
                ModernWar.FragmentationGrenadeExtension fragmentation =
                    new ModernWar.FragmentationGrenadeExtension
                    {
                        fragmentProjectile = fragmentDef,
                        fragmentCount = Mathf.Max(1, Props.beyondFragmentCount),
                        radius = Mathf.Max(0.1f, Props.beyondFragmentRadius),
                        minimumRangeFactor = Mathf.Clamp(
                            Props.beyondFragmentMinimumRangeFactor,
                            0.05f,
                            1f)
                    };

                ModernWar.GrenadeExplosionEffectUtility.ThrowDirectionalFragments(
                    beyondCell,
                    map,
                    fragmentation,
                    instigator,
                    parent,
                    beyondDirection.ToVector3(),
                    Props.beyondFragmentConeDegrees);
            }
            catch (System.Exception exception)
            {
                Log.ErrorOnce(
                    $"Helodrace breach fragments failed; continuing detonation. {exception}",
                    parent.thingIDNumber ^ 0x71C3);
            }
        }

        public void Abandon(bool showMessage)
        {
            if (parent.Destroyed)
            {
                return;
            }

            Thing messageTarget = targetWall ?? parent;
            parent.Destroy(DestroyMode.Vanish);

            if (showMessage)
            {
                Messages.Message(
                    "HD_BreachExplosive_Abandoned".Translate(),
                    messageTarget,
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }
        }
    }

    public static class BreachExplosiveUtility
    {
        public const string InstalledChargeDefName = "HD_InstalledBreachCharge";
        public const string FuseJobDefName = "HD_InstallBreachChargeFuse";
        public const string ShockTubeJobDefName = "HD_InstallBreachChargeShockTube";
        public const string TriggerJobDefName = "HD_TriggerBreachCharge";

        private static ThingDef installedChargeDef;

        public static ThingDef InstalledChargeDef
        {
            get
            {
                if (installedChargeDef == null)
                {
                    installedChargeDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                        InstalledChargeDefName);
                }

                return installedChargeDef;
            }
        }

        public static CompProperties_InstalledBreachCharge ChargeProps =>
            InstalledChargeDef?.GetCompProperties<CompProperties_InstalledBreachCharge>();

        public static IEnumerable<Gizmo> GetPawnGizmos(Pawn pawn)
        {
            if (pawn?.Faction != Faction.OfPlayer || pawn.inventory == null)
            {
                yield break;
            }

            CompInstalledBreachCharge activeCharge = ActiveChargeFor(pawn);
            if (activeCharge != null)
            {
                foreach (Gizmo gizmo in activeCharge.GetControlGizmos())
                {
                    yield return gizmo;
                }

                yield break;
            }

            CompBreachIgniter fuseIgniter = FindIgniter(
                pawn,
                BreachInitiationMode.TimeFuse,
                preferM60: true);
            if (fuseIgniter != null)
            {
                yield return MakeInstallCommand(
                    pawn,
                    fuseIgniter,
                    BreachInitiationMode.TimeFuse);
            }

            CompBreachIgniter shockTubeIgniter = FindIgniter(
                pawn,
                BreachInitiationMode.ShockTube,
                preferM60: false);
            if (shockTubeIgniter != null)
            {
                yield return MakeInstallCommand(
                    pawn,
                    shockTubeIgniter,
                    BreachInitiationMode.ShockTube);
            }
        }

        private static Command_Action MakeInstallCommand(
            Pawn pawn,
            CompBreachIgniter igniter,
            BreachInitiationMode mode)
        {
            bool shockTube = mode == BreachInitiationMode.ShockTube;
            string labelKey = shockTube
                ? "HD_BreachExplosive_InstallShockTube_Label"
                : "HD_BreachExplosive_InstallFuse_Label";
            string descKey = shockTube
                ? "HD_BreachExplosive_InstallShockTube_Desc"
                : "HD_BreachExplosive_InstallFuse_Desc";

            Command_Action command = new Command_Action
            {
                defaultLabel = labelKey.Translate().ToString(),
                defaultDesc = descKey.Translate(
                    igniter.RangeFor(mode).ToString("0.#")).ToString(),
                icon = ContentFinder<Texture2D>.Get(
                    ChargeProps?.installGizmoIconPath ?? "Skill/HD_SetBreachCharge",
                    false) ?? BaseContent.BadTex,
                action = () => BeginTargeting(pawn, igniter, mode)
            };

            if (!CanOperate(pawn))
            {
                command.Disable(
                    "HD_BreachExplosive_OperatorUnavailable".Translate().ToString());
            }
            else if (CountInInventory(pawn, C4Def) < 1)
            {
                command.Disable("HD_BreachExplosive_NoC4".Translate(1).ToString());
            }

            return command;
        }

        private static void BeginTargeting(
            Pawn pawn,
            CompBreachIgniter igniter,
            BreachInitiationMode mode)
        {
            if (!CanOperate(pawn)
                || igniter?.parent == null
                || !HasInInventory(pawn, igniter.parent.def))
            {
                Messages.Message(
                    "HD_BreachExplosive_OperatorUnavailable".Translate(),
                    pawn,
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
                    validator = target => IsValidWall(target.Thing)
                },
                target => TryStartInstallation(
                    pawn,
                    igniter,
                    mode,
                    target.Thing as Building));
        }

        private static void TryStartInstallation(
            Pawn pawn,
            CompBreachIgniter igniter,
            BreachInitiationMode mode,
            Building target)
        {
            if (!IsValidWall(target))
            {
                Reject("HD_BreachExplosive_InvalidTarget", target);
                return;
            }

            if (ActiveChargeFor(pawn) != null)
            {
                Reject("HD_BreachExplosive_AlreadyLinked", pawn);
                return;
            }

            if (ChargeOnWall(target) != null)
            {
                Reject("HD_BreachExplosive_WallAlreadyCharged", target);
                return;
            }

            if (igniter?.parent == null
                || !igniter.Supports(mode)
                || !pawn.inventory.innerContainer.Contains(igniter.parent))
            {
                Reject("HD_BreachExplosive_MissingIgniter", pawn);
                return;
            }

            int requiredC4 = RequiredC4For(target);
            int availableC4 = CountInInventory(pawn, C4Def);
            if (availableC4 < requiredC4)
            {
                Messages.Message(
                    "HD_BreachExplosive_NoC4".Translate(requiredC4),
                    target,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (!TryFindInteractionCell(pawn, target, out IntVec3 interactionCell))
            {
                Reject("HD_BreachExplosive_CannotReach", target);
                return;
            }

            if (!pawn.CanReserve(target, 1, -1, null, false))
            {
                Reject("HD_BreachExplosive_CannotReserve", target);
                return;
            }

            string jobDefName = mode == BreachInitiationMode.ShockTube
                ? ShockTubeJobDefName
                : FuseJobDefName;
            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(jobDefName);
            if (jobDef == null)
            {
                Log.Error("[Helodrace] Missing " + jobDefName + " JobDef.");
                return;
            }

            Job job = JobMaker.MakeJob(jobDef, target, interactionCell, igniter.parent);
            job.count = requiredC4;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static void Reject(string translationKey, Thing target)
        {
            Messages.Message(
                translationKey.Translate(),
                target,
                MessageTypeDefOf.RejectInput,
                false);
        }

        public static bool CanOperate(Pawn pawn)
        {
            return pawn != null
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.health?.capacities
                    ?.CapableOf(PawnCapacityDefOf.Manipulation) == true;
        }

        public static bool IsValidWall(Thing thing)
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

        public static int RequiredC4For(Building wall)
        {
            float hitPointsPerC4 = Mathf.Max(
                1f,
                ChargeProps?.hitPointsPerC4 ?? 200f);
            return Mathf.Max(
                1,
                Mathf.CeilToInt(Mathf.Max(1, wall?.HitPoints ?? 1) / hitPointsPerC4));
        }

        public static int WorkTicksFor(
            Pawn worker,
            CompBreachIgniter igniter,
            BreachInitiationMode mode,
            int c4Count)
        {
            CompProperties_InstalledBreachCharge chargeProps = ChargeProps;
            float rawWork = Mathf.Max(
                chargeProps?.minimumWorkTicks ?? 150,
                Mathf.Max(1, c4Count)
                    * (chargeProps?.workTicksPerC4 ?? 120f));
            rawWork *= igniter?.InstallWorkFactorFor(mode) ?? 1f;
            float constructionSpeed = Mathf.Max(
                0.1f,
                worker?.GetStatValue(StatDefOf.ConstructionSpeed) ?? 1f);
            return Mathf.Max(30, Mathf.CeilToInt(rawWork / constructionSpeed));
        }

        public static bool TryFindInteractionCell(
            Pawn pawn,
            Building target,
            out IntVec3 interactionCell)
        {
            foreach (IntVec3 candidate in GenAdj.CellsAdjacentCardinal(target)
                .OrderBy(cell => cell.DistanceToSquared(pawn.Position)))
            {
                if (candidate.InBounds(pawn.Map)
                    && candidate.Standable(pawn.Map)
                    && !candidate.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(
                        candidate,
                        PathEndMode.OnCell,
                        Danger.Deadly,
                        1,
                        -1,
                        null,
                        false))
                {
                    interactionCell = candidate;
                    return true;
                }
            }

            interactionCell = IntVec3.Invalid;
            return false;
        }

        public static CompBreachIgniter FindIgniter(
            Pawn pawn,
            BreachInitiationMode mode,
            bool preferM60)
        {
            if (pawn?.inventory?.innerContainer == null)
            {
                return null;
            }

            CompBreachIgniter fallback = null;
            foreach (Thing thing in pawn.inventory.innerContainer)
            {
                CompBreachIgniter comp =
                    (thing as ThingWithComps)?.TryGetComp<CompBreachIgniter>();
                if (comp?.Supports(mode) != true)
                {
                    continue;
                }

                bool isM60 = thing.def.defName == "HD_M60Igniter";
                if (isM60 == preferM60)
                {
                    return comp;
                }

                fallback = comp;
            }

            return fallback;
        }

        public static ThingDef C4Def => DefDatabase<ThingDef>.GetNamedSilentFail(
            ChargeProps?.c4DefName ?? "HD_C4");

        public static int CountInInventory(Pawn pawn, ThingDef def)
        {
            if (pawn?.inventory?.innerContainer == null || def == null)
            {
                return 0;
            }

            int count = 0;
            foreach (Thing thing in pawn.inventory.innerContainer)
            {
                if (thing.def == def)
                {
                    count += thing.stackCount;
                }
            }

            return count;
        }

        public static bool HasInInventory(Pawn pawn, ThingDef def)
        {
            return CountInInventory(pawn, def) > 0;
        }

        public static bool ConsumeFromInventory(Pawn pawn, ThingDef def, int count)
        {
            if (CountInInventory(pawn, def) < count)
            {
                return false;
            }

            ThingOwner<Thing> inventory = pawn.inventory.innerContainer;
            int remaining = count;
            for (int i = inventory.Count - 1; i >= 0 && remaining > 0; i--)
            {
                Thing thing = inventory[i];
                if (thing.def != def)
                {
                    continue;
                }

                int takeCount = Mathf.Min(remaining, thing.stackCount);
                Thing taken = inventory.Take(thing, takeCount);
                remaining -= takeCount;
                taken.Destroy(DestroyMode.Vanish);
            }

            return remaining == 0;
        }

        public static CompInstalledBreachCharge ActiveChargeFor(Pawn pawn)
        {
            if (pawn?.Map == null || InstalledChargeDef == null)
            {
                return null;
            }

            List<Thing> charges = pawn.Map.listerThings.ThingsOfDef(InstalledChargeDef);
            for (int i = 0; i < charges.Count; i++)
            {
                CompInstalledBreachCharge comp =
                    (charges[i] as ThingWithComps)
                    ?.TryGetComp<CompInstalledBreachCharge>();
                if (comp?.OperatorPawn == pawn && comp.RequiresOperatorControl)
                {
                    return comp;
                }
            }

            return null;
        }

        public static CompInstalledBreachCharge ChargeOnWall(Building wall)
        {
            if (wall?.Map == null || InstalledChargeDef == null)
            {
                return null;
            }

            List<Thing> charges = wall.Map.listerThings.ThingsOfDef(InstalledChargeDef);
            for (int i = 0; i < charges.Count; i++)
            {
                CompInstalledBreachCharge comp =
                    (charges[i] as ThingWithComps)
                    ?.TryGetComp<CompInstalledBreachCharge>();
                if (comp?.TargetWall == wall && comp.IsActive)
                {
                    return comp;
                }
            }

            return null;
        }
    }

    public class JobDriver_InstallBreachCharge : JobDriver
    {
        private const TargetIndex WallInd = TargetIndex.A;
        private const TargetIndex InteractionCellInd = TargetIndex.B;
        private const TargetIndex IgniterInd = TargetIndex.C;

        private Building Wall => job.GetTarget(WallInd).Thing as Building;
        private IntVec3 InteractionCell => job.GetTarget(InteractionCellInd).Cell;
        private ThingWithComps Igniter =>
            job.GetTarget(IgniterInd).Thing as ThingWithComps;
        private CompBreachIgniter IgniterComp =>
            Igniter?.TryGetComp<CompBreachIgniter>();

        private BreachInitiationMode Mode =>
            job.def.defName == BreachExplosiveUtility.ShockTubeJobDefName
                ? BreachInitiationMode.ShockTube
                : BreachInitiationMode.TimeFuse;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Wall, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(
                    InteractionCell,
                    job,
                    1,
                    -1,
                    null,
                    errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(WallInd);
            this.FailOn(() => !BreachExplosiveUtility.IsValidWall(Wall));
            this.FailOn(() => IgniterComp?.Supports(Mode) != true);
            this.FailOn(() => pawn.inventory?.innerContainer
                ?.Contains(Igniter) != true);
            this.FailOn(() => BreachExplosiveUtility.CountInInventory(
                pawn,
                BreachExplosiveUtility.C4Def) < job.count);
            this.FailOn(() => BreachExplosiveUtility.ChargeOnWall(Wall) != null);
            this.FailOn(() => BreachExplosiveUtility.ActiveChargeFor(pawn) != null);

            yield return Toils_Goto.GotoCell(
                InteractionCellInd,
                PathEndMode.OnCell);

            int workTicks = BreachExplosiveUtility.WorkTicksFor(
                pawn,
                IgniterComp,
                Mode,
                job.count);
            Toil install = Toils_General.Wait(workTicks, WallInd);
            install.handlingFacing = true;
            install.WithProgressBarToilDelay(WallInd);
            install.FailOn(() => pawn.Position != InteractionCell);
            install.tickAction = delegate
            {
                if (Wall != null)
                {
                    pawn.rotationTracker.FaceTarget(Wall);
                    pawn.skills?.Learn(SkillDefOf.Construction, 0.08f);
                }
            };
            yield return install;

            yield return Toils_General.Do(FinishInstallation);
        }

        private void FinishInstallation()
        {
            Building wall = Wall;
            CompBreachIgniter igniter = IgniterComp;
            ThingDef chargeDef = BreachExplosiveUtility.InstalledChargeDef;
            ThingDef c4Def = BreachExplosiveUtility.C4Def;
            ThingDef igniterDef = Igniter?.def;
            int requiredC4 = Mathf.Max(1, job.count);

            if (!BreachExplosiveUtility.IsValidWall(wall)
                || igniter?.Supports(Mode) != true
                || chargeDef == null
                || c4Def == null
                || igniterDef == null
                || BreachExplosiveUtility.ChargeOnWall(wall) != null
                || BreachExplosiveUtility.ActiveChargeFor(pawn) != null
                || BreachExplosiveUtility.CountInInventory(pawn, igniterDef) < 1
                || BreachExplosiveUtility.CountInInventory(pawn, c4Def) < requiredC4)
            {
                return;
            }

            float range = igniter.RangeFor(Mode);
            if (!BreachExplosiveUtility.ConsumeFromInventory(pawn, igniterDef, 1)
                || !BreachExplosiveUtility.ConsumeFromInventory(
                    pawn,
                    c4Def,
                    requiredC4))
            {
                return;
            }

            Map map = wall.Map;
            ThingWithComps charge = ThingMaker.MakeThing(chargeDef) as ThingWithComps;
            if (charge == null)
            {
                Log.Error("[Helodrace] HD_InstalledBreachCharge is not a ThingWithComps.");
                return;
            }

            IntVec3 facing = pawn.Position - wall.Position;
            Rot4 installRotation = facing.IsValid && facing != IntVec3.Zero
                ? Rot4.FromIntVec3(facing)
                : pawn.Rotation.Opposite;
            GenSpawn.Spawn(
                charge,
                wall.Position,
                map,
                installRotation,
                WipeMode.Vanish,
                false,
                false);

            CompInstalledBreachCharge installed =
                charge.TryGetComp<CompInstalledBreachCharge>();
            if (installed == null)
            {
                charge.Destroy(DestroyMode.Vanish);
                Log.Error("[Helodrace] Installed breach charge comp is missing.");
                return;
            }

            installed.Initialize(
                wall,
                pawn,
                igniterDef,
                Mode,
                requiredC4,
                range);

            Messages.Message(
                "HD_BreachExplosive_Installed".Translate(
                    requiredC4,
                    range.ToString("0.#")),
                charge,
                MessageTypeDefOf.PositiveEvent,
                false);
        }
    }

    public class JobDriver_TriggerBreachCharge : JobDriver
    {
        private const TargetIndex ChargeInd = TargetIndex.A;

        private ThingWithComps Charge =>
            job.GetTarget(ChargeInd).Thing as ThingWithComps;

        private CompInstalledBreachCharge ChargeComp =>
            Charge?.TryGetComp<CompInstalledBreachCharge>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Charge, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(ChargeInd);
            this.FailOn(() => ChargeComp?.OperatorPawn != pawn);
            this.FailOn(() => ChargeComp == null
                || !ChargeComp.CanTrigger(out _));

            int triggerTicks = Mathf.Max(
                1,
                ChargeComp?.Props.triggerWorkTicks ?? 60);
            Toil trigger = Toils_General.Wait(triggerTicks, ChargeInd);
            trigger.handlingFacing = true;
            trigger.WithProgressBarToilDelay(ChargeInd);
            trigger.tickAction = delegate
            {
                if (Charge != null)
                {
                    pawn.rotationTracker.FaceTarget(Charge);
                }
            };
            yield return trigger;

            yield return Toils_General.Do(() => ChargeComp?.Trigger());
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_BreachExplosive
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = __result.Concat(
                BreachExplosiveUtility.GetPawnGizmos(__instance));
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class Patch_PawnPathFollower_StartPath_BreachTether
    {
        public static bool Prefix(
            Pawn_PathFollower __instance,
            Pawn ___pawn,
            LocalTargetInfo __0)
        {
            CompInstalledBreachCharge charge =
                BreachExplosiveUtility.ActiveChargeFor(___pawn);
            if (charge == null
                || !__0.IsValid
                || charge.IsWithinTether(__0.Cell))
            {
                return true;
            }

            __instance.StopDead();
            Messages.Message(
                "HD_BreachExplosive_TetherBlocked".Translate(
                    charge.TetherRange.ToString("0.#")),
                charge.parent,
                MessageTypeDefOf.RejectInput,
                false);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    public static class Patch_PawnPathFollower_EnterCell_BreachTether
    {
        public static bool Prefix(
            Pawn_PathFollower __instance,
            Pawn ___pawn,
            IntVec3 ___nextCell)
        {
            CompInstalledBreachCharge charge =
                BreachExplosiveUtility.ActiveChargeFor(___pawn);
            if (charge == null
                || !___nextCell.IsValid
                || charge.IsWithinTether(___nextCell))
            {
                return true;
            }

            __instance.StopDead();
            ___pawn?.jobs?.EndCurrentJob(JobCondition.Incompletable);
            return false;
        }
    }
}
