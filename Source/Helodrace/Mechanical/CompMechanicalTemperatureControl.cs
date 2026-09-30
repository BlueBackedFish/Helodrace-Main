using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class CompMechanicalTemperatureControl : ThingComp
    {
        public CompProperties_MechanicalTemperatureControl Props => (CompProperties_MechanicalTemperatureControl)props;

        private CompMechanicalUser mechanicalUser;
        private float targetColdHeatRemovalPerSecond = -1f;

        public float TargetColdHeatRemovalPerSecond
        {
            get
            {
                if (targetColdHeatRemovalPerSecond < 0f)
                {
                    targetColdHeatRemovalPerSecond = Props.defaultColdHeatRemovalPerSecond;
                }
                return targetColdHeatRemovalPerSecond;
            }
            set => targetColdHeatRemovalPerSecond = Mathf.Clamp(value, 0f, Props.maxColdHeatRemovalPerSecond);
        }

        public float TorqueDemandFactor => TargetColdHeatRemovalPerSecond
            / Mathf.Max(0.01f, Props.defaultColdHeatRemovalPerSecond);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            mechanicalUser = parent.GetComp<CompMechanicalUser>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref targetColdHeatRemovalPerSecond, "targetColdHeatRemovalPerSecond", -1f);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();

            if (mechanicalUser == null || !mechanicalUser.HasPower)
            {
                return;
            }

            float transferPerRareTick = EnergyPerRareTick(TargetColdHeatRemovalPerSecond, OutputFactor);
            if (Mathf.Approximately(transferPerRareTick, 0f))
            {
                return;
            }

            float coldEnergyLimit = -transferPerRareTick;
            float hotEnergyLimit = transferPerRareTick * Props.heatDumpFactor;
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

        internal static float EnergyPerRareTick(float coldHeatRemovalPerSecond, float outputFactor)
        {
            // CompTickRare runs once per 250 game ticks. GenTemperature expects the
            // total energy for that invocation, as used by vanilla heaters/coolers.
            return Mathf.Max(0f, coldHeatRemovalPerSecond) * Mathf.Clamp01(outputFactor)
                * GenTicks.TickRareInterval / GenTicks.TicksPerRealSecond;
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

        private float OutputFactor
        {
            get
            {
                if (mechanicalUser == null || !mechanicalUser.HasPower)
                {
                    return 0f;
                }

                return Mathf.Clamp01(mechanicalUser.RealRPM
                    / Mathf.Max(1f, mechanicalUser.Props.recommendedRPM));
            }
        }

        public override string CompInspectStringExtra()
        {
            if (mechanicalUser == null)
            {
                return null;
            }

            float coldPerSecond = TargetColdHeatRemovalPerSecond * OutputFactor;
            float hotPerSecond = coldPerSecond * Props.heatDumpFactor;
            string coldRoom = SideRoomLabel(ColdSideCell);
            string hotRoom = SideRoomLabel(HotSideCell);
            return "HD_HeatPump_Inspect".Translate(
                TargetColdHeatRemovalPerSecond.ToString("F1"),
                coldPerSecond.ToString("F1"),
                hotPerSecond.ToString("F1"),
                coldRoom,
                hotRoom).Resolve();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            foreach (int change in new[] { -10, -1, 1, 10 })
            {
                int step = change;
                yield return new Command_Action
                {
                    action = () => TargetColdHeatRemovalPerSecond += step,
                    defaultLabel = (step > 0 ? "+" : "") + step + " W",
                    defaultDesc = "HD_HeatPump_AdjustCooling".Translate(step.ToString()),
                    icon = ContentFinder<Texture2D>.Get(
                        step < 0 ? "UI/Commands/TempLower" : "UI/Commands/TempRaise", true)
                };
            }
        }

        private string SideRoomLabel(IntVec3 cell)
        {
            if (!cell.InBounds(parent.Map))
            {
                return "HD_HeatPump_OutOfBounds".Translate().Resolve();
            }

            Room room = cell.GetRoom(parent.Map);
            if (room == null)
            {
                return "HD_HeatPump_NoRoom".Translate(
                    OutdoorTemperatureAt(cell).ToStringTemperature("F1")).Resolve();
            }

            if (room.UsesOutdoorTemperature)
            {
                return "HD_HeatPump_Outdoors".Translate(
                    OutdoorTemperatureAt(cell).ToStringTemperature("F1")).Resolve();
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
        public float defaultColdHeatRemovalPerSecond = 2f;
        public float maxColdHeatRemovalPerSecond = 80f;
        public float heatDumpFactor = 1.25f;
        public float outdoorSideEfficiencyMultiplier = 1f;
        public float coldSideTargetTemperature = -273.15f;
        public float hotSideTargetTemperature = 1000f;

        public CompProperties_MechanicalTemperatureControl()
        {
            compClass = typeof(CompMechanicalTemperatureControl);
        }
    }
}
