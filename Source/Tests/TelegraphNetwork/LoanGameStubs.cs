using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Verse
{
    public interface IExposable { void ExposeData(); }
    public enum LoadSaveMode { Saving, LoadingVars, PostLoadInit }
    public struct IntVec3 { }
    public sealed class MapPawns { public readonly List<Pawn> AllPawnsSpawned = new List<Pawn>(); }
    public static class ThingMaker { public static Thing MakeThing(ThingDef def) => new Thing { def = def }; }
    public enum ThingPlaceMode { Near }
    public static class GenPlace
    {
        public static int PlacesBeforeFailure = int.MaxValue;
        public static int PartialPlacement;
        public static bool TryPlaceThing(Thing thing, Position cell, Map map, ThingPlaceMode mode, Action<Thing, int> placedAction)
        {
            if (PlacesBeforeFailure-- <= 0) return false;
            if (PartialPlacement > 0)
            {
                int count = Math.Min(PartialPlacement, thing.stackCount - 1);
                var placed = new Thing { def = thing.def, stackCount = count, Map = map, Position = cell };
                map.listerThings.AllThings.Add(placed); thing.stackCount -= count;
                placedAction(placed, count); PartialPlacement = 0;
                return false;
            }
            thing.Map = map; thing.Position = cell; map.listerThings.AllThings.Add(thing);
            placedAction(thing, thing.stackCount); return true;
        }
    }
}
namespace Verse.AI.Group
{
    public class LordJob { }
    public class LordToil { }
    public class LordToil_ExitMap : LordToil { }
    public class LordToil_ExitMapAndEscortCarriers : LordToil { }
}
namespace RimWorld
{
    public class LordJob_TradeWithColony : LordJob { public IntVec3 chillSpot = new IntVec3(); }
    internal class LordToil_DefendTraderCaravan : LordToil { }
    public sealed class IncidentParms { public Faction faction; public TraderKindDef traderKind; public bool forced; public Map Map; }
    public sealed class IncidentWorker
    {
        public Func<IncidentParms, bool> Execute;
        public int Attempts;
        public bool TryExecute(IncidentParms parms) { Attempts++; return Execute?.Invoke(parms) ?? false; }
    }
    public sealed class IncidentDef { public readonly object category = new object(); public readonly IncidentWorker Worker = new IncidentWorker(); }
    public static class IncidentDefOf { public static readonly IncidentDef TraderCaravanArrival = new IncidentDef(); }
    public static class StorytellerUtility { public static IncidentParms DefaultParmsNow(object category, Map map) => new IncidentParms { Map = map }; }
}
namespace Helodrace
{
    public sealed class CompTelegraphTable
    {
        public Thing parent;
        public MapComponent_TelegraphNetwork Network;
        public int Cells = 10;
        public string TransmissionFailure(Faction target, bool allowHostileTrade = false)
        {
            if (parent == null || !parent.Spawned || parent.Faction != Faction.OfPlayer || Network?.Provider != target)
                return "unavailable";
            var status = Network.Status(Cells > 0);
            if (status != TelegraphConnectionStatus.Online) return status.ToString();
            return !allowHostileTrade && target.Hostile ? "hostile" : null;
        }
        public bool ConsumePrimaryCell()
        {
            if (TransmissionFailure(Network.Provider, true) != null) return false;
            Cells--; return true;
        }
    }
}
namespace Helodrace.Economy
{
    internal static class HelodMoneyTrading { public static ThingDef MoneyDef => DefDatabase<ThingDef>.Value; }
    public sealed class FinancialCaravanScope : IDisposable
    {
        public static Faction Current;
        private readonly Faction previous;
        private FinancialCaravanScope(Faction faction) { previous = Current; Current = faction; }
        public static FinancialCaravanScope Enter(Faction faction) => new FinancialCaravanScope(faction);
        public void Dispose() { Current = previous; }
    }
    public sealed class LordJob_FinancialCaravan : LordJob_TradeWithColony { public bool Protected = true; }
}
