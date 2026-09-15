using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using RimWorld;
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
            public Color32[] colors;
            public long used;
            public int width, height;
            public int xMin, yMin, xMax, yMax;
        }
        private static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        private static readonly ConditionalWeakTable<List<ModularRenderNode>, Entry> snapshots =
            new ConditionalWeakTable<List<ModularRenderNode>, Entry>();
        private static long clock;
        private static bool reportedFailure;

        private sealed class Request
        {
            public List<ModularRenderNode> nodes;
            public Entry last;
            public float changedAt;
            public string signature;
            public IEnumerator<Texture2D> job;
        }
        private static readonly ConditionalWeakTable<CompModularWeaponNode, Request> requests =
            new ConditionalWeakTable<CompModularWeaponNode, Request>();
        // At most 24 MiB of RGBA source pixels; GPU readback is icon-sized, never 1024px.
        private static readonly Dictionary<Texture, Pixels> sources = new Dictionary<Texture, Pixels>();
        private const int SourceCapacity = 96;
        private static int workFrame = -1;
        private static readonly long FrameBudget = Math.Max(1, Stopwatch.Frequency * 2 / 1000);

        public static Texture2D Get(CompModularWeaponNode root)
        {
            Request request = requests.GetValue(root, key => new Request());
            // No baking or signature construction on Layout/input events.
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return request.last?.evicted == false ? request.last.texture : null;

            var nodes = root.RenderSnapshot();
            if (!ReferenceEquals(nodes, request.nodes))
            {
                request.job?.Dispose();
                request.job = null;
                request.signature = null;
                request.nodes = nodes;
                request.changedAt = Time.realtimeSinceStartup;
            }

            Entry remembered;
            if (snapshots.TryGetValue(nodes, out remembered) && !remembered.evicted)
            {
                remembered.used = ++clock;
                request.last = remembered;
                return remembered.texture;
            }
            Texture2D fallback = request.last?.evicted == false ? request.last.texture : null;
            // Dragging generates many snapshots. Bake only after input has settled.
            if (Time.realtimeSinceStartup - request.changedAt < 0.2f
                || workFrame == Time.frameCount) return fallback;
            workFrame = Time.frameCount;
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (request.job == null)
                {
                    var layers = new List<Layer>();
                    foreach (var node in nodes.OrderBy(n => n.Props.outlinePriority))
                        AddLayer(layers, node, true);
                    foreach (var node in nodes) AddLayer(layers, node, false);
                    if (layers.Count == 0) return fallback;
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
                    request.signature = key.ToString();
                    Entry shared;
                    if (cache.TryGetValue(request.signature, out shared))
                    {
                        shared.used = ++clock;
                        snapshots.Remove(nodes);
                        snapshots.Add(nodes, shared);
                        request.last = shared;
                        return shared.texture;
                    }
                    request.job = Bake(layers);
                }
                // Each step handles a source read or a few output rows. The budget is
                // shared across all commands, including multiple selected pawns.
                while (Stopwatch.GetTimestamp() - started < FrameBudget)
                {
                    if (!request.job.MoveNext())
                    {
                        request.job.Dispose();
                        request.job = null;
                        break;
                    }
                    Texture2D result = request.job.Current;
                    if (result == null) continue;
                    Remember(request, nodes, result);
                    request.job.Dispose();
                    request.job = null;
                    return result;
                }
            }
            catch (Exception ex)
            {
                request.job?.Dispose();
                request.job = null;
                if (!reportedFailure)
                {
                    reportedFailure = true;
                    Log.Warning("[Helodrace] Modular icon bake failed; using default icon. " + ex);
                }
                // Memoize failure for this snapshot instead of retrying every repaint.
                snapshots.Remove(nodes);
                snapshots.Add(nodes, new Entry { used = ++clock });
            }
            return fallback;
        }

        private static void Remember(Request request, List<ModularRenderNode> nodes, Texture2D texture)
        {
            Entry entry;
            if (cache.TryGetValue(request.signature, out entry))
                UnityEngine.Object.Destroy(texture);
            else
            {
                if (cache.Count >= Capacity)
                {
                    var oldest = cache.OrderBy(pair => pair.Value.used).First();
                    oldest.Value.evicted = true;
                    UnityEngine.Object.Destroy(oldest.Value.texture);
                    cache.Remove(oldest.Key);
                }
                entry = new Entry { texture = texture };
                cache.Add(request.signature, entry);
            }
            entry.used = ++clock;
            snapshots.Remove(nodes);
            snapshots.Add(nodes, entry);
            request.last = entry;
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
            Pixels cached;
            if (sources.TryGetValue(texture, out cached))
            {
                cached.used = ++clock;
                return cached;
            }
            int width = Math.Min(Resolution, texture.width);
            int height = Math.Min(Resolution, texture.height);
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(width, height,
                0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D readable = null;
            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                cached = new Pixels { colors = readable.GetPixels32(), width = width,
                    height = height, used = ++clock,
                    xMin = width, yMin = height, xMax = -1, yMax = -1 };
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (cached.colors[y * width + x].a > 2)
                        {
                            cached.xMin = Math.Min(cached.xMin, x);
                            cached.xMax = Math.Max(cached.xMax, x);
                            cached.yMin = Math.Min(cached.yMin, y);
                            cached.yMax = Math.Max(cached.yMax, y);
                        }
                if (sources.Count >= SourceCapacity)
                    sources.Remove(sources.OrderBy(pair => pair.Value.used).First().Key);
                sources.Add(texture, cached);
                return cached;
            }
            finally
            {
                RenderTexture.active = previous;
                if (readable != null) UnityEngine.Object.Destroy(readable);
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static IEnumerator<Texture2D> Bake(List<Layer> layers)
        {
            var localSources = new Dictionary<Texture, Pixels>();
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (var layer in layers)
            {
                Pixels pixels;
                if (!localSources.TryGetValue(layer.texture, out pixels))
                {
                    localSources.Add(layer.texture, pixels = Read(layer.texture));
                    yield return null;
                }
                int xMin = pixels.xMin, yMin = pixels.yMin,
                    xMax = pixels.xMax, yMax = pixels.yMax;
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
                Pixels source = localSources[layer.texture];
                float cosine = Mathf.Cos(-layer.angle), sine = Mathf.Sin(-layer.angle);
                for (int y = 0; y < Resolution; y++)
                {
                    if ((y & 3) == 0) yield return null;
                    for (int x = 0; x < Resolution; x++)
                    {
                        Vector2 point = center + new Vector2((x + .5f) / Resolution - .5f,
                            (y + .5f) / Resolution - .5f) * span;
                        Vector2 delta = point - layer.center;
                        Vector2 local = new Vector2(cosine * delta.x - sine * delta.y,
                            sine * delta.x + cosine * delta.y);
                        float u = local.x / layer.size.x + .5f, v = local.y / layer.size.y + .5f;
                        if (u < 0 || u >= 1 || v < 0 || v >= 1) continue;
                        Color src = (Color)source.colors[(int)(v * source.height) * source.width
                            + (int)(u * source.width)] * layer.tint;
                        if (src.a <= 0f) continue;
                        int index = y * Resolution + x;
                        Color dst = output[index];
                        float alpha = src.a + dst.a * (1 - src.a);
                        if (alpha <= 0) continue;
                        Color mixed = (src * src.a + dst * (dst.a * (1 - src.a))) / alpha;
                        mixed.a = alpha;
                        output[index] = mixed;
                    }
                }
            }
            var result = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            result.name = "Helodrace modular weapon icon";
            result.wrapMode = TextureWrapMode.Clamp;
            result.filterMode = FilterMode.Bilinear;
            result.SetPixels(output);
            result.Apply(false, true);
            yield return result;
        }

        private static Vector2 Rotate(Vector2 p, float angle)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
        }
    }

    // Command_VerbTarget.DrawIcon (ownerThing) and ColonistBar both use this
    // instance-aware overload. A shared ThingDef icon cannot represent two builds.
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ThingIcon),
        new Type[] { typeof(Rect), typeof(Thing), typeof(float), typeof(Rot4?),
            typeof(bool), typeof(float), typeof(bool) })]
    public static class Patch_ModularWeaponThingIcon
    {
        public static bool Prefix(Rect rect, Thing thing, float alpha, float scale,
            bool grayscale)
        {
            var root = thing?.GetInnerIfMinified()?.TryGetComp<CompModularWeaponNode>();
            if (root?.Props.isAssemblyRoot != true) return true;
            Texture2D icon = ModularWeaponIconCache.Get(root);
            // Retain vanilla fallback while the time-sliced bake is pending.
            if (icon == null) return true;
            Color previous = GUI.color;
            try
            {
                GUI.color = Color.white;
                Material material = grayscale
                    ? MaterialPool.MatFrom(new MaterialRequest
                    {
                        shader = ShaderDatabase.GrayscaleGUI,
                        color = Color.white,
                        maskTex = Texture2D.redTexture
                    })
                    : null;
                Widgets.DrawTextureFitted(rect, icon, scale, material, alpha);
            }
            finally
            {
                GUI.color = previous;
            }
            return false;
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
