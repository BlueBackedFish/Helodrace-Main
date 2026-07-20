using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class CompProperties_TowLauncher : CompProperties
    {
        public CompProperties_TowLauncher()
        {
            compClass = typeof(CompTowLauncher);
        }
    }

    public class CompTowLauncher : ThingComp
    {
        private Projectile_Tow activeMissile;
        private int lastBlockedFireMessageTick = -99999;

        public Projectile_Tow ActiveMissile
        {
            get
            {
                if (activeMissile != null && !activeMissile.Destroyed && activeMissile.Spawned)
                {
                    return activeMissile;
                }

                activeMissile = null;
                if (parent.Map == null)
                {
                    return null;
                }

                foreach (Thing thing in parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile))
                {
                    if (thing is Projectile_Tow missile
                        && !missile.Destroyed
                        && missile.Spawned
                        && missile.LauncherBuilding == parent)
                    {
                        activeMissile = missile;
                        break;
                    }
                }

                return activeMissile;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref activeMissile, "activeTowMissile");
        }

        public void SetActiveMissile(Projectile_Tow missile)
        {
            activeMissile = missile;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            Projectile_Tow missile = ActiveMissile;
            if (parent.Faction != Faction.OfPlayer || missile == null || missile.Map != parent.Map)
            {
                yield break;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel = "HD_TOW_ChangeTarget_Label".Translate().ToString(),
                defaultDesc = "HD_TOW_ChangeTarget_Desc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ?? BaseContent.BadTex,
                action = BeginChangeTarget
            };

            if (!missile.GuidanceActive)
            {
                command.Disable("HD_TOW_ChangeTarget_WireBroken".Translate().ToString());
            }

            yield return command;
        }

        public void NotifyBlockedFireAttempt()
        {
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - lastBlockedFireMessageTick < 120)
            {
                return;
            }

            lastBlockedFireMessageTick = currentTick;
            Messages.Message(
                "HD_TOW_ActiveMissileBlocksFire".Translate(),
                ActiveMissile,
                MessageTypeDefOf.RejectInput,
                false);
        }

        private void BeginChangeTarget()
        {
            Projectile_Tow missile = ActiveMissile;
            if (parent.Map == null || missile == null || missile.Map != parent.Map || !missile.GuidanceActive)
            {
                Messages.Message(
                    "HD_TOW_ChangeTarget_WireBroken".Translate(),
                    parent,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            Map map = parent.Map;
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = true,
                canTargetBuildings = true,
                canTargetItems = true,
                canTargetPawns = true,
                validator = target => target.IsValid && target.Cell.InBounds(map)
            },
                target =>
                {
                    if (missile.TryChangeTarget(target))
                    {
                        Messages.Message(
                            "HD_TOW_TargetChanged".Translate(),
                            missile,
                            MessageTypeDefOf.NeutralEvent,
                            false);
                    }
                });
        }
    }

    public static class TowLauncherFireUtility
    {
        public static CompTowLauncher LauncherMannedBy(Pawn pawn)
        {
            if (pawn?.Map == null)
            {
                return null;
            }

            foreach (Thing thing in pawn.Map.listerThings.AllThings)
            {
                if (thing is Building_TurretGun turret
                    && turret.TryGetComp<CompMannable>()?.ManningPawn == pawn)
                {
                    CompTowLauncher comp = turret.TryGetComp<CompTowLauncher>();
                    if (comp != null)
                    {
                        return comp;
                    }
                }
            }

            return null;
        }

        public static bool CanStartFire(Verb verb, ref bool result)
        {
            CompTowLauncher comp = (verb?.Caster as Building_TurretGun)?.TryGetComp<CompTowLauncher>();
            if (comp?.ActiveMissile == null)
            {
                return true;
            }

            if (verb.Caster.Faction == Faction.OfPlayer)
            {
                comp.NotifyBlockedFireAttempt();
            }

            result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_TowLauncher_Primary
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            return TowLauncherFireUtility.CanStartFire(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_TowLauncher_WithDestination
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            return TowLauncherFireUtility.CanStartFire(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TowLauncher
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            CompTowLauncher comp = TowLauncherFireUtility.LauncherMannedBy(__instance);
            if (comp != null)
            {
                __result = __result.Concat(comp.CompGetGizmosExtra());
            }
        }
    }
}
