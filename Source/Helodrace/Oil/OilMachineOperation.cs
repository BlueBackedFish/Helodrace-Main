using RimWorld;
using Verse;

namespace Helodrace
{
    internal static class OilMachineOperation
    {
        // Every installed supply must be available. A fuel-driven rig can run
        // independently; a shaft user still needs its mechanical network.
        public static bool IsPowered(ThingWithComps machine)
        {
            CompFlickable flickable = machine.GetComp<CompFlickable>();
            if (flickable != null && !flickable.SwitchIsOn) return false;
            CompMechanicalUser shaft = machine.GetComp<CompMechanicalUser>();
            CompPowerTrader electric = machine.GetComp<CompPowerTrader>();
            CompRefuelable fuel = machine.GetComp<CompRefuelable>();
            if (shaft != null && !shaft.HasPower) return false;
            if (electric != null && !electric.PowerOn) return false;
            if (fuel != null && !fuel.HasFuel) return false;
            return shaft != null || electric != null || fuel != null;
        }
    }
}
