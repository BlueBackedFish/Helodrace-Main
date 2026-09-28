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
        public float maxRPM = 500f;
        public float lowestRPM = 50f;
        public float rpmInaccuracy = 10f;

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
            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalRpmRange", lowestRPM.ToString("F0") + " - " + maxRPM.ToString("F0") + " RPM", "HD_Stat_MechanicalRpmRange_Desc", 4);
            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalRpmInaccuracy", "±" + rpmInaccuracy.ToString("F0") + " RPM", "HD_Stat_MechanicalRpmInaccuracy_Desc", 5);
        }
    }

    public class CompProperties_MechanicalUser : CompProperties
    {
        public float powerConsumed = 0f;
        public float requireTorque = 100f;
        public float minimalRPM = 50f;
        public float recommendedRPM = 300f;
        public float defaultGearRatio = 1f;

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
            yield return MechanicalStatEntries.Entry("HD_Stat_MechanicalDefaultGearRatio", defaultGearRatio.ToString("F1") + "x", "HD_Stat_MechanicalDefaultGearRatio_Desc", 3);
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
        private float targetRPM = -1f;
        private float pendingTargetRPM = -1f;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            flickable = parent.GetComp<CompFlickable>();
            refuelable = parent.GetComp<CompRefuelable>();
            powerTrader = parent.GetComp<CompPowerTrader>();
        }

        public float TargetRPM
        {
            get
            {
                if (targetRPM < 0)
                {
                    targetRPM = Props.maxRPM;
                }
                return targetRPM;
            }
            set
            {
                targetRPM = UnityEngine.Mathf.Clamp(value, Props.lowestRPM, Props.maxRPM);
            }
        }

        public float PendingTargetRPM
        {
            get
            {
                if (pendingTargetRPM < 0)
                {
                    pendingTargetRPM = TargetRPM;
                }
                return pendingTargetRPM;
            }
            set
            {
                pendingTargetRPM = UnityEngine.Mathf.Clamp(value, Props.lowestRPM, Props.maxRPM);
            }
        }

        public bool WantsConfiguration => pendingTargetRPM >= 0 && !UnityEngine.Mathf.Approximately(pendingTargetRPM, TargetRPM);

        public void ApplyPendingTargetRPM()
        {
            if (WantsConfiguration)
            {
                TargetRPM = pendingTargetRPM;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref targetRPM, "targetRPM", -1f);
            Scribe_Values.Look(ref pendingTargetRPM, "pendingTargetRPM", -1f);
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
                float scaledOutput = (TargetRPM / Props.maxRPM) * Props.recommendedPower;
                return UnityEngine.Mathf.Clamp(scaledOutput, 0f, UnityEngine.Mathf.Max(0f, Props.maxPossiblePower));
            }
        }

        public float CurrentRPM
        {
            get
            {
                if (!IsProducingPower) return 0f;
                return TargetRPM;
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
                    Props.maxPossiblePower.ToString("F0"), 
                    CurrentRPM.ToString("F1"), 
                    Props.rpmInaccuracy.ToString("F0")
                );
            }

            if (WantsConfiguration)
            {
                str += "\n" + "HD_MechanicalEmitter_TargetSpeedPending".Translate(TargetRPM.ToString("F0"), PendingTargetRPM.ToString("F0"));
            }
            else
            {
                str += "\n" + "HD_MechanicalEmitter_TargetSpeed".Translate(TargetRPM.ToString("F0"));
            }

            return str;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (parent.Faction == Faction.OfPlayer)
            {
                yield return new Command_Action
                {
                    action = () => PendingTargetRPM -= 50f,
                    defaultLabel = "-50",
                    defaultDesc = "HD_Command_DecreaseRPM_Desc".Translate("50"),
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/TempLower", true)
                };
                yield return new Command_Action
                {
                    action = () => PendingTargetRPM -= 10f,
                    defaultLabel = "-10",
                    defaultDesc = "HD_Command_DecreaseRPM_Desc".Translate("10"),
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/TempLower", true)
                };
                yield return new Command_Action
                {
                    action = () => PendingTargetRPM += 10f,
                    defaultLabel = "+10",
                    defaultDesc = "HD_Command_IncreaseRPM_Desc".Translate("10"),
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/TempRaise", true)
                };
                yield return new Command_Action
                {
                    action = () => PendingTargetRPM += 50f,
                    defaultLabel = "+50",
                    defaultDesc = "HD_Command_IncreaseRPM_Desc".Translate("50"),
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/TempRaise", true)
                };
            }
        }
    }

    public class CompMechanicalUser : CompMechanicalNode
    {
        public CompProperties_MechanicalUser Props => (CompProperties_MechanicalUser)props;
        public bool HasPower { get; private set; }
        public CompFlickable Flickable { get; private set; }
        public CompMechanicalHazard Hazard { get; private set; }
        public CompMechanicalGenerator Generator { get; private set; }
        
        // This holds the actual ratio of torque the network was able to provide (1.0 = fully powered, <1.0 = overloaded)
        public float TorqueFulfillmentRatio { get; private set; } = 0f;

        private float gearRatio = -1f;
        private float pendingGearRatio = -1f;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Flickable = parent.GetComp<CompFlickable>();
            Hazard = parent.GetComp<CompMechanicalHazard>();
            Generator = parent.GetComp<CompMechanicalGenerator>();
        }

        public float GearRatio
        {
            get
            {
                if (gearRatio < 0)
                {
                    gearRatio = Props.defaultGearRatio;
                }
                return gearRatio;
            }
            set
            {
                gearRatio = UnityEngine.Mathf.Max(0.1f, value);
            }
        }

        public float PendingGearRatio
        {
            get
            {
                if (pendingGearRatio < 0)
                {
                    pendingGearRatio = GearRatio;
                }
                return pendingGearRatio;
            }
            set
            {
                pendingGearRatio = UnityEngine.Mathf.Max(0.1f, value);
            }
        }

        public bool WantsConfiguration => pendingGearRatio >= 0 && !UnityEngine.Mathf.Approximately(pendingGearRatio, GearRatio);
        
        public void ApplyPendingGearRatio()
        {
            if (WantsConfiguration)
            {
                GearRatio = pendingGearRatio;
            }
        }

        // If the grid can't provide full torque, the user's RPM physically bogs down proportional to the missing torque.
        public float RealRPM => Network != null ? (Network.GridRPM / GearRatio) * TorqueFulfillmentRatio : 0f;
        public float RealInaccuracy => Network != null ? Network.GridInaccuracy / GearRatio : 0f;
        public float RealRpmError => Network != null ? Network.GridRpmError / GearRatio : 0f;
        
        // Power = Torque * RPM. If RealRPM == recommendedRPM, this demands exactly requireTorque.
        public float GridTorqueDemanded 
        {
            get
            {
                if (Network == null || Props.recommendedRPM <= 0) return 0f;
                
                // Calculate based on what the RPM *would* be before any overload torque drops
                float intendedRpm = Network.GridRPM / GearRatio;
                return Props.requireTorque * (intendedRpm / Props.recommendedRPM);
            }
        }
        
        // Effective RPM caps at recommendedRPM. Used by other systems to calculate actual work speed.
        public float EffectiveRPM => UnityEngine.Mathf.Min(RealRPM, Props.recommendedRPM);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref gearRatio, "gearRatio", -1f);
            Scribe_Values.Look(ref pendingGearRatio, "pendingGearRatio", -1f);
        }

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

            if (WantsConfiguration)
            {
                str += "HD_MechanicalUser_GearRatioPending".Translate(GearRatio.ToString("F1"), PendingGearRatio.ToString("F1")).Resolve() + "\n";
            }
            else
            {
                str += "HD_MechanicalUser_GearRatio".Translate(GearRatio.ToString("F1")).Resolve() + "\n";
            }

            string opStatus = HasPower ? "HD_MechanicalUser_Operating".Translate().Resolve() : "HD_MechanicalUser_Stalled".Translate().Resolve();

            str += "HD_MechanicalUser_SpeedInfo".Translate(
                RealRPM.ToString("F1"),
                RealInaccuracy.ToString("F1"),
                (HasPower ? EffectiveRPM : 0f).ToString("F1"),
                opStatus
            ).Resolve();

            return str;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (parent.Faction == Faction.OfPlayer)
            {
                yield return new Command_Action
                {
                    action = () => PendingGearRatio -= 1f,
                    defaultLabel = "-1.0",
                    defaultDesc = "HD_Command_DecreaseGear_Desc".Translate("1.0"),
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/TempLower", true)
                };
                yield return new Command_Action
                {
                    action = () => PendingGearRatio -= 0.1f,
                    defaultLabel = "-0.1",
                    defaultDesc = "HD_Command_DecreaseGear_Desc".Translate("0.1"),
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/TempLower", true)
                };
                yield return new Command_Action
                {
                    action = () => PendingGearRatio += 0.1f,
                    defaultLabel = "+0.1",
                    defaultDesc = "HD_Command_IncreaseGear_Desc".Translate("0.1"),
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/TempRaise", true)
                };
                yield return new Command_Action
                {
                    action = () => PendingGearRatio += 1f,
                    defaultLabel = "+1.0",
                    defaultDesc = "HD_Command_IncreaseGear_Desc".Translate("1.0"),
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/TempRaise", true)
                };
            }
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
        public bool HasRpmMismatch = false;

        // Aggregated network stats
        public float GridRPM = 0f;
        public float GridInaccuracy = 0f;
        public float GridRpmError = 0f;

        public void UpdateNetwork()
        {
            CurrentPowerOutput = 0f;
            CurrentMaximumPower = 0f;
            CurrentPowerNeeded = 0f;
            GridRPM = 0f;
            GridInaccuracy = 0f;
            GridRpmError = 0f;
            HasRpmMismatch = false;

            List<CompMechanicalEmitter> activeEmitters = activeEmittersBuffer;
            activeEmitters.Clear();
            float normalPowerAvailable = 0f;
            float maximumPowerAvailable = 0f;
            float totalEmitterRpm = 0f;
            float minimumEmitterRpm = float.MaxValue;
            float maximumEmitterRpm = float.MinValue;

            // 1. Check Power Sources First
            foreach (var node in nodes)
            {
                if (node is CompMechanicalEmitter emitter && emitter.IsProducingPower)
                {
                    activeEmitters.Add(emitter);
                }
            }

            // 2. Detect RPM mismatch in linear time by comparing each source's
            // tolerance interval against the extreme intervals of the other sources.
            if (activeEmitters.Count > 0)
            {
                if (activeEmitters.Count > 1)
                {
                    float smallestUpper = float.MaxValue;
                    float secondSmallestUpper = float.MaxValue;
                    int smallestUpperIndex = -1;
                    float largestLower = float.MinValue;
                    float secondLargestLower = float.MinValue;
                    int largestLowerIndex = -1;

                    for (int i = 0; i < activeEmitters.Count; i++)
                    {
                        CompMechanicalEmitter emitter = activeEmitters[i];
                        float tolerance = UnityEngine.Mathf.Max(0f, emitter.Props.rpmInaccuracy);
                        float lower = emitter.TargetRPM - tolerance;
                        float upper = emitter.TargetRPM + tolerance;

                        if (upper < smallestUpper)
                        {
                            secondSmallestUpper = smallestUpper;
                            smallestUpper = upper;
                            smallestUpperIndex = i;
                        }
                        else if (upper < secondSmallestUpper)
                        {
                            secondSmallestUpper = upper;
                        }

                        if (lower > largestLower)
                        {
                            secondLargestLower = largestLower;
                            largestLower = lower;
                            largestLowerIndex = i;
                        }
                        else if (lower > secondLargestLower)
                        {
                            secondLargestLower = lower;
                        }
                    }

                    for (int i = 0; i < activeEmitters.Count; i++)
                    {
                        CompMechanicalEmitter emitter = activeEmitters[i];
                        float tolerance = UnityEngine.Mathf.Max(0f, emitter.Props.rpmInaccuracy);
                        float lower = emitter.TargetRPM - tolerance;
                        float upper = emitter.TargetRPM + tolerance;
                        float otherSmallestUpper = i == smallestUpperIndex
                            ? secondSmallestUpper
                            : smallestUpper;
                        float otherLargestLower = i == largestLowerIndex
                            ? secondLargestLower
                            : largestLower;

                        bool emitterMismatched = lower > otherSmallestUpper
                            || upper < otherLargestLower;
                        if (emitterMismatched)
                        {
                            HasRpmMismatch = true;
                            ApplyMismatchEffect(emitter);
                        }
                    }
                }

                // 3. Gather every source value before deciding the final grid output.
                foreach (var emitter in activeEmitters)
                {
                    float emitterMaximum = UnityEngine.Mathf.Max(0f, emitter.Props.maxPossiblePower);
                    float emitterNormal = UnityEngine.Mathf.Clamp(emitter.PowerOutput, 0f, emitterMaximum);

                    normalPowerAvailable += emitterNormal;
                    maximumPowerAvailable += emitterMaximum;
                    totalEmitterRpm += emitter.TargetRPM;
                    minimumEmitterRpm = UnityEngine.Mathf.Min(minimumEmitterRpm, emitter.TargetRPM);
                    maximumEmitterRpm = UnityEngine.Mathf.Max(maximumEmitterRpm, emitter.TargetRPM);
                    GridInaccuracy += UnityEngine.Mathf.Max(0f, emitter.Props.rpmInaccuracy);

                    if (emitter.TargetRPM > GridRPM)
                    {
                        GridRPM = emitter.TargetRPM;
                    }
                }

                GridRpmError = activeEmitters.Count > 1
                    ? maximumEmitterRpm - minimumEmitterRpm
                    : 0f;

                if (HasRpmMismatch)
                {
                    // Average RPM, cut under 10 (floor to nearest 10)
                    float averageRpm = totalEmitterRpm / activeEmitters.Count;
                    GridRPM = UnityEngine.Mathf.Floor(averageRpm / 10f) * 10f;

                    // Double inaccuracy
                    GridInaccuracy *= 2f;

                    // Both normal and maximum combined output receive the same
                    // mismatch penalty before the final cap is calculated.
                    normalPowerAvailable = UnityEngine.Mathf.Floor(
                        normalPowerAvailable * (2f / 3f) / 10f) * 10f;
                    maximumPowerAvailable = UnityEngine.Mathf.Floor(
                        maximumPowerAvailable * (2f / 3f) / 10f) * 10f;
                }
            }

            maximumPowerAvailable = UnityEngine.Mathf.Max(0f, maximumPowerAvailable);
            normalPowerAvailable = UnityEngine.Mathf.Clamp(
                normalPowerAvailable, 0f, maximumPowerAvailable);
            CurrentMaximumPower = maximumPowerAvailable;

            // 4. Gather every active user's demand.
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

            // 5. Final output calculation. This is the only stage that assigns
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
                            user.RealRpmError,
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

        private void ApplyMismatchEffect(CompMechanicalEmitter emitter)
        {
            // Source-specific building damage remains separate from the simplified
            // user-machine hazard component.
            if (Verse.Rand.Chance(0.05f))
            {
                switch (emitter.Props.sourceType)
                {
                    case PowerSourceType.SteamEngine:
                        emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Crush, 15f));
                        FleckMaker.ThrowDustPuff(emitter.parent.DrawPos, emitter.parent.Map, 1.5f);
                        break;
                    case PowerSourceType.Engine:
                        emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Burn, 10f));
                        FireUtility.TryStartFireIn(emitter.parent.Position, emitter.parent.Map, 0.5f, null);
                        break;
                    case PowerSourceType.Motor:
                        FleckMaker.ThrowMicroSparks(emitter.parent.DrawPos, emitter.parent.Map);
                        GenExplosion.DoExplosion(
                            emitter.parent.Position,
                            emitter.parent.Map,
                            1.5f,
                            DamageDefOf.EMP,
                            emitter.parent);
                        break;
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
