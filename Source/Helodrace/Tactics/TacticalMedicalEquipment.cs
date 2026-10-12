using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Helodrace.Squads;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed class TacticalBagRecovery : IExposable
    {
        public Pawn Helper;
        public Thing Bag;
        public Job Job;
        public int Started, SavedJobId = -1;
        public bool Recovered;
        public void RestoreJob(Job job) => Job = job?.loadID == SavedJobId
            && job.def.defName == TacticalMedicalEquipmentPolicy.RecoveryJob && job.targetA.Thing == Bag ? job : null;
        public void ExposeData()
        {
            Scribe_References.Look(ref Helper, "helper");
            Scribe_References.Look(ref Bag, "bag");
            Scribe_Values.Look(ref Started, "started");
            Scribe_Values.Look(ref Recovered, "recovered");
            if (Scribe.mode == LoadSaveMode.Saving) SavedJobId = Job?.loadID ?? -1;
            Scribe_Values.Look(ref SavedJobId, "jobId", -1);
        }
    }

    public static class TacticalMedicalEquipmentPolicy
    {
        public const string RecoveryJob = "HD_NewTacticalRecoverMedibag";
        public const int RetryTicks = 600, Range = 20, Timeout = 600;
        public static bool Dropped(bool known, bool spawned, bool destroyed, bool sameMap) => known && spawned && !destroyed && sameMap;
        public static bool Compatible(Pawn pawn, Apparel bag) => pawn.apparel != null
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
            && ApparelUtility.HasPartsToWear(pawn, bag.def) && bag.PawnCanWear(pawn, true)
            && (!CompBiocodable.IsBiocoded(bag) || CompBiocodable.IsBiocodedFor(bag, pawn))
            && pawn.apparel.WornApparel.All(item => item.TryGetComp<CompMedibag>() == null
                && ApparelUtility.CanWearTogether(item.def, bag.def, pawn.RaceProps.body));
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private static void RememberMedicalBags(TacticalSquadCommand command, Pawn pawn)
        {
            if (pawn.apparel == null) return;
            foreach (Apparel apparel in pawn.apparel.WornApparel)
                if (apparel.TryGetComp<CompMedibag>() != null && !command.MedicalBags.Contains(apparel)) command.MedicalBags.Add(apparel);
        }

        internal void MedicalBagUnequipped(Pawn pawn, Thing bag)
        {
            if (pawn != null && RootContext(pawn, out TacticalSquadCommand command) && command.MedicalBags.Contains(bag))
            { command.BagRecoveryAt = GenTicks.TicksGame + 1; Wake(pawn); }
        }

        internal bool OwnsBagRecovery(Pawn pawn, Job job) => RootContext(pawn, out TacticalSquadCommand command)
            && command.BagRecovery?.Helper == pawn && command.BagRecovery.Job == job;

        internal void MedicalBagRecovered(Pawn pawn, Job job)
        {
            if (!OwnsBagRecovery(pawn, job)) return;
            TacticalSquadCommand command = byPawn[pawn].CqbParent ?? byPawn[pawn];
            command.BagRecovery.Recovered = true;
            foreach (TacticalMemberCommand member in command.Members)
            {
                member.Medic = member.Pawn == pawn;
                var assignment = OrganizationAPI.GetGroup(member.Pawn)?.roleAssignments.Find(a => a.pawn == member.Pawn);
                if (assignment != null) assignment.medicalDuty = member.Medic;
            }
            OrganizationMedical.Assign(pawn); RememberMedicalBags(command, pawn);
            command.NextMedicalCheck = GenTicks.TicksGame + 1; Wake(pawn);
        }

        private bool AdvanceMedicalBagRecovery(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalBagRecovery recovery = command.BagRecovery;
            if (recovery != null)
            {
                command.Due = Math.Min(command.Due, tick + 30);
                TacticalMemberCommand helper = command.Members.Find(m => m.Pawn == recovery.Helper);
                bool working = helper != null && Available(helper, map) && helper.Pawn.CurJob == recovery.Job;
                bool safe = working && !KnownCareDanger(helper.Pawn) && MedicalSafe(command, helper.Pawn.Position, tick)
                    && (recovery.Recovered || recovery.Bag?.Spawned == true && MedicalSafe(command, recovery.Bag.Position, tick));
                if (!recovery.Recovered && safe && tick - recovery.Started < TacticalMedicalEquipmentPolicy.Timeout)
                {
                    GuardDuringBagRecovery(command, active, helper, tick); return true;
                }
                if (working && !recovery.Recovered)
                {
                    if (!CanIssue(helper)) return true;
                    Issue(helper, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"), helper.Pawn.Position,
                        command.FieldResponse?.Focus ?? command.Goal));
                }
                command.PhaseStarted += tick - recovery.Started;
                command.BagRecovery = null;
                command.ContactRestoring |= command.Phase == TacticalCommandPhase.Clear && command.Plan != null;
                command.BagRecoveryAt = tick + TacticalMedicalEquipmentPolicy.RetryTicks;
                command.Due = tick + 1;
                return false;
            }
            if (command.MedicalCare != null || tick < command.BagRecoveryAt
                || !TacticalMedicalPolicy.SafePhase(command.Phase, command.RoomScan == null, command.FieldResponse?.Stage)
                || command.ContactResponse != null || command.FocusFire != null || command.ChargeAction != null
                || command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared
                || command.Contacts.Melee.Dangerous(tick)) return false;
            if (command.FieldResponse != null && active.Exists(member => !AtPost(member))) return false;
            command.BagRecoveryAt = tick + TacticalMedicalEquipmentPolicy.RetryTicks;
            command.MedicalBags.RemoveAll(item => item == null || item.Destroyed);
            Apparel dropped = command.MedicalBags.Find(item => TacticalMedicalEquipmentPolicy.Dropped(true, item.Spawned, item.Destroyed, item.Map == map)
                && MedicalSafe(command, item.Position, tick)) as Apparel;
            if (dropped == null) return false;
            TacticalMemberCommand best = null; int distance = int.MaxValue;
            foreach (TacticalMemberCommand member in active)
            {
                int candidate = member.Pawn.Position.DistanceToSquared(dropped.Position);
                if (candidate > TacticalMedicalEquipmentPolicy.Range * TacticalMedicalEquipmentPolicy.Range || candidate >= distance
                    || tick < member.RetryTick || member.Pawn.CurJob?.playerForced == true || KnownCareDanger(member.Pawn)
                    || !TacticalMedicalEquipmentPolicy.Compatible(member.Pawn, dropped) || !MedicalSafe(command, member.Pawn.Position, tick)
                    || !GenSight.LineOfSight(member.Pawn.Position, dropped.Position, map, true,
                        cell => cell.Walkable(map) && !(cell.GetEdifice(map) is Building_Door))) continue;
                best = member; distance = candidate;
            }
            if (best == null) return false;
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryPlan(tick)) { command.BagRecoveryAt = tick + 30; return false; }
            long started = Stopwatch.GetTimestamp(); bool reachable;
            try { reachable = best.Pawn.CanReserveAndReach(dropped, PathEndMode.ClosestTouch, Danger.Deadly); }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            if (!reachable) return false;
            if (!CanIssue(best)) { command.BagRecoveryAt = tick + 30; return false; }
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TacticalMedicalEquipmentPolicy.RecoveryJob), dropped);
            command.BagRecovery = new TacticalBagRecovery { Helper = best.Pawn, Bag = dropped, Job = job, Started = tick };
            if (!Issue(best, job)) { command.BagRecovery = null; return false; }
            command.Link.Cooperation.LocalReady = false; command.Due = tick + 30;
            return true;
        }

        private void GuardDuringBagRecovery(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalMemberCommand helper, int tick)
        {
            foreach (TacticalMemberCommand member in active)
            {
                if (member == helper || member.Pawn.CurJob == member.Job && member.Job?.def.defName == "HD_NewTacticalContactGuard") continue;
                if (tick < member.RetryTick || !CanIssue(member)) continue;
                Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"), member.Pawn.Position,
                    command.FieldResponse?.Focus ?? command.Goal));
            }
        }
    }

    public sealed class JobDriver_TacticalRecoverMedibag : TacticalJobDriver
    {
        private bool recovered;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner();
            MapComponent_TacticalCommands owner = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
            this.FailOn(() => !recovered && (!owner.OwnsBagRecovery(pawn, job) || job.targetA.Thing?.Spawned != true));
            yield return AdmitMovement();
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_General.Wait(60, TargetIndex.A);
            yield return Toils_General.Do(() =>
            {
                var bag = job.targetA.Thing as Apparel;
                if (bag?.Spawned != true || !TacticalMedicalEquipmentPolicy.Compatible(pawn, bag))
                { EndJobWith(JobCondition.Incompletable); return; }
                pawn.apparel.Wear(bag, false);
                if (bag.Wearer != pawn)
                {
                    if (!bag.Spawned && bag.holdingOwner == null) GenPlace.TryPlaceThing(bag, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    EndJobWith(JobCondition.Incompletable); return;
                }
                recovered = true; owner.MedicalBagRecovered(pawn, job);
            });
        }
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref recovered, "recoveredBag"); }
    }
}
