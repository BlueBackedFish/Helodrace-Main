using System;
using System.Collections.Generic;
using System.Linq;

// Exercise the real radio menus, joy selection and job callbacks without Unity.
namespace Verse
{
    public enum Danger { Deadly }
    public enum TimeSpeed { Paused }
    public struct IntVec3 { }
    public class Thing
    {
        public RimWorld.Faction Faction = RimWorld.Faction.OfPlayer;
        public Map Map = new Map();
        public bool Spawned = true;
        public IntVec3 InteractionCell;
        public RimWorld.CompPowerTrader Power = new RimWorld.CompPowerTrader();
    }
    public class Building : Thing
    {
        public virtual IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn) { yield break; }
    }
    public class Pawn : Thing
    {
        public readonly Health health = new Health();
        public readonly Jobs jobs = new Jobs();
        public readonly RotationTracker rotationTracker = new RotationTracker();
        public bool Reachable = true, Reservable = true, SpotAvailable = true;
        public int ComfortTicks;
    }
    public sealed class Health { public readonly Capacities capacities = new Capacities(); }
    public sealed class Capacities
    {
        public bool Hearing = true, Talking = true;
        public bool CapableOf(object capacity) => capacity == RimWorld.PawnCapacityDefOf.Hearing ? Hearing : Talking;
    }
    public sealed class Jobs
    {
        public AI.Job Last;
        public bool TryTakeOrderedJob(AI.Job job) { Last = job; job.playerForced = true; return true; }
    }
    public sealed class RotationTracker { public void FaceTarget(Thing target) { } }
    public sealed class Map
    {
        public readonly Conditions gameConditionManager = new Conditions();
        public readonly Helodrace.MapComponent_HamRadio Ham = new Helodrace.MapComponent_HamRadio();
        public T GetComponent<T>() => (T)(object)Ham;
    }
    public sealed class Conditions { public bool Disabled; public bool ElectricityDisabled(Map map) => Disabled; }
    public sealed class FloatMenuOption
    {
        public string Label;
        public Action action;
        public object Icon, Color;
        public FloatMenuOption(string label, Action action) { Label = label; this.action = action; }
        public FloatMenuOption(string label, Action action, object icon, object color) : this(label, action) { Icon = icon; Color = color; }
    }
    public class JobDef { public string defName; public int joyDuration = 4000; }
    public static class DefDatabase<T> where T : JobDef, new()
    {
        public static T GetNamed(string name) => new T { defName = name };
    }
    public static class Find { public static readonly RimWorld.FactionManager FactionManager = new RimWorld.FactionManager(); }
    public static class JobMaker
    {
        public static AI.Job MakeJob(JobDef def, Thing thing, IntVec3 cell)
            => new AI.Job { def = def, targetA = new LocalTargetInfo { Thing = thing }, targetB = new LocalTargetInfo { Cell = cell } };
    }
    public struct LocalTargetInfo { public Thing Thing; public IntVec3 Cell; public static implicit operator LocalTargetInfo(Thing thing) => new LocalTargetInfo { Thing = thing }; }
    public static class GameExtensions
    {
        public static T TryGetComp<T>(this Thing thing) => (T)(object)thing.Power;
        public static bool CanReach(this Pawn pawn, Thing thing, AI.PathEndMode mode, Danger danger) => pawn.Reachable;
        public static bool CanReserve(this Pawn pawn, LocalTargetInfo thing) => pawn.Reservable;
        public static bool CanReserveSittableOrSpot(this Pawn pawn, IntVec3 cell) => pawn.SpotAvailable;
        public static bool Reserve(this Pawn pawn, LocalTargetInfo thing, AI.Job job, int count, int stack, object layer, bool error) => pawn.Reservable;
        public static bool ReserveSittableOrSpot(this Pawn pawn, IntVec3 cell, AI.Job job, bool error) => pawn.SpotAvailable;
        public static string Translate(this string key, params object[] args) => key + (args.Length == 0 ? "" : ": " + string.Join(", ", args));
        public static string ToStringWithSign(this int value) => value.ToString("+0;-0;0");
        public static bool TryRandomElement<T>(this IEnumerable<T> values, out T value) { value = values.FirstOrDefault(); return values.Any(); }
    }
    public static class ToilMaker { public static AI.Toil MakeToil(string name) => new AI.Toil(); }
    public static class Scribe_Values
    {
        public static bool Loading;
        public static bool Saved;
        public static void Look(ref bool value, string key, bool defaultValue) { if (Loading) value = Saved; else Saved = value; }
    }
}
namespace Verse.AI
{
    public enum TargetIndex { A, B }
    public enum PathEndMode { OnCell, InteractionCell }
    public enum ToilCompleteMode { Delay }
    public class Job
    {
        public JobDef def;
        public LocalTargetInfo targetA, targetB;
        public object commTarget;
        public bool playerForced;
    }
    public class Toil
    {
        public Action initAction;
        public Action<int> tickIntervalAction;
        public Action Finish;
        public ToilCompleteMode defaultCompleteMode;
        public int defaultDuration;
        public bool handlingFacing;
        public void AddFinishAction(Action action) => Finish += action;
    }
    public abstract class JobDriver
    {
        public Pawn pawn;
        public Job job;
        protected Thing TargetThingA => job.targetA.Thing;
        public readonly List<Func<bool>> Failures = new List<Func<bool>>();
        public abstract bool TryMakePreToilReservations(bool errorOnFailed);
        protected abstract IEnumerable<Toil> MakeNewToils();
        public List<Toil> Toils() => MakeNewToils().ToList();
        public virtual bool? IsSameJobAs(Job other) => null;
        public virtual void ExposeData() { }
    }
    public static class Toils_Goto { public static Toil GotoCell(TargetIndex index, PathEndMode mode) => new Toil(); }
    public static class FailExtensions
    {
        public static void FailOn(this JobDriver driver, Func<bool> fail) => driver.Failures.Add(fail);
        public static void FailOnDespawnedNullOrForbidden(this JobDriver driver, TargetIndex index) => driver.Failures.Add(() => driver.job.targetA.Thing?.Spawned != true);
        public static void FailOnBurningImmobile(this JobDriver driver, TargetIndex index) { }
    }
}
namespace RimWorld
{
    public sealed class CompPowerTrader { public bool PowerOn = true; }
    public sealed class FactionDef { public string defName; public object FactionIcon = new object(); }
    public sealed class Faction
    {
        public static readonly Faction OfPlayer = new Faction { IsPlayer = true };
        public bool IsPlayer, defeated;
        public string Name;
        public int PlayerGoodwill, Calls;
        public object Color = new object();
        public FactionDef def = new FactionDef();
        public FactionRelationKind PlayerRelationKind;
        public void TryOpenComms(Verse.Pawn pawn) => Calls++;
    }
    public enum FactionRelationKind { Hostile, Neutral, Ally }
    public static class FactionRelationExtensions { public static string GetLabelCap(this FactionRelationKind kind) => kind.ToString(); }
    public sealed class FactionManager { public readonly List<Faction> AllFactionsVisibleInViewOrder = new List<Faction>(); }
    public static class PawnCapacityDefOf { public static readonly object Hearing = new object(), Talking = new object(); }
    public sealed class JoyGiverDef { public Verse.JobDef jobDef = new Verse.JobDef(); }
    public class JoyGiver_InteractBuildingInteractionCell
    {
        public readonly JoyGiverDef def = new JoyGiverDef();
        protected virtual bool CanInteractWith(Verse.Pawn pawn, Verse.Thing thing, bool inBed) => pawn.Reservable;
        protected virtual Verse.AI.Job TryGivePlayJob(Verse.Pawn pawn, Verse.Thing thing) => Verse.JobMaker.MakeJob(def.jobDef, thing, thing.InteractionCell);
        public Verse.AI.Job Play(Verse.Pawn pawn, Verse.Thing thing) => CanInteractWith(pawn, thing, false) ? TryGivePlayJob(pawn, thing) : null;
    }
    public enum JoyTickFullJoyAction { EndJob }
    public static class JoyUtility
    {
        public static int Ticks, RoomThoughts;
        public static bool JoyTickCheckEnd(Verse.Pawn pawn, int delta, JoyTickFullJoyAction action, float factor, Verse.Building building) { Ticks += delta; return false; }
        public static void TryGainRecRoomThought(Verse.Pawn pawn) => RoomThoughts++;
    }
    public static class PawnUtility { public static void GainComfortFromCellIfPossible(this Verse.Pawn pawn, int delta) => pawn.ComfortTicks += delta; }
}
namespace Helodrace
{
    public static class HamRadioUtility { public static bool IsHamFaction(RimWorld.Faction faction) => HamRadioRoutingRules.IsHamFaction(faction.def?.defName); }
    public sealed class MapComponent_HamRadio
    {
        public int Calls;
        public string Chat(Verse.Pawn pawn, RimWorld.Faction faction) { Calls++; return "Chatter"; }
    }
}
