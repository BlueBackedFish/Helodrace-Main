using System;
using System.Linq;
using System.Runtime.Serialization;
using Helodrace.Tactics;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public bool r7ReloadDefensePreserved = true;
    }
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private string reloadDefense;
        private void ExposeDefenseReloadState()
        {
            Scribe_Values.Look(ref reloadDefense, "reloadDefense");
            Scribe_Values.Look(ref defenseStep, "drillDefenseStep");
            Scribe_Values.Look(ref defenseHiddenAt, "drillDefenseHiddenAt");
            Scribe_Collections.Look(ref defenseEvents, "drillDefenseEvents", LookMode.Value);
        }
        private string DefenseState() => Digest(string.Join("|", ReloadSquads.Select(c => c.Id + ":" + c.Phase
            + ":" + c.Defensive + ":" + c.DefenseAnchor + ":" + c.DefenseRestoring + ":" + c.Goal
            + ":" + c.RaidLord.CurLordToil.GetType().FullName + ":" + NativeJobs(c) + ":" + FieldState(c)
            + ":" + Response(c) + ":" + CareState(c) + ":" + History(c) + ":" + Contacts(c)
            + ":" + string.Join(";", c.Members.Select(m => m.Pawn.thingIDNumber + ":" + m.Pawn.Position
                + ":" + m.Pawn.mindState.duty.def.defName + ":" + m.Pawn.mindState.duty.focus.Cell)))))
            + ":" + map.GetComponent<MapComponent_TacticalCommands>().ClaimCount
            + ":" + map.GetComponent<MapComponent_TacticalCommands>().LeaseCount;
        private void VerifyDefenseReload()
        {
            result.r7ReloadDefensePreserved &= DefenseState() == reloadDefense;
            result.r7ReloadJobsBound &= ReloadSquads.SelectMany(c => c.Members).Where(m => m.SavedJobId >= 0)
                .All(m => m.Job != null && m.Job == m.Pawn.CurJob && m.Job.loadID == m.SavedJobId);
            result.r7Reloads++; reloadPending = false;
            DefenseEvent("native load " + reloadStep + " exact defense/response/Job/duty state=" + result.r7ReloadDefensePreserved);
            if (!result.r7ReloadDefensePreserved || !result.r7ReloadJobsBound)
                throw new InvalidOperationException("Native fixed-defense reload changed anchor/phase/response/Job/duty state.");
        }
        private void ApplyDefenseReload()
        {
            if (reloadStep >= RequiredReloads) return;
            TacticalSquadCommand[] commands = ReloadSquads;
            if (commands.Length != 2) return;
            bool ready = reloadStep == 0 ? commands.All(c => c.FieldResponse != null
                && c.Members.Any(m => m.Job?.def.defName == "HD_NewTacticalContactGuard" && m.Pawn.CurJob == m.Job))
                : defenseStep >= 4 && commands.All(c => c.Phase == TacticalCommandPhase.Defending && c.FieldResponse == null
                    && c.ContactResponse == null && c.Members.All(m => m.Job == null));
            if (!ready) return;
            reloadDefense = DefenseState(); reloadPending = true; reloadStep++;
            DefenseEvent("saving native defense checkpoint " + reloadStep);
            string save = "R7TacticalCheckpoint" + reloadStep;
            GameDataSaveLoader.SaveGame(save); GameDataSaveLoader.LoadGame(save);
        }
    }
}
