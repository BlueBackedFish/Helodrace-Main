using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Helodrace.Tactical;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using HarmonyLib;

namespace Helodrace.Tactics
{
    public static class TacticalMedicalPolicy
    {
        public const int ScanMembers = 2, ScanInterval = 600, RetryInterval = 1800, MaximumCareTicks = 2400;
        public static bool Urgent(float bloodLoss) => bloodLoss >= .4f;
        public static bool SafePhase(TacticalCommandPhase phase, bool scanDone, TacticalFieldStage? fieldStage) =>
            fieldStage == TacticalFieldStage.Defending || phase == TacticalCommandPhase.Clear && scanDone && fieldStage == null;
    }
    public sealed partial class TacticalMedicalCare
    {
        public TacticalMemberCommand Patient, Helper;
        public Job Job;
        public int Started;
        public bool Finished, Successful, Plasma, CancelRequested;
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long MedicalMemberChecks, MedicalTreatments, MedicalCompleted, MedicalAborted, MedicalPlasma, MedicalRejoins;
        internal bool OwnsMedicalJob(Pawn pawn, Job job) => byPawn.TryGetValue(pawn, out TacticalSquadCommand command)
            && command.MedicalCare?.Helper.Pawn == pawn && command.MedicalCare.Job == job;
        public void MedicalJobFinished(Pawn pawn, Job job, JobCondition condition)
        {
            if (!OwnsMedicalJob(pawn, job)) return;
            TacticalMedicalCare care = byPawn[pawn].MedicalCare;
            care.Finished = true; care.Successful = condition == JobCondition.Succeeded;
            JobFinished(pawn, job, condition);
        }
        private bool AdvanceMedicalCare(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalMedicalCare care = command.MedicalCare;
            if (care != null)
            {
                command.Due = Math.Min(command.Due, tick + 15);
                if (care.CancelRequested || !care.Patient.Pawn.Spawned || care.Patient.Pawn.Dead || !Available(care.Helper, map)
                    || !MedicalSafe(command, care.Patient.Pawn.Position, tick)
                    || KnownCareDanger(care.Patient.Pawn) || KnownCareDanger(care.Helper.Pawn)
                    || tick - care.Started >= TacticalMedicalPolicy.MaximumCareTicks
                    || !care.Finished && care.Helper.Pawn.CurJob != care.Job)
                {
                    // Admit the replacement before cancelling treatment, so a
                    // whole vanilla think-tree search is not inserted here.
                    if (care.Helper.Pawn.CurJob == care.Job && Available(care.Helper, map))
                    {
                        if (!CanIssue(care.Helper)) return true;
                        IntVec3 focus = command.FieldResponse?.Focus ?? command.Plan?.Opening ?? command.Goal;
                        Issue(care.Helper, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"),
                            care.Helper.Pawn.Position, focus));
                    }
                    EndMedicalCare(command, care, tick, false); return false;
                }
                if (!care.Finished)
                {
                    GuardDuringCare(command, active, care, tick); return true;
                }
                EndMedicalCare(command, care, tick, care.Successful); return false;
            }
            bool field = command.FieldResponse != null;
            bool safePhase = TacticalMedicalPolicy.SafePhase(command.Phase, command.RoomScan == null, command.FieldResponse?.Stage);
            if (!safePhase || !field && tick >= command.MedicalWindowUntil || tick < command.NextMedicalCheck
                || command.ContactResponse != null || command.ChargeAction != null
                || command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared) return false;
            if (field && active.Exists(m => !AtPost(m))) return false;
            if (!MedicalSafe(command, active[0].Pawn.Position, tick)) return false;
            // Two health-cache reads per advance, not a map/organization sweep.
            for (int checkedMembers = 0; checkedMembers < TacticalMedicalPolicy.ScanMembers
                && command.MedicalCursor < command.Members.Count; checkedMembers++)
            {
                TacticalMemberCommand candidate = command.Members[command.MedicalCursor++];
                Pawn patient = candidate.Pawn; MedicalMemberChecks++;
                if (!patient.Spawned || patient.Map != map || patient.Dead || patient.InMentalState
                    || tick - candidate.LastCareAt < TacticalMedicalPolicy.RetryInterval || KnownCareDanger(patient)) continue;
                float blood = patient.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0;
                float bleed = patient.health.hediffSet.BleedRateTotal;
                if (bleed <= 0 && !TacticalMedicalPolicy.Urgent(blood)) continue;
                float score = bleed + blood;
                if (score <= command.MedicalCandidateScore || !MedicalSafe(command, patient.Position, tick)) continue;
                command.MedicalCandidate = candidate; command.MedicalCandidateScore = score;
            }
            if (command.MedicalCursor < command.Members.Count)
            { command.Due = Math.Min(command.Due, tick + 15); return true; }
            TacticalMemberCommand selected = command.MedicalCandidate;
            command.MedicalCandidate = null; command.MedicalCandidateScore = 0; command.MedicalCursor = 0;
            command.NextMedicalCheck = tick + TacticalMedicalPolicy.ScanInterval;
            if (selected == null) return false;
            if (!MedicalSafe(command, selected.Pawn.Position, tick)) return false;
            if (!TryBeginMedicalCare(command, active, selected, tick))
            {
                if (command.DeferredWork || command.NextMedicalCheck <= tick + 15)
                {
                    command.MedicalCandidate = selected; command.MedicalCandidateScore = 1;
                    command.MedicalCursor = command.Members.Count; command.NextMedicalCheck = tick + 15;
                    command.Due = Math.Min(command.Due, tick + 15); return true;
                }
                // Untreatable vital-only bleeding or absent qualification/stock
                // must not monopolize every selection ahead of other patients.
                selected.LastCareAt = tick; return false;
            }
            return true;
        }

        private bool MedicalSafe(TacticalSquadCommand command, IntVec3 patient, int tick)
        {
            if (command.FieldResponse == null && !command.SecuredCells.Contains(patient)) return false;
            if (command.ChargeAction != null || command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared
                || command.FieldResponse?.Screen != null || command.ContactResponse != null) return false;
            foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                if (TacticalContactMemory.Fresh(contact, tick) && GenSight.LineOfSight(patient, contact.Position, map, true)
                    && (patient.DistanceToSquared(contact.Position) <= 784
                        || command.FieldResponse != null && CoverUtility.CalculateOverallBlockChance(patient, contact.Position, map) < .1f)) return false;
            return true;
        }
        private void GuardDuringCare(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalMedicalCare care, int tick)
        {
            IntVec3 focus = command.FieldResponse?.Focus ?? command.Plan?.Opening ?? command.Goal;
            foreach (TacticalMemberCommand member in active)
            {
                if (member == care.Helper || member == care.Patient || member.Pawn.CurJob?.playerForced == true) continue;
                if (member.Pawn.CurJob == member.Job && member.Job?.def.defName == "HD_NewTacticalContactGuard") continue;
                if (tick < member.RetryTick || !CanIssue(member)) continue;
                Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"), member.Pawn.Position, focus));
            }
        }
        private static bool NeedsHemostasis(Pawn patient, CompMedibag bag, int tick)
        {
            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
                if (hediff.Part != null && (bag == null || bag.AllowsHemostasisPart(hediff.Part.def))
                    && hediff.BleedRate > 0 && !PartHemostasis.HasDressing(patient.health.hediffSet, hediff.Part, tick)) return true;
            return false;
        }
        private static bool KnownCareDanger(Pawn pawn)
        {
            Thing known = pawn.mindState?.knownExploder;
            if (known?.Spawned != true || known.Destroyed || RaidSmokeUtility.IsScreeningProjectile(known.def)) return false;
            float radius = Math.Max(12, known.def.projectile?.explosionRadius ?? 0);
            var fragments = known.def.GetModExtension<Helodrace.ModernWar.FragmentationGrenadeExtension>();
            if (fragments != null) radius = Math.Max(radius, Math.Max(fragments.radius, fragments.longRangeRadius));
            return pawn.Position.DistanceToSquared(known.Position) <= radius * radius;
        }
        private bool TryBeginMedicalCare(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalMemberCommand patient, int tick)
        {
            TacticalMemberCommand helper = null; Job treatment = null; bool plasma = false;
            bool needsHemostasis = NeedsHemostasis(patient.Pawn, null, tick);
            if (needsHemostasis)
            {
                if (Available(patient, map) && TcccUtility.CanTreat(patient.Pawn, patient.Pawn)) helper = patient;
                else foreach (TacticalMemberCommand member in active)
                    if (member.Pawn.Position.DistanceToSquared(patient.Pawn.Position) <= 64
                        && TcccUtility.CanTreat(member.Pawn, patient.Pawn)) { helper = member; break; }
                if (helper != null)
                {
                    treatment = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_TCCC_Treat"), patient.Pawn);
                    treatment.count = (int)TcccTreatment.Hemostasis;
                }
            }
            if (helper == null)
            {
                bool needsPlasma = TacticalMedicalPolicy.Urgent(patient.Pawn.health.hediffSet
                    .GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0);
                foreach (TacticalMemberCommand member in active)
                {
                    if (member.Pawn.Position.DistanceToSquared(patient.Pawn.Position) > 64 || member.Pawn.CurJob?.playerForced == true
                        || !member.Pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) continue;
                    foreach (Apparel apparel in member.Pawn.apparel.WornApparel)
                    {
                        CompMedibag bag = apparel.TryGetComp<CompMedibag>();
                        if (bag == null || !bag.CanUseOn(member.Pawn, patient.Pawn)) continue;
                        if (!bag.HemostasisAvailable && !bag.PlasmaAvailable && member.Pawn.inventory != null)
                            foreach (Thing stock in member.Pawn.inventory.innerContainer.InnerListForReading.ToArray())
                                if (bag.TryLoadInventorySupply(stock)) break;
                        if (needsHemostasis && bag.HemostasisAvailable && NeedsHemostasis(patient.Pawn, bag, tick))
                            treatment = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_MedibagHemostasis"), patient.Pawn, apparel);
                        else if (needsPlasma && bag.PlasmaAvailable && !patient.Pawn.health.hediffSet.HasHediff(bag.Props.plasmaTransfusionHediff))
                        { treatment = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_MedibagPlasmaTransfusion"), patient.Pawn, apparel); plasma = true; }
                        if (treatment != null) { helper = member; break; }
                    }
                    if (helper != null) break;
                }
            }
            if (helper == null || helper.Pawn.CurJob?.playerForced == true || tick < helper.RetryTick
                || KnownCareDanger(helper.Pawn) || !CanIssue(helper)) return false;
            if (helper != patient && !GenSight.LineOfSight(helper.Pawn.Position, patient.Pawn.Position, map, true,
                c => c.Walkable(map) && !(c.GetEdifice(map) is Building_Door))) return false;
            if (helper != patient && Available(patient, map))
            {
                EnsurePost(patient, patient.Pawn.Position, command.Goal, tick);
                if (!AtPost(patient)) { command.NextMedicalCheck = tick + 15; return false; }
            }
            treatment.canUseRangedWeapon = false;
            var care = new TacticalMedicalCare { Patient = patient, Helper = helper, Job = treatment, Started = tick, Plasma = plasma };
            command.MedicalCare = care;
            if (!Issue(helper, treatment)) { command.MedicalCare = null; command.NextMedicalCheck = tick + 15; return false; }
            patient.MedicalRejoinPending |= patient.Pawn.Downed;
            MedicalTreatments++; if (plasma) MedicalPlasma++;
            command.Link.Cooperation.LocalReady = false; command.Due = tick + 15; return true;
        }
        private void EndMedicalCare(TacticalSquadCommand command, TacticalMedicalCare care, int tick, bool success)
        {
            if (success) MedicalCompleted++; else MedicalAborted++;
            bool followUp = success && !care.Plasma && TacticalMedicalPolicy.Urgent(care.Patient.Pawn.health.hediffSet
                .GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0);
            care.Patient.LastCareAt = followUp ? tick - TacticalMedicalPolicy.RetryInterval : tick;
            command.NextMedicalCheck = followUp ? tick + 1 : tick + TacticalMedicalPolicy.ScanInterval;
            command.PhaseStarted += tick - care.Started;
            care.Patient.Entered = care.Patient.EntryAssignmentDone = false;
            care.Patient.MedicalRejoinPending |= care.Patient.Pawn.Downed;
            care.Helper.Entered = care.Helper.EntryAssignmentDone = false;
            command.MedicalCare = null; command.Due = tick + 1;
            command.ContactRestoring = command.Phase == TacticalCommandPhase.Clear && command.Plan != null;
        }
        private void RejoinTreatedMembers(TacticalSquadCommand command, int tick)
        {
            int joined = 0;
            foreach (TacticalMemberCommand member in command.Members)
            {
                if (!member.MedicalRejoinPending) continue;
                Pawn pawn = member.Pawn;
                if (!pawn.Spawned || pawn.Map != map || pawn.Dead) { member.MedicalRejoinPending = false; continue; }
                if (pawn.Downed || pawn.InMentalState) continue;
                Lord current = pawn.GetLord();
                if (current != null) { member.MedicalRejoinPending = false; continue; }
                if (!GameComponent_TacticalCommands.IsAssaultLord(command.RaidLord) || pawn.Faction != command.RaidLord.faction)
                { member.MedicalRejoinPending = false; continue; }
                if (joined >= 1 || !Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.TryJob(tick))
                { command.DeferredWork = true; continue; }
                // Incapacitation removes an assault pawn from its native Lord.
                // A treated survivor must regain that same mission, rather than
                // defaulting to the faction's solo exit-map job after recovery.
                long started = Stopwatch.GetTimestamp();
                try
                {
                    command.RaidLord.AddPawn(pawn); member.MedicalRejoinPending = false; joined++; MedicalRejoins++;
                    if (command.Phase == TacticalCommandPhase.Complete && pawn.CurJob?.playerForced != true)
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                finally { Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.Account(tick, Stopwatch.GetTimestamp() - started); }
                member.RetryTick = 0; member.Entered = member.EntryAssignmentDone = false;
            }
        }
    }

    // The same existing TCCC/bag drivers serve player orders and owned AI care.
    // Toil sequence stays constant, even when execution is rebuilt after load.
    public static class TacticalCareBridge
    {
        public static bool Owned(Pawn pawn, Job job) => TacticalEngineSelection.Kind == TacticalEngineKind.New
            && pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.OwnsMedicalJob(pawn, job) == true;
        public static void Finished(Pawn pawn, Job job, JobCondition condition)
        {
            if (TacticalEngineSelection.Kind == TacticalEngineKind.New)
                pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.MedicalJobFinished(pawn, job, condition);
        }
        public static Toil Movement(Pawn pawn, Job job, Action ready)
        {
            bool owned = false;
            Toil gate = ToilMaker.MakeToil("TacticalCareMovementBudget"); gate.defaultCompleteMode = ToilCompleteMode.Never;
            gate.initAction = () => { owned = Owned(pawn, job); if (owned) pawn.pather.StopDead(); };
            gate.tickAction = () =>
            {
                if (!owned || Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.TryPath(GenTicks.TicksGame)) ready();
            };
            return gate;
        }
    }
    [NewTactical, HarmonyPatch(typeof(Pawn_HealthTracker), "MakeUndowned")]
    public static class Patch_NewTactical_MedicalRecoveryWake
    {
        public static void Postfix(Pawn ___pawn) => ___pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.Wake(___pawn);
    }
}
