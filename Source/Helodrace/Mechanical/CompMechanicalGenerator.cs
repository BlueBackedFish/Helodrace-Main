using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class CompPowerPlantMechanical : CompPowerPlant
    {
        private CompMechanicalGenerator generator;

        protected override float DesiredPowerOutput =>
            generator?.CurrentElectricalOutput ?? 0f;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            generator = parent.GetComp<CompMechanicalGenerator>();
            base.PostSpawnSetup(respawningAfterLoad);
        }
    }

    public class CompProperties_MechanicalGenerator : CompProperties
    {
        public float ratedMechanicalInput = 10000f;
        public float peakEfficiency = 0.80f;
        public float minimumEfficiency = 0.60f;
        public float inputDeviationPerStep = 0.10f;
        public float efficiencyStep = 0.05f;

        public CompProperties_MechanicalGenerator()
        {
            compClass = typeof(CompMechanicalGenerator);
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
        {
            foreach (StatDrawEntry stat in base.SpecialDisplayStats(req))
            {
                yield return stat;
            }

            yield return MechanicalStatEntries.Entry(
                "HD_Stat_GeneratorRatedInput",
                ratedMechanicalInput.ToString("F0") + " W",
                "HD_Stat_GeneratorRatedInput_Desc",
                10);
            yield return MechanicalStatEntries.Entry(
                "HD_Stat_GeneratorEfficiency",
                (peakEfficiency * 100f).ToString("F0") + "% / "
                    + (minimumEfficiency * 100f).ToString("F0") + "%",
                "HD_Stat_GeneratorEfficiency_Desc",
                11);
        }
    }

    public class CompMechanicalGenerator : ThingComp
    {
        public CompProperties_MechanicalGenerator Props =>
            (CompProperties_MechanicalGenerator)props;

        public float CurrentMechanicalInput { get; private set; }
        public float CurrentEfficiency { get; private set; }
        public float CurrentElectricalOutput { get; private set; }

        private CompMechanicalUser mechanicalUser;
        private CompPowerPlant powerPlant;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            mechanicalUser = parent.GetComp<CompMechanicalUser>();
            powerPlant = parent.GetComp<CompPowerPlant>();
            SetOutput(0f, 0f, 0f);
        }

        public void RefreshOutput()
        {
            if (mechanicalUser == null || powerPlant == null || !mechanicalUser.HasPower)
            {
                SetOutput(0f, 0f, 0f);
                return;
            }

            float ratedInput = Mathf.Max(1f, Props.ratedMechanicalInput);
            float actualInput = Mathf.Max(
                0f,
                mechanicalUser.GridTorqueDemanded
                    * Mathf.Clamp01(mechanicalUser.TorqueFulfillmentRatio));

            float stepWidth = ratedInput * Mathf.Max(0.001f, Props.inputDeviationPerStep);
            float deviation = Mathf.Abs(actualInput - ratedInput);
            int efficiencySteps = Mathf.FloorToInt((deviation + 0.001f) / stepWidth);
            float efficiency = Mathf.Max(
                Props.minimumEfficiency,
                Props.peakEfficiency - efficiencySteps * Props.efficiencyStep);
            float electricalOutput = actualInput * efficiency;

            SetOutput(actualInput, efficiency, electricalOutput);
        }

        private void SetOutput(float mechanicalInput, float efficiency, float electricalOutput)
        {
            CurrentMechanicalInput = mechanicalInput;
            CurrentEfficiency = efficiency;
            CurrentElectricalOutput = electricalOutput;

            if (powerPlant != null)
            {
                powerPlant.PowerOutput = electricalOutput;
            }
        }

        public override string CompInspectStringExtra()
        {
            if (mechanicalUser == null || !mechanicalUser.HasPower)
            {
                return "HD_MechanicalGenerator_Stopped".Translate().Resolve();
            }

            return "HD_MechanicalGenerator_Info".Translate(
                CurrentMechanicalInput.ToString("F0"),
                (CurrentEfficiency * 100f).ToString("F0"),
                CurrentElectricalOutput.ToString("F0")).Resolve();
        }
    }
}
