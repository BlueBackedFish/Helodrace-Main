using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace Helodrace
{
    public class WorkGiver_DevelopOilField : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(DefDatabase<ThingDef>.GetNamed("HD_CableToolRig"));

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t.IsForbidden(pawn)) return false;
            if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;
            
            var comp = t.TryGetComp<CompOilFieldDevelopment>();
            if (comp == null || !comp.IsPoweredBySteam) return false;

            // Only provide the job if maintenance is required
            return comp.needsMaintenance || forced;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_DevelopOilField"), t);
        }
    }

    public class JobDriver_DevelopOilField : JobDriver
    {
        protected Thing Rig => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Rig, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            // Pawn spends time servicing the rig
            Toil work = Toils_General.Wait(600); // 10 seconds (600 ticks)
            work.WithProgressBarToilDelay(TargetIndex.A);
            work.tickAction = delegate
            {
                pawn.skills.Learn(SkillDefOf.Construction, 0.5f);
            };
            work.AddFinishAction(delegate
            {
                Rig.TryGetComp<CompOilFieldDevelopment>()?.Notify_WorkPerformed();
            });
            yield return work;
        }
    }
}
