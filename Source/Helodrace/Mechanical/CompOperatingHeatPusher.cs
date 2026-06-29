using RimWorld;
using Verse;

namespace Helodrace
{
    public class CompOperatingHeatPusher : ThingComp
    {
        private const int HeatPushInterval = GenTicks.TickRareInterval;

        public CompProperties_OperatingHeatPusher Props => (CompProperties_OperatingHeatPusher)props;

        public override void CompTick()
        {
            base.CompTick();
            if (parent.IsHashIntervalTick(HeatPushInterval))
            {
                PushOperatingHeat();
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            PushOperatingHeat();
        }

        private void PushOperatingHeat()
        {
            if (!parent.Spawned || parent.Map == null || !IsOperating)
            {
                return;
            }

            float roomTemperature = GenTemperature.GetTemperatureForCell(parent.Position, parent.Map);
            if (roomTemperature >= Props.heatPushMaxTemperature)
            {
                return;
            }

            GenTemperature.PushHeat(parent.Position, parent.Map, Props.heatPerSecond * HeatPushInterval / GenTicks.TicksPerRealSecond);
        }

        private bool IsOperating
        {
            get
            {
                CompFlickable flickable = parent.GetComp<CompFlickable>();
                if (flickable != null && !flickable.SwitchIsOn)
                {
                    return false;
                }

                CompRefuelable refuelable = parent.GetComp<CompRefuelable>();
                if (refuelable != null && !refuelable.HasFuel)
                {
                    return false;
                }

                CompMechanicalEmitter emitter = parent.GetComp<CompMechanicalEmitter>();
                if (emitter != null)
                {
                    return emitter.IsProducingPower;
                }

                CompMechanicalUser user = parent.GetComp<CompMechanicalUser>();
                if (user != null)
                {
                    return user.HasPower;
                }

                return refuelable != null;
            }
        }
    }

    public class CompProperties_OperatingHeatPusher : CompProperties
    {
        public float heatPerSecond = 6f;
        public float heatPushMaxTemperature = 80f;

        public CompProperties_OperatingHeatPusher()
        {
            compClass = typeof(CompOperatingHeatPusher);
        }
    }
}
