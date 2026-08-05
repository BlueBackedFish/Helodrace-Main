using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class CompProperties_ANFODetonator : CompProperties
    {
        public float detonationRange = 50f;
        public string chargeDefName = "HD_ANFOBlastingCharge";
        public int operationTicks = 120;

        public CompProperties_ANFODetonator()
        {
            compClass = typeof(CompANFODetonator);
        }
    }

    public class CompANFODetonator : ThingComp
    {
        private CompProperties_ANFODetonator Props => (CompProperties_ANFODetonator)props;

        public int OperationTicks => Props.operationTicks;

        public bool HasLinkedCharges => LinkedCharges.Count > 0;

        private List<Thing> LinkedCharges
        {
            get
            {
                if (!parent.Spawned)
                    return new List<Thing>();

                ThingDef chargeDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.chargeDefName);
                if (chargeDef == null)
                    return new List<Thing>();

                float rangeSquared = Props.detonationRange * Props.detonationRange;
                return parent.Map.listerThings.ThingsOfDef(chargeDef)
                    .Where(charge => charge.Spawned
                        && charge.Faction == parent.Faction
                        && charge.TryGetComp<CompANFORainSensitivity>()?.CanDetonate != false
                        && charge.Position.DistanceToSquared(parent.Position) <= rangeSquared)
                    .OrderBy(charge => charge.Position.DistanceToSquared(parent.Position))
                    .ToList();
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (!parent.Spawned || parent.Faction != Faction.OfPlayer)
                yield break;

            List<Thing> charges = LinkedCharges;
            Command_Action command = new Command_Action
            {
                defaultLabel = "HD_ANFO_Detonate_Label".Translate(),
                defaultDesc = "HD_ANFO_Detonate_Desc".Translate(Props.detonationRange.ToString("0")),
                icon = ContentFinder<Texture2D>.Get("Buildings/HD_MiningCharge_north", true),
                action = BeginDetonationJob
            };

            if (charges.Count == 0)
                command.Disable("HD_ANFO_NoLinkedCharges".Translate(Props.detonationRange.ToString("0")));

            yield return command;
        }

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned)
                return null;

            return "HD_ANFO_LinkedCharges".Translate(LinkedCharges.Count, Props.detonationRange.ToString("0"));
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (!parent.Spawned)
                return;

            GenDraw.DrawRadiusRing(parent.Position, Props.detonationRange);
            foreach (Thing charge in LinkedCharges)
                GenDraw.DrawLineBetween(parent.DrawPos, charge.DrawPos, SimpleColor.Red, 0.15f);
        }

        private Pawn FindOperator()
        {
            if (!parent.Spawned)
                return null;

            return parent.Map.mapPawns.FreeColonistsSpawned
                .Where(pawn => !pawn.Downed
                    && !pawn.Drafted
                    && !pawn.InMentalState
                    && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                    && pawn.CanReserveAndReach(parent, PathEndMode.InteractionCell, Danger.Deadly))
                .OrderBy(pawn => pawn.Position.DistanceToSquared(parent.Position))
                .FirstOrDefault();
        }

        private void BeginDetonationJob()
        {
            if (!HasLinkedCharges)
            {
                Messages.Message(
                    "HD_ANFO_NoLinkedCharges".Translate(Props.detonationRange.ToString("0")),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            Pawn operatorPawn = FindOperator();
            if (operatorPawn == null)
            {
                Messages.Message("HD_ANFO_NoOperator".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_OperateANFODetonator");
            if (jobDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_OperateANFODetonator JobDef is missing.", 48612051);
                return;
            }

            operatorPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, parent), JobTag.Misc);
        }

        public void DetonateLinkedCharges(Pawn operatorPawn)
        {
            List<Thing> charges = LinkedCharges;
            if (charges.Count == 0)
            {
                Messages.Message(
                    "HD_ANFO_NoLinkedCharges".Translate(Props.detonationRange.ToString("0")),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            int armedCount = 0;
            foreach (Thing charge in charges)
            {
                CompExplosive explosive = charge.TryGetComp<CompExplosive>();
                if (explosive == null)
                    continue;

                explosive.StartWick(operatorPawn ?? parent);
                armedCount++;
            }

            Messages.Message(
                "HD_ANFO_Detonated".Translate(armedCount),
                new LookTargets(charges),
                MessageTypeDefOf.ThreatBig,
                false);
        }
    }

    public class JobDriver_OperateANFODetonator : JobDriver
    {
        private const TargetIndex DetonatorInd = TargetIndex.A;

        private Thing Detonator => job.GetTarget(DetonatorInd).Thing;

        private CompANFODetonator DetonatorComp => Detonator?.TryGetComp<CompANFODetonator>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Detonator, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(DetonatorInd);
            this.FailOn(() => DetonatorComp == null || !DetonatorComp.HasLinkedCharges);

            yield return Toils_Goto.GotoThing(DetonatorInd, PathEndMode.InteractionCell);

            int operationTicks = DetonatorComp?.OperationTicks ?? 120;
            Toil operate = Toils_General.Wait(operationTicks, DetonatorInd);
            operate.WithProgressBarToilDelay(DetonatorInd);
            yield return operate;

            yield return Toils_General.Do(() => DetonatorComp?.DetonateLinkedCharges(pawn));
        }
    }
}
