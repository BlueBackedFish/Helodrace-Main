using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Vector3 = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;
using Color = Waylith.Math.Color;
using Waylith.Math;

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

                Vector3 localPoint = node.Props.graphicOffset.ToNative() + laser.emitterOffset.ToNative();
                Vector3 origin = ModularLightGeometry.EmitterOrigin(node.transform, localPoint,
                    drawLoc, bodyAngle, flipped, AltitudeLayer.MoteOverhead.AltitudeFor());
                if (!ModularLightGeometry.TryLaserBeam(origin, target, weaponDirection,
                    alignToWeapon, laser.maxLength, out Vector3 end, out bool clipped)) continue;

                Material material = MaterialFor(laser.color.ToNative());
                GenDraw.DrawLineBetween(
                    origin.ToGame(),
                    end.ToGame(),
                    material,
                    Mathf.Max(0.005f, laser.beamWidth));

                // A truncated or recoil-deflected beam has no confirmed surface
                // at its end. Do not draw a floating target dot there.
                if (!laser.drawDot || clipped || alignToWeapon) continue;
                float dotSize = Mathf.Max(0.01f, laser.dotSize);
                Graphics.DrawMesh(
                    MeshPool.plane10,
                    SpatialMath.CreateTransform(
                        end,
                        Quaternion.Identity,
                        new Vector3(dotSize, 1f, dotSize)).ToGame(),
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

                Vector3 localPoint = node.Props.graphicOffset.ToNative() + laser.emitterOffset.ToNative();
                Vector3 origin = ModularLightGeometry.EmitterOrigin(node.transform, localPoint,
                    drawLoc, bodyAngle, flipped, AltitudeLayer.MoteOverhead.AltitudeFor());

                Vector3 delta = target - origin;
                delta.Y = 0f;
                float length = Mathf.Min(delta.Length(), laser.maxLength);
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
                    origin.ToGame(),
                    clearEnd.ToGame(),
                    nearMaterial,
                    Mathf.Max(0.005f, laser.beamWidth * 0.85f));
                GenDraw.DrawLineBetween(
                    clearEnd.ToGame(),
                    end.ToGame(),
                    blurredMaterial,
                    Mathf.Max(0.005f, laser.beamWidth * 0.55f));
            }
        }

        private static Material MaterialFor(Color color)
        {
            Material material;
            if (materials.TryGetValue(color, out material) && material != null)
                return material;

            material = SolidColorMaterials.NewSolidColorMaterial(
                color.ToGame(),
                ShaderDatabase.MoteGlow);
            materials[color] = material;
            return material;
        }
    }
}
