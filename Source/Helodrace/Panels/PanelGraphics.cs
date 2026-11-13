using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Verse;
using Waylith.Graphics.Meshes;
using Waylith.Rendering;
using NativeColor = Waylith.Math.Color;

namespace Helodrace.Panels
{
    public sealed class Building_SandwichPanelWall : Building
    {
        protected override void DrawAt(UnityEngine.Vector3 drawLoc, bool flip = false)
        {
            int mask = 0;
            for (int i = 0; i < 4; i++)
            {
                IntVec3 cell = Position + GenAdj.CardinalDirections[i];
                if (cell.InBounds(Map) && PanelRules.IsHost(cell.GetEdifice(Map)?.def)) mask |= 1 << i;
            }
            PanelGraphics.DrawWall(this, mask);
        }
    }

    // Lifetime is the loaded mod assembly. Exactly 16 meshes, one shared texture
    // and material per mod root; no mesh or texture creation inside a draw call.
    public static class PanelGraphics
    {
        private sealed class Resources
        {
            public readonly Mesh[] walls = new Mesh[16];
            public readonly Material wall;
            public readonly Material[] panels = new Material[5];
            public readonly Mesh strip;
            public Resources(string root)
            {
                string modelRoot = Path.Combine(root, "Meshes", "WallPanels");
                for (int i = 0; i < 16; i++)
                    walls[i] = ObjMeshLoader.LoadFromFile(Path.Combine(modelRoot, $"Wall_{i:00}.obj"));
                var shader = ShaderRegistry.Resolve("Map/Cutout");
                wall = new Material(shader, "HD sandwich wall atlas")
                {
                    MainTexture = Texture2D.LoadFromFile(Path.Combine(root, "Textures", "Buildings", "WallPanels", "HD_SandwichWall_Atlas.png"), false),
                    Color = new NativeColor(1, 1, 1, 1)
                };
                NativeColor[] colors = {
                    new(.69f,.63f,.43f,1), new(.93f,.93f,.89f,1),
                    new(.40f,.44f,.46f,1), new(.74f,.59f,.22f,1), new(.72f,.79f,.81f,1)
                };
                for (int i = 0; i < panels.Length; i++)
                    panels[i] = new Material(ShaderRegistry.Resolve("Map/SolidColor"), "HD panel strip " + i) { Color = colors[i] };
                // Thin visible upper strip on the host's footprint; a neutral
                // unconnected host uses the outside edge. Door strips disappear
                // while open in this first construction milestone.
                Vector3[] v = {new(-.45f,0,0),new(-.45f,0,.045f),new(.45f,0,.045f),new(.45f,0,0)};
                strip = new Mesh(MeshArrays.Build(v, new[]{0,1,2,0,2,3},
                    null, null, new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)}, null));
            }
        }
        private static readonly Dictionary<string, Resources> resources = new();
        public static void Preload(string root)
        {
            if (!resources.ContainsKey(root)) resources[root] = new Resources(root);
        }
        private static Resources For(Thing thing) => resources[thing.def.modContentPack.RootDir];
        public static void DrawWall(Building wall, int mask)
        {
            Resources r = For(wall);
            float altitude = wall.def.altitudeLayer.AltitudeFor();
            // In the map view Y is a draw layer, not a meter of physical height.
            // Project the authored Z-up wall into a small northward facade and
            // keep its depth below BuildingOnTop so it cannot cover roofs/UI.
            float depth = MathF.Max(.0001f, AltitudeLayer.BuildingOnTop.AltitudeFor() - altitude - .001f);
            Matrix4x4 transform = Matrix4x4.Identity;
            transform.M22 = depth / 1.25f;
            transform.M23 = .20f;
            transform.M41 = wall.Position.x + .5f;
            transform.M42 = altitude;
            transform.M43 = wall.Position.z + .5f;
            Graphics.DrawMesh(r.walls[mask & 15], transform, r.wall, 0);
        }
        public static void DrawPanel(Building_WallPanel panel)
        {
            Resources r = For(panel);
            int index = panel.def.defName switch {
                "HD_Panel_Styrofoam" => 1, "HD_Panel_RHA" => 2,
                "HD_Panel_Aramid" => 3, "HD_Panel_EMI" => 4, _ => 0
            };
            float angle = panel.Rotation.AsInt * MathF.PI / 2;
            float distance = panel.Host is Building_SandwichPanelWall ? .095f : .46f;
            float layer = (int)panel.Properties.slot * .05f;
            Vector3 offset = new(MathF.Sin(angle)*(distance+layer),0,MathF.Cos(angle)*(distance+layer));
            Vector3 pos = new(panel.Position.x+.5f, AltitudeLayer.BuildingOnTop.AltitudeFor()+.01f, panel.Position.z+.5f);
            if (panel.Host is Building_SandwichPanelWall) pos.Z += .25f;
            Matrix4x4 transform = Matrix4x4.CreateRotationY(angle) * Matrix4x4.CreateTranslation(pos+offset);
            Graphics.DrawMesh(r.strip, transform, r.panels[index], 0);
        }
    }
}
