using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public static class ModularWeaponLaserRenderer
    {
        private static readonly Dictionary<Color, Material> materials =
            new Dictionary<Color, Material>();

        public static bool HasLaser(CompModularWeaponNode comp)
        {
            List<ModularRenderNode> nodes = comp?.RenderSnapshot();
            if (nodes == null) return false;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].Props.laser != null) return true;
            return false;
        }

        public static void Draw(
            CompModularWeaponNode comp,
            Vector3 drawLoc,
            float bodyAngle,
            bool flipped,
            Vector3 target,
            bool alignToWeapon,
            Vector3 weaponDirection)
        {
            List<ModularRenderNode> nodes = comp.RenderSnapshot();
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                ModularWeaponLaserProperties laser = node.Props.laser;
                if (laser == null) continue;

                Vector3 localPoint = node.Props.graphicOffset + laser.emitterOffset;
                Vector2 local = node.transform.TransformPoint(localPoint);
                if (flipped) local.x = -local.x;
                Vector2 worldOffset = RotateForWorldYaw(local, bodyAngle);
                Vector3 origin = drawLoc
                    + new Vector3(worldOffset.x, 0f, worldOffset.y);
                origin.y = AltitudeLayer.MoteOverhead.AltitudeFor();

                Vector3 end = target;
                end.y = origin.y;
                Vector3 delta = end - origin;
                delta.y = 0f;
                if (delta.sqrMagnitude < 0.0001f) continue;
                if (alignToWeapon && weaponDirection.sqrMagnitude > 0.5f)
                {
                    delta = weaponDirection.normalized * delta.magnitude;
                    end = origin + delta;
                    end.y = origin.y;
                }
                bool clipped = delta.magnitude > laser.maxLength;
                if (clipped)
                    end = origin + delta.normalized * laser.maxLength;

                Material material = MaterialFor(laser.color);
                GenDraw.DrawLineBetween(
                    origin,
                    end,
                    material,
                    Mathf.Max(0.005f, laser.beamWidth));

                // A truncated or recoil-deflected beam has no confirmed surface
                // at its end. Do not draw a floating target dot there.
                if (!laser.drawDot || clipped || alignToWeapon) continue;
                float dotSize = Mathf.Max(0.01f, laser.dotSize);
                Graphics.DrawMesh(
                    MeshPool.plane10,
                    Matrix4x4.TRS(
                        end,
                        Quaternion.identity,
                        new Vector3(dotSize, 1f, dotSize)),
                    material,
                    0);
            }
        }

        public static void DrawAimDirection(
            CompModularWeaponNode comp,
            Vector3 drawLoc,
            float bodyAngle,
            bool flipped,
            Vector3 target)
        {
            List<ModularRenderNode> nodes = comp.RenderSnapshot();
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                ModularWeaponLaserProperties laser = node.Props.laser;
                if (laser == null) continue;

                Vector3 localPoint = node.Props.graphicOffset + laser.emitterOffset;
                Vector2 local = node.transform.TransformPoint(localPoint);
                if (flipped) local.x = -local.x;
                Vector2 worldOffset = RotateForWorldYaw(local, bodyAngle);
                Vector3 origin = drawLoc
                    + new Vector3(worldOffset.x, 0f, worldOffset.y);
                origin.y = AltitudeLayer.MoteOverhead.AltitudeFor();

                Vector3 delta = target - origin;
                delta.y = 0f;
                float length = Mathf.Min(delta.magnitude, laser.maxLength);
                if (length < 0.05f) continue;

                Vector3 direction = delta.normalized;
                Vector3 end = origin + direction * length;
                Material nearMaterial = MaterialFor(
                    new Color(laser.color.r, laser.color.g, laser.color.b, 0.72f));
                Material blurredMaterial = MaterialFor(
                    new Color(laser.color.r, laser.color.g, laser.color.b, 0.14f));

                // Keep only a short clear section at the emitter. The rest is a
                // low-alpha beam so it communicates the focus line without acting
                // like a precise rangefinder or target marker.
                Vector3 clearEnd = origin + direction * Mathf.Min(0.65f, length);
                GenDraw.DrawLineBetween(
                    origin,
                    clearEnd,
                    nearMaterial,
                    Mathf.Max(0.005f, laser.beamWidth * 0.85f));
                GenDraw.DrawLineBetween(
                    clearEnd,
                    end,
                    blurredMaterial,
                    Mathf.Max(0.005f, laser.beamWidth * 0.55f));
            }
        }

        private static Vector2 RotateForWorldYaw(Vector2 value, float degrees)
        {
            Vector3 rotated = Quaternion.AngleAxis(degrees, Vector3.up)
                * new Vector3(value.x, 0f, value.y);
            return new Vector2(rotated.x, rotated.z);
        }

        private static Material MaterialFor(Color color)
        {
            Material material;
            if (materials.TryGetValue(color, out material) && material != null)
                return material;

            material = SolidColorMaterials.NewSolidColorMaterial(
                color,
                ShaderDatabase.MoteGlow);
            materials[color] = material;
            return material;
        }
    }
}
