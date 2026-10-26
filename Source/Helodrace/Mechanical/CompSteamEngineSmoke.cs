using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace Helodrace
{
    public class CompProperties_SteamEngineSmoke : CompProperties
    {
        // Model-space outlet (X, height, depth), before the 64-degree sprite projection.
        public Vector3 smokeOffset = Vector3.zero;

        public CompProperties_SteamEngineSmoke()
        {
            this.compClass = typeof(CompSteamEngineSmoke);
        }
    }

    public class CompSteamEngineSmoke : ThingComp
    {
        private CompRefuelable refuelable;
        private CompMechanicalEmitter emitter;

        public CompProperties_SteamEngineSmoke Props => (CompProperties_SteamEngineSmoke)this.props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            this.refuelable = this.parent.TryGetComp<CompRefuelable>();
            this.emitter = this.parent.TryGetComp<CompMechanicalEmitter>();
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!this.parent.IsHashIntervalTick(30))
            {
                return;
            }

            // Mechanical engines should only smoke while they are actually running.
            bool isRunning = emitter != null
                ? emitter.IsProducingPower
                : refuelable == null || refuelable.HasFuel;
            if (isRunning)
            {
                ThrowSmoke();
            }
        }

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
}
