using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Helodrace.Profiling;
using Helodrace.Tactics;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public int r7Reloads;
        [DataMember] public bool r7ReloadJobsBound = true, r7ReloadHistoryPreserved = true, r7ReloadOpeningPreserved = true;
        [DataMember] public bool r7ReloadLiveGrenadePreserved, r7ReloadContactsPreserved;
        [DataMember] public bool r7ReloadResponsePreserved = true;
        [DataMember] public bool r7ReloadChargePreserved = true;
        [DataMember] public string[] r7ChargeEvents;
        [DataMember] public bool r7ReloadCarePreserved = true, r7ReloadFieldPreserved = true, r7ReloadToilStatePreserved = true;
    }
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool ReloadFixture => result.fixtureCase == "r7-save-load" || result.fixtureCase == "r7-charge-load"
            || GenCommandLine.TryGetCommandLineArg("hdTacticalAuditReload", out _) && (FieldFixture || MedicalFixture || CooperationFixture || DefenseFixture);
        private int RequiredReloads => FieldFixture || MedicalFixture || CooperationFixture || DefenseFixture ? 2 : result.fixtureCase == "r7-charge-load" ? 3 : 4;
        private int reloadStep;
        private bool reloadPending;
        private string reloadHistory, reloadContacts, reloadResponse, reloadCharge, reloadCare, reloadField, reloadJobs;
        private string savedMedicalCommand, savedFieldCommand;
        private IntVec3 reloadOpening;
        private bool reloadHadLiveGrenade;
        private int nextChargeTrace;
        private System.Collections.Generic.List<Pawn> savedStartPawns;
        private System.Collections.Generic.List<IntVec3> savedStartPositions;
        public override void ExposeData()
        {
            if (output == null) return;
            string state = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                using (var stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(TacticalEngineAuditResult)).WriteObject(stream, result);
                    state = Encoding.UTF8.GetString(stream.ToArray());
                }
            }
            Scribe_Values.Look(ref state, "engineAuditResult");
            if (Scribe.mode == LoadSaveMode.LoadingVars && state != null)
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(state)))
                    result = (TacticalEngineAuditResult)new DataContractJsonSerializer(typeof(TacticalEngineAuditResult)).ReadObject(stream);
            Scribe_Values.Look(ref initialized, "engineAuditInitialized");
            Scribe_Values.Look(ref started, "engineAuditStarted");
            Scribe_Values.Look(ref nextProgress, "engineAuditNextProgress");
            Scribe_Values.Look(ref fixtureRight, "engineAuditRight"); Scribe_Values.Look(ref fixtureTop, "engineAuditTop");
            Scribe_References.Look(ref owner, "engineAuditOwner");
            Scribe_Collections.Look(ref raiders, "engineAuditRaiders", LookMode.Reference);
            Scribe_Collections.Look(ref starts, "engineAuditStarts", LookMode.Reference, LookMode.Value,
                ref savedStartPawns, ref savedStartPositions);
            Scribe_Collections.Look(ref entered, "engineAuditEntered", LookMode.Reference);
            Scribe_Collections.Look(ref arrived, "engineAuditArrived", LookMode.Reference);
            Scribe_Collections.Look(ref unexpected, "engineAuditUnexpected", LookMode.Value);
            Scribe_Values.Look(ref reloadStep, "reloadStep"); Scribe_Values.Look(ref reloadPending, "reloadPending");
            Scribe_Values.Look(ref reloadHistory, "reloadHistory"); Scribe_Values.Look(ref reloadContacts, "reloadContacts");
            Scribe_Values.Look(ref reloadResponse, "reloadResponse");
            Scribe_Values.Look(ref reloadCharge, "reloadCharge");
            Scribe_Values.Look(ref reloadCare, "reloadCare"); Scribe_Values.Look(ref reloadField, "reloadField");
            Scribe_Values.Look(ref reloadJobs, "reloadJobs");
            Scribe_Values.Look(ref reloadOpening, "reloadOpening"); Scribe_Values.Look(ref reloadHadLiveGrenade, "reloadHadLiveGrenade");
            ExposeReloadDrillState();
            ExposeCommunicationReloadState();
            ExposeDefenseReloadState();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) measured = -1;
        }
        public override void FinalizeInit()
        {
            if (output == null || !initialized || !ReloadFixture) return;
            ProtectedRaiders.Clear(); foreach (Pawn pawn in raiders) ProtectedRaiders.Add(pawn);
            MapComponent_RaidMovementRuntimeAudit.ProtectedOwner = owner;
            map.GetComponent<MapComponent_TacticalCommands>()?.RestoreSavedCommands();
            medicalCommand = map.GetComponent<MapComponent_TacticalCommands>()?.SavedCommand(savedMedicalCommand);
            fieldCommand = map.GetComponent<MapComponent_TacticalCommands>()?.SavedCommand(savedFieldCommand);
            fieldPlan = fieldCommand?.Plan;
            benchmark = new ProfileBenchmark { fixtureVersion = result.fixtureVersion, seed = result.seed,
                mapFingerprint = result.mapFingerprint, pawnFingerprint = result.pawnFingerprint,
                faction = raiders[0].Faction.def.defName, requestedPopulation = result.requestedPopulation,
                unitCount = result.units, radioOperators = result.radioOperators, warmupTicks = result.warmupTicks,
                sampleTicks = result.sampleTicks, engine = result.engine, effectiveEngine = result.effectiveEngine,
                newEngineImplemented = result.newEngineImplemented, workload = result.workload, fixtureCase = result.fixtureCase };
            try { VerifyReload(); }
            catch (Exception exception)
            {
                result.error = exception.ToString(); Write(); finished = true;
                Log.Error("R7 reload verification failed: " + exception); UnityEngine.Application.Quit();
            }
        }
        private static string Digest(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }
        private static string History(TacticalSquadCommand command) => Digest(string.Join(";", command.SecuredCells
            .OrderBy(c => c.x).ThenBy(c => c.z)) + "|" + string.Join(";", command.SecuredPlans.Select(p => p.Opening)));
        private static string Contacts(TacticalSquadCommand command) => string.Join(";", command.Contacts.Memory.Entries
            .Select(c => c.EnemyId + ":" + c.Position + ":" + c.SeenTick + ":" + c.PreviousPosition + ":" + c.PreviousTick));
        private void ExposeReloadDrillState()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            { savedMedicalCommand = medicalCommand?.Id; savedFieldCommand = fieldCommand?.Id; }
            Scribe_Values.Look(ref savedMedicalCommand, "drillMedicalCommand"); Scribe_Values.Look(ref savedFieldCommand, "drillFieldCommand");
            Scribe_Values.Look(ref medicalStep, "drillMedicalStep"); Scribe_Values.Look(ref medicalStarted, "drillMedicalStarted");
            Scribe_References.Look(ref medicalPatient, "drillMedicalPatient");
            Scribe_Collections.Look(ref medicalWounds, "drillMedicalWounds", LookMode.Reference);
            Scribe_Collections.Look(ref medicalEvents, "drillMedicalEvents", LookMode.Value);
            Scribe_Values.Look(ref medicalGuards, "drillMedicalGuards", true);
            Scribe_Values.Look(ref medicalObserved, "drillMedicalObserved");
            Scribe_Values.Look(ref medicalLoggedCompletions, "drillMedicalLoggedCompletions");
            Scribe_Values.Look(ref medicalThreatExposed, "drillMedicalThreatExposed");
            Scribe_Values.Look(ref fieldStep, "drillFieldStep"); Scribe_Values.Look(ref fieldStepAt, "drillFieldStepAt");
            Scribe_Values.Look(ref fieldSeen, "drillFieldSeen");
            Scribe_Collections.Look(ref fieldEvents, "drillFieldEvents", LookMode.Value);
            Scribe_Values.Look(ref fieldPostsUnique, "drillFieldPostsUnique", true);
            Scribe_Values.Look(ref fieldSingleTeam, "drillFieldSingleTeam", true);
            Scribe_Values.Look(ref fieldFrozen, "drillFieldFrozen", true);
            Scribe_Values.Look(ref fieldBoundBaseline, "drillFieldBoundBaseline");
            Scribe_Values.Look(ref fieldSmokeTargets, "drillFieldSmokeTargets", true);
            Scribe_Values.Look(ref fieldSmokeLogged, "drillFieldSmokeLogged");
        }
        private static string NativeJobs(TacticalSquadCommand command) => string.Join(";", command.Members.Where(m => m.Job != null)
            .Select(m => m.Pawn.thingIDNumber + ":" + m.Job.loadID + ":" + m.Pawn.jobs.curDriver?.CurToilIndex + ":"
                + AccessTools.Field(typeof(JobDriver), "ticksLeftThisToil").GetValue(m.Pawn.jobs.curDriver)));
        private static string CareState(TacticalSquadCommand command)
        {
            TacticalMedicalCare care = command.MedicalCare;
            return care == null ? "none" : care.Patient?.Pawn.thingIDNumber + ":" + care.Helper?.Pawn.thingIDNumber
                + ":" + care.Job?.loadID + ":" + care.Started + ":" + care.Plasma + ":" + care.Finished + ":" + care.Successful;
        }
        private static string FieldState(TacticalSquadCommand command)
        {
            TacticalFieldResponse field = command.FieldResponse;
            if (field == null) return "none";
            TacticalFieldSmoke smoke = field.Screen;
            return field.Stage + ":" + field.Anchor + ":" + field.Focus + ":" + field.MovingTeam
                + ":" + field.Assigned + ":" + string.Join(";", field.Posts) + ":" + string.Join(";", field.FireGroup)
                + ":" + smoke?.Target + ":" + smoke?.Thrower?.thingIDNumber + ":" + smoke?.Job?.loadID
                + ":" + smoke?.Launched + ":" + smoke?.Returned
                + ":" + (smoke?.Projectile?.Spawned == true ? smoke.Projectile.thingIDNumber : -1);
        }
        private static string Response(TacticalSquadCommand command)
        {
            TacticalContactResponse response = command.ContactResponse;
            return response == null ? "none" : response.Started + ":" + response.LastSeen + ":" + response.Assigned
                + ":" + response.First + ":" + response.Second + ":" + string.Join(";", response.Posts);
        }
        private static string ChargeState(TacticalSquadCommand command)
        {
            TacticalChargeAction action = command.ChargeAction;
            if (action == null) return "none";
            CompInstalledBreachCharge charge = action.Charge;
            bool live = charge?.parent.Spawned == true;
            var fragments = action.SavedFragments.Concat(charge?.Fragments ?? Enumerable.Empty<Projectile>())
                .Where(p => p?.Spawned == true).Select(p => p.thingIDNumber).Distinct().OrderBy(id => id);
            return action.Detonated + ":" + action.EffectsCleared + ":" + action.SettledAt + ":" + live
                + ":" + (live ? charge.parent.thingIDNumber : -1) + ":" + (charge?.OperatorPawn ?? action.EffectOperator)?.thingIDNumber
                + ":" + (live ? AccessTools.Field(typeof(CompInstalledBreachCharge), "ticksToDetonation").GetValue(charge) : -1)
                + ":" + string.Join(";", fragments)
                + ":target=" + (live ? charge?.TargetWall?.Spawned + "/" + charge?.TargetWall?.Destroyed : "none")
                + ":edifice=" + command.Plan.Opening.GetEdifice(command.Owner.map)?.def.defName
                + ":walkable=" + command.Plan.Opening.Walkable(command.Owner.map);
        }
        private void VerifyReload()
        {
            if (!reloadPending) return;
            if (CooperationFixture) { VerifyCommunicationReload(); return; }
            if (DefenseFixture) { VerifyDefenseReload(); return; }
            TacticalSquadCommand command = map.GetComponent<MapComponent_TacticalCommands>().Commands.Single();
            result.r7ReloadJobsBound &= command.Members.Where(m => m.SavedJobId >= 0)
                .All(m => m.Job != null && m.Job == m.Pawn.CurJob && m.Job.loadID == m.SavedJobId);
            result.r7ReloadHistoryPreserved &= History(command) == reloadHistory;
            result.r7ReloadOpeningPreserved &= command.Plan?.Opening == reloadOpening;
            if (reloadHadLiveGrenade)
                result.r7ReloadLiveGrenadePreserved = command.OpeningAction?.Launched == true
                    && !command.OpeningAction.EffectsCleared && command.OpeningAction.Projectile?.Spawned == true;
            result.r7ReloadContactsPreserved = Contacts(command) == reloadContacts;
            result.r7ReloadResponsePreserved &= Response(command) == reloadResponse;
            result.r7ReloadChargePreserved &= ChargeState(command) == reloadCharge;
            result.r7ReloadCarePreserved &= CareState(command) == reloadCare;
            result.r7ReloadFieldPreserved &= FieldState(command) == reloadField;
            result.r7ReloadToilStatePreserved &= NativeJobs(command) == reloadJobs;
            if (!result.r7ReloadChargePreserved) Log.Error("R7 charge before=" + reloadCharge + " after=" + ChargeState(command));
            result.r7Reloads++; reloadPending = false;
            Log.Message("R7 reload " + result.r7Reloads + " jobs=" + result.r7ReloadJobsBound
                + " secured=" + result.r7ReloadHistoryPreserved + " opening=" + result.r7ReloadOpeningPreserved
                + " live=" + result.r7ReloadLiveGrenadePreserved + " contacts=" + result.r7ReloadContactsPreserved);
            if (!result.r7ReloadJobsBound || !result.r7ReloadHistoryPreserved || !result.r7ReloadOpeningPreserved
                || reloadHadLiveGrenade && !result.r7ReloadLiveGrenadePreserved || !result.r7ReloadContactsPreserved
                || !result.r7ReloadResponsePreserved || !result.r7ReloadChargePreserved || !result.r7ReloadCarePreserved
                || !result.r7ReloadFieldPreserved || !result.r7ReloadToilStatePreserved)
                throw new InvalidOperationException("R7 checkpoint did not restore the actual command/Job/effect state.");
        }
        private void ApplyReloadDrill()
        {
            if (reloadPending) return;
            if (CooperationFixture) { ApplyCommunicationReload(); return; }
            if (DefenseFixture) { ApplyDefenseReload(); return; }
            if (reloadStep >= RequiredReloads)
            {
                // A protected visible opponent intentionally keeps contact guards
                // engaged. Withdraw it physically after restoring that engagement
                // to test stale memory and subsequent mission resumption.
                if (!FieldFixture && !MedicalFixture) owner.Position = new IntVec3(180, 0, 180);
                return;
            }
            TacticalSquadCommand command = map.GetComponent<MapComponent_TacticalCommands>()?.Commands.FirstOrDefault();
            if (command == null) return;
            if (result.fixtureCase == "r7-charge-load" && GenTicks.TicksGame >= nextChargeTrace)
            {
                nextChargeTrace = GenTicks.TicksGame + 120;
                TacticalChargeAction action = command.ChargeAction;
                string reason = "no charge";
                bool canTrigger = action?.Charge?.CanTrigger(out reason) == true;
                string trace = GenTicks.TicksGame + ":" + command.Phase + " step=" + reloadStep
                    + " charge=" + action?.Charge?.parent.Spawned + " trigger=" + canTrigger + "/" + reason
                    + " deadline=" + action?.Deadline + " withdrawing=" + action?.Withdrawing
                    + " worker=" + action?.Installer?.CurJob?.def.defName + " positions="
                    + string.Join(";", command.Members.Select(m => m.Pawn.Position + ":" + m.Job?.def.defName
                        + ":" + m.Pawn.CurJob?.loadID + "/" + m.Job?.loadID + ":held="
                        + (m.Pawn.jobs.curDriver as TacticalJobDriver)?.AtPost));
                var events = result.r7ChargeEvents ?? new string[0];
                if (events.Length < 40) result.r7ChargeEvents = events.Concat(new[] { trace }).ToArray();
                Log.Message("R7 charge trace " + trace);
            }
            bool checkpoint = reloadStep == 0 ? command.OpeningAction?.Launched == true
                && command.OpeningAction.Projectile?.Spawned == true
                : reloadStep == 1 ? command.SecuredPlans.Count >= 1 && command.Phase == TacticalCommandPhase.Stack
                : reloadStep == 2 ? command.OpeningAction?.Enemy.IsValid == true && command.Phase == TacticalCommandPhase.Support
                : command.ContactResponse != null && command.ContactResponse.Assigned == command.Members.Count;
            if (result.fixtureCase == "r7-charge-load")
            {
                TacticalChargeAction action = command.ChargeAction;
                checkpoint = reloadStep == 0 ? action?.Charge?.parent.Spawned == true && !action.Detonated
                    : reloadStep == 1 ? action?.Charge?.Triggered == true && action.Charge.parent.Spawned
                    : action?.Detonated == true && action.Charge?.parent.Destroyed == true && !action.EffectsCleared;
            }
            else if (MedicalFixture)
            {
                TacticalMedicalCare care = command.MedicalCare;
                checkpoint = care?.Job != null && !care.Finished && care.Helper.Pawn.CurJob == care.Job
                    && GenTicks.TicksGame - care.Started > (reloadStep == 0 ? 1200 : 60)
                    && care.Plasma == (reloadStep == 1);
            }
            else if (FieldFixture)
            {
                TacticalFieldResponse field = command.FieldResponse;
                checkpoint = reloadStep == 0 ? field?.Screen?.Launched == true && field.Screen.Projectile?.Spawned == true
                    : field?.Stage == TacticalFieldStage.Moving;
            }
            if (!checkpoint) return;
            result.caseTriggered = true; reloadOpening = command.Plan.Opening;
            reloadHistory = History(command); reloadContacts = Contacts(command);
            reloadResponse = Response(command);
            reloadCharge = ChargeState(command);
            reloadCare = CareState(command); reloadField = FieldState(command); reloadJobs = NativeJobs(command);
            reloadHadLiveGrenade = command.OpeningAction?.Launched == true && command.OpeningAction.Projectile?.Spawned == true;
            reloadStep++; reloadPending = true;
            Log.Message("R7 saving checkpoint " + reloadStep + " phase=" + command.Phase + " secured=" + command.SecuredCells.Count);
            string save = "R7TacticalCheckpoint" + reloadStep;
            // Functional-only fixture. CPU comparison is collected separately;
            // native save/load/JIT cost is not relabelled as steady tick cost.
            GameDataSaveLoader.SaveGame(save); GameDataSaveLoader.LoadGame(save);
        }
    }
}
