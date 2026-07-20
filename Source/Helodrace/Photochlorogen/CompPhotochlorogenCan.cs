using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class CompProperties_PhotochlorogenCan : CompProperties
    {
        public float radius = 10f;
        public float spawnChance = 0.55f;
        public int gasPerCell = 1;
        public float densityPerPulse = 0.22f;
        public float emissionRadius = 1.9f;
        public float edgeDensityFactor = 0.55f;
        public int burnDurationTicks = 1800;
        public int gasSimulationIntervalTicks = 30;
        public int spreadIntervalTicks = 300;
        public ThingDef gasDef;
        public bool destroyOnUse = true;

        public CompProperties_PhotochlorogenCan()
        {
            compClass = typeof(CompPhotochlorogenCan);
        }
    }

    public class CompPhotochlorogenCan : ThingComp
    {
        private const string IgniteJobDefName = "HD_IgnitePhotochlorogenCan";

        private bool used;
        private bool burning;
        private int burnEndTick;
        private int nextSpreadTick;

        public CompProperties_PhotochlorogenCan Props => (CompProperties_PhotochlorogenCan)props;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref used, "used", false);
            Scribe_Values.Look(ref burning, "burning", false);
            Scribe_Values.Look(ref burnEndTick, "burnEndTick", 0);
            Scribe_Values.Look(ref nextSpreadTick, "nextSpreadTick", 0);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!burning || parent.Map == null)
            {
                return;
            }

            if (parent.IsHashIntervalTick(60))
            {
                ThrowBurningEffect();
            }

            int ticksGame = Find.TickManager.TicksGame;
            if (ticksGame >= nextSpreadTick)
            {
                int simulationTicks = Mathf.Clamp(Props.gasSimulationIntervalTicks, 1, Props.spreadIntervalTicks);
                SpreadGas(simulationTicks);
                nextSpreadTick = ticksGame + simulationTicks;
            }

            if (ticksGame >= burnEndTick)
            {
                FinishBurning();
            }
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.CompFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            if (selPawn == null || selPawn.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            string label = "HD_PhotochlorogenCan_Deploy_Label".Translate();
            if (used)
            {
                yield return new FloatMenuOption(label + ": " + (burning ? "HD_PhotochlorogenCan_Deploy_Burning" : "HD_PhotochlorogenCan_Deploy_Used").Translate(), null);
                yield break;
            }

            if (!selPawn.CanReach(parent, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
                yield break;
            }

            if (!selPawn.CanReserve(parent))
            {
                yield return new FloatMenuOption(label + ": " + "Reserved".Translate(), null);
                yield break;
            }

            yield return new FloatMenuOption(label, delegate
            {
                JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(IgniteJobDefName);
                if (jobDef == null)
                {
                    Log.ErrorOnce("Helodrace: HD_IgnitePhotochlorogenCan JobDef is missing.", 93214601);
                    return;
                }

                Job job = JobMaker.MakeJob(jobDef, parent);
                selPawn.jobs.TryTakeOrderedJob(job);
            });
        }

        public void Ignite(Pawn igniter)
        {
            if (used || parent.Map == null || Props.gasDef == null)
            {
                return;
            }

            used = true;
            burning = true;
            burnEndTick = Find.TickManager.TicksGame + Props.burnDurationTicks;
            nextSpreadTick = Find.TickManager.TicksGame;
            ThrowIgnitionEffect();
            Messages.Message("HD_PhotochlorogenCan_Deploy_Message".Translate(igniter?.LabelShort ?? parent.LabelShort, parent.LabelShort, Props.radius.ToString("F0")), parent, MessageTypeDefOf.NegativeEvent);
        }

        private void SpreadGas(int simulationTicks)
        {
            if (parent.Map == null || Props.gasDef == null)
            {
                return;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.emissionRadius, true))
            {
                if (!CanGasOccupy(cell))
                {
                    continue;
                }

                float distanceFactor = Mathf.InverseLerp(Props.emissionRadius, 0f, parent.Position.DistanceTo(cell));
                float densityFactor = Mathf.Lerp(Props.edgeDensityFactor, 1f, distanceFactor);
                float simulationFactor = Props.spreadIntervalTicks > 0 ? simulationTicks / (float)Props.spreadIntervalTicks : 1f;
                Gas_Photochlorogen.AddGasAt(cell, parent.Map, Props.gasDef, Props.densityPerPulse * Props.gasPerCell * densityFactor * Props.spawnChance * simulationFactor);
            }
        }

        private void FinishBurning()
        {
            burning = false;
            if (Props.destroyOnUse && !parent.Destroyed)
            {
                parent.Destroy(DestroyMode.Vanish);
            }
        }

        private bool CanGasOccupy(IntVec3 cell)
        {
            if (parent.Map == null || !cell.InBounds(parent.Map))
            {
                return false;
            }

            MapComponent_PhotochlorogenGasGrid gasGrid = parent.Map.GetComponent<MapComponent_PhotochlorogenGasGrid>();
            return gasGrid?.CanGasOccupy(cell) ?? cell.Standable(parent.Map);
        }

        private void ThrowIgnitionEffect()
        {
            Vector3 loc = parent.DrawPos;
            loc.z += 0.45f;
            FleckMaker.ThrowFireGlow(loc, parent.Map, 2.0f);
            FleckMaker.ThrowMicroSparks(loc, parent.Map);
            FleckMaker.ThrowSmoke(loc, parent.Map, 1.4f);
        }

        private void ThrowBurningEffect()
        {
            Vector3 loc = parent.DrawPos;
            loc.z += 0.45f;
            FleckMaker.ThrowFireGlow(loc, parent.Map, 1.25f);
            FleckMaker.ThrowSmoke(loc, parent.Map, 0.7f);
        }
    }

    public class JobDriver_IgnitePhotochlorogenCan : JobDriver
    {
        private const TargetIndex CanInd = TargetIndex.A;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(CanInd);
            this.FailOnBurningImmobile(CanInd);
            this.FailOn(() => TargetThingA.TryGetComp<CompPhotochlorogenCan>() == null);

            yield return Toils_Goto.GotoThing(CanInd, PathEndMode.Touch);

            Toil ignite = Toils_General.Wait(120);
            ignite.WithProgressBarToilDelay(CanInd);
            ignite.FailOnCannotTouch(CanInd, PathEndMode.Touch);
            yield return ignite;

            yield return new Toil
            {
                initAction = delegate
                {
                    Thing thing = job.GetTarget(CanInd).Thing;
                    thing?.TryGetComp<CompPhotochlorogenCan>()?.Ignite(pawn);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(CanInd), job, 1, -1, null, errorOnFailed);
        }
    }
}
