using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class CompProperties_M47DragonLauncher : CompProperties
    {
        public CompProperties_M47DragonLauncher()
        {
            compClass = typeof(CompM47DragonLauncher);
        }
    }

    public class CompM47DragonLauncher : ThingComp
    {
        private Projectile_M47Dragon activeMissile;
        private int lastBlockedFireMessageTick = -99999;

        public Projectile_M47Dragon ActiveMissile =>
            activeMissile != null && !activeMissile.Destroyed && activeMissile.Spawned
                ? activeMissile
                : null;

        private Pawn Wielder
        {
            get
            {
                if (parent.ParentHolder is Pawn_EquipmentTracker tracker)
                {
                    return tracker.pawn;
                }

                return null;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref activeMissile, "activeM47DragonMissile");
        }

        public void SetActiveMissile(Projectile_M47Dragon missile)
        {
            activeMissile = missile;
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
                "HD_M47Dragon_ActiveMissileBlocksFire".Translate(),
                ActiveMissile,
                MessageTypeDefOf.RejectInput,
                false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn wielder = Wielder;
            Projectile_M47Dragon missile = ActiveMissile;
            if (wielder?.Faction != Faction.OfPlayer || missile == null || missile.Map != wielder.Map)
            {
                yield break;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel = "HD_M47Dragon_ChangeTarget_Label".Translate().ToString(),
                defaultDesc = "HD_M47Dragon_ChangeTarget_Desc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ?? BaseContent.BadTex,
                action = BeginChangeTarget
            };

            if (!missile.GuidanceActive)
            {
                command.Disable("HD_M47Dragon_ChangeTarget_WireBroken".Translate().ToString());
            }
            else if (!BTXUtility.IsHelod(wielder))
            {
                command.Disable("HD_M47Dragon_ChangeTarget_HelodOnly".Translate().ToString());
            }

            yield return command;
        }

        private void BeginChangeTarget()
        {
            Pawn wielder = Wielder;
            Projectile_M47Dragon missile = ActiveMissile;
            if (wielder?.Map == null || missile == null || missile.Map != wielder.Map || !missile.GuidanceActive)
            {
                Messages.Message("HD_M47Dragon_ChangeTarget_WireBroken".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!BTXUtility.IsHelod(wielder))
            {
                Messages.Message("HD_M47Dragon_ChangeTarget_HelodOnly".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            // Retargeting deliberately uses its own targeter. It changes only
            // the linked missile and never starts the launcher's firing verb.
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = true,
                canTargetBuildings = true,
                canTargetItems = true,
                canTargetPawns = true,
                validator = target => target.IsValid && target.Cell.InBounds(wielder.Map)
            },
                target =>
                {
                    if (missile.TryChangeTarget(target))
                    {
                        Messages.Message("HD_M47Dragon_TargetChanged".Translate(), missile, MessageTypeDefOf.NeutralEvent, false);
                    }
                });
        }
    }

    public static class M47DragonFireUtility
    {
        public static bool CanStartFire(Verb verb, ref bool result)
        {
            CompM47DragonLauncher comp = verb?.EquipmentSource?.TryGetComp<CompM47DragonLauncher>();
            if (comp?.ActiveMissile == null)
            {
                return true;
            }

            if (verb.CasterPawn?.Faction == Faction.OfPlayer)
            {
                comp.NotifyBlockedFireAttempt();
            }

            result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_M47Dragon_Primary
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            return M47DragonFireUtility.CanStartFire(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_Verb_TryStartCastOn_M47Dragon_WithDestination
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            return M47DragonFireUtility.CanStartFire(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_M47Dragon
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            CompM47DragonLauncher comp = __instance?.equipment?.Primary?.TryGetComp<CompM47DragonLauncher>();
            if (comp != null)
            {
                __result = __result.Concat(comp.CompGetGizmosExtra());
            }
        }
    }
}
