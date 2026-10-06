using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Helodrace.Squads;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace
{
    // Exercises the player-facing generator and native incident, rather than
    // replacing either with the handcrafted movement benchmark's organization.
    internal sealed class RaidSpawnRuntimeAudit
    {
        private readonly Map map;
        private readonly Pawn owner;
        private readonly string output;
        private readonly Action prepareFixture;
        private readonly List<Pawn> raiders = new List<Pawn>();
        private readonly Dictionary<Pawn, IntVec3> starts = new Dictionary<Pawn, IntVec3>();
        private readonly HashSet<Pawn> moved = new HashSet<Pawn>();
        private readonly HashSet<Pawn> tacticalMoved = new HashSet<Pawn>();
        private readonly HashSet<string> startedUnits = new HashSet<string>();
        private readonly Dictionary<Pawn, IntVec3> lastPositions = new Dictionary<Pawn, IntVec3>();
        private int scenario = -1, started, nextSample;
        private readonly string[] cases = { "LOW organization command", "LOW native EdgeWalkIn",
            "LOW native EdgeWalkInGroups", "HIGH native EdgeWalkInGroups" };

        internal RaidSpawnRuntimeAudit(Map map, Pawn owner, string output, Action prepareFixture)
        { this.map = map; this.owner = owner; this.output = output; this.prepareFixture = prepareFixture; }

        internal bool Update()
        {
            if (scenario < 0) Begin();
            foreach (Pawn pawn in raiders)
                if (pawn.Spawned)
                {
                    if (pawn.Position != starts[pawn]) moved.Add(pawn);
                    if (pawn.Position != lastPositions[pawn] && MapComponent_RaidTacticalOrders.Owned(pawn.CurJob))
                        tacticalMoved.Add(pawn);
                    lastPositions[pawn] = pawn.Position;
                }
            var units = raiders.Select(RaidTacticalUnit.ForPawn).Where(unit => unit != null).Distinct().ToList();
            foreach (RaidTacticalUnit unit in units)
                if (map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(unit.Id)?.ActivePlan?.Success == true)
                    startedUnits.Add(unit.Id);
            int age = GenTicks.TicksGame - started;
            if (age >= nextSample)
            {
                Sample(age);
                nextSample += 300;
            }
            if (age < 3600) return false;
            bool passed = raiders.Count > 0 && moved.Count == raiders.Count
                && units.Count > 0 && units.All(unit => startedUnits.Contains(unit.Id)
                    && unit.Members.Any(pawn => tacticalMoved.Contains(pawn)))
                && raiders.All(pawn => pawn.Spawned && pawn.GetLord() != null)
                && (scenario != 3 || raiders.Count == 13);
            Write("\"case\":\"" + cases[scenario] + "\",\"passed\":" + (passed ? "true" : "false")
                + ",\"moved\":" + moved.Count + ",\"tacticalMoved\":" + tacticalMoved.Count
                + ",\"startedUnits\":" + startedUnits.Count + ",\"units\":" + units.Count + ",\"population\":" + raiders.Count);
            if (scenario + 1 == cases.Length) { Write("\"complete\":true"); return true; }
            Begin(); return false;
        }

        private void Begin()
        {
            foreach (Lord lord in raiders.Select(pawn => pawn.GetLord()).Where(value => value != null).Distinct().ToList())
                map.lordManager.RemoveLord(lord);
            foreach (Pawn pawn in raiders)
            { OrganizationAPI.Registry.Detach(pawn); if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish); }
            raiders.Clear(); starts.Clear(); moved.Clear(); tacticalMoved.Clear(); lastPositions.Clear(); startedUnits.Clear();
            prepareFixture();
            scenario++;
            Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named(
                scenario == 3 ? "HD_HelodCivilHighFaction" : "HD_HelodCivilLowFaction"));
            if (faction == null) throw new InvalidOperationException("Missing raid faction.");
            faction.SetRelation(new FactionRelation(Faction.OfPlayer, FactionRelationKind.Hostile) { baseGoodwill = -100 });
            faction.RelationWith(Faction.OfPlayer).baseGoodwill = -100;
            Faction.OfPlayer.RelationWith(faction).baseGoodwill = -100;
            var before = new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned);
            if (scenario == 0)
            {
                OrganizationDebug.SpawnExample();
                // The command shows its organization summary in a forced-pause
                // dialog. Close that audit-created result as a player would.
                Find.WindowStack.TryRemove(typeof(Dialog_MessageBox), false);
            }
            else
            {
                var parms = new IncidentParms { target = map, faction = faction, points = 1400f,
                    raidStrategy = DefDatabase<RaidStrategyDef>.GetNamed(
                        scenario == 3 ? "ImmediateAttack" : "ImmediateAttackSappers"),
                    raidArrivalMode = DefDatabase<PawnsArrivalModeDef>.GetNamed(
                        scenario == 1 ? "EdgeWalkIn" : "EdgeWalkInGroups"),
                    spawnCenter = new IntVec3(66, 0, 95), forced = true };
                if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
                    throw new InvalidOperationException("Actual raid incident failed: " + cases[scenario]);
            }
            raiders.AddRange(map.mapPawns.AllPawnsSpawned.Where(pawn => !before.Contains(pawn) && pawn != owner));
            if (raiders.Count == 0 || raiders.Count > 52 || raiders.Any(pawn => RaidTacticalUnit.ForPawn(pawn) == null))
                throw new InvalidOperationException("Unexpected actual raid population/organization: " + raiders.Count);
            foreach (Pawn pawn in raiders) { starts[pawn] = pawn.Position; lastPositions[pawn] = pawn.Position; }
            started = GenTicks.TicksGame; nextSample = 0;
            Log.Message("Raid spawn audit: " + cases[scenario] + " count=" + raiders.Count);
        }

        private void Sample(int age)
        {
            var plans = map.GetComponent<MapComponent_RaidTacticalPlans>();
            var execution = map.GetComponent<MapComponent_RaidTacticalExecution>();
            var units = raiders.Select(RaidTacticalUnit.ForPawn).Where(unit => unit != null).Distinct().ToList();
            string states = string.Join(";", units.Select(unit => unit.Id + ":"
                + (execution.StateFor(unit.Id)?.Phase.ToString() ?? "NoState") + ":"
                + (plans.Plans.FirstOrDefault(plan => plan.UnitId == unit.Id)?.Reason ?? "NoPlan")));
            string actors = string.Join(";", raiders.Take(20).Select(pawn =>
                $"{pawn.thingIDNumber}@{pawn.Position} lord={pawn.GetLord()?.LordJob?.GetType().Name}"
                + $" duty={pawn.mindState?.duty?.def?.defName} job={pawn.CurJobDef?.defName}"
                + $" giver={pawn.CurJob?.jobGiver?.GetType().Name} target={pawn.CurJob?.targetA}"
                + $" controlled={execution.ControlsPawn(pawn)} moving={pawn.pather?.Moving}"
                + $" command={MapComponent_RaidTacticalOrders.For(pawn)?.Kind}/{MapComponent_RaidTacticalOrders.For(pawn)?.Destination}"));
            Write("\"case\":\"" + cases[scenario] + "\",\"age\":" + age + ",\"population\":" + raiders.Count
                + ",\"moved\":" + moved.Count + ",\"tacticalMoved\":" + tacticalMoved.Count
                + ",\"cache\":\"" + Escape(map.GetComponent<MapComponent_TacticalMapAnalysis>().BuildStatus)
                + "\",\"states\":\"" + Escape(states) + "\",\"actors\":\"" + Escape(actors) + "\"");
        }

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n");
        private void Write(string fields) => File.AppendAllText(output, "{" + fields + "}\n");
    }
}
