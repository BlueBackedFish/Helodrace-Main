using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Helodrace.Profiling;
using Helodrace.Tactics;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public int r7Reloads;
        [DataMember] public bool r7ReloadJobsBound = true, r7ReloadHistoryPreserved = true, r7ReloadOpeningPreserved = true;
        [DataMember] public bool r7ReloadLiveGrenadePreserved, r7ReloadContactsPreserved;
        [DataMember] public bool r7ReloadResponsePreserved = true;
    }
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool ReloadFixture => result.fixtureCase == "r7-save-load";
        private int reloadStep;
        private bool reloadPending;
        private string reloadHistory, reloadContacts, reloadResponse;
        private IntVec3 reloadOpening;
        private bool reloadHadLiveGrenade;
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
            Scribe_Values.Look(ref reloadOpening, "reloadOpening"); Scribe_Values.Look(ref reloadHadLiveGrenade, "reloadHadLiveGrenade");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) measured = -1;
        }
        public override void FinalizeInit()
        {
            if (output == null || !initialized || !ReloadFixture) return;
            ProtectedRaiders.Clear(); foreach (Pawn pawn in raiders) ProtectedRaiders.Add(pawn);
            MapComponent_RaidMovementRuntimeAudit.ProtectedOwner = owner;
            map.GetComponent<MapComponent_TacticalCommands>()?.RestoreSavedCommands();
            benchmark = new ProfileBenchmark { fixtureVersion = result.fixtureVersion, seed = result.seed,
                mapFingerprint = result.mapFingerprint, pawnFingerprint = result.pawnFingerprint,
                faction = raiders[0].Faction.def.defName, requestedPopulation = result.requestedPopulation,
                unitCount = result.units, radioOperators = result.radioOperators, warmupTicks = result.warmupTicks,
                sampleTicks = result.sampleTicks, engine = result.engine, effectiveEngine = result.effectiveEngine,
                newEngineImplemented = result.newEngineImplemented, workload = result.workload, fixtureCase = result.fixtureCase };
            VerifyReload();
        }
        private static string Digest(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }
        private static string History(TacticalSquadCommand command) => Digest(string.Join(";", command.SecuredCells
            .OrderBy(c => c.x).ThenBy(c => c.z)) + "|" + string.Join(";", command.SecuredPlans.Select(p => p.Opening)));
        private static string Contacts(TacticalSquadCommand command) => string.Join(";", command.Contacts.Memory.Entries
            .Select(c => c.EnemyId + ":" + c.Position + ":" + c.SeenTick + ":" + c.PreviousPosition + ":" + c.PreviousTick));
        private static string Response(TacticalSquadCommand command)
        {
            TacticalContactResponse response = command.ContactResponse;
            return response == null ? "none" : response.Started + ":" + response.LastSeen + ":" + response.Assigned
                + ":" + response.First + ":" + response.Second + ":" + string.Join(";", response.Posts);
        }
        private void VerifyReload()
        {
            if (!reloadPending) return;
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
            result.r7Reloads++; reloadPending = false;
            Log.Message("R7 reload " + result.r7Reloads + " jobs=" + result.r7ReloadJobsBound
                + " secured=" + result.r7ReloadHistoryPreserved + " opening=" + result.r7ReloadOpeningPreserved
                + " live=" + result.r7ReloadLiveGrenadePreserved + " contacts=" + result.r7ReloadContactsPreserved);
            if (!result.r7ReloadJobsBound || !result.r7ReloadHistoryPreserved || !result.r7ReloadOpeningPreserved
                || reloadHadLiveGrenade && !result.r7ReloadLiveGrenadePreserved || !result.r7ReloadContactsPreserved
                || !result.r7ReloadResponsePreserved)
                throw new InvalidOperationException("R7 checkpoint did not restore the actual command/Job/effect state.");
        }
        private void ApplyReloadDrill()
        {
            if (reloadPending) return;
            if (reloadStep >= 4)
            {
                // A protected visible opponent intentionally keeps contact guards
                // engaged. Withdraw it physically after restoring that engagement
                // to test stale memory and subsequent mission resumption.
                owner.Position = new IntVec3(180, 0, 180); return;
            }
            TacticalSquadCommand command = map.GetComponent<MapComponent_TacticalCommands>()?.Commands.FirstOrDefault();
            if (command == null) return;
            bool checkpoint = reloadStep == 0 ? command.OpeningAction?.Launched == true
                && command.OpeningAction.Projectile?.Spawned == true
                : reloadStep == 1 ? command.SecuredPlans.Count >= 1 && command.Phase == TacticalCommandPhase.Stack
                : reloadStep == 2 ? command.OpeningAction?.Enemy.IsValid == true && command.Phase == TacticalCommandPhase.Support
                : command.ContactResponse != null && command.ContactResponse.Assigned == command.Members.Count;
            if (!checkpoint) return;
            result.caseTriggered = true; reloadOpening = command.Plan.Opening;
            reloadHistory = History(command); reloadContacts = Contacts(command);
            reloadResponse = Response(command);
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
