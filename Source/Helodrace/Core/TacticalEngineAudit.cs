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
        [DataMember] public bool complete, isolationVerified, newEngineImplemented;
        [DataMember] public string engine, effectiveEngine, workload, seed, mapFingerprint, pawnFingerprint, error;
        [DataMember] public string[] legacyComponents, installedLegacyHooks, finalPawnJobs;
    }

    // A new fixture independent of legacy plan/order getters. All engines receive
    // the same actual organizations, equipment, Lord and world geometry.
    public sealed class MapComponent_TacticalEngineAudit : MapComponent
    {
        private readonly string output;
        private readonly List<Pawn> raiders = new List<Pawn>();
        private readonly Dictionary<Pawn, IntVec3> starts = new Dictionary<Pawn, IntVec3>();
        private readonly HashSet<Pawn> entered = new HashSet<Pawn>(), arrived = new HashSet<Pawn>();
        private Pawn owner;
        private int started, measured = -1, nextProgress;
        private bool initialized, finishing, finished;
        private ProfileBenchmark benchmark;
        private readonly TacticalEngineAuditResult result = new TacticalEngineAuditResult();
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
                if (tick >= nextProgress) { Progress(tick); nextProgress = tick + 30; }
                if (measured < 0 && tick - started >= result.warmupTicks)
                {
                    measured = tick;
                    benchmark.startPhases = Phases();
                    AgentMethodProfiler.Begin("engine-audit", result.workload == "open-approach" ? 100 : 101,
                        raiders.Count, 3, seconds: 300, benchmark: benchmark);
                }
                if (measured < 0 || tick - measured < result.sampleTicks) return;
                Progress(tick);
                benchmark.endPhases = Phases();
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
            if (result.workload != "open-approach" && result.workload != "sapper-wall") throw new ArgumentException("Unknown workload.");
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
            MapComponent_RaidMovementRuntimeAudit.ProtectedOwner = owner;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList()) if (pawn != owner) pawn.Destroy(DestroyMode.Vanish);
            foreach (IntVec3 cell in map.AllCells)
            {
                foreach (Thing thing in cell.GetThingList(map).ToList()) if (!(thing is Pawn))
                { if (thing.def.destroyable) thing.Destroy(DestroyMode.Vanish); else thing.DeSpawn(); }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.roofGrid.SetRoof(cell, null);
            }
            void Place(ThingDef def, IntVec3 position)
            {
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
                thing.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(thing, position, map, WipeMode.Vanish);
            }
            for (int x = 100; x <= 140; x++)
            { Place(ThingDefOf.Wall, new IntVec3(x, 0, 100)); Place(ThingDefOf.Wall, new IntVec3(x, 0, 136)); }
            for (int z = 101; z < 136; z++)
            {
                if (result.workload != "open-approach" || z != 118) Place(ThingDefOf.Wall, new IntVec3(100, 0, z));
                Place(ThingDefOf.Wall, new IntVec3(140, 0, z));
            }
            for (int x = 101; x < 140; x++) for (int z = 101; z < 136; z++)
                map.roofGrid.SetRoof(new IntVec3(x, 0, z), RoofDefOf.RoofConstructed);
            IntVec3 goal = new IntVec3(120, 0, 118);
            Place(ThingDefOf.Bed, goal);
            ((Building_Bed)goal.GetEdifice(map)).CompAssignableToPawn.TryAssignPawn(owner);
            owner.Position = goal; owner.drafter.Drafted = true; owner.equipment.DestroyAllEquipment();
            Job wait = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture); wait.expiryInterval = 1000000;
            owner.jobs.StartJob(wait, JobCondition.InterruptForced);
            result.mapFingerprint = RaidAuditSeed.Fingerprint(map);
            FormationDef formation = DefDatabase<FormationDef>.GetNamed(high ? "HD_Formation_MW_RifleSquad" : "HD_Formation_GW_RifleSquad");
            while (raiders.Count < result.requestedPopulation)
            {
                List<Pawn> members = OrganizationGenerator.Generate(new PawnGroupMakerParms { faction = faction,
                    groupKind = PawnGroupKindDefOf.Combat, raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                    points = formation.FormationCost, tile = map.Tile, seed = 347001 + result.units }, out CombatOrganization organization);
                if (members.Count != OrganizationGenerator.KindsFor(formation).Count() || organization.rootGroups.Count != 1
                    || organization.rootGroups[0].formation != formation) throw new InvalidOperationException("Fixture requires a complete rifle squad.");
                result.units++;
                foreach (Pawn pawn in members)
                {
                    int index = raiders.Count;
                    IntVec3 cell = new IntVec3(66 + index % 16, 0, 90 + index / 16);
                    GenSpawn.Spawn(pawn, cell, map); raiders.Add(pawn); starts[pawn] = cell;
                }
            }
            result.population = raiders.Count;
            result.radioOperators = raiders.Count(pawn => RaidTacticalRadioUtility.Radios(pawn).Any());
            if (result.workload == "sapper-wall" && raiders.Count > 0)
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
            result.pawnFingerprint = PawnFingerprint();
            if (raiders.Count > 0) LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false,
                canTimeoutOrFlee: false, sappers: result.workload == "sapper-wall", canSteal: false), map, raiders);
            benchmark = new ProfileBenchmark { fixtureVersion = 5, seed = result.seed, mapFingerprint = result.mapFingerprint,
                faction = faction.def.defName, requestedPopulation = result.requestedPopulation, unitCount = result.units,
                radioOperators = result.radioOperators, warmupTicks = result.warmupTicks, sampleTicks = result.sampleTicks,
                engine = result.engine, effectiveEngine = result.effectiveEngine, newEngineImplemented = result.newEngineImplemented,
                workload = result.workload, pawnFingerprint = result.pawnFingerprint };
            VerifyIsolation(); started = GenTicks.TicksGame;
        }
        private string PawnFingerprint()
        {
            var text = new StringBuilder();
            foreach (Pawn pawn in raiders)
            {
                text.Append(pawn.kindDef.defName).Append('|').Append(pawn.kindDef.canBeSapper).Append('|')
                    .Append(pawn.ageTracker.AgeBiologicalTicks).Append('|');
                foreach (var skill in pawn.skills.skills) text.Append(skill.def.defName).Append(':').Append(skill.Level).Append(';');
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
            result.finalPawnJobs = raiders.Select(pawn => pawn.kindDef.defName + "@" + pawn.Position + ":"
                + pawn.CurJob?.def?.defName + "->" + pawn.CurJob?.targetA.ToString()).ToArray();
            result.breachedWallCells = Enumerable.Range(101,35).Count(z => new IntVec3(100,0,z).GetEdifice(map) == null
                && (result.workload != "open-approach" || z != 118));
        }
        private string Phases() => TacticalEngineSelection.Kind != TacticalEngineKind.Legacy ? result.effectiveEngine
            : string.Join(",", map.GetComponent<MapComponent_RaidTacticalPlans>().Plans
                .GroupBy(plan => map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(plan.UnitId)?.Phase.ToString() ?? "NoState")
                .OrderBy(group => group.Key).Select(group => group.Key + ":" + group.Count()));
        private void Progress(int tick)
        {
            foreach (Pawn pawn in raiders.Where(pawn => pawn.Spawned && !pawn.Dead && !pawn.Downed))
            {
                if (pawn.Position.x > 100 && pawn.Position.x < 140 && pawn.Position.z > 100 && pawn.Position.z < 136
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
            result.isolationVerified = legacy ? result.legacyComponents.Length == 11 && result.installedLegacyHooks.Length > 0
                : result.legacyComponents.Length == 0 && result.installedLegacyHooks.Length == 0;
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
