using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed class JobDriver_TacticalObserve : TacticalJobDriver
    {
        public override IntVec3 PostCell => job.targetC.Cell;
        public bool Peeking;
        public bool RetreatRequested;
        private int nextScan, cursor;
        private readonly List<IntVec3> shootable = new List<IntVec3>(5);
        public IntVec3 Source => job.targetQueueA[0].Cell;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner(); job.canUseRangedWeapon = false;
            yield return AdmitMovement();
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil observe = Toils_General.Wait(TacticalOpeningPolicy.ObservationTicks);
            observe.handlingFacing = true;
            observe.initAction = () => { Peeking = true; pawn.rotationTracker.FaceCell(job.targetB.Cell); };
            AddFinishAction(_ => Peeking = false);
            observe.tickAction = () =>
            {
                if (RetreatRequested) { Peeking = false; ReadyForNextToil(); return; }
                int tick = GenTicks.TicksGame;
                if (tick < nextScan) return;
                TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (!budget.TryObserve(tick)) return;
                nextScan = tick + 15;
                var enemies = pawn.Map.mapPawns.AllPawnsSpawned;
                IntVec3 opening = job.targetQueueA[1].Cell, travel = job.targetB.Cell - opening;
                IntVec3 inward = new IntVec3(System.Math.Sign(travel.x), 0, System.Math.Sign(travel.z));
                // Rotation through at most sixteen pawn candidates per scan.
                // Reuse shootable cells instead of allocating per enemy.
                for (int i = 0; i < System.Math.Min(16, enemies.Count); i++)
                {
                    if (cursor >= enemies.Count) cursor = 0;
                    Pawn enemy = enemies[cursor++]; IntVec3 cell = enemy.Position, delta = cell - opening;
                    int forward = delta.x * inward.x + delta.z * inward.z;
                    bool door = cell.GetEdifice(pawn.Map) is RimWorld.Building_Door;
                    bool prisoner = !enemy.Dead && enemy.IsPrisoner && HelodRace.IsHelod(enemy);
                    if (!prisoner && (enemy.Dead || enemy.Downed || !enemy.HostileTo(pawn)) || forward < (door ? 0 : 1)
                        || Source.DistanceToSquared(cell) > 196) continue;
                    shootable.Clear(); ShootLeanUtility.CalcShootableCellsOf(shootable, enemy, Source);
                    bool visible = false;
                    foreach (IntVec3 destination in shootable)
                        if (GenSight.LineOfSight(Source, destination, pawn.Map, true)) { visible = true; break; }
                    if (!visible) continue;
                    if (prisoner)
                    { pawn.Map.GetComponent<MapComponent_TacticalCommands>()?.ObservedPrisoner(pawn, job); continue; }
                    bool ranged = TacticalThreatEligibility.Ranged(enemy);
                    MapComponent_TacticalCommands owner = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
                    bool melee = owner?.ObservedOpeningMelee(pawn, job, enemy, tick, ranged) == true;
                    if (!ranged && !melee) continue;
                    Peeking = false;
                    owner.Observed(pawn, job, cell, enemy.thingIDNumber, ranged);
                    ReadyForNextToil(); return;
                }
            };
            yield return observe;
            yield return Toils_General.Do(() =>
            {
                Peeking = false;
                // Do not overwrite the one enemy already recorded.
                pawn.Map.GetComponent<MapComponent_TacticalCommands>().ObservationFinished(pawn, job);
            });
            yield return AdmitMovement();
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            yield return Hold(job.targetB.Cell);
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref Peeking, "tacticalPeeking");
            Scribe_Values.Look(ref RetreatRequested, "tacticalRetreatRequested");
            Scribe_Values.Look(ref nextScan, "tacticalNextScan"); Scribe_Values.Look(ref cursor, "tacticalScanCursor");
        }
    }

    public sealed class JobDriver_TacticalThrow : TacticalJobDriver
    {
        public override IntVec3 PostCell => job.targetQueueA[0].Cell;
        public bool Prepared, Released;
        private int returnCheckAt,returnDeadline=-1,failedReturnUntil;
        private IntVec3 returnGoal=IntVec3.Invalid,failedReturn=IntVec3.Invalid;
        public Thing Grenade => job.targetB.Thing;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner(); job.canUseRangedWeapon = false;
            this.FailOn(() => !Released && (Grenade == null || Grenade.Destroyed || pawn.inventory?.Contains(Grenade) != true));
            Toil prepare = Toils_General.Wait(TacticalOpeningPolicy.PrepareTicks);
            prepare.initAction = () =>
            {
                pawn.pather.StopDead(); pawn.stances.CancelBusyStanceHard();
                if (pawn.equipment != null) foreach (Verb verb in pawn.equipment.AllEquipmentVerbs) verb.Reset();
            };
            yield return prepare;
            yield return Toils_General.Do(() => Prepared = true);
            yield return AdmitMovement();
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            Toil returnReady=ToilMaker.MakeToil("TacticalCoveredReturnReady");
            returnReady.defaultCompleteMode=ToilCompleteMode.Never;
            returnReady.initAction=()=> { if(returnDeadline<0) returnDeadline=GenTicks.TicksGame+180; };
            returnReady.tickAction=()=>
            {
                int tick=GenTicks.TicksGame;
                if(tick<returnCheckAt) return;
                returnCheckAt=tick+10;
                if(pawn.Map.GetComponent<MapComponent_TacticalCommands>().EnsureSupportReturn(pawn,job,IntVec3.Invalid)) ReadyForNextToil();
                else if(tick>=returnDeadline) EndJobWith(JobCondition.Incompletable);
            };
            yield return returnReady;
            Toil release = Toils_General.Wait(TacticalOpeningPolicy.ReleaseTicks, TargetIndex.A);
            release.handlingFacing = true;
            release.initAction = () => pawn.rotationTracker.FaceCell(job.targetA.Cell);
            yield return release;
            yield return Toils_General.Do(() =>
            {
                if(!pawn.Map.GetComponent<MapComponent_TacticalCommands>().EnsureSupportReturn(pawn,job,IntVec3.Invalid))
                { JumpToToil(returnReady); return; }
                if (!MapComponent_TacticalCommands.SafeOpeningThrow(pawn, Grenade, pawn.Position, job.targetA.Cell))
                { EndJobWith(JobCondition.Incompletable); return; }
                Projectile projectile = InventoryGrenadeUtility.Launch(pawn, Grenade, job.targetA);
                if (projectile == null) { EndJobWith(JobCondition.Incompletable); return; }
                Released = true; Prepared = false;
                pawn.Map.GetComponent<MapComponent_TacticalCommands>().Launched(pawn, job, projectile);
            });
            // A blocked slot may change only to a validated connected safe return.
            // Actual arrival still owns Returned; no timeout can fabricate it.
            Toil returning=ToilMaker.MakeToil("TacticalCoveredReturn");
            returning.defaultCompleteMode=ToilCompleteMode.Never;
            returning.tickAction=()=>
            {
                int tick=GenTicks.TicksGame; if(tick<returnCheckAt) return;
                returnCheckAt=tick+10;
                if(tick>=failedReturnUntil) failedReturn=IntVec3.Invalid;
                var owner=pawn.Map.GetComponent<MapComponent_TacticalCommands>();
                if(!owner.EnsureSupportReturn(pawn,job,failedReturn)) { pawn.pather.StopDead(); return; }
                IntVec3 goal=job.targetQueueA[0].Cell;
                if(pawn.Position==goal)
                { pawn.pather.StopDead(); owner.SupportReturned(pawn,job); ReadyForNextToil(); return; }
                if(pawn.pather.Moving && returnGoal==goal) return;
                if(!Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.TryPath(tick)) return;
                returnGoal=goal; pawn.pather.StartPath(goal,PathEndMode.OnCell);
            };
            yield return returning;
            yield return Hold(job.targetQueueA[1].Cell);
        }
        public override void Notify_PatherFailed()
        {
            if(Released && pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.CqbThrowOwned(pawn,job)==true)
            { failedReturn=returnGoal; failedReturnUntil=GenTicks.TicksGame+90; returnGoal=IntVec3.Invalid;
                pawn.pather.StopDead(); returnCheckAt=GenTicks.TicksGame+1; return; }
            base.Notify_PatherFailed();
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref Prepared, "tacticalGrenadePrepared");
            Scribe_Values.Look(ref Released, "tacticalGrenadeReleased");
            Scribe_Values.Look(ref returnCheckAt,"returnCheckAt");
            Scribe_Values.Look(ref returnDeadline,"returnDeadline",-1);
            Scribe_Values.Look(ref returnGoal,"returnGoal",IntVec3.Invalid);
            Scribe_Values.Look(ref failedReturn,"failedReturn",IntVec3.Invalid);
            Scribe_Values.Look(ref failedReturnUntil,"failedReturnUntil");
        }
    }

    public static class TacticalOpeningPresentation
    {
        internal static bool BlocksGun(Pawn pawn) => pawn?.jobs?.curDriver is JobDriver_TacticalThrow driver && !driver.Released
            || pawn?.jobs?.curDriver is JobDriver_ThrowInventoryGrenade inventory && !inventory.Released
            || pawn?.jobs?.curDriver is JobDriver_TacticalObserve;
    }
    [NewTactical, HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] {
        typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_NewTactical_NoGunCast
    {
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            if (__instance.EquipmentSource == null || __instance.IsMeleeAttack || !TacticalOpeningPresentation.BlocksGun(__instance.CasterPawn)) return true;
            __result = false; return false;
        }
    }
    [NewTactical, HarmonyPatch(typeof(Verb), nameof(Verb.Available))]
    public static class Patch_NewTactical_NoGunAvailable
    {
        public static void Postfix(Verb __instance, ref bool __result)
        {
            if (__instance.EquipmentSource != null && !__instance.IsMeleeAttack && TacticalOpeningPresentation.BlocksGun(__instance.CasterPawn)) __result = false;
        }
    }
    [NewTactical, HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.CarryWeaponOpenly))]
    public static class Patch_NewTactical_HideGun
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        { if (TacticalOpeningPresentation.BlocksGun(pawn)) __result = false; }
    }
    [NewTactical, HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    public static class Patch_NewTactical_DrawGrenade
    {
        public static void Postfix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            var driver = pawn.jobs?.curDriver as JobDriver_TacticalThrow;
            Thing grenade = driver?.Prepared == true && !driver.Released ? driver.Grenade : null;
            if (pawn.jobs?.curDriver is JobDriver_ThrowInventoryGrenade inventory && !inventory.Released) grenade = inventory.Grenade;
            if (grenade?.Graphic == null || flags.HasFlag(PawnRenderFlags.Portrait) || pawn.Dead || pawn.Downed) return;
            Vector3 hand = drawPos + new Vector3(.23f, .045f, .25f).RotatedBy(facing.AsAngle);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(hand, facing.AsQuat, new Vector3(.42f, 1f, .42f)),
                grenade.Graphic.MatAt(facing, grenade), 0);
        }
    }
    [NewTactical, HarmonyPatch(typeof(PawnLeaner), nameof(PawnLeaner.ShouldLean))]
    public static class Patch_NewTactical_ObserveLean
    {
        public static void Postfix(Pawn ___pawn, ref IntVec3 ___shootSourceOffset, ref bool __result)
        {
            if (!(___pawn.jobs?.curDriver is JobDriver_TacticalObserve observer) || !observer.Peeking) return;
            ___shootSourceOffset = observer.Source - ___pawn.Position; __result = true;
        }
    }
}
