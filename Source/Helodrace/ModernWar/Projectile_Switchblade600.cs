using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class Switchblade600ProjectileExtension : DefModExtension
    {
        public int smokeTrailIntervalTicks = 12;
        public float smokeTrailSize = 0.45f;
    }

    public class Projectile_Switchblade600 : Projectile_Explosive
    {
        private int ticksSinceLaunch;

        protected override void Tick()
        {
            base.Tick();
            ticksSinceLaunch++;

            Switchblade600ProjectileExtension extension = def.GetModExtension<Switchblade600ProjectileExtension>();
            int interval = Mathf.Max(1, extension?.smokeTrailIntervalTicks ?? 12);
            if (ticksSinceLaunch % interval == 0 && Map != null)
            {
                float size = Mathf.Max(0.1f, extension?.smokeTrailSize ?? 0.45f);
                FleckMaker.ThrowSmoke(Position.ToVector3Shifted(), Map, size);
            }
        }
    }
}
