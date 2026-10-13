using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using LudeonTK;

namespace Helodrace.Squads
{
    public sealed class MortarRaidExtension : DefModExtension { public bool chemical; }
    public enum RaidMortarPhase { Assemble, Fire, Recover }
    public static class RaidMortarPolicy
    {
        public static int KitCost(bool chemical) => chemical ? 450 : 350;
        public static int ShellCount(bool chemical) => chemical ? 6 : 12;
        public static bool Withdraw(bool chemical, int initial, int able, int shots, int elapsed) =>
            initial > 0 && (initial - able >= Math.Max(1, (int)Math.Ceiling(initial * (chemical ? .15 : .25))))
            || shots >= ShellCount(chemical) || elapsed >= 18000;
        public static bool AssemblyExpired(int elapsed, int attempts) => elapsed >= 2400 || attempts >= 3;
        public static bool RecoveryExpired(int elapsed) => elapsed >= 900;
    }
    public sealed class RaidStrategyWorker_HelodMortar : RaidStrategyWorker
    {
        public bool Chemical => def.GetModExtension<MortarRaidExtension>()?.chemical == true;
        public int KitCost => RaidMortarPolicy.KitCost(Chemical);
        public override float MinimumPoints(Faction faction, PawnGroupKindDef groupKind) => base.MinimumPoints(faction, groupKind) + KitCost;
        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind) =>
            parms.faction?.def.GetModExtension<FactionOrganizationExtension>()?.doctrine != null
            && parms.faction.HostileTo(Faction.OfPlayer)
            && (!Chemical || parms.faction.def.defName == "HD_HelodCivilLowFaction") && base.CanUseWith(parms, groupKind);
        public override void MakeLords(IncidentParms parms, List<Pawn> pawns)
        {
            // Organization arrival groups are spawn positions, not free extra mortar kits.
            if (pawns.Count == 0) return;
            var map = (Map)parms.target;
            Lord lord = LordMaker.MakeNewLord(parms.faction, MakeLordJob(parms, map, pawns, Rand.Int), map, pawns);
            lord.inSignalLeave = parms.inSignalEnd; QuestUtility.AddQuestTag(lord, parms.questTag);
        }
        protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
        {
            Pawn[] crew = pawns.Where(p => !p.Downed && !p.Dead && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                && !OrganizationAPI.GetGroup(p).roleAssignments.Any(a => a.pawn == p && a.medicalDuty)
                && p.equipment?.Primary?.def.IsRangedWeapon == true)
                .OrderBy(p => OrganizationAPI.GetGroup(p).roleAssignments.First(a => a.pawn == p).combatRole?.combatFunction == "HAR" ? 1 : 0)
                .ThenBy(p => OrganizationAPI.GetGroup(p).EffectiveCommander == p ? 1 : 0).Take(3).ToArray();
            var organization = RaidTacticalUnit.ForPawn(pawns[0]).Organization;
            if (crew.Length < 3 || organization.MortarKitReservation == 0)
            { organization.FinishSupport(organization.MortarKitReservation, false); organization.MortarKitReservation = 0; return new LordJob_HelodSweep(parms.faction); }
            ThingDef[] parts = { MortarAssemblyUtility.BaseplateDef, MortarAssemblyUtility.MountDef, MortarAssemblyUtility.BarrelDef };
            for (int i = 0; i < 3; i++) crew[i].apparel.Wear((Apparel)ThingMaker.MakeThing(parts[i]), false);
            ThingDef shell = DefDatabase<ThingDef>.GetNamed(Chemical ? "HD_81mmMortarShell_BA" : "HD_81mmMortarShell_M43HE");
            foreach (Pawn carrier in crew)
            {
                Thing ammunition = ThingMaker.MakeThing(shell); ammunition.stackCount = RaidMortarPolicy.ShellCount(Chemical) / 3;
                if (!carrier.inventory.innerContainer.TryAdd(ammunition)) ammunition.Destroy(DestroyMode.Vanish);
            }
            if (Chemical)
                foreach (Pawn pawn in pawns)
                {
                    ThingDef pouchDef = DefDatabase<ThingDef>.GetNamed("HD_Apparel_GreatWarCBRNPouch");
                    var pouch = (Apparel)ThingMaker.MakeThing(pouchDef, GenStuff.DefaultStuffFor(pouchDef));
                    pawn.apparel.Wear(pouch, false); pouch.GetComp<CompGasMaskPouch>().SetMaskWorn(true);
                }
            organization.FinishSupport(organization.MortarKitReservation, true); organization.MortarKitReservation = 0;
            IntVec3 spot = RCellFinder.FindSiegePositionFrom(parms.spawnCenter.IsValid ? parms.spawnCenter : pawns[0].Position, map);
            return new LordJob_HelodMortar(spot, crew.ToList(), Chemical);
        }
    }
    public sealed partial class CombatOrganization
    {
        public int MortarKitReservation;
    }
    public sealed class LordJob_HelodMortar : LordJob
    {
        private IntVec3 spot;
        private List<Pawn> crew;
        private bool chemical;
        public LordJob_HelodMortar() { }
        public LordJob_HelodMortar(IntVec3 spot, List<Pawn> crew, bool chemical)
        { this.spot = spot; this.crew = crew; this.chemical = chemical; }
        public override bool GuiltyOnDowned => true;
        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            LordToil travel = graph.AttachSubgraph(new LordJob_Travel(spot).CreateGraph()).StartingToil;
            var mortar = new LordToil_HelodMortar(spot, crew, chemical);
            var exit = new LordToil_ExitMap(LocomotionUrgency.Jog, false, true) { useAvoidGrid = true };
            graph.AddToil(mortar); graph.AddToil(exit);
            var arrive = new Transition(travel, mortar);
            arrive.AddTrigger(new Trigger_Memo("TravelArrived")); arrive.AddTrigger(new Trigger_TicksPassed(3000));
            graph.AddTransition(arrive);
            var depart = new Transition(mortar, exit);
            depart.AddTrigger(new Trigger_Memo("HelodMortarLeave")); depart.AddPostAction(new TransitionAction_WakeAll()); graph.AddTransition(depart);
            var abortTravel = new Transition(travel, exit);
            abortTravel.AddTrigger(new Trigger_FractionPawnsLost(chemical ? .15f : .25f));
            abortTravel.AddTrigger(new Trigger_BecameNonHostileToPlayer()); graph.AddTransition(abortTravel);
            return graph;
        }
        public override void ExposeData()
        { Scribe_Values.Look(ref spot, "spot"); Scribe_Collections.Look(ref crew, "crew", LookMode.Reference); Scribe_Values.Look(ref chemical, "chemical"); }
    }
    public sealed class LordToilData_HelodMortar : LordToilData
    {
        public Building_TurretGun Mortar;
        public RaidMortarPhase Phase;
        public IntVec3 Spot;
        public List<Pawn> Crew = new List<Pawn>();
        public Pawn Worker;
        public int Initial, Started, PhaseAt, RetryAt, Attempts, Shots;
        public bool Chemical, HadLoaded;
        public override void ExposeData()
        {
            Scribe_References.Look(ref Mortar, "mortar"); Scribe_References.Look(ref Worker, "worker");
            Scribe_Values.Look(ref Phase, "phase"); Scribe_Values.Look(ref Spot, "spot");
            Scribe_Collections.Look(ref Crew, "crew", LookMode.Reference);
            Scribe_Values.Look(ref Initial, "initial"); Scribe_Values.Look(ref Started, "started"); Scribe_Values.Look(ref PhaseAt, "phaseAt");
            Scribe_Values.Look(ref RetryAt, "retryAt"); Scribe_Values.Look(ref Attempts, "attempts"); Scribe_Values.Look(ref Shots, "shots");
            Scribe_Values.Look(ref Chemical, "chemical"); Scribe_Values.Look(ref HadLoaded, "hadLoaded");
        }
    }
    public sealed class LordToil_HelodMortar : LordToil
    {
        public LordToilData_HelodMortar State => (LordToilData_HelodMortar)data;
        public LordToil_HelodMortar(IntVec3 spot, List<Pawn> crew, bool chemical)
        { data = new LordToilData_HelodMortar { Spot = spot, Crew = crew, Chemical = chemical }; }
        public override bool AllowSatisfyLongNeeds => false;
        public override bool ForceHighStoryDanger => true;
        public override void Init()
        {
            if (State.Initial == 0) { State.Initial = lord.ownedPawns.Count; State.Started = State.PhaseAt = GenTicks.TicksGame; }
            UpdateAllDuties();
        }
        private bool Able(Pawn pawn) => pawn?.Spawned == true && pawn.Map == Map && !pawn.Dead && !pawn.Downed
            && !pawn.InMentalState && pawn.GetLord() == lord;
        public override void UpdateAllDuties()
        { foreach (Pawn pawn in lord.ownedPawns) pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend, State.Spot, State.Crew.Contains(pawn) ? 4 : 14); }
        public override void LordToilTick()
        {
            int tick = GenTicks.TicksGame;
            if (tick % 30 != lord.loadID % 30) return;
            var state = State;
            int able = lord.ownedPawns.Count(Able);
            if (able == 0) { lord.ReceiveMemo("HelodMortarLeave"); return; }
            CompChangeableProjectile magazine = state.Mortar?.Spawned == true ? state.Mortar.gun.TryGetComp<CompChangeableProjectile>() : null;
            if (state.Phase == RaidMortarPhase.Fire && magazine != null)
            {
                if (magazine.Loaded) state.HadLoaded = true;
                else if (state.HadLoaded) { state.Shots++; state.HadLoaded = false; }
            }
            bool contact = lord.ownedPawns.Any(p => Able(p) && p.mindState.lastHarmTick >= state.Started
                && tick - p.mindState.lastHarmTick < 300);
            bool leave = RaidMortarPolicy.Withdraw(state.Chemical, state.Initial, able, state.Shots, tick - state.Started)
                || !lord.faction.HostileTo(Faction.OfPlayer)
                || contact && tick % 300 == lord.loadID % 30 && Rand.Chance(state.Chemical ? .6f : .25f);
            if (state.Phase != RaidMortarPhase.Recover && leave) Recover(tick);
            if (state.Phase == RaidMortarPhase.Assemble)
            {
                if (state.Mortar?.Spawned == true)
                {
                    state.Phase = RaidMortarPhase.Fire; state.PhaseAt = tick; state.Attempts = 0;
                    // Enemy mortars select targets natively. Forced targets are
                    // reserved for player turrets and get cleared on the next tick.
                    return;
                }
                bool assembling = state.Crew.Count > 0 && state.Crew[0].CurJobDef?.defName == "HD_RaidAssembleMortar";
                if (RaidMortarPolicy.AssemblyExpired(tick - state.PhaseAt, assembling ? 0 : state.Attempts)
                    || state.Crew.Count != 3 || state.Crew.Any(p => !Able(p))) { Recover(tick); return; }
                if (tick < state.RetryAt || assembling) return;
                Pawn worker = state.Crew[0];
                IntVec3 spot = IntVec3.Invalid;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(state.Spot, 6, true).Take(32))
                    if (!cell.Roofed(Map) && MortarAssemblyUtility.CanPlaceMortarAt(worker, cell)) { spot = cell; break; }
                if (!spot.IsValid) { state.Attempts++; state.RetryAt = tick + 180; return; }
                state.Spot = spot; UpdateAllDuties();
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_RaidAssembleMortar"), spot, state.Crew[1], state.Crew[2]);
                worker.jobs.StartJob(job, JobCondition.InterruptForced); state.Worker = worker;
                state.Attempts++; state.RetryAt = tick + 180; return;
            }
            if (state.Phase == RaidMortarPhase.Fire)
            {
                if (magazine == null || tick - state.PhaseAt >= 2400 && state.Shots == 0) { Recover(tick); return; }
                if (state.Worker?.CurJobDef?.defName == "HD_RaidManMortar" || tick < state.RetryAt) return;
                Pawn gunner = state.Crew.FirstOrDefault(Able) ?? lord.ownedPawns.FirstOrDefault(Able);
                if (gunner == null || !gunner.CanReserveAndReach(state.Mortar, PathEndMode.InteractionCell, Danger.Deadly))
                { state.RetryAt = tick + 180; return; }
                gunner.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_RaidManMortar"), state.Mortar), JobCondition.InterruptForced);
                state.Worker = gunner; state.RetryAt = tick + 180; return;
            }
            if (state.Phase == RaidMortarPhase.Recover)
            {
                if (state.Mortar?.Spawned != true || RaidMortarPolicy.RecoveryExpired(tick - state.PhaseAt))
                { lord.ReceiveMemo("HelodMortarLeave"); return; }
                if (tick < state.RetryAt || state.Worker?.CurJobDef?.defName == "HD_RaidPackMortar") return;
                Pawn worker = state.Crew.Concat(lord.ownedPawns).Where(Able).Distinct()
                    .OrderBy(p => p.Position.DistanceToSquared(state.Spot)).Take(4)
                    .FirstOrDefault(p => p.CanReserveAndReach(state.Mortar, PathEndMode.Touch, Danger.Deadly));
                if (worker != null)
                {
                    state.Worker = worker;
                    worker.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_RaidPackMortar"), state.Mortar), JobCondition.InterruptForced);
                }
                state.RetryAt = tick + 180;
            }
        }
        private void Recover(int tick)
        {
            State.Phase = RaidMortarPhase.Recover; State.PhaseAt = tick; State.RetryAt = tick;
            foreach (Pawn pawn in lord.ownedPawns)
                if (pawn.CurJobDef?.defName == "HD_RaidAssembleMortar" || pawn.CurJobDef?.defName == "HD_RaidManMortar")
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            State.Mortar?.OrderAttack(LocalTargetInfo.Invalid); UpdateAllDuties();
        }
        public void Assembled(Building_TurretGun mortar) => State.Mortar = mortar;
        public Thing FindShell()
        {
            string name = State.Chemical ? "HD_81mmMortarShell_BA" : "HD_81mmMortarShell_M43HE";
            return lord.ownedPawns.Where(Able).Where(p => p.Position.DistanceToSquared(State.Spot) <= 144)
                .SelectMany(p => p.inventory.innerContainer).FirstOrDefault(t => t.def.defName == name && t.stackCount > 0);
        }
    }
    public sealed class JobDriver_RaidAssembleMortar : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            var owner = pawn.GetLord()?.CurLordToil as LordToil_HelodMortar;
            this.FailOn(() => owner == null || owner.State.Phase != RaidMortarPhase.Assemble || pawn.Downed
                || job.targetB.Pawn?.Spawned != true || job.targetC.Pawn?.Spawned != true
                || job.targetB.Pawn.Downed || job.targetC.Pawn.Downed);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Wait(900, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() =>
            {
                Pawn[] crew = { pawn, job.targetB.Pawn, job.targetC.Pawn };
                ThingDef[] parts = { MortarAssemblyUtility.BaseplateDef, MortarAssemblyUtility.MountDef, MortarAssemblyUtility.BarrelDef };
                if (!MortarAssemblyUtility.CanPlaceMortarAt(pawn, pawn.Position)
                    || crew.Any(p => p.Position.DistanceToSquared(pawn.Position) > 144)
                    || Enumerable.Range(0, 3).Any(i => MortarAssemblyUtility.Worn(crew[i], parts[i]) == null))
                { EndJobWith(JobCondition.Incompletable); return; }
                var mortar = (Building_TurretGun)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Building_M1_81mmMortar"));
                mortar.SetFaction(pawn.Faction); GenSpawn.Spawn(mortar, pawn.Position, pawn.Map, Rot4.North);
                for (int i = 0; i < 3; i++) MortarAssemblyUtility.Consume(crew[i], parts[i]);
                owner.Assembled(mortar);
            });
        }
    }
    public sealed class JobDriver_RaidManMortar : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            var owner = pawn.GetLord()?.CurLordToil as LordToil_HelodMortar;
            var mortar = job.targetA.Thing as Building_TurretGun;
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => owner == null || owner.State.Phase != RaidMortarPhase.Fire || pawn.Downed);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            Toil load = Toils_General.Wait(120, TargetIndex.A);
            load.WithProgressBarToilDelay(TargetIndex.A); load.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
            Toil consume = Toils_General.Do(() =>
            {
                var magazine = mortar.gun.TryGetComp<CompChangeableProjectile>();
                if (magazine.Loaded) return;
                Thing shell = owner.FindShell();
                if (shell == null) { EndJobWith(JobCondition.Incompletable); return; }
                Thing used = shell.SplitOff(1); magazine.LoadShell(used.def, 1); used.Destroy(DestroyMode.Vanish);
            });
            Toil man = ToilMaker.MakeToil("HelodRaidMortarMan");
            man.defaultCompleteMode = ToilCompleteMode.Never; man.handlingFacing = true;
            man.tickAction = () =>
            {
                if (!mortar.gun.TryGetComp<CompChangeableProjectile>().Loaded) { JumpToToil(load); return; }
                mortar.GetComp<CompMannable>().ManForATick(pawn); pawn.rotationTracker.FaceCell(mortar.Position);
            };
            man.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_Jump.JumpIf(man, () => mortar.gun.TryGetComp<CompChangeableProjectile>().Loaded);
            yield return load; yield return consume; yield return man;
        }
    }
    public sealed class JobDriver_RaidPackMortar : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            var owner = pawn.GetLord()?.CurLordToil as LordToil_HelodMortar;
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.Downed || owner == null || pawn.GetLord()?.CurLordToil != owner || owner.State.Phase != RaidMortarPhase.Recover);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(180, TargetIndex.A).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() =>
            {
                Pawn[] carriers = pawn.GetLord().ownedPawns.Where(p => p.Spawned && !p.Downed && !p.Dead
                    && p.Position.DistanceToSquared(pawn.Position) <= 144).OrderBy(p => p == pawn ? 0 : 1).Take(3).ToArray();
                ThingDef[] parts = { MortarAssemblyUtility.BaseplateDef, MortarAssemblyUtility.MountDef, MortarAssemblyUtility.BarrelDef };
                var returned = new List<Thing>();
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Thing part = ThingMaker.MakeThing(parts[i]); returned.Add(part);
                        if (!carriers[i % carriers.Length].inventory.innerContainer.TryAdd(part))
                        { foreach (Thing item in returned) item.Destroy(DestroyMode.Vanish); EndJobWith(JobCondition.Incompletable); return; }
                    }
                    job.targetA.Thing.Destroy(DestroyMode.Vanish);
                }
                catch { foreach (Thing item in returned) item.Destroy(DestroyMode.Vanish); throw; }
            });
        }
    }
    [NewTactical, HarmonyPatch(typeof(RaidStrategyWorker), nameof(RaidStrategyWorker.CanUseWith))]
    public static class Patch_HelodRaid_ImplementedMissions
    {
        public static bool Prefix(RaidStrategyWorker __instance, IncidentParms parms, ref bool __result)
        {
            if (parms.faction?.def.GetModExtension<FactionOrganizationExtension>()?.doctrine == null
                || !parms.faction.HostileTo(Faction.OfPlayer)
                || __instance is RaidStrategyWorker_HelodSweep || __instance is RaidStrategyWorker_HelodMortar) return true;
            __result = false; return false;
        }
    }
    public static class RaidMortarDebug
    {
        [DebugAction("Helodrace/New tactical AI", "Inspect mortar raids", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Inspect()
        {
            string text = string.Join("\n", Find.CurrentMap.lordManager.lords.Where(l => l.CurLordToil is LordToil_HelodMortar)
                .Select(l => { var s = ((LordToil_HelodMortar)l.CurLordToil).State;
                    return $"lord={l.loadID} phase={s.Phase} chemical={s.Chemical} shots={s.Shots}/{RaidMortarPolicy.ShellCount(s.Chemical)} spot={s.Spot} mortar={s.Mortar?.Spawned} attempts={s.Attempts} phaseAt={s.PhaseAt}"; }));
            Find.WindowStack.Add(new Dialog_MessageBox(string.IsNullOrEmpty(text) ? "No active mortar raids." : text));
        }
    }
}
