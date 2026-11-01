// Frozen from 586d610f before owned weapon editor geometry conversion.
using Helodrace.ModernWar;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;
using Vector2 = System.Numerics.Vector2;
using Rect = Waylith.Math.Rect;
using Color = Waylith.Math.Color;

namespace Helodrace.Tests.FrozenWeaponUI
{
    internal sealed class ModularWeaponTextureData
    {
        public const int Resolution = 256;
        private static readonly ConditionalWeakTable<Texture2D, ModularWeaponTextureData> cache =
            new ConditionalWeakTable<Texture2D, ModularWeaponTextureData>();
        public byte[] Alpha { get; private set; }
        public Rect VisibleUv { get; private set; } = new Rect(0f, 0f, 1f, 1f);
        public bool HasPixels { get; private set; } = true;

        public static ModularWeaponTextureData For(Texture2D texture) => cache.GetValue(texture, Read);

        public bool Contains(float u, float v)
            => Alpha != null && Alpha[Mathf.Clamp((int)(v * Resolution), 0, Resolution - 1)
                * Resolution + Mathf.Clamp((int)(u * Resolution), 0, Resolution - 1)] > 24;

        private static ModularWeaponTextureData Read(Texture2D texture)
        {
            var result = new ModularWeaponTextureData();
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = null;
            Texture2D copy = null;
            try
            {
                temporary = RenderTexture.GetTemporary(Resolution, Resolution,
                    0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                copy = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0f, 0f, Resolution, Resolution).ToGame(), 0, 0, false);
                Color32[] pixels = copy.GetPixels32();
                result.Alpha = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) result.Alpha[i] = pixels[i].a;
                Rect uv;
                result.HasPixels = ModularWeaponImageBounds.TryFind(result.Alpha, Resolution, Resolution, out uv);
                result.VisibleUv = uv;
            }
            catch (Exception)
            {
                // The uncropped image remains usable if graphics readback is unavailable.
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
            return result;
        }
    }

    internal sealed class ModularWeaponPartImage
    {
        private sealed class Layer
        {
            public Texture2D Texture, Outline;
            public Vector2 Size;
            public float Order;
        }
        private static readonly Dictionary<ThingDef, ModularWeaponPartImage> cache =
            new Dictionary<ThingDef, ModularWeaponPartImage>();
        private readonly List<Layer> layers = new List<Layer>();
        private Rect bounds;
        private bool hasBounds;
        public bool HasImage => layers.Count > 0;

        public static ModularWeaponPartImage For(ThingDef def)
        {
            ModularWeaponPartImage image;
            if (cache.TryGetValue(def, out image)) return image;
            image = new ModularWeaponPartImage();
            var props = def.GetCompProperties<CompProperties_ModularWeaponNode>();
            image.Add(def.graphicData, props?.graphicLayer ?? 0f);
            if (props?.additionalGraphics != null)
                foreach (var extra in props.additionalGraphics)
                    if (extra?.graphicData != null) image.Add(extra.graphicData, extra.graphicLayer);
            // List.Sort is not stable: preserve XML order for equal layers.
            for (int i = 1; i < image.layers.Count; i++)
            {
                Layer value = image.layers[i];
                int j = i - 1;
                while (j >= 0 && image.layers[j].Order > value.Order)
                {
                    image.layers[j + 1] = image.layers[j];
                    j--;
                }
                image.layers[j + 1] = value;
            }
            cache.Add(def, image);
            return image;
        }

        private void Add(GraphicData graphic, float order)
        {
            if (graphic?.texPath.NullOrEmpty() != false) return;
            Texture2D texture = ContentFinder<Texture2D>.Get(graphic.texPath, false);
            if (texture == null) return;
            Texture2D outline = ContentFinder<Texture2D>.Get(graphic.texPath + "_Outline", false);
            var layer = new Layer { Texture = texture, Outline = outline, Size = graphic.drawSize.ToNative(), Order = order };
            layers.Add(layer);
            Include(texture, layer.Size);
            if (outline != null) Include(outline, layer.Size);
        }

        private void Include(Texture2D texture, Vector2 size)
        {
            var data = ModularWeaponTextureData.For(texture);
            if (!data.HasPixels) return;
            Rect part = ModularWeaponImageBounds.OnCanvas(data.VisibleUv, size);
            bounds = hasBounds ? ModularWeaponImageBounds.Union(bounds, part) : part;
            hasBounds = true;
        }

        public void Draw(Rect area, float zoom = 1f, Vector2 pan = default(Vector2))
        {
            if (!hasBounds || area.width <= 0f || area.height <= 0f) return;
            float scale = Mathf.Min(area.width / Mathf.Max(0.001f, bounds.width),
                area.height / Mathf.Max(0.001f, bounds.height)) * 0.94f * zoom;
            GUI.BeginGroup(area.ToGame());
            Vector2 origin = area.size * 0.5f + pan
                - new Vector2(bounds.center.X, -bounds.center.Y) * scale;
            Color previous = GUI.color.ToNative();
            foreach (Layer layer in layers)
            {
                Rect rect = Centered(origin, layer.Size * scale);
                if (layer.Outline != null) GUI.DrawTexture(rect.ToGame(), layer.Outline);
                else
                {
                    GUI.color = new Color(0f, 0f, 0f, previous.a).ToGame();
                    float edge = Mathf.Clamp(zoom, 1f, 3f);
                    for (int x = -1; x <= 1; x++)
                        for (int y = -1; y <= 1; y++)
                            if (x != 0 || y != 0)
                                GUI.DrawTexture(new Rect(rect.x + x * edge, rect.y + y * edge,
                                    rect.width, rect.height).ToGame(), layer.Texture);
                    GUI.color = previous.ToGame();
                }
            }
            foreach (Layer layer in layers) GUI.DrawTexture(Centered(origin, layer.Size * scale).ToGame(), layer.Texture);
            GUI.color = previous.ToGame();
            GUI.EndGroup();
        }

        private static Rect Centered(Vector2 center, Vector2 size)
            => new Rect(center.X - size.X * 0.5f, center.Y - size.Y * 0.5f, size.X, size.Y);
    }

    internal sealed class Dialog_ModularWeaponPartImage : Window
    {
        private readonly ThingDef part;
        private readonly ModularWeaponPreviewCamera camera = new ModularWeaponPreviewCamera();
        public override UnityEngine.Vector2 InitialSize => new UnityEngine.Vector2(760f, 560f);

        public Dialog_ModularWeaponPartImage(ThingDef part)
        {
            this.part = part;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
        }

        public override void DoWindowContents(UnityEngine.Rect gameRect)
        {
            Rect inRect = gameRect.ToNative();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 30f, 35f).ToGame(), part.LabelCap);
            Text.Font = GameFont.Small;
            Rect area = new Rect(0f, 78f, inRect.width, Mathf.Max(1f, inRect.height - 120f));
            if (Widgets.ButtonText(new Rect(0f, 40f, 32f, 28f).ToGame(), "−"))
                camera.ZoomAt(area, camera.Zoom / 1.25f, area.center);
            if (Widgets.ButtonText(new Rect(38f, 40f, 32f, 28f).ToGame(), "+"))
                camera.ZoomAt(area, camera.Zoom * 1.25f, area.center);
            Widgets.Label(new Rect(80f, 43f, 70f, 25f).ToGame(), camera.Zoom.ToString("P0"));
            if (Widgets.ButtonText(new Rect(155f, 40f, 115f, 28f).ToGame(),
                "HD_ModularWeapon_ResetView".Translate())) camera.Reset();
            Widgets.DrawBoxSolidWithOutline(area.ToGame(), new Color(0.16f, 0.18f, 0.20f).ToGame(),
                new Color(0.4f, 0.45f, 0.5f).ToGame(), 1);
            Event current = Event.current;
            if (Mouse.IsOver(area.ToGame()))
            {
                if (current.type == EventType.ScrollWheel)
                {
                    camera.ZoomAt(area, camera.Zoom * Mathf.Pow(1.12f, -current.delta.y), current.mousePosition.ToNative());
                    current.Use();
                }
                else if (current.type == EventType.MouseDrag && current.button == 2)
                {
                    camera.Move(current.delta.ToNative());
                    current.Use();
                }
            }
            ModularWeaponPartImage.For(part).Draw(area, camera.Zoom, camera.Pan);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, inRect.height - 34f, inRect.width, 34f).ToGame(),
                "HD_ModularWeapon_ZoomHint".Translate());
            Text.Font = GameFont.Small;
        }
    }
}
