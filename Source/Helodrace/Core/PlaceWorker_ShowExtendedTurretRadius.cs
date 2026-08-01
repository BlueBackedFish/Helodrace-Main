using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    /// <summary>
    /// Draws turret ranges during placement without relying on GenRadial for
    /// radii larger than its precomputed radial-pattern limit.
    /// </summary>
    public class PlaceWorker_ShowExtendedTurretRadius : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(
            BuildableDef checkingDef,
            IntVec3 loc,
            Rot4 rot,
            Map map,
            Thing thingToIgnore = null,
            Thing thing = null)
        {
            return true;
        }

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            VerbProperties verb = def?.building?.turretGunDef?.Verbs?.Find(IsTurretVerb);
            if (verb == null)
            {
                return;
            }

            // DrawGhost is only used for an actual placement preview. Keeping
            // rendering out of AllowsPlacing prevents AI blueprint checks from
            // showing the player's range overlay during a raid.
            Map map = Find.CurrentMap;
            DrawRange(center, verb.range, map);
            DrawRange(center, verb.minRange, map);
        }

        private static bool IsTurretVerb(VerbProperties verb)
        {
            return verb?.verbClass == typeof(Verb_Shoot)
                || (verb?.verbClass != null && typeof(Verb_Spray).IsAssignableFrom(verb.verbClass));
        }

        private static void DrawRange(IntVec3 center, float radius, Map map)
        {
            if (radius <= 0f)
            {
                return;
            }

            if (radius <= GenRadial.MaxRadialPatternRadius)
            {
                GenDraw.DrawRadiusRing(center, radius);
                return;
            }

            if (map != null && CoversWholeMap(center, radius, map))
            {
                GenDraw.DrawMapBoundaryLines();
                return;
            }

            GenDraw.DrawCircleOutline(center.ToVector3Shifted(), radius);
        }

        private static bool CoversWholeMap(IntVec3 center, float radius, Map map)
        {
            Vector3 origin = center.ToVector3Shifted();
            float maxX = map.Size.x - 0.5f;
            float maxZ = map.Size.z - 0.5f;
            float radiusSquared = radius * radius;

            return (new Vector3(0.5f, 0f, 0.5f) - origin).sqrMagnitude <= radiusSquared
                && (new Vector3(maxX, 0f, 0.5f) - origin).sqrMagnitude <= radiusSquared
                && (new Vector3(0.5f, 0f, maxZ) - origin).sqrMagnitude <= radiusSquared
                && (new Vector3(maxX, 0f, maxZ) - origin).sqrMagnitude <= radiusSquared;
        }
    }
}
