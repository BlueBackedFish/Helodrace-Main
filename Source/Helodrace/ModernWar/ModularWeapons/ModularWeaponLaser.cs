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
                if (delta.magnitude > laser.maxLength)
                    end = origin + delta.normalized * laser.maxLength;

                Material material = MaterialFor(laser.color);
                GenDraw.DrawLineBetween(
                    origin,
                    end,
                    material,
                    Mathf.Max(0.005f, laser.beamWidth));

                if (!laser.drawDot) continue;
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
