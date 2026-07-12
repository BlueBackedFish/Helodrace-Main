using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace
{
    public class MapComponent_MortarRaidAI : MapComponent
    {
        private readonly HashSet<int> activatedBaseplateCarriers = new HashSet<int>();
        private readonly Dictionary<int, int> firstSeenTick = new Dictionary<int, int>();
        private readonly HashSet<int> coveredAutomaticRiflemen = new HashSet<int>();
        private readonly HashSet<int> releasedRaidAnchors = new HashSet<int>();
        private readonly HashSet<int> retreatingRaidAnchors = new HashSet<int>();
        private readonly Dictionary<int, int> coverBuiltCount = new Dictionary<int, int>();
        private readonly Dictionary<int, int> initialColonistCount = new Dictionary<int, int>();
        private readonly HashSet<int> leaderLossHandledAnchors = new HashSet<int>();
        private readonly HashSet<int> disorganizedRaidAnchors = new HashSet<int>();
        private readonly HashSet<int> leaderConfirmedAnchors = new HashSet<int>();
        private readonly HashSet<int> abandonedCasualtyIds = new HashSet<int>();

        public MapComponent_MortarRaidAI(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            List<int> activated = activatedBaseplateCarriers.ToList();
            List<int> covered = coveredAutomaticRiflemen.ToList();
            List<int> released = releasedRaidAnchors.ToList();
            List<int> retreating = retreatingRaidAnchors.ToList();
            List<int> keys = firstSeenTick.Keys.ToList();
            List<int> values = keys.Select(k => firstSeenTick[k]).ToList();
            List<int> colonistKeys = initialColonistCount.Keys.ToList();
            List<int> colonistValues = colonistKeys.Select(k => initialColonistCount[k]).ToList();
            List<int> leaderHandled = leaderLossHandledAnchors.ToList();
            List<int> disorganized = disorganizedRaidAnchors.ToList();
            List<int> leaderConfirmed = leaderConfirmedAnchors.ToList();
            Scribe_Collections.Look(ref activated, "activatedMortarBaseplateCarriers", LookMode.Value);
            Scribe_Collections.Look(ref covered, "coveredAutomaticRiflemen", LookMode.Value);
            Scribe_Collections.Look(ref released, "releasedMortarRaidAnchors", LookMode.Value);
            Scribe_Collections.Look(ref retreating, "retreatingMortarRaidAnchors", LookMode.Value);
            Scribe_Collections.Look(ref keys, "mortarRaidFirstSeenIds", LookMode.Value);
            Scribe_Collections.Look(ref values, "mortarRaidFirstSeenTicks", LookMode.Value);
            Scribe_Collections.Look(ref colonistKeys, "mortarRaidInitialColonistIds", LookMode.Value);
            Scribe_Collections.Look(ref colonistValues, "mortarRaidInitialColonistCounts", LookMode.Value);
            Scribe_Collections.Look(ref leaderHandled, "mortarRaidLeaderLossHandled", LookMode.Value);
            Scribe_Collections.Look(ref disorganized, "mortarRaidDisorganized", LookMode.Value);
            Scribe_Collections.Look(ref leaderConfirmed, "mortarRaidLeaderConfirmed", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                activatedBaseplateCarriers.Clear();
                coveredAutomaticRiflemen.Clear();
                releasedRaidAnchors.Clear();
                retreatingRaidAnchors.Clear();
                firstSeenTick.Clear();
                initialColonistCount.Clear();
                leaderLossHandledAnchors.Clear();
                disorganizedRaidAnchors.Clear();
                leaderConfirmedAnchors.Clear();
                if (activated != null) foreach (int id in activated) activatedBaseplateCarriers.Add(id);
                if (covered != null) foreach (int id in covered) coveredAutomaticRiflemen.Add(id);
                if (released != null) foreach (int id in released) releasedRaidAnchors.Add(id);
                if (retreating != null) foreach (int id in retreating) retreatingRaidAnchors.Add(id);
                if (keys != null && values != null)
                    for (int i = 0; i < keys.Count && i < values.Count; i++) firstSeenTick[keys[i]] = values[i];
                if (colonistKeys != null && colonistValues != null)
                    for (int i = 0; i < colonistKeys.Count && i < colonistValues.Count; i++) initialColonistCount[colonistKeys[i]] = colonistValues[i];
                if (leaderHandled != null) foreach (int id in leaderHandled) leaderLossHandledAnchors.Add(id);
                if (disorganized != null) foreach (int id in disorganized) disorganizedRaidAnchors.Add(id);
                if (leaderConfirmed != null) foreach (int id in leaderConfirmed) leaderConfirmedAnchors.Add(id);
            }
        }

        public override void MapComponentTick()
        {
            if (!map.IsHashIntervalTick(30)) return;

            List<Pawn> hostiles = map.mapPawns.AllPawnsSpawned
                .Where(p => p.RaceProps.Humanlike && p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer) && !p.Dead)
                .ToList();

            foreach (IGrouping<Faction, Pawn> group in hostiles.GroupBy(p => p.Faction))
            {
                Pawn baseCarrier = group.FirstOrDefault(p => firstSeenTick.ContainsKey(p.thingIDNumber));
                if (baseCarrier == null)
                {
                    baseCarrier = group.FirstOrDefault(p => MortarAssemblyUtility.Worn(p, MortarAssemblyUtility.BaseplateDef) != null);
                    if (baseCarrier == null
                        || !group.Any(p => MortarAssemblyUtility.Worn(p, MortarAssemblyUtility.MountDef) != null)
                        || !group.Any(p => MortarAssemblyUtility.Worn(p, MortarAssemblyUtility.BarrelDef) != null))
                        continue;
                }

                if (!firstSeenTick.ContainsKey(baseCarrier.thingIDNumber))
                {
                    firstSeenTick[baseCarrier.thingIDNumber] = Find.TickManager.TicksGame;
                    initialColonistCount[baseCarrier.thingIDNumber] = map.mapPawns.FreeColonistsSpawned.Count;
                }

                int anchorId = baseCarrier.thingIDNumber;
                if (group.Any(p => p.kindDef?.defName == "HD_GW_HelodSquadLeader"))
                    leaderConfirmedAnchors.Add(anchorId);
                Building_TurretGun mortar = map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("HD_Building_M1_81mmMortar"))
                    .OfType<Building_TurretGun>().FirstOrDefault(b => b.Faction == group.Key);

                if (retreatingRaidAnchors.Contains(anchorId))
                {
                    TryEvacuateCasualties(group.ToList());
                    OrderRetreat(group.ToList());
                    continue;
                }

                if (HandleSquadLeaderLoss(group.ToList(), anchorId, mortar))
                    continue;

                if (HandleAutomaticRiflemenLost(group.ToList(), anchorId, mortar))
                    continue;

                if (!releasedRaidAnchors.Contains(anchorId))
                {
                    BuildCoverAndDeployAutomaticRiflemen(group.ToList(), baseCarrier);
                }

                int elapsed = Find.TickManager.TicksGame - firstSeenTick[baseCarrier.thingIDNumber];
                if (elapsed >= 420 && !activatedBaseplateCarriers.Contains(baseCarrier.thingIDNumber)
                    && TryStartMortarAssemblyAtSafeCell(baseCarrier))
                    activatedBaseplateCarriers.Add(baseCarrier.thingIDNumber);

                if (mortar != null)
                {
                    if (!MortarInteractionCellSafe(mortar))
                    {
                        TryEvacuateCasualties(group.ToList());
                        RecoverMortarParts(group.ToList(), mortar);
                        retreatingRaidAnchors.Add(anchorId);
                        OrderRetreat(group.ToList());
                        continue;
                    }
                    ManMortarAndSupplyShells(group.ToList(), mortar);
                    bool hasFired = mortar.LastAttackTargetTick > firstSeenTick[anchorId];
                    if (hasFired && AmmunitionExhausted(group.ToList(), mortar))
                    {
                        if (ColonySufficientlyWeakened(anchorId))
                        {
                            releasedRaidAnchors.Add(anchorId);
                            BeginAssault(group.ToList(), group.Key);
                        }
                        else
                        {
                            TryEvacuateCasualties(group.ToList());
                            RecoverMortarParts(group.ToList(), mortar);
                            retreatingRaidAnchors.Add(anchorId);
                            OrderRetreat(group.ToList());
                        }
                    }
                }
            }
        }

        private bool TryStartMortarAssemblyAtSafeCell(Pawn baseCarrier)
        {
            IntVec3 cell = IntVec3.Invalid;
            IEnumerable<IntVec3> candidates = GenRadial.RadialCellsAround(baseCarrier.Position, 20f, true)
                .Where(c => c.InBounds(map) && MortarAssemblyUtility.CanPlaceMortarAt(baseCarrier, c))
                .OrderBy(c => c.DistanceToSquared(map.Center));
            foreach (IntVec3 candidate in candidates) { cell = candidate; break; }
            if (!cell.IsValid) return false;
            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_BeginM1MortarAssembly");
            if (jobDef == null) return false;
            baseCarrier.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, cell), JobTag.Misc);
            return true;
        }

        private static bool MortarInteractionCellSafe(Building_TurretGun mortar)
        {
            if (mortar?.Map == null || !mortar.Position.InBounds(mortar.Map)) return false;
            IntVec3 interaction = mortar.InteractionCell;
            return interaction.IsValid && interaction.InBounds(mortar.Map) && interaction.Standable(mortar.Map)
                && !mortar.Map.terrainGrid.TerrainAt(mortar.Position).IsWater
                && !mortar.Map.terrainGrid.TerrainAt(interaction).IsWater;
        }

        private void BuildCoverAndDeployAutomaticRiflemen(List<Pawn> pawns, Pawn anchor)
        {
            ThingDef sandbags = DefDatabase<ThingDef>.GetNamedSilentFail("Sandbags");
            if (sandbags == null) return;

            List<Pawn> automaticRiflemen = pawns.Where(p => p.kindDef?.defName == "HD_GW_HelodAutomaticRifleman").OrderBy(p => p.thingIDNumber).ToList();
            if (automaticRiflemen.Count == 0 || coverBuiltCount.ContainsKey(anchor.thingIDNumber)) return;

            IntVec3 towardColony = CardinalDirectionToward(anchor.Position, map.Center);
            IntVec3 side = new IntVec3(-towardColony.z, 0, towardColony.x);
            int lineLength = System.Math.Max(5, automaticRiflemen.Count * 2 + 1);
            IntVec3 lineCenter;
            if (!TryFindStraightCoverLine(anchor.Position, towardColony, side, lineLength, out lineCenter)) return;

            int half = lineLength / 2;
            for (int offset = -half; offset <= half; offset++)
            {
                IntVec3 cell = lineCenter + side * offset;
                GenSpawn.Spawn(ThingMaker.MakeThing(sandbags, ThingDefOf.Cloth), cell, map);
            }
            coverBuiltCount[anchor.thingIDNumber] = lineLength;

            for (int i = 0; i < automaticRiflemen.Count; i++)
            {
                Pawn rifleman = automaticRiflemen[i];
                int offset = i - (automaticRiflemen.Count - 1) / 2;
                IntVec3 firingCell = lineCenter - towardColony + side * (offset * 2);
                rifleman.mindState.duty = new PawnDuty(DutyDefOf.Defend, firingCell, 1.2f);
                coveredAutomaticRiflemen.Add(rifleman.thingIDNumber);
            }
        }

        private bool TryFindStraightCoverLine(IntVec3 anchor, IntVec3 forward, IntVec3 side, int length, out IntVec3 center)
        {
            int half = length / 2;
            for (int distance = 3; distance <= 9; distance++)
            for (int lateral = 0; lateral <= 5; lateral++)
            for (int sign = lateral == 0 ? 0 : -1; sign <= 1; sign += 2)
            {
                IntVec3 candidate = anchor + forward * distance + side * (lateral * sign);
                bool clear = true;
                for (int offset = -half; offset <= half; offset++)
                {
                    IntVec3 cell = candidate + side * offset;
                    if (!cell.InBounds(map) || !cell.Walkable(map) || cell.GetEdifice(map) != null || cell.GetFirstItem(map) != null)
                    {
                        clear = false;
                        break;
                    }
                }
                if (clear) { center = candidate; return true; }
                if (lateral == 0) break;
            }
            center = IntVec3.Invalid;
            return false;
        }

        private static IntVec3 CardinalDirectionToward(IntVec3 from, IntVec3 to)
        {
            int dx = to.x - from.x;
            int dz = to.z - from.z;
            return System.Math.Abs(dx) >= System.Math.Abs(dz)
                ? new IntVec3(System.Math.Sign(dx), 0, 0)
                : new IntVec3(0, 0, System.Math.Sign(dz));
        }

        private void HoldAtFieldPosition(List<Pawn> pawns, Pawn anchor, Building_TurretGun mortar)
        {
            List<Pawn> automatic = pawns.Where(p => p.kindDef?.defName == "HD_GW_HelodAutomaticRifleman").OrderBy(p => p.thingIDNumber).ToList();
            IntVec3 towardColony = new IntVec3(System.Math.Sign(map.Center.x - anchor.Position.x), 0, System.Math.Sign(map.Center.z - anchor.Position.z));
            IntVec3 side = new IntVec3(-towardColony.z, 0, towardColony.x);
            foreach (Pawn pawn in pawns)
            {
                string jobName = pawn.CurJobDef?.defName;
                if (jobName == "HD_AssembleM1MortarMount" || jobName == "HD_AssembleM1MortarBarrel" || jobName == "ManTurret")
                    continue;
                int autoIndex = automatic.IndexOf(pawn);
                IntVec3 assignedCell = autoIndex >= 0
                    ? anchor.Position + towardColony * 2 + side * ((autoIndex - (automatic.Count - 1) / 2) * 2)
                    : (pawn == anchor ? anchor.Position : anchor.Position + new IntVec3((pawn.thingIDNumber % 5) - 2, 0, ((pawn.thingIDNumber / 5) % 5) - 2));
                CompSharpshooterWeapon mode = autoIndex >= 0 ? pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>() : null;
                if (pawn.Position != assignedCell && assignedCell.Standable(map))
                {
                    if (mode != null && mode.altModeActive)
                        mode.PerformSwitch();
                    pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, assignedCell), JobTag.Misc);
                }
                else if (pawn.Position.DistanceTo(anchor.Position) > 12f)
                {
                    if (mode != null && mode.altModeActive)
                        mode.PerformSwitch();
                    pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, CellFinder.RandomClosewalkCellNear(anchor.Position, map, 8)), JobTag.Misc);
                }
                else if (pawn.CurJobDef != JobDefOf.Wait_MaintainPosture)
                {
                    Job wait = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture);
                    wait.expiryInterval = 180;
                    pawn.jobs.TryTakeOrderedJob(wait, JobTag.Misc);
                    if (mode != null && mode.CanUseSharpshooterMode && !mode.altModeActive)
                        mode.PerformSwitch();
                }
            }
        }

        private void ManMortarAndSupplyShells(List<Pawn> pawns, Building_TurretGun mortar)
        {
            Pawn shellBearer = pawns.FirstOrDefault(p => p.kindDef?.defName == "HD_GW_HelodMortarShellBearer");
            if (shellBearer == null || shellBearer.Downed) return;
            ThingDef shellDef = DefDatabase<ThingDef>.GetNamed("HD_81mmMortarShell_M43HE");
            Thing shells = shellBearer.inventory.innerContainer.FirstOrDefault(t => t.def == shellDef);
            CompChangeableProjectile loader = mortar.GunCompEq?.parent?.TryGetComp<CompChangeableProjectile>();
            if (shells != null && loader != null && !loader.Loaded && shellBearer.Position.DistanceTo(mortar.Position) <= 3f)
            {
                int count = shells.stackCount;
                shellBearer.inventory.innerContainer.Remove(shells);
                loader.LoadShell(shellDef, count);
                shells.Destroy();
            }
            if (shellBearer.CurJobDef != JobDefOf.ManTurret)
                shellBearer.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.ManTurret, mortar), JobTag.Misc);
        }

        private bool AmmunitionExhausted(List<Pawn> pawns, Building_TurretGun mortar)
        {
            ThingDef shellDef = DefDatabase<ThingDef>.GetNamed("HD_81mmMortarShell_M43HE");
            CompChangeableProjectile loader = mortar.GunCompEq?.parent?.TryGetComp<CompChangeableProjectile>();
            return (loader == null || !loader.Loaded)
                && !pawns.Any(p => p.inventory?.innerContainer.Any(t => t.def == shellDef) == true)
                && !map.listerThings.ThingsOfDef(shellDef).Any(t => t.Position.DistanceTo(mortar.Position) <= 12f);
        }

        private bool ColonySufficientlyWeakened(int anchorId)
        {
            int initial = initialColonistCount.TryGetValue(anchorId, out int count) ? count : map.mapPawns.FreeColonistsSpawned.Count;
            int current = map.mapPawns.FreeColonistsSpawned.Count;
            int deadOrMissing = System.Math.Max(0, initial - current);
            int downed = map.mapPawns.FreeColonistsSpawned.Count(p => p.Downed);
            int neutralized = deadOrMissing + downed;
            int required = System.Math.Max(1, (int)System.Math.Ceiling(initial * 0.34f));
            return neutralized >= required;
        }

        private bool HandleAutomaticRiflemenLost(List<Pawn> pawns, int anchorId, Building_TurretGun mortar)
        {
            if (pawns.Any(p => p.kindDef?.defName == "HD_GW_HelodAutomaticRifleman" && IsMobile(p)))
                return false;

            TryEvacuateCasualties(pawns);

            List<Pawn> mobile = pawns.Where(IsMobile).ToList();
            if (mobile.Count >= 3 && mortar != null)
                RecoverMortarParts(pawns, mortar);
            else if (mobile.Count >= 1 && mortar != null && !mortar.Destroyed)
                mortar.Destroy(DestroyMode.KillFinalize);

            if (mortar == null)
            {
                Pawn baseCarrier = pawns.FirstOrDefault(p => firstSeenTick.ContainsKey(p.thingIDNumber));
                if (baseCarrier?.Map != null)
                    MortarAssemblyUtility.RemoveAssemblyBlueprint(baseCarrier.Position, baseCarrier.Map);
            }

            CancelMortarJobs(pawns);
            retreatingRaidAnchors.Add(anchorId);
            OrderRetreat(mobile);
            return true;
        }

        private bool HandleSquadLeaderLoss(List<Pawn> pawns, int anchorId, Building_TurretGun mortar)
        {
            if (!leaderConfirmedAnchors.Contains(anchorId)
                || pawns.Any(p => p.kindDef?.defName == "HD_GW_HelodSquadLeader")
                || leaderLossHandledAnchors.Contains(anchorId))
                return false;

            TryEvacuateCasualties(pawns);

            leaderLossHandledAnchors.Add(anchorId);
            if (!Rand.Chance(0.55f))
            {
                disorganizedRaidAnchors.Add(anchorId);
                return false;
            }

            List<Pawn> mobile = pawns.Where(IsMobile).ToList();
            if (mobile.Count >= 3 && mortar != null) RecoverMortarParts(pawns, mortar);
            else if (mobile.Count >= 1 && mortar != null && !mortar.Destroyed) mortar.Destroy(DestroyMode.KillFinalize);
            CancelMortarJobs(pawns);
            retreatingRaidAnchors.Add(anchorId);
            OrderRetreat(mobile);
            return true;
        }

        public bool TacticalActionAllowed(Faction faction)
        {
            bool disorganized = map.mapPawns.AllPawnsSpawned.Any(p => p.Faction == faction
                && firstSeenTick.ContainsKey(p.thingIDNumber) && disorganizedRaidAnchors.Contains(p.thingIDNumber));
            return !disorganized || Rand.Chance(0.25f);
        }

        private static bool IsMobile(Pawn pawn)
        {
            return pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving);
        }

        private static void CancelMortarJobs(List<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns.Where(IsMobile))
            {
                string name = pawn.CurJobDef?.defName;
                if (name == "HD_AssembleM1MortarMount" || name == "HD_AssembleM1MortarBarrel" || name == "ManTurret")
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        private void RecoverMortarParts(List<Pawn> pawns, Building_TurretGun mortar)
        {
            JobDef evacDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_EvacuateRaidCasualty");
            List<Pawn> recoveryCrew = pawns.Where(IsMobile)
                .Where(p => evacDef == null || p.CurJobDef != evacDef)
                .OrderBy(p => p.Position.DistanceToSquared(mortar.Position)).Take(3).ToList();
            if (recoveryCrew.Count < 3)
            {
                mortar.Destroy(DestroyMode.KillFinalize);
                return;
            }

            mortar.Destroy(DestroyMode.Vanish);
            EquipRecoveredPart(recoveryCrew[0], MortarAssemblyUtility.BaseplateDef);
            EquipRecoveredPart(recoveryCrew[1], MortarAssemblyUtility.MountDef);
            EquipRecoveredPart(recoveryCrew[2], MortarAssemblyUtility.BarrelDef);
        }

        private bool TryEvacuateCasualties(List<Pawn> pawns)
        {
            List<Pawn> casualties = pawns.Where(p => p.Downed && p.Spawned && !p.Dead
                && !abandonedCasualtyIds.Contains(p.thingIDNumber)).ToList();
            if (casualties.Count == 0) return false;
            JobDef evacDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_EvacuateRaidCasualty");
            if (evacDef == null) return false;

            var assignedCarriers = new HashSet<Pawn>(pawns.Where(p => p.CurJobDef == evacDef));
            var assignedCasualties = new HashSet<Pawn>(assignedCarriers
                .Select(p => p.CurJob?.targetA.Pawn).Where(p => p != null));
            foreach (Pawn casualty in casualties)
            {
                if (assignedCasualties.Contains(casualty)) continue;
                Pawn carrier = pawns.Where(IsMobile)
                    .Where(p => !assignedCarriers.Contains(p) && p.CanReserveAndReach(casualty, PathEndMode.Touch, Danger.Deadly))
                    .OrderBy(p => p.Position.DistanceToSquared(casualty.Position)).FirstOrDefault();
                if (carrier == null) continue;
                IntVec3 exit = IntVec3.Invalid;
                IEnumerable<IntVec3> exits = map.AllCells.Where(c => c.OnEdge(map) && c.Standable(map)
                    && carrier.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
                    .OrderBy(c => carrier.Position.DistanceToSquared(c));
                foreach (IntVec3 candidate in exits) { exit = candidate; break; }
                if (!exit.IsValid) continue;
                assignedCarriers.Add(carrier);
                assignedCasualties.Add(casualty);
                carrier.jobs.TryTakeOrderedJob(JobMaker.MakeJob(evacDef, casualty, exit), JobTag.Misc);
            }

            foreach (Pawn casualty in casualties.Where(c => !assignedCasualties.Contains(c)))
                abandonedCasualtyIds.Add(casualty.thingIDNumber);
            return assignedCarriers.Count > 0;
        }

        private static void EquipRecoveredPart(Pawn carrier, ThingDef partDef)
        {
            if (carrier == null) return;
            Apparel part = (Apparel)ThingMaker.MakeThing(partDef);
            if (carrier.apparel.CanWearWithoutDroppingAnything(partDef)) carrier.apparel.Wear(part, false);
            else carrier.inventory.innerContainer.TryAdd(part);
        }

        private static void OrderRetreat(List<Pawn> pawns)
        {
            JobDef evacDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_EvacuateRaidCasualty");
            bool evacuationRunning = evacDef != null && pawns.Any(p => p.CurJobDef == evacDef);
            if (evacuationRunning)
            {
                Pawn casualty = pawns.FirstOrDefault(p => p.Downed && p.Spawned && !p.Dead);
                IntVec3 supportCenter = casualty?.Position ?? pawns.Where(IsMobile).Select(p => p.Position).FirstOrDefault();
                foreach (Pawn pawn in pawns.Where(IsMobile).Where(p => p.CurJobDef != evacDef))
                {
                    if (pawn.mindState.duty?.def == DutyDefOf.Defend)
                        continue;
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend, supportCenter, 12f)
                    {
                        locomotion = LocomotionUrgency.Jog,
                        maxDanger = Danger.Deadly
                    };
                    if (pawn.CurJob != null) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                return;
            }

            Lord lord = pawns.Select(p => p.GetLord()).FirstOrDefault(l => l != null);
            if (lord != null)
            {
                if (!(lord.LordJob is LordJob_MortarRaidRetreat))
                    lord.SetJob(new LordJob_MortarRaidRetreat());
                return;
            }

            foreach (Pawn pawn in pawns.Where(IsMobile))
            {
                CompSharpshooterWeapon mode = pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
                if (mode != null && mode.altModeActive) mode.PerformSwitch();
                pawn.mindState.duty = new PawnDuty(DutyDefOf.ExitMapBest)
                {
                    locomotion = LocomotionUrgency.Sprint,
                    maxDanger = Danger.Deadly
                };
            }
        }

        private static void BeginAssault(List<Pawn> pawns, Faction faction)
        {
            Lord lord = pawns.Select(p => p.GetLord()).FirstOrDefault(l => l != null);
            if (lord != null)
                lord.SetJob(new LordJob_AssaultColony(faction, true, true, false, false, true, false, false));
        }
    }

    public class JobDriver_EvacuateRaidCasualty : JobDriver
    {
        private Pawn Casualty => job.targetA.Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Casualty, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Casualty == null || Casualty.Dead || !Casualty.Downed);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, false, false);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            yield return Toils_General.Do(() => pawn.ExitMap(false, Rot4.Invalid));
        }
    }
}
