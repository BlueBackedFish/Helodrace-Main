using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace
{
    // --- PROPERTIES ---

    public class CompProperties_MechanicalTransmitter : CompProperties
    {
        public CompProperties_MechanicalTransmitter()
        {
            this.compClass = typeof(CompMechanicalTransmitter);
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
        {
            foreach (StatDrawEntry stat in base.SpecialDisplayStats(req))
            {
                yield return stat;
            }

            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalRole", "HD_Stat_MechanicalRole_Transmitter".Translate().Resolve(), "HD_Stat_MechanicalRole_Transmitter_Desc");
        }
    }

    public enum PowerSourceType
    {
        SteamEngine,
        Engine,
        Motor
    }

    internal static class MechanicalStatEntries
    {
        public const int DisplayPriority = 5500;

        public static StatDrawEntry Entry(string labelKey, string value, string reportKey, int priorityOffset = 0)
        {
            return new StatDrawEntry(
                StatCategoryDefOf.Building,
                labelKey.Translate().Resolve(),
                value,
                reportKey.Translate().Resolve(),
                DisplayPriority - priorityOffset
            );
        }
    }

    public class CompProperties_MechanicalEmitter : CompProperties
    {
        public PowerSourceType sourceType = PowerSourceType.SteamEngine;
        public float maxPossiblePower = 1000f;
        public float recommendedPower = 800f;
        public float operatingRPM = 500f;
        public float lowestRPM = 50f;

        public CompProperties_MechanicalEmitter()
        {
            this.compClass = typeof(CompMechanicalEmitter);
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
        {
            foreach (StatDrawEntry stat in base.SpecialDisplayStats(req))
            {
                yield return stat;
            }

            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalRole", "HD_Stat_MechanicalRole_Emitter".Translate().Resolve(), "HD_Stat_MechanicalRole_Emitter_Desc");
            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalSourceType", sourceType.ToString(), "HD_Stat_MechanicalSourceType_Desc", 1);
            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalRecommendedPower", recommendedPower.ToString("F0") + " W", "HD_Stat_MechanicalRecommendedPower_Desc", 2);
            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalMaxPower", maxPossiblePower.ToString("F0") + " W", "HD_Stat_MechanicalMaxPower_Desc", 3);
        }
    }

    public class CompProperties_MechanicalUser : CompProperties
    {
        public float powerConsumed = 0f;
        public float requireTorque = 100f;
        public float minimalRPM = 50f;
        public float recommendedRPM = 300f;

        public CompProperties_MechanicalUser()
        {
            this.compClass = typeof(CompMechanicalUser);
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
        {
            foreach (StatDrawEntry stat in base.SpecialDisplayStats(req))
            {
                yield return stat;
            }

            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalRole", "HD_Stat_MechanicalRole_User".Translate().Resolve(), "HD_Stat_MechanicalRole_User_Desc");
            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalRequiredTorque", requireTorque.ToString("F0"), "HD_Stat_MechanicalRequiredTorque_Desc", 1);
            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalRpmRequirement", minimalRPM.ToString("F0") + " - " + recommendedRPM.ToString("F0") + " RPM", "HD_Stat_MechanicalRpmRequirement_Desc", 2);
        }
    }


    // --- COMPONENTS ---

    public abstract class CompMechanicalNode : ThingComp
    {
        public MechanicalNetwork Network { get; set; }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Map.GetComponent<MechanicalNetworkManager>().RegisterNode(this);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (previousMap != null)
            {
                previousMap.GetComponent<MechanicalNetworkManager>()?.DeregisterNode(this);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            map?.GetComponent<MechanicalNetworkManager>()?.DeregisterNode(this);
        }
    }

    public class CompMechanicalTransmitter : CompMechanicalNode
    {
        // Line shafts
    }

    public class CompMechanicalEmitter : CompMechanicalNode
    {
        public CompProperties_MechanicalEmitter Props => (CompProperties_MechanicalEmitter)props;

        private CompFlickable flickable;
        private CompRefuelable refuelable;
        private CompPowerTrader powerTrader;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            flickable = parent.GetComp<CompFlickable>();
            refuelable = parent.GetComp<CompRefuelable>();
            powerTrader = parent.GetComp<CompPowerTrader>();
        }

        public bool IsProducingPower
        {
            get
            {
                if (flickable != null && !flickable.SwitchIsOn) return false;

                if (refuelable != null && !refuelable.HasFuel) return false;

                if (powerTrader != null && !powerTrader.PowerOn) return false;

                return true;
            }
        }

        public float PowerOutput
        {
            get
            {
                if (!IsProducingPower) return 0f;
                return UnityEngine.Mathf.Clamp(Props.recommendedPower, 0f, UnityEngine.Mathf.Max(0f, Props.maxPossiblePower));
            }
        }

        public override string CompInspectStringExtra()
        {
            string str = "";
            if (!IsProducingPower)
            {
                str = "HD_MechanicalEmitter_Off".Translate(Props.sourceType.ToString());
            }
            else
            {
                str = "HD_MechanicalEmitter_On".Translate(
                    Props.sourceType.ToString(), 
                    PowerOutput.ToString("F0"), 
                    Props.maxPossiblePower.ToString("F0")
                );
            }
            return str;
        }
    }

    public class CompMechanicalUser : CompMechanicalNode
    {
        public CompProperties_MechanicalUser Props => (CompProperties_MechanicalUser)props;
        public bool HasPower { get; private set; }
        public CompFlickable Flickable { get; private set; }
        public CompMechanicalHazard Hazard { get; private set; }
        public CompMechanicalGenerator Generator { get; private set; }
        public CompMechanicalTemperatureControl TemperatureControl { get; private set; }
        
        // This holds the actual ratio of torque the network was able to provide (1.0 = fully powered, <1.0 = overloaded)
        public float TorqueFulfillmentRatio { get; private set; } = 0f;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Flickable = parent.GetComp<CompFlickable>();
            Hazard = parent.GetComp<CompMechanicalHazard>();
            Generator = parent.GetComp<CompMechanicalGenerator>();
            TemperatureControl = parent.GetComp<CompMechanicalTemperatureControl>();
        }

        // The transmission matches each machine's rated speed to the unloaded shaft.
        // Under overload, GridRPM falls while this ratio stays fixed for the update.
        private float AutomaticGearRatio => Network != null && Network.NominalGridRPM > 0f && Props.recommendedRPM > 0f
            ? Network.NominalGridRPM / Props.recommendedRPM
            : 1f;

        public float RealRPM => Network != null ? Network.GridRPM / AutomaticGearRatio : 0f;

        public float GridTorqueDemanded => Network != null
            ? UnityEngine.Mathf.Max(0f, Props.requireTorque)
                * (TemperatureControl?.TorqueDemandFactor ?? 1f)
            : 0f;
        
        // Effective RPM caps at recommendedRPM. Used by other systems to calculate actual work speed.
        public float EffectiveRPM => UnityEngine.Mathf.Min(RealRPM, Props.recommendedRPM);

        public void UpdatePowerStatus(bool isPowered, float fulfillmentRatio)
        {
            HasPower = isPowered;
            TorqueFulfillmentRatio = fulfillmentRatio;
        }

        public override string CompInspectStringExtra()
        {
            string torqueStatus = TorqueFulfillmentRatio < 1.0f && TorqueFulfillmentRatio > 0f 
                ? "HD_MechanicalUser_TorqueStruggle".Translate((TorqueFulfillmentRatio*100).ToString("F0")).Resolve()
                : "";

            string str = "HD_MechanicalUser_Info".Translate(
                Props.requireTorque.ToString("F0"),
                GridTorqueDemanded.ToString("F1"),
                torqueStatus,
                Props.minimalRPM.ToString("F0"),
                Props.recommendedRPM.ToString("F0"),
                Hazard != null
                    ? "HD_MechanicalUser_DangerConfigured".Translate().Resolve()
                    : "HD_MechanicalUser_DangerNone".Translate().Resolve()
            ).Resolve() + "\n";

            string opStatus = HasPower ? "HD_MechanicalUser_Operating".Translate().Resolve() : "HD_MechanicalUser_Stalled".Translate().Resolve();

            str += "HD_MechanicalUser_SpeedInfo".Translate(
                RealRPM.ToString("F1"),
                (HasPower ? EffectiveRPM : 0f).ToString("F1"),
                opStatus
            ).Resolve();

            return str;
        }

    }

    // --- NETWORK LOGIC ---

    public class MechanicalNetwork
    {
        public List<CompMechanicalNode> nodes = new List<CompMechanicalNode>();
        private readonly List<CompMechanicalEmitter> activeEmittersBuffer =
            new List<CompMechanicalEmitter>();
        public float CurrentPowerOutput = 0f;
        public float CurrentMaximumPower = 0f;
        public float CurrentPowerNeeded = 0f;

        // Aggregated network stats
        public float GridRPM = 0f;
        public float NominalGridRPM = 0f;

        public void UpdateNetwork()
        {
            CurrentPowerOutput = 0f;
            CurrentMaximumPower = 0f;
            CurrentPowerNeeded = 0f;
            GridRPM = 0f;
            NominalGridRPM = 0f;

            List<CompMechanicalEmitter> activeEmitters = activeEmittersBuffer;
            activeEmitters.Clear();
            float normalPowerAvailable = 0f;
            float maximumPowerAvailable = 0f;

            // Find active power sources.
            foreach (var node in nodes)
            {
                if (node is CompMechanicalEmitter emitter && emitter.IsProducingPower)
                {
                    activeEmitters.Add(emitter);
                }
            }

            // Combine active sources. Automatic gearing lets users follow the
            // fastest shaft while each source contributes its available power.
            foreach (var emitter in activeEmitters)
            {
                float emitterMaximum = UnityEngine.Mathf.Max(0f, emitter.Props.maxPossiblePower);
                float emitterNormal = UnityEngine.Mathf.Clamp(emitter.PowerOutput, 0f, emitterMaximum);

                normalPowerAvailable += emitterNormal;
                maximumPowerAvailable += emitterMaximum;
                GridRPM = UnityEngine.Mathf.Max(GridRPM, emitter.Props.operatingRPM);
            }

            maximumPowerAvailable = UnityEngine.Mathf.Max(0f, maximumPowerAvailable);
            normalPowerAvailable = UnityEngine.Mathf.Clamp(
                normalPowerAvailable, 0f, maximumPowerAvailable);
            CurrentMaximumPower = maximumPowerAvailable;
            NominalGridRPM = GridRPM;

            // Gather every active user's demand.
            foreach (var node in nodes)
            {
                if (node is CompMechanicalUser user)
                {
                    CompFlickable flickable = user.Flickable;
                    if (flickable == null || flickable.SwitchIsOn)
                    {
                        CurrentPowerNeeded += user.GridTorqueDemanded;
                    }
                }
            }

            // Final output calculation. This is the only stage that assigns
            // CurrentPowerOutput, and it is capped again unconditionally below.
            float torqueFulfillment = 1.0f;
            CurrentPowerOutput = normalPowerAvailable;

            if (CurrentPowerNeeded > normalPowerAvailable && maximumPowerAvailable > 0f)
            {
                CurrentPowerOutput = UnityEngine.Mathf.Min(CurrentPowerNeeded, maximumPowerAvailable);

                if (CurrentPowerNeeded > maximumPowerAvailable)
                {
                    // Engines pushed beyond max limits!
                    // The ratio of how much torque we have versus what we actually need
                    torqueFulfillment = maximumPowerAvailable / CurrentPowerNeeded;
                    
                    // RPM drops drastically because it can't handle the load
                    GridRPM *= torqueFulfillment;

                    // Power sources retain their original overload maintenance
                    // and building-damage behavior.
                    if (Verse.Rand.Chance(0.20f))
                    {
                        foreach (var emitter in activeEmitters)
                        {
                            ApplyOverloadEffect(emitter, GridRPM);
                        }
                    }
                }
            }

            // Guard against malformed Def values, floating-point drift, and future
            // calculation branches bypassing the overload block.
            CurrentPowerOutput = UnityEngine.Mathf.Clamp(
                CurrentPowerOutput, 0f, CurrentMaximumPower);

            bool gridHasEnoughTorque = CurrentPowerOutput > 0;

            // Update users
            foreach (var node in nodes)
            {
                if (node is CompMechanicalUser user)
                {
                    CompFlickable flickable = user.Flickable;
                    bool isSwitchedOn = flickable == null || flickable.SwitchIsOn;
                    
                    // Update user with the torque ratio before checking RPM
                    user.UpdatePowerStatus(gridHasEnoughTorque && isSwitchedOn, torqueFulfillment);
                    
                    bool meetsRpm = user.RealRPM >= user.Props.minimalRPM;
                    
                    // Below minimum RPM the machine stops completely and cannot
                    // cause a low-speed operating accident.
                    if (!meetsRpm)
                    {
                        user.UpdatePowerStatus(false, 0f);
                    }
                    else
                    {
                        // The dangerous operating band exists only when minimum
                        // and recommended RPM differ.
                        float dangerousBelowRpm =
                            user.Props.minimalRPM < user.Props.recommendedRPM
                                ? user.Props.recommendedRPM
                                : -1f;
                        user.Hazard?.Evaluate(
                            user.RealRPM,
                            user.HasPower,
                            dangerousBelowRpm);
                    }

                    // Use the final network state after torque and minimum-RPM
                    // checks, so electrical output cannot lead the mechanical
                    // calculation by one update.
                    user.Generator?.RefreshOutput();
                }
            }
        }

        private void ApplyOverloadEffect(CompMechanicalEmitter emitter, float currentGridRpm)
        {
            switch (emitter.Props.sourceType)
            {
                case PowerSourceType.SteamEngine:
                    if (currentGridRpm < emitter.Props.lowestRPM)
                    {
                        emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Crush, 25f));
                        FleckMaker.ThrowDustPuff(emitter.parent.DrawPos, emitter.parent.Map, 2.0f);
                        if (Verse.Rand.Chance(0.1f))
                        {
                            Messages.Message(
                                "Steam engine suffered catastrophic structural damage due to low-RPM high-torque overload!",
                                emitter.parent,
                                MessageTypeDefOf.NegativeEvent);
                            emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Crush, 300f));
                        }
                    }
                    break;
                case PowerSourceType.Engine:
                    CompBreakdownable breakdown = emitter.parent.GetComp<CompBreakdownable>();
                    if (breakdown != null && !breakdown.BrokenDown)
                    {
                        breakdown.DoBreakdown();
                        Messages.Message(
                            "Combustion engine broke down from mechanical overload!",
                            emitter.parent,
                            MessageTypeDefOf.NegativeEvent);
                    }
                    else
                    {
                        emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, 15f));
                        FleckMaker.ThrowSmoke(emitter.parent.DrawPos, emitter.parent.Map, 1.5f);
                    }
                    break;
                case PowerSourceType.Motor:
                    CompPowerTrader powerTrader = emitter.parent.GetComp<CompPowerTrader>();
                    bool explodedBattery = false;
                    if (powerTrader?.PowerNet != null)
                    {
                        foreach (CompPowerBattery battery in powerTrader.PowerNet.batteryComps)
                        {
                            if (battery.StoredEnergy <= 0f)
                            {
                                continue;
                            }

                            battery.SetStoredEnergyPct(0f);
                            GenExplosion.DoExplosion(
                                battery.parent.Position,
                                battery.parent.Map,
                                2.9f,
                                DamageDefOf.Flame,
                                null);
                            explodedBattery = true;
                            Messages.Message(
                                "Electric motor overload caused a battery to violently discharge!",
                                battery.parent,
                                MessageTypeDefOf.NegativeEvent);
                            break;
                        }
                    }

                    if (!explodedBattery)
                    {
                        GenExplosion.DoExplosion(
                            emitter.parent.Position,
                            emitter.parent.Map,
                            1.9f,
                            DamageDefOf.EMP,
                            null);
                        emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Burn, 20f));
                        Messages.Message(
                            "Electric motor short-circuited under heavy mechanical load!",
                            emitter.parent,
                            MessageTypeDefOf.NegativeEvent);
                    }
                    break;
            }
        }

    }

    public class MechanicalNetworkManager : MapComponent
    {
        private HashSet<CompMechanicalNode> allNodes = new HashSet<CompMechanicalNode>();
        private List<MechanicalNetwork> networks = new List<MechanicalNetwork>();
        private bool isDirty = true;

        public MechanicalNetworkManager(Map map) : base(map) { }

        public void RegisterNode(CompMechanicalNode node)
        {
            if (node?.parent == null || !node.parent.Spawned || node.parent.Map != map)
            {
                return;
            }

            if (allNodes.Add(node))
            {
                isDirty = true;
            }
        }

        public void DeregisterNode(CompMechanicalNode node)
        {
            if (node != null)
            {
                node.Network = null;
            }

            if (node != null && allNodes.Remove(node))
            {
                isDirty = true;
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            // Old versions only deregistered on destruction. Minification,
            // transfer and some debug clear paths can despawn without destroying,
            // leaving nodes and whole networks ticking forever in a long save.
            if (allNodes.Count > 0 && map.IsHashIntervalTick(GenTicks.TickRareInterval))
            {
                int removed = allNodes.RemoveWhere(node =>
                    node?.parent == null
                    || !node.parent.Spawned
                    || node.parent.Map != map);
                if (removed > 0)
                {
                    isDirty = true;
                }
            }

            if (allNodes.Count == 0)
            {
                if (networks.Count > 0)
                {
                    networks.Clear();
                }
                isDirty = false;
                return;
            }

            if (isDirty)
            {
                RebuildNetworks();
                isDirty = false;
            }

            if (Find.TickManager.TicksGame % 60 == 0)
            {
                foreach (var net in networks)
                {
                    net.UpdateNetwork();
                }
            }
        }

        private void RebuildNetworks()
        {
            allNodes.RemoveWhere(node =>
                node?.parent == null
                || !node.parent.Spawned
                || node.parent.Map != map);
            networks.Clear();
            foreach (var node in allNodes)
            {
                node.Network = null;
            }

            foreach (var node in allNodes)
            {
                if (node.Network != null) continue;

                MechanicalNetwork newNet = new MechanicalNetwork();
                networks.Add(newNet);

                Queue<CompMechanicalNode> queue = new Queue<CompMechanicalNode>();
                queue.Enqueue(node);
                node.Network = newNet;
                newNet.nodes.Add(node);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();

                    // Include occupied cells too so floor-level transmitters can connect to
                    // wall-mounted or overlaid machines such as heat pumps.
                    foreach (var cell in current.parent.OccupiedRect().ExpandedBy(1).Cells)
                    {
                        if (!cell.InBounds(map)) continue;
                        
                        var neighborThings = cell.GetThingList(map);
                        foreach (var thing in neighborThings)
                        {
                            if (thing == current.parent) continue;

                            var neighborNode = thing.TryGetComp<CompMechanicalNode>();
                            if (neighborNode != null && neighborNode.Network == null)
                            {
                                neighborNode.Network = newNet;
                                newNet.nodes.Add(neighborNode);
                                queue.Enqueue(neighborNode);
                            }
                        }
                    }
                }
            }
        }
    }
}
