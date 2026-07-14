using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Aircraft
{
    public sealed class CompProperties_AircraftTransporter : CompProperties_Transporter
    {
        public CompProperties_AircraftTransporter()
        {
            compClass = typeof(CompAircraftTransporter);
        }
    }

    /// <summary>
    /// Uses the vanilla transporter container, load dialog and transferables.
    /// Loading itself is handled by the aircraft job so split stacks and an
    /// ownerless aircraft are processed through one deterministic path.
    /// </summary>
    public sealed class CompAircraftTransporter : CompTransporter
    {
        private static readonly FieldInfo LeftToLoadField =
            AccessTools.Field(typeof(CompTransporter), "leftToLoad");

        private AircraftThing Aircraft => parent as AircraftThing;

        private List<TransferableOneWay> LeftToLoad =>
            LeftToLoadField?.GetValue(this) as List<TransferableOneWay>;

        public Thing NextThingToLoad(Pawn hauler)
        {
            if (hauler == null || Aircraft == null || Aircraft.IsAirborne) return null;
            List<TransferableOneWay> leftToLoad = LeftToLoad;
            if (leftToLoad == null) return null;

            return leftToLoad
                .Where(transferable => transferable != null && transferable.CountToTransfer > 0)
                .SelectMany(transferable => transferable.things ?? new List<Thing>())
                .Where(thing => thing != null && thing.Spawned && thing.Map == parent.Map
                    && CountToLoad(thing) > 0 && !thing.IsForbidden(hauler)
                    && hauler.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Some))
                .OrderBy(thing => thing.Position.DistanceToSquared(hauler.Position))
                .FirstOrDefault();
        }

        public int CountToLoad(Thing thing)
        {
            if (thing == null || thing.def == null || Aircraft == null || Aircraft.IsAirborne)
                return 0;
            TransferableOneWay transferable = LeftToLoad?.FirstOrDefault(entry => entry != null
                && entry.CountToTransfer > 0 && entry.things != null && entry.things.Contains(thing));
            if (transferable == null) return 0;

            float unitMass = Mathf.Max(0.0001f, thing.GetStatValue(StatDefOf.Mass));
            int massCount = Mathf.FloorToInt(Mathf.Max(0f, MassCapacity - MassUsage) / unitMass);
            return Mathf.Clamp(Mathf.Min(transferable.CountToTransfer, massCount), 0, thing.stackCount);
        }

        public bool TryAcceptFrom(Pawn hauler, int maximumCount)
        {
            Thing carried = hauler?.carryTracker?.CarriedThing;
            if (carried == null) return false;
            int remaining = RemainingCountForDefinition(carried.def);
            float unitMass = Mathf.Max(0.0001f, carried.GetStatValue(StatDefOf.Mass));
            int massCount = Mathf.FloorToInt(
                Mathf.Max(0f, MassCapacity - MassUsage) / unitMass);
            int requested = Mathf.Min(Mathf.Max(0, maximumCount),
                Mathf.Min(carried.stackCount, Mathf.Min(remaining, massCount)));
            if (requested <= 0) return false;

            int before = RemainingCountForDefinition(carried.def);
            int transferred = hauler.carryTracker.innerContainer.TryTransferToContainer(
                carried, GetDirectlyHeldThings(), requested, false);
            if (transferred <= 0) return false;

            // ThingOwner normally sends this notification. Apply only a
            // missing remainder so merged stacks cannot leave stale work.
            int recorded = Mathf.Max(0, before - RemainingCountForDefinition(carried.def));
            if (recorded < transferred)
            {
                Thing loaded = SearchableContents.FirstOrDefault(thing => thing.def == carried.def);
                if (loaded != null)
                    SubtractFromToLoadList(loaded, transferred - recorded, false);
            }
            return true;
        }

        private int RemainingCountForDefinition(ThingDef def) => LeftToLoad?
            .Where(entry => entry?.ThingDef == def && entry.CountToTransfer > 0)
            .Sum(entry => entry.CountToTransfer) ?? 0;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Aircraft == null || Aircraft.IsAirborne || parent.Map == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "HD_Aircraft_VanillaCargo_Label".Translate(),
                defaultDesc = "HD_Aircraft_VanillaCargo_Desc".Translate(),
                icon = BaseContent.BadTex,
                action = () => Find.WindowStack.Add(new Dialog_LoadTransporters(
                    parent.Map, new List<CompTransporter> { this }))
            };

            if (LoadingInProgressOrReadyToLaunch)
            {
                yield return new Command_Action
                {
                    defaultLabel = "HD_Aircraft_VanillaCargoCancel_Label".Translate(),
                    defaultDesc = "HD_Aircraft_VanillaCargoCancel_Desc".Translate(),
                    icon = BaseContent.BadTex,
                    action = () => CancelLoad(parent.Map)
                };
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_LoadTransporters), nameof(WorkGiver_LoadTransporters.HasJobOnThing))]
    public static class Patch_VanillaLoadTransporters_SkipAircraft
    {
        public static bool Prefix(Thing t, ref bool __result)
        {
            if (!(t is AircraftThing)) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// Crew uses CompAircraftManifest. Suppress only the pawn population step
    /// in the vanilla transporter dialog, leaving its item loading untouched.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_LoadTransporters), "AddPawnsToTransferables")]
    public static class Patch_DialogLoadTransporters_AircraftItemsOnly
    {
        public static bool Prefix(List<CompTransporter> ___transporters)
        {
            return ___transporters == null
                || !___transporters.Any(comp => comp?.parent is AircraftThing);
        }
    }
}
