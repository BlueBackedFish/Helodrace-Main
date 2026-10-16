using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public static class TemporaryRestraint
    {
        public const string DefName = "HD_TemporaryRestraint", JobName = "HD_TemporaryRestrain";
        public const int WorkTicks = 120;
        public static bool Bound(Pawn patient) => patient?.health?.hediffSet?.hediffs
            .Any(h => h.def.defName == DefName) == true;
        public static bool CanAid(Pawn patient) => patient != null && !patient.Dead && patient.Downed && Bound(patient);
        public static bool CanHandle(Pawn actor, Pawn patient, bool release) => actor != null && patient != null
            && actor != patient && actor.Spawned && patient.Spawned && actor.Map == patient.Map
            && !actor.Dead && !actor.Downed && !actor.InMentalState && !patient.Dead
            && actor.RaceProps.Humanlike && patient.RaceProps.Humanlike
            && actor.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
            && (release ? Bound(patient) : patient.Downed && patient.HostileTo(actor) && !Bound(patient));
        public static void Apply(Pawn patient)
        {
            if (!Bound(patient)) patient.health.AddHediff(DefDatabase<HediffDef>.GetNamed(DefName));
        }
        public static void Release(Pawn patient)
        {
            foreach (var effect in patient.health.hediffSet.hediffs.Where(h => h.def.defName == DefName).ToList())
                patient.health.RemoveHediff(effect);
        }
    }

    public sealed class JobDriver_TemporaryRestrain : Tactics.TacticalJobDriver
    {
        private Pawn Patient => job.targetA.Thing as Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(Patient, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner();
            this.FailOn(() => !TemporaryRestraint.CanHandle(pawn, Patient, job.count == 1)
                || Tactics.MapComponent_TacticalCommands.KnownCareDanger(pawn)
                || Tactics.MapComponent_TacticalCommands.KnownCareDanger(Patient));
            yield return AdmitMovement();
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var work = Toils_General.Wait(TemporaryRestraint.WorkTicks, TargetIndex.A);
            work.FailOn(() => !pawn.Position.AdjacentTo8WayOrInside(Patient.Position));
            work.WithProgressBarToilDelay(TargetIndex.A); yield return work;
            yield return Toils_General.Do(() =>
            {
                if (job.count == 1) TemporaryRestraint.Release(Patient);
                else TemporaryRestraint.Apply(Patient);
            });
        }
    }

    public sealed class FloatMenuOptionProvider_TemporaryRestraint : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;
        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            var actor = context.FirstSelectedPawn;
            bool release = TemporaryRestraint.Bound(clickedPawn);
            if (!TemporaryRestraint.CanHandle(actor, clickedPawn, release)) return null;
            string label = (release ? "HD_Restraint_Release" : "HD_Restraint_Bind").Translate(clickedPawn.LabelShort);
            if (!actor.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
            return new FloatMenuOption(label, () =>
            {
                var order = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TemporaryRestraint.JobName), clickedPawn);
                order.count = release ? 1 : 0;
                actor.jobs.TryTakeOrderedJob(order, JobTag.Misc);
            });
        }
    }
}
