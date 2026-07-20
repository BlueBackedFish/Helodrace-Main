using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    /// <summary>
    /// Defines the two artwork layers used when this ammunition is loaded into
    /// an M220 TOW launcher. More missile types can provide different paths.
    /// </summary>
    public class TowLoadedGraphicExtension : DefModExtension
    {
        public string loadedLineTexPath;
        public string loadedTexPath;
    }

    public class CompProperties_TowLauncherGraphic : CompProperties
    {
        public string lowTexPath;
        public string highTexPath;
        public float drawSize = 2f;
        public float rotationOffsetDegrees = -90f;

        public CompProperties_TowLauncherGraphic()
        {
            compClass = typeof(CompTowLauncherGraphic);
        }
    }

    /// <summary>
    /// Draw order (bottom to top): loaded missile line, launcher low, loaded
    /// missile, launcher high. The launcher halves are always drawn, while the
    /// ammunition layers disappear after firing and throughout reloading.
    /// </summary>
    public class CompTowLauncherGraphic : ThingComp
    {
        private Material lowMaterial;
        private Material highMaterial;
        private ThingDef cachedShellDef;
        private Material loadedLineMaterial;
        private Material loadedMaterial;

        private CompProperties_TowLauncherGraphic Props =>
            (CompProperties_TowLauncherGraphic)props;

        public override void PostDraw()
        {
            base.PostDraw();

            Building_TurretGun turret = parent as Building_TurretGun;
            if (turret?.Top == null)
            {
                return;
            }

            Quaternion rotation = Quaternion.AngleAxis(
                turret.Top.CurRotation + Props.rotationOffsetDegrees,
                Vector3.up);
            Vector3 drawPos = parent.DrawPos;
            drawPos.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            Vector3 scale = new Vector3(Props.drawSize, 1f, Props.drawSize);

            CompChangeableProjectile loader = turret.GunCompEq?.parent?.TryGetComp<CompChangeableProjectile>();
            TowLoadedGraphicExtension shellGraphic = loader?.Loaded == true
                ? loader.LoadedShell?.GetModExtension<TowLoadedGraphicExtension>()
                : null;

            // Keep these increments explicit: changing their order changes how
            // the missile is occluded by the launch tube artwork.
            if (shellGraphic != null)
            {
                CacheShellMaterials(loader.LoadedShell, shellGraphic);
                DrawLayer(drawPos, rotation, scale, loadedLineMaterial);
            }

            drawPos.y += 0.001f;
            DrawLayer(drawPos, rotation, scale, LowMaterial);

            if (shellGraphic != null)
            {
                drawPos.y += 0.001f;
                DrawLayer(drawPos, rotation, scale, loadedMaterial);
            }

            drawPos.y += 0.001f;
            DrawLayer(drawPos, rotation, scale, HighMaterial);
        }

        private static void DrawLayer(Vector3 drawPos, Quaternion rotation, Vector3 scale, Material material)
        {
            if (material != null)
            {
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawPos, rotation, scale), material, 0);
            }
        }

        private void CacheShellMaterials(ThingDef shellDef, TowLoadedGraphicExtension extension)
        {
            if (cachedShellDef == shellDef)
            {
                return;
            }

            cachedShellDef = shellDef;
            loadedLineMaterial = MaterialFrom(extension.loadedLineTexPath);
            loadedMaterial = MaterialFrom(extension.loadedTexPath);
        }

        private static Material MaterialFrom(string texPath)
        {
            return texPath.NullOrEmpty() ? null : MaterialPool.MatFrom(texPath, ShaderDatabase.Cutout);
        }

        private Material LowMaterial => lowMaterial ?? (lowMaterial = MaterialFrom(Props.lowTexPath));
        private Material HighMaterial => highMaterial ?? (highMaterial = MaterialFrom(Props.highTexPath));
    }
}
