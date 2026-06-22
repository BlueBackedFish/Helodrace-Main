using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class CompMechanicalTemperatureControl : ThingComp
    {
        public CompProperties_MechanicalTemperatureControl Props => (CompProperties_MechanicalTemperatureControl)props;

        private CompMechanicalUser mechanicalUser;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            mechanicalUser = parent.GetComp<CompMechanicalUser>();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();

            if (mechanicalUser == null || !mechanicalUser.HasPower)
            {
                return;
            }

            float transferPerSecond = Mathf.Abs(Props.heatPerSecond) * HeatTransferFactor;
            if (Mathf.Approximately(transferPerSecond, 0f))
            {
                return;
            }

            float coldEnergyLimit = -transferPerSecond;
            float hotEnergyLimit = transferPerSecond * Props.heatDumpFactor;
            bool coldSideOutdoor = IsOutdoorOrInvalid(ColdSideCell);
            bool hotSideOutdoor = IsOutdoorOrInvalid(HotSideCell);

            if (coldSideOutdoor != hotSideOutdoor)
            {
                coldEnergyLimit *= Props.outdoorSideEfficiencyMultiplier;
                hotEnergyLimit *= Props.outdoorSideEfficiencyMultiplier;
            }

            ApplyTemperatureChangeToRoom(ColdSideCell, coldEnergyLimit, Props.coldSideTargetTemperature);
            ApplyTemperatureChangeToRoom(HotSideCell, hotEnergyLimit, Props.hotSideTargetTemperature);
        }

        private bool IsOutdoorOrInvalid(IntVec3 cell)
        {
            if (!cell.InBounds(parent.Map))
            {
                return true;
            }

            Room room = cell.GetRoom(parent.Map);
            return room == null || room.UsesOutdoorTemperature;
        }

        private void ApplyTemperatureChangeToRoom(IntVec3 cell, float energyLimit, float targetTemperature)
        {
            if (!cell.InBounds(parent.Map))
            {
                return;
            }

            Room room = cell.GetRoom(parent.Map);
            if (room == null || room.UsesOutdoorTemperature)
            {
                return;
            }

            float tempChange = GenTemperature.ControlTemperatureTempChange(cell, parent.Map, energyLimit, targetTemperature);
            if (!Mathf.Approximately(tempChange, 0f))
            {
                room.Temperature += tempChange;
            }
        }

        private IntVec3 ColdSideCell => parent.Position + IntVec3.South.RotatedBy(parent.Rotation);

        private IntVec3 HotSideCell => parent.Position + IntVec3.North.RotatedBy(parent.Rotation);

        private float RatedRPM
        {
            get
            {
                if (Props.ratedRPM > 0f)
                {
                    return Props.ratedRPM;
                }

                return mechanicalUser?.Props.recommendedRPM ?? 1f;
            }
        }

        private float HeatTransferFactor
        {
            get
            {
                if (mechanicalUser == null || !mechanicalUser.HasPower)
                {
                    return 0f;
                }

                float ratedRPM = Mathf.Max(1f, RatedRPM);
                return Mathf.Clamp(mechanicalUser.RealRPM / ratedRPM, 0f, Props.maxHeatTransferFactor);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (mechanicalUser == null)
            {
                return null;
            }

            float coldPerSecond = Mathf.Abs(Props.heatPerSecond) * HeatTransferFactor;
            float hotPerSecond = coldPerSecond * Props.heatDumpFactor;
            string coldRoom = SideRoomLabel(ColdSideCell);
            string hotRoom = SideRoomLabel(HotSideCell);
            return "Heat pump transfer: -" + coldPerSecond.ToString("F1") + " W / +" + hotPerSecond.ToString("F1") + " W (RPM " + mechanicalUser.RealRPM.ToString("F0") + "/" + RatedRPM.ToString("F0") + ")\nCold side: " + coldRoom + "\nHot side: " + hotRoom;
        }

        private string SideRoomLabel(IntVec3 cell)
        {
            if (!cell.InBounds(parent.Map))
            {
                return "out of bounds";
            }

            Room room = cell.GetRoom(parent.Map);
            if (room == null)
            {
                return "no room (" + OutdoorTemperatureAt(cell).ToStringTemperature("F1") + ")";
            }

            if (room.UsesOutdoorTemperature)
            {
                return "outdoors (" + OutdoorTemperatureAt(cell).ToStringTemperature("F1") + ")";
            }

            return room.Temperature.ToStringTemperature("F1");
        }

        private float OutdoorTemperatureAt(IntVec3 cell)
        {
            return GenTemperature.GetTemperatureForCell(cell, parent.Map);
        }
    }

    public class CompProperties_MechanicalTemperatureControl : CompProperties
    {
        public float heatPerSecond = -21f;
        public float heatDumpFactor = 1.25f;
        public float outdoorSideEfficiencyMultiplier = 1f;
        public float coldSideTargetTemperature = -273.15f;
        public float hotSideTargetTemperature = 1000f;
        public float ratedRPM = 0f;
        public float maxHeatTransferFactor = 1.5f;

        public CompProperties_MechanicalTemperatureControl()
        {
            compClass = typeof(CompMechanicalTemperatureControl);
        }
    }
}
