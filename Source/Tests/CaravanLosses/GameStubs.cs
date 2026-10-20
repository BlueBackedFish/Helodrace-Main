using System;
using System.Collections.Generic;

namespace Verse
{
    public interface IExposable { void ExposeData(); }
    public class GameComponent { public virtual void ExposeData() { } }
    public sealed class Game { public object Ledger; public T GetComponent<T>() => (T)Ledger; }
    public static class Current { public static Game Game; }
    public static class Find { public static readonly TickManager TickManager = new TickManager(); }
    public sealed class TickManager { public int TicksGame; }
    public sealed class Map { public int uniqueID = 1; public MapParent Parent = new MapParent(); }
    public sealed class MapParent { public string LabelCap = "Colony"; }
    public sealed class ThingDef { public string defName = "Item"; }
    public class Thing
    {
        public RimWorld.Faction Faction;
        public Map MapHeld = new Map();
        public ThingDef def = new ThingDef();
        public string Id = Guid.NewGuid().ToString(), LabelNoCount = "Item";
        public int stackCount = 1;
        public float MarketValue = 100;
        public string GetUniqueLoadID() => Id;
        public void TakeDamage(DamageInfo dinfo) { }
    }
    public sealed class Pawn : Thing
    {
        public bool Dead;
        public string LabelShortCap = "Merchant";
        public Pawn_HealthTracker health = new Pawn_HealthTracker();
        public Pawn_InventoryTracker inventory = new Pawn_InventoryTracker();
        public Pawn_EquipmentTracker equipment = new Pawn_EquipmentTracker();
        public RimWorld.Pawn_ApparelTracker apparel = new RimWorld.Pawn_ApparelTracker();
        public Pawn_CarryTracker carryTracker = new Pawn_CarryTracker();
        public AI.Group.Lord Lord;
        public void Kill(DamageInfo? dinfo, Hediff exactCulprit) { }
    }
    public sealed class Pawn_HealthTracker
    {
        public SummaryHealthHandler summaryHealth = new SummaryHealthHandler();
        public HediffSet hediffSet = new HediffSet();
        public void MakeDowned(DamageInfo? dinfo, Hediff hediff) { }
    }
    public sealed class SummaryHealthHandler { public float SummaryHealthPercent = 1f; }
    public sealed class HediffSet { public List<Hediff> hediffs = new List<Hediff>(); }
    public sealed class Pawn_InventoryTracker { public List<Thing> innerContainer = new List<Thing>(); }
    public sealed class Pawn_EquipmentTracker { public List<Thing> AllEquipmentListForReading = new List<Thing>(); }
    public sealed class Pawn_CarryTracker { public Thing CarriedThing; }
    public sealed class DamageDef { public bool Violent = true; public bool ExternalViolenceFor(Thing pawn) => Violent; }
    public struct DamageInfo { public float Amount; public Thing Instigator; public DamageDef Def; }
    public class Hediff
    {
        public string Id = Guid.NewGuid().ToString();
        public float Severity;
        public bool Bleeding;
        public RimWorld.HediffDef def = new RimWorld.HediffDef();
        public string GetUniqueLoadID() => Id;
    }
    public sealed class Hediff_Injury : Hediff { }
    public struct IntVec3 { }
    public enum ThingPlaceMode { Near }
    public sealed class ThingOwner
    {
        public bool TryDrop(Thing thing, IntVec3 dropLoc, Map map, ThingPlaceMode mode, int count, out Thing resultingThing,
            Action<Thing, int> placedAction, Predicate<IntVec3> nearPlaceValidator)
        { resultingThing = thing; return true; }
        public bool TryDrop(Thing thing, IntVec3 dropLoc, Map map, ThingPlaceMode mode, out Thing resultingThing,
            Action<Thing, int> placedAction, Predicate<IntVec3> nearPlaceValidator, bool playDropSound)
        { resultingThing = thing; return true; }
    }
    public enum LoadSaveMode { Saving, LoadingVars, PostLoadInit }
    public enum LookMode { Reference, Value, Deep }
    public static class Scribe
    {
        public static LoadSaveMode mode;
        public static Dictionary<string, object> Data = new Dictionary<string, object>();
        public static void Look<T>(ref T value, string key, T defaultValue)
        {
            if (mode == LoadSaveMode.LoadingVars) value = Data.TryGetValue(key, out var saved) ? (T)saved : defaultValue;
            else if (mode == LoadSaveMode.Saving) Data[key] = value;
        }
    }
    public static class Scribe_Values { public static void Look<T>(ref T value, string key, T defaultValue = default) => Scribe.Look(ref value, key, defaultValue); }
    public static class Scribe_References { public static void Look<T>(ref T value, string key) => Scribe.Look(ref value, key, default); }
    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> values, string key, LookMode look)
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit) return;
            if (look == LookMode.Value)
            {
                if (Scribe.mode == LoadSaveMode.LoadingVars) values = new List<T>((List<T>)Scribe.Data[key]);
                else Scribe.Data[key] = new List<T>(values);
                return;
            }
            var outer = Scribe.Data;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var saved = new List<Dictionary<string, object>>();
                foreach (var value in values)
                {
                    Scribe.Data = new Dictionary<string, object>();
                    ((IExposable)value).ExposeData(); saved.Add(Scribe.Data);
                }
                outer[key] = saved;
            }
            else
            {
                values = new List<T>();
                foreach (var item in (List<Dictionary<string, object>>)outer[key])
                {
                    Scribe.Data = item;
                    var value = Activator.CreateInstance<T>(); ((IExposable)value).ExposeData(); values.Add(value);
                }
            }
            Scribe.Data = outer;
        }
    }
}
namespace Verse.AI.Group
{
    public sealed class Lord { public int loadID = 1; public object LordJob = new RimWorld.LordJob_TradeWithColony(); }
    public static class LordUtility { public static Lord GetLord(this Verse.Pawn pawn) => pawn.Lord; }
}
namespace RimWorld
{
    public sealed class Faction { public static readonly Faction OfPlayer = new Faction(); public string Name = "LOW", DefName = "HD_HelodCivilLowFaction"; public int Goodwill = -80; }
    public sealed class LordJob_TradeWithColony { }
    public sealed class Pawn_ApparelTracker { public List<Verse.Thing> WornApparel = new List<Verse.Thing>(); }
    public sealed class HediffDef { }
    public static class HediffDefOf { public static readonly HediffDef BloodLoss = new HediffDef(); }
}
namespace Helodrace.Economy
{
    public static class HelodMoneyTrading
    {
        public static bool IsHelodFaction(RimWorld.Faction faction)
            => faction != RimWorld.Faction.OfPlayer && (faction?.DefName == "HD_HelodCivilLowFaction" || faction?.DefName == "HD_HelodCivilHighFaction");
    }
}
