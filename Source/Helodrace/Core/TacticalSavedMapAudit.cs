using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Helodrace.Tactics;
using Helodrace.Squads;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public string[] savedMapDefenders, savedMapLoadout, savedMapNativeRoomVetoes;
    }
    // Explicit CLI only. Load a COPY through native autostart, retain its
    // structures/defenders, and spawn one ordinary complete raid organization.
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private readonly bool savedMapAudit;
        private readonly List<string> savedMapTrace = new List<string>();
        private readonly Dictionary<string,string> savedMapStates = new Dictionary<string,string>();
        private void UpdateSavedMapAudit()
        {
            if (map != Find.CurrentMap) return;
            try
            {
                if (!initialized)
                {
                    InitializeSavedMapAudit(); initialized = true; Write();
                }
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                int tick = GenTicks.TicksGame;
                if (tick >= nextProgress)
                {
                    nextProgress = tick + 10; TraceSavedMapAudit(tick);
                }
                if (tick - started < result.sampleTicks) return;
                TraceSavedMapAudit(tick); result.complete = true; Write();
                finished = true; Application.Quit();
            }
            catch (Exception exception)
            {
                result.error = exception.ToString(); result.complete = true; Write();
                finished = true; Log.Error("Saved-map CQB audit: " + exception); Application.Quit();
            }
        }
        private void InitializeSavedMapAudit()
        {
            result.fixtureCase = "saved-map-cqb"; result.engine = "new";
            result.effectiveEngine = TacticalEngineSelection.EffectiveEngine;
            result.sampleTicks = Argument("hdTacticalAuditSample",6000);
            result.speed = 3;
            DebugSettings.enableStoryteller = DebugSettings.enableRandomDiseases = DebugSettings.enableRandomMentalStates = false;
            DebugViewSettings.neverForceNormalSpeed = true;
            Faction faction = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed("HD_HelodCivilLowFaction"));
            faction.RelationWith(Faction.OfPlayer).kind = FactionRelationKind.Hostile;
            Faction.OfPlayer.RelationWith(faction).kind = FactionRelationKind.Hostile;
            var defenders = map.mapPawns.FreeColonists.ToArray();
            if (defenders.Length == 0) throw new InvalidOperationException("The copied save has no player defenders.");
            result.savedMapDefenders = defenders.Select(p => p.ThingID + "@" + p.Position + " weapon=" + p.equipment?.Primary?.def.defName
                + " ranged=" + TacticalThreatEligibility.Ranged(p) + " job=" + p.CurJobDef?.defName).ToArray();
            ProtectedRaiders.Clear(); foreach (Pawn pawn in defenders) ProtectedRaiders.Add(pawn);
            FormationDef formation = DefDatabase<FormationDef>.GetNamed("HD_Formation_GW_RifleSquad");
            List<Pawn> members;
            GeneratingFixture = true;
            try
            {
                members = OrganizationGenerator.Generate(new PawnGroupMakerParms { faction = faction,
                    groupKind = PawnGroupKindDefOf.Combat, raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                    points = formation.FormationCost, tile = map.Tile, seed = 347001 }, out _);
            }
            finally { GeneratingFixture = false; }
            var used = new HashSet<IntVec3>();
            for (int i = 0; i < members.Count; i++)
            {
                IntVec3 seed = new IntVec3(Argument("hdCqbSavedSpawnX",90) + i % 4,0,Argument("hdCqbSavedSpawnZ",169) + i / 4);
                IntVec3 spot = GenRadial.RadialCellsAround(seed,8,true).First(c => c.InBounds(map)
                    && c.Standable(map) && c.GetFirstPawn(map) == null && !used.Contains(c));
                used.Add(spot); GenSpawn.Spawn(members[i],spot,map); raiders.Add(members[i]);
                ProtectedRaiders.Add(members[i]);
            }
            result.population = members.Count; result.units = 1;
            result.savedMapLoadout = members.Select(p => p.ThingID + "@" + p.Position + " canGrenade=" + InventoryGrenadeUtility.CanUseGrenades(p)
                + " inventory=" + string.Join(",",p.inventory.innerContainer.Select(t => t.def.defName + ":grenade=" + InventoryGrenadeUtility.IsInventoryGrenade(t.def)))).ToArray();
            LordMaker.MakeNewLord(faction,new LordJob_AssaultColony(faction,canKidnap:false,canTimeoutOrFlee:false,
                sappers:true,canSteal:false),map,members);
            started = GenTicks.TicksGame;
            Log.Message("Saved-map CQB audit loaded original geometry; population=" + members.Count);
        }
        private void TraceSavedMapAudit(int tick)
        {
            var service = map.GetComponent<MapComponent_TacticalCommands>();
            result.newObservations = service.Observations; result.newObservationContacts = service.ObservationContacts;
            result.newSupportThrows = service.SupportThrows; result.newSupportWaits = service.SupportWaits;
            result.newSupportReturns = service.SupportReturns; result.newUnsafeEntries = service.UnsafeEntries;
            result.newContactResponses = service.ContactResponses; result.newContactShots = service.ContactShots;
            result.newLastJobFailure = service.LastJobFailure; result.newJobsIssued = service.JobsIssued;
            result.newPassedOpening = service.Commands.Sum(c => c.Members.Count(m => m.Passed));
            result.newEnteredByOrder = service.Commands.Sum(c => c.Members.Count(m => m.Entered));
            result.newCommands = service.Commands.Select(SavedMapState).ToArray();
            foreach (var command in service.Commands)
            {
                if (result.savedMapNativeRoomVetoes == null && command.OpeningAction?.SupportTargets.Count > 0)
                {
                    IntVec3 target = command.OpeningAction.SupportTargets[0];
                    Room room = target.GetRoom(map);
                    Room RoomAt(IntVec3 cell) => cell.InBounds(map) ? cell.GetRoom(map) : null;
                    Pawn raider = command.Members[0].Pawn;
                    result.savedMapNativeRoomVetoes = map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.HostileTo(raider)
                        && TacticalGrenadePolicy.FriendlyInRoom(room,p.Position,p.Position.GetEdifice(map) is Building_Door,RoomAt,command.Plan))
                        .Take(32).Select(p => p.ThingID + "@" + p.Position + " roof=" + p.Position.Roofed(map)
                            + " target=" + target + " distance=" + p.Position.DistanceTo(target)).ToArray();
                }
                string state = SavedMapState(command);
                if (savedMapStates.TryGetValue(command.Id,out string previous) && state == previous) continue;
                savedMapStates[command.Id] = state;
                if (savedMapTrace.Count < 512) savedMapTrace.Add((tick-started) + ":" + state);
            }
            result.newRoomDiagnostics = savedMapTrace.ToArray();
            result.measuredTicks = tick-started;
            result.finalPawnJobs = raiders.Select(p => p.ThingID + "@" + p.Position + ":" + p.CurJobDef?.defName).ToArray();
        }
        private static string SavedMapState(TacticalSquadCommand command)
        {
            TacticalOpeningAction action = command.OpeningAction;
            return command.Id + ":" + command.Phase + " opening=" + command.Plan?.Opening + " goal=" + command.Goal
                + " direct=" + command.Plan?.Direct + " shared=" + command.Plan?.ReportedPassage
                + " failure=" + command.LastPlanFailure + " retry=" + command.PlanRetryAt + " next=" + command.Mission.NextGoal
                + " contact=" + (command.ContactResponse != null) + " focus=" + command.FocusFire?.Stage + " field=" + command.FieldResponse?.Stage
                + " action=" + (action != null) + " room=" + action?.RoomCells + " outdoors=" + action?.Outdoors
                + " observed=" + action?.Enemy + " done=" + action?.ObservationDone + " issued=" + action?.ThrowIssued + " launched=" + action?.Launched
                + " skip=" + action?.SupportSkip + " checks=" + action?.GrenadeChecks + "/" + action?.TrajectoryChecks + "/" + action?.UnsafeChecks
                + " roomSafety=" + action?.SupportRoomCells.Count + "/" + action?.SupportRoomReady + "/" + action?.SupportRoomTruncated
                + " cursor=" + action?.SupportCursor
                + " ready=" + command.Members.Count(m => m.Job != null && m.Pawn.CurJob == m.Job
                    && m.Pawn.jobs.curDriver is TacticalJobDriver driver && driver.AtPost && m.Pawn.Position == m.Job.targetA.Cell)
                + "/" + command.Members.Count + " passed=" + command.Members.Count(m => m.Passed) + " entered=" + command.Members.Count(m => m.Entered)
                + " jobs=" + string.Join(",",command.Members.GroupBy(m => m.Pawn.CurJobDef?.defName ?? "none").Select(g => g.Key + "=" + g.Count()));
        }
    }
}
