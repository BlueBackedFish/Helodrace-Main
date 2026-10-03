using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    // The marker distinguishes a breached, stuck-open door from an unrelated
    // electrical breakdown on a door that already has vanilla breakdowns.
    public sealed class CompDoorBreachFault : ThingComp
    {
        private bool jammed;
        public bool Jammed => jammed;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref jammed, "sledgehammerJammed");
        }

        public bool Jam(Pawn worker, int damage)
        {
            if (!(parent is Building_Door door) || !door.Spawned || jammed) return false;
            CompBreakdownable breakdown = door.GetComp<CompBreakdownable>();
            if (breakdown == null) return false;
            jammed = true;
            // Leave the door intact and register it for ordinary HP repair too.
            door.HitPoints = Math.Max(1, door.HitPoints - Math.Max(1, damage));
            door.Map.listerBuildingsRepairable.Notify_BuildingTookDamage(door);
            breakdown.DoBreakdown();
            door.StartManualOpenBy(worker);
            door.Map.reachability.ClearCache();
            return true;
        }

        public void Repaired()
        {
            if (!jammed) return;
            jammed = false;
            if (parent.Spawned) parent.Map.reachability.ClearCache();
        }
    }

    // These comps use vanilla breakdown display, persistence and repair, but
    // do not introduce random mechanical failures to ordinary wooden doors.
    public sealed class CompBreachableDoorBreakdown : CompBreakdownable { }

    public static class DoorBreachFaultUtility
    {
        public static void InitializeDoorDefs()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading.Where(def =>
                def.thingClass != null && typeof(Building_Door).IsAssignableFrom(def.thingClass)))
            {
                if (def.comps == null) def.comps = new System.Collections.Generic.List<CompProperties>();
                if (!def.comps.Any(comp => comp.compClass != null
                    && typeof(CompBreakdownable).IsAssignableFrom(comp.compClass)))
                    def.comps.Add(new CompProperties { compClass = typeof(CompBreachableDoorBreakdown) });
                if (!def.comps.Any(comp => comp.compClass == typeof(CompDoorBreachFault)))
                    def.comps.Add(new CompProperties { compClass = typeof(CompDoorBreachFault) });
            }
        }

        public static bool Jammed(Building_Door door) => door.GetComp<CompDoorBreachFault>()?.Jammed == true;
    }

    [HarmonyPatch(typeof(CompBreakdownable), nameof(CompBreakdownable.CheckForBreakdown))]
    public static class Patch_BreachedDoor_NoRandomBreakdown
    {
        public static bool Prefix(CompBreakdownable __instance) =>
            !(__instance is CompBreachableDoorBreakdown);
    }

    [HarmonyPatch(typeof(Building_Door), "get_AlwaysOpen")]
    public static class Patch_BreachedDoor_AlwaysOpen
    {
        public static void Postfix(Building_Door __instance, ref bool __result)
        {
            if (DoorBreachFaultUtility.Jammed(__instance)) __result = true;
        }
    }

    [HarmonyPatch(typeof(Building_Door), "get_WillCloseSoon")]
    public static class Patch_BreachedDoor_FreePassage
    {
        public static void Postfix(Building_Door __instance, ref bool __result)
        {
            if (DoorBreachFaultUtility.Jammed(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(CompBreakdownable), nameof(CompBreakdownable.Notify_Repaired))]
    public static class Patch_BreachedDoor_BreakdownRepaired
    {
        public static void Postfix(CompBreakdownable __instance) =>
            __instance.parent.GetComp<CompDoorBreachFault>()?.Repaired();
    }

    [HarmonyPatch(typeof(ListerBuildingsRepairable), nameof(ListerBuildingsRepairable.Notify_BuildingRepaired))]
    public static class Patch_BreachedDoor_OrdinaryRepair
    {
        public static void Postfix(Building b)
        {
            if (!(b is Building_Door door) || door.HitPoints < door.MaxHitPoints
                || !DoorBreachFaultUtility.Jammed(door)) return;
            door.GetComp<CompBreakdownable>()?.Notify_Repaired();
        }
    }
}
