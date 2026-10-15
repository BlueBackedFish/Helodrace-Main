using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Helodrace.Profiling;
using Helodrace.Tactics;
using Helodrace.Squads;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public string[] savedMapDefenders, savedMapLoadout, savedMapNativeRoomVetoes;
        [DataMember] public string savedMapDownProbe;
        [DataMember] public string savedMapRetreatProbe;
        [DataMember] public int savedMapEvacuated;
        [DataMember] public bool savedMapPatientEvacuated;
    }
    // Explicit CLI only. Load a COPY through native autostart, retain its
    // structures/defenders, and spawn one ordinary complete raid organization.
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private readonly bool savedMapAudit;
        private readonly List<string> savedMapTrace = new List<string>();
        private readonly Dictionary<string,string> savedMapStates = new Dictionary<string,string>();
        private Pawn retreatProbePatient;
        private void UpdateSavedMapAudit()
        {
            if (map != Find.CurrentMap) return;
            try
            {
                if (!initialized)
                {
                    InitializeSavedMapAudit(); initialized = true; Write();
                    AgentMethodProfiler.Begin("saved-map-cqb",102,raiders.Count,3,seconds:1800);
                }
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                int tick = GenTicks.TicksGame;
                if(tick-started>=120 && result.savedMapRetreatProbe==null
                    && GenCommandLine.TryGetCommandLineArg("hdCqbSavedRetreatProbe",out _))
                {
                    var patients=raiders.Where(p=>!p.Dead && !p.Downed && !OrganizationMedical.HasDuty(p)).Take(2).ToArray();
                    retreatProbePatient=patients[0];
                    Lord lord=retreatProbePatient.GetLord();
                    foreach(var patient in patients) patient.health.AddHediff(HediffDefOf.Anesthetic);
                    var exit=lord.Graph.lordToils.FirstOrDefault(t=>TacticalCasualtyPolicy.RetreatPhase(t));
                    if(exit==null) { exit=new LordToil_ExitMapAndDefendSelf {lord=lord}; lord.Graph.AddToil(exit); }
                    lord.GotoToil(exit);
                    result.savedMapRetreatProbe="downed="+string.Join(",",patients.Select(p=>p.ThingID))+" exit="+exit.GetType().Name;
                }
                if (tick >= nextProgress)
                {
                    nextProgress = tick + 10; TraceSavedMapAudit(tick);
                }
                if(tick-started>=900 && result.savedMapDownProbe==null
                    && GenCommandLine.TryGetCommandLineArg("hdCqbSavedDownProbe",out _))
                {
                    Pawn raider=raiders.First(p=>!p.Dead && !p.Downed);
                    Pawn shooter=raiders.First(p=>p!=raider && p.equipment?.Primary!=null);
                    var probes=new List<string>();
                    foreach(Pawn victim in new[]{raider}.Concat(map.mapPawns.FreeColonists.ToArray()))
                    {
                        ProtectedRaiders.Remove(victim);
                        try
                        {
                            victim.health.AddHediff(HediffDefOf.Anesthetic);
                            int before=victim.health.hediffSet.hediffs.Count;
                            var damage=victim.TakeDamage(new DamageInfo(DamageDefOf.Bullet,5,2,instigator:shooter,
                                hitPart:victim.RaceProps.body.corePart,weapon:shooter.equipment.Primary.def));
                            probes.Add(victim.ThingID+": downed="+victim.Downed+" damage="+damage.totalDamageDealt
                                +" injuriesAdded="+(victim.health.hediffSet.hediffs.Count-before));
                        }
                        catch(Exception e) { probes.Add(victim.ThingID+": "+e); Log.Error("Saved-map down probe: "+e); }
                        finally { ProtectedRaiders.Add(victim); }
                    }
                    result.savedMapDownProbe=string.Join("\n",probes);
                }
                if (tick - started < result.sampleTicks) return;
                AgentMethodProfiler.End(); if(AgentMethodProfiler.Capturing) return;
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
            if(GenCommandLine.TryGetCommandLineArg("hdCqbSavedGoalX",out _))
                map.GetComponent<MapComponent_TacticalCommands>().SetObjective(new IntVec3(Argument("hdCqbSavedGoalX",145),0,Argument("hdCqbSavedGoalZ",132)),Faction.OfPlayer);
            DebugSettings.enableStoryteller = DebugSettings.enableRandomDiseases = DebugSettings.enableRandomMentalStates = false;
            DebugViewSettings.neverForceNormalSpeed = true;
            bool high=GenCommandLine.TryGetCommandLineArg("hdCqbSavedHigh",out _);
            Faction faction = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed(high?"HD_HelodCivilHighFaction":"HD_HelodCivilLowFaction"));
            faction.RelationWith(Faction.OfPlayer).kind = FactionRelationKind.Hostile;
            Faction.OfPlayer.RelationWith(faction).kind = FactionRelationKind.Hostile;
            var defenders = map.mapPawns.FreeColonists.ToArray();
            if (defenders.Length == 0) throw new InvalidOperationException("The copied save has no player defenders.");
            result.savedMapDefenders = defenders.Select(p => p.ThingID + "@" + p.Position + " weapon=" + p.equipment?.Primary?.def.defName
                + " ranged=" + TacticalThreatEligibility.Ranged(p) + " job=" + p.CurJobDef?.defName).ToArray();
            ProtectedRaiders.Clear(); foreach (Pawn pawn in defenders) ProtectedRaiders.Add(pawn);
            FormationDef formation = DefDatabase<FormationDef>.GetNamed(high?"HD_Formation_MW_RifleSquad":"HD_Formation_GW_RifleSquad");
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
                + " medic="+OrganizationMedical.HasDuty(p)+" training="+string.Join(",",p.health.hediffSet.hediffs
                    .Where(h=>h.def.defName.Contains("Training")).Select(h=>h.def.defName))
                + " pouches="+string.Join(",",Helodrace.Tactical.MedicalPouchUtility.WornPouches(p)
                    .Select(b=>b.Capacity+":"+b.Hemostatic))
                + " nvg="+p.apparel.WornApparel.SelectMany(a=>a.TryGetComp<Helodrace.ModernWar.CompModularArmor>()?.InstalledParts
                    ?? Array.Empty<Helodrace.ModernWar.InstalledModularArmorPart>()).Count(part=>part.slot?.defName=="HD_MH_Slot_NightVision")
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
            result.savedMapEvacuated=raiders.Count(p=>!p.Spawned && !p.Dead);
            result.savedMapPatientEvacuated=retreatProbePatient!=null && !retreatProbePatient.Spawned && !retreatProbePatient.Dead;
            result.newPassedOpening = service.Commands.Sum(c => c.Members.Count(m => m.Passed));
            result.newEnteredByOrder = service.Commands.Sum(c => c.Members.Count(m => m.Entered));
            result.newCommands = service.ExecutionCommands.Select(SavedMapState).ToArray();
            foreach (var command in service.Commands.SelectMany(c=>new[]{c}.Concat(c.CqbTeams)))
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
                + " wait=" + command.CqbWait + " version=" + command.PlanVersion + " due="+command.Due
                + " combatUntil="+command.EntryCombatUntil+" secured="+command.GoalSecured
                + " frontier="+command.FrontierCursor+"/"+command.Frontiers.Count+" search="+command.CqbSearchCursor
                + " pursuit="+command.Contacts.Pursuit.RoomReady+":"+command.Contacts.Pursuit.Version
                + " identify="+command.Link.IdentifyUntil+" restoring="+command.ContactRestoring
                + " medical="+(command.MedicalCare!=null)+":"+(command.BagRecovery!=null)
                + " ready=" + command.Members.Count(m => m.Job != null && m.Pawn.CurJob == m.Job
                    && m.Pawn.jobs.curDriver is TacticalJobDriver driver && driver.AtPost && m.Pawn.Position == driver.PostCell)
                + "/" + command.Members.Count + " passed=" + command.Members.Count(m => m.Passed) + " entered=" + command.Members.Count(m => m.Entered)
                + " jobs=" + string.Join(",",command.Members.GroupBy(m => m.Pawn.CurJobDef?.defName ?? "none").Select(g => g.Key + "=" + g.Count()))
                + " actors=" + string.Join(";",command.Members.Select(m=>m.Pawn.ThingID+"@"+m.Pawn.Position
                    + "/"+m.CqbOrder?.Owner+":"+m.CqbOrder?.Version+":"+m.CqbOrder?.Revision
                    + ":dest="+m.CqbOrder?.Destination+":cross="+m.Crossed+":lane="+m.LaneCleared+":entryOwner="+m.EntryOwner));
        }
    }
}
