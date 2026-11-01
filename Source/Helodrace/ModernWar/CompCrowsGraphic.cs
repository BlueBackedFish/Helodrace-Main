using RimWorld;
using UnityEngine;
using Waylith.Math;
using Vector3 = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;
using Verse;

namespace Helodrace
{
    public sealed class CompProperties_CrowsGraphic : CompProperties
    {
        public CompProperties_CrowsGraphic() { compClass = typeof(CompCrowsGraphic); }
    }

    public sealed class CompCrowsGraphic : ThingComp
    {
        private const string Root = "Buildings/Security/CROWSII/";
        private Material baseMaterial, frameMaterial, barrelMaterial, bodyMaterial, feedMaterial;
        private CrowsWeaponDef cachedWeapon;
        private int lastShotTick = -99999;
        public void NotifyShotFired() { lastShotTick = Find.TickManager.TicksGame; }
        public void NotifyWeaponChanged()
        {
            frameMaterial = barrelMaterial = bodyMaterial = feedMaterial = null;
            lastShotTick = -99999;
        }

        public override void PostDraw()
        {
            if (!(parent is Building_Crows turret) || turret.Top == null) return;
            baseMaterial = baseMaterial ?? MaterialPool.MatFrom(Root + "HD_CROWSII_M2_Base", ShaderDatabase.Cutout);
            CrowsWeaponDef weapon = turret.InstalledWeapon;
            if (frameMaterial == null || cachedWeapon != weapon)
            {
                cachedWeapon = weapon;
                frameMaterial = MaterialPool.MatFrom(weapon?.frameTexPath ?? Root + "Unarmed/HD_CROWSII_Unarmed_Frame", ShaderDatabase.Cutout);
                barrelMaterial = weapon == null ? null : MaterialPool.MatFrom(weapon.barrelTexPath, ShaderDatabase.Cutout);
                bodyMaterial = weapon == null ? null : MaterialPool.MatFrom(weapon.bodyTexPath, ShaderDatabase.Cutout);
                feedMaterial = weapon == null ? null : MaterialPool.MatFrom(weapon.feedTexPath, ShaderDatabase.Cutout);
            }
            Vector3 pos = parent.DrawPos.ToNative();
            pos.Y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            // MeshPool.plane10 is one tile wide, not ten. The registered art
            // uses the same 3.2-tile canvas as the foundation graphic.
            float drawSize = parent.def.building.turretTopDrawSize;
            Vector3 scale = new Vector3(drawSize, 1f, drawSize);
            Draw(pos, Quaternion.AngleAxis(parent.Rotation.AsAngle, Vector3.UnitY), scale, baseMaterial);
            Quaternion rotation = Quaternion.AngleAxis(turret.Top.CurRotation, Vector3.UnitY);
            pos.Y += .001f;
            Draw(pos, rotation, scale, frameMaterial);
            if (weapon == null) return;
            int age = Find.TickManager.TicksGame - lastShotTick;
            float recoil = weapon.barrelRecoil && age >= 0 && age < 6 ? .0375f * (1f - age / 6f) : 0f;
            pos.Y += .001f;
            Draw(pos - Vector3.Transform(Vector3.UnitZ, rotation) * recoil, rotation, scale, barrelMaterial);
            pos.Y += .001f;
            Draw(pos, rotation, scale, bodyMaterial);
            pos.Y += .001f;
            Draw(pos, rotation, scale, feedMaterial);
        }

        private static void Draw(Vector3 pos, Quaternion rotation, Vector3 scale, Material material)
            => Graphics.DrawMesh(MeshPool.plane10, SpatialMath.CreateTransform(pos, rotation, scale).ToGame(), material, 0);
    }
}
