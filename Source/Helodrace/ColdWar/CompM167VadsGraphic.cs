using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace
{
    public class CompProperties_M167VadsGraphic : CompProperties
    {
        public string topOutlineTexPath;
        public List<string> barrelTexPaths = new List<string>();
        public List<string> barrelOutlineTexPaths = new List<string>();
        public float drawSize = 6f;
        public float rotationOffsetDegrees = -90f;
        public Vector2 turretCenterOffset = Vector2.zero;
        public string casingTexPath;
        public float casingDrawSize = 6f;
        public float casingPortDistance = 1f;
        public float casingPortRightOffset = 0.5f;
        public int casingRandomStartTicks = 15;
        public string casingDropSoundDef = "HD_LargeShellDrop";
        public int casingDropSoundTick = 44;
        public float casingDropSoundVolume = 0.15f;

        public CompProperties_M167VadsGraphic()
        {
            compClass = typeof(CompM167VadsGraphic);
        }
    }

    /// <summary>
    /// Adds the M167's animated barrel layers without replacing the vanilla
    /// building-base or turret-top graphics.
    /// </summary>
    public class CompM167VadsGraphic : ThingComp
    {
        private sealed class CasingVisual
        {
            public int spawnTick;
            public Vector3 origin;
            public Vector3 initialVelocity;
            public Vector3 randomVelocity;
            public float initialAngle;
            public float spinDegreesPerTick;
            public bool dropSoundPlayed;
        }

        private const int CasingLifetimeTicks = 45;
        private static readonly System.Reflection.FieldInfo BurstWarmupTicksLeftField =
            typeof(Building_TurretGun).GetField(
                "burstWarmupTicksLeft",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private int barrelFrame;
        private int warmupDurationTicks;
        private float frameProgress;
        private float spinSpeedFramesPerTick;
        private bool wasWarmingUp;
        private int lastCasingDropSoundTick = -99999;
        private readonly List<CasingVisual> casings = new List<CasingVisual>();
        private Material casingMaterial;
        private Material topOutlineMaterial;
        private readonly List<Material> barrelMaterials = new List<Material>();
        private readonly List<Material> barrelOutlineMaterials = new List<Material>();

        private CompProperties_M167VadsGraphic Props =>
            (CompProperties_M167VadsGraphic)props;

        public Vector3 TurretCenterWorldOffset
        {
            get
            {
                Vector3 localOffset = new Vector3(
                    Props.turretCenterOffset.x,
                    0f,
                    Props.turretCenterOffset.y);
                return Quaternion.AngleAxis(parent.Rotation.AsAngle, Vector3.up) * localOffset;
            }
        }

        public void NotifyShotFired()
        {
            Building_TurretGun turret = parent as Building_TurretGun;
            if (turret?.Top == null)
            {
                return;
            }

            float aimAngle = turret.Top.CurRotation;
            Quaternion aimRotation = Quaternion.AngleAxis(aimAngle, Vector3.up);

            Vector3 forward = aimRotation * Vector3.forward;
            Vector3 right = aimRotation * Vector3.right;
            casings.Add(new CasingVisual
            {
                spawnTick = Find.TickManager.TicksGame,
                origin = parent.DrawPos
                    + TurretCenterWorldOffset
                    + forward * Props.casingPortDistance
                    + right * Props.casingPortRightOffset,
                initialVelocity = right * 0.05f,
                randomVelocity = right * Rand.Range(-0.015f, 0.015f)
                    + forward * Rand.Range(-0.02f, 0.02f),
                initialAngle = aimAngle,
                spinDegreesPerTick = Rand.Range(-24f, 24f)
            });

            if (casings.Count > 128)
            {
                casings.RemoveAt(0);
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            Building_TurretGun turret = parent as Building_TurretGun;
            if (turret?.Top == null)
            {
                return;
            }

            Pawn gunner = parent.TryGetComp<CompMannable>()?.ManningPawn;
            if (gunner != null && gunner.Spawned && !gunner.Dead)
            {
                gunner.Rotation = Rot4.FromAngleFlat(turret.Top.CurRotation);
            }

            UpdateBarrelAnimation(turret);

            int currentTick = Find.TickManager.TicksGame;
            PlayCasingDropSounds(currentTick);
            casings.RemoveAll(casing => currentTick - casing.spawnTick >= CasingLifetimeTicks);
        }

        public override void PostDraw()
        {
            base.PostDraw();

            Building_TurretGun turret = parent as Building_TurretGun;
            if (turret == null || turret.Top == null)
            {
                return;
            }

            EnsureMaterials();
            int frameCount = Mathf.Min(barrelMaterials.Count, barrelOutlineMaterials.Count);
            if (frameCount == 0 || topOutlineMaterial == null)
            {
                return;
            }

            int frame = barrelFrame % frameCount;
            Quaternion rotation = Quaternion.AngleAxis(
                turret.Top.CurRotation + Props.rotationOffsetDegrees,
                Vector3.up);
            // MeshPool.plane10 scaling was authored at one tenth of the size
            // required by these additional 1024x1024 M167 layers.
            Vector3 scale = new Vector3(Props.drawSize, 1f, Props.drawSize);

            // Every *OutLine texture sits below both vanilla graphics: the
            // building base and the turret top. The filled barrel is uppermost.
            Vector3 outlinePos = parent.DrawPos + TurretCenterWorldOffset;
            outlinePos.y = AltitudeLayer.Building.AltitudeFor() - 0.001f;
            Matrix4x4 outlineMatrix = Matrix4x4.TRS(outlinePos, rotation, scale);
            Graphics.DrawMesh(MeshPool.plane10, outlineMatrix, topOutlineMaterial, 0);
            Graphics.DrawMesh(MeshPool.plane10, outlineMatrix, barrelOutlineMaterials[frame], 0);

            Vector3 barrelPos = outlinePos;
            barrelPos.y = AltitudeLayer.BuildingOnTop.AltitudeFor() + 0.001f;
            Graphics.DrawMesh(
                MeshPool.plane10,
                Matrix4x4.TRS(barrelPos, rotation, scale),
                barrelMaterials[frame],
                0);

            DrawCasings();
        }

        private void DrawCasings()
        {
            if (casingMaterial == null || casings.Count == 0)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            Vector3 casingScale = new Vector3(
                Props.casingDrawSize / 10f,
                1f,
                Props.casingDrawSize / 10f);

            for (int i = 0; i < casings.Count; i++)
            {
                CasingVisual casing = casings[i];
                int age = currentTick - casing.spawnTick;
                int randomAge = Mathf.Max(0, age - Props.casingRandomStartTicks);
                float progress = Mathf.Clamp01(age / (float)CasingLifetimeTicks);
                Vector3 position = casing.origin
                    + casing.initialVelocity * age
                    + casing.randomVelocity * randomAge;
                position.y = AltitudeLayer.BuildingOnTop.AltitudeFor()
                    + 0.002f
                    + Mathf.Sin(progress * Mathf.PI) * 0.018f;

                float angle = casing.initialAngle
                    + casing.spinDegreesPerTick * randomAge;
                Graphics.DrawMesh(
                    MeshPool.plane10,
                    Matrix4x4.TRS(
                        position,
                        Quaternion.AngleAxis(angle, Vector3.up),
                        casingScale),
                    casingMaterial,
                    0);
            }
        }

        private void PlayCasingDropSounds(int currentTick)
        {
            if (parent.Map == null || currentTick == lastCasingDropSoundTick)
            {
                return;
            }

            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail(Props.casingDropSoundDef);
            if (sound == null)
            {
                return;
            }

            for (int i = 0; i < casings.Count; i++)
            {
                CasingVisual casing = casings[i];
                int age = currentTick - casing.spawnTick;
                if (casing.dropSoundPlayed || age < Props.casingDropSoundTick)
                {
                    continue;
                }

                casing.dropSoundPlayed = true;
                int randomAge = Mathf.Max(0, age - Props.casingRandomStartTicks);
                Vector3 landingPosition = casing.origin
                    + casing.initialVelocity * age
                    + casing.randomVelocity * randomAge;
                IntVec3 soundCell = landingPosition.ToIntVec3();
                if (!soundCell.InBounds(parent.Map))
                {
                    soundCell = parent.Position;
                }

                SoundInfo soundInfo = SoundInfo.InMap(
                    new TargetInfo(soundCell, parent.Map),
                    MaintenanceType.None);
                soundInfo.volumeFactor = Props.casingDropSoundVolume;
                SoundStarter.PlayOneShot(sound, soundInfo);
                lastCasingDropSoundTick = currentTick;
                return;
            }
        }

        private void UpdateBarrelAnimation(Building_TurretGun turret)
        {
            int frameCount = Mathf.Min(Props.barrelTexPaths.Count, Props.barrelOutlineTexPaths.Count);
            if (frameCount <= 0)
            {
                return;
            }

            int remainingWarmupTicks = WarmupTicksRemaining(turret);
            bool warmingUp = remainingWarmupTicks > 0;
            bool bursting = turret.GunCompEq?.PrimaryVerb?.Bursting == true;

            if (!warmingUp && !bursting)
            {
                // Preserve rotational inertia after a burst. From the maximum
                // speed of 0.5 frames/tick this reaches rest in about 120 ticks.
                spinSpeedFramesPerTick = Mathf.MoveTowards(
                    spinSpeedFramesPerTick,
                    0f,
                    1f / 120f);
                warmupDurationTicks = 0;
                wasWarmingUp = false;
            }
            else if (bursting)
            {
                // At maximum speed update every tick and skip the visually
                // redundant intermediate phases: 1 -> 3 -> 1 -> 3.
                spinSpeedFramesPerTick = 1f;
                barrelFrame = barrelFrame == 0 ? 2 : 0;
                frameProgress = 0f;
            }
            else
            {
                int remainingTicks = remainingWarmupTicks;
                if (!wasWarmingUp || warmupDurationTicks <= 0)
                {
                    warmupDurationTicks = Mathf.Max(1, remainingTicks);
                    frameProgress = 0f;
                }

                float warmupProgress = 1f
                    - Mathf.Clamp01(remainingTicks / (float)Mathf.Max(1, warmupDurationTicks));

                // Start at one frame per twelve ticks, then accelerate smoothly
                // toward one frame per tick immediately before firing.
                float warmupSpeed = Mathf.Lerp(1f / 12f, 1f, warmupProgress);
                spinSpeedFramesPerTick = Mathf.Max(spinSpeedFramesPerTick, warmupSpeed);
            }

            wasWarmingUp = warmingUp;
            if (!bursting)
            {
                frameProgress += spinSpeedFramesPerTick;
                while (frameProgress >= 1f)
                {
                    frameProgress -= 1f;
                    barrelFrame = (barrelFrame + 1) % frameCount;
                }
            }
        }

        private static int WarmupTicksRemaining(Building_TurretGun turret)
        {
            if (turret == null || BurstWarmupTicksLeftField == null)
            {
                return 0;
            }

            return Mathf.Max(0, (int)BurstWarmupTicksLeftField.GetValue(turret));
        }

        private void EnsureMaterials()
        {
            if (casingMaterial == null && !Props.casingTexPath.NullOrEmpty())
            {
                casingMaterial = MaterialPool.MatFrom(Props.casingTexPath, ShaderDatabase.Cutout);
            }

            if (topOutlineMaterial == null && !Props.topOutlineTexPath.NullOrEmpty())
            {
                topOutlineMaterial = MaterialPool.MatFrom(Props.topOutlineTexPath, ShaderDatabase.Cutout);
            }

            if (barrelMaterials.Count == 0)
            {
                for (int i = 0; i < Props.barrelTexPaths.Count; i++)
                {
                    barrelMaterials.Add(MaterialPool.MatFrom(Props.barrelTexPaths[i], ShaderDatabase.Cutout));
                }
            }

            if (barrelOutlineMaterials.Count == 0)
            {
                for (int i = 0; i < Props.barrelOutlineTexPaths.Count; i++)
                {
                    barrelOutlineMaterials.Add(
                        MaterialPool.MatFrom(Props.barrelOutlineTexPaths[i], ShaderDatabase.Cutout));
                }
            }
        }
    }

    /// <summary>
    /// Vanilla turretTopOffset does not follow the building's Rot4 in the same
    /// coordinate space as directional base offsets. Inject the M167's local
    /// center offset into the vanilla top draw location after rotating it by
    /// the placed building direction.
    /// </summary>
    [HarmonyPatch(typeof(TurretTop), nameof(TurretTop.DrawTurret))]
    public static class Patch_TurretTop_DrawTurret_M167DirectionalOffset
    {
        private static readonly System.Reflection.FieldInfo ParentTurretField =
            AccessTools.Field(typeof(TurretTop), "parentTurret");

        public static void Prefix(TurretTop __instance, ref Vector3 drawLoc)
        {
            Building_TurretGun turret =
                ParentTurretField?.GetValue(__instance) as Building_TurretGun;
            CompM167VadsGraphic graphic = turret?.TryGetComp<CompM167VadsGraphic>();
            if (graphic != null)
            {
                drawLoc += graphic.TurretCenterWorldOffset;
            }
        }
    }
}
