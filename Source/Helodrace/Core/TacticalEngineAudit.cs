using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Helodrace.Profiling;
using Helodrace.Squads;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace
{
    [DataContract]
    public sealed class TacticalEngineAuditResult
    {
        [DataMember] public int schema = 1, requestedPopulation, population, units, radioOperators, moved, entered, objectiveReached, alive;
        [DataMember] public int warmupTicks, sampleTicks, firstEntryTick = -1, firstObjectiveTick = -1, lastProgressTick;
        [DataMember] public int sapperEligiblePawns, breachedWallCells;
        [DataMember] public int measuredTicks;
        [DataMember] public int newPlannedUnits, newCompletedUnits, newPassedOpening, newEnteredByOrder, newRearPassedOpening;
        [DataMember] public int newDoorFaults;
        [DataMember] public long newJobsIssued, newJobFailures, newPlansAttempted;
        [DataMember] public long newObservations, newObservationContacts, newSupportThrows, newSupportWaits, newSupportReturns, newUnsafeEntries;
        [DataMember] public long newToolRecoveriesStarted, newToolRecoveriesCompleted, newCutterJobsStarted;
        [DataMember] public long newChargesInstalled, newChargeDetonations, newChargeOperatorTransfers, newChargeWaits;
        [DataMember] public long newRoomScanSteps, newRoomsSecured, newRoomPlansAttempted;
        [DataMember] public bool newRoomProgressComplete;
        [DataMember] public bool newUnexpectedOpeningReused, newDirectObjectiveCleared;
        [DataMember] public string[] newSecuredPortals;
        [DataMember] public int fixtureVersion = 13;
        [DataMember] public bool environmentControlled;
        [DataMember] public string[] unexpectedPawns;
        [DataMember] public bool newConnectedStacks, newFunctionalComplete;
        [DataMember] public bool newPhysicalPlansValid;
        [DataMember] public int newAllCompleteTick = -1;
        [DataMember] public string[] newCommands, newComponents, installedNewHooks;
        [DataMember] public string newLastJobFailure;
        [DataMember] public double? uninstrumentedMainCpuMs, uninstrumentedProcessCpuMs;
        [DataMember] public bool complete, isolationVerified, newEngineImplemented;
        [DataMember] public string engine, effectiveEngine, workload, seed, mapFingerprint, pawnFingerprint, error, fixtureCase;
        [DataMember] public bool caseTriggered, returnedOutsideAfterInterruption;
        [DataMember] public int interruptionTick = -1, returnOutsideTick = -1;
        [DataMember] public string returnOutsideJob;
        [DataMember] public string finalBoundarySketch;
        [DataMember] public string[] legacyComponents, installedLegacyHooks, finalPawnJobs;
    }

    // A new fixture independent of legacy plan/order getters. All engines receive
    // the same actual organizations, equipment, Lord and world geometry.
    public sealed class MapComponent_TacticalEngineAudit : MapComponent
    {
        internal static readonly HashSet<Pawn> ProtectedRaiders = new HashSet<Pawn>();
        internal static bool GeneratingFixture;
        internal static int FixturePawnIndex;
        private readonly string output;
        private readonly List<Pawn> raiders = new List<Pawn>();
        private readonly Dictionary<Pawn, IntVec3> starts = new Dictionary<Pawn, IntVec3>();
        private readonly HashSet<Pawn> entered = new HashSet<Pawn>(), arrived = new HashSet<Pawn>();
        private readonly HashSet<string> unexpected = new HashSet<string>();
        private Pawn owner;
        private Pawn interruptedPawn;
        private Tactics.TacticalSquadCommand interruptedCommand;
        private IntVec3 interruptionOpening;
        private int fixtureRight = 140, fixtureTop = 136;
        private int started, measured = -1, nextProgress;
        private long uninstrumentedMainStart = -1, uninstrumentedProcessStart;
        private readonly WindowsMethodClock windowClock = new WindowsMethodClock();
        private bool initialized, finishing, finished;
        private ProfileBenchmark benchmark;
        private readonly TacticalEngineAuditResult result = new TacticalEngineAuditResult();
        private bool MultiRoomFixture => result.fixtureCase == "multiroom" || result.fixtureCase == "unexpected-hole"
            || result.fixtureCase == "inside-goal";
        public MapComponent_TacticalEngineAudit(Map map) : base(map)
        {
            GenCommandLine.TryGetCommandLineArg("hdTacticalEngineAudit", out output);
        }
        private static int Argument(string name, int fallback)
        {
            if (!GenCommandLine.TryGetCommandLineArg(name, out string text)) return fallback;
            if (!int.TryParse(text, out int value) || value < 0) throw new ArgumentException("Invalid audit argument: " + name);
            return value;
        }
        public override void MapComponentUpdate()
        {
            if (output == null || finished) return;
            try
            {
                if (!initialized) { Initialize(); initialized = true; }
                if (finishing)
                {
                    AgentMethodProfiler.End();
                    if (AgentMethodProfiler.Capturing) return;
                    VerifyIsolation(); FinalDiagnostics(); result.complete = true;
                    Write(); finished = true; Application.Quit(); return;
                }
                Find.TickManager.CurTimeSpeed = TimeSpeed.Fast;
                int tick = GenTicks.TicksGame;
                ApplyCase();
                if (tick >= nextProgress) { Progress(tick); nextProgress = tick + 30; }
                if (measured < 0 && tick - started >= result.warmupTicks)
                {
                    measured = tick;
                    benchmark.startPhases = Phases();
                    AgentMethodProfiler.Begin("engine-audit", result.workload == "open-approach" ? 100 : 101,
                        raiders.Count, 3, seconds: 1800, benchmark: benchmark);
                    if (!GenCommandLine.TryGetCommandLineArg("hdMethodProfile", out _))
                    {
                        uninstrumentedProcessStart = windowClock.ProcessCpu100ns();
                        uninstrumentedMainStart = windowClock.Cpu100ns();
                    }
                }
                if (measured < 0 || tick - measured < result.sampleTicks) return;
                Progress(tick);
                benchmark.endPhases = Phases();
                result.measuredTicks = tick - measured;
                if (uninstrumentedMainStart >= 0)
                {
                    result.uninstrumentedMainCpuMs = (windowClock.Cpu100ns() - uninstrumentedMainStart) / 10000.0;
                    result.uninstrumentedProcessCpuMs = (windowClock.ProcessCpu100ns() - uninstrumentedProcessStart) / 10000.0;
                }
                AgentMethodProfiler.End(); finishing = true;
            }
            catch (Exception error)
            {
                result.error = error.ToString(); result.complete = false;
                Write(); finished = true; Log.Error("Tactical engine audit: " + error); Application.Quit();
            }
        }
        private void Initialize()
        {
            result.engine = TacticalEngineSelection.Kind.ToString().ToLowerInvariant();
            result.effectiveEngine = TacticalEngineSelection.EffectiveEngine;
            result.newEngineImplemented = TacticalEngineSelection.NewImplemented;
            result.requestedPopulation = Argument("hdTacticalAuditPopulation", 50);
            result.warmupTicks = Argument("hdTacticalAuditWarmup", 600);
            result.sampleTicks = Argument("hdTacticalAuditSample", 1200);
            if (result.sampleTicks == 0) throw new ArgumentException("Audit sample ticks must be positive.");
            GenCommandLine.TryGetCommandLineArg("hdTacticalAuditWorkload", out result.workload);
            result.fixtureCase = GenCommandLine.TryGetCommandLineArg("hdTacticalAuditCase", out string fixtureCase) ? fixtureCase : "normal";
            if (result.fixtureCase != "normal" && result.fixtureCase != "interrupt" && result.fixtureCase != "casualty"
                && result.fixtureCase != "rocks" && result.fixtureCase != "narrow" && result.fixtureCase != "contact" && result.fixtureCase != "field"
                && result.fixtureCase != "recovery" && result.fixtureCase != "cutter" && result.fixtureCase != "cutter-recovery"
                && result.fixtureCase != "charge-recovery" && result.fixtureCase != "charge-fuse-casualty" && result.fixtureCase != "charge-change"
                && result.fixtureCase != "multiroom" && result.fixtureCase != "unexpected-hole"
                && result.fixtureCase != "inside-goal") throw new ArgumentException("Unknown audit case.");
            if (result.workload != "open-approach" && result.workload != "sapper-wall" && result.workload != "sapper-door") throw new ArgumentException("Unknown workload.");
            GenCommandLine.TryGetCommandLineArg("hdRaidMovementAuditSeed", out result.seed);
            bool high = GenCommandLine.TryGetCommandLineArg("hdTacticalAuditHigh", out _);
            Faction faction = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed(
                high ? "HD_HelodCivilHighFaction" : "HD_HelodCivilLowFaction"));
            faction.RelationWith(Faction.OfPlayer).baseGoodwill = -100;
            faction.RelationWith(Faction.OfPlayer).kind = FactionRelationKind.Hostile;
            Faction.OfPlayer.RelationWith(faction).baseGoodwill = -100;
            Faction.OfPlayer.RelationWith(faction).kind = FactionRelationKind.Hostile;
            if (!faction.HostileTo(Faction.OfPlayer)) throw new InvalidOperationException("Audit faction must be hostile.");
            owner = map.mapPawns.FreeColonists.First();
            // The isolated fixture must not acquire an unrelated raid/disease
            // midway through a longer CQB window. These flags are audit-only;
            // this process quits when the capture ends.
            DebugSettings.enableStoryteller = false;
            DebugSettings.enableRandomDiseases = false;
            DebugSettings.enableRandomMentalStates = false;
            DebugSettings.noAnimals = true;
            Find.Storyteller.incidentQueue = new IncidentQueue();
            MapComponent_RaidMovementRuntimeAudit.ProtectedOwner = owner;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList()) if (pawn != owner) pawn.Destroy(DestroyMode.Vanish);
            foreach (IntVec3 cell in map.AllCells)
            {
                foreach (Thing thing in cell.GetThingList(map).ToList()) if (!(thing is Pawn))
                { if (thing.def.destroyable) thing.Destroy(DestroyMode.Vanish); else thing.DeSpawn(); }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.roofGrid.SetRoof(cell, null);
            }
            // Destroying an ancient casket during the building cleanup can spawn
            // its sleeping pawn. Clear those before generating the fixture force.
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList()) if (pawn != owner) pawn.Destroy(DestroyMode.Vanish);
            void Place(ThingDef def, IntVec3 position)
            {
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
                thing.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(thing, position, map, WipeMode.Vanish);
            }
            bool small = result.fixtureCase == "narrow" || result.fixtureCase == "contact";
            int right = small ? 105 : 140, top = small ? 105 : 136;
            fixtureRight = right; fixtureTop = top;
            if (result.fixtureCase != "field")
            {
            for (int x = 100; x <= right; x++)
            { Place(ThingDefOf.Wall, new IntVec3(x, 0, 100)); Place(ThingDefOf.Wall, new IntVec3(x, 0, top)); }
            for (int z = 101; z < top; z++)
            {
                if (result.workload != "open-approach" || z != 118)
                    Place(result.workload == "sapper-door" && z == 108 ? ThingDefOf.Door : ThingDefOf.Wall, new IntVec3(100, 0, z));
                Place(ThingDefOf.Wall, new IntVec3(right, 0, z));
            }
            for (int x = 101; x < right; x++) for (int z = 101; z < top; z++)
                map.roofGrid.SetRoof(new IntVec3(x, 0, z), RoofDefOf.RoofConstructed);
            }
            if (MultiRoomFixture)
            {
                for (int z = 101; z < top; z++) Place(z == 108 ? ThingDefOf.Door : ThingDefOf.Wall, new IntVec3(114, 0, z));
                for (int x = 115; x < right; x++) Place(x == 126 ? ThingDefOf.Door : ThingDefOf.Wall, new IntVec3(x, 0, 119));
            }
            if (result.fixtureCase == "rocks")
                for (int z = 105; z <= 114; z += 2) Place(ThingDefOf.Wall, new IntVec3(99, 0, z));
            IntVec3 goal = small ? new IntVec3(103,0,103)
                : result.fixtureCase == "inside-goal" ? new IntVec3(108,0,110)
                : MultiRoomFixture ? new IntVec3(120,0,116) : new IntVec3(120, 0, 118);
            Place(ThingDefOf.Bed, goal);
            ((Building_Bed)goal.GetEdifice(map)).CompAssignableToPawn.TryAssignPawn(owner);
            owner.Position = goal; owner.drafter.Drafted = true; owner.equipment.DestroyAllEquipment();
            // Keep the named bed as objective, but avoid intentionally aiming a
            // grenade beside the final partition in the three-separate-room case.
            if (MultiRoomFixture && result.fixtureCase != "inside-goal") owner.Position = new IntVec3(120,0,110);
            if (MultiRoomFixture && Enumerable.Range(115, right - 115).Any(x =>
                {
                    Building partition = new IntVec3(x,0,119).GetEdifice(map);
                    return partition?.def.IsWall != true && !(partition is Building_Door);
                })) throw new InvalidOperationException("The named bed must not wipe the multiroom partition.");
            Job wait = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture); wait.expiryInterval = 1000000;
            owner.jobs.StartJob(wait, JobCondition.InterruptForced);
            result.mapFingerprint = RaidAuditSeed.Fingerprint(map);
            FormationDef formation = DefDatabase<FormationDef>.GetNamed(high ? "HD_Formation_MW_RifleSquad" : "HD_Formation_GW_RifleSquad");
            while (raiders.Count < result.requestedPopulation)
            {
                List<Pawn> members;
                CombatOrganization organization;
                FixturePawnIndex = raiders.Count;
                GeneratingFixture = true;
                try { members = OrganizationGenerator.Generate(new PawnGroupMakerParms { faction = faction,
                    groupKind = PawnGroupKindDefOf.Combat, raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                    points = formation.FormationCost, tile = map.Tile, seed = 347001 + result.units }, out organization); }
                finally { GeneratingFixture = false; }
                if (members.Count != OrganizationGenerator.KindsFor(formation).Count() || organization.rootGroups.Count != 1
                    || organization.rootGroups[0].formation != formation) throw new InvalidOperationException("Fixture requires a complete rifle squad.");
                result.units++;
                foreach (Pawn pawn in members)
                {
                    int index = raiders.Count;
                    IntVec3 cell = result.fixtureCase == "inside-goal" ? new IntVec3(104 + index % 4,0,104 + index / 4)
                        : new IntVec3(66 + index % 16, 0, 90 + index / 16);
                    GenSpawn.Spawn(pawn, cell, map); raiders.Add(pawn); starts[pawn] = cell;
                }
            }
            result.population = raiders.Count;
            ProtectedRaiders.Clear(); foreach (Pawn pawn in raiders) ProtectedRaiders.Add(pawn);
            result.radioOperators = raiders.Count(pawn => RaidTacticalRadioUtility.Radios(pawn).Any());
            if (result.workload.StartsWith("sapper-", StringComparison.Ordinal) && raiders.Count > 0)
            {
                // HIGH's normal kind is not vanilla-sapper eligible. Give the same
                // fixture-only capability to all engines; never change production XML.
                Pawn miner = raiders.FirstOrDefault(pawn => !pawn.skills.GetSkill(SkillDefOf.Mining).TotallyDisabled
                    && !StatDefOf.MiningSpeed.Worker.IsDisabledFor(pawn));
                if (miner == null) throw new InvalidOperationException("Fixture has no mining-capable pawn.");
                miner.kindDef.canBeSapper = true;
                miner.skills.GetSkill(SkillDefOf.Mining).Level = 20;
                result.sapperEligiblePawns = raiders.Count(SappersUtility.IsGoodBackupSapper);
                if (result.sapperEligiblePawns == 0) throw new InvalidOperationException("Fixture has no eligible vanilla sapper.");
            }
            ConfigureBreachEquipment();
            result.pawnFingerprint = PawnFingerprint();
            if (result.fixtureCase == "field") map.GetComponent<Tactics.MapComponent_TacticalCommands>()?.SetObjective(goal + new IntVec3(1,0,1));
            if (result.fixtureCase == "inside-goal") map.GetComponent<Tactics.MapComponent_TacticalCommands>()?.SetObjective(goal);
            if (raiders.Count > 0) LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false,
                canTimeoutOrFlee: false, sappers: result.workload.StartsWith("sapper-", StringComparison.Ordinal), canSteal: false), map, raiders);
            benchmark = new ProfileBenchmark { fixtureVersion = 13, seed = result.seed, mapFingerprint = result.mapFingerprint,
                faction = faction.def.defName, requestedPopulation = result.requestedPopulation, unitCount = result.units,
                radioOperators = result.radioOperators, warmupTicks = result.warmupTicks, sampleTicks = result.sampleTicks,
                engine = result.engine, effectiveEngine = result.effectiveEngine, newEngineImplemented = result.newEngineImplemented,
                workload = result.workload, pawnFingerprint = result.pawnFingerprint, fixtureCase = result.fixtureCase };
            VerifyIsolation(); started = GenTicks.TicksGame;
        }
        private void ConfigureBreachEquipment()
        {
            bool charge = result.fixtureCase.StartsWith("charge-", StringComparison.Ordinal);
            if (result.fixtureCase != "recovery" && result.fixtureCase != "cutter" && result.fixtureCase != "cutter-recovery" && !charge) return;
            bool cutter = result.fixtureCase.StartsWith("cutter", StringComparison.Ordinal);
            bool kept = false;
            foreach (Pawn pawn in raiders)
                foreach (Apparel apparel in pawn.apparel.WornApparel.ToArray())
                    if (apparel.TryGetComp<CompSledgehammerBreach>() != null)
                    {
                        if (!cutter && !charge && !kept) { kept = true; continue; }
                        pawn.apparel.Remove(apparel); apparel.Destroy();
                    }
            if (!cutter && !charge && !kept) throw new InvalidOperationException("Recovery fixture needs one real worn hammer.");
            if (charge)
            {
                foreach (Pawn pawn in raiders)
                    foreach (Thing item in pawn.inventory.innerContainer.ToArray())
                        if (item.def == BreachExplosiveUtility.C4Def || (item as ThingWithComps)?.TryGetComp<CompBreachIgniter>() != null)
                        { pawn.inventory.innerContainer.Remove(item); item.Destroy(); }
                Thing c4 = ThingMaker.MakeThing(BreachExplosiveUtility.C4Def); c4.stackCount = 3;
                raiders[0].inventory.innerContainer.TryAdd(c4);
                raiders[0].inventory.innerContainer.TryAdd(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(
                    result.fixtureCase == "charge-recovery" ? "HD_M81Igniter" : "HD_M60Igniter")));
            }
            if (cutter)
            {
                Pawn pawn = raiders[0];
                if (pawn.equipment.Primary != null) { var old = pawn.equipment.Primary; pawn.equipment.Remove(old); old.Destroy(); }
                pawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_PowerCutter")));
            }
        }

        private void ApplyCase()
        {
            if (interruptedPawn?.Spawned == true && interruptedCommand.Phase != Tactics.TacticalCommandPhase.Complete
                && interruptedCommand.Phase != Tactics.TacticalCommandPhase.Released
                && interruptedPawn.Position.x < interruptionOpening.x + 1)
            {
                result.returnedOutsideAfterInterruption = true;
                if (result.returnOutsideTick < 0)
                { result.returnOutsideTick = GenTicks.TicksGame - started; result.returnOutsideJob = interruptedPawn.CurJobDef?.defName; }
            }
            if (result.fixtureCase != "interrupt" && result.fixtureCase != "casualty" && result.fixtureCase != "contact"
                && result.fixtureCase != "recovery" && result.fixtureCase != "cutter-recovery" && result.fixtureCase != "unexpected-hole"
                && !result.fixtureCase.StartsWith("charge-", StringComparison.Ordinal)) return;
            if (result.caseTriggered || TacticalEngineSelection.Kind != TacticalEngineKind.New) return;
            var service = map.GetComponent<Tactics.MapComponent_TacticalCommands>();
            if (result.fixtureCase == "unexpected-hole")
            {
                // Change a different boundary after the squad commits to its
                // second entry, before that room's one-shot structural survey.
                var command = service.Commands.FirstOrDefault(item => item.SecuredPlans.Count == 1
                    && item.Phase == Tactics.TacticalCommandPhase.Observe && item.Plan?.Opening.x == 114);
                if (command == null) return;
                var hole = new IntVec3(120,0,119);
                Building wall = hole.GetEdifice(map);
                if (wall?.def.IsWall != true) throw new InvalidOperationException("Unexpected-hole fixture must begin with an intact wall.");
                wall.Destroy(DestroyMode.Vanish); result.caseTriggered = true;
            }
            else if (result.fixtureCase.StartsWith("charge-", StringComparison.Ordinal))
            {
                var command = service.Commands.FirstOrDefault(item => item.ChargeAction != null
                    && (result.fixtureCase == "charge-recovery" ? item.ChargeAction.Charge == null
                        && item.ChargeAction.Installer?.CurJobDef?.defName == "HD_NewTacticalInstallCharge"
                        : result.fixtureCase == "charge-change" ? item.ChargeAction.Detonated && !item.ChargeAction.EffectsCleared
                        : item.ChargeAction.Charge != null && !item.ChargeAction.Detonated));
                if (command == null) return;
                result.caseTriggered = true;
                if (result.fixtureCase == "charge-change")
                { service.SetObjective(command.Goal + new IntVec3(1,0,1)); command.ChargeAction.Charge.OperatorPawn.Kill(null); }
                else command.ChargeAction.Installer.Kill(null);
            }
            else if (result.fixtureCase == "contact")
            {
                var command = service.Commands.FirstOrDefault(item => item.Phase == Tactics.TacticalCommandPhase.Observe && item.Plan != null);
                if (command == null) return;
                // Place the stationary fixture enemy on the opening sight line
                // before observation. Keep the named bed as the mission goal.
                owner.Position = command.Plan.Inside + command.Plan.Inward * 2; result.caseTriggered = true;
            }
            else if (result.fixtureCase == "interrupt")
            {
                var member = service.Commands.SelectMany(command => command.Members)
                    .LastOrDefault(item => item.Crossed && !item.Entered && item.Pawn.jobs.curDriver is Tactics.JobDriver_TacticalIngress driver && !driver.AtPost);
                if (member == null) return;
                interruptedPawn = member.Pawn;
                interruptedCommand = service.Commands.First(command => command.Members.Contains(member));
                interruptionOpening = interruptedCommand.Plan.Opening;
                result.caseTriggered = true;
                result.interruptionTick = GenTicks.TicksGame - started;
                interruptedPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            else if (result.fixtureCase == "casualty" || result.fixtureCase == "recovery" || result.fixtureCase == "cutter-recovery")
            {
                var command = service.Commands.FirstOrDefault(item => item.Phase == Tactics.TacticalCommandPhase.Breach
                    && item.Breacher?.CurJobDef?.defName == (result.fixtureCase == "cutter-recovery" ? "HD_NewTacticalCut" : "HD_NewTacticalBreach"));
                if (command == null) return;
                result.caseTriggered = true; command.Breacher.Kill(null);
            }
        }
        private string PawnFingerprint()
        {
            var text = new StringBuilder();
            foreach (Pawn pawn in raiders)
            {
                text.Append(pawn.kindDef.defName).Append('|').Append(pawn.kindDef.canBeSapper).Append('|')
                    .Append(pawn.ageTracker.AgeBiologicalTicks).Append('|').Append(pawn.gender).Append('|')
                    .Append(pawn.story.Childhood?.defName).Append('|').Append(pawn.story.Adulthood?.defName).Append('|');
                foreach (var trait in pawn.story.traits.allTraits)
                    text.Append(trait.def.defName).Append(':').Append(trait.Degree).Append(';');
                if (pawn.genes != null)
                    foreach (var gene in pawn.genes.GenesListForReading)
                        text.Append(gene.def.defName).Append(':').Append(gene.Active).Append(';');
                foreach (var skill in pawn.skills.skills) text.Append(skill.def.defName).Append(':').Append(skill.Level)
                    .Append(':').Append(skill.passion).Append(';');
                foreach (Thing item in pawn.apparel.WornApparel.Cast<Thing>().Concat(pawn.equipment.AllEquipmentListForReading)
                    .Concat(pawn.inventory.innerContainer).OrderBy(item => item.def.defName))
                {
                    text.Append(item.def.defName).Append(':').Append(item.Stuff?.defName).Append(':').Append(item.stackCount)
                        .Append(':').Append(item.HitPoints).Append(':').Append(item.TryGetComp<CompQuality>()?.Quality.ToString()).Append(';');
                    var weapon = item.TryGetComp<ModernWar.CompModularWeaponNode>();
                    if (weapon != null) text.Append(CanonicalPreset(ModernWar.ModularPresetXmlExporter.ExportWeapon(weapon)));
                    var armor = item.TryGetComp<ModernWar.CompModularArmor>();
                    if (armor != null) text.Append(CanonicalPreset(ModernWar.ModularPresetXmlExporter.ExportArmor(armor)));
                }
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                    text.Append(hediff.def.defName).Append(':').Append(hediff.Severity.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                text.Append('\n');
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "pawn-manifest.txt"), text.ToString());
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
        }
        internal static string CanonicalPreset(string xml)
        {
            // Root export DefName contains DateTime.UtcNow, a document ID rather
            // than gear. Keep every real part/slot and configuration value.
            int start = xml.IndexOf("<defName>", StringComparison.Ordinal);
            int end = xml.IndexOf("</defName>", StringComparison.Ordinal);
            return start >= 0 && end >= start ? xml.Remove(start, end + "</defName>".Length - start) : xml;
        }
        private void FinalDiagnostics()
        {
            var newService = map.GetComponent<Tactics.MapComponent_TacticalCommands>();
            if (newService != null)
            {
                var commands = newService.Commands.ToArray();
                result.newPlannedUnits = commands.Count(command => command.Plan != null);
                result.newCompletedUnits = commands.Count(command => command.Phase == Tactics.TacticalCommandPhase.Complete);
                result.newPassedOpening = commands.Sum(command => command.Members.Count(member => member.Passed));
                result.newEnteredByOrder = commands.Sum(command => command.Members.Count(member => member.Entered));
                result.newRearPassedOpening = commands.Sum(command => command.Members.Count(member => member.Passed && member.Rear));
                result.newConnectedStacks = commands.All(command => command.Plan != null && (command.Plan.Direct || command.HadConnectedStack));
                bool Interior(IntVec3 cell) => cell.x > 100 && cell.x < fixtureRight && cell.z > 100 && cell.z < fixtureTop;
                bool Physical(Tactics.TacticalLocalPlan plan, int count) => plan.Positions.All(Interior)
                    && plan.Positions.Distinct().Count() == count && (plan.Direct || Interior(plan.Inside)
                        && (MultiRoomFixture || !Interior(plan.Outside)));
                result.newPhysicalPlansValid = commands.All(command => command.Plan != null
                    && command.Plan.Positions.All(Interior) && command.Plan.Positions.Distinct().Count() == command.Members.Count
                    && (command.Plan.Direct || (command.Plan.Opening.x == 100 || command.Plan.Opening.x == fixtureRight
                        || command.Plan.Opening.z == 100 || command.Plan.Opening.z == fixtureTop)
                        && !Interior(command.Plan.Outside) && Interior(command.Plan.Inside)));
                if (MultiRoomFixture)
                    result.newPhysicalPlansValid = commands.All(command => command.Plan != null && Physical(command.Plan, command.Members.Count)
                        && command.SecuredPlans.All(plan => Physical(plan, command.Members.Count) && (plan.Direct || Tactics.TacticalLocalPlanner.Connected(plan.Stack))));
                result.newJobsIssued = newService.JobsIssued; result.newJobFailures = newService.JobFailures;
                result.newLastJobFailure = newService.LastJobFailure;
                result.newPlansAttempted = newService.PlansAttempted;
                result.newObservations = newService.Observations; result.newObservationContacts = newService.ObservationContacts;
                result.newSupportThrows = newService.SupportThrows; result.newSupportWaits = newService.SupportWaits;
                result.newSupportReturns = newService.SupportReturns; result.newUnsafeEntries = newService.UnsafeEntries;
                result.newToolRecoveriesStarted = newService.ToolRecoveriesStarted;
                result.newToolRecoveriesCompleted = newService.ToolRecoveriesCompleted;
                result.newCutterJobsStarted = newService.CutterJobsStarted;
                result.newChargesInstalled = newService.ChargesInstalled; result.newChargeDetonations = newService.ChargeDetonations;
                result.newChargeOperatorTransfers = newService.ChargeOperatorTransfers; result.newChargeWaits = newService.ChargeWaits;
                result.newRoomScanSteps = newService.RoomScanSteps; result.newRoomsSecured = newService.RoomsSecured;
                result.newRoomPlansAttempted = newService.RoomPlansAttempted;
                result.newSecuredPortals = commands.Select(command => command.Id + ":" + string.Join(";", command.SecuredPlans
                    .Select(plan => plan.Opening.ToString()))).ToArray();
                result.newRoomProgressComplete = !MultiRoomFixture || commands.All(command => command.GoalSecured
                    && new[] { new IntVec3(108,0,110), new IntVec3(120,0,110), new IntVec3(120,0,128) }.All(command.SecuredCells.Contains)
                    && command.SecuredPlans.Count >= 3 && command.SecuredPlans.Select(plan => plan.Opening).Distinct().Count() == command.SecuredPlans.Count);
                result.newUnexpectedOpeningReused = result.fixtureCase != "unexpected-hole" || result.caseTriggered
                    && commands.All(command => command.SecuredPlans.Any(plan => plan.Opening == new IntVec3(120,0,119) && plan.ExistingOpening));
                result.newDirectObjectiveCleared = result.fixtureCase != "inside-goal" || commands.All(command => command.GoalSecured
                    && command.SecuredPlans.FirstOrDefault()?.Direct == true
                    && command.SecuredPlans[0].Positions.All(cell => cell.x > 100 && cell.x < 114));
                result.newDoorFaults = map.listerThings.AllThings.OfType<Building_Door>().Count(DoorBreachFaultUtility.Jammed);
                result.newFunctionalComplete = commands.Length == result.units && result.newCompletedUnits == result.units
                    && result.newEnteredByOrder == result.alive && result.newConnectedStacks && result.newPhysicalPlansValid && result.newRoomProgressComplete
                    && result.newUnexpectedOpeningReused && result.newDirectObjectiveCleared
                    && commands.All(command => command.Plan.Direct || command.Members.All(member => member.Pawn.Dead || member.Pawn.Downed || member.Passed));
                result.newCommands = commands.Select(command => command.Id + ":" + command.Phase + " opening=" + command.Plan?.Opening
                    + " outside=" + command.Plan?.Outside + ":walkable=" + (command.Plan?.Outside.Standable(map))
                    + ":edifice=" + command.Plan?.Outside.GetEdifice(map)
                    + " barrier=" + (command.Plan?.Opening.GetEdifice(map) is Building_Door entryDoor
                        ? entryDoor + ":open=" + entryDoor.Open + ":jam=" + DoorBreachFaultUtility.Jammed(entryDoor) : "wall/gap")
                    + " direct=" + command.Plan?.Direct + " failures=" + command.Failures + " lastFailure=" + command.LastPlanFailure
                    + " tools=" + command.Members.Count(member => CompSledgehammerBreach.WornBy(member.Pawn) != null)
                    + " members="
                    + string.Join(";", command.Members.Select((member, index) => member.Pawn.Position + ":" + member.Pawn.CurJobDef
                        + ":passed=" + member.Passed + ":entered=" + member.Entered + ":slot="
                        + (command.Plan == null ? "none" : command.Plan.Positions[index].ToString())
                        + ":jobA=" + member.Pawn.CurJob?.targetA + ":jobB=" + member.Pawn.CurJob?.targetB
                        + ":jobC=" + member.Pawn.CurJob?.targetC + ":toil=" + member.Pawn.jobs.curDriver?.CurToilIndex
                        + ":moving=" + member.Pawn.pather.Moving + ":dest=" + member.Pawn.pather.Destination))).ToArray();
            }
            result.finalPawnJobs = raiders.Select(pawn => pawn.kindDef.defName + "@" + pawn.Position + ":"
                + pawn.CurJob?.def?.defName + "->" + pawn.CurJob?.targetA.ToString()).ToArray();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                if (pawn != owner && !ProtectedRaiders.Contains(pawn)) unexpected.Add(pawn.ThingID + ":" + pawn.kindDef.defName);
            result.unexpectedPawns = unexpected.OrderBy(id => id).ToArray();
            result.environmentControlled = unexpected.Count == 0 && !DebugSettings.enableStoryteller
                && !DebugSettings.enableRandomDiseases && !DebugSettings.enableRandomMentalStates && DebugSettings.noAnimals;
            result.breachedWallCells = Enumerable.Range(101,fixtureTop - 101).Count(z => new IntVec3(100,0,z).GetEdifice(map) == null
                && (result.workload != "open-approach" || z != 118));
            var sketch = new StringBuilder("x=96..108, rows z=98.." + (fixtureTop + 1) + "\n");
            for (int z = 98; z <= fixtureTop + 1; z++)
            {
                sketch.Append(z).Append(' ');
                for (int x = 96; x <= 108; x++)
                {
                    var cell = new IntVec3(x,0,z); Building building = cell.GetEdifice(map);
                    sketch.Append(building is Building_Door ? 'D' : building?.def.IsWall == true ? '#'
                        : !cell.Standable(map) ? 'X' : cell.GetFirstPawn(map) != null ? 'p' : '.');
                }
                sketch.Append('\n');
            }
            result.finalBoundarySketch = sketch.ToString();
        }
        private string Phases() => TacticalEngineSelection.Kind == TacticalEngineKind.New
            ? string.Join(",", map.GetComponent<Tactics.MapComponent_TacticalCommands>().Commands.GroupBy(command => command.Phase)
                .OrderBy(group => group.Key).Select(group => group.Key + ":" + group.Count()))
            : TacticalEngineSelection.Kind != TacticalEngineKind.Legacy ? result.effectiveEngine
            : string.Join(",", map.GetComponent<MapComponent_RaidTacticalPlans>().Plans
                .GroupBy(plan => map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(plan.UnitId)?.Phase.ToString() ?? "NoState")
                .OrderBy(group => group.Key).Select(group => group.Key + ":" + group.Count()));
        private void Progress(int tick)
        {
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                if (pawn != owner && !ProtectedRaiders.Contains(pawn)) unexpected.Add(pawn.ThingID + ":" + pawn.kindDef.defName);
            if (result.newAllCompleteTick < 0 && result.units > 0 && TacticalEngineSelection.Kind == TacticalEngineKind.New)
            {
                var service = map.GetComponent<Tactics.MapComponent_TacticalCommands>();
                if (service.Commands.Count() == result.units && service.Commands.All(command => command.Phase == Tactics.TacticalCommandPhase.Complete))
                    result.newAllCompleteTick = tick - started;
            }
            foreach (Pawn pawn in raiders.Where(pawn => pawn.Spawned && !pawn.Dead && !pawn.Downed))
            {
                if (pawn.Position.x > 100 && pawn.Position.x < fixtureRight && pawn.Position.z > 100 && pawn.Position.z < fixtureTop
                    && entered.Add(pawn))
                { if (result.firstEntryTick < 0) result.firstEntryTick = tick - started; result.lastProgressTick = tick - started; }
                if (pawn.Position.DistanceToSquared(owner.Position) <= 36 && arrived.Add(pawn))
                { if (result.firstObjectiveTick < 0) result.firstObjectiveTick = tick - started; result.lastProgressTick = tick - started; }
            }
            result.entered = entered.Count; result.objectiveReached = arrived.Count;
            result.moved = raiders.Count(pawn => pawn.Spawned && pawn.Position != starts[pawn]);
            result.alive = raiders.Count(pawn => pawn.Spawned && !pawn.Dead && !pawn.Downed);
        }
        private void VerifyIsolation()
        {
            result.legacyComponents = map.components.Cast<object>().Concat(Current.Game.components)
                .Select(value => value.GetType()).Where(TacticalEngineSelection.IsLegacy).Select(type => type.FullName).OrderBy(name => name).ToArray();
            result.installedLegacyHooks = Harmony.GetAllPatchedMethods().SelectMany(method =>
            {
                Patches patches = Harmony.GetPatchInfo(method);
                return patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Concat(patches.Finalizers)
                    .Where(patch => TacticalEngineSelection.IsLegacy(patch.PatchMethod.DeclaringType))
                    .Select(patch => method.DeclaringType.FullName + "." + method.Name + " <- " + patch.PatchMethod.DeclaringType.FullName);
            }).OrderBy(name => name).ToArray();
            bool legacy = TacticalEngineSelection.Kind == TacticalEngineKind.Legacy;
            result.newComponents = map.components.Cast<object>().Concat(Current.Game.components)
                .Where(value => value.GetType().IsDefined(typeof(NewTacticalAttribute), false))
                .Select(value => value.GetType().FullName).OrderBy(name => name).ToArray();
            result.installedNewHooks = Harmony.GetAllPatchedMethods().SelectMany(method =>
            {
                Patches patches = Harmony.GetPatchInfo(method);
                return patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Concat(patches.Finalizers)
                    .Where(patch => patch.PatchMethod.DeclaringType.IsDefined(typeof(NewTacticalAttribute), false))
                    .Select(patch => method.DeclaringType.FullName + "." + method.Name + " <- " + patch.PatchMethod.DeclaringType.FullName);
            }).OrderBy(name => name).ToArray();
            result.isolationVerified = legacy ? result.legacyComponents.Length == 11 && result.installedLegacyHooks.Length > 0
                : result.legacyComponents.Length == 0 && result.installedLegacyHooks.Length == 0;
            result.isolationVerified &= result.newComponents.Length == (TacticalEngineSelection.Kind == TacticalEngineKind.New ? 2 : 0);
            // R3 adds narrow presentation/weapon hooks; these must still be
            // absent in Vanilla/Legacy, with every concrete New patch present.
            int expectedHooks = typeof(TacticalEngineSelection).Assembly.GetTypes().Count(type => type.IsDefined(typeof(NewTacticalAttribute), false)
                && type.IsDefined(typeof(HarmonyPatch), false));
            result.isolationVerified &= result.installedNewHooks.Length == (TacticalEngineSelection.Kind == TacticalEngineKind.New ? expectedHooks : 0);
            if (!result.isolationVerified) throw new InvalidOperationException("Tactical engine isolation failed.");
        }
        private void Write()
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(TacticalEngineAuditResult)).WriteObject(stream, result);
                File.WriteAllText(output, Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
    }
}
