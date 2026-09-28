using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Laser emitter carried by a part. Attach one to each rail variant (top, bottom, side);
    // each renders its own beam from its own origin, so a weapon can legitimately carry more
    // than one.
    public class LaserSightProps
    {
        public Color color = new Color(1f, 0.15f, 0.15f, 0.75f);

        public float beamWidth = 0.06f;

        // Dot painted where the beam lands, in world cells.
        public float dotSize = 0.22f;
        public bool drawDot = true;

        // Cap so a beam aimed across the map does not stretch forever if the target is
        // somehow far outside weapon range.
        public float maxLength = 80f;
    }
}
