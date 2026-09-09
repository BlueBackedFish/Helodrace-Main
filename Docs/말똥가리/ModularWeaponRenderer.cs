using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Draws part overlays on a held weapon, mirroring PawnRenderUtility.DrawEquipmentAiming
    // exactly (mesh choice, equippedAngleOffset, recoil) so overlays stay locked to the gun.
    public static class ModularWeaponRenderer
    {
        public static void DrawPartOverlays(Thing eq, Vector3 drawLoc, float aimAngle)
        {
            CompWeaponModular.HeldPatchCalls++;

            var comp = (eq as ThingWithComps)?.GetComp<CompWeaponModular>();
            if (comp == null || !comp.HasDrawableParts) return;   // cheap per-frame bail-out

            // --- replicate vanilla mesh + angle selection ---
            float angle = aimAngle - 90f;
            Mesh mesh;
            bool flipped = false;

            if (aimAngle > 20f && aimAngle < 160f)
            {
                mesh = MeshPool.plane10;
                angle += eq.def.equippedAngleOffset;
            }
            else if (aimAngle > 200f && aimAngle < 340f)
            {
                mesh = MeshPool.plane10Flip;
                angle -= 180f;
                angle -= eq.def.equippedAngleOffset;
                flipped = true;
            }
            else
            {
                mesh = MeshPool.plane10;
                angle += eq.def.equippedAngleOffset;
            }
            angle %= 360f;

            // --- recoil ---
            // IMPORTANT: drawLoc already contains the recoil translation. Vanilla
            // DrawEquipmentAiming mutates its own drawLoc parameter (`drawLoc += b;`), and a
            // Harmony Postfix receives parameters as they are at method exit - not the
            // original arguments. Adding EquipmentUtility.Recoil's offset again here made the
            // attachments kick twice as far as the gun they are bolted to.
            //
            // The ANGLE still has to be re-applied, because `angle` above is recomputed from
            // aimAngle (which vanilla does not mutate) rather than inherited.
            CompEquippable eqComp = eq.TryGetComp<CompEquippable>();
            if (eqComp != null)
            {
                Vector3 unusedOffset;
                float recoilAngle;
                EquipmentUtility.Recoil(eq.def, EquipmentUtility.GetRecoilVerb(eqComp.AllVerbs),
                    out unusedOffset, out recoilAngle, aimAngle);
                angle += recoilAngle;
            }

            int meshLayer = 0;

            foreach (var kv in comp.FittedPartsForDrawing())
            {
                WeaponPartDef part = kv.Key;
                PartDrawData data = kv.Value;

                // Offset is authored in weapon-local space: x = along barrel, z = up on sprite.
                // Mirror x when the flipped mesh is used, then rotate into world space.
                Vector3 local = data.offset;
                if (flipped) local.x = -local.x;
                Vector3 pos = drawLoc + local.RotatedBy(angle);

                // Signed: negative layer draws the part beneath the weapon body.
                pos.y += data.layer * CompWeaponModular.LayerAltitudeStep;

                CompWeaponModular.HeldDrawCalls++;
                Material mat = part.Graphic.MatSingleFor(eq);
                Vector3 size = new Vector3(
                    part.Graphic.drawSize.x * data.scale, 0f,
                    part.Graphic.drawSize.y * data.scale);

                Matrix4x4 matrix = Matrix4x4.TRS(
                    pos, Quaternion.AngleAxis(angle + data.angleOffset, Vector3.up), size);

                Graphics.DrawMesh(mesh, matrix, mat, meshLayer);
            }

            // NOTE: laser and flashlight beams are NOT drawn here. This hook only runs as
            // part of pawn equipment rendering, which the game skips at some zoom levels -
            // that is why beams vanished when zooming in or out. They are drawn from
            // ModularBeamDrawer (a MapComponent) instead, which updates every frame.
        }

        // Beams are drawn only while the wielder is actually aiming at something: an idle
        // pawn carrying the weapon shows nothing. Each laser part emits its own beam from its
        // own origin, so a top-rail and an under-rail unit both appear.
        public static void DrawLasers(Thing eq, CompWeaponModular comp,
            Vector3 drawLoc, float angle, bool flipped)
        {
            Pawn pawn = (eq.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            if (pawn == null || !pawn.Spawned) return;

            Stance_Busy stance = pawn.stances?.curStance as Stance_Busy;
            if (stance == null || stance.neverAimWeapon || !stance.focusTarg.IsValid) return;

            var lasers = comp.FittedLasers();
            if (lasers.Count == 0) return;

            Vector3 target = stance.focusTarg.HasThing
                ? stance.focusTarg.Thing.DrawPos
                : stance.focusTarg.Cell.ToVector3Shifted();

            foreach (var kv in lasers)
            {
                LaserSightProps laser = kv.Key.laser;
                PartDrawData data = kv.Value;

                // Emitter position = where the SIGHT BODY ended up, plus the emitter's own
                // offset within that body.
                //
                // laserOffset is relative to the part, not to the weapon: the diode is bolted
                // to the sight, so when a handguard's childOffsets move the sight forward the
                // beam has to move with it. Anchoring to data.offset makes that automatic and
                // keeps handguard defs down to a single <offset> per rail.
                Vector3 local = data.offset + data.laserOffset;
                if (flipped) local.x = -local.x;

                Vector3 origin = drawLoc + local.RotatedBy(angle);
                origin.y = AltitudeLayer.MoteOverhead.AltitudeFor();

                Vector3 end = target;
                end.y = origin.y;

                Vector3 delta = end - origin;
                delta.y = 0f;
                if (delta.sqrMagnitude < 0.0001f) continue;

                if (delta.magnitude > laser.maxLength)
                    end = origin + delta.normalized * laser.maxLength;

                Material mat = LaserMaterial(laser.color);
                GenDraw.DrawLineBetween(origin, end, mat, Mathf.Max(0.005f, data.laserWidth));

                if (!laser.drawDot) continue;

                float dotSize = Mathf.Max(0.01f, data.laserDot);
                Matrix4x4 dot = Matrix4x4.TRS(end, Quaternion.identity,
                    new Vector3(dotSize, 1f, dotSize));
                Graphics.DrawMesh(MeshPool.plane10, dot, mat, 0);
            }
        }

        // Cone of light down the aim line, plus a ring on the target. Purely visual - the
        // dazzle debuff is applied on tick (see Patch_EquipmentTrackerTick), because rendering
        // runs many times per tick and not at all for off-screen pawns.
        public static void DrawFlashlights(Thing eq, CompWeaponModular comp,
            Vector3 drawLoc, float angle, bool flipped)
        {
            Pawn pawn = (eq.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            if (pawn == null || !pawn.Spawned) return;

            Stance_Busy stance = pawn.stances?.curStance as Stance_Busy;
            if (stance == null || stance.neverAimWeapon || !stance.focusTarg.IsValid) return;

            var lights = comp.FittedFlashlights();
            if (lights.Count == 0) return;

            Vector3 target = stance.focusTarg.HasThing
                ? stance.focusTarg.Thing.DrawPos
                : stance.focusTarg.Cell.ToVector3Shifted();

            foreach (var kv in lights)
            {
                FlashlightProps light = kv.Key.flashlight;
                PartDrawData data = kv.Value;

                Vector3 local = data.offset + data.lightOffset;
                if (flipped) local.x = -local.x;

                Vector3 origin = drawLoc + local.RotatedBy(angle);
                origin.y = AltitudeLayer.MoteOverhead.AltitudeFor();

                Vector3 delta = target - origin;
                delta.y = 0f;
                float length = delta.magnitude;
                if (length < 0.05f) continue;

                // Out of working range: draw nothing at all rather than a beam that stops in
                // mid-air short of the target.
                if (length > light.maxRange) continue;

                Quaternion rot = Quaternion.AngleAxis(delta.AngleFlat(), Vector3.up);

                float near = data.lightCone > 0f ? data.lightCone : light.nearWidth;
                float far = data.lightRadius > 0f ? data.lightRadius : light.farWidth;
                float circle = data.lightCircle > 0f ? data.lightCircle : light.circleRadius;

                // Close range: taper the wide end with distance so the shape keeps its
                // proportions. At full range nothing changes.
                if (light.closeRange > 0.01f && length < light.closeRange)
                {
                    float t = Mathf.Max(light.closeRangeMinScale, length / light.closeRange);
                    far *= t;
                    circle *= t;
                }

                // The emitter end can end up wider than the far end once tapered, which turns
                // the beam inside out - clamp it.
                near = Mathf.Min(near, far);

                // Shaft: trapezoid, stretched to reach. Only the far width is adjustable, so
                // it can be splayed like a fan without touching the emitter end.
                if (!light.coneTexPath.NullOrEmpty())
                {
                    Material coneMat = MaterialPool.MatFrom(
                        light.coneTexPath, ShaderDatabase.MoteGlow, light.color);

                    // Overshoot compensates for a texture whose glow fades before its top
                    // edge; the mesh itself already ends on the target's centre.
                    float shaftLength = Mathf.Max(0.05f, length + light.lengthOvershoot);

                    Graphics.DrawMesh(ConeMesh(near, far),
                        Matrix4x4.TRS(origin, rot, new Vector3(1f, 1f, shaftLength)),
                        coneMat, 0);
                }

                // Head: its own round texture, constant size, sitting at the far end.
                if (!light.drawCircle || circle <= 0f || light.circleTexPath.NullOrEmpty())
                    continue;

                // Straight onto the target rather than projected along the beam. The two are
                // the same when nothing interferes, but this stays correct if the shaft is
                // ever shortened or the emitter sits off-axis, and it keeps the pool centred
                // on what is actually being aimed at.
                Vector3 pool = target;
                pool.y = origin.y + 0.001f;

                Material poolMat = MaterialPool.MatFrom(
                    light.circleTexPath, ShaderDatabase.MoteGlow, light.color);

                float dia = circle * 2f;
                Graphics.DrawMesh(MeshPool.plane10,
                    Matrix4x4.TRS(pool, Quaternion.identity, new Vector3(dia, 1f, dia)),
                    poolMat, 0);
            }
        }

        // Trapezoid in the XZ plane: near edge at z=0, far edge at z=1, so only the Z scale
        // changes with range.
        //
        // SUBDIVIDED ON PURPOSE. A trapezoid drawn as two triangles maps its texture wrong:
        // UVs interpolate linearly per triangle, but the quad's width does not, so the image
        // visibly creases along the shared diagonal - the beam looked bent. Splitting it into
        // rows (and mirroring the diagonals about a centre column) shrinks each triangle's
        // width delta until the distortion is invisible.
        private static readonly Dictionary<long, Mesh> coneMeshes = new Dictionary<long, Mesh>();

        private const int ConeRows = 10;

        private static Mesh ConeMesh(float nearWidth, float farWidth)
        {
            int nk = Mathf.RoundToInt(nearWidth * 100f);
            int fk = Mathf.RoundToInt(farWidth * 100f);
            long key = ((long)nk << 32) | (uint)fk;

            Mesh mesh;
            if (coneMeshes.TryGetValue(key, out mesh)) return mesh;

            float n = nk / 100f * 0.5f;
            float f = fk / 100f * 0.5f;

            // Three columns per row: left, centre, right. The centre column keeps the
            // triangulation symmetric, so whatever distortion remains is mirrored rather than
            // running diagonally across the beam.
            int rows = ConeRows + 1;
            var verts = new Vector3[rows * 3];
            var uvs = new Vector2[rows * 3];

            for (int r = 0; r < rows; r++)
            {
                float t = (float)r / ConeRows;
                float half = Mathf.Lerp(n, f, t);
                int i = r * 3;

                verts[i] = new Vector3(-half, 0f, t);
                verts[i + 1] = new Vector3(0f, 0f, t);
                verts[i + 2] = new Vector3(half, 0f, t);

                uvs[i] = new Vector2(0f, t);
                uvs[i + 1] = new Vector2(0.5f, t);
                uvs[i + 2] = new Vector2(1f, t);
            }

            var tris = new int[ConeRows * 4 * 3];
            int ti = 0;
            for (int r = 0; r < ConeRows; r++)
            {
                int a = r * 3;
                int b = (r + 1) * 3;

                // left half
                tris[ti++] = a; tris[ti++] = b; tris[ti++] = b + 1;
                tris[ti++] = a; tris[ti++] = b + 1; tris[ti++] = a + 1;

                // right half, mirrored
                tris[ti++] = a + 1; tris[ti++] = b + 1; tris[ti++] = b + 2;
                tris[ti++] = a + 1; tris[ti++] = b + 2; tris[ti++] = a + 2;
            }

            mesh = new Mesh { name = "LGMW_LightCone_" + nk + "_" + fk };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            coneMeshes[key] = mesh;
            return mesh;
        }

        // Cached per colour: MoteGlow keeps the beam bright in darkness, which is the whole
        // point of a laser, and building a material every frame would be wasteful.
        private static readonly Dictionary<Color, Material> laserMats =
            new Dictionary<Color, Material>();

        private static Material LaserMaterial(Color color)
        {
            Material mat;
            if (laserMats.TryGetValue(color, out mat)) return mat;

            mat = SolidColorMaterials.NewSolidColorMaterial(color, ShaderDatabase.MoteGlow);
            laserMats[color] = mat;
            return mat;
        }
    }
}