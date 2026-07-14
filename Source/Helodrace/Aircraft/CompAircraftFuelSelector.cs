using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Aircraft
{
    /// <summary>
    /// RimWorld 1.6's refuelable stat iterator unconditionally evaluates
    /// ThingDef.building.IsTurret(). Aircraft are moving ThingWithComps rather
    /// than Buildings, so their building field is intentionally null. There
    /// are no refuelable-specific stats to add for that case; the vanilla
    /// iterator only adds the turret "shots before rearm" entry.
    /// </summary>
    [HarmonyPatch(typeof(CompProperties_Refuelable), nameof(CompProperties_Refuelable.SpecialDisplayStats))]
    public static class Patch_CompPropertiesRefuelable_SpecialDisplayStats_NonBuilding
    {
        public static bool Prefix(StatRequest req, ref IEnumerable<StatDrawEntry> __result)
        {
            ThingDef thingDef = req.Def as ThingDef;
            if (thingDef == null || thingDef.building != null)
            {
                return true;
            }

            __result = Enumerable.Empty<StatDrawEntry>();
            return false;
        }
    }

    public sealed class CompProperties_AircraftFuelSelector : CompProperties
    {
        public CompProperties_AircraftFuelSelector()
        {
            compClass = typeof(CompAircraftFuelSelector);
        }
    }

    /// <summary>
    /// Adds a per-aircraft choice on top of CompRefuelable's allowed fuel
    /// filter. The vanilla component continues to own capacity, target level,
    /// hauling jobs, reservations and save compatibility.
    /// </summary>
    public sealed class CompAircraftFuelSelector : ThingComp
    {
        private ThingDef selectedFuelDef;

        public ThingDef SelectedFuelDef
        {
            get
            {
                EnsureValidSelection();
                return selectedFuelDef;
            }
        }

        public CompRefuelable Refuelable => parent.TryGetComp<CompRefuelable>();

        public IEnumerable<ThingDef> AllowedFuelDefs
        {
            get
            {
                CompRefuelable refuelable = Refuelable;
                return refuelable?.Props?.fuelFilter?.AllowedThingDefs
                    ?.Where(def => def != null)
                    .OrderBy(def => def.label) ?? Enumerable.Empty<ThingDef>();
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureValidSelection();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref selectedFuelDef, "selectedAircraftFuelDef");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureValidSelection();
            }
        }

        public bool Allows(Thing thing)
        {
            return thing != null && Allows(thing.def);
        }

        public bool Allows(ThingDef thingDef)
        {
            ThingDef selected = SelectedFuelDef;
            return selected != null && thingDef == selected;
        }

        public void SelectFuel(ThingDef fuelDef)
        {
            if (fuelDef == null || !AllowedFuelDefs.Contains(fuelDef))
            {
                return;
            }

            selectedFuelDef = fuelDef;
            Messages.Message("HD_Aircraft_FuelSelected".Translate(fuelDef.LabelCap), parent,
                MessageTypeDefOf.NeutralEvent, false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            List<ThingDef> fuels = AllowedFuelDefs.ToList();
            Command_Action command = new Command_Action
            {
                defaultLabel = "HD_Aircraft_SelectFuel_Label".Translate(),
                defaultDesc = "HD_Aircraft_SelectFuel_Desc".Translate(),
                icon = SelectedFuelDef?.uiIcon ?? BaseContent.BadTex,
                action = OpenFuelMenu
            };

            if (fuels.Count == 0)
            {
                command.Disable("HD_Aircraft_NoAllowedFuel".Translate());
            }

            yield return command;
        }

        public override string CompInspectStringExtra()
        {
            ThingDef fuelDef = SelectedFuelDef;
            if (fuelDef == null)
            {
                return "HD_Aircraft_SelectedFuel_None".Translate();
            }

            return "HD_Aircraft_SelectedFuel".Translate(fuelDef.LabelCap);
        }

        private void OpenFuelMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (ThingDef fuelDef in AllowedFuelDefs)
            {
                ThingDef localFuelDef = fuelDef;
                string label = localFuelDef.LabelCap.ToString();
                if (localFuelDef == SelectedFuelDef)
                {
                    label += " (" + "HD_Aircraft_SelectedMarker".Translate() + ")";
                }

                options.Add(new FloatMenuOption(label, () => SelectFuel(localFuelDef),
                    localFuelDef.uiIcon, Color.white));
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void EnsureValidSelection()
        {
            List<ThingDef> allowed = AllowedFuelDefs.ToList();
            if (selectedFuelDef == null || !allowed.Contains(selectedFuelDef))
            {
                selectedFuelDef = allowed.FirstOrDefault();
            }
        }
    }

    /// <summary>
    /// Vanilla chooses any def in CompRefuelable.fuelFilter. These two narrow
    /// that result to the selection stored on this particular aircraft.
    /// </summary>
    [HarmonyPatch(typeof(RefuelWorkGiverUtility), "FindBestFuel")]
    public static class Patch_RefuelWorkGiverUtility_FindBestFuel_Aircraft
    {
        public static void Postfix(Pawn pawn, Thing refuelable, ref Thing __result)
        {
            CompAircraftFuelSelector selector = refuelable?.TryGetComp<CompAircraftFuelSelector>();
            ThingDef selected = selector?.SelectedFuelDef;
            if (selector == null || selected == null || __result?.def == selected)
            {
                return;
            }

            __result = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(selected),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                thing => !thing.IsForbidden(pawn) && pawn.CanReserve(thing));
        }
    }

    [HarmonyPatch(typeof(RefuelWorkGiverUtility), "FindAllFuel")]
    public static class Patch_RefuelWorkGiverUtility_FindAllFuel_Aircraft
    {
        public static void Postfix(Thing refuelable, ref List<Thing> __result)
        {
            CompAircraftFuelSelector selector = refuelable?.TryGetComp<CompAircraftFuelSelector>();
            if (selector == null || __result == null)
            {
                return;
            }

            __result.RemoveAll(thing => !selector.Allows(thing));
        }
    }
}
