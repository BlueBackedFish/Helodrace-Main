using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Helodrace.Aircraft
{
    /// <summary>
    /// Loader for saves made by the previous ThingOwner-based implementation.
    /// It is never used to store aircraft in new saves.
    /// </summary>
    internal sealed class LegacyAircraftWorldHolder : IThingHolder
    {
        private ThingOwner<Thing> contents;
        private readonly AircraftCaravan parent;

        public ThingOwner<Thing> Contents => contents;
        public IThingHolder ParentHolder => parent;

        public LegacyAircraftWorldHolder(AircraftCaravan parent)
        {
            this.parent = parent;
            contents = new ThingOwner<Thing>(this, false, LookMode.Deep, false);
        }

        public void ExposeContents()
        {
            Scribe_Deep.Look(ref contents, "aircraftCaravanContents", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && contents == null)
                contents = new ThingOwner<Thing>(this, false, LookMode.Deep, false);
        }

        public ThingOwner GetDirectlyHeldThings() => contents;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, contents);
        }
    }

    public enum AircraftCrewRole
    {
        Pilot,
        Passenger
    }

    public sealed class AircraftCrewAssignment : IExposable
    {
        public Pawn pawn;
        public AircraftCrewRole role;
        public int seatIndex;

        public AircraftCrewAssignment()
        {
        }

        public AircraftCrewAssignment(Pawn pawn, AircraftCrewRole role, int seatIndex)
        {
            this.pawn = pawn;
            this.role = role;
            this.seatIndex = seatIndex;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref role, "role", AircraftCrewRole.Passenger);
            Scribe_Values.Look(ref seatIndex, "seatIndex");
        }
    }

    /// <summary>
    /// A normal pawn-owning Caravan with separately serialized aircraft data.
    /// Pawns and trade cargo use the inherited vanilla caravan containers;
    /// only the unspawned aircraft state is stored here for map restoration.
    /// </summary>
    public sealed class AircraftCaravan : Caravan
    {
        private AircraftThing storedAircraft;
        private LegacyAircraftWorldHolder legacyAircraftHolder;
        private Pawn worldPilot;
        private List<AircraftCrewAssignment> crewAssignments = new List<AircraftCrewAssignment>();
        private Material cachedMaterial;
        private Texture2D cachedMaterialTexture;
        private bool traveling;
        private PlanetTile originTile = PlanetTile.Invalid;
        private PlanetTile destinationTile = PlanetTile.Invalid;
        private int travelStartTick;
        private int travelDurationTicks;

        private static Material oneWayRangeMaterial;
        private static Material roundTripRangeMaterial;
        private static Material travelLineMaterial;

        public AircraftThing Aircraft => storedAircraft;
        public IReadOnlyList<AircraftCrewAssignment> CrewAssignments => crewAssignments;
        private Pawn AssignedPilot => crewAssignments?
            .FirstOrDefault(assignment => assignment?.role == AircraftCrewRole.Pilot)?.pawn
            ?? worldPilot;
        public bool Traveling => traveling;
        public float TravelProgress => !traveling || travelDurationTicks <= 0
            ? 0f
            : Mathf.Clamp01((Find.TickManager.TicksGame - travelStartTick) /
                (float)travelDurationTicks);

        private AircraftDefExtension FlightDef => Aircraft?.def?.GetModExtension<AircraftDefExtension>()
            ?? AircraftDefaults.Extension;
        private CompRefuelable FuelComp => Aircraft?.FuelComp;
        private int OneWayRange => Mathf.Min(FlightDef.worldRange,
            Mathf.FloorToInt((FuelComp?.Fuel ?? 0f) / Mathf.Max(0.001f, FlightDef.worldFuelPerTile)));
        private int RoundTripRange => Mathf.Min(FlightDef.worldRange,
            Mathf.FloorToInt((FuelComp?.Fuel ?? 0f) /
                Mathf.Max(0.001f, FlightDef.worldFuelPerTile * 2f)));

        public override string Label => Aircraft == null
            ? base.Label
            : "HD_Aircraft_WorldCaravan_Label".Translate(Aircraft.LabelCap).Resolve();

        public override Texture2D ExpandingIcon => Aircraft?.def?.uiIcon ?? base.ExpandingIcon;
        public override Color ExpandingIconColor => Color.white;

        public override Vector3 DrawPos
        {
            get
            {
                if (!traveling || originTile < 0 || destinationTile < 0) return base.DrawPos;
                Vector3 origin = Find.WorldGrid.GetTileCenter(originTile);
                Vector3 destination = Find.WorldGrid.GetTileCenter(destinationTile);
                return Vector3.Slerp(origin, destination, TravelProgress);
            }
        }

        public override Material Material
        {
            get
            {
                Texture2D texture = Aircraft?.def?.uiIcon;
                if (texture == null) return base.Material;
                if (cachedMaterial == null || cachedMaterialTexture != texture)
                {
                    cachedMaterialTexture = texture;
                    cachedMaterial = MaterialPool.MatFrom(texture,
                        ShaderDatabase.WorldOverlayTransparentLit, Color.white);
                }
                return cachedMaterial;
            }
        }

        public override Material ExpandingMaterial => Material;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref storedAircraft, "aircraftCaravanAircraft");

            // Read the previous ThingOwner-based format only while loading.
            // New saves contain only aircraftCaravanAircraft.
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                if (legacyAircraftHolder == null)
                    legacyAircraftHolder = new LegacyAircraftWorldHolder(this);
                legacyAircraftHolder.ExposeContents();
            }
            Scribe_References.Look(ref worldPilot, "aircraftCaravanPilot");
            Scribe_Collections.Look(ref crewAssignments, "aircraftCrewAssignments",
                LookMode.Deep);
            Scribe_Values.Look(ref traveling, "aircraftCaravanTraveling");
            Scribe_Values.Look(ref originTile, "aircraftCaravanOriginTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref destinationTile, "aircraftCaravanDestinationTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref travelStartTick, "aircraftCaravanTravelStartTick");
            Scribe_Values.Look(ref travelDurationTicks, "aircraftCaravanTravelDurationTicks");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (storedAircraft == null && legacyAircraftHolder?.Contents != null)
                {
                    storedAircraft = legacyAircraftHolder.Contents
                        .OfType<AircraftThing>().FirstOrDefault();
                    if (storedAircraft != null)
                        legacyAircraftHolder.Contents.Remove(storedAircraft);
                }
                legacyAircraftHolder = null;
                if (crewAssignments == null)
                    crewAssignments = new List<AircraftCrewAssignment>();

                if (traveling && (originTile < 0 || destinationTile < 0 || travelDurationTicks < 1))
                    traveling = false;

                // Migrate expeditions saved before AircraftCaravan became a
                // true Caravan. Their crew and cargo are still inside the
                // aircraft manifest at this point.
                TransferAircraftContentsToCaravan(Aircraft);

                // Saves made by the first Caravan implementation can already
                // contain members that were never registered in WorldPawns.
                foreach (Pawn pawn in PawnsListForReading.ToList())
                    EnsureWorldPawn(pawn);
            }
        }

        protected override void Tick()
        {
            // Caravan.TickInterval removes members that are not represented
            // as caravan/world pawns. Drain the aircraft first, before that
            // vanilla validation gets a chance to see an empty caravan.
            TransferAircraftContentsToCaravan(Aircraft);
            foreach (Pawn pawn in PawnsListForReading.ToList())
                EnsureWorldPawn(pawn);
            base.Tick();
            storedAircraft?.DoTick();
            if (traveling && Find.TickManager.TicksGame >= travelStartTick + travelDurationTicks)
                ArriveAtDestination();
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (traveling && originTile >= 0 && destinationTile >= 0)
                GenDraw.DrawWorldLineBetween(Find.WorldGrid.GetTileCenter(originTile),
                    Find.WorldGrid.GetTileCenter(destinationTile), TravelLineMaterial, 0.018f);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            // Ask only world objects on this tile for their vanilla caravan
            // interactions. Calling Caravan.GetGizmos wholesale would also
            // expose walking, splitting and resting controls that conflict
            // with the aircraft's shuttle-style movement.
            if (!traveling)
            {
                foreach (WorldObject worldObject in Find.WorldObjects.AllWorldObjects)
                {
                    if (worldObject == this || worldObject.Tile != Tile) continue;
                    foreach (Gizmo gizmo in worldObject.GetCaravanGizmos(this))
                        yield return gizmo;
                }
            }

            Command_Action send = new Command_Action
            {
                defaultLabel = "HD_Aircraft_WorldSend_Label".Translate(),
                defaultDesc = "HD_Aircraft_WorldSend_Desc".Translate(
                    OneWayRange, RoundTripRange, FlightDef.worldFuelPerTile.ToString("0.##")),
                icon = ExpandingIcon ?? BaseContent.BadTex,
                action = BeginWorldTargeting
            };
            if (traveling)
                send.Disable("HD_Aircraft_WorldSend_AlreadyTraveling".Translate());
            else if (AssignedPilot == null || AssignedPilot.Dead || AssignedPilot.Downed
                || AssignedPilot.InMentalState)
                send.Disable("HD_Aircraft_Bombing_NoPilot".Translate());
            else if (OneWayRange <= 0)
                send.Disable("HD_Aircraft_WorldSend_NoFuel".Translate());
            yield return send;
        }

        public override string GetInspectString()
        {
            string result = base.GetInspectString();
            AircraftThing aircraft = Aircraft;
            if (aircraft == null) return result;

            string pilot = AssignedPilot?.LabelShortCap ?? "HD_Aircraft_None".Translate();
            string aircraftLine = "HD_Aircraft_WorldCaravan_Inspect".Translate(
                aircraft.LabelCap, pilot, PawnsListForReading.Count);
            if (traveling)
            {
                int ticksLeft = Mathf.Max(0,
                    travelStartTick + travelDurationTicks - Find.TickManager.TicksGame);
                aircraftLine += "\n" + "HD_Aircraft_WorldCaravan_Traveling".Translate(
                    ticksLeft.ToStringTicksToPeriod());
            }
            else
            {
                aircraftLine += "\n" + "HD_Aircraft_WorldCaravan_Ranges".Translate(
                    OneWayRange, RoundTripRange);
            }
            return result.NullOrEmpty() ? aircraftLine : result + "\n" + aircraftLine;
        }

        private void BeginWorldTargeting()
        {
            if (traveling || OneWayRange <= 0) return;
            Find.WorldTargeter.BeginTargeting(ChooseDestination, true, ExpandingIcon,
                false, DrawRangeRings, TargetLabel, CanTarget, Tile, true);
        }

        private bool ChooseDestination(GlobalTargetInfo target)
        {
            if (!CanTarget(target))
            {
                Messages.Message("HD_Aircraft_WorldSend_OutOfRange".Translate(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }

            int distance = DistanceTo(target.Tile, OneWayRange + 1);
            if (distance > RoundTripRange)
            {
                float returnFuel = distance * FlightDef.worldFuelPerTile * 2f;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "HD_Aircraft_WorldSend_OneWayWarning".Translate(
                        distance, (FuelComp?.Fuel ?? 0f).ToString("0.#"),
                        returnFuel.ToString("0.#")),
                    () => StartTravel(target.Tile, distance)));
                return true;
            }

            StartTravel(target.Tile, distance);
            return true;
        }

        private bool CanTarget(GlobalTargetInfo target)
        {
            if (!target.IsValid || target.Tile < 0 || target.Tile == Tile) return false;
            int distance = DistanceTo(target.Tile, OneWayRange + 1);
            return distance > 0 && distance <= OneWayRange;
        }

        private TaggedString TargetLabel(GlobalTargetInfo target)
        {
            if (!target.IsValid || target.Tile < 0 || target.Tile == Tile)
                return "HD_Aircraft_WorldSend_InvalidTarget".Translate();
            int distance = DistanceTo(target.Tile, OneWayRange + 1);
            float fuelCost = distance * FlightDef.worldFuelPerTile;
            if (distance <= 0 || distance > OneWayRange)
                return "HD_Aircraft_WorldSend_TargetOutOfRange".Translate(distance, OneWayRange);
            return distance <= RoundTripRange
                ? "HD_Aircraft_WorldSend_TargetRoundTrip".Translate(distance, fuelCost.ToString("0.#"))
                : "HD_Aircraft_WorldSend_TargetOneWay".Translate(distance, fuelCost.ToString("0.#"));
        }

        private void DrawRangeRings()
        {
            if (OneWayRange > 0)
                GenDraw.DrawWorldRadiusRing(Tile, OneWayRange, OneWayRangeMaterial);
            if (RoundTripRange > 0)
                GenDraw.DrawWorldRadiusRing(Tile, RoundTripRange, RoundTripRangeMaterial);
        }

        private int DistanceTo(PlanetTile target, int maxDistance)
        {
            return Find.WorldGrid.TraversalDistanceBetween(Tile, target, true,
                Mathf.Max(1, maxDistance), false);
        }

        private void StartTravel(PlanetTile target, int distance)
        {
            if (traveling || target < 0 || distance <= 0 || distance > OneWayRange) return;
            float fuelCost = distance * FlightDef.worldFuelPerTile;
            if (FuelComp == null || FuelComp.Fuel + 0.001f < fuelCost)
            {
                Messages.Message("HD_Aircraft_WorldSend_NoFuel".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            FuelComp.ConsumeFuel(fuelCost);
            originTile = Tile;
            destinationTile = target;
            travelStartTick = Find.TickManager.TicksGame;
            travelDurationTicks = Mathf.Max(1, FlightDef.worldFlightDurationTicks);
            traveling = true;
        }

        private void ArriveAtDestination()
        {
            traveling = false;
            if (destinationTile < 0) return;
            Tile = destinationTile;
            originTile = Tile;
            Messages.Message("HD_Aircraft_WorldSend_Arrived".Translate(Label), this,
                MessageTypeDefOf.PositiveEvent, false);
        }

        private static Material OneWayRangeMaterial => oneWayRangeMaterial ??
            (oneWayRangeMaterial = MaterialPool.MatFrom(BaseContent.WhiteTex,
                ShaderDatabase.WorldOverlayTransparent, new Color(1f, 0.72f, 0.15f, 0.85f)));
        private static Material RoundTripRangeMaterial => roundTripRangeMaterial ??
            (roundTripRangeMaterial = MaterialPool.MatFrom(BaseContent.WhiteTex,
                ShaderDatabase.WorldOverlayTransparent, new Color(0.2f, 0.95f, 0.35f, 0.9f)));
        private static Material TravelLineMaterial => travelLineMaterial ??
            (travelLineMaterial = MaterialPool.MatFrom(BaseContent.WhiteTex,
                ShaderDatabase.WorldOverlayTransparent, new Color(0.3f, 0.8f, 1f, 0.9f)));

        private bool TryStoreAircraft(AircraftThing aircraft)
        {
            if (aircraft == null || aircraft.Spawned || storedAircraft != null) return false;
            storedAircraft = aircraft;
            return true;
        }

        private void TransferAircraftContentsToCaravan(AircraftThing aircraft)
        {
            if (aircraft == null) return;
            CompAircraftManifest manifest = aircraft.Manifest;
            if (manifest != null && manifest.Occupants.Any())
            {
                List<Pawn> extracted = manifest.ExtractOccupantsForWorld(out Pawn extractedPilot);
                AddCrewMembers(extracted, extractedPilot, false);
            }
            if (manifest != null)
            {
                foreach (Thing cargo in manifest.Cargo.ToList())
                {
                    if (!manifest.InnerContainer.Remove(cargo)) continue;
                    AddPawnOrItem(cargo, true);
                }
            }

            CompAircraftTransporter transporter = aircraft.TryGetComp<CompAircraftTransporter>();
            ThingOwner transported = transporter?.GetDirectlyHeldThings();
            if (transported == null) return;
            foreach (Thing thing in transported.ToList())
            {
                if (!transported.Remove(thing)) continue;
                if (thing is Pawn pawn)
                {
                    if (!ContainsPawn(pawn)) AddPawn(pawn, true);
                    EnsureWorldPawn(pawn);
                }
                else
                    AddPawnOrItem(thing, true);
            }
        }

        private bool AddCrewMembers(IEnumerable<Pawn> crew, Pawn pilot, bool replaceAssignments)
        {
            List<Pawn> orderedCrew = crew?.Where(pawn => pawn != null).Distinct().ToList()
                ?? new List<Pawn>();
            if (replaceAssignments) crewAssignments.Clear();

            int passengerSeat = replaceAssignments
                ? 0
                : crewAssignments.Where(assignment => assignment != null
                    && assignment.role == AircraftCrewRole.Passenger)
                    .Select(assignment => assignment.seatIndex + 1).DefaultIfEmpty(0).Max();
            bool allAdded = true;
            foreach (Pawn pawn in orderedCrew.OrderBy(member => member == pilot ? 0 : 1))
            {
                if (!ContainsPawn(pawn)) AddPawn(pawn, true);
                if (!ContainsPawn(pawn))
                {
                    allAdded = false;
                    continue;
                }
                EnsureWorldPawn(pawn);

                AircraftCrewRole role = pawn == pilot
                    ? AircraftCrewRole.Pilot : AircraftCrewRole.Passenger;
                int seatIndex = role == AircraftCrewRole.Pilot ? 0 : passengerSeat++;
                AircraftCrewAssignment existing = crewAssignments.FirstOrDefault(
                    assignment => assignment?.pawn == pawn);
                if (existing == null)
                    crewAssignments.Add(new AircraftCrewAssignment(pawn, role, seatIndex));
                else
                {
                    existing.role = role;
                    existing.seatIndex = seatIndex;
                }
            }

            if (pilot != null && ContainsPawn(pilot)) worldPilot = pilot;
            return allAdded && orderedCrew.Count > 0;
        }

        private void RollbackCrewToAircraft(CompAircraftManifest manifest,
            IEnumerable<Pawn> crew, Pawn pilot)
        {
            List<Pawn> pawns = crew?.Where(pawn => pawn != null).Distinct().ToList()
                ?? new List<Pawn>();
            foreach (Pawn pawn in pawns)
            {
                if (ContainsPawn(pawn)) RemovePawn(pawn);
                if (Find.WorldPawns?.Contains(pawn) == true) Find.WorldPawns.RemovePawn(pawn);
            }
            crewAssignments.Clear();
            worldPilot = null;
            manifest?.RestoreOccupantsFromWorld(pawns, pilot);
        }

        internal static void EnsureWorldPawn(Pawn pawn)
        {
            if (pawn == null || Find.WorldPawns == null || Find.WorldPawns.Contains(pawn)) return;
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
        }

        internal AircraftThing ReleaseAircraftForMapEntry()
        {
            AircraftThing aircraft = storedAircraft;
            storedAircraft = null;
            return aircraft;
        }

        public static bool TryCreateFromMap(AircraftThing aircraft, out AircraftCaravan caravan,
            out string rejection)
        {
            caravan = null;
            rejection = null;
            if (aircraft == null || !aircraft.Spawned || aircraft.Map == null
                || aircraft.FlightState != AircraftFlightState.Flying)
            {
                rejection = "HD_Aircraft_Expedition_NotReady".Translate();
                return false;
            }

            WorldObjectDef worldDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("HD_AircraftCaravan");
            if (worldDef == null)
            {
                rejection = "HD_Aircraft_Expedition_MissingWorldDef".Translate();
                return false;
            }

            Map map = aircraft.Map;
            PlanetTile tile = map.Tile;
            IntVec3 mapPosition = aircraft.Position;
            Rot4 rotation = aircraft.Rotation;
            Faction faction = aircraft.Manifest?.Pilot?.Faction
                ?? aircraft.Faction ?? map.ParentFaction ?? Faction.OfPlayer;
            CompAircraftManifest manifest = aircraft.Manifest;
            Pawn extractedPilot = null;
            List<Pawn> extractedCrew = manifest != null
                ? manifest.ExtractOccupantsForWorld(out extractedPilot)
                : new List<Pawn>();
            if (extractedCrew.Count == 0 || extractedPilot == null)
            {
                manifest?.RestoreOccupantsFromWorld(extractedCrew, extractedPilot);
                rejection = "HD_Aircraft_Expedition_NotReady".Translate();
                return false;
            }

            // Only after every occupant has left the aircraft do we construct
            // the caravan. From this point onward the aircraft manifest must
            // remain pawn-free for the entire world-map phase.
            AircraftCaravan created = WorldObjectMaker.MakeWorldObject(worldDef) as AircraftCaravan;
            if (created == null)
            {
                manifest.RestoreOccupantsFromWorld(extractedCrew, extractedPilot);
                rejection = "HD_Aircraft_Expedition_MissingWorldDef".Translate();
                return false;
            }

            aircraft.DeSpawnOrDeselect(DestroyMode.Vanish);
            bool accepted = false;
            try
            {
                accepted = created.TryStoreAircraft(aircraft);
            }
            catch (Exception exception)
            {
                // A failed map-to-world transition must never strand the
                // aircraft as an unspawned, unowned Thing.
                created.ReleaseAircraftForMapEntry();
                Log.Error("Helodrace: failed to serialize aircraft state for its caravan. "
                    + exception);
            }
            if (!accepted)
            {
                if (!aircraft.Destroyed && !aircraft.Spawned)
                    GenSpawn.Spawn(aircraft, mapPosition, map, rotation);
                manifest.RestoreOccupantsFromWorld(extractedCrew, extractedPilot);
                rejection = "HD_Aircraft_Expedition_TransferFailed".Translate();
                return false;
            }

            created.Tile = tile;
            if (faction != null) created.SetFaction(faction);
            // Match CaravanMaker.MakeCaravan exactly: the caravan must exist
            // in WorldObjects before AddPawn, and PassToWorld comes afterward.
            Find.WorldObjects.Add(created);
            bool crewAdded = created.AddCrewMembers(extractedCrew, extractedPilot, true);
            created.TransferAircraftContentsToCaravan(aircraft);
            if (!crewAdded || created.PawnsListForReading.Count == 0
                || aircraft.Manifest?.Occupants.Any() == true)
            {
                AircraftThing returnedAircraft = created.ReleaseAircraftForMapEntry() ?? aircraft;
                created.RollbackCrewToAircraft(manifest, extractedCrew, extractedPilot);
                if (!returnedAircraft.Destroyed && !returnedAircraft.Spawned)
                    GenSpawn.Spawn(returnedAircraft, mapPosition, map, rotation);
                rejection = "HD_Aircraft_Expedition_TransferFailed".Translate();
                Log.Error("Helodrace: canceled aircraft caravan creation because its crew "
                    + "could not be represented exclusively as caravan members.");
                if (created.Spawned) Find.WorldObjects.Remove(created);
                return false;
            }
            created.SetUniqueId(Find.UniqueIDsManager.GetNextCaravanID());
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.Select(created);
            CameraJumper.TryJump(new GlobalTargetInfo(tile));
            caravan = created;
            return true;
        }
    }

    /// <summary>
    /// Vanilla settlement attack/visit arrival removes the caravan after it
    /// puts its pawns on the generated map. Restore the separately serialized
    /// aircraft beside its crew before the caravan object is discarded.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_AircraftCaravanEnterMap
    {
        public sealed class EntryState
        {
            public AircraftThing aircraft;
            public List<Pawn> pawns;
        }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(CaravanEnterMapUtility).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == nameof(CaravanEnterMapUtility.Enter));
        }

        public static void Prefix(Caravan caravan, ref EntryState __state)
        {
            if (!(caravan is AircraftCaravan aircraftCaravan) || aircraftCaravan.Aircraft == null)
                return;

            __state = new EntryState
            {
                pawns = aircraftCaravan.PawnsListForReading.ToList(),
                aircraft = aircraftCaravan.ReleaseAircraftForMapEntry()
            };
        }

        public static void Postfix(Map map, EntryState __state)
        {
            AircraftThing aircraft = __state?.aircraft;
            if (aircraft == null || aircraft.Destroyed || aircraft.Spawned || map == null) return;

            Pawn crew = __state.pawns?.FirstOrDefault(pawn => pawn != null
                && pawn.Spawned && pawn.Map == map);
            IntVec3 near = crew?.Position ?? CellFinder.RandomEdgeCell(map);
            if (!GenPlace.TryPlaceThing(aircraft, near, map, ThingPlaceMode.Near))
            {
                Log.Error("Helodrace: failed to place an aircraft after its caravan entered a map.");
            }
        }
    }
}
