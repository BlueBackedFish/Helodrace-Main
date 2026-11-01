// Frozen from e94e51db; geometry/pose/save subset; unchanged anatomy selection is covered by product regressions.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace Helodrace.Tests.Frozen
{
    internal struct ShotgunPose
    {
        internal int tick;
        internal Vector3 position;
        internal Vector2 radii;
    }

    public sealed class ShotgunTarget : IExposable
    {
        public Pawn pawn;
        public BodyPartRecord centre;
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_BodyParts.Look(ref centre, "centre");
        }
    }

    public sealed class ShotgunShell : IExposable
    {
        public int id;
        public Vector3 origin;
        public Vector3 targetPoint;
        public Vector2 aimError;
        public List<Projectile> projectiles = new List<Projectile>();
        private List<ShotgunTarget> targets = new List<ShotgunTarget>();
        private readonly Dictionary<Pawn, ShotgunPose> poses = new Dictionary<Pawn, ShotgunPose>();

        internal ShotgunPose PoseFor(Pawn pawn)
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            if (poses.TryGetValue(pawn, out ShotgunPose pose) && pose.tick == tick) return pose;
            // A hit may interrupt a job and reset path progress immediately.
            // All pellets arriving in this tick must see the same physical
            // pose, while armor and injuries still resolve independently.
            pose = new ShotgunPose { tick = tick, position = ShotgunBallistics.Position(pawn),
                radii = ShotgunBallistics.BodyRadii(pawn.BodySize, pawn.GetPosture() == PawnPosture.Standing) };
            poses[pawn] = pose;
            return pose;
        }

        internal Vector3 Forward
        {
            get
            {
                Vector3 delta = targetPoint - origin;
                delta.y = 0f;
                return delta.sqrMagnitude > 0.001f ? delta.normalized : new Vector3(0f, 0f, 1f);
            }
        }
        internal Vector3 Right => new Vector3(Forward.z, 0f, -Forward.x);
        internal float AimDistance => Math.Max(0.1f, (targetPoint - origin).MagnitudeHorizontal());
        internal float DistanceTo(Vector3 point) => Math.Max(0f, Vector3.Dot(point - origin, Forward));
        internal Vector2 OffsetAt(Vector3 point, Vector2 slope)
        {
            float distance = DistanceTo(point);
            Vector2 offset = aimError * (distance / AimDistance) + slope * distance;
            offset.x -= Vector3.Dot(point - origin, Right);
            return offset;
        }





        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref origin.x, "originX");
            Scribe_Values.Look(ref origin.y, "originY");
            Scribe_Values.Look(ref origin.z, "originZ");
            Scribe_Values.Look(ref targetPoint.x, "targetX");
            Scribe_Values.Look(ref targetPoint.y, "targetY");
            Scribe_Values.Look(ref targetPoint.z, "targetZ");
            Scribe_Values.Look(ref aimError.x, "aimX");
            Scribe_Values.Look(ref aimError.y, "aimY");
            Scribe_Collections.Look(ref projectiles, "projectiles", LookMode.Reference);
            Scribe_Collections.Look(ref targets, "targets", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                projectiles = projectiles ?? new List<Projectile>();
                targets = targets ?? new List<ShotgunTarget>();
                targets.RemoveAll(target => target?.pawn == null);
            }
        }
    }

}
