using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace Helodrace
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

        internal BodyPartRecord CentreFor(Pawn pawn, DamageDef damage)
        {
            ShotgunTarget target = targets.Find(value => value.pawn == pawn);
            if (target == null)
            {
                target = new ShotgunTarget { pawn = pawn, centre = ShotgunBodyProfile.For(pawn.RaceProps.body).Centre(pawn, damage)?.part };
                targets.Add(target);
            }
            return target.centre;
        }

        internal bool TrySetAimCentre(Pawn pawn, BodyPartRecord part)
        {
            // Only the first pellet's launch may set the shell's aim. Later
            // pellets and in-flight selection changes reuse that same centre.
            if (projectiles.Count != 0 || part == null) return false;
            BodyPartRecord surface = ShotgunBodyProfile.For(pawn.RaceProps.body).Owner(part)?.part;
            if (!ShotgunBallistics.IsPresent(pawn, surface)) return false;
            ShotgunTarget target = targets.Find(value => value.pawn == pawn);
            if (target == null) targets.Add(new ShotgunTarget { pawn = pawn, centre = surface });
            else target.centre = surface;
            return true;
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

    public sealed class MapComponent_ShotgunShells : MapComponent
    {
        private List<ShotgunShell> shells = new List<ShotgunShell>();
        private readonly Dictionary<Projectile, ShotgunShell> byProjectile = new Dictionary<Projectile, ShotgunShell>();
        public MapComponent_ShotgunShells(Map map) : base(map) { }
        internal void Register(Projectile projectile, ShotgunShell shell)
        {
            if (byProjectile.ContainsKey(projectile)) return;
            if (shell.projectiles.Count == 0) shells.Add(shell);
            shell.projectiles.Add(projectile);
            byProjectile.Add(projectile, shell);
        }
        internal ShotgunShell ShellFor(Projectile projectile) => byProjectile.TryGetValue(projectile, out ShotgunShell shell) ? shell : null;
        private void Prune()
        {
            foreach (ShotgunShell shell in shells)
                shell.projectiles.RemoveAll(projectile =>
                {
                    if (projectile != null && !projectile.Destroyed) return false;
                    if (projectile != null) byProjectile.Remove(projectile);
                    return true;
                });
            shells.RemoveAll(shell => shell.projectiles.Count == 0);
        }
        public override void MapComponentTick()
        {
            if (shells.Count > 0 && Find.TickManager.TicksGame % 60 == 0) Prune();
        }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) Prune();
            Scribe_Collections.Look(ref shells, "shotgunShells", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            shells = shells ?? new List<ShotgunShell>();
            shells.RemoveAll(shell => shell == null);
            Prune();
            byProjectile.Clear();
            foreach (ShotgunShell shell in shells)
                foreach (Projectile projectile in shell.projectiles) byProjectile[projectile] = shell;
        }
    }

    internal sealed class ShotgunImpact
    {
        internal ShotgunShell shell;
        internal Projectile_ShotgunPellet pellet;
        internal Pawn pawn;
        internal DamageDef damageDef;
        internal BodyPartRecord surface;
        internal bool selectedSurface;
        internal float relativeSpread;
        internal float energy;
        internal float incomingDamage;
        internal bool internalDamage;
    }

    internal static class ShotgunSpreadContext
    {
        [ThreadStatic] internal static ShotgunLaunch Launching;
        [ThreadStatic] internal static ShotgunImpact Impact;
    }
}
