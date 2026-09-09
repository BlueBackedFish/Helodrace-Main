using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    public class CompProperties_WeaponModular : CompProperties
    {
        // Slots this weapon exposes.
        public List<WeaponPartSlotDef> slots = new List<WeaponPartSlotDef>();

        // Parts installed by default at spawn (before any AI randomization).
        // Required slots should be covered here (or via the slot's defaultPart), otherwise
        // freshly made weapons spawn incomplete.
        public List<WeaponPartDef> defaultParts = new List<WeaponPartDef>();

        // --- Muzzle flash ---
        // Spawned from the WEAPON rather than the projectile, so barrel length and muzzle
        // devices can move and change it. Put the effecter here instead of on the bullet's
        // CompProperties_ProjectileEffecter, and set the EffecterDef's own
        // offsetTowardsTarget to 0 - the distance below positions it instead.
        public EffecterDef muzzleFlashEffecter;

        // Distance from the pawn to the barrel tip, in cells (what offsetTowardsTarget did).
        public float muzzleFlashDistance = 1.6f;

        public float muzzleFlashScale = 1f;

        // Ties part size AND offsets to the weapon's own drawSize.
        //
        // Set this to the weapon drawSize you tuned the offsets at (e.g. 1.4). From then on,
        // changing the weapon's graphicData drawSize scales every attachment and every offset
        // by the same ratio, so shrinking the gun shrinks the whole assembly instead of
        // leaving the attachments floating at their old size and distance.
        //
        // Leave at 0 to disable (parts keep their literal drawSize / offsets).
        public float partScaleReference = 0f;

        // Per-weapon placement overrides for part overlays. See WeaponPartDrawOverride.
        public List<WeaponPartDrawOverride> drawOverrides = new List<WeaponPartDrawOverride>();

        // Shifts the ENTIRE weapon - body and every overlay together - in sprite space.
        // Vanilla places the body; this nudges the whole assembly after the fact so the gun
        // can be recentred in the pawn's hands or on the ground without editing the texture.
        // x = along the barrel, z = perpendicular. Scaled by partScaleReference like offsets.
        public Vector3 bodyDrawOffset = Vector3.zero;

        // (No base sound here on purpose. The weapon keeps its normal verb <soundCast> in
        // XML; a fitted part only overrides it when that part says so - e.g. a suppressor.)

        public CompProperties_WeaponModular()
        {
            compClass = typeof(CompWeaponModular);
        }
    }
}