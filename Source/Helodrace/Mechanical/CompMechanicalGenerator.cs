using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace
{
    public class CompPowerPlantMechanical : CompPowerPlant
    {
        private CompMechanicalGenerator generator;
        protected override float DesiredPowerOutput => generator?.CurrentElectricalOutput ?? 0f;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            generator = parent.GetComp<CompMechanicalGenerator>();
            base.PostSpawnSetup(respawningAfterLoad);
        }
    }

    public class CompProperties_MechanicalGenerator : CompProperties
    {
        public float ratedMechanicalInput = 10000f;
        public float peakEfficiency = .80f;
        public float minimumEfficiency = .60f;
        public float defaultTargetElectricalOutput = 300f;
        public CompProperties_MechanicalGenerator() { compClass = typeof(CompMechanicalGenerator); }
        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
        {
            foreach (var stat in base.SpecialDisplayStats(req)) yield return stat;
            yield return MechanicalStatEntries.Entry("HD_Stat_GeneratorRatedInput", ratedMechanicalInput.ToString("F0") + " W", "HD_Stat_GeneratorRatedInput_Desc", 10);
            yield return MechanicalStatEntries.Entry("HD_Stat_GeneratorEfficiency", (peakEfficiency * 100).ToString("F0") + "% / " + (minimumEfficiency * 100).ToString("F0") + "%", "HD_Stat_GeneratorEfficiency_Desc", 11);
        }
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (var error in base.ConfigErrors(parentDef)) yield return error;
            if (ratedMechanicalInput <= 0 || minimumEfficiency < 0 || peakEfficiency <= 0
                || peakEfficiency > 1 || minimumEfficiency > peakEfficiency)
                yield return "mechanical generator requires positive rated input and 0 <= minimum <= peak <= 1 efficiency";
        }
    }

    public class CompMechanicalGenerator : ThingComp
    {
        public CompProperties_MechanicalGenerator Props => (CompProperties_MechanicalGenerator)props;
        private float targetElectricalOutput = -1f;
        private CompMechanicalUser mechanicalUser;
        private CompPowerPlant powerPlant;
        public float MaximumElectricalOutput => MathF.Max(0f, Props.ratedMechanicalInput * Props.peakEfficiency);
        public float TargetElectricalOutput
        {
            get => Math.Clamp(targetElectricalOutput < 0 ? Props.defaultTargetElectricalOutput : targetElectricalOutput, 0f, MaximumElectricalOutput);
            set
            {
                targetElectricalOutput = float.IsFinite(value) ? Math.Clamp(value, 0f, MaximumElectricalOutput) : 0f;
                if (parent.Spawned) mechanicalUser?.Network?.UpdateNetwork();
            }
        }
        public float RequestedMechanicalInput => MechanicalPowerMath.MechanicalInputFor(
            TargetElectricalOutput, Props.ratedMechanicalInput, Props.minimumEfficiency, Props.peakEfficiency);
        public float CurrentMechanicalInput { get; private set; }
        public float CurrentEfficiency { get; private set; }
        public float CurrentElectricalOutput { get; private set; }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref targetElectricalOutput, "targetElectricalOutput", -1f);
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            mechanicalUser = parent.GetComp<CompMechanicalUser>();
            powerPlant = parent.GetComp<CompPowerPlant>();
            SetOutput(0f, 0f, 0f);
        }
        public void RefreshOutput()
        {
            if (mechanicalUser == null || powerPlant == null || !mechanicalUser.HasPower || !parent.Spawned)
            {
                SetOutput(0f, 0f, 0f);
                return;
            }
            float input = MathF.Min(Props.ratedMechanicalInput,
                mechanicalUser.GridPowerDemanded * Math.Clamp(mechanicalUser.PowerFulfillmentRatio, 0f, 1f));
            float output = MathF.Min(TargetElectricalOutput,
                MechanicalPowerMath.ElectricalOutput(input, Props.ratedMechanicalInput, Props.minimumEfficiency, Props.peakEfficiency));
            SetOutput(input, input > 0f ? output / input : 0f, output);
        }
        private void SetOutput(float input, float efficiency, float output)
        {
            CurrentMechanicalInput = input;
            CurrentEfficiency = efficiency;
            CurrentElectricalOutput = output;
            if (powerPlant != null) powerPlant.PowerOutput = output;
        }
        public override string CompInspectStringExtra() => "HD_MechanicalGenerator_Target".Translate(
            TargetElectricalOutput.ToString("F0"), RequestedMechanicalInput.ToString("F0")).Resolve() + "\n"
            + (mechanicalUser?.HasPower != true ? "HD_MechanicalGenerator_Stopped".Translate().Resolve()
                : "HD_MechanicalGenerator_Info".Translate(CurrentMechanicalInput.ToString("F0"),
                    (CurrentEfficiency * 100).ToString("F0"), CurrentElectricalOutput.ToString("F0")).Resolve());
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "HD_Generator_SetOutput".Translate(TargetElectricalOutput.ToString("F0")),
                defaultDesc = "HD_Generator_SetOutputDesc".Translate(),
                icon = TexCommand.DesirePower,
                action = () =>
                {
                    var choices = new List<FloatMenuOption>();
                    foreach (float watts in new[] { 0f, 300f, 600f, 1200f, 1800f, 2400f, 3600f, 4800f, 6000f, MaximumElectricalOutput })
                    {
                        float target = MathF.Min(watts, MaximumElectricalOutput);
                        if (choices.Exists(o => o.Label == target.ToString("F0") + " W")) continue;
                        choices.Add(new FloatMenuOption(target.ToString("F0") + " W", () => TargetElectricalOutput = target));
                    }
                    Find.WindowStack.Add(new FloatMenu(choices));
                }
            };
        }
    }
}