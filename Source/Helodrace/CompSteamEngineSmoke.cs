using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace Helodrace
{
    public class CompProperties_SteamEngineSmoke : CompProperties
    {
        public Vector3 smokeOffset = new Vector3(-0.3f, 0f, 3f);

        public CompProperties_SteamEngineSmoke()
        {
            this.compClass = typeof(CompSteamEngineSmoke);
        }
    }

    public class CompSteamEngineSmoke : ThingComp
    {
        private CompRefuelable refuelable;

        public CompProperties_SteamEngineSmoke Props => (CompProperties_SteamEngineSmoke)this.props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            this.refuelable = this.parent.TryGetComp<CompRefuelable>();
        }

        public override void CompTick()
        {
            base.CompTick();
            
            // Only smoke if fueled
            if (refuelable == null || refuelable.HasFuel)
            {
                if (this.parent.IsHashIntervalTick(30)) // Every 0.5 seconds
                {
                    ThrowSmoke();
                }
            }
        }

        private void ThrowSmoke()
        {
            // Use Position (bottom-left) as base for tile-based offset
            // ToVector3Shifted() puts it in the center of the bottom-left tile (0,0)
            Vector3 loc = this.parent.Position.ToVector3Shifted();
            
            // Add the offset (e.g., 1, 2)
            loc.x += Props.smokeOffset.x;
            loc.z += Props.smokeOffset.z;
            
            FleckMaker.ThrowSmoke(loc, this.parent.Map, 1.5f);
        }
    }
}
