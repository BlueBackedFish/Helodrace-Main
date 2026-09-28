using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Per-weapon placement override. A suppressor sits at a different spot on a bullpup
    // than on a full-length rifle, so the weapon - not the part - has the final say.
    //
    // Used in two places:
    //   * CompProperties_WeaponModular.drawOverrides - the WEAPON placing parts on itself
    //   * WeaponPartDef.childOffsets - a PART placing OTHER parts (a long barrel moving the
    //     muzzle device forward, since the muzzle sits at the end of whatever barrel is on)
    //
    // Replacement priority (first match wins):
    //   1) weapon override matching the exact part
    //   2) child offset (from another installed part) matching the exact part
    //   3) child offset matching the part's slot
    //   4) weapon override matching the part's slot
    //   5) WeaponPartDef.drawOffset (the part's own default)
    //
    // Every additive entry that matches is then summed on top of that result.
    public class WeaponPartDrawOverride
    {
        public WeaponPartSlotDef slot;
        public WeaponPartDef part;

        public Vector3 offset = Vector3.zero;
        public float scale = 1f;
        public float angleOffset = 0f;

        // false (default) = REPLACE the target's placement outright.
        // true            = ADD on top of whatever was resolved, so a long barrel can simply
        //                   push the muzzle forward by +0.2 without restating its position.
        public bool additive = false;

        // Where this part's laser beam starts, if it has one. Sentinel so an override that
        // only sets <offset> does not silently reset the emitter to the sprite centre.
        public static readonly Vector3 UnsetOffset = new Vector3(-99999f, -99999f, -99999f);
        public Vector3 laserOffset = UnsetOffset;

        public bool HasLaserOffset => laserOffset.x > UnsetOffset.x + 1f;

        // Beam thickness and landing-dot size. Sentinels again: an override that only moves
        // the emitter must not reset the beam's look to zero.
        public float laserWidth = Unset;
        public float laserDot = Unset;

        public bool HasLaserWidth => laserWidth > Unset + 1f;
        public bool HasLaserDot => laserDot > Unset + 1f;

        // Weapon light: emitter position, cone spread, target ring size.
        public Vector3 lightOffset = UnsetOffset;
        public float lightCone = Unset;
        public float lightRadius = Unset;

        public bool HasLightOffset => lightOffset.x > UnsetOffset.x + 1f;
        public bool HasLightCone => lightCone > Unset + 1f;
        public bool HasLightRadius => lightRadius > Unset + 1f;

        public float lightCircle = Unset;
        public bool HasLightCircle => lightCircle > Unset + 1f;

        // Draw order among overlays. Higher = drawn on top; negative tucks under the gun.
        //
        // Omitting <layer> must mean "keep whatever the part already says"
        // (WeaponPartDef.drawLayer) - with a plain 0 default, a weapon that overrode only
        // <offset> silently reset every part's layer to 0.
        //
        // A sentinel is used rather than float? because nullable primitives are not reliably
        // parsed by the def loader; this works regardless.
        public const float Unset = -99999f;
        public float layer = Unset;

        public bool HasLayer => layer > Unset + 1f;

        public bool Matches(WeaponPartDef p)
        {
            if (part != null) return part == p;
            if (slot != null) return p.slot == slot;
            return false;
        }
    }

    // Resolved, ready-to-draw placement for one fitted part.
    public struct PartDrawData
    {
        public Vector3 offset;
        public float scale;
        public float angleOffset;
        public float layer;
        public Vector3 laserOffset;
        public float laserWidth;
        public float laserDot;
        public Vector3 lightOffset;
        public float lightCone;
        public float lightRadius;
        public float lightCircle;
    }
}