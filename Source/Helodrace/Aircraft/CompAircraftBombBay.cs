using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Aircraft
{
    public sealed class AircraftBombDefExtension : DefModExtension
    {
        public ThingDef projectile;
        public List<string> bombTags = new List<string>();
        public float fallSpeed = 0.12f;
        public float weaponRange = 30f;
        public float horizontalScatter = 1.5f;
        public int cooldownTicks = 30;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (projectile == null) yield return "projectile must be set.";
            else if (projectile.projectile == null) yield return "projectile must reference a projectile ThingDef.";
            if (fallSpeed <= 0f) yield return "fallSpeed must be greater than zero.";
            if (weaponRange <= 0f) yield return "weaponRange must be greater than zero.";
            if (horizontalScatter < 0f) yield return "horizontalScatter cannot be negative.";
            if (cooldownTicks < 0) yield return "cooldownTicks cannot be negative.";
        }
    }

    public sealed class AircraftBombSlotLimit
    {
        public ThingDef bombDef;
        public int maxCountPerSlot = 1;
    }

    public sealed class CompProperties_AircraftBombBay : CompProperties
    {
        public float capacity = 1000f;
        public List<ThingDef> allowedBombDefs = new List<ThingDef>();
        public List<string> allowedBombTags = new List<string>();
        public bool hasCenterSlot = true;
        public int wingSlotsPerSide = -1;
        public int wingSlotPairs = 2;
        public float minimumImbalancedTurnRateFactor = 0.4f;
        public int defaultMaxCountPerSlot = 1;
        public List<AircraftBombSlotLimit> bombSlotLimits = new List<AircraftBombSlotLimit>();
        public int ResolvedWingSlotsPerSide => wingSlotsPerSide >= 0 ? wingSlotsPerSide : wingSlotPairs;

        public CompProperties_AircraftBombBay()
        {
            compClass = typeof(CompAircraftBombBay);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (capacity < 0f) yield return "capacity cannot be negative.";
            if (wingSlotsPerSide < -1) yield return "wingSlotsPerSide cannot be less than -1.";
            if (ResolvedWingSlotsPerSide < 0) yield return "wingSlotsPerSide cannot be negative.";
            if (minimumImbalancedTurnRateFactor <= 0f || minimumImbalancedTurnRateFactor > 1f)
                yield return "minimumImbalancedTurnRateFactor must be greater than zero and at most one.";
            if (defaultMaxCountPerSlot <= 0)
                yield return "defaultMaxCountPerSlot must be greater than zero.";
            if (bombSlotLimits != null)
            {
                foreach (AircraftBombSlotLimit limit in bombSlotLimits)
                {
                    if (limit == null || limit.bombDef == null)
                        yield return "each bombSlotLimits entry must define bombDef.";
                    else if (limit.maxCountPerSlot <= 0)
                        yield return $"bombSlotLimits entry for {limit.bombDef.defName} must be greater than zero.";
                }
                foreach (IGrouping<ThingDef, AircraftBombSlotLimit> duplicate in bombSlotLimits
                    .Where(limit => limit?.bombDef != null).GroupBy(limit => limit.bombDef)
                    .Where(group => group.Count() > 1))
                    yield return $"bombSlotLimits contains duplicate entries for {duplicate.Key.defName}.";
            }
            if (!hasCenterSlot && ResolvedWingSlotsPerSide == 0)
                yield return "at least one center or wing bomb slot is required.";
            if ((allowedBombDefs == null || allowedBombDefs.Count == 0)
                && (allowedBombTags == null || allowedBombTags.Count == 0))
                yield return "at least one allowedBombDefs entry or allowedBombTags entry is required.";
        }
    }

    public sealed class CompAircraftBombBay : ThingComp, IThingHolder
    {
        private ThingOwner<Thing> bombs;
        private Dictionary<ThingDef, int> targetBombCounts = new Dictionary<ThingDef, int>();
        private ThingDef centerBombDef;
        private int centerBombCount;
        private List<ThingDef> wingPairBombDefs = new List<ThingDef>();
        private List<int> wingPairBombCounts = new List<int>();
        private int centerBombsPresent;
        private List<int> leftWingBombCounts = new List<int>();
        private List<int> rightWingBombCounts = new List<int>();
        private bool airborneHardpointsInitialized;
        private int hardpointCountStateVersion;
        private bool slotLoadoutInitialized;
        private int nextDropTick;

        public CompProperties_AircraftBombBay Props => (CompProperties_AircraftBombBay)props;
        public ThingOwner BombContainer => bombs;
        public IEnumerable<Thing> LoadedBombStacks => bombs ?? Enumerable.Empty<Thing>();
        public float LoadedMass => LoadedBombStacks.Sum(StackMass);
        public float RemainingMass => Mathf.Max(0f, Props.capacity - LoadedMass);
        public IEnumerable<ThingDef> AllowedBombDefs => DefDatabase<ThingDef>.AllDefsListForReading
            .Where(IsAllowedBomb).OrderBy(def => def.label);
        public IReadOnlyDictionary<ThingDef, int> TargetBombCounts => targetBombCounts;
        public ThingDef CenterBombDef => centerBombDef;
        public int CenterBombCount => centerBombCount;
        public IReadOnlyList<ThingDef> WingPairBombDefs => wingPairBombDefs;
        public IReadOnlyList<int> WingPairBombCounts => wingPairBombCounts;
        public float ConfiguredMass => targetBombCounts.Sum(pair => UnitMass(pair.Key) * pair.Value);
        public float WingImbalanceMass
        {
            get
            {
                EnsureAirborneHardpointState();
                return Mathf.Abs(CurrentRightWingMass() - CurrentLeftWingMass());
            }
        }
        public float TurnRateFactor
        {
            get
            {
                EnsureAirborneHardpointState();
                float leftMass = CurrentLeftWingMass();
                float rightMass = CurrentRightWingMass();
                float totalMass = leftMass + rightMass;
                if (totalMass <= 0.001f) return 1f;
                float imbalance = Mathf.Abs(rightMass - leftMass) / totalMass;
                return Mathf.Lerp(1f, Props.minimumImbalancedTurnRateFactor, imbalance);
            }
        }

        private AircraftThing Aircraft => parent as AircraftThing;
        private bool CanChangeLoadout => Aircraft == null || !Aircraft.IsAirborne;

        public override void Initialize(CompProperties properties)
        {
            base.Initialize(properties);
            bombs = new ThingOwner<Thing>(this, false, LookMode.Deep, false);
            EnsureSlotList();
            EnsurePresenceLists();
            RebuildTargetCounts();
            hardpointCountStateVersion = 2;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref bombs, "aircraftBombs", this);
            Scribe_Collections.Look(ref targetBombCounts, "aircraftBombLoadoutTargets", LookMode.Def, LookMode.Value);
            Scribe_Defs.Look(ref centerBombDef, "aircraftCenterBombSlot");
            Scribe_Values.Look(ref centerBombCount, "aircraftCenterBombSlotCount");
            Scribe_Collections.Look(ref wingPairBombDefs, "aircraftWingBombSlotPairs", LookMode.Def);
            Scribe_Collections.Look(ref wingPairBombCounts, "aircraftWingBombSlotCounts", LookMode.Value);
            Scribe_Values.Look(ref centerBombsPresent, "aircraftCenterBombsPresent");
            Scribe_Collections.Look(ref leftWingBombCounts, "aircraftLeftWingBombCounts", LookMode.Value);
            Scribe_Collections.Look(ref rightWingBombCounts, "aircraftRightWingBombCounts", LookMode.Value);
            Scribe_Values.Look(ref airborneHardpointsInitialized, "aircraftAirborneHardpointsInitialized");
            Scribe_Values.Look(ref hardpointCountStateVersion, "aircraftHardpointCountStateVersion");
            Scribe_Values.Look(ref slotLoadoutInitialized, "aircraftSlotLoadoutInitialized");
            Scribe_Values.Look(ref nextDropTick, "nextAircraftBombDropTick");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (bombs == null) bombs = new ThingOwner<Thing>(this, false, LookMode.Deep, false);
                if (targetBombCounts == null) targetBombCounts = new Dictionary<ThingDef, int>();
                EnsureSlotList();
                EnsurePresenceLists();
                if (hardpointCountStateVersion < 2)
                {
                    airborneHardpointsInitialized = false;
                    hardpointCountStateVersion = 2;
                }
                if (!slotLoadoutInitialized)
                {
                    MigrateLegacyTargets();
                    slotLoadoutInitialized = true;
                }
                RebuildTargetCounts();
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            if (previousMap != null && bombs != null)
                bombs.TryDropAll(parent.Position, previousMap, ThingPlaceMode.Near, null, null, true);
            else
                bombs?.ClearAndDestroyContentsOrPassToWorld(mode);
            base.PostDestroy(mode, previousMap);
        }

        public ThingOwner GetDirectlyHeldThings() => bombs;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, bombs);
        }

        public bool IsAllowedBomb(ThingDef bombDef)
        {
            if (bombDef == null || bombDef.GetModExtension<AircraftBombDefExtension>() == null) return false;
            if (Props.allowedBombDefs != null && Props.allowedBombDefs.Contains(bombDef)) return true;

            HashSet<string> allowedTags = new HashSet<string>(Props.allowedBombTags ?? new List<string>());
            if (allowedTags.Count == 0) return false;
            return TagsFor(bombDef).Any(allowedTags.Contains);
        }

        public int CountThatFits(Thing bomb)
        {
            if (bomb == null || !IsAllowedBomb(bomb.def) || !CanChangeLoadout) return 0;
            float mass = Mathf.Max(0.0001f, bomb.GetStatValue(StatDefOf.Mass));
            return Mathf.Clamp(Mathf.FloorToInt(RemainingMass / mass), 0, bomb.stackCount);
        }

        public int LoadedCount(ThingDef bombDef)
        {
            return LoadedBombStacks.Where(thing => thing.def == bombDef).Sum(thing => thing.stackCount);
        }

        public int TargetCount(ThingDef bombDef)
        {
            return bombDef != null && targetBombCounts.TryGetValue(bombDef, out int count) ? count : 0;
        }

        public int NeededCount(ThingDef bombDef)
        {
            return Mathf.Max(0, TargetCount(bombDef) - LoadedCount(bombDef));
        }

        public bool HasConfiguredLoadout
        {
            get
            {
                EnsureSlotList();
                return centerBombDef != null || wingPairBombDefs.Any(def => def != null);
            }
        }

        public bool LoadoutReadyForTakeoff
        {
            get
            {
                RebuildTargetCounts();
                int configuredCount = targetBombCounts.Values.Sum();
                int loadedCount = LoadedBombStacks.Sum(thing => thing.stackCount);
                return configuredCount == loadedCount
                    && targetBombCounts.All(pair => LoadedCount(pair.Key) == pair.Value)
                    && LoadedBombStacks.All(thing => TargetCount(thing.def) > 0);
            }
        }

        public void PrepareForTakeoff()
        {
            EnsureSlotList();
            EnsurePresenceLists();
            centerBombsPresent = centerBombDef == null ? 0 : centerBombCount;
            for (int index = 0; index < wingPairBombDefs.Count; index++)
            {
                int count = wingPairBombDefs[index] == null ? 0 : wingPairBombCounts[index];
                leftWingBombCounts[index] = count;
                rightWingBombCounts[index] = count;
            }
            airborneHardpointsInitialized = true;
            hardpointCountStateVersion = 2;
        }

        public int AvailableOnMap(ThingDef bombDef)
        {
            return parent.Map?.listerThings.ThingsOfDef(bombDef).Sum(thing => thing.stackCount) ?? 0;
        }

        public int MaxCountPerSlot(ThingDef bombDef)
        {
            AircraftBombSlotLimit exact = Props.bombSlotLimits?
                .FirstOrDefault(limit => limit?.bombDef == bombDef);
            return Mathf.Max(1, exact?.maxCountPerSlot ?? Props.defaultMaxCountPerSlot);
        }

        public bool TryConfigureCenter(ThingDef bombDef, int count, out string rejection)
        {
            EnsureSlotList();
            int requestedCenterCount = bombDef == null
                ? 0
                : Mathf.Clamp(count, 1, MaxCountPerSlot(bombDef));
            return TryApplySlotLayout(bombDef, requestedCenterCount,
                new List<ThingDef>(wingPairBombDefs),
                new List<int>(wingPairBombCounts), out rejection);
        }

        public bool TryConfigureWingSlot(int slotIndex, ThingDef bombDef, int countPerSide,
            out string rejection)
        {
            EnsureSlotList();
            rejection = null;
            if (slotIndex < 0 || slotIndex >= wingPairBombDefs.Count)
            {
                rejection = "HD_Aircraft_BombSlot_Invalid".Translate();
                return false;
            }

            List<ThingDef> requestedWings = new List<ThingDef>(wingPairBombDefs);
            List<int> requestedCounts = new List<int>(wingPairBombCounts);
            requestedWings[slotIndex] = bombDef;
            requestedCounts[slotIndex] = bombDef == null
                ? 0
                : Mathf.Clamp(countPerSide, 1, MaxCountPerSlot(bombDef));
            return TryApplySlotLayout(centerBombDef, centerBombCount,
                requestedWings, requestedCounts, out rejection);
        }

        private bool TryApplySlotLayout(ThingDef requestedCenter, int requestedCenterCount,
            List<ThingDef> requestedWings, List<int> requestedCounts, out string rejection)
        {
            rejection = null;
            if (!CanChangeLoadout)
            {
                rejection = "HD_Aircraft_BombBay_Airborne".Translate();
                return false;
            }

            if (requestedCenter != null && (!Props.hasCenterSlot || !IsAllowedBomb(requestedCenter))
                || requestedWings.Any(def => def != null && !IsAllowedBomb(def)))
            {
                rejection = "HD_Aircraft_InvalidBomb".Translate();
                return false;
            }
            if (requestedCenter != null
                && (requestedCenterCount <= 0 || requestedCenterCount > MaxCountPerSlot(requestedCenter)))
            {
                rejection = "HD_Aircraft_BombSlot_CountInvalid".Translate(
                    requestedCenter.LabelCap, MaxCountPerSlot(requestedCenter));
                return false;
            }

            for (int index = 0; index < requestedWings.Count; index++)
            {
                ThingDef bombDef = requestedWings[index];
                int count = index < requestedCounts.Count ? requestedCounts[index] : 0;
                if (bombDef != null && (count <= 0 || count > MaxCountPerSlot(bombDef)))
                {
                    rejection = "HD_Aircraft_BombSlot_CountInvalid".Translate(
                        bombDef.LabelCap, MaxCountPerSlot(bombDef));
                    return false;
                }
            }

            float requestedMass = UnitMass(requestedCenter) * requestedCenterCount
                + requestedWings.Select((def, index) => def == null ? 0f
                    : UnitMass(def) * requestedCounts[index] * 2f).Sum();
            if (requestedMass > Props.capacity + 0.001f)
            {
                rejection = "HD_Aircraft_BombLoadoutOverMass".Translate(
                    requestedMass.ToString("0.#"), Props.capacity.ToString("0.#"));
                return false;
            }

            centerBombDef = Props.hasCenterSlot ? requestedCenter : null;
            centerBombCount = centerBombDef == null ? 0 : requestedCenterCount;
            int slotCount = Mathf.Max(0, Props.ResolvedWingSlotsPerSide);
            wingPairBombDefs = requestedWings.Take(slotCount).ToList();
            wingPairBombCounts = requestedCounts.Take(slotCount).ToList();
            EnsureSlotList();
            airborneHardpointsInitialized = false;
            slotLoadoutInitialized = true;
            RebuildTargetCounts();
            UnloadExcessBombs();
            return true;
        }

        public Thing NextBombFor(Pawn hauler)
        {
            CleanupTargets();
            if (!CanChangeLoadout || hauler?.Map == null) return null;
            Thing best = null;
            float bestDistance = float.MaxValue;
            foreach (ThingDef bombDef in targetBombCounts.Keys)
            {
                if (NeededCount(bombDef) <= 0) continue;
                Thing candidate = GenClosest.ClosestThingReachable(
                    hauler.Position, hauler.Map, ThingRequest.ForDef(bombDef),
                    PathEndMode.ClosestTouch, TraverseParms.For(hauler), 9999f,
                    thing => !thing.IsForbidden(hauler) && hauler.CanReserve(thing));
                if (candidate == null || CountThatFits(candidate) <= 0) continue;
                float distance = candidate.Position.DistanceToSquared(hauler.Position);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public bool TryAcceptBombFrom(Pawn hauler, int maximumCount = int.MaxValue)
        {
            Thing carried = hauler?.carryTracker?.CarriedThing;
            if (carried == null) return false;
            int count = Mathf.Min(Mathf.Max(0, maximumCount),
                Mathf.Min(CountThatFits(carried), NeededCount(carried.def)));
            if (count <= 0) return false;
            int transferred = hauler.carryTracker.innerContainer.TryTransferToContainer(
                carried, bombs, count, false);
            return transferred > 0;
        }

        public bool TryDropBomb(ThingDef bombDef, out string rejection)
        {
            rejection = null;
            AircraftThing aircraft = Aircraft;
            if (aircraft == null || !aircraft.IsAirborne || aircraft.Map == null)
            {
                rejection = "HD_Aircraft_Bombing_NotAirborne".Translate();
                return false;
            }
            if (aircraft.Manifest != null && !aircraft.Manifest.HasCapablePilot)
            {
                rejection = "HD_Aircraft_Bombing_NoPilot".Translate();
                return false;
            }
            if (Find.TickManager.TicksGame < nextDropTick)
            {
                rejection = "HD_Aircraft_Bombing_Cooldown".Translate();
                return false;
            }

            Thing stack = bombs.FirstOrDefault(thing => thing.def == bombDef);
            AircraftBombDefExtension bomb = bombDef?.GetModExtension<AircraftBombDefExtension>();
            if (stack == null || bomb?.projectile == null)
            {
                rejection = "HD_Aircraft_Bombing_NoBomb".Translate();
                return false;
            }
            if (!aircraft.CanAttackWithRange(bomb.weaponRange))
            {
                rejection = "HD_Aircraft_Bombing_OutOfRange".Translate(
                    bomb.weaponRange.ToString("0.#"), aircraft.Altitude);
                return false;
            }

            if (!TrySelectHardpointForRelease(bombDef, out bool releaseCenter,
                out int wingPairIndex, out bool releaseLeftWing))
            {
                rejection = "HD_Aircraft_Bombing_NoBomb".Translate();
                return false;
            }

            Projectile projectile = ThingMaker.MakeThing(bomb.projectile) as Projectile;
            if (projectile == null)
            {
                rejection = "HD_Aircraft_Bombing_InvalidProjectile".Translate();
                return false;
            }

            IntVec3 impactCell = CalculateImpactCell(bomb);
            GenSpawn.Spawn(projectile, aircraft.Position, aircraft.Map);
            LocalTargetInfo target = new LocalTargetInfo(impactCell);
            projectile.Launch(aircraft, aircraft.ExactPosition, target, target,
                ProjectileHitFlags.All, false, null, bombDef);

            if (releaseCenter)
                centerBombsPresent--;
            else if (releaseLeftWing)
                leftWingBombCounts[wingPairIndex]--;
            else
                rightWingBombCounts[wingPairIndex]--;

            Thing spent = bombs.Take(stack, 1);
            spent?.Destroy(DestroyMode.Vanish);
            nextDropTick = Find.TickManager.TicksGame + bomb.cooldownTicks;
            return true;
        }

        private bool TrySelectHardpointForRelease(ThingDef bombDef, out bool releaseCenter,
            out int wingPairIndex, out bool releaseLeftWing)
        {
            EnsureAirborneHardpointState();
            releaseCenter = centerBombsPresent > 0 && centerBombDef == bombDef;
            wingPairIndex = -1;
            releaseLeftWing = false;
            if (releaseCenter) return true;

            float currentImbalance = CurrentRightWingMass() - CurrentLeftWingMass();
            float bombMass = UnitMass(bombDef);
            float bestResult = float.MaxValue;
            for (int index = 0; index < wingPairBombDefs.Count; index++)
            {
                if (wingPairBombDefs[index] != bombDef) continue;
                if (leftWingBombCounts[index] > 0)
                {
                    float result = Mathf.Abs(currentImbalance + bombMass);
                    if (result < bestResult)
                    {
                        bestResult = result;
                        wingPairIndex = index;
                        releaseLeftWing = true;
                    }
                }
                if (rightWingBombCounts[index] > 0)
                {
                    float result = Mathf.Abs(currentImbalance - bombMass);
                    if (result < bestResult)
                    {
                        bestResult = result;
                        wingPairIndex = index;
                        releaseLeftWing = false;
                    }
                }
            }
            return wingPairIndex >= 0;
        }

        public IntVec3 CalculateImpactCell(AircraftBombDefExtension bomb)
        {
            Vector3 forward = DirectionForDegrees(Aircraft.HeadingDegrees);
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            float longitudinalError = Rand.Range(-bomb.horizontalScatter, bomb.horizontalScatter);
            float lateralError = Rand.Range(-bomb.horizontalScatter, bomb.horizontalScatter);
            float fallTicks = Aircraft.Altitude / Mathf.Max(0.001f, bomb.fallSpeed);
            float forwardDistance = Aircraft.CurrentSpeed * fallTicks;
            Vector3 impact = Aircraft.ExactPosition
                + forward * Mathf.Max(0f, forwardDistance + longitudinalError)
                + right * lateralError;
            return ClampToMap(impact.ToIntVec3());
        }

        public IntVec3 CalculatePredictedImpactCell(AircraftBombDefExtension bomb)
        {
            if (Aircraft?.Map == null || bomb == null)
            {
                return IntVec3.Invalid;
            }

            float fallTicks = Aircraft.Altitude / Mathf.Max(0.001f, bomb.fallSpeed);
            float forwardDistance = Aircraft.CurrentSpeed * fallTicks;
            Vector3 impact = Aircraft.ExactPosition
                + DirectionForDegrees(Aircraft.HeadingDegrees) * Mathf.Max(0f, forwardDistance);
            return ClampToMap(impact.ToIntVec3());
        }

        private void DrawBombPrediction(ThingDef bombDef)
        {
            AircraftThing aircraft = Aircraft;
            if (aircraft == null || !aircraft.IsAirborne || aircraft.Map == null)
            {
                return;
            }

            AircraftBombDefExtension bomb = bombDef?.GetModExtension<AircraftBombDefExtension>();
            IntVec3 predicted = CalculatePredictedImpactCell(bomb);
            if (!predicted.IsValid || !predicted.InBounds(aircraft.Map))
            {
                return;
            }

            GenDraw.DrawLineBetween(aircraft.DrawPos, predicted.ToVector3Shifted(),
                SimpleColor.Yellow, 0.12f);
            GenDraw.DrawTargetHighlight(new LocalTargetInfo(predicted));

            float explosionRadius = bomb?.projectile?.projectile?.explosionRadius ?? 0f;
            if (explosionRadius > 0f)
            {
                GenDraw.DrawRadiusRing(predicted, explosionRadius, Color.red);
            }

            if (bomb.horizontalScatter > 0f)
            {
                GenDraw.DrawRadiusRing(predicted,
                    bomb.horizontalScatter * 1.414214f, Color.yellow);
            }
        }

        public void EjectAllBombs()
        {
            if (!CanChangeLoadout || parent.Map == null) return;
            centerBombDef = null;
            centerBombCount = 0;
            EnsureSlotList();
            for (int index = 0; index < wingPairBombDefs.Count; index++)
            {
                wingPairBombDefs[index] = null;
                wingPairBombCounts[index] = 0;
            }
            EnsurePresenceLists();
            centerBombsPresent = 0;
            for (int index = 0; index < leftWingBombCounts.Count; index++)
            {
                leftWingBombCounts[index] = 0;
                rightWingBombCounts[index] = 0;
            }
            airborneHardpointsInitialized = false;
            RebuildTargetCounts();
            bombs.TryDropAll(parent.Position, parent.Map, ThingPlaceMode.Near, null, null, false);
        }

        public override string CompInspectStringExtra()
        {
            string result = "HD_Aircraft_BombBayInspect".Translate(
                LoadedMass.ToString("0.#"), Props.capacity.ToString("0.#"));
            if (Aircraft != null && Aircraft.IsAirborne && WingImbalanceMass > 0.001f)
            {
                result += "\n" + "HD_Aircraft_BombImbalanceInspect".Translate(
                    WingImbalanceMass.ToString("0.#"),
                    ((1f - TurnRateFactor) * 100f).ToString("0.#"));
            }
            return result;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            yield return GroundCommand("HD_Aircraft_ConfigureBombs_Label", "HD_Aircraft_ConfigureBombs_Desc",
                () => Find.WindowStack.Add(new Dialog_AircraftBombLoadout(this)),
                !AllowedBombDefs.Any() ? "HD_Aircraft_NoAllowedBombDefs".Translate() : null);
            yield return GroundCommand("HD_Aircraft_UnloadBombs_Label", "HD_Aircraft_UnloadBombs_Desc",
                EjectAllBombs, !LoadedBombStacks.Any() ? "HD_Aircraft_NoLoadedBombs".Translate() : null);

            if (Aircraft == null || !Aircraft.IsAirborne) yield break;
            foreach (IGrouping<ThingDef, Thing> group in LoadedBombStacks.GroupBy(thing => thing.def))
            {
                ThingDef localDef = group.Key;
                int count = group.Sum(thing => thing.stackCount);
                AircraftBombDefExtension bomb = localDef.GetModExtension<AircraftBombDefExtension>();
                float predicted = bomb == null ? 0f : Aircraft.CurrentSpeed * Aircraft.Altitude / Mathf.Max(0.001f, bomb.fallSpeed);
                Command_Action dropCommand = new Command_Action
                {
                    defaultLabel = "HD_Aircraft_DropBomb_Label".Translate(localDef.LabelCap, count),
                    defaultDesc = "HD_Aircraft_DropBomb_Desc".Translate(predicted.ToString("0.#"), bomb?.horizontalScatter.ToString("0.#") ?? "0"),
                    icon = localDef.uiIcon ?? BaseContent.BadTex,
                    onHover = () => DrawBombPrediction(localDef),
                    action = () =>
                    {
                        if (!TryDropBomb(localDef, out string rejection))
                            Messages.Message(rejection, parent, MessageTypeDefOf.RejectInput, false);
                    }
                };
                if (bomb != null && !Aircraft.CanAttackWithRange(bomb.weaponRange))
                {
                    dropCommand.Disable("HD_Aircraft_Bombing_OutOfRange".Translate(
                        bomb.weaponRange.ToString("0.#"), Aircraft.Altitude));
                }
                yield return dropCommand;
            }
        }

        private Command_Action GroundCommand(string label, string desc, Action action, string disabledReason)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel = label.Translate(),
                defaultDesc = desc.Translate(),
                icon = BaseContent.BadTex,
                action = action
            };
            if (!CanChangeLoadout) command.Disable("HD_Aircraft_BombBay_Airborne".Translate());
            else if (!disabledReason.NullOrEmpty()) command.Disable(disabledReason);
            return command;
        }

        private void UnloadExcessBombs()
        {
            if (parent.Map == null) return;
            foreach (IGrouping<ThingDef, Thing> group in LoadedBombStacks.GroupBy(thing => thing.def).ToList())
            {
                int excess = Mathf.Max(0, group.Sum(thing => thing.stackCount) - TargetCount(group.Key));
                foreach (Thing stack in group.ToList())
                {
                    if (excess <= 0) break;
                    int dropCount = Mathf.Min(excess, stack.stackCount);
                    bombs.TryDrop(stack, parent.Position, parent.Map, ThingPlaceMode.Near,
                        dropCount, out Thing _, null, null);
                    excess -= dropCount;
                }
            }
        }

        private void CleanupTargets()
        {
            RebuildTargetCounts();
            foreach (ThingDef invalid in targetBombCounts.Keys
                .Where(def => def == null || !IsAllowedBomb(def) || targetBombCounts[def] <= 0).ToList())
                targetBombCounts.Remove(invalid);
        }

        private void EnsureSlotList()
        {
            if (wingPairBombDefs == null) wingPairBombDefs = new List<ThingDef>();
            if (wingPairBombCounts == null) wingPairBombCounts = new List<int>();
            int required = Mathf.Max(0, Props.ResolvedWingSlotsPerSide);
            if (wingPairBombDefs.Count > required)
                wingPairBombDefs.RemoveRange(required, wingPairBombDefs.Count - required);
            if (wingPairBombCounts.Count > required)
                wingPairBombCounts.RemoveRange(required, wingPairBombCounts.Count - required);
            while (wingPairBombDefs.Count < required) wingPairBombDefs.Add(null);
            while (wingPairBombCounts.Count < required)
            {
                int index = wingPairBombCounts.Count;
                wingPairBombCounts.Add(wingPairBombDefs[index] == null ? 0 : 1);
            }
            for (int index = 0; index < required; index++)
                wingPairBombCounts[index] = wingPairBombDefs[index] == null
                    ? 0
                    : Mathf.Clamp(wingPairBombCounts[index], 1, MaxCountPerSlot(wingPairBombDefs[index]));
            if (!Props.hasCenterSlot) centerBombDef = null;
            centerBombCount = centerBombDef == null
                ? 0
                : Mathf.Clamp(centerBombCount, 1, MaxCountPerSlot(centerBombDef));
        }

        private void EnsurePresenceLists()
        {
            if (leftWingBombCounts == null) leftWingBombCounts = new List<int>();
            if (rightWingBombCounts == null) rightWingBombCounts = new List<int>();
            int required = Mathf.Max(0, Props.ResolvedWingSlotsPerSide);
            if (leftWingBombCounts.Count > required)
                leftWingBombCounts.RemoveRange(required, leftWingBombCounts.Count - required);
            if (rightWingBombCounts.Count > required)
                rightWingBombCounts.RemoveRange(required, rightWingBombCounts.Count - required);
            while (leftWingBombCounts.Count < required) leftWingBombCounts.Add(0);
            while (rightWingBombCounts.Count < required) rightWingBombCounts.Add(0);
        }

        private void EnsureAirborneHardpointState()
        {
            EnsureSlotList();
            EnsurePresenceLists();
            if (airborneHardpointsInitialized || Aircraft == null || !Aircraft.IsAirborne) return;

            Dictionary<ThingDef, int> remaining = LoadedBombStacks
                .GroupBy(thing => thing.def)
                .ToDictionary(group => group.Key, group => group.Sum(thing => thing.stackCount));
            centerBombsPresent = TakeReconstructedBombs(centerBombDef, centerBombCount, remaining);
            for (int index = 0; index < wingPairBombDefs.Count; index++)
            {
                ThingDef bombDef = wingPairBombDefs[index];
                int configured = wingPairBombCounts[index];
                int available = bombDef != null && remaining.TryGetValue(bombDef, out int count)
                    ? Mathf.Min(count, configured * 2) : 0;
                int balanced = Mathf.Min(configured, available / 2);
                leftWingBombCounts[index] = balanced;
                rightWingBombCounts[index] = balanced;
                available -= balanced * 2;
                if (available > 0)
                    leftWingBombCounts[index] += Mathf.Min(configured - balanced, available);
                if (bombDef != null && remaining.ContainsKey(bombDef))
                    remaining[bombDef] -= leftWingBombCounts[index] + rightWingBombCounts[index];
            }
            airborneHardpointsInitialized = true;
        }

        private static int TakeReconstructedBombs(ThingDef bombDef, int requestedCount,
            Dictionary<ThingDef, int> remaining)
        {
            if (bombDef == null || !remaining.TryGetValue(bombDef, out int count) || count <= 0)
                return 0;
            int taken = Mathf.Min(requestedCount, count);
            remaining[bombDef] = count - taken;
            return taken;
        }

        private float CurrentLeftWingMass()
        {
            EnsurePresenceLists();
            float mass = 0f;
            for (int index = 0; index < wingPairBombDefs.Count; index++)
                mass += leftWingBombCounts[index] * UnitMass(wingPairBombDefs[index]);
            return mass;
        }

        private float CurrentRightWingMass()
        {
            EnsurePresenceLists();
            float mass = 0f;
            for (int index = 0; index < wingPairBombDefs.Count; index++)
                mass += rightWingBombCounts[index] * UnitMass(wingPairBombDefs[index]);
            return mass;
        }

        private void RebuildTargetCounts()
        {
            EnsureSlotList();
            targetBombCounts = new Dictionary<ThingDef, int>();
            AddTarget(centerBombDef, centerBombCount);
            for (int index = 0; index < wingPairBombDefs.Count; index++)
                AddTarget(wingPairBombDefs[index], wingPairBombCounts[index] * 2);
        }

        private void AddTarget(ThingDef bombDef, int count)
        {
            if (bombDef == null || !IsAllowedBomb(bombDef)) return;
            targetBombCounts[bombDef] = TargetCount(bombDef) + count;
        }

        private void MigrateLegacyTargets()
        {
            Dictionary<ThingDef, int> legacy = targetBombCounts == null
                ? new Dictionary<ThingDef, int>()
                : new Dictionary<ThingDef, int>(targetBombCounts);
            centerBombDef = null;
            centerBombCount = 0;
            EnsureSlotList();
            for (int index = 0; index < wingPairBombDefs.Count; index++)
            {
                wingPairBombDefs[index] = null;
                wingPairBombCounts[index] = 0;
            }

            int wingIndex = 0;
            foreach (KeyValuePair<ThingDef, int> pair in legacy
                .Where(pair => pair.Key != null && pair.Value > 0 && IsAllowedBomb(pair.Key)))
            {
                int remaining = pair.Value;
                if (Props.hasCenterSlot && centerBombDef == null && remaining % 2 == 1)
                {
                    centerBombDef = pair.Key;
                    centerBombCount = 1;
                    remaining -= centerBombCount;
                }
                while (remaining >= 2 && wingIndex < wingPairBombDefs.Count)
                {
                    int perSide = Mathf.Min(MaxCountPerSlot(pair.Key), remaining / 2);
                    wingPairBombDefs[wingIndex] = pair.Key;
                    wingPairBombCounts[wingIndex] = perSide;
                    wingIndex++;
                    remaining -= perSide * 2;
                }
            }
        }

        private IntVec3 ClampToMap(IntVec3 cell)
        {
            Map map = parent.Map;
            return new IntVec3(Mathf.Clamp(cell.x, 0, map.Size.x - 1), 0,
                Mathf.Clamp(cell.z, 0, map.Size.z - 1));
        }

        private static IEnumerable<string> TagsFor(ThingDef def)
        {
            AircraftBombDefExtension extension = def.GetModExtension<AircraftBombDefExtension>();
            if (extension?.bombTags != null)
                foreach (string tag in extension.bombTags) if (!tag.NullOrEmpty()) yield return tag;
            if (def.tradeTags != null)
                foreach (string tag in def.tradeTags) if (!tag.NullOrEmpty()) yield return tag;
            if (def.weaponTags != null)
                foreach (string tag in def.weaponTags) if (!tag.NullOrEmpty()) yield return tag;
            if (def.thingSetMakerTags != null)
                foreach (string tag in def.thingSetMakerTags) if (!tag.NullOrEmpty()) yield return tag;
        }

        private static float StackMass(Thing thing)
        {
            return thing.GetStatValue(StatDefOf.Mass) * thing.stackCount;
        }

        public static float UnitMass(ThingDef def)
        {
            return def?.GetStatValueAbstract(StatDefOf.Mass) ?? 0f;
        }

        private static Vector3 DirectionForDegrees(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }
    }

    public sealed class Dialog_AircraftBombLoadout : Window
    {
        private readonly CompAircraftBombBay bay;

        public override Vector2 InitialSize => new Vector2(760f, 500f);

        public Dialog_AircraftBombLoadout(CompAircraftBombBay bay)
        {
            this.bay = bay;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f),
                "HD_Aircraft_BombLoadout_Title".Translate(bay.parent.LabelCap).Resolve());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y + 38f, inRect.width, 28f),
                "HD_Aircraft_BombLoadout_Mass".Translate(
                    bay.ConfiguredMass.ToString("0.#"), bay.Props.capacity.ToString("0.#")).Resolve());
            Widgets.Label(new Rect(inRect.x, inRect.y + 65f, inRect.width, 42f),
                "HD_Aircraft_BombLoadout_Symmetric".Translate().Resolve());

            float layoutTop = inRect.y + 125f;
            float centerX = inRect.center.x;
            int pairCount = bay.WingPairBombDefs.Count;
            float slotSize = pairCount == 0 ? 92f : Mathf.Clamp(
                (inRect.width - 170f) / (pairCount * 2f) - 10f, 52f, 82f);
            float step = slotSize + 10f;
            for (int index = pairCount - 1; index >= 0; index--)
            {
                float distance = 32f + index * step;
                ThingDef bombDef = bay.WingPairBombDefs[index];
                int localIndex = index;
                DrawSlot(new Rect(centerX - distance - slotSize, layoutTop, slotSize, slotSize),
                    bombDef, bay.WingPairBombCounts[index],
                    "HD_Aircraft_BombSlot_WingLeft".Translate(index + 1),
                    () => OpenWingPicker(localIndex));
                DrawSlot(new Rect(centerX + distance, layoutTop, slotSize, slotSize),
                    bombDef, bay.WingPairBombCounts[index],
                    "HD_Aircraft_BombSlot_WingRight".Translate(index + 1),
                    () => OpenWingPicker(localIndex));
            }

            if (bay.Props.hasCenterSlot)
            {
                const float centerSize = 96f;
                DrawSlot(new Rect(centerX - centerSize / 2f, layoutTop + slotSize + 48f,
                    centerSize, centerSize), bay.CenterBombDef, bay.CenterBombCount,
                    "HD_Aircraft_BombSlot_Center".Translate(),
                    () => Find.WindowStack.Add(new Dialog_AircraftBombSlotPicker(bay, true, 0)));
            }

            int configured = bay.TargetBombCounts.Values.Sum();
            int loaded = bay.LoadedBombStacks.Sum(thing => thing.stackCount);
            Widgets.Label(new Rect(inRect.x, inRect.yMax - 48f, inRect.width - 140f, 38f),
                "HD_Aircraft_BombLoadout_Loaded".Translate(loaded, configured).Resolve());
            if (Widgets.ButtonText(new Rect(inRect.xMax - 126f, inRect.yMax - 48f, 120f, 40f),
                "CloseButton".Translate().Resolve())) Close();
        }

        private void OpenWingPicker(int pairIndex)
        {
            Find.WindowStack.Add(new Dialog_AircraftBombSlotPicker(bay, false, pairIndex));
        }

        private static void DrawSlot(Rect rect, ThingDef bombDef, int count,
            TaggedString slotLabel, Action clicked)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.13f, 0.15f, 0.96f));
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            Widgets.DrawBox(rect, 2);
            if (bombDef != null)
                Widgets.DrawTextureFitted(rect.ContractedBy(9f), bombDef.uiIcon ?? BaseContent.BadTex, 0.82f);

            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x, rect.y - 22f, rect.width, 20f), slotLabel);
            Text.Anchor = TextAnchor.LowerCenter;
            string contents = bombDef == null
                ? "HD_Aircraft_BombSlot_Empty".Translate().Resolve()
                : bombDef.LabelCap.Resolve() + (count > 1 ? " x" + count : string.Empty);
            Widgets.Label(rect.ContractedBy(3f), contents);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (bombDef != null)
                TooltipHandler.TipRegion(rect, "HD_Aircraft_BombSlot_Tooltip".Translate(
                    bombDef.LabelCap, CompAircraftBombBay.UnitMass(bombDef).ToString("0.#")));
            if (Widgets.ButtonInvisible(rect)) clicked();
        }
    }

    public sealed class Dialog_AircraftBombSlotPicker : Window
    {
        private readonly CompAircraftBombBay bay;
        private readonly bool centerSlot;
        private readonly int wingStartIndex;
        private ThingDef selectedDef;
        private int quantity = 1;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(690f, 570f);

        public Dialog_AircraftBombSlotPicker(CompAircraftBombBay bay, bool centerSlot,
            int wingStartIndex)
        {
            this.bay = bay;
            this.centerSlot = centerSlot;
            this.wingStartIndex = wingStartIndex;
            selectedDef = centerSlot ? bay.CenterBombDef : bay.WingPairBombDefs[wingStartIndex];
            quantity = selectedDef == null ? 1
                : centerSlot ? bay.CenterBombCount : bay.WingPairBombCounts[wingStartIndex];
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f),
                "HD_Aircraft_BombSlotPicker_Title".Translate(
                    centerSlot ? "HD_Aircraft_BombSlot_Center".Translate()
                        : "HD_Aircraft_BombSlot_WingPair".Translate(wingStartIndex + 1)).Resolve());
            Text.Font = GameFont.Small;

            List<ThingDef> bombDefs = bay.AllowedBombDefs.ToList();
            const float cardWidth = 150f;
            const float cardHeight = 164f;
            const float gap = 10f;
            Rect outRect = new Rect(inRect.x, inRect.y + 42f, inRect.width,
                inRect.height - 142f);
            int columns = Mathf.Max(1, Mathf.FloorToInt((outRect.width - 16f) / (cardWidth + gap)));
            int rows = Mathf.CeilToInt(bombDefs.Count / (float)columns);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f,
                Mathf.Max(outRect.height, rows * (cardHeight + gap)));
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect, true);
            for (int index = 0; index < bombDefs.Count; index++)
            {
                int column = index % columns;
                int row = index / columns;
                DrawBombCard(bombDefs[index], new Rect(column * (cardWidth + gap),
                    row * (cardHeight + gap), cardWidth, cardHeight));
            }
            Widgets.EndScrollView();

            float controlsY = inRect.yMax - 90f;
            int maxQuantity = selectedDef == null ? 1 : bay.MaxCountPerSlot(selectedDef);
            TaggedString quantityLabel = centerSlot
                ? "HD_Aircraft_BombSlotPicker_CenterQuantity".Translate(quantity)
                : "HD_Aircraft_BombSlotPicker_Quantity".Translate(quantity, quantity * 2);
            Widgets.Label(new Rect(inRect.x, controlsY, 255f, 32f), quantityLabel.Resolve());
            if (Widgets.ButtonText(new Rect(inRect.x + 260f, controlsY, 42f, 32f), "-"))
                quantity = Mathf.Max(1, quantity - 1);
            if (Widgets.ButtonText(new Rect(inRect.x + 308f, controlsY, 42f, 32f), "+"))
                quantity = Mathf.Min(maxQuantity, quantity + 1);

            Rect clearRect = new Rect(inRect.x, inRect.yMax - 44f, 110f, 38f);
            Rect applyRect = new Rect(inRect.xMax - 238f, inRect.yMax - 44f, 110f, 38f);
            Rect cancelRect = new Rect(inRect.xMax - 120f, inRect.yMax - 44f, 110f, 38f);
            if (Widgets.ButtonText(clearRect, "HD_Aircraft_BombSlotPicker_Clear".Translate().Resolve()))
                Apply(null);
            if (Widgets.ButtonText(applyRect, "AcceptButton".Translate().Resolve()))
            {
                if (selectedDef == null)
                    Messages.Message("HD_Aircraft_BombSlotPicker_Select".Translate(), bay.parent,
                        MessageTypeDefOf.RejectInput, false);
                else
                    Apply(selectedDef);
            }
            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate().Resolve())) Close();
        }

        private void DrawBombCard(ThingDef bombDef, Rect rect)
        {
            Color background = selectedDef == bombDef
                ? new Color(0.28f, 0.42f, 0.56f, 0.95f)
                : new Color(0.12f, 0.13f, 0.15f, 0.96f);
            Widgets.DrawBoxSolid(rect, background);
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            Widgets.DrawBox(rect, selectedDef == bombDef ? 3 : 1);
            Widgets.DrawTextureFitted(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 84f),
                bombDef.uiIcon ?? BaseContent.BadTex, 0.9f);
            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 4f, rect.y + 94f, rect.width - 8f, 22f), bombDef.LabelCap);
            Widgets.Label(new Rect(rect.x + 4f, rect.y + 116f, rect.width - 8f, 44f),
                "HD_Aircraft_BombSlotPicker_Info".Translate(
                    CompAircraftBombBay.UnitMass(bombDef).ToString("0.#"),
                    bay.AvailableOnMap(bombDef), bay.MaxCountPerSlot(bombDef)).Resolve());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (Widgets.ButtonInvisible(rect))
            {
                selectedDef = bombDef;
                quantity = Mathf.Clamp(quantity, 1, bay.MaxCountPerSlot(bombDef));
            }
        }

        private void Apply(ThingDef bombDef)
        {
            string rejection;
            bool applied = centerSlot
                ? bay.TryConfigureCenter(bombDef, quantity, out rejection)
                : bay.TryConfigureWingSlot(wingStartIndex, bombDef, quantity, out rejection);
            if (applied) Close();
            else Messages.Message(rejection, bay.parent, MessageTypeDefOf.RejectInput, false);
        }
    }

    public sealed class WorkGiver_LoadAircraftBombs : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Refuelable);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            CompAircraftBombBay bay = thing.TryGetComp<CompAircraftBombBay>();
            return bay != null && pawn.CanReserve(thing) && bay.NextBombFor(pawn) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            CompAircraftBombBay bay = thing.TryGetComp<CompAircraftBombBay>();
            Thing bomb = bay?.NextBombFor(pawn);
            if (bomb == null) return null;
            int count = Mathf.Min(bay.CountThatFits(bomb), bay.NeededCount(bomb.def));
            if (count <= 0) return null;
            Job job = JobMaker.MakeJob(AircraftJobDefOf.HD_LoadAircraftBomb, thing, bomb);
            job.count = count;
            return job;
        }
    }

    public sealed class JobDriver_LoadAircraftBomb : JobDriver
    {
        private AircraftThing Aircraft => job.targetA.Thing as AircraftThing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.targetB, job, 1, job.count, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnForbidden(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, false,
                true, false, false);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil load = ToilMaker.MakeToil("LoadAircraftBomb");
            load.initAction = () =>
            {
                CompAircraftBombBay bay = Aircraft?.TryGetComp<CompAircraftBombBay>();
                bool accepted = bay != null && bay.TryAcceptBombFrom(pawn, job.count);
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position,
                        ThingPlaceMode.Near, out Thing _);
                if (!accepted)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                job.count = 0;
                EndJobWith(JobCondition.Succeeded);
            };
            load.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return load;
        }
    }
}
