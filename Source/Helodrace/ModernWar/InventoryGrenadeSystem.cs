using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public static class InventoryGrenadeUtility
    {
        public const float CloseThrowRange = 5.9f;
        public const float NormalThrowRange = 13.9f;
        public const int CloseThrowTicks = 90;
        public const int NormalThrowTicks = 180;
        private const float CloseThrowMinimumMissRadius = 0.08f;
        private const float CloseThrowMaximumMissRadius = 1.15f;
        private const float NormalThrowMinimumMissRadius = 0.16f;
        private const float NormalThrowMaximumMissRadius = 2.4f;
        private static readonly IntVec3[] LeanThrowOffsets =
        {
            new IntVec3(0, 0, 1),
            new IntVec3(1, 0, 0),
            new IntVec3(0, 0, -1),
            new IntVec3(-1, 0, 0)
        };

        private static ThingCategoryDef GrenadeCategory =>
            DefDatabase<ThingCategoryDef>.GetNamedSilentFail("HD_InventoryGrenades");

        public static bool IsInventoryGrenade(ThingDef def)
        {
            ThingCategoryDef category = GrenadeCategory;
            return def != null
                && def.projectileWhenLoaded != null
                && category != null
                && def.IsWithinCategory(category);
        }

        public static bool CanUseGrenades(Pawn pawn)
        {
            return pawn != null && !pawn.WorkTagIsDisabled(WorkTags.Violent);
        }

        public static List<Thing> GrenadeStacks(Pawn pawn)
        {
            return pawn?.inventory?.innerContainer?
                .Where(thing => thing.stackCount > 0 && IsInventoryGrenade(thing.def))
                .ToList() ?? new List<Thing>();
        }

        public static bool CanThrowAt(Pawn pawn, IntVec3 targetCell, float range)
        {
            return CanUseGrenades(pawn)
                && TryFindThrowSource(pawn, targetCell, range, out _);
        }

        public static bool TryFindThrowSource(
            Pawn pawn,
            IntVec3 targetCell,
            float range,
            out IntVec3 sourceCell)
        {
            sourceCell = IntVec3.Invalid;
            Map map = pawn?.Map;
            if (map == null
                || !CanUseGrenades(pawn)
                || !pawn.Spawned
                || !targetCell.IsValid
                || !targetCell.InBounds(map)
                || targetCell == pawn.Position
                || pawn.Position.DistanceTo(targetCell) > range)
            {
                return false;
            }

            if (GenSight.LineOfSight(pawn.Position, targetCell, map, true))
            {
                sourceCell = pawn.Position;
                return true;
            }

            int bestDistanceSquared = int.MaxValue;
            for (int i = 0; i < LeanThrowOffsets.Length; i++)
            {
                IntVec3 candidate = pawn.Position + LeanThrowOffsets[i];
                if (!candidate.InBounds(map)
                    || !candidate.Standable(map)
                    || !GenSight.LineOfSight(pawn.Position, candidate, map, true)
                    || (candidate != targetCell
                        && !GenSight.LineOfSight(candidate, targetCell, map, true)))
                {
                    continue;
                }

                int distanceSquared = candidate.DistanceToSquared(targetCell);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    sourceCell = candidate;
                }
            }

            return sourceCell.IsValid;
        }

        public static IEnumerable<Gizmo> GetGizmos(Pawn pawn)
        {
            List<Thing> grenades = GrenadeStacks(pawn);
            if (grenades.Count == 0 || pawn?.Faction != Faction.OfPlayer || pawn.Downed)
            {
                yield break;
            }

            Texture2D grenadeIcon = grenades[0].def.uiIcon ?? BaseContent.BadTex;
            Texture2D closeThrowIcon =
                ContentFinder<Texture2D>.Get("Skill/HD_ThrowShort", false) ?? grenadeIcon;
            Texture2D normalThrowIcon =
                ContentFinder<Texture2D>.Get("Skill/HD_ThrowLong", false) ?? grenadeIcon;
            Command_Action closeCommand = new Command_Action
            {
                defaultLabel = "HD_Grenade_CloseThrow".Translate().ToString(),
                defaultDesc = "HD_Grenade_CloseThrowDesc".Translate(
                    CloseThrowTicks.ToStringTicksToPeriod(), CloseThrowRange).ToString(),
                icon = closeThrowIcon,
                action = () => BeginChooseGrenade(pawn, true)
            };
            Command_Action normalCommand = new Command_Action
            {
                defaultLabel = "HD_Grenade_NormalThrow".Translate().ToString(),
                defaultDesc = "HD_Grenade_NormalThrowDesc".Translate(
                    NormalThrowTicks.ToStringTicksToPeriod(), NormalThrowRange).ToString(),
                icon = normalThrowIcon,
                action = () => BeginChooseGrenade(pawn, false)
            };

            if (!CanUseGrenades(pawn))
            {
                string reason = "IsIncapableOfViolenceShort".Translate();
                closeCommand.Disable(reason);
                normalCommand.Disable(reason);
            }

            yield return closeCommand;
            yield return normalCommand;
        }

        private static void BeginChooseGrenade(Pawn pawn, bool closeThrow)
        {
            if (!CanUseGrenades(pawn))
            {
                Messages.Message(
                    "IsIncapableOfViolence".Translate(pawn.Named("PAWN")),
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            List<Thing> grenades = GrenadeStacks(pawn)
                .GroupBy(thing => thing.def)
                .Select(group => group.First())
                .OrderBy(thing => thing.def.label)
                .ToList();

            if (grenades.Count == 0)
            {
                Messages.Message("HD_Grenade_None".Translate(), pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (grenades.Count == 1)
            {
                BeginTargeting(pawn, grenades[0].def, closeThrow);
                return;
            }

            List<FloatMenuOption> options = grenades.Select(grenade =>
            {
                int count = pawn.inventory.Count(grenade.def);
                return new FloatMenuOption(
                    "HD_Grenade_SelectEntry".Translate(grenade.def.LabelCap, count),
                    () => BeginTargeting(pawn, grenade.def, closeThrow),
                    grenade.def);
            }).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void BeginTargeting(Pawn pawn, ThingDef grenadeDef, bool closeThrow)
        {
            float range = closeThrow ? CloseThrowRange : NormalThrowRange;
            TargetingParameters parameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetItems = true,
                validator = target => CanThrowAt(pawn, target.Cell, range)
            };

            System.Action<LocalTargetInfo> drawPreview = target =>
            {
                if (pawn.Map == null)
                {
                    return;
                }

                GenDraw.DrawRadiusRing(pawn.Position, range, Color.white);
                if (target.IsValid && target.Cell.InBounds(pawn.Map))
                {
                    float distance = pawn.Position.DistanceTo(target.Cell);
                    float missRadius = ThrowMissRadius(pawn, closeThrow, distance);
                    Color color = CanThrowAt(pawn, target.Cell, range) ? Color.yellow : Color.red;
                    GenDraw.DrawRadiusRing(target.Cell, Mathf.Max(0.1f, missRadius), color);
                }
            };

            Find.Targeter.BeginTargeting(
                parameters,
                target =>
                {
                    Thing grenade = GrenadeStacks(pawn).FirstOrDefault(thing => thing.def == grenadeDef);
                    if (grenade == null)
                    {
                        Messages.Message("HD_Grenade_NoneOfType".Translate(grenadeDef.LabelCap), pawn,
                            MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(
                        closeThrow ? "HD_ThrowInventoryGrenadeClose" : "HD_ThrowInventoryGrenadeNormal");
                    if (jobDef == null)
                    {
                        return;
                    }

                    Job job = JobMaker.MakeJob(jobDef, target, grenade);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                },
                drawPreview);
            MapComponent_PersistentTargetingOverlay.Set(pawn.Map, drawPreview);
        }

        public static float ThrowMissRadius(Pawn pawn, bool closeThrow, float distance)
        {
            int shootingLevel = pawn?.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
            float skillFactor = 1f - Mathf.Clamp01(shootingLevel / 20f);
            float minimumRadius = closeThrow
                ? CloseThrowMinimumMissRadius
                : NormalThrowMinimumMissRadius;
            float maximumRadius = closeThrow
                ? CloseThrowMaximumMissRadius
                : NormalThrowMaximumMissRadius;
            float range = closeThrow ? CloseThrowRange : NormalThrowRange;
            float distanceFactor = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(distance / range));
            return Mathf.Lerp(minimumRadius, maximumRadius, Mathf.Pow(skillFactor, 1.15f))
                * distanceFactor;
        }

        private static LocalTargetInfo ScatteredThrowTarget(
            Pawn pawn,
            LocalTargetInfo intendedTarget,
            bool closeThrow)
        {
            float distance = pawn.Position.DistanceTo(intendedTarget.Cell);
            float missRadius = ThrowMissRadius(pawn, closeThrow, distance);
            Vector2 missOffset = Rand.InsideUnitCircle * missRadius;
            Vector3 scatteredPosition = intendedTarget.CenterVector3
                + new Vector3(missOffset.x, 0f, missOffset.y);
            IntVec3 scatteredCell = scatteredPosition.ToIntVec3();
            return scatteredCell.InBounds(pawn.Map)
                ? new LocalTargetInfo(scatteredCell)
                : intendedTarget;
        }

        public static void Launch(
            Pawn pawn,
            Thing grenade,
            LocalTargetInfo target,
            bool closeThrow)
        {
            ThingDef projectileDef = grenade?.def?.projectileWhenLoaded;
            float range = closeThrow ? CloseThrowRange : NormalThrowRange;
            if (grenade == null
                || !CanUseGrenades(pawn)
                || projectileDef == null
                || !target.IsValid
                || !TryFindThrowSource(pawn, target.Cell, range, out IntVec3 sourceCell))
            {
                return;
            }

            Thing consumed = grenade.SplitOff(1);
            Projectile projectile = GenSpawn.Spawn(projectileDef, sourceCell, pawn.Map) as Projectile;
            if (projectile != null)
            {
                LocalTargetInfo usedTarget = ScatteredThrowTarget(pawn, target, closeThrow);
                Vector3 throwOrigin = pawn.DrawPos;
                if (sourceCell != pawn.Position)
                {
                    Vector3 leanDirection = sourceCell.ToVector3Shifted() - pawn.DrawPos;
                    leanDirection.y = 0f;
                    throwOrigin += leanDirection.normalized * 0.65f;
                }

                projectile.Launch(
                    pawn,
                    throwOrigin,
                    usedTarget,
                    target,
                    ProjectileHitFlags.All,
                    false,
                    null);

                if (projectile is Helodrace.ModernWar.Projectile_ModernGrenade modernGrenade)
                {
                    modernGrenade.ConfigureInventoryThrow(closeThrow);
                }
            }

            consumed.Destroy(DestroyMode.Vanish);
        }
    }

    public sealed class JobDriver_ThrowInventoryGrenade : JobDriver
    {
        private const TargetIndex TargetInd = TargetIndex.A;
        private const TargetIndex GrenadeInd = TargetIndex.B;

        private LocalTargetInfo ThrowTarget => job.GetTarget(TargetInd);
        private Thing Grenade => job.GetTarget(GrenadeInd).Thing;
        private bool CloseThrow => job.def.defName == "HD_ThrowInventoryGrenadeClose";
        private float Range => CloseThrow
            ? InventoryGrenadeUtility.CloseThrowRange
            : InventoryGrenadeUtility.NormalThrowRange;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Grenade == null
                || !InventoryGrenadeUtility.CanUseGrenades(pawn)
                || pawn.inventory?.Contains(Grenade) != true
                || !InventoryGrenadeUtility.IsInventoryGrenade(Grenade.def));
            this.FailOn(() => !ThrowTarget.IsValid
                || !InventoryGrenadeUtility.CanThrowAt(pawn, ThrowTarget.Cell, Range));

            int preparationTicks = CloseThrow
                ? InventoryGrenadeUtility.CloseThrowTicks
                : InventoryGrenadeUtility.NormalThrowTicks;
            Toil prepare = Toils_General.Wait(preparationTicks, TargetInd);
            prepare.WithProgressBarToilDelay(TargetInd);
            yield return prepare;

            yield return new Toil
            {
                initAction = () => InventoryGrenadeUtility.Launch(
                    pawn,
                    Grenade,
                    ThrowTarget,
                    CloseThrow),
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
