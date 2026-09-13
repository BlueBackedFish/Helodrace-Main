using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    internal enum ModularMuzzleEffectKind
    {
        Bare,
        FlashHider,
        MuzzleBrake,
        Suppressor
    }

    internal sealed class ModularMuzzleEffectProfile
    {
        public ModularMuzzleEffectKind kind;
        public Vector2 localMuzzle;
        public float localAngle;
    }

    /// <summary>
    /// Resolves both the visible muzzle end and the correct authored effect family
    /// from the live attachment tree. Device names are deliberately accepted as a
    /// fallback so newly-authored brake/hider/suppressor parts work without another
    /// per-part XML statistic.
    /// </summary>
    internal static class ModularWeaponMuzzleEffectResolver
    {
        public static ModularMuzzleEffectProfile Resolve(
            CompModularWeaponNode root)
        {
            List<ModularRenderNode> nodes = root?.RenderSnapshot();
            if (nodes == null || nodes.Count == 0) return null;

            ModularRenderNode barrel = null;
            ModularRenderNode directDevice = null;
            ModularRenderNode suppressor = null;
            Vector2 muzzle = new Vector2(root.MuzzleFlashDistance, 0f);

            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                ModularAttachmentSocket socket = node?.Props?.SocketNamed("muzzle");
                if (socket?.transform == null) continue;
                barrel = node;
                muzzle = node.transform.TransformPoint(socket.transform.position);
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                if (node == null) continue;
                if (barrel != null
                    && node.parentComp == barrel.comp
                    && EqualsToken(node.parentSocketId, "muzzle"))
                    directDevice = node;

                if (ContainsToken(node.parentSocketId, "suppressor")
                    || IsDedicatedSuppressor(node))
                {
                    if (suppressor == null || node.depth > suppressor.depth)
                        suppressor = node;
                }
            }

            ModularRenderNode terminal = suppressor ?? directDevice;
            ModularMuzzleEffectKind kind = suppressor != null
                ? ModularMuzzleEffectKind.Suppressor
                : Classify(directDevice);
            if (terminal != null)
            {
                ModularAttachmentMount mount = terminal.Props?.MountNamed(
                    terminal.mountId);
                if (mount?.transform != null)
                {
                    Vector3 tip = mount.transform.position;
                    tip.x += TerminalExtension(terminal, kind);
                    muzzle = terminal.transform.TransformPoint(tip);
                }
            }

            return new ModularMuzzleEffectProfile
            {
                kind = kind,
                localMuzzle = muzzle,
                localAngle = (terminal ?? barrel)?.transform.angle ?? 0f
            };
        }

        private static ModularMuzzleEffectKind Classify(ModularRenderNode node)
        {
            if (node == null) return ModularMuzzleEffectKind.Bare;
            string name = Identity(node);

            if (ContainsToken(name, "muzzlebrake")
                || ContainsToken(name, "muzzle brake")
                || ContainsToken(name, "muzzlebreak")
                || ContainsToken(name, "muzzle break")
                || ContainsToken(name, "m16a2")
                || ContainsToken(name, "nt4"))
                return ModularMuzzleEffectKind.MuzzleBrake;

            if (ContainsToken(name, "flashhider")
                || ContainsToken(name, "flash hider")
                || ContainsToken(name, "flash suppressor")
                || ContainsToken(name, "m16a1")
                || ContainsToken(name, "sf3p"))
                return ModularMuzzleEffectKind.FlashHider;

            if (IsDedicatedSuppressor(node))
                return ModularMuzzleEffectKind.Suppressor;

            // A part occupying the muzzle thread but carrying no more specific name
            // is safest to render as a conventional multi-port flash hider.
            return ModularMuzzleEffectKind.FlashHider;
        }

        private static bool IsDedicatedSuppressor(ModularRenderNode node)
        {
            string defName = node?.thing?.def?.defName;
            if (ContainsToken(defName, "suppressor")
                || ContainsToken(defName, "silencer")
                || ContainsToken(defName, "_supp_"))
                return true;

            string label = node?.thing?.def?.label;
            return !ContainsToken(label, "flash suppressor")
                && (ContainsToken(label, "suppressor")
                    || ContainsToken(label, "silencer"));
        }

        private static float TerminalExtension(
            ModularRenderNode node,
            ModularMuzzleEffectKind kind)
        {
            string identity = Identity(node);
            if (kind == ModularMuzzleEffectKind.Suppressor)
                return ContainsToken(identity, "mp5sd") ? 0.173f : 0.146f;
            if (ContainsToken(identity, "m16a1")) return 0.043f;
            if (ContainsToken(identity, "sf3p")) return 0.068f;
            if (ContainsToken(identity, "nt4")) return 0.052f;
            if (kind == ModularMuzzleEffectKind.MuzzleBrake) return 0.060f;
            return 0.055f;
        }

        private static string Identity(ModularRenderNode node)
        {
            ThingDef def = node?.thing?.def;
            return (def?.defName ?? string.Empty) + " " + (def?.label ?? string.Empty);
        }

        private static bool EqualsToken(string value, string token)
        {
            return string.Equals(value, token, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsToken(string value, string token)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Entry point shared by vanilla and compatibility assemblies through the existing
    /// successful-shot notification. One effect is allowed per weapon per game tick.
    /// </summary>
    public static class ModularWeaponMuzzleEffectUtility
    {
        private static readonly Dictionary<int, int> lastEffectTicks =
            new Dictionary<int, int>();

        public static void NotifyShot(
            Verb verb,
            CompModularWeaponNode root)
        {
            Pawn shooter = verb?.caster as Pawn;
            Thing equipment = verb?.EquipmentSource;
            Map map = shooter?.Map;
            if (root?.Props.isAssemblyRoot != true
                || equipment == null
                || shooter?.Spawned != true
                || map == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            int weaponId = equipment.thingIDNumber;
            int previous;
            if (lastEffectTicks.TryGetValue(weaponId, out previous)
                && previous == now)
                return;
            lastEffectTicks[weaponId] = now;

            ModularMuzzleEffectProfile profile =
                ModularWeaponMuzzleEffectResolver.Resolve(root);
            float coefficient = root.MuzzleFlashCoefficient;
            if (profile == null || coefficient <= 0.001f) return;

            Vector3 target = verb.CurrentTarget.CenterVector3;
            Vector3 shotDirection = target - shooter.DrawPos;
            shotDirection.y = 0f;
            if (shotDirection.sqrMagnitude < 0.0001f)
                shotDirection = shooter.Rotation.FacingCell.ToVector3();
            shotDirection.Normalize();
            float aimAngle = shotDirection.AngleFlat();

            Vector3 gunCenter;
            float bodyAngle;
            bool flipped;
            ModularWeaponAimingPoseUtility.Resolve(
                equipment,
                shooter,
                aimAngle,
                out gunCenter,
                out bodyAngle,
                out flipped);

            Vector2 local = profile.localMuzzle;
            if (flipped) local.x = -local.x;
            Vector3 offset = Quaternion.AngleAxis(bodyAngle, Vector3.up)
                * new Vector3(local.x, 0f, local.y);
            Vector3 muzzle = gunCenter + offset;
            float localYaw = flipped ? profile.localAngle : -profile.localAngle;
            float effectYaw = bodyAngle + localYaw;
            Vector3 forward = Quaternion.AngleAxis(effectYaw, Vector3.up)
                * (flipped ? Vector3.left : Vector3.right);
            forward.y = 0f;
            forward.Normalize();

            map.GetComponent<ModularWeaponMuzzleEffectDrawer>()?.Spawn(
                muzzle,
                effectYaw,
                flipped,
                forward,
                profile.kind,
                coefficient);
        }
    }

    /// <summary>
    /// Draws the directional 256px effect textures with their common authored muzzle
    /// root pinned to the resolved barrel end. Smoke persists and expands after the
    /// short flash; sufficiently low coefficients intentionally spawn smoke only.
    /// </summary>
    public sealed class ModularWeaponMuzzleEffectDrawer : MapComponent
    {
        private const string TextureRoot = "Effects/GunFire/HD_GunFire";
        private const float AuthoredRootToCenter = 0.3125f; // (128 - 48) / 256
        private const float SmokeOnlyThreshold = 0.28f;
        private const int AlphaSteps = 8;

        private sealed class ActiveEffect
        {
            public Vector3 muzzle;
            public Vector3 forward;
            public float yaw;
            public bool flipped;
            public ModularMuzzleEffectKind kind;
            public float coefficient;
            public int startTick;
            public int flashTicks;
            public int smokeTicks;
            public float scaleJitter;
        }

        private static readonly Dictionary<string, Material[]> materialCache =
            new Dictionary<string, Material[]>();
        private readonly List<ActiveEffect> active = new List<ActiveEffect>();

        public ModularWeaponMuzzleEffectDrawer(Map map) : base(map)
        {
        }

        internal void Spawn(
            Vector3 muzzle,
            float yaw,
            bool flipped,
            Vector3 forward,
            ModularMuzzleEffectKind kind,
            float coefficient)
        {
            active.Add(new ActiveEffect
            {
                muzzle = muzzle,
                forward = forward,
                yaw = yaw,
                flipped = flipped,
                kind = kind,
                coefficient = Mathf.Clamp(coefficient, 0.01f, 2.5f),
                startTick = Find.TickManager?.TicksGame ?? 0,
                flashTicks = Rand.RangeInclusive(2, 3),
                smokeTicks = Rand.RangeInclusive(20, 28),
                scaleJitter = Rand.Range(0.92f, 1.08f)
            });
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            if (Find.CurrentMap != map || active.Count == 0) return;

            int now = Find.TickManager?.TicksGame ?? 0;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                ActiveEffect effect = active[i];
                int age = now - effect.startTick;
                if (age < 0 || age > effect.smokeTicks)
                {
                    active.RemoveAt(i);
                    continue;
                }

                DrawSmoke(effect, age);
                if (effect.coefficient >= SmokeOnlyThreshold
                    && age <= effect.flashTicks)
                    DrawFlash(effect, age);
            }
        }

        private static void DrawFlash(ActiveEffect effect, int age)
        {
            float progress = Mathf.Clamp01(age / (float)effect.flashTicks);
            float alpha = 1f - progress;
            float size = Mathf.Clamp(
                0.20f + effect.coefficient * 0.48f,
                0.30f,
                1.25f);
            size *= effect.scaleJitter * Mathf.Lerp(1f, 1.10f, progress);
            DrawPinned(
                effect,
                TexturePath(effect.kind, false),
                ShaderDatabase.MoteGlow,
                size,
                alpha,
                0f);
        }

        private static void DrawSmoke(ActiveEffect effect, int age)
        {
            float progress = Mathf.Clamp01(age / (float)effect.smokeTicks);
            float alpha = Mathf.Lerp(0.58f, 0f, Mathf.SmoothStep(0f, 1f, progress));
            float size = Mathf.Clamp(
                0.33f + Mathf.Sqrt(effect.coefficient) * 0.18f,
                0.36f,
                0.72f);
            size *= effect.scaleJitter * Mathf.Lerp(0.82f, 1.35f, progress);
            DrawPinned(
                effect,
                TexturePath(effect.kind, true),
                ShaderDatabase.Transparent,
                size,
                alpha,
                0f);
        }

        private static void DrawPinned(
            ActiveEffect effect,
            string texturePath,
            Shader shader,
            float size,
            float alpha,
            float forwardDrift)
        {
            if (alpha <= 0.01f) return;
            Material material = EffectMaterial(texturePath, shader, alpha);
            if (material == null) return;

            Vector3 center = effect.muzzle
                + effect.forward * (size * AuthoredRootToCenter + forwardDrift);
            center.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            Mesh mesh = effect.flipped ? MeshPool.plane10Flip : MeshPool.plane10;
            Graphics.DrawMesh(
                mesh,
                Matrix4x4.TRS(
                    center,
                    Quaternion.AngleAxis(effect.yaw, Vector3.up),
                    new Vector3(size, 1f, size)),
                material,
                0);
        }

        private static Material EffectMaterial(
            string path,
            Shader shader,
            float alpha)
        {
            int step = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Clamp01(alpha) * AlphaSteps),
                1,
                AlphaSteps);
            string key = path + "|" + shader.name;
            Material[] variants;
            if (!materialCache.TryGetValue(key, out variants))
            {
                variants = new Material[AlphaSteps];
                materialCache[key] = variants;
            }

            int index = step - 1;
            Material result = variants[index];
            if (result != null) return result;
            if (ContentFinder<Texture2D>.Get(path, false) == null) return null;

            float quantizedAlpha = step / (float)AlphaSteps;
            result = MaterialPool.MatFrom(
                path,
                shader,
                new Color(1f, 1f, 1f, quantizedAlpha));
            variants[index] = result;
            return result;
        }

        private static string TexturePath(
            ModularMuzzleEffectKind kind,
            bool smoke)
        {
            string family;
            switch (kind)
            {
                case ModularMuzzleEffectKind.FlashHider:
                    family = "FlashHider";
                    break;
                case ModularMuzzleEffectKind.MuzzleBrake:
                    // The supplied filename intentionally uses "Break".
                    family = "MuzzleBreak";
                    break;
                case ModularMuzzleEffectKind.Suppressor:
                    family = "Suppressor";
                    break;
                default:
                    family = "Bare";
                    break;
            }
            return TextureRoot + (smoke ? "Smoke" : "Flash") + family;
        }
    }
}
