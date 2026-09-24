using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace Helodrace
{
    public static class HelodHelicopterSupport
    {
        public static bool ProviderAllows(Faction faction, HelodForwardBaseService service)
        {
            return service != HelodForwardBaseService.HelicopterQRF
                && service != HelodForwardBaseService.HelicopterMedevac
                || faction?.def?.defName?.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static CellRect LandingRect(IntVec3 cell) => new CellRect(cell.x - 1, cell.z - 1, 4, 3);

        public static bool ValidDZ(Map map, IntVec3 cell, HelodSupportHelicopter self = null)
        {
            if (map == null || !cell.IsValid) return false;
            CellRect rect = LandingRect(cell);
            foreach (IntVec3 c in rect)
                if (!c.InBounds(map) || !c.Standable(map) || c.Roofed(map) || c.Fogged(map)
                    || c.GetEdifice(map) != null || c.GetFirstThing<Fire>(map) != null) return false;
            return !map.listerThings.AllThings.OfType<HelodSupportHelicopter>()
                .Any(h => h != self && LandingRect(h.Position).Overlaps(rect));
        }

        public static bool Eligible(Pawn pawn, Map map) => pawn != null && pawn.Spawned
            && pawn.Map == map && !pawn.Dead && pawn.Faction == Faction.OfPlayer
            && pawn.RaceProps.Humanlike;

        public static bool AvailablePatient(Pawn pawn, Map map) => Eligible(pawn, map)
            && !map.listerThings.AllThings.OfType<HelodSupportHelicopter>().Any(h => h.ReservesPatient(pawn));

        public static void Request(Map map, HelodForwardBase source, Pawn caller, HelodForwardBaseService service)
        {
            if (service == HelodForwardBaseService.HelicopterMedevac)
                Find.WindowStack.Add(new Dialog_HelodMedevac(map, patients => TargetDZ(map, source, caller, service, patients)));
            else TargetDZ(map, source, caller, service, new List<Pawn>());
        }

        private static void TargetDZ(Map map, HelodForwardBase source, Pawn caller,
            HelodForwardBaseService service, List<Pawn> patients)
        {
            Messages.Message("HD_Heli_SelectDZ".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false,
                validator = t => ValidDZ(map, t.Cell)
            }, target =>
            {
                if (Find.CurrentMap != map || caller == null || caller.Map != map || source == null
                    || source.Destroyed || !source.HasService(service) || SCR300RadioUtility.IsBlackout(map)
                    || !source.HasServiceCapacity(service) || source.Tile < 0 || map.Tile < 0
                    || !ValidDZ(map, target.Cell)
                    || Find.WorldGrid.ApproxDistanceInTiles(source.Tile, map.Tile) > HelodForwardBaseServiceUtility.SupportRange(service)
                    || patients.Any(p => !AvailablePatient(p, map))
                    || (service == HelodForwardBaseService.HelicopterMedevac && (patients.Count < 1 || patients.Count > 3)))
                {
                    Messages.Message("HD_SCR300_ServiceUnavailable".Translate(), MessageTypeDefOf.RejectInput);
                    return;
                }
                var aircraft = (HelodSupportHelicopter)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_SupportHelicopter"));
                aircraft.Initialize(source, map, target.Cell, service, patients);
                if (aircraft.GetDirectlyHeldThings().Count == 0)
                {
                    aircraft.DiscardUnlaunched();
                    Messages.Message("HD_SCR300_DeploymentFailed".Translate(), MessageTypeDefOf.RejectInput);
                    return;
                }
                if (!source.TryConsumeServiceUse(service, out string reason))
                {
                    aircraft.DiscardUnlaunched();
                    Messages.Message(reason, MessageTypeDefOf.RejectInput);
                    return;
                }
                GenSpawn.Spawn(aircraft, target.Cell, map);
            }, null, () =>
            {
                if (Find.CurrentMap == map)
                    GenDraw.DrawFieldEdges(LandingRect(UI.MouseCell()).Where(c => c.InBounds(map)).ToList(),
                        ValidDZ(map, UI.MouseCell()) ? Color.green : Color.red);
            });
        }
    }

    public sealed class Dialog_HelodMedevac : Window
    {
        private readonly Map map;
        private readonly Action<List<Pawn>> accept;
        private readonly List<Pawn> selected = new List<Pawn>();
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(520f, 550f);
        public Dialog_HelodMedevac(Map map, Action<List<Pawn>> accept)
        {
            this.map = map; this.accept = accept;
            forcePause = true; doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = true;
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 55), "HD_Heli_SelectPatients".Translate(selected.Count));
            var pawns = map.mapPawns.AllPawnsSpawned.Where(p => HelodHelicopterSupport.AvailablePatient(p, map)).ToList();
            selected.RemoveAll(p => !pawns.Contains(p));
            Rect list = new Rect(0, 60, rect.width, rect.height - 115);
            Widgets.BeginScrollView(list, ref scroll, new Rect(0, 0, list.width - 18, pawns.Count * 34));
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                bool value = selected.Contains(pawn), previous = value;
                Widgets.CheckboxLabeled(new Rect(0, i * 34, list.width - 22, 30), pawn.LabelShortCap
                    + (Tactical.TcccUtility.MistReady(pawn) ? " (MIST)" : ""), ref value);
                if (Tactical.TcccUtility.MistReady(pawn))
                    TooltipHandler.TipRegion(new Rect(0, i * 34, list.width - 22, 30),
                        Tactical.TcccUtility.Effect(pawn, "HD_TCCC_Mist").report);
                if (value && !previous && selected.Count < 3) selected.Add(pawn);
                if (!value) selected.Remove(pawn);
            }
            Widgets.EndScrollView();
            if (Widgets.ButtonText(new Rect(0, rect.height - 42, rect.width, 38), "HD_Heli_ChooseDZ".Translate())
                && selected.Count > 0)
            {
                Close(); accept(new List<Pawn>(selected));
            }
        }
    }

    // The world holder owns the aircraft and its passengers while it is at the forward base.
    // References to maps/bases may disappear; passengers remain deep-saved until a home map is available.
    public sealed class HelodHelicopterFlights : WorldComponent, IThingHolder
    {
        private ThingOwner<HelodSupportHelicopter> aircraft;
        public HelodHelicopterFlights(World world) : base(world)
        { aircraft = new ThingOwner<HelodSupportHelicopter>(this, LookMode.Deep, false); }
        public IThingHolder ParentHolder => null;
        public ThingOwner GetDirectlyHeldThings() => aircraft;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, aircraft);
        public bool HasFlightFor(Map map) => aircraft.InnerListForReading.Any(h => h.HomeMap == map);
        public void Receive(HelodSupportHelicopter helicopter)
        { helicopter.DeSpawn(); aircraft.TryAdd(helicopter); helicopter.ArriveAtBase(); }
        public override void ExposeData() { base.ExposeData(); aircraft.ExposeData(); }
        public override void WorldComponentTick()
        {
            foreach (var helicopter in aircraft.InnerListForReading.ToList())
            {
                helicopter.TickAtBase();
                if (!helicopter.ReadyToReturn) continue;
                Map map = helicopter.HomeMap;
                if (map == null || !Find.Maps.Contains(map)) map = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
                if (map == null) continue;
                IntVec3 dz = helicopter.HomeDZ;
                if (!HelodHelicopterSupport.ValidDZ(map, dz)
                    && !CellFinder.TryFindRandomCell(map, c => HelodHelicopterSupport.ValidDZ(map, c), out dz)) continue;
                aircraft.Remove(helicopter);
                helicopter.BeginReturn(map, dz);
                GenSpawn.Spawn(helicopter, dz, map);
            }
        }
    }

    [StaticConstructorOnStartup]
    public sealed class HelodSupportHelicopter : ThingWithComps, IThingHolder
    {
        private static readonly Material[] BodyMaterials = { AircraftMaterial("DoorOutline"), AircraftMaterial("Base"),
            AircraftMaterial("DoorFront"), AircraftMaterial("WingFly") };
        private static readonly Material[] RotorMaterials = { AircraftMaterial("1Fly"), AircraftMaterial("2Fly"),
            AircraftMaterial("3Fly"), AircraftMaterial("4Fly") };
        private static Material AircraftMaterial(string suffix) => MaterialPool.MatFrom(
            "Effects/Aircraft/MH60M/HD_MH60M_" + suffix, ShaderDatabase.Transparent);
        private enum FlightPhase { Arriving, Loading, LeavingForBase, AtBase, Returning, Unloading, Leaving }
        private ThingOwner<Thing> passengers;
        private List<Pawn> patients = new List<Pawn>();
        private List<Pawn> crew = new List<Pawn>();
        private List<Pawn> delivered = new List<Pawn>();
        private HelodForwardBase source;
        private Map homeMap;
        private IntVec3 homeDZ;
        private HelodForwardBaseService service;
        private FlightPhase phase;
        private int phaseTicks;
        private int returnTick;
        private bool recalling;
        private bool mistDispatch;
        private Sustainer sound;
        private const int FlightTicks = 240;
        public Map HomeMap => homeMap;
        public IntVec3 HomeDZ => homeDZ;
        public bool ReadyToReturn => Find.TickManager.TicksGame >= returnTick;
        public bool IsLoading => phase == FlightPhase.Loading;
        public bool ReservesPatient(Pawn pawn) => patients.Contains(pawn) && !delivered.Contains(pawn)
            && phase != FlightPhase.Leaving;
        public HelodSupportHelicopter() { passengers = new ThingOwner<Thing>(this, LookMode.Deep); }
        public ThingOwner GetDirectlyHeldThings() => passengers;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, passengers);

        public void Initialize(HelodForwardBase source, Map map, IntVec3 dz, HelodForwardBaseService service, List<Pawn> selected)
        {
            this.source = source; homeMap = map; homeDZ = dz; this.service = service;
            SetFaction(source.Faction); patients = new List<Pawn>(selected);
            mistDispatch = selected.Count > 0 && selected.All(Tactical.TcccUtility.MistReady);
            var soldiers = PawnGroupMakerUtility.GeneratePawns(new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat, faction = source.Faction, tile = map.Tile,
                points = 700f, generateFightersOnly = true, dontUseSingleUseRocketLaunchers = true
            }).ToList();
            if (service == HelodForwardBaseService.HelicopterQRF)
                foreach (Pawn p in soldiers) passengers.TryAdd(p);
            else
            {
                foreach (Pawn p in soldiers)
                    if (crew.Count < selected.Count && !p.Downed) { crew.Add(p); passengers.TryAdd(p); }
                    else Find.WorldPawns.PassToWorld(p);
                while (crew.Count < selected.Count)
                {
                    Pawn p = PawnGenerator.GeneratePawn(source.Faction.def.basicMemberKind ?? PawnKindDefOf.Colonist, source.Faction);
                    crew.Add(p); passengers.TryAdd(p);
                }
            }
        }

        public void DiscardUnlaunched()
        {
            foreach (Pawn p in passengers.InnerListForReading.OfType<Pawn>().ToList()) { passengers.Remove(p); Find.WorldPawns.PassToWorld(p); }
            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData(); passengers.ExposeData();
            Scribe_Collections.Look(ref patients, "patients", LookMode.Reference);
            Scribe_Collections.Look(ref crew, "crew", LookMode.Reference);
            Scribe_Collections.Look(ref delivered, "delivered", LookMode.Reference);
            Scribe_References.Look(ref source, "source"); Scribe_References.Look(ref homeMap, "homeMap");
            Scribe_Values.Look(ref homeDZ, "homeDZ"); Scribe_Values.Look(ref service, "service");
            Scribe_Values.Look(ref phase, "phase"); Scribe_Values.Look(ref phaseTicks, "phaseTicks");
            Scribe_Values.Look(ref returnTick, "returnTick");
            Scribe_Values.Look(ref recalling, "recalling");
            Scribe_Values.Look(ref mistDispatch, "mistDispatch");
        }

        private void SetPhase(FlightPhase next) { phase = next; phaseTicks = 0; recalling = false; }
        public void ArriveAtBase()
        {
            SetPhase(FlightPhase.AtBase);
            // Only tend actively bleeding injuries; do not heal severity, restore limbs,
            // cure disease, or remove blood-loss hediffs.
            foreach (Pawn p in passengers.InnerListForReading.OfType<Pawn>().Where(p => patients.Contains(p) && !p.Dead))
                foreach (Hediff h in p.health.hediffSet.hediffs.ToList())
                    if (h.Bleeding && h.TendableNow()) h.Tended(1f, 1f);
            returnTick = Find.TickManager.TicksGame + 2 * GenDate.TicksPerHour - FlightTicks;
            Messages.Message("HD_Heli_AtBase".Translate(source?.LabelCap ?? ""), MessageTypeDefOf.PositiveEvent);
        }
        public void TickAtBase() { passengers.DoTick(); }
        public void BeginReturn(Map map, IntVec3 dz)
        { homeMap = map; homeDZ = dz; SetPhase(FlightPhase.Returning); }

        protected override void Tick()
        {
            base.Tick(); passengers.DoTick(); phaseTicks++;
            if (sound == null || sound.Ended)
                sound = DefDatabase<SoundDef>.GetNamedSilentFail("HD_HelicopterLoop")?.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.PerTick));
            sound?.Maintain();
            if (phaseTicks % 12 == 0) FleckMaker.ThrowDustPuff(Position.ToVector3Shifted(), Map, 1.5f);
            int arrivalTicks = phase == FlightPhase.Arriving && mistDispatch ? FlightTicks * 3 / 4 : FlightTicks;
            if ((phase == FlightPhase.Arriving || phase == FlightPhase.Returning) && phaseTicks >= arrivalTicks)
            {
                if (!HelodHelicopterSupport.ValidDZ(Map, Position, this))
                {
                    // Never land through a new roof/building. Wait for a safe nearby DZ.
                    if (CellFinder.TryFindRandomCellNear(Position, Map, 30, c => HelodHelicopterSupport.ValidDZ(Map, c, this), out IntVec3 replacement))
                    { Position = replacement; homeDZ = replacement; phaseTicks = 0; }
                    return;
                }
                if (service == HelodForwardBaseService.HelicopterQRF)
                {
                    List<Pawn> squad = passengers.InnerListForReading.OfType<Pawn>().ToList();
                    foreach (Pawn p in squad) SpawnPassenger(p);
                    if (squad.Count > 0) LordMaker.MakeNewLord(Faction, new LordJob_AssistColony(Faction.OfPlayer, Position), Map, squad);
                    SetPhase(FlightPhase.Leaving);
                }
                else
                {
                    bool returning = phase == FlightPhase.Returning;
                    foreach (Corpse corpse in passengers.InnerListForReading.OfType<Corpse>().ToList()) SpawnPassenger(corpse);
                    foreach (Pawn p in crew.Where(p => passengers.Contains(p)).ToList()) SpawnPassenger(p);
                    SetPhase(returning ? FlightPhase.Unloading : FlightPhase.Loading);
                }
            }
            if (phase == FlightPhase.Loading || phase == FlightPhase.Unloading)
            {
                if (!recalling && phaseTicks % 60 == 0) AssignCrew();
                bool finished = IsLoading
                    ? patients.All(p => p == null || p.Dead || p.Destroyed || passengers.Contains(p) || p.MapHeld != Map)
                    : patients.All(p => p == null || p.Dead || p.Destroyed || delivered.Contains(p)
                        || !passengers.Contains(p) && p.MapHeld != Map);
                if (recalling || finished || phaseTicks > GenDate.TicksPerHour)
                {
                    if (!recalling && phaseTicks > GenDate.TicksPerHour && !finished)
                    {
                        Messages.Message("HD_Heli_Timeout".Translate(), MessageTypeDefOf.CautionInput);
                        // Only unload patients actually still aboard. Never teleport a patient out of a rescuer's arms.
                        foreach (Pawn p in patients.Where(p => passengers.Contains(p)).ToList())
                            if (!IsLoading) { SpawnPassenger(p); delivered.Add(p); }
                    }
                    recalling = true;
                    RecallCrew();
                    if (crew.All(p => p == null || p.Dead || p.Downed || !p.Spawned || p.Map != Map)
                        || phaseTicks > 2 * GenDate.TicksPerHour)
                    {
                        if (phaseTicks > 2 * GenDate.TicksPerHour)
                            foreach (Pawn p in crew.Where(p => p != null && p.Spawned && p.Map == Map))
                            {
                                p.jobs.EndCurrentJob(JobCondition.InterruptForced);
                                if (p.carryTracker.CarriedThing != null)
                                    p.carryTracker.TryDropCarriedThing(p.Position, ThingPlaceMode.Near, out Thing dropped);
                                p.mindState.exitMapAfterTick = Find.TickManager.TicksGame;
                            }
                        // Patients who could not be recovered stay where they are, with their rescuers.
                        patients.RemoveAll(p => IsLoading && !passengers.Contains(p));
                        SetPhase(IsLoading && patients.Count > 0 ? FlightPhase.LeavingForBase : FlightPhase.Leaving);
                    }
                }
            }
            if (phase == FlightPhase.LeavingForBase && phaseTicks >= FlightTicks)
            {
                sound?.End(); Find.World.GetComponent<HelodHelicopterFlights>().Receive(this);
            }
            else if (phase == FlightPhase.Leaving && phaseTicks >= FlightTicks)
            {
                foreach (Thing p in passengers.InnerListForReading.ToList())
                {
                    if (!(p is Pawn pawn) || patients.Contains(pawn)) SpawnPassenger(p);
                    else { passengers.Remove(pawn); Find.WorldPawns.PassToWorld(pawn); }
                }
                Destroy();
            }
        }

        private void SpawnPassenger(Thing p)
        {
            passengers.Remove(p);
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(Position, Map, 3), Map);
        }

        private void AssignCrew()
        {
            foreach (Pawn medic in crew.Where(p => p != null && p.Spawned && p.Map == Map && !p.Dead && !p.Downed))
            {
                if (medic.CurJobDef == DefDatabase<JobDef>.GetNamed("HD_HeliCarry")
                    || medic.CurJobDef == DefDatabase<JobDef>.GetNamed("HD_HeliBoard")) continue;
                Pawn patient = patients.FirstOrDefault(p => p != null && !p.Destroyed && !delivered.Contains(p)
                    && (IsLoading ? HelodHelicopterSupport.Eligible(p, Map)
                        : !p.Dead && (passengers.Contains(p) || p.Spawned && p.Map == Map))
                    && !crew.Any(other => other != medic && other.CurJob?.targetA.Thing == p)
                    && (!IsLoading || medic.CanReach(p, PathEndMode.Touch, Danger.Deadly)));
                if (patient == null) continue;
                if (!IsLoading)
                {
                    if (passengers.Contains(patient)) SpawnPassenger(patient);
                    if (!patient.Downed && patient.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                    { delivered.Add(patient); continue; }
                }
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_HeliCarry"), patient, this);
                job.count = 1;
                if (!IsLoading) job.targetC = Destination(patient, medic);
                medic.jobs.StartJob(job, JobCondition.InterruptForced);
            }
        }

        private LocalTargetInfo Destination(Pawn patient, Pawn medic)
        {
            Building_Bed bed = Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>()
                .Where(b => !b.ForPrisoners && !b.IsForbidden(patient) && b.Position.GetFirstThing<Fire>(Map) == null)
                .Where(b => RestUtility.CanUseBedEver(patient, b.def) && b.CurOccupants.Count() < b.SleepingSlotsCount
                    && RestUtility.CanUseBedNow(b, patient, true, true)
                    && !crew.Any(p => p.CurJob?.targetC.Thing == b)
                    && medic.CanReach(b, PathEndMode.Touch, Danger.Deadly))
                .OrderByDescending(b => b == patient.ownership.OwnedBed).ThenByDescending(b => b.Medical).FirstOrDefault();
            if (bed != null) return bed;
            IntVec3 cell;
            if (CellFinder.TryFindRandomCell(Map, c => c.Standable(Map) && c.Roofed(Map) && !c.Fogged(Map)
                && medic.CanReach(c, PathEndMode.OnCell, Danger.Deadly), out cell)) return cell;
            return CellFinder.RandomClosewalkCellNear(Position, Map, 3);
        }

        public void PatientDelivered(Pawn patient) { if (!delivered.Contains(patient)) delivered.Add(patient); }
        public void LoadCarried(Pawn medic)
        {
            Pawn p = medic.carryTracker.CarriedThing as Pawn;
            if (p != null && patients.Contains(p))
                medic.carryTracker.innerContainer.TryTransferToContainer(p, passengers, 1);
        }
        public void Board(Pawn medic)
        {
            if (!crew.Contains(medic) || medic.carryTracker.CarriedThing != null) return;
            medic.jobs.EndCurrentJob(JobCondition.Succeeded, false);
            medic.DeSpawn(); passengers.TryAdd(medic);
        }
        private void RecallCrew()
        {
            foreach (Pawn medic in crew.Where(p => p != null && p.Spawned && p.Map == Map && !p.Dead && !p.Downed))
            {
                if (medic.CurJobDef == DefDatabase<JobDef>.GetNamed("HD_HeliCarry") || medic.CurJobDef == DefDatabase<JobDef>.GetNamed("HD_HeliBoard")) continue;
                if (!medic.CanReach(this, PathEndMode.Touch, Danger.Deadly))
                { medic.mindState.exitMapAfterTick = Find.TickManager.TicksGame; continue; }
                medic.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_HeliBoard"), this), JobCondition.InterruptForced);
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            sound?.End();
            if (Spawned) foreach (Thing p in passengers.InnerListForReading.ToList()) SpawnPassenger(p);
            base.Destroy(mode);
        }

        public override string GetInspectString() => "HD_Heli_Status".Translate(
            ("HD_TelegraphTable_ForwardBase_Service_" + service).Translate(),
            ("HD_Heli_Phase_" + phase).Translate(), passengers.Count);

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float progress = Mathf.Clamp01(phaseTicks / (float)(phase == FlightPhase.Arriving && mistDispatch ? FlightTicks * 3 / 4 : FlightTicks));
            bool arriving = phase == FlightPhase.Arriving || phase == FlightPhase.Returning;
            bool leaving = phase == FlightPhase.Leaving || phase == FlightPhase.LeavingForBase;
            float height = arriving ? (1f - Mathf.SmoothStep(0, 1, progress)) : leaving ? Mathf.SmoothStep(0, 1, progress) : 0f;
            drawLoc.z += height * 18f;
            drawLoc.x += arriving ? -height * 12f : height * 12f;
            float doorOffset = (phase == FlightPhase.Loading || phase == FlightPhase.Unloading)
                ? .45f * Mathf.SmoothStep(0f, 1f, phaseTicks / 60f) : 0f;
            for (int i = 0; i < 5; i++)
            {
                Vector3 partPosition = drawLoc;
                partPosition.y = AltitudeLayer.MoteOverhead.AltitudeFor() + i * .01f;
                if (i == 0 || i == 2) partPosition.x -= doorOffset;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(partPosition, Quaternion.identity,
                    new Vector3(5.2f, 1, 5.2f)), i < 4 ? BodyMaterials[i]
                        : RotorMaterials[Find.TickManager.TicksGame / 2 % 4], 0);
            }
        }
    }

    public sealed class JobDriver_HelodHeliCarry : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(Tactical.TcccUtility.MistReady(job.targetA.Pawn) ? 30 : 60, TargetIndex.A)
                .FailOn(() => job.targetA.Pawn == null || !job.targetA.Pawn.Spawned
                    || !pawn.Position.AdjacentTo8WayOrInside(job.targetA.Pawn.Position));
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, false, false);
            var heli = job.targetB.Thing as HelodSupportHelicopter;
            if (job.targetC.IsValid)
            {
                yield return Toils_Goto.Goto(TargetIndex.C, job.targetC.HasThing ? PathEndMode.Touch : PathEndMode.OnCell);
                yield return Toils_General.Wait(Tactical.TcccUtility.MistReady(job.targetA.Pawn) ? 30 : 60);
                yield return Toils_General.Do(() =>
                {
                    Pawn patient = pawn.carryTracker.CarriedThing as Pawn;
                    var bed = job.targetC.Thing as Building_Bed;
                    if (patient == null) return;
                    if (bed != null && (!bed.Spawned || !bed.AnyUnoccupiedSleepingSlot
                        || !RestUtility.CanUseBedNow(bed, patient, true, true)))
                    {
                        pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing safelyDropped);
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    IntVec3 cell = bed != null ? RestUtility.GetBedSleepingSlotPosFor(patient, bed) : job.targetC.Cell;
                    if (pawn.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Near, out Thing dropped))
                    {
                        heli?.PatientDelivered(patient);
                        if (bed != null && patient != null && !patient.Dead)
                            patient.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, bed), JobCondition.InterruptForced);
                    }
                });
            }
            else
            {
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
                yield return Toils_General.Do(() => heli?.LoadCarried(pawn));
            }
        }
    }
    public sealed class JobDriver_HelodHeliBoard : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => (job.targetA.Thing as HelodSupportHelicopter)?.Board(pawn));
        }
    }

    [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.AnyPawnBlockingMapRemoval), MethodType.Getter)]
    public static class HelodHelicopterMapRetention
    {
        public static void Postfix(Map ___map, ref bool __result)
        {
            if (!__result && ___map != null)
                __result = ___map.listerThings.AllThings.OfType<HelodSupportHelicopter>().Any()
                    || (Find.World?.GetComponent<HelodHelicopterFlights>()?.HasFlightFor(___map) ?? false);
        }
    }
}
