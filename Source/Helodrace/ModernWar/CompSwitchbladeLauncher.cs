using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace
{
    public static class SwitchbladeLauncherUtility
    {
        private const string LauncherDefName = "HD_Building_Switchblade600Launcher";
        private const string TabletDefName = "HD_MilitaryTablet";
        private const string DroneDefName = "HD_Switchblade600_Drone";
        private const float DefaultLoiterRadius = 8f;
        private const int DefaultLoiterDistance = 18;
        private static readonly Dictionary<int, int> lastRejectMessageTicks = new Dictionary<int, int>();

        public static bool CanOperate(Thing launcher, out Pawn operatorPawn)
        {
            operatorPawn = launcher?.TryGetComp<CompMannable>()?.ManningPawn;
            if (!HasLoadedMunition(launcher))
            {
                return false;
            }

            return FindTablet(operatorPawn) != null;
        }

        public static bool IsSwitchbladeLauncher(Thing thing)
        {
            return thing?.def?.defName == LauncherDefName;
        }

        public static void NotifyCannotOperate(Thing launcher)
        {
            int thingId = launcher?.thingIDNumber ?? 0;
            int ticksGame = Find.TickManager.TicksGame;
            if (lastRejectMessageTicks.TryGetValue(thingId, out int lastTick) && ticksGame - lastTick < 120)
            {
                return;
            }

            lastRejectMessageTicks[thingId] = ticksGame;
            string messageKey = !HasLoadedMunition(launcher)
                ? "HD_SwitchbladeLauncher_RequiresAmmo"
                : "HD_SwitchbladeLauncher_RequiresTablet";
            Messages.Message(messageKey.Translate(), launcher, MessageTypeDefOf.RejectInput, false);
        }

        public static bool HasLoadedMunition(Thing launcher)
        {
            return FindProjectileComp(launcher)?.Loaded == true;
        }

        public static CompChangeableProjectile FindProjectileComp(Thing launcher)
        {
            CompChangeableProjectile comp = launcher?.TryGetComp<CompChangeableProjectile>();
            if (comp != null)
            {
                return comp;
            }

            return (launcher as Building_TurretGun)?.GunCompEq?.parent?.TryGetComp<CompChangeableProjectile>();
        }

        public static CompSwitchbladeTablet FindTablet(Pawn pawn)
        {
            return pawn?.apparel?.WornApparel?
                .Select(apparel => apparel.TryGetComp<CompSwitchbladeTablet>())
                .FirstOrDefault(comp => comp?.CanLinkDrone() == true);
        }

        public static bool TryLaunchDrone(Thing launcher, LocalTargetInfo target, out SwitchbladeDrone drone)
        {
            drone = null;
            if (launcher?.Map == null || !CanOperate(launcher, out Pawn operatorPawn))
            {
                NotifyCannotOperate(launcher);
                return false;
            }

            ThingDef droneDef = DefDatabase<ThingDef>.GetNamedSilentFail(DroneDefName);
            if (droneDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_Switchblade600_Drone ThingDef is missing.", 97160301);
                return false;
            }

            CompSwitchbladeTablet tablet = FindTablet(operatorPawn);
            if (tablet == null)
            {
                NotifyCannotOperate(launcher);
                return false;
            }

            CompChangeableProjectile projectileComp = FindProjectileComp(launcher);
            if (projectileComp?.Loaded != true)
            {
                NotifyCannotOperate(launcher);
                return false;
            }

            Vector3 launchDirection = LaunchDirectionFor(launcher, target);
            IntVec3 spawnCell = launcher.Position + CellForDirection(launchDirection);
            if (!spawnCell.InBounds(launcher.Map))
            {
                spawnCell = launcher.Position;
            }

            Thing thing = GenSpawn.Spawn(ThingMaker.MakeThing(droneDef), spawnCell, launcher.Map);
            FleckMaker.ThrowSmoke(spawnCell.ToVector3Shifted(), launcher.Map, 0.55f);
            SoundDef.Named("HD_DroneFlight_Start").PlayOneShot(new TargetInfo(spawnCell, launcher.Map));
            drone = thing as SwitchbladeDrone;
            if (drone == null)
            {
                thing.Destroy();
                Log.ErrorOnce("Helodrace: HD_Switchblade600_Drone does not use SwitchbladeDrone thingClass.", 97160302);
                return false;
            }

            IntVec3 loiterCenter = (launcher.Position.ToVector3Shifted() + launchDirection * DefaultLoiterDistance).ToIntVec3();
            loiterCenter = ClampToMap(loiterCenter, launcher.Map);
            drone.InitializeLoiter(loiterCenter, DefaultLoiterRadius, tablet.parent, operatorPawn, launchDirection);
            tablet.SetActiveDrone(drone);
            projectileComp.Notify_ProjectileLaunched();
            launcher.TryGetComp<CompSwitchbladeLauncher>()?.MarkFired();
            return true;
        }

        private static Vector3 LaunchDirectionFor(Thing launcher, LocalTargetInfo target)
        {
            Vector3 direction = target.IsValid
                ? target.Cell.ToVector3Shifted() - launcher.Position.ToVector3Shifted()
                : launcher.Rotation.FacingCell.ToVector3();
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = launcher.Rotation.FacingCell.ToVector3();
            }

            return direction.normalized;
        }

        private static IntVec3 CellForDirection(Vector3 direction)
        {
            int x = Mathf.RoundToInt(direction.x);
            int z = Mathf.RoundToInt(direction.z);
            if (x == 0 && z == 0)
            {
                z = 1;
            }

            return new IntVec3(x, 0, z);
        }

        private static IntVec3 ClampToMap(IntVec3 cell, Map map)
        {
            return new IntVec3(
                Mathf.Clamp(cell.x, 0, map.Size.x - 1),
                0,
                Mathf.Clamp(cell.z, 0, map.Size.z - 1));
        }
    }

    public class CompProperties_SwitchbladeLauncher : CompProperties
    {
        public CompProperties_SwitchbladeLauncher()
        {
            compClass = typeof(CompSwitchbladeLauncher);
        }
    }

    public class CompSwitchbladeLauncher : ThingComp
    {
        private bool fired;
        private static Material loadedMaterial;
        private static Material firedMaterial;

        public bool Fired => fired;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref fired, "fired");
        }

        public void MarkFired()
        {
            fired = true;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            Vector3 drawPos = parent.DrawPos;
            drawPos.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            Vector3 scale = new Vector3(1.25f / 10f, 1f, 1.25f / 10f);
            Quaternion rotation = parent.Rotation.AsQuat;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawPos, rotation, scale), CurrentMaterial, 0);
        }

        private Material CurrentMaterial => SwitchbladeLauncherUtility.HasLoadedMunition(parent) ? LoadedMaterial : FiredMaterial;
        private static Material LoadedMaterial => loadedMaterial ?? (loadedMaterial = MaterialPool.MatFrom("Building/Security/HD_SwitchBlade600_Canister", ShaderDatabase.Cutout));
        private static Material FiredMaterial => firedMaterial ?? (firedMaterial = MaterialPool.MatFrom("Building/Security/HD_SwitchBlade600_CanisterUsed", ShaderDatabase.Cutout));
    }

    public static class SwitchbladeLauncherVerbPatches
    {
        public static bool Prefix(Verb verb, ref bool result)
        {
            Thing caster = verb?.Caster;
            if (!SwitchbladeLauncherUtility.IsSwitchbladeLauncher(caster))
            {
                return true;
            }

            if (SwitchbladeLauncherUtility.CanOperate(caster, out _))
            {
                return true;
            }

            SwitchbladeLauncherUtility.NotifyCannotOperate(caster);
            result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_SwitchbladeLauncher_Primary
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            return SwitchbladeLauncherVerbPatches.Prefix(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_SwitchbladeLauncher_WithDestination
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            return SwitchbladeLauncherVerbPatches.Prefix(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot_SwitchbladeLauncher
    {
        public static bool Prefix(Verb_LaunchProjectile __instance, ref bool __result)
        {
            Thing caster = __instance?.Caster;
            if (!SwitchbladeLauncherUtility.IsSwitchbladeLauncher(caster))
            {
                return true;
            }

            __result = SwitchbladeLauncherUtility.TryLaunchDrone(caster, __instance.CurrentTarget, out _);
            return false;
        }
    }
}
