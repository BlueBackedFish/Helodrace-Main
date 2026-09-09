using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    /// <summary>
    /// Draws Legion-style weapon-light beams once per frame. Keeping this in a map
    /// component avoids the zoom-dependent gaps caused by equipment render callbacks.
    /// </summary>
    public sealed class ModularWeaponFlashlightDrawer : MapComponent
    {
        public ModularWeaponFlashlightDrawer(Map map) : base(map)
        {
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            if (Find.CurrentMap != map) return;

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
                DrawFor(pawns[i]);
        }

        private static void DrawFor(Pawn pawn)
        {
            Stance_Busy stance = pawn?.stances?.curStance as Stance_Busy;
            if (stance == null || stance.neverAimWeapon || !stance.focusTarg.IsValid)
                return;

            ThingWithComps weapon = pawn.equipment?.Primary;
            CompModularWeaponNode comp = weapon?.GetComp<CompModularWeaponNode>();
            if (comp == null || !comp.Props.isAssemblyRoot)
                return;

            bool hasFlashlight = ModularWeaponFlashlightRenderer.HasFlashlight(comp);
            bool hasLaser = ModularWeaponLaserRenderer.HasLaser(comp);
            if (!hasFlashlight && !hasLaser) return;

            Vector3 target = stance.focusTarg.HasThing
                ? stance.focusTarg.Thing.DrawPos
                : stance.focusTarg.Cell.ToVector3Shifted();
            Vector3 direction = target - pawn.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            float aimAngle = direction.AngleFlat();
            Vector3 drawLoc;
            float bodyAngle;
            bool flipped;
            bool recoilActive;
            ModularWeaponAimingPoseUtility.Resolve(
                weapon,
                pawn,
                aimAngle,
                out drawLoc,
                out bodyAngle,
                out flipped,
                out recoilActive);
            Vector3 weaponDirection = WeaponDirection(bodyAngle, flipped);
            if (hasLaser)
                ModularWeaponLaserRenderer.Draw(
                    comp,
                    drawLoc,
                    bodyAngle,
                    flipped,
                    target,
                    recoilActive,
                    weaponDirection);
            if (hasFlashlight)
                ModularWeaponFlashlightRenderer.Draw(
                    comp,
                    drawLoc,
                    bodyAngle,
                    flipped,
                    target,
                    recoilActive,
                    weaponDirection);
        }

        private static Vector3 WeaponDirection(float bodyAngle, bool flipped)
        {
            Vector3 localForward = flipped ? Vector3.left : Vector3.right;
            Vector3 direction = Quaternion.AngleAxis(bodyAngle, Vector3.up)
                * localForward;
            direction.y = 0f;
            return direction.normalized;
        }
    }

    public static class ModularWeaponFlashlightRenderer
    {
        private const int ConeRows = 10;
        private static readonly Dictionary<long, Mesh> coneMeshes =
            new Dictionary<long, Mesh>();
        private static readonly Dictionary<Color, Material> proceduralConeMaterials =
            new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> proceduralPoolMaterials =
            new Dictionary<Color, Material>();
        private static Texture2D proceduralConeTexture;
        private static Texture2D proceduralPoolTexture;

        public static bool HasFlashlight(CompModularWeaponNode comp)
        {
            List<ModularRenderNode> nodes = comp?.RenderSnapshot();
            if (nodes == null) return false;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].Props.flashlight != null) return true;
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
                ModularWeaponFlashlightProperties light = node.Props.flashlight;
                if (light == null) continue;

                Vector3 localPoint = node.Props.graphicOffset + light.emitterOffset;
                Vector2 local = node.transform.TransformPoint(localPoint);
                if (flipped) local.x = -local.x;
                Vector2 worldOffset = RotateForWorldYaw(local, bodyAngle);
                Vector3 origin = drawLoc
                    + new Vector3(worldOffset.x, 0f, worldOffset.y);
                origin.y = AltitudeLayer.MoteOverhead.AltitudeFor();

                Vector3 delta = target - origin;
                delta.y = 0f;
                float length = delta.magnitude;
                if (length < 0.05f || length > light.maxRange) continue;
                Vector3 beamTarget = target;
                if (alignToWeapon && weaponDirection.sqrMagnitude > 0.5f)
                {
                    delta = weaponDirection * length;
                    beamTarget = origin + delta;
                    beamTarget.y = origin.y;
                }

                float nearWidth = light.nearWidth;
                float farWidth = light.farWidth;
                float poolRadius = light.circleRadius;
                if (light.closeRange > 0.01f && length < light.closeRange)
                {
                    float scale = Mathf.Max(
                        light.closeRangeMinScale,
                        length / light.closeRange);
                    farWidth *= scale;
                    poolRadius *= scale;
                }
                nearWidth = Mathf.Min(nearWidth, farWidth);

                Quaternion rotation = Quaternion.AngleAxis(
                    delta.AngleFlat(),
                    Vector3.up);
                Material coneMaterial = LightMaterial(
                    light.coneTexPath,
                    light.color,
                    false);
                if (coneMaterial != null)
                {
                    float shaftLength = Mathf.Max(
                        0.05f,
                        length + light.lengthOvershoot);
                    Graphics.DrawMesh(
                        ConeMesh(nearWidth, farWidth),
                        Matrix4x4.TRS(
                            origin,
                            rotation,
                            new Vector3(1f, 1f, shaftLength)),
                        coneMaterial,
                        0);
                }

                if (!light.drawCircle || poolRadius <= 0f) continue;
                Material poolMaterial = LightMaterial(
                    light.circleTexPath,
                    light.color,
                    true);
                if (poolMaterial == null) continue;

                Vector3 pool = beamTarget;
                pool.y = origin.y + 0.001f;
                float diameter = poolRadius * 2f;
                Graphics.DrawMesh(
                    MeshPool.plane10,
                    Matrix4x4.TRS(
                        pool,
                        Quaternion.identity,
                        new Vector3(diameter, 1f, diameter)),
                    poolMaterial,
                    0);
            }
        }

        private static Vector2 RotateForWorldYaw(Vector2 value, float degrees)
        {
            Vector3 rotated = Quaternion.AngleAxis(degrees, Vector3.up)
                * new Vector3(value.x, 0f, value.y);
            return new Vector2(rotated.x, rotated.z);
        }

        private static Material LightMaterial(string path, Color color, bool pool)
        {
            if (!path.NullOrEmpty())
            {
                Texture2D texture = ContentFinder<Texture2D>.Get(path, false);
                if (texture != null)
                    return MaterialPool.MatFrom(path, ShaderDatabase.MoteGlow, color);
            }

            Dictionary<Color, Material> cache = pool
                ? proceduralPoolMaterials
                : proceduralConeMaterials;
            Material result;
            if (cache.TryGetValue(color, out result) && result != null) return result;

            Texture2D fallback = pool
                ? (proceduralPoolTexture ?? (proceduralPoolTexture = MakePoolTexture()))
                : (proceduralConeTexture ?? (proceduralConeTexture = MakeConeTexture()));
            result = new Material(ShaderDatabase.MoteGlow)
            {
                name = pool
                    ? "Helodrace modular flashlight pool"
                    : "Helodrace modular flashlight cone",
                mainTexture = fallback,
                color = color
            };
            cache[color] = result;
            return result;
        }

        private static Texture2D MakeConeTexture()
        {
            const int width = 32;
            const int height = 64;
            Texture2D texture = new Texture2D(
                width,
                height,
                TextureFormat.ARGB32,
                false)
            {
                name = "Helodrace modular flashlight cone gradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float vertical = (float)y / (height - 1);
                float lengthAlpha = Mathf.Lerp(1f, 0.22f, vertical);
                for (int x = 0; x < width; x++)
                {
                    float horizontal = Mathf.Abs((float)x / (width - 1) * 2f - 1f);
                    float edgeAlpha = 1f - Mathf.SmoothStep(0.55f, 1f, horizontal);
                    byte alpha = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(lengthAlpha * edgeAlpha) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D MakePoolTexture()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(
                size,
                size,
                TextureFormat.ARGB32,
                false)
            {
                name = "Helodrace modular flashlight pool gradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = (float)y / (size - 1) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (float)x / (size - 1) * 2f - 1f;
                    float radius = Mathf.Sqrt(nx * nx + ny * ny);
                    float alpha = 1f - Mathf.SmoothStep(0.05f, 1f, radius);
                    pixels[y * size + x] = new Color32(
                        255,
                        255,
                        255,
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Mesh ConeMesh(float nearWidth, float farWidth)
        {
            int nearKey = Mathf.Max(1, Mathf.RoundToInt(nearWidth * 100f));
            int farKey = Mathf.Max(1, Mathf.RoundToInt(farWidth * 100f));
            long key = ((long)nearKey << 32) | (uint)farKey;
            Mesh mesh;
            if (coneMeshes.TryGetValue(key, out mesh) && mesh != null) return mesh;

            float nearHalf = nearKey / 100f * 0.5f;
            float farHalf = farKey / 100f * 0.5f;
            int rows = ConeRows + 1;
            Vector3[] vertices = new Vector3[rows * 3];
            Vector2[] uvs = new Vector2[rows * 3];
            for (int row = 0; row < rows; row++)
            {
                float progress = (float)row / ConeRows;
                float half = Mathf.Lerp(nearHalf, farHalf, progress);
                int index = row * 3;
                vertices[index] = new Vector3(-half, 0f, progress);
                vertices[index + 1] = new Vector3(0f, 0f, progress);
                vertices[index + 2] = new Vector3(half, 0f, progress);
                uvs[index] = new Vector2(0f, progress);
                uvs[index + 1] = new Vector2(0.5f, progress);
                uvs[index + 2] = new Vector2(1f, progress);
            }

            int[] triangles = new int[ConeRows * 12];
            int triangleIndex = 0;
            for (int row = 0; row < ConeRows; row++)
            {
                int current = row * 3;
                int next = (row + 1) * 3;
                triangles[triangleIndex++] = current;
                triangles[triangleIndex++] = next;
                triangles[triangleIndex++] = next + 1;
                triangles[triangleIndex++] = current;
                triangles[triangleIndex++] = next + 1;
                triangles[triangleIndex++] = current + 1;
                triangles[triangleIndex++] = current + 1;
                triangles[triangleIndex++] = next + 1;
                triangles[triangleIndex++] = next + 2;
                triangles[triangleIndex++] = current + 1;
                triangles[triangleIndex++] = next + 2;
                triangles[triangleIndex++] = current + 2;
            }

            mesh = new Mesh
            {
                name = "Helodrace modular flashlight cone " + nearKey + " " + farKey,
                vertices = vertices,
                uv = uvs
            };
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            coneMeshes[key] = mesh;
            return mesh;
        }
    }

    public sealed class HediffCompProperties_ModularFlashlightDazzle
        : HediffCompProperties
    {
        public int graceTicks = 3;
        public float fadePerTick = 0.15f;

        public HediffCompProperties_ModularFlashlightDazzle()
        {
            compClass = typeof(HediffComp_ModularFlashlightDazzle);
        }
    }

    public sealed class HediffComp_ModularFlashlightDazzle : HediffComp
    {
        private int lastLitTick = -9999;
        private int litTick = -9999;
        private float accumulated;

        private HediffCompProperties_ModularFlashlightDazzle Props =>
            (HediffCompProperties_ModularFlashlightDazzle)props;

        public void NotifyLit(float intensity)
        {
            int now = Find.TickManager.TicksGame;
            if (litTick != now)
            {
                litTick = now;
                accumulated = 0f;
            }

            accumulated += intensity;
            lastLitTick = now;
            parent.Severity = Mathf.Min(accumulated, parent.def.maxSeverity);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (Find.TickManager.TicksGame - lastLitTick > Props.graceTicks)
                severityAdjustment -= Props.fadePerTick;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref lastLitTick, "lastLitTick", -9999);
        }

        public override string CompDebugString()
        {
            return "lights this tick: " + accumulated.ToString("0.##")
                + "\nlast lit: " + (Find.TickManager.TicksGame - lastLitTick)
                + " ticks ago";
        }
    }

    [HarmonyPatch(
        typeof(Pawn_EquipmentTracker),
        nameof(Pawn_EquipmentTracker.EquipmentTrackerTick))]
    public static class Patch_EquipmentTrackerTick_ModularFlashlight
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_EquipmentTracker __instance)
        {
            Pawn pawn = __instance.pawn;
            if (pawn == null || !pawn.Spawned) return;

            CompModularWeaponNode comp = __instance.Primary
                ?.GetComp<CompModularWeaponNode>();
            if (comp == null || !comp.Props.isAssemblyRoot) return;

            Stance_Busy stance = pawn.stances?.curStance as Stance_Busy;
            if (stance == null || stance.neverAimWeapon
                || !stance.focusTarg.HasThing)
                return;

            Pawn victim = stance.focusTarg.Thing as Pawn;
            if (victim == null || victim.Dead || victim.health == null) return;

            List<ModularRenderNode> nodes = comp.RenderSnapshot();
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularWeaponFlashlightProperties light = nodes[i].Props.flashlight;
                if (light == null || !Applies(light, victim)) continue;
                if (victim.Position.DistanceTo(pawn.Position) > light.maxRange) continue;

                HediffDef hediffDef = light.hediff;
                float intensity = light.intensity;
                float severityPerSecond = 0f;
                bool requireExisting = false;
                if (!light.targetOverrides.NullOrEmpty())
                {
                    for (int j = 0; j < light.targetOverrides.Count; j++)
                    {
                        ModularFlashlightTargetOverride targetOverride =
                            light.targetOverrides[j];
                        if (targetOverride == null || !targetOverride.Matches(victim))
                            continue;
                        if (targetOverride.hediff != null)
                            hediffDef = targetOverride.hediff;
                        intensity = targetOverride.intensity;
                        severityPerSecond = targetOverride.severityPerSecond;
                        requireExisting = targetOverride.requireExisting;
                        break;
                    }
                }

                if (hediffDef == null) continue;
                Hediff hediff = victim.health.hediffSet
                    .GetFirstHediffOfDef(hediffDef);
                if (hediff == null)
                {
                    if (requireExisting) continue;
                    hediff = HediffMaker.MakeHediff(hediffDef, victim);
                    hediff.Severity = 0.01f;
                    victim.health.AddHediff(hediff);
                }

                HediffComp_ModularFlashlightDazzle dazzle =
                    (hediff as HediffWithComps)
                        ?.TryGetComp<HediffComp_ModularFlashlightDazzle>();
                if (dazzle != null)
                {
                    dazzle.NotifyLit(intensity);
                    continue;
                }

                if (severityPerSecond > 0f)
                    hediff.Severity = Mathf.Min(
                        hediff.Severity + severityPerSecond / 60f,
                        hediffDef.maxSeverity);
            }
        }

        private static bool Applies(
            ModularWeaponFlashlightProperties light,
            Pawn victim)
        {
            if (victim.RaceProps == null) return true;
            if (victim.RaceProps.IsMechanoid) return light.affectsMechanoids;
            if (victim.RaceProps.Animal) return light.affectsAnimals;
            return true;
        }
    }
}
