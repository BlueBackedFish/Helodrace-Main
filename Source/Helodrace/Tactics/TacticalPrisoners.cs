using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Tactical;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public static class TacticalPrisonerPolicy
    {
        public const int CriticalBleedingTicks = 6 * GenDate.TicksPerHour, ScanSlice = 2, ScanInterval = 120;
        public static bool NeedsAid(bool disabled, bool secured, int ticksToDeath) => disabled && secured
            && ticksToDeath >= 0 && ticksToDeath <= CriticalBleedingTicks;
        public static bool CarePhase(TacticalCommandPhase phase) => phase == TacticalCommandPhase.Clear || phase == TacticalCommandPhase.Complete;
    }
    public sealed partial class TacticalSquadCommand
    {
        public int PrisonerScanCursor, NextPrisonerCheck;
        public bool PrisonerWorkPending => PrisonerScanCursor > 0 || MedicalCare?.Captive == true
            || Members.Any(m => m.Job != null && m.Pawn?.CurJob == m.Job && m.Job.def.defName == TemporaryRestraint.JobName);
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private bool AdvancePrisoners(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            if (command.CqbParent != null || command.Defensive || command.Withdrawing) return false;
            if (command.MedicalCare?.Captive == true) return AdvanceMedicalCare(command, active, tick);
            if (!TacticalPrisonerPolicy.CarePhase(command.Phase) || command.FieldResponse != null
                || command.MedicalCare != null || command.ChargeAction != null
                || command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared
                || command.CqbTeams.Any(t => CqbLiveAction(t)
                    || !t.Terminal && !TacticalPrisonerPolicy.CarePhase(t.Phase))) return false;
            if (command.Members.Any(m => m.Pawn?.CurJobDef?.defName == TemporaryRestraint.JobName && m.Pawn.CurJob == m.Job))
            { command.Due = Math.Min(command.Due, tick + 10); return true; }
            if (tick < command.NextPrisonerCheck) return false;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int inspected = 0; inspected < TacticalPrisonerPolicy.ScanSlice && command.PrisonerScanCursor < pawns.Count; inspected++)
            {
                var patient = pawns[command.PrisonerScanCursor++];
                if (patient.Dead || !patient.Downed || !patient.RaceProps.Humanlike || !patient.Position.Roofed(map)
                    || !SharedSecured.Cleared(patient.Position) || active.Count == 0 || !patient.HostileTo(active[0].Pawn)
                    || KnownCareDanger(patient) || !MedicalSafe(command, patient.Position, tick)) continue;
                if (!TemporaryRestraint.Bound(patient))
                {
                    foreach (var helper in active.OrderBy(m => m.Medic ? 0 : TcccUtility.HasTraining(m.Pawn) ? 1 : 2)
                        .ThenBy(m => m.Pawn.Position.DistanceToSquared(patient.Position)))
                    {
                        if (!TemporaryRestraint.CanHandle(helper.Pawn, patient, false)
                            || !MedicalSafe(command, helper.Pawn.Position, tick) || !helper.Pawn.CanReserve(patient)
                            || helper.Pawn.CurJob?.playerForced == true || tick < helper.RetryTick || !CanIssue(helper)) continue;
                        var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TemporaryRestraint.JobName), patient);
                        if (Issue(helper, job)) { command.PrisonerScanCursor--; command.Due = tick + 10; return true; }
                    }
                    continue;
                }
                if (TacticalPrisonerPolicy.NeedsAid(true, true, HealthUtility.TicksUntilDeathDueToBloodLoss(patient))
                    && NeedsHemostasis(patient, null, tick)
                    && TryBeginMedicalCare(command, active, new TacticalMemberCommand { Pawn = patient }, tick, captive: true))
                { command.PrisonerScanCursor--; return true; }
            }
            bool pending = command.PrisonerScanCursor < pawns.Count;
            command.NextPrisonerCheck = tick + (pending ? 10 : TacticalPrisonerPolicy.ScanInterval);
            if (!pending) command.PrisonerScanCursor = 0;
            command.Due = Math.Min(command.Due, command.NextPrisonerCheck);
            return pending && command.Phase == TacticalCommandPhase.Complete;
        }
    }
}
