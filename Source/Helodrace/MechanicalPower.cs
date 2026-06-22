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
    }

    public enum PowerSourceType
    {
        SteamEngine,
        Engine,
        Motor
    }

    public enum MechanicalDangerType
    {
        Belt,
        Rotary,
        Saw,
        Fixed,
        Shatter,
        Kickback,
        Friction
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
    }

    public class CompProperties_MechanicalUser : CompProperties
    {
        public float powerConsumed = 0f;
        public float requireTorque = 100f;
        public float minimalRPM = 50f;
        public float recommendedRPM = 300f;
        public float defaultGearRatio = 1f;
        public List<MechanicalDangerType> dangerTypes = new List<MechanicalDangerType>();
        
        // Belt Hazard Specifics
        public float beltSafeRPM = 200f;
        public float beltDamageRadius = 1.9f;
        public int beltDamageAmount = 15;

        public CompProperties_MechanicalUser()
        {
            this.compClass = typeof(CompMechanicalUser);
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
    }

    public class CompMechanicalTransmitter : CompMechanicalNode
    {
        // Line shafts
    }

    public class CompMechanicalEmitter : CompMechanicalNode
    {
        public CompProperties_MechanicalEmitter Props => (CompProperties_MechanicalEmitter)props;
        
        private float targetRPM = -1f;
        private float pendingTargetRPM = -1f;

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
                var flickable = parent.GetComp<CompFlickable>();
                if (flickable != null && !flickable.SwitchIsOn) return false;

                var refuelable = parent.GetComp<CompRefuelable>();
                if (refuelable != null && !refuelable.HasFuel) return false;

                return true;
            }
        }

        public float PowerOutput
        {
            get
            {
                if (!IsProducingPower) return 0f;
                return (TargetRPM / Props.maxRPM) * Props.recommendedPower;
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
        
        // This holds the actual ratio of torque the network was able to provide (1.0 = fully powered, <1.0 = overloaded)
        public float TorqueFulfillmentRatio { get; private set; } = 0f;

        private float gearRatio = -1f;
        private float pendingGearRatio = -1f;

        // Belt hazard specifics
        public bool isLubricated = false;
        public float lubeMtbHours = 0f;
        public bool isBeltBroken = false;

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
        
        public bool WantsLubrication => Props.dangerTypes != null && Props.dangerTypes.Contains(MechanicalDangerType.Belt) && !isBeltBroken && !isLubricated;
        public bool WantsBeltRepair => isBeltBroken;

        public void ApplyPendingGearRatio()
        {
            if (WantsConfiguration)
            {
                GearRatio = pendingGearRatio;
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (Props.dangerTypes != null && Props.dangerTypes.Contains(MechanicalDangerType.Belt))
            {
                if (isBeltBroken) return;

                if (isLubricated)
                {
                    if (HasPower && RealRPM > 0f)
                    {
                        // Expiration MTB check. 2500 ticks = 1 in-game hour.
                        if (Rand.MTBEventOccurs(lubeMtbHours, 2500f, 1f))
                        {
                            isLubricated = false;
                            Messages.Message(parent.Label + " drive belt has dried up and needs lubrication.", parent, MessageTypeDefOf.NeutralEvent);
                        }
                    }
                }
                else if (HasPower && RealRPM > Props.beltSafeRPM)
                {
                    // MTB event for snapping. ~2 hours MTB if running continuously over safe RPM.
                    if (Rand.MTBEventOccurs(2f, 2500f, 1f))
                    {
                        SnapBelt();
                    }
                }
            }
        }

        private void SnapBelt()
        {
            isBeltBroken = true;
            GenExplosion.DoExplosion(parent.Position, parent.Map, Props.beltDamageRadius, DamageDefOf.Cut, parent, Props.beltDamageAmount);
            Messages.Message(parent.Label + " drive belt snapped violently due to lack of lubrication!", parent, MessageTypeDefOf.NegativeEvent);
            UpdatePowerStatus(false, 0f);
        }

        public void Lubricate(Thing slush)
        {
            float baseMtb = 6f; // default fallback
            
            var lubeComp = slush.TryGetComp<CompLubricant>();
            if (lubeComp != null)
            {
                baseMtb = lubeComp.Props.lubeMtbHours;
            }
            
            isLubricated = true;
            lubeMtbHours = baseMtb;
            slush.SplitOff(1).Destroy();
        }

        public void RepairBelt(Thing parts)
        {
            isBeltBroken = false;
            isLubricated = false;
            lubeMtbHours = 0f;
            if (parts != null)
            {
                parts.SplitOff(5).Destroy(); // Assumes 5 steel or cloth
            }
        }

        // If the grid can't provide full torque, the user's RPM physically bogs down proportional to the missing torque.
        public float RealRPM => (Network != null && !isBeltBroken) ? (Network.GridRPM / GearRatio) * TorqueFulfillmentRatio : 0f;
        public float RealInaccuracy => (Network != null && !isBeltBroken) ? Network.GridInaccuracy / GearRatio : 0f;
        
        // Power = Torque * RPM. If RealRPM == recommendedRPM, this demands exactly requireTorque.
        public float GridTorqueDemanded 
        {
            get
            {
                if (Network == null || Props.recommendedRPM <= 0 || isBeltBroken) return 0f;
                
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
            Scribe_Values.Look(ref isLubricated, "isLubricated", false);
            Scribe_Values.Look(ref lubeMtbHours, "lubeMtbHours", 0f);
            Scribe_Values.Look(ref isBeltBroken, "isBeltBroken", false);
        }

        public void UpdatePowerStatus(bool isPowered, float fulfillmentRatio)
        {
            HasPower = isPowered && !isBeltBroken;
            TorqueFulfillmentRatio = fulfillmentRatio;
        }

        public override string CompInspectStringExtra()
        {
            string torqueStatus = TorqueFulfillmentRatio < 1.0f && TorqueFulfillmentRatio > 0f 
                ? "HD_MechanicalUser_TorqueStruggle".Translate((TorqueFulfillmentRatio*100).ToString("F0")).Resolve()
                : "";

            string dangerStr = (Props.dangerTypes != null && Props.dangerTypes.Count > 0) 
                ? string.Join(", ", Props.dangerTypes) 
                : "HD_MechanicalUser_DangerNone".Translate().Resolve();

            string str = "HD_MechanicalUser_Info".Translate(
                Props.requireTorque.ToString("F0"),
                GridTorqueDemanded.ToString("F1"),
                torqueStatus,
                Props.minimalRPM.ToString("F0"),
                Props.recommendedRPM.ToString("F0"),
                dangerStr
            ).Resolve() + "\n";

            if (Props.dangerTypes != null && Props.dangerTypes.Contains(MechanicalDangerType.Belt))
            {
                if (isBeltBroken)
                {
                    str += "HD_MechanicalUser_BeltSnapped".Translate().Resolve() + "\n";
                }
                else if (!isLubricated)
                {
                    str += "HD_MechanicalUser_BeltDry".Translate().Resolve() + "\n";
                }
                else
                {
                    str += "HD_MechanicalUser_BeltLubricated".Translate(lubeMtbHours.ToString("F0")).Resolve() + "\n";
                }
            }

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
        public float CurrentPowerOutput = 0f;
        public float CurrentPowerNeeded = 0f;
        public bool HasRpmMismatch = false;

        // Aggregated network stats
        public float GridRPM = 0f;
        public float GridInaccuracy = 0f;

        public void UpdateNetwork()
        {
            CurrentPowerOutput = 0f;
            CurrentPowerNeeded = 0f;
            GridRPM = 0f;
            GridInaccuracy = 0f;
            HasRpmMismatch = false;

            List<CompMechanicalEmitter> activeEmitters = new List<CompMechanicalEmitter>();

            // 1. Check Power Sources First
            foreach (var node in nodes)
            {
                if (node is CompMechanicalEmitter emitter && emitter.IsProducingPower)
                {
                    activeEmitters.Add(emitter);
                }
            }

            // 2. Check Inaccuracy and RPM Mismatch, and calculate aggregated grid stats
            if (activeEmitters.Count > 0)
            {
                if (activeEmitters.Count > 1)
                {
                    for (int i = 0; i < activeEmitters.Count; i++)
                    {
                        for (int j = i + 1; j < activeEmitters.Count; j++)
                        {
                            var e1 = activeEmitters[i];
                            var e2 = activeEmitters[j];
                            
                            float rpmDiff = UnityEngine.Mathf.Abs(e1.TargetRPM - e2.TargetRPM);
                            float inaccuracySum = e1.Props.rpmInaccuracy + e2.Props.rpmInaccuracy;

                            if (rpmDiff > inaccuracySum)
                            {
                                HasRpmMismatch = true;
                                ApplyMismatchEffect(e1);
                                ApplyMismatchEffect(e2);
                            }
                        }
                    }
                }

                if (!HasRpmMismatch)
                {
                    // Safe aggregation
                    foreach (var emitter in activeEmitters)
                    {
                        CurrentPowerOutput += emitter.PowerOutput;
                        GridInaccuracy += emitter.Props.rpmInaccuracy;
                        if (emitter.TargetRPM > GridRPM)
                        {
                            GridRPM = emitter.TargetRPM;
                        }
                    }
                }
                else
                {
                    // Mismatch penalty aggregation
                    float totalRpm = 0f;
                    float rawTorque = 0f;

                    foreach (var emitter in activeEmitters)
                    {
                        totalRpm += emitter.TargetRPM;
                        rawTorque += emitter.PowerOutput;
                        GridInaccuracy += emitter.Props.rpmInaccuracy;
                    }

                    // Average RPM, cut under 10 (floor to nearest 10)
                    float averageRpm = totalRpm / activeEmitters.Count;
                    GridRPM = UnityEngine.Mathf.Floor(averageRpm / 10f) * 10f;

                    // Double inaccuracy
                    GridInaccuracy *= 2f;

                    // Torque reduced to 2/3, cut under 10
                    float reducedTorque = rawTorque * (2f / 3f);
                    CurrentPowerOutput = UnityEngine.Mathf.Floor(reducedTorque / 10f) * 10f;
                }
            }

            // 3. Check Power Users Next
            foreach (var node in nodes)
            {
                if (node is CompMechanicalUser user)
                {
                    var flickable = user.parent.GetComp<CompFlickable>();
                    if (flickable == null || flickable.SwitchIsOn)
                    {
                        CurrentPowerNeeded += user.GridTorqueDemanded;
                    }
                }
            }

            // 4. Overload Logic (Compensating torque and dropping RPM)
            float torqueFulfillment = 1.0f;
            if (CurrentPowerNeeded > CurrentPowerOutput && CurrentPowerOutput > 0)
            {
                float totalMaxPower = 0f;
                foreach (var emitter in activeEmitters)
                {
                    totalMaxPower += HasRpmMismatch ? (emitter.Props.maxPossiblePower * 0.66f) : emitter.Props.maxPossiblePower;
                }

                if (CurrentPowerNeeded <= totalMaxPower)
                {
                    // Engines increase torque to meet demand, RPM is safe
                    CurrentPowerOutput = CurrentPowerNeeded;
                }
                else
                {
                    // Engines pushed beyond max limits!
                    CurrentPowerOutput = totalMaxPower; // Hard cap torque at maximum possible output
                    
                    // The ratio of how much torque we have versus what we actually need
                    torqueFulfillment = totalMaxPower / CurrentPowerNeeded;
                    
                    // RPM drops drastically because it can't handle the load
                    GridRPM *= torqueFulfillment;

                    // Apply overload damage randomly (20% chance per second)
                    if (Verse.Rand.Chance(0.20f))
                    {
                        foreach (var emitter in activeEmitters)
                        {
                            ApplyOverloadEffect(emitter, GridRPM);
                        }
                    }
                }
            }

            bool gridHasEnoughTorque = CurrentPowerOutput > 0;

            // Update users
            foreach (var node in nodes)
            {
                if (node is CompMechanicalUser user)
                {
                    var flickable = user.parent.GetComp<CompFlickable>();
                    bool isSwitchedOn = flickable == null || flickable.SwitchIsOn;
                    
                    // Update user with the torque ratio before checking RPM
                    user.UpdatePowerStatus(gridHasEnoughTorque && isSwitchedOn, torqueFulfillment);
                    
                    bool meetsRpm = user.RealRPM >= user.Props.minimalRPM;
                    
                    // If it doesn't meet RPM after the torque drop, turn it off entirely
                    if (!meetsRpm)
                    {
                        user.UpdatePowerStatus(false, 0f);
                    }
                }
            }
        }

        private void ApplyMismatchEffect(CompMechanicalEmitter emitter)
        {
            // Apply effects rarely so it doesn't instantly destroy the base (runs every 60 ticks / 1 second)
            if (Verse.Rand.Chance(0.05f)) // 5% chance every second while mismatched
            {
                switch (emitter.Props.sourceType)
                {
                    case PowerSourceType.SteamEngine:
                        // Steam Engine: Gears grind and take crush damage
                        emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Crush, 15f));
                        FleckMaker.ThrowDustPuff(emitter.parent.DrawPos, emitter.parent.Map, 1.5f);
                        break;
                    case PowerSourceType.Engine:
                        // Combustion Engine: Overheats and catches fire
                        emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Burn, 10f));
                        FireUtility.TryStartFireIn(emitter.parent.Position, emitter.parent.Map, 0.5f, null);
                        break;
                    case PowerSourceType.Motor:
                        // Electric Motor: Short circuits and throws EMP sparks
                        FleckMaker.ThrowMicroSparks(emitter.parent.DrawPos, emitter.parent.Map);
                        GenExplosion.DoExplosion(emitter.parent.Position, emitter.parent.Map, 1.5f, DamageDefOf.EMP, emitter.parent);
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
                            Messages.Message("Steam engine suffered catastrophic structural damage due to low-RPM high-torque overload!", emitter.parent, MessageTypeDefOf.NegativeEvent);
                            emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Crush, 300f));
                        }
                    }
                    break;
                case PowerSourceType.Engine:
                    var breakdown = emitter.parent.GetComp<CompBreakdownable>();
                    if (breakdown != null && !breakdown.BrokenDown)
                    {
                        breakdown.DoBreakdown();
                        Messages.Message("Combustion engine broke down from mechanical overload!", emitter.parent, MessageTypeDefOf.NegativeEvent);
                    }
                    else
                    {
                        emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, 15f));
                        FleckMaker.ThrowSmoke(emitter.parent.DrawPos, emitter.parent.Map, 1.5f);
                    }
                    break;
                case PowerSourceType.Motor:
                    var powerTrader = emitter.parent.GetComp<CompPowerTrader>();
                    bool explodedBattery = false;
                    if (powerTrader != null && powerTrader.PowerNet != null)
                    {
                        foreach (var battery in powerTrader.PowerNet.batteryComps)
                        {
                            if (battery.StoredEnergy > 0)
                            {
                                battery.SetStoredEnergyPct(0f);
                                GenExplosion.DoExplosion(battery.parent.Position, battery.parent.Map, 2.9f, DamageDefOf.Flame, null);
                                explodedBattery = true;
                                Messages.Message("Electric motor overload caused a battery to violently discharge!", battery.parent, MessageTypeDefOf.NegativeEvent);
                                break;
                            }
                        }
                    }
                    
                    if (!explodedBattery)
                    {
                        GenExplosion.DoExplosion(emitter.parent.Position, emitter.parent.Map, 1.9f, DamageDefOf.EMP, null);
                        emitter.parent.TakeDamage(new DamageInfo(DamageDefOf.Burn, 20f));
                        Messages.Message("Electric motor short-circuited under heavy mechanical load!", emitter.parent, MessageTypeDefOf.NegativeEvent);
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
            allNodes.Add(node);
            isDirty = true;
        }

        public void DeregisterNode(CompMechanicalNode node)
        {
            allNodes.Remove(node);
            isDirty = true;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
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
