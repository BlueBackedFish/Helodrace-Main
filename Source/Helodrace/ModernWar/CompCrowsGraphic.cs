using RimWorld;
using UnityEngine;
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
        private int lastShotTick = -99999;
        public void NotifyShotFired() { lastShotTick = Find.TickManager.TicksGame; }

        public override void PostDraw()
        {
            if (!(parent is Building_Crows turret) || turret.Top == null) return;
            baseMaterial = baseMaterial ?? MaterialPool.MatFrom(Root + "HD_CROWSII_M2_Base", ShaderDatabase.Cutout);
            frameMaterial = frameMaterial ?? MaterialPool.MatFrom(Root + "HD_CROWSII_M2_Frame", ShaderDatabase.Cutout);
            barrelMaterial = barrelMaterial ?? MaterialPool.MatFrom(Root + "HD_CROWSII_M2Barrel", ShaderDatabase.Cutout);
            bodyMaterial = bodyMaterial ?? MaterialPool.MatFrom(Root + "HD_CROWSII_M2Body", ShaderDatabase.Cutout);
            feedMaterial = feedMaterial ?? MaterialPool.MatFrom(Root + "HD_CROWSII_M2_FeedOverlay", ShaderDatabase.Cutout);
            Vector3 pos = parent.DrawPos;
            pos.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            // MeshPool.plane10 is one tile wide, not ten. The registered art
            // uses the same 3.2-tile canvas as the foundation graphic.
            float drawSize = parent.def.building.turretTopDrawSize;
            Vector3 scale = new Vector3(drawSize, 1f, drawSize);
            Draw(pos, Quaternion.AngleAxis(parent.Rotation.AsAngle, Vector3.up), scale, baseMaterial);
            Quaternion rotation = Quaternion.AngleAxis(turret.Top.CurRotation, Vector3.up);
            pos.y += .001f;
            Draw(pos, rotation, scale, frameMaterial);
            int age = Find.TickManager.TicksGame - lastShotTick;
            float recoil = age >= 0 && age < 6 ? .0375f * (1f - age / 6f) : 0f;
            pos.y += .001f;
            Draw(pos - rotation * Vector3.forward * recoil, rotation, scale, barrelMaterial);
            pos.y += .001f;
            Draw(pos, rotation, scale, bodyMaterial);
            pos.y += .001f;
            Draw(pos, rotation, scale, feedMaterial);
        }

        private static void Draw(Vector3 pos, Quaternion rotation, Vector3 scale, Material material)
            => Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, rotation, scale), material, 0);
    }
}
