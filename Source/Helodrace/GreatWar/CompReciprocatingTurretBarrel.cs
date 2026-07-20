using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class CompProperties_ReciprocatingTurretBarrel : CompProperties
    {
        public string barrelTexPath;
        public string bodyTexPath;
        public float drawSize = 1.2f;
        public float drawOffset;
        public float recoilDistance = 0.075f;
        public int recoilTicks = 4;

        public CompProperties_ReciprocatingTurretBarrel()
        {
            compClass = typeof(CompReciprocatingTurretBarrel);
        }
    }

    /// <summary>
    /// Draws a turret top as two aligned layers. Only the barrel layer moves,
    /// allowing weapons such as the M2HB to cycle without moving the receiver.
    /// </summary>
    public class CompReciprocatingTurretBarrel : ThingComp
    {
        private int lastShotTick = -99999;
        private Material barrelMaterial;
        private Material bodyMaterial;

        private CompProperties_ReciprocatingTurretBarrel Props =>
            (CompProperties_ReciprocatingTurretBarrel)props;

        public void NotifyShotFired()
        {
            lastShotTick = Find.TickManager.TicksGame;
        }

        public override void PostDraw()
        {
            base.PostDraw();

            Building_TurretGun turret = parent as Building_TurretGun;
            if (turret == null || turret.Top == null)
            {
                return;
            }

            float angle = turret.Top.CurRotation;
            Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.up);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 drawPos = parent.DrawPos;
            drawPos += forward * Props.drawOffset;
            drawPos.y = AltitudeLayer.BuildingOnTop.AltitudeFor();

            Vector3 scale = new Vector3(Props.drawSize / 10f, 1f, Props.drawSize / 10f);
            Vector3 barrelPos = drawPos - forward * CurrentRecoil;

            // The supplied textures are authored on the same 512x512 canvas:
            // barrel first, then body directly over it with no rest offset.
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(barrelPos, rotation, scale), BarrelMaterial, 0);
            drawPos.y += 0.001f;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawPos, rotation, scale), BodyMaterial, 0);
        }

        private float CurrentRecoil
        {
            get
            {
                int age = Find.TickManager.TicksGame - lastShotTick;
                if (age < 0 || age >= Props.recoilTicks)
                {
                    return 0f;
                }

                return Props.recoilDistance * (1f - age / (float)Props.recoilTicks);
            }
        }

        private Material BarrelMaterial => barrelMaterial ??
            (barrelMaterial = MaterialPool.MatFrom(Props.barrelTexPath, ShaderDatabase.Cutout));

        private Material BodyMaterial => bodyMaterial ??
            (bodyMaterial = MaterialPool.MatFrom(Props.bodyTexPath, ShaderDatabase.Cutout));
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot_ReciprocatingTurretBarrel
    {
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (__result && __instance.Caster is Building_TurretGun turret)
            {
                turret.TryGetComp<CompReciprocatingTurretBarrel>()?.NotifyShotFired();
            }
        }
    }
}
