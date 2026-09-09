using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Draws laser and flashlight beams for every aiming pawn on the map.
    //
    // These used to be drawn from the Postfix on PawnRenderUtility.DrawEquipmentAiming, which
    // seemed natural - it already had the weapon's exact draw position and angle. The problem
    // is that hook only runs as part of pawn equipment rendering, and the game does not run
    // that every frame at every zoom level, so beams flickered out when the camera zoomed in
    // or out.
    //
    // MapComponentUpdate runs once per frame regardless, so the transform is recomputed here
    // instead - using the same formula PawnRenderUtility uses to place the gun.
    public class ModularBeamDrawer : MapComponent
    {
        public ModularBeamDrawer(Map map) : base(map)
        {
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();

            // Nothing renders while the map is not the one on screen.
            if (Find.CurrentMap != map) return;

            // IReadOnlyList in current versions - index it directly rather than casting.
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                DrawFor(pawns[i]);
            }
        }

        private static void DrawFor(Pawn pawn)
        {
            // Cheapest checks first: most pawns on a map are not aiming anything.
            Stance_Busy stance = pawn.stances?.curStance as Stance_Busy;
            if (stance == null || stance.neverAimWeapon || !stance.focusTarg.IsValid) return;

            ThingWithComps weapon = pawn.equipment?.Primary;
            CompWeaponModular comp = weapon?.GetComp<CompWeaponModular>();
            if (comp == null) return;

            bool anyLaser = comp.FittedLasers().Count > 0;
            bool anyLight = comp.FittedFlashlights().Count > 0;
            if (!anyLaser && !anyLight) return;

            Vector3 target = stance.focusTarg.HasThing
                ? stance.focusTarg.Thing.DrawPos
                : stance.focusTarg.Cell.ToVector3Shifted();

            Vector3 from = pawn.DrawPos;
            Vector3 dir = target - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            float aimAngle = dir.AngleFlat();

            // Same placement PawnRenderUtility gives the weapon while aiming.
            float distanceFactor = pawn.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;
            Vector3 drawLoc = from
                + new Vector3(0f, 0f, 0.4f + weapon.def.equippedDistanceOffset)
                    .RotatedBy(aimAngle) * distanceFactor;

            // And the same sprite rotation, including the flipped west-facing case.
            float angle = aimAngle - 90f;
            bool flipped = false;

            if (aimAngle > 200f && aimAngle < 340f)
            {
                angle -= 180f;
                angle -= weapon.def.equippedAngleOffset;
                flipped = true;
            }
            else
            {
                angle += weapon.def.equippedAngleOffset;
            }
            angle %= 360f;

            // Whole-weapon offset moves the muzzle too, so the beam origins track it.
            Vector3 body = comp.BodyDrawOffset;
            if (body != Vector3.zero)
            {
                if (flipped) body.x = -body.x;
                drawLoc += body.RotatedBy(angle);
            }

            if (anyLaser) ModularWeaponRenderer.DrawLasers(weapon, comp, drawLoc, angle, flipped);
            if (anyLight) ModularWeaponRenderer.DrawFlashlights(weapon, comp, drawLoc, angle, flipped);
        }
    }
}