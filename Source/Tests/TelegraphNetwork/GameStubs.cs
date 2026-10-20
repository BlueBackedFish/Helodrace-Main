using System;
using System.Collections.Generic;
using System.Linq;

// Minimal game surfaces for testing the production map component's transactions
// and persistence. UI/job discovery and vanilla diplomacy still need game checks.
namespace Verse
{
    public class GameComponent
    {
        public virtual void ExposeData() { }
        public virtual void GameComponentTick() { }
        public virtual void LoadedGame() { }
    }
    public sealed class Game
    {
        public GameComponent Component;
        public readonly Dictionary<Type, GameComponent> Components = new Dictionary<Type, GameComponent>();
        public T GetComponent<T>() where T : GameComponent => Components.TryGetValue(typeof(T), out var component) ? (T)component : Component as T;
    }
    public static class Current { public static Game Game; }
    public class MapComponent
    {
        protected readonly Map map;
        public MapComponent(Map map) { this.map = map; }
        public virtual void ExposeData() { }
        public virtual void MapComponentTick() { }
    }

    public sealed class Map
    {
        public bool IsPlayerHome = true;
        public int Tile;
        public readonly Position Center = new Position();
        public readonly MapParent Parent = new MapParent();
        public readonly ListerThings listerThings = new ListerThings();
        public readonly MapPawns mapPawns = new MapPawns();
    }
    public sealed class MapParent { public string LabelCap = "Test settlement"; }
    public sealed class ListerThings
    {
        public readonly List<Thing> AllThings = new List<Thing>();
        public List<Thing> ThingsOfDef(ThingDef def) => AllThings.Where(t => !t.Destroyed && t.def == def).ToList();
    }
    public class Thing
    {
        public Map Map;
        public RimWorld.Faction Faction;
        public bool Spawned = true, Destroyed, Forbidden, Accessible = true;
        public int stackCount;
        public ThingDef def;
        public Position Position = new Position();
        public object Comp;
        public T TryGetComp<T>() where T : class => Comp as T;
        public bool IsForbidden(Pawn pawn) => Forbidden;
        public Thing SplitOff(int count)
        {
            if (count <= 0 || count > stackCount) throw new Exception("Invalid payment split");
            stackCount -= count;
            if (stackCount == 0) Map?.listerThings.AllThings.Remove(this);
            return new Thing { stackCount = count, def = def };
        }
        public void Destroy() { Destroyed = true; }
    }
    public sealed class Position
    {
        public bool IsFogged;
        public bool Fogged(Map map) => IsFogged;
        public float DistanceTo(IntVec3 other) => 0f;
    }
    public class Pawn : Thing
    {
        public bool Dead, Downed, InMentalState, Protected;
        public RimWorld.Pawn_TraderTracker trader;
        public AI.Group.Lord Lord;
        public readonly Health health = new Health();
        public readonly Skills skills = new Skills();
        public bool HostileTo(RimWorld.Faction faction) => Faction?.HostileTo(faction) ?? false;
        public bool CanReach(Thing thing, AI.PathEndMode mode, Danger danger)
            => thing != null && thing.Spawned && thing.Map == Map && thing.Accessible;
    }
    public sealed class Health { public readonly Capacities capacities = new Capacities(); }
    public sealed class Capacities { public bool CanTalk = true; public bool CapableOf(object capacity) => CanTalk; }
    public sealed class Skills { public readonly Skill Social = new Skill(); public Skill GetSkill(object def) => Social; }
    public sealed class Skill { public bool TotallyDisabled; }
    public sealed class ThingDef { public string defName; public int stackLimit = 100; }
    public enum Danger { Deadly }
    public static class DefDatabase<T> where T : class
    {
        public static T Value;
        public static T GetNamed(string name) => Value ?? throw new Exception("Missing test definition: " + name);
    }
    public static class Rand
    {
        public static bool UseMaximum;
        public static int RangeInclusive(int min, int max) => UseMaximum ? max : min;
    }
    public static class TextExtensions
    {
        public static object Named<T>(this T value, string key) => value;
        public static string Translate(this string key, params object[] args) => key;
        public static string ToStringTicksToPeriod(this int ticks) => ticks.ToString();
    }
    public static class Find
    {
        public static readonly TickManager TickManager = new TickManager();
        public static readonly WorldGrid WorldGrid = new WorldGrid();
        public static readonly LetterStack LetterStack = new LetterStack();
        public static readonly RimWorld.GoodwillSituationManager GoodwillSituationManager = new RimWorld.GoodwillSituationManager();
        public static readonly RimWorld.HistoryEventsManager HistoryEventsManager = new RimWorld.HistoryEventsManager();
        public static readonly List<Map> Maps = new List<Map>();
    }
    public sealed class TickManager { public int TicksGame; public long TicksAbs; }
    public sealed class WorldGrid { public Longitude LongLatOf(int tile) => new Longitude(); }
    public struct Longitude { public float x; }
    public sealed class LetterStack
    {
        public int Count;
        public void ReceiveLetter(string title, string body, object def, RimWorld.Planet.GlobalTargetInfo target) { Count++; }
        public void ReceiveLetter(string title, string body, object def) { Count++; }
    }
    public static class Messages
    {
        public static int Count;
        public static void Message(string text, Pawn pawn, object def) { Count++; }
        public static void Message(string text, object def) { Count++; }
        public static void Message(string text, RimWorld.Planet.GlobalTargetInfo target, object def) { Count++; }
    }
    public static class Scribe
    {
        public static bool Loading;
        public static LoadSaveMode mode => Loading ? LoadSaveMode.LoadingVars : LoadSaveMode.Saving;
        public static Dictionary<string, object> Values = new Dictionary<string, object>();
        public static void Look<T>(ref T value, string key, T defaultValue)
        {
            if (Loading) value = Values.TryGetValue(key, out var stored) ? (T)stored : defaultValue;
            else Values[key] = value;
        }
    }
    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string key, T defaultValue = default) => Scribe.Look(ref value, key, defaultValue);
    }
    public static class Scribe_References
    {
        public static void Look<T>(ref T value, string key) where T : class => Scribe.Look(ref value, key, default(T));
    }
    public enum LookMode { Reference, Value, Deep }
    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> values, string key, LookMode mode) where T : IExposable, new()
        {
            var outer = Scribe.Values;
            if (Scribe.Loading)
            {
                values = new List<T>();
                if (!outer.TryGetValue(key, out var saved)) return;
                foreach (var item in (List<Dictionary<string, object>>)saved)
                {
                    Scribe.Values = item;
                    var value = new T(); value.ExposeData(); values.Add(value);
                }
            }
            else
            {
                var saved = new List<Dictionary<string, object>>();
                foreach (T value in values)
                {
                    Scribe.Values = new Dictionary<string, object>();
                    value.ExposeData(); saved.Add(Scribe.Values);
                }
                outer[key] = saved;
            }
            Scribe.Values = outer;
        }
        public static void Look<K, V>(ref Dictionary<K, V> values, string key, LookMode keyMode, LookMode valueMode,
            ref List<K> keys, ref List<V> entries)
        {
            if (Scribe.Loading)
                values = Scribe.Values.TryGetValue(key, out var saved) && saved != null ? new Dictionary<K, V>((Dictionary<K, V>)saved) : null;
            else Scribe.Values[key] = values == null ? null : new Dictionary<K, V>(values);
        }
    }
}
namespace Verse.AI { public enum PathEndMode { Touch } }
namespace Verse.AI.Group
{
    public sealed class Lord
    {
        public readonly List<Verse.Pawn> ownedPawns = new List<Verse.Pawn>();
        public LordJob LordJob;
        public LordToil CurLordToil;
    }
    public static class LordUtility { public static Lord GetLord(this Verse.Pawn pawn) => pawn.Lord; }
}
namespace RimWorld
{
    public sealed class FactionDef { public string defName; public bool permanentEnemy; }
    public sealed class Faction
    {
        public static readonly Faction OfPlayer = new Faction();
        public FactionDef def = new FactionDef();
        public bool defeated, Hostile, temporary, HasGoodwill = true;
        public bool CanChange = true;
        public string Name = "Test faction";
        public bool IsPlayer => ReferenceEquals(this, OfPlayer);
        public int PlayerGoodwill, NativeNaturalGoodwill, MaxGoodwill = 100, GoodwillChanges;
        public int NaturalGoodwill => NativeNaturalGoodwill;
        public int BaseGoodwillWith(Faction other) => PlayerGoodwill;
        public int CalculateAdjustedGoodwillChange(Faction other, int amount) => amount;
        public bool TryAffectGoodwillWith(Faction other, int amount, HistoryEventDef reason = null)
        {
            PlayerGoodwill += amount; GoodwillChanges++; return true;
        }
        public bool HostileTo(Faction other) => Hostile;
        public bool CanChangeGoodwillFor(Faction other, int change) => CanChange && other.CanChange;
    }
    public class GoodwillSituationWorker { public virtual int GetNaturalGoodwillOffset(Faction faction) => 0; }
    public sealed class GoodwillSituationManager
    {
        public int Recalculations;
        public void RecalculateAll(bool canSendMessage) { Recalculations++; }
        public int GetMaxGoodwill(Faction faction) => faction.MaxGoodwill;
    }
    public sealed class HistoryEventDef { }
    public static class HistoryEventDefOf
    {
        public static readonly HistoryEventDef ReachNaturalGoodwill = new HistoryEventDef();
        public static readonly HistoryEventDef GaveGift = new HistoryEventDef();
        public static readonly HistoryEventDef Traded = new HistoryEventDef();
    }
    public sealed class HistoryEvent { public HistoryEvent(HistoryEventDef def, params object[] args) { } }
    public static class HistoryEventArgsNames { public const string AffectedFaction = "Faction", CustomGoodwill = "Goodwill"; }
    public sealed class HistoryEventsManager
    {
        public int Count;
        public void RecordEvent(HistoryEvent entry, bool canApplySelfTookThoughts) { Count++; }
    }
    public sealed class Dialog_Trade { public void DoWindowContents() { } }
    public sealed class TraderKindDef { }
    public static class TradeSession { public static Verse.Pawn trader; }
    public sealed class Pawn_TraderTracker { public bool CanTradeNow = true; public TraderKindDef traderKind = new TraderKindDef(); }
    public static class PawnCapacityDefOf { public static readonly object Talking = new object(); }
    public static class SkillDefOf { public static readonly object Social = new object(); }
    public static class LetterDefOf { public static readonly object PositiveEvent = new object(), NegativeEvent = new object(); }
    public static class MessageTypeDefOf { public static readonly object PositiveEvent = new object(), NeutralEvent = new object(); }
    public static class GenDate
    {
        public static int LocalHour = 12;
        public static int HourOfDay(long ticks, float longitude) => LocalHour;
    }
}
namespace RimWorld.Planet
{
    public sealed class GlobalTargetInfo { public GlobalTargetInfo(Verse.Position cell, Verse.Map map) { } }
    public static class FactionGiftUtility
    {
        public static void GiveGift() { }
        public static void OfferGiftsCommand() { }
    }
    public sealed class TransportersArrivalAction_GiveGift { public static void GetFloatMenuOptions() { } }
}
