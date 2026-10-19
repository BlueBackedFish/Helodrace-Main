using System.Collections.Generic;

// Only the map lifecycle, persistence and incident surfaces used by the real
// HAM component. Actual game patch targets are checked by Verify-HamHooks.ps1.
namespace Verse
{
    public class MapComponent
    {
        protected readonly Map map;
        public MapComponent(Map map) { this.map = map; }
        public virtual void FinalizeInit() { }
        public virtual void ExposeData() { }
        public virtual void MapComponentTick() { }
    }
    public sealed class Map { public bool IsPlayerHome = true; public int uniqueID, Tile; }
    public sealed class Pawn { public Map Map; }
    public static class Find
    {
        public static readonly TickManager TickManager = new TickManager();
        public static readonly WorldGrid WorldGrid = new WorldGrid();
        public static readonly World World = new World();
    }
    public sealed class TickManager { public int TicksAbs, TicksGame; }
    public sealed class World { public readonly WorldInfo info = new WorldInfo(); }
    public sealed class WorldInfo { public int Seed; }
    public sealed class WorldGrid
    {
        public float Longitude;
        public (float x, float y) LongLatOf(int tile) => (Longitude, 0);
    }
    public static class Scribe_Values
    {
        public static bool Loading;
        public static Dictionary<string, object> Stored = new Dictionary<string, object>();
        public static void Look<T>(ref T value, string key, T defaultValue)
        {
            if (Loading) value = Stored.TryGetValue(key, out object saved) ? (T)saved : defaultValue;
            else Stored[key] = value;
        }
    }
    public static class Messages { public static void Message(string text, Pawn pawn, object type) { } }
}
namespace RimWorld
{
    public sealed class FactionDef { public string defName; }
    public sealed class Faction { public FactionDef def = new FactionDef(); public bool defeated; }
    public static class GenDate
    {
        public const int TicksPerDay = 60000;
        public static long LocalTicksOffsetFromLongitude(float longitude) => (long)(longitude * (TicksPerDay / 360f));
    }
    public sealed class IncidentDef
    {
        public readonly object category = new object();
        public readonly IncidentWorker Worker = new IncidentWorker();
    }
    public sealed class IncidentWorker
    {
        public int Calls;
        public bool Success = true;
        public bool TryExecute(IncidentParms parms) { Calls++; return Success; }
    }
    public sealed class IncidentParms { public bool forced; }
    public static class IncidentDefOf { public static readonly IncidentDef GiveQuest_Random = new IncidentDef(); }
    public static class StorytellerUtility { public static IncidentParms DefaultParmsNow(object category, Verse.Map map) => new IncidentParms(); }
    public static class MessageTypeDefOf { public static readonly object PositiveEvent = new object(); }
}
namespace Helodrace
{
    public static class HamRadioUtility
    {
        public static bool IsHamFaction(RimWorld.Faction faction) => faction != null && HamRadioRoutingRules.IsHamFaction(faction.def?.defName);
        public static string RandomChatter() => "Ordinary chatter";
    }
}
