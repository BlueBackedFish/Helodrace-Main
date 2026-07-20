using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace
{
    public interface ICASFlareTarget
    {
        bool IsActiveFlare { get; }
        IntVec3 FlareCell { get; }
        Map FlareMap { get; }
        Thing Launcher { get; }
    }

    public static class CasFlareTargetUtility
    {
        public static IEnumerable<Thing_M8FlareTarget> ActiveFlares(Map map)
        {
            if (map?.listerThings?.AllThings == null)
            {
                return Enumerable.Empty<Thing_M8FlareTarget>();
            }

            return map.listerThings.AllThings.OfType<Thing_M8FlareTarget>()
                .Where(flare => flare.IsActiveFlare);
        }

        public static bool TryFindClosest(Map map, IntVec3 origin,
            out Thing_M8FlareTarget flare)
        {
            flare = ActiveFlares(map)
                .OrderBy(candidate => candidate.Position.DistanceToSquared(origin))
                .FirstOrDefault();
            return flare != null;
        }

        public static bool IsValidTarget(Thing thing)
        {
            return thing is ICASFlareTarget flare && flare.IsActiveFlare;
        }
    }

    public sealed class Projectile_M8Flare : Projectile
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;
            Thing launcher = Launcher;

            base.Impact(hitThing, blockedByShield);

            if (impactMap == null || !impactCell.InBounds(impactMap))
            {
                return;
            }

            Thing_M8FlareTarget existing = impactMap.thingGrid
                .ThingsListAtFast(impactCell)
                .OfType<Thing_M8FlareTarget>()
                .FirstOrDefault();
            if (existing != null)
            {
                existing.Refresh(launcher);
                return;
            }

            ThingDef markerDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_M8FlareTarget");
            Thing_M8FlareTarget marker = markerDef == null
                ? null
                : ThingMaker.MakeThing(markerDef) as Thing_M8FlareTarget;
            if (marker == null)
            {
                Log.ErrorOnce("Helodrace: HD_M8FlareTarget is missing or has the wrong thingClass.", 83008114);
                return;
            }

            marker.Refresh(launcher);
            GenSpawn.Spawn(marker, impactCell, impactMap);
        }
    }

    public sealed class Thing_M8FlareTarget : ThingWithComps, ICASFlareTarget
    {
        public const int LifetimeTicks = 1800;

        private int ticksRemaining = LifetimeTicks;
        private Thing launcher;

        public bool IsActiveFlare => Spawned && !Destroyed && ticksRemaining > 0;
        public IntVec3 FlareCell => Position;
        public Map FlareMap => Map;
        public Thing Launcher => launcher;

        public void Refresh(Thing newLauncher)
        {
            ticksRemaining = LifetimeTicks;
            launcher = newLauncher;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", LifetimeTicks);
            Scribe_References.Look(ref launcher, "launcher");
        }

        protected override void Tick()
        {
            base.Tick();
            ticksRemaining--;
            if (ticksRemaining <= 0)
            {
                Destroy(DestroyMode.Vanish);
                return;
            }

            if (Map != null && this.IsHashIntervalTick(45))
            {
                FleckMaker.ThrowSmoke(Position.ToVector3Shifted(), Map, 0.35f);
            }
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            GenDraw.DrawRadiusRing(Position, 1.5f);
        }

        public override string GetInspectString()
        {
            string inspect = base.GetInspectString();
            string flareLine = "HD_M8Flare_TimeRemaining".Translate(
                ticksRemaining.ToStringTicksToPeriod()).ToString();
            return inspect.NullOrEmpty() ? flareLine : inspect + "\n" + flareLine;
        }
    }
}
