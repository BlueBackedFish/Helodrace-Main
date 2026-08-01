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

        public static List<Thing> GrenadeStacks(Pawn pawn)
        {
            return pawn?.inventory?.innerContainer?
                .Where(thing => thing.stackCount > 0 && IsInventoryGrenade(thing.def))
                .ToList() ?? new List<Thing>();
        }

        public static IEnumerable<Gizmo> GetGizmos(Pawn pawn)
        {
            List<Thing> grenades = GrenadeStacks(pawn);
            if (grenades.Count == 0 || pawn?.Faction != Faction.OfPlayer || pawn.Downed)
            {
                yield break;
            }

            Texture2D grenadeIcon = grenades[0].def.uiIcon ?? BaseContent.BadTex;
            yield return new Command_Action
            {
                defaultLabel = "HD_Grenade_CloseThrow".Translate().ToString(),
                defaultDesc = "HD_Grenade_CloseThrowDesc".Translate(
                    CloseThrowTicks.ToStringTicksToPeriod(), CloseThrowRange).ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/AttackMelee", false) ?? grenadeIcon,
                action = () => BeginChooseGrenade(pawn, true)
            };

            yield return new Command_Action
            {
                defaultLabel = "HD_Grenade_NormalThrow".Translate().ToString(),
                defaultDesc = "HD_Grenade_NormalThrowDesc".Translate(
                    NormalThrowTicks.ToStringTicksToPeriod(), NormalThrowRange).ToString(),
                icon = grenadeIcon,
                action = () => BeginChooseGrenade(pawn, false)
            };
        }

        private static void BeginChooseGrenade(Pawn pawn, bool closeThrow)
        {
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
                validator = target => target.IsValid
                    && target.Cell.InBounds(pawn.Map)
                    && target.Cell != pawn.Position
                    && pawn.Position.DistanceTo(target.Cell) <= range
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
                    Color color = distance <= range ? Color.yellow : Color.red;
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
            if (pawn?.Map == null || grenade == null || projectileDef == null)
            {
                return;
            }

            Thing consumed = grenade.SplitOff(1);
            Projectile projectile = GenSpawn.Spawn(projectileDef, pawn.Position, pawn.Map) as Projectile;
            if (projectile != null)
            {
                LocalTargetInfo usedTarget = ScatteredThrowTarget(pawn, target, closeThrow);
                projectile.Launch(
                    pawn,
                    pawn.DrawPos,
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
                || pawn.inventory?.Contains(Grenade) != true
                || !InventoryGrenadeUtility.IsInventoryGrenade(Grenade.def));
            this.FailOn(() => !ThrowTarget.IsValid
                || ThrowTarget.Cell == pawn.Position
                || pawn.Position.DistanceTo(ThrowTarget.Cell) > Range);

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
