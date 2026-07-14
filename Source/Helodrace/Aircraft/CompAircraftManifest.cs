using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Aircraft
{
    public sealed class CompProperties_AircraftManifest : CompProperties
    {
        public int passengerCapacity = 4;
        public float cargoCapacity = 500f;

        public CompProperties_AircraftManifest()
        {
            compClass = typeof(CompAircraftManifest);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (passengerCapacity < 0) yield return "passengerCapacity cannot be negative.";
            if (cargoCapacity < 0f) yield return "cargoCapacity cannot be negative.";
        }
    }

    /// <summary>
    /// Owns real Pawn and Thing instances. Calling ThingOwner.DoTick is what
    /// keeps passenger needs, health, hediffs, immunity and biological timers
    /// advancing while the pawn is not spawned on the map.
    /// </summary>
    public sealed class CompAircraftManifest : ThingComp, IThingHolder
    {
        private ThingOwner<Thing> innerContainer;
        private Pawn pilot;
        private List<Thing> pendingCargo = new List<Thing>();

        public CompProperties_AircraftManifest Props => (CompProperties_AircraftManifest)props;
        public ThingOwner InnerContainer => innerContainer;
        public Pawn Pilot => IsValidOccupant(pilot) ? pilot : null;
        public bool HasCapablePilot => Pilot != null && !Pilot.Dead && !Pilot.Downed && !Pilot.InMentalState;
        public IEnumerable<Pawn> Occupants => innerContainer?.OfType<Pawn>() ?? Enumerable.Empty<Pawn>();
        public IEnumerable<Pawn> Passengers => Occupants.Where(pawn => pawn != Pilot);
        public IEnumerable<Thing> Cargo => innerContainer?.Where(thing => !(thing is Pawn)) ?? Enumerable.Empty<Thing>();
        public int PassengerCount => Passengers.Count();
        public float CargoMass => Cargo.Sum(StackMass);
        public float RemainingCargoMass => Mathf.Max(0f, Props.cargoCapacity - CargoMass);
        public IReadOnlyList<Thing> PendingCargo => pendingCargo;

        private AircraftThing Aircraft => parent as AircraftThing;
        private bool CanChangeManifest => Aircraft == null || !Aircraft.IsAirborne;

        public override void Initialize(CompProperties properties)
        {
            base.Initialize(properties);
            innerContainer = new ThingOwner<Thing>(this, false, LookMode.Deep, false);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref innerContainer, "aircraftContents", this);
            Scribe_References.Look(ref pilot, "aircraftPilot");
            Scribe_Collections.Look(ref pendingCargo, "pendingAircraftCargo", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (innerContainer == null)
                    innerContainer = new ThingOwner<Thing>(this, false, LookMode.Deep, false);
                if (pendingCargo == null) pendingCargo = new List<Thing>();
                CleanupManifest();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            innerContainer?.DoTick();
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                CleanupManifest();
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            if (previousMap != null && innerContainer != null)
            {
                innerContainer.TryDropAll(parent.Position, previousMap, ThingPlaceMode.Near, null, null, true);
            }
            else
            {
                innerContainer?.ClearAndDestroyContentsOrPassToWorld(mode);
            }

            base.PostDestroy(mode, previousMap);
        }

        public ThingOwner GetDirectlyHeldThings() => innerContainer;
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, innerContainer);
        }

        public bool TryBoard(Pawn pawn, bool asPilot, out string rejection)
        {
            rejection = null;
            if (!CanChangeManifest)
            {
                rejection = "HD_Aircraft_Manifest_Airborne".Translate();
                return false;
            }

            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != parent.Map)
            {
                rejection = "HD_Aircraft_InvalidOccupant".Translate();
                return false;
            }

            if (asPilot && Pilot != null)
            {
                rejection = "HD_Aircraft_PilotOccupied".Translate();
                return false;
            }

            if (!asPilot && PassengerCount >= Props.passengerCapacity)
            {
                rejection = "HD_Aircraft_PassengerFull".Translate();
                return false;
            }

            Map pawnMap = pawn.Map;
            IntVec3 pawnPosition = pawn.Position;
            pawn.DeSpawnOrDeselect();
            if (!innerContainer.TryAdd(pawn, false))
            {
                if (!pawn.Spawned && pawnMap != null)
                {
                    GenSpawn.Spawn(pawn, pawnPosition, pawnMap);
                }
                rejection = "HD_Aircraft_BoardingFailed".Translate();
                return false;
            }

            if (asPilot) pilot = pawn;
            return true;
        }

        public bool TryDisembark(Pawn pawn)
        {
            if (!CanChangeManifest || pawn == null || !innerContainer.Contains(pawn) || parent.Map == null)
                return false;

            bool wasPilot = pawn == pilot;
            bool dropped = innerContainer.TryDrop(pawn, parent.Position, parent.Map,
                ThingPlaceMode.Near, 1, out Thing _, null, null);
            if (dropped && wasPilot) pilot = null;
            return dropped;
        }

        public int CargoCountThatFits(Thing thing)
        {
            if (thing == null || thing is Pawn || !CanChangeManifest) return 0;
            float unitMass = Mathf.Max(0.0001f, thing.GetStatValue(StatDefOf.Mass));
            return Mathf.Clamp(Mathf.FloorToInt(RemainingCargoMass / unitMass), 0, thing.stackCount);
        }

        public bool DesignateCargo(Thing thing, out string rejection)
        {
            rejection = null;
            if (!CanChangeManifest)
            {
                rejection = "HD_Aircraft_Manifest_Airborne".Translate();
                return false;
            }

            if (thing == null || thing is Pawn || !thing.Spawned || thing.Map != parent.Map)
            {
                rejection = "HD_Aircraft_InvalidCargo".Translate();
                return false;
            }

            if (CargoCountThatFits(thing) <= 0)
            {
                rejection = "HD_Aircraft_CargoFull".Translate();
                return false;
            }

            if (!pendingCargo.Contains(thing)) pendingCargo.Add(thing);
            return true;
        }

        public Thing NextCargoFor(Pawn hauler)
        {
            CleanupPendingCargo();
            return pendingCargo
                .Where(thing => CargoCountThatFits(thing) > 0
                    && !thing.IsForbidden(hauler)
                    && hauler.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Some))
                .OrderBy(thing => thing.Position.DistanceToSquared(hauler.Position))
                .FirstOrDefault();
        }

        public bool TryAcceptCargoFrom(Pawn hauler, int maximumCount = int.MaxValue)
        {
            Thing carried = hauler?.carryTracker?.CarriedThing;
            if (carried == null) return false;
            int count = Mathf.Min(Mathf.Max(0, maximumCount), CargoCountThatFits(carried));
            if (count <= 0) return false;

            int transferred = hauler.carryTracker.innerContainer.TryTransferToContainer(
                carried, innerContainer, count, false);
            CleanupPendingCargo();
            return transferred > 0;
        }

        public void EjectAllCargo()
        {
            if (!CanChangeManifest || parent.Map == null) return;
            foreach (Thing thing in Cargo.ToList())
            {
                innerContainer.TryDrop(thing, parent.Position, parent.Map,
                    ThingPlaceMode.Near, thing.stackCount, out Thing _, null, null);
            }
        }

        /// <summary>
        /// Moves crew and ordinary manifest cargo into a real caravan while
        /// the aircraft is represented on the world map. This makes pawns,
        /// inventories, needs and trade goods visible to vanilla caravan
        /// interaction code. Bombs and aircraft fuel stay on the aircraft.
        /// </summary>
        public void TransferContentsToCaravan(Caravan caravan, out Pawn transferredPilot)
        {
            transferredPilot = null;
            if (caravan == null || innerContainer == null) return;
            List<Pawn> crew = ExtractOccupantsForWorld(out transferredPilot);

            foreach (Pawn pawn in crew)
            {
                caravan.AddPawn(pawn, true);
                AircraftCaravan.EnsureWorldPawn(pawn);
            }

            foreach (Thing thing in Cargo.ToList())
            {
                if (!innerContainer.Remove(thing)) continue;
                caravan.AddPawnOrItem(thing, true);
            }
            pendingCargo.Clear();
        }

        public List<Pawn> ExtractOccupantsForWorld(out Pawn extractedPilot)
        {
            extractedPilot = Pilot;
            List<Pawn> extracted = Occupants.ToList();
            foreach (Pawn pawn in extracted)
                innerContainer.Remove(pawn);
            pilot = null;
            return extracted;
        }

        public void RestoreOccupantsFromWorld(IEnumerable<Pawn> occupants, Pawn restoredPilot)
        {
            if (innerContainer == null || occupants == null) return;
            foreach (Pawn pawn in occupants)
            {
                if (pawn != null && !innerContainer.Contains(pawn))
                    innerContainer.TryAdd(pawn, false);
            }
            pilot = restoredPilot != null && innerContainer.Contains(restoredPilot)
                ? restoredPilot : null;
        }

        public override string CompInspectStringExtra()
        {
            string pilotLabel = Pilot?.LabelShortCap ?? "HD_Aircraft_None".Translate();
            return "HD_Aircraft_ManifestInspect".Translate(
                pilotLabel,
                PassengerCount,
                Props.passengerCapacity,
                CargoMass.ToString("0.#"),
                Props.cargoCapacity.ToString("0.#"));
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            yield return ManifestCommand("HD_Aircraft_Disembark_Label", "HD_Aircraft_Disembark_Desc",
                OpenDisembarkMenu, !Occupants.Any() ? "HD_Aircraft_NoOccupants".Translate() : null);
        }

        private Command_Action ManifestCommand(string label, string desc, Action action, string disabledReason)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel = label.Translate(),
                defaultDesc = desc.Translate(),
                icon = BaseContent.BadTex,
                action = action
            };
            if (!CanChangeManifest) command.Disable("HD_Aircraft_Manifest_Airborne".Translate());
            else if (!disabledReason.NullOrEmpty()) command.Disable(disabledReason);
            return command;
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.CompFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            // Aircraft ownership is intentionally irrelevant. Only the pawn
            // receiving the ordered job must be under player control.
            if (selPawn == null || !selPawn.IsColonistPlayerControlled
                || !selPawn.Spawned || parent.Map == null || selPawn.Map != parent.Map
                || !CanChangeManifest)
            {
                yield break;
            }

            string pilotLabel = "HD_Aircraft_RightClickBoardPilot".Translate(parent.LabelShort);
            string passengerLabel = "HD_Aircraft_RightClickBoardPassenger".Translate(parent.LabelShort);

            if (!selPawn.CanReach(parent, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                yield return new FloatMenuOption(pilotLabel + ": " + "NoPath".Translate(), null);
                if (Props.passengerCapacity > 0)
                    yield return new FloatMenuOption(passengerLabel + ": " + "NoPath".Translate(), null);
                yield break;
            }

            if (!selPawn.CanReserve(parent))
            {
                yield return new FloatMenuOption(pilotLabel + ": " + "Reserved".Translate(), null);
                if (Props.passengerCapacity > 0)
                    yield return new FloatMenuOption(passengerLabel + ": " + "Reserved".Translate(), null);
                yield break;
            }

            if (Pilot == null)
            {
                yield return new FloatMenuOption(pilotLabel, () => IssueBoardingJob(selPawn, true));
            }
            else
            {
                yield return new FloatMenuOption(pilotLabel + ": " + "HD_Aircraft_PilotOccupied".Translate(), null);
            }

            if (Props.passengerCapacity <= 0)
            {
                yield break;
            }

            if (PassengerCount < Props.passengerCapacity)
            {
                yield return new FloatMenuOption(passengerLabel, () => IssueBoardingJob(selPawn, false));
            }
            else
            {
                yield return new FloatMenuOption(passengerLabel + ": " + "HD_Aircraft_PassengerFull".Translate(), null);
            }
        }

        private void IssueBoardingJob(Pawn pawn, bool asPilot)
        {
            JobDef jobDef = asPilot
                ? AircraftJobDefOf.HD_BoardAircraftPilot
                : AircraftJobDefOf.HD_BoardAircraftPassenger;
            Job job = JobMaker.MakeJob(jobDef, parent);
            if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                Messages.Message("HD_Aircraft_BoardingJobRejected".Translate(pawn.LabelShortCap),
                    pawn, MessageTypeDefOf.RejectInput, false);
            }
        }

        private void OpenDisembarkMenu()
        {
            List<FloatMenuOption> options = Occupants
                .Select(pawn => new FloatMenuOption(
                    pawn == Pilot
                        ? "HD_Aircraft_PilotMenuEntry".Translate(pawn.LabelShortCap).ToString()
                        : pawn.LabelShortCap,
                    () => TryDisembark(pawn)))
                .ToList();
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }

        private void CleanupManifest()
        {
            if (!IsValidOccupant(pilot)) pilot = null;
            CleanupPendingCargo();
        }

        private void CleanupPendingCargo()
        {
            pendingCargo.RemoveAll(thing => thing == null || thing.Destroyed
                || !thing.Spawned || thing.Map != parent.Map || innerContainer.Contains(thing));
        }

        private bool IsValidOccupant(Pawn pawn)
        {
            return pawn != null && innerContainer != null && innerContainer.Contains(pawn);
        }

        private static float StackMass(Thing thing)
        {
            return thing.GetStatValue(StatDefOf.Mass) * thing.stackCount;
        }
    }

    [DefOf]
    public static class AircraftJobDefOf
    {
        public static JobDef HD_BoardAircraftPilot;
        public static JobDef HD_BoardAircraftPassenger;
        public static JobDef HD_LoadAircraftCargo;
        public static JobDef HD_LoadAircraftBomb;

        static AircraftJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AircraftJobDefOf));
        }
    }

    public sealed class JobDriver_BoardAircraft : JobDriver
    {
        private AircraftThing Aircraft => job.targetA.Thing as AircraftThing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Aircraft, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            Toil board = ToilMaker.MakeToil("BoardAircraft");
            board.initAction = () =>
            {
                // TryBoard despawns the pawn, which clears its current job.
                // Capture every job/target-derived value before that happens.
                AircraftThing aircraft = Aircraft;
                CompAircraftManifest manifest = aircraft?.TryGetComp<CompAircraftManifest>();
                bool asPilot = job.def == AircraftJobDefOf.HD_BoardAircraftPilot;
                string pawnLabel = pawn.LabelShortCap;
                string aircraftLabel = aircraft?.LabelShortCap ?? "aircraft";
                string rejection = null;
                if (manifest == null || !manifest.TryBoard(pawn, asPilot, out rejection))
                {
                    if (rejection.NullOrEmpty()) rejection = "HD_Aircraft_BoardingFailed".Translate();
                    if (!rejection.NullOrEmpty()) Messages.Message(rejection, pawn, MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                }
                else
                {
                    Messages.Message(
                        (asPilot ? "HD_Aircraft_BoardedAsPilot" : "HD_Aircraft_BoardedAsPassenger")
                            .Translate(pawnLabel, aircraftLabel),
                        aircraft, MessageTypeDefOf.NeutralEvent, false);
                }
            };
            board.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return board;
        }
    }

    public sealed class WorkGiver_LoadAircraftCargo : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Refuelable);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            CompAircraftTransporter transporter = thing.TryGetComp<CompAircraftTransporter>();
            return transporter != null && pawn.CanReserve(thing)
                && transporter.NextThingToLoad(pawn) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            CompAircraftTransporter transporter = thing.TryGetComp<CompAircraftTransporter>();
            Thing cargo = transporter?.NextThingToLoad(pawn);
            if (cargo == null) return null;
            int count = transporter.CountToLoad(cargo);
            if (count <= 0) return null;
            Job job = JobMaker.MakeJob(AircraftJobDefOf.HD_LoadAircraftCargo, thing, cargo);
            job.count = count;
            return job;
        }
    }

    public sealed class JobDriver_LoadAircraftCargo : JobDriver
    {
        private AircraftThing Aircraft => job.targetA.Thing as AircraftThing;
        private Thing Cargo => job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Aircraft, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(Cargo, job, 1, job.count, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnForbidden(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);
            // Keep the originally reserved count intact. The old
            // subtractNumTakenFromJobCount=true call changed job.count after
            // pickup and made the final transfer use a newly calculated size.
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, false,
                true, false, false);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil load = ToilMaker.MakeToil("LoadAircraftCargo");
            load.initAction = () =>
            {
                CompAircraftTransporter transporter =
                    Aircraft?.TryGetComp<CompAircraftTransporter>();
                bool accepted = transporter != null
                    && transporter.TryAcceptFrom(pawn, job.count);
                // A stack can shrink or capacity can change while the pawn is
                // walking. Never leave an untransferred remainder in the carry
                // tracker when this instant toil finishes.
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position,
                        ThingPlaceMode.Near, out Thing _);
                if (!accepted)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                job.count = 0;
                EndJobWith(JobCondition.Succeeded);
            };
            load.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return load;
        }
    }
}
