using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    // UI-only, neutral-pose bake. Never changes the shared ThingDef.uiIcon.
    public static class ModularWeaponIconCache
    {
        private const int Resolution = 256;
        private const int Capacity = 128;
        private sealed class Entry
        {
            public Texture2D texture;
            public long used;
            public bool evicted;
        }
        private sealed class Layer
        {
            public Texture texture;
            public Vector2 center, size;
            public float angle;
            public Color tint;
        }
        private sealed class Pixels
        {
            public Color[] colors;
            public int width, height;
        }
        private static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        private static readonly ConditionalWeakTable<List<ModularRenderNode>, Entry> snapshots =
            new ConditionalWeakTable<List<ModularRenderNode>, Entry>();
        private static long clock;
        private static bool reportedFailure;

        public static Texture2D Get(CompModularWeaponNode root)
        {
            var nodes = root.RenderSnapshot();
            Entry remembered;
            if (snapshots.TryGetValue(nodes, out remembered))
            {
                if (!remembered.evicted)
                {
                    remembered.used = ++clock;
                    return remembered.texture;
                }
                snapshots.Remove(nodes);
            }
            var layers = new List<Layer>();
            foreach (var node in nodes.OrderBy(n => n.Props.outlinePriority))
                AddLayer(layers, node, true);
            foreach (var node in nodes) AddLayer(layers, node, false);
            if (layers.Count == 0) return null;
            var key = new StringBuilder();
            foreach (var layer in layers)
            {
                key.Append(layer.texture.GetInstanceID()).Append(':');
                foreach (float value in new[] { layer.center.x, layer.center.y,
                    layer.size.x, layer.size.y, layer.angle,
                    layer.tint.r, layer.tint.g, layer.tint.b, layer.tint.a })
                    key.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                key.Append(';');
            }
            string signature = key.ToString();
            Entry entry;
            if (cache.TryGetValue(signature, out entry))
            {
                entry.used = ++clock;
                snapshots.Add(nodes, entry);
                return entry.texture;
            }
            // Only read GPU textures during repaint; layout and input use the fallback.
            if (Event.current == null || Event.current.type != EventType.Repaint) return null;
            try
            {
                Texture2D result = Bake(layers);
                if (cache.Count >= Capacity)
                {
                    var oldest = cache.OrderBy(pair => pair.Value.used).First();
                    oldest.Value.evicted = true;
                    UnityEngine.Object.Destroy(oldest.Value.texture);
                    cache.Remove(oldest.Key);
                }
                entry = new Entry { texture = result, used = ++clock };
                cache.Add(signature, entry);
                snapshots.Add(nodes, entry);
                return result;
            }
            catch (Exception ex)
            {
                if (!reportedFailure)
                {
                    reportedFailure = true;
                    Log.Warning("[Helodrace] Modular icon bake failed; using default icon. " + ex);
                }
                // Remember failures too, so a bad texture cannot trigger repeated readbacks.
                if (cache.Count < Capacity)
                    cache[signature] = new Entry { used = ++clock };
                return null;
            }
        }

        private static void AddLayer(List<Layer> layers, ModularRenderNode node, bool outline)
        {
            if (!ModularWeaponAssemblyRenderer.ShouldDrawNode(node)) return;
            Graphic graphic = node.thing.Graphic;
            if (graphic == null) return;
            string path = node.thing.def.graphicData?.texPath;
            Texture texture = outline
                ? (path.NullOrEmpty() ? null : ContentFinder<Texture2D>.Get(path + "_Outline", false))
                : graphic.MatSingle.mainTexture;
            if (texture == null) return;
            layers.Add(new Layer { texture = texture, center = node.GraphicCenter,
                size = Vector2.Scale(graphic.drawSize, node.GraphicScale),
                angle = node.GraphicAngle * Mathf.Deg2Rad,
                tint = outline ? Color.white : graphic.Color });
        }

        private static Pixels Read(Texture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(texture.width, texture.height,
                0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D readable = null;
            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                return new Pixels { colors = readable.GetPixels(), width = texture.width,
                    height = texture.height };
            }
            finally
            {
                RenderTexture.active = previous;
                if (readable != null) UnityEngine.Object.Destroy(readable);
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static Texture2D Bake(List<Layer> layers)
        {
            var sources = new Dictionary<Texture, Pixels>();
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (var layer in layers)
            {
                Pixels pixels;
                if (!sources.TryGetValue(layer.texture, out pixels))
                    sources.Add(layer.texture, pixels = Read(layer.texture));
                int xMin = pixels.width, yMin = pixels.height, xMax = -1, yMax = -1;
                for (int y = 0; y < pixels.height; y++)
                    for (int x = 0; x < pixels.width; x++)
                        if (pixels.colors[y * pixels.width + x].a > 0.01f)
                        { xMin = Math.Min(xMin, x); xMax = Math.Max(xMax, x);
                          yMin = Math.Min(yMin, y); yMax = Math.Max(yMax, y); }
                if (xMax < 0) continue;
                foreach (int x in new[] { xMin, xMax + 1 })
                    foreach (int y in new[] { yMin, yMax + 1 })
                    {
                        Vector2 corner = Rotate(new Vector2((x / (float)pixels.width - .5f) * layer.size.x,
                            (y / (float)pixels.height - .5f) * layer.size.y), layer.angle) + layer.center;
                        min = Vector2.Min(min, corner); max = Vector2.Max(max, corner);
                    }
            }
            if (min.x == float.MaxValue) throw new InvalidOperationException("No visible pixels.");
            Vector2 center = (min + max) * .5f;
            float span = Mathf.Max(max.x - min.x, max.y - min.y, .001f) * 1.08f;
            Color[] output = new Color[Resolution * Resolution];
            foreach (var layer in layers)
            {
                if (Mathf.Abs(layer.size.x * layer.size.y) < .0000001f) continue;
                Pixels source = sources[layer.texture];
                for (int y = 0; y < Resolution; y++)
                    for (int x = 0; x < Resolution; x++)
                    {
                        Vector2 point = center + new Vector2((x + .5f) / Resolution - .5f,
                            (y + .5f) / Resolution - .5f) * span;
                        Vector2 local = Rotate(point - layer.center, -layer.angle);
                        float u = local.x / layer.size.x + .5f, v = local.y / layer.size.y + .5f;
                        if (u < 0 || u >= 1 || v < 0 || v >= 1) continue;
                        Color src = source.colors[(int)(v * source.height) * source.width
                            + (int)(u * source.width)] * layer.tint;
                        int index = y * Resolution + x;
                        Color dst = output[index];
                        float alpha = src.a + dst.a * (1 - src.a);
                        if (alpha <= 0) continue;
                        Color mixed = (src * src.a + dst * (dst.a * (1 - src.a))) / alpha;
                        mixed.a = alpha;
                        output[index] = mixed;
                    }
            }
            var result = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            result.name = "Helodrace modular weapon icon";
            result.wrapMode = TextureWrapMode.Clamp;
            result.filterMode = FilterMode.Bilinear;
            result.SetPixels(output);
            result.Apply(false, true);
            return result;
        }

        private static Vector2 Rotate(Vector2 p, float angle)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
        }
    }

    [HarmonyPatch(typeof(Command), nameof(Command.GizmoOnGUI))]
    public static class Patch_ModularWeaponCommandIcon
    {
        public static void Prefix(Command __instance)
        {
            var command = __instance as Command_VerbTarget;
            var root = command?.verb?.EquipmentSource?.TryGetComp<CompModularWeaponNode>();
            if (root?.Props.isAssemblyRoot != true) return;
            Texture2D icon = ModularWeaponIconCache.Get(root);
            command.icon = icon ?? root.parent.def.uiIcon;
            if (icon == null) return;
            command.iconAngle = 0f;
            command.iconOffset = Vector2.zero;
            command.iconDrawScale = 1f;
            command.defaultIconColor = Color.white;
        }
    }
}
