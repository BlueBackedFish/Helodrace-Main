using System;
using System.Collections.Generic;
using System.Linq;

// Exercise the real radio menus, joy selection and job callbacks without Unity.
namespace Verse
{
    public enum Danger { None, Deadly }
    public enum TimeSpeed { Paused }
    public struct IntVec3
    {
        public int x, z;
        public IntVec3(int x, int y, int z) { this.x = x; this.z = z; }
        public int LengthHorizontalSquared => x * x + z * z;
        public static IntVec3 operator -(IntVec3 a, IntVec3 b) => new IntVec3(a.x - b.x, 0, a.z - b.z);
    }
    public sealed class ThingDef
    {
        public float specialDisplayRadius = 4f;
        public readonly RimWorld.BuildingProperties building = new RimWorld.BuildingProperties();
    }
    public class Thing
    {
        public RimWorld.Faction Faction = RimWorld.Faction.OfPlayer;
        public Map Map = new Map();
        public bool Spawned = true;
        public IntVec3 InteractionCell;
        public IntVec3 Position;
        public ThingDef def = new ThingDef();
        public RimWorld.CompPowerTrader Power = new RimWorld.CompPowerTrader();
        public RimWorld.CompRefuelable Fuel;
        public string LabelShort = "radio";
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
        public readonly object Room = new object();
        public readonly Dictionary<IntVec3, object> Rooms = new Dictionary<IntVec3, object>();
        public readonly Dictionary<IntVec3, Building> Seats = new Dictionary<IntVec3, Building>();
        public readonly HashSet<IntVec3> Blocked = new HashSet<IntVec3>(), Forbidden = new HashSet<IntVec3>(), Reserved = new HashSet<IntVec3>();
        public readonly DestinationReservations pawnDestinationReservationManager = new DestinationReservations();
        public readonly Conditions gameConditionManager = new Conditions();
        public readonly Helodrace.MapComponent_HamRadio Ham = new Helodrace.MapComponent_HamRadio();
        public readonly Helodrace.MapComponent_TelegraphNetwork Network = new Helodrace.MapComponent_TelegraphNetwork();
        public T GetComponent<T>() => (T)(object)(typeof(T) == typeof(Helodrace.MapComponent_TelegraphNetwork) ? (object)Network : Ham);
    }
    public sealed class DestinationReservations { public bool CanReserve(IntVec3 cell, Pawn pawn) => !pawn.Map.Reserved.Contains(cell); }
    public static class GenRadial
    {
        public static IEnumerable<IntVec3> RadialCellsAround(IntVec3 center, float radius, bool useCenter)
        {
            int limit = (int)Math.Ceiling(radius);
            for (int x = -limit; x <= limit; x++)
                for (int z = -limit; z <= limit; z++)
                    if (x * x + z * z <= radius * radius && (useCenter || x != 0 || z != 0))
                        yield return new IntVec3(center.x + x, 0, center.z + z);
        }
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
        public static T GetNamedSilentFail(string name) => GetNamed(name);
    }
    public static class Find { public static readonly RimWorld.FactionManager FactionManager = new RimWorld.FactionManager(); }
    public static class JobMaker
    {
        public static AI.Job MakeJob(JobDef def, Thing thing, IntVec3 cell, Thing seat)
        {
            var job = MakeJob(def, thing, cell); job.targetC = seat; return job;
        }
        public static AI.Job MakeJob(JobDef def, Thing thing) => MakeJob(def, thing, thing.InteractionCell);
        public static AI.Job MakeJob(JobDef def, Thing thing, IntVec3 cell)
            => new AI.Job { def = def, targetA = new LocalTargetInfo { Thing = thing }, targetB = new LocalTargetInfo { Cell = cell } };
    }
    public struct LocalTargetInfo { public Thing Thing; public IntVec3 Cell; public static implicit operator LocalTargetInfo(Thing thing) => new LocalTargetInfo { Thing = thing }; }
    public static class GameExtensions
    {
        public static T TryGetComp<T>(this Thing thing) => (T)(typeof(T) == typeof(RimWorld.CompRefuelable) ? (object)thing.Fuel : thing.Power);
        public static bool CanReach(this Pawn pawn, Thing thing, AI.PathEndMode mode, Danger danger) => pawn.Reachable;
        public static bool CanReach(this Pawn pawn, IntVec3 cell, AI.PathEndMode mode, Danger danger) => pawn.Reachable;
        public static bool CanReserve(this Pawn pawn, LocalTargetInfo thing) => pawn.Reservable;
        public static bool CanReserveSittableOrSpot(this Pawn pawn, IntVec3 cell) => pawn.SpotAvailable;
        public static bool InBounds(this IntVec3 cell, Map map) => Math.Abs(cell.x) < 50 && Math.Abs(cell.z) < 50;
        public static bool Standable(this IntVec3 cell, Map map) => !map.Blocked.Contains(cell);
        public static object GetRoom(this IntVec3 cell, Map map) => map.Rooms.TryGetValue(cell, out var room) ? room : map.Room;
        public static object GetRoom(this Thing thing) => thing.Position.GetRoom(thing.Map);
        public static Building GetEdifice(this IntVec3 cell, Map map) => map.Seats.TryGetValue(cell, out var seat) ? seat : null;
        public static bool IsForbidden(this IntVec3 cell, Pawn pawn) => pawn.Map.Forbidden.Contains(cell);
        public static bool Reserve(this Pawn pawn, LocalTargetInfo thing, AI.Job job, int count, int stack, object layer, bool error) => pawn.Reservable;
        public static bool ReserveSittableOrSpot(this Pawn pawn, IntVec3 cell, AI.Job job, bool error) => pawn.SpotAvailable;
        public static string Translate(this string key, params object[] args) => key + (args.Length == 0 ? "" : ": " + string.Join(", ", args));
        public static string ToStringWithSign(this int value) => value.ToString("+0;-0;0");
        public static bool TryRandomElement<T>(this IEnumerable<T> values, out T value) { value = values.FirstOrDefault(); return values.Any(); }
    }
    public static class ToilMaker { public static AI.Toil MakeToil(string name) => new AI.Toil(); }
    public class CompProperties { public Type compClass; }
    public class ThingComp
    {
        public Thing parent;
        public CompProperties props;
        public virtual string CompInspectStringExtra() => null;
        public virtual IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn) { yield break; }
    }
    public static class Log { public static void ErrorOnce(string text, int id) => throw new Exception(text); }
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
        public LocalTargetInfo targetA, targetB, targetC;
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
    public sealed class BuildingProperties { public bool isSittable; }
    public sealed class CompPowerTrader { public bool PowerOn = true; }
    public sealed class CompRefuelable { public float Fuel; public void ConsumeFuel(float amount) => Fuel -= amount; }
    public sealed class FactionDef { public string defName; public object FactionIcon = new object(); }
    public sealed class Faction
    {
        public static readonly Faction OfPlayer = new Faction { IsPlayer = true };
        public bool IsPlayer, defeated;
        public bool Hostile;
        public bool HostileTo(Faction other) => Hostile;
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
    public class JoyGiver_InteractBuilding
    {
        public readonly JoyGiverDef def = new JoyGiverDef();
        protected virtual bool CanInteractWith(Verse.Pawn pawn, Verse.Thing thing, bool inBed) => pawn.Reservable;
        protected virtual Verse.AI.Job TryGivePlayJob(Verse.Pawn pawn, Verse.Thing thing) => pawn.SpotAvailable && pawn.Reachable
            ? Verse.JobMaker.MakeJob(def.jobDef, thing, thing.InteractionCell) : null;
        public Verse.AI.Job Play(Verse.Pawn pawn, Verse.Thing thing) => CanInteractWith(pawn, thing, false) ? TryGivePlayJob(pawn, thing) : null;
        public bool CanPlayInBed(Verse.Pawn pawn, Verse.Thing thing) => CanInteractWith(pawn, thing, true);
    }
    public class JoyGiver_InteractBuildingInteractionCell : JoyGiver_InteractBuilding { }
    public class JobDriver_WatchTelevision : Verse.AI.JobDriver
    {
        public static readonly Verse.AI.Toil NativeToil = new Verse.AI.Toil();
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Verse.AI.Toil> MakeNewToils() { yield return NativeToil; }
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
    public sealed class MapComponent_TelegraphNetwork
    {
        public int Ready = -1, Hour;
        public RimWorld.Faction Provider;
        public bool InstallationRequested => Ready >= 0;
        public TelegraphConnectionStatus Status(bool hasPrimaryCell = true)
            => TelegraphNetworkRules.Status(Ready, 100, Hour, Provider != null && !Provider.defeated, hasPrimaryCell);
        public string StatusText(TelegraphConnectionStatus status) => status.ToString();
    }
    public static class HamRadioUtility { public static bool IsHamFaction(RimWorld.Faction faction) => HamRadioRoutingRules.IsHamFaction(faction.def?.defName); }
    public sealed class MapComponent_HamRadio
    {
        public int Calls;
        public string Chat(Verse.Pawn pawn, RimWorld.Faction faction) { Calls++; return "Chatter"; }
    }
}
namespace UnityEngine
{
    public static class Mathf { public static int FloorToInt(float value) => (int)Math.Floor(value); }
}
