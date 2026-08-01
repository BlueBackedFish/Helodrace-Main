using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public enum MechanicalHazardTarget
    {
        Area,
        Operator
    }

    public class CompProperties_MechanicalHazard : CompProperties
    {
        public float rpmErrorThreshold = -1f;
        public float mtbSeconds = 30f;
        public MechanicalHazardTarget target = MechanicalHazardTarget.Area;
        public DamageDef damageDef;
        public float damageAmount = 10f;
        public float damageRadius = 1.9f;
        public BodyPartDef bodyPart;

        public CompProperties_MechanicalHazard()
        {
            compClass = typeof(CompMechanicalHazard);
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
        {
            foreach (StatDrawEntry stat in base.SpecialDisplayStats(req))
            {
                yield return stat;
            }

            string condition = "HD_MechanicalHazard_Disabled".Translate().Resolve();
            if (rpmErrorThreshold >= 0f)
            {
                condition = "HD_MechanicalHazard_ConditionUserBoth".Translate(
                    rpmErrorThreshold.ToString("F0")).Resolve();
            }
            else
            {
                condition = "HD_MechanicalHazard_ConditionUserSpeed".Translate().Resolve();
            }

            string damage = (damageDef?.label ?? DamageDefOf.Crush.label)
                + " " + damageAmount.ToString("F0");
            string targetText = target == MechanicalHazardTarget.Area
                ? "HD_MechanicalHazard_TargetArea".Translate(damageRadius.ToString("F1")).Resolve()
                : "HD_MechanicalHazard_TargetOperator".Translate(
                    bodyPart?.label ?? "HD_MechanicalHazard_AnyBodyPart".Translate().Resolve()).Resolve();

            yield return MechanicalStatEntries.Entry(
                "HD_Stat_MechanicalHazards",
                condition + "; " + targetText + "; " + damage,
                "HD_Stat_MechanicalHazards_Desc",
                4);
        }
    }

    public class CompMechanicalHazard : ThingComp
    {
        public CompProperties_MechanicalHazard Props =>
            (CompProperties_MechanicalHazard)props;

        private int lastEvaluationTick = -1;

        public void Evaluate(
            float rpm,
            float rpmError,
            bool operating,
            float dangerousBelowRpm)
        {
            if (!operating || !parent.Spawned || parent.Map == null)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (lastEvaluationTick == currentTick)
            {
                return;
            }
            lastEvaluationTick = currentTick;

            bool lowRpm = dangerousBelowRpm > 0f
                && rpm > 0f
                && rpm < dangerousBelowRpm;
            bool excessiveError = Props.rpmErrorThreshold >= 0f
                && rpmError > Props.rpmErrorThreshold;

            if (!lowRpm && !excessiveError)
            {
                return;
            }

            float mtbSeconds = Mathf.Max(0.1f, Props.mtbSeconds);
            if (!Rand.MTBEventOccurs(
                    mtbSeconds,
                    GenTicks.TicksPerRealSecond,
                    GenTicks.TicksPerRealSecond))
            {
                return;
            }

            DamageDef selectedDamage = Props.damageDef ?? DamageDefOf.Crush;
            bool applied = Props.target == MechanicalHazardTarget.Operator
                ? DamageOperator(selectedDamage)
                : DamageArea(selectedDamage);

            if (applied)
            {
                string reason = lowRpm
                    ? "HD_MechanicalHazard_ReasonLowRpm".Translate().Resolve()
                    : "HD_MechanicalHazard_ReasonError".Translate().Resolve();
                Messages.Message(
                    "HD_MechanicalHazard_Triggered".Translate(parent.Label, reason),
                    parent,
                    MessageTypeDefOf.NegativeEvent);
            }
        }

        private bool DamageArea(DamageDef selectedDamage)
        {
            GenExplosion.DoExplosion(
                parent.Position,
                parent.Map,
                Mathf.Max(0.1f, Props.damageRadius),
                selectedDamage,
                parent,
                Mathf.Max(0, Mathf.RoundToInt(Props.damageAmount)));
            return true;
        }

        private bool DamageOperator(DamageDef selectedDamage)
        {
            Pawn operatorPawn = FindOperator();
            if (operatorPawn == null)
            {
                return false;
            }

            BodyPartRecord hitPart = FindRequestedBodyPart(operatorPawn);
            operatorPawn.TakeDamage(new DamageInfo(
                selectedDamage,
                Mathf.Max(0f, Props.damageAmount),
                -1f,
                -1f,
                parent,
                hitPart));
            return true;
        }

        private Pawn FindOperator()
        {
            IReadOnlyList<Pawn> pawns = parent.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                Job job = pawn?.CurJob;
                if (job == null)
                {
                    continue;
                }

                if (job.targetA.Thing == parent
                    || job.targetB.Thing == parent
                    || job.targetC.Thing == parent)
                {
                    return pawn;
                }
            }

            return null;
        }

        private BodyPartRecord FindRequestedBodyPart(Pawn pawn)
        {
            if (Props.bodyPart == null)
            {
                return null;
            }

            List<BodyPartRecord> matches = new List<BodyPartRecord>();
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part.def == Props.bodyPart)
                {
                    matches.Add(part);
                }
            }

            return matches.Count > 0 ? matches.RandomElement() : null;
        }
    }
}
