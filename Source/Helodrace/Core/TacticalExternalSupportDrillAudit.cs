using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Helodrace.Tactics;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public bool r7MortarRequestBound, r7CasRequestBound, r7SupportHeld, r7SupportLongStrikeHeld;
        [DataMember] public bool r7SupportNoAdvance = true, r7SupportPlanPreserved = true, r7GuidanceJobPreserved = true;
        [DataMember] public bool r7SupportMissionResumed, r7SupportMissionComplete, r7ReloadSupportPreserved = true;
        [DataMember] public string[] r7ExternalSupportEvents;
    }
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool ExternalSupportFixture => result.fixtureCase == "r7-support";
        private TacticalSquadCommand externalCommand;
        private HelodForwardBase externalBase;
        private int externalStep, externalRequestedAt;
        private IntVec3 externalOpening = IntVec3.Invalid;
        private int externalGuidanceJob = -1;
        private List<string> externalEvents = new List<string>();
        private string externalSnapshot;
        private TacticalSquadCommand ExternalCommand => externalCommand ?? (externalCommand =
            map.GetComponent<MapComponent_TacticalCommands>().Commands.Single());
        private void ExternalEvent(string message)
        {
            externalEvents.Add((GenTicks.TicksGame - started) + ":" + message);
            result.r7ExternalSupportEvents = externalEvents.ToArray();
            Log.Message("R7 external support " + externalEvents.Last());
        }
        private void ApplyExternalSupportDrill()
        {
            if (TacticalEngineSelection.Kind != TacticalEngineKind.New || result.units != 1)
                throw new InvalidOperationException("Support drill requires one actual new-engine squad.");
            TacticalSquadCommand command = ExternalCommand;
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            Pawn caller = command.Members[0].Pawn;
            int tick = GenTicks.TicksGame;
            if (externalStep == 0)
            {
                if (command.Plan == null || command.Phase != TacticalCommandPhase.Stack) return;
                externalOpening = command.Plan.Opening;
                externalBase = (HelodForwardBase)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("HD_ForwardBase"));
                externalBase.Tile = map.Tile; externalBase.SetFaction(caller.Faction); Find.WorldObjects.Add(externalBase);
                map.GetComponent<MapComponent_HelodMortarSupport>().QueueStrike(new IntVec3(155, 0, 160),
                    new IntVec3(155, 0, 160), ThingDefOf.Shell_HighExplosive, externalBase, caller,
                    volleyCount: 2, shellsPerVolley: 1, volleyIntervalTicks: 1600, scatterRadius: 1);
                result.r7MortarRequestBound = command.SupportCaller == caller && command.SupportAim == new IntVec3(155, 0, 160);
                externalRequestedAt = tick; externalStep = 1; result.caseTriggered = true;
                ExternalEvent("actual mortar QueueStrike interrupted stack approach; two real volleys requested");
            }
            if (externalStep == 1 || externalStep == 3)
            {
                result.r7SupportNoAdvance &= service.FieldBounds == 0;
                result.r7SupportPlanPreserved &= command.Plan?.Opening == externalOpening;
                if (command.FieldResponse?.Assigned == command.Members.Count
                    && command.Members.Any(m => m.Job?.def.defName == "HD_NewTacticalContactGuard" && m.Pawn.CurJob == m.Job))
                    result.r7SupportHeld = true;
                if (externalStep == 1 && tick - externalRequestedAt > TacticalSupportPolicy.MinimumHold
                    && map.GetComponent<MapComponent_HelodMortarSupport>().HasActiveStrike(caller)
                    && command.SupportCaller == caller && command.FieldResponse != null)
                    result.r7SupportLongStrikeHeld = true;
                if (externalStep == 3 && map.GetComponent<MapComponent_HelodCasSupport>().RequiresStationaryGuidance(caller))
                    result.r7GuidanceJobPreserved &= caller.CurJobDef?.defName == "HD_CASStationaryGuidance"
                        && caller.CurJob.loadID == externalGuidanceJob;
                if (command.SupportCaller != null || command.FieldResponse != null) return;
                ExternalEvent(externalStep == 1 ? "mortar completed; held beyond minimum and original mission resumed"
                    : "CAS completed; actual guidance Job retained and original mission resumed");
                externalStep++;
            }
            if (externalStep == 2)
            {
                var plan = new HelodCasAttackPlan(new IntVec3(0, 0, 150), new IntVec3(160, 0, 150),
                    HelodCasGuidanceMode.TalkOn, map, 1, 1);
                map.GetComponent<MapComponent_HelodCasSupport>().QueueStrike(plan, caller, externalBase, null, 1);
                result.r7CasRequestBound = command.SupportCaller == caller && command.SupportAim == plan.CurrentAimCell(map);
                externalGuidanceJob = caller.CurJob?.loadID ?? -1;
                externalRequestedAt = tick; externalStep = 3;
                ExternalEvent("actual CAS QueueStrike requested; native stationary guidance Job=" + externalGuidanceJob);
            }
            if (externalStep == 4)
            {
                result.r7SupportMissionResumed = command.SupportCaller == null && command.FieldResponse == null;
                if (!result.r7SupportMissionComplete && command.Phase == TacticalCommandPhase.Complete
                    && command.Members.All(m => m.EntryAssignmentDone))
                { result.r7SupportMissionComplete = true; ExternalEvent("all actual members completed original CQB mission after both support waits"); }
            }
        }
        private void ExposeExternalSupportDrill()
        {
            Scribe_References.Look(ref externalBase, "externalDrillBase");
            Scribe_Values.Look(ref externalStep, "externalDrillStep");
            Scribe_Values.Look(ref externalRequestedAt, "externalDrillRequestedAt");
            Scribe_Values.Look(ref externalOpening, "externalDrillOpening", IntVec3.Invalid);
            Scribe_Values.Look(ref externalGuidanceJob, "externalDrillGuidanceJob", -1);
            Scribe_Values.Look(ref externalSnapshot, "externalDrillSnapshot");
            Scribe_Collections.Look(ref externalEvents, "externalDrillEvents", LookMode.Value);
        }
        private string ExternalState()
        {
            TacticalSquadCommand command = ExternalCommand;
            Pawn caller = command.SupportCaller;
            return Digest(command.SupportCaller?.thingIDNumber + ":" + command.SupportAim + ":" + command.SupportUntil
                + ":" + command.SupportCheckAt + ":" + command.SupportStrikeActive + ":" + command.Phase + ":" + command.Plan?.Opening
                + ":" + NativeJobs(command) + ":" + FieldState(command) + ":" + History(command)
                + ":guidance=" + caller?.CurJob?.loadID + ":" + caller?.jobs.curDriver?.CurToilIndex
                + ":mortar=" + map.GetComponent<MapComponent_HelodMortarSupport>().HasActiveStrike(caller)
                + ":cas=" + map.GetComponent<MapComponent_HelodCasSupport>().HasActiveStrike(caller));
        }
        private void ApplyExternalSupportReload()
        {
            if (reloadStep >= RequiredReloads || externalStep != (reloadStep == 0 ? 1 : 3)) return;
            TacticalSquadCommand command = ExternalCommand;
            if (command.FieldResponse?.Assigned != command.Members.Count
                || !command.Members.Any(m => m.Job?.def.defName == "HD_NewTacticalContactGuard" && m.Pawn.CurJob == m.Job)) return;
            externalSnapshot = ExternalState(); reloadPending = true; reloadStep++;
            ExternalEvent("saving actual support checkpoint " + reloadStep);
            string save = "R7TacticalCheckpoint" + reloadStep;
            GameDataSaveLoader.SaveGame(save); GameDataSaveLoader.LoadGame(save);
        }
        private void VerifyExternalSupportReload()
        {
            result.r7ReloadSupportPreserved &= ExternalState() == externalSnapshot;
            result.r7ReloadJobsBound &= ExternalCommand.Members.Where(m => m.SavedJobId >= 0)
                .All(m => m.Job == m.Pawn.CurJob && m.Job?.loadID == m.SavedJobId);
            result.r7Reloads++; reloadPending = false;
            ExternalEvent("native support load " + reloadStep + " exact state=" + result.r7ReloadSupportPreserved);
            if (!result.r7ReloadSupportPreserved || !result.r7ReloadJobsBound)
                throw new InvalidOperationException("Native support reload changed request/actual strike/guard/guidance execution state.");
        }
    }
}
