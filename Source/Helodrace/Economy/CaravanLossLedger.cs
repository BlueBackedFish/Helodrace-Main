using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Economy
{
    public enum CaravanLossKind { DroppedItem, Injury, Death, ForwardBaseOverdue, LoanOverdue }

    public sealed class CaravanLossEntry : IExposable
    {
        public Faction Faction;
        public string FactionName, CaravanKey, MapName, PawnId, PawnName, Subject, ItemDefName;
        public int Tick, Count;
        public CaravanLossKind Kind;
        public double UnitValue, Value;
        public int GoodwillPenalty;
        public float FinancialPenalty;
        public void ExposeData()
        {
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref FactionName, "factionName");
            Scribe_Values.Look(ref CaravanKey, "caravan");
            Scribe_Values.Look(ref MapName, "map");
            Scribe_Values.Look(ref PawnId, "pawnId");
            Scribe_Values.Look(ref PawnName, "pawnName");
            Scribe_Values.Look(ref Subject, "subject");
            Scribe_Values.Look(ref ItemDefName, "itemDef");
            Scribe_Values.Look(ref Tick, "tick");
            Scribe_Values.Look(ref Count, "count");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref UnitValue, "unitValue");
            Scribe_Values.Look(ref Value, "value");
            Scribe_Values.Look(ref GoodwillPenalty, "goodwillPenalty");
            Scribe_Values.Look(ref FinancialPenalty, "financialPenalty");
        }
    }

    public sealed class CaravanPawnLoss : IExposable
    {
        public Faction Faction;
        public string CaravanKey, MapName, PawnId, PawnName;
        public int MapId;
        public double InitialValue, InjuryValue;
        public float InitialHealth;
        public bool Dead, Departed;
        public List<string> PlayerWounds = new List<string>();
        public void ExposeData()
        {
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref CaravanKey, "caravan");
            Scribe_Values.Look(ref MapName, "map");
            Scribe_Values.Look(ref MapId, "mapId");
            Scribe_Values.Look(ref PawnId, "pawnId");
            Scribe_Values.Look(ref PawnName, "pawnName");
            Scribe_Values.Look(ref InitialValue, "initialValue");
            Scribe_Values.Look(ref InjuryValue, "injuryValue");
            Scribe_Values.Look(ref InitialHealth, "initialHealth");
            Scribe_Values.Look(ref Dead, "dead");
            Scribe_Values.Look(ref Departed, "departed");
            Scribe_Collections.Look(ref PlayerWounds, "playerWounds", LookMode.Value);
        }
    }

    // This is an audit ledger only. Recording a loss never spends money,
    // changes goodwill, creates debt, or settles a compensation claim.
    public sealed class GameComponent_CaravanLosses : GameComponent
    {
        private List<CaravanLossEntry> entries = new List<CaravanLossEntry>();
        private List<CaravanPawnLoss> victims = new List<CaravanPawnLoss>();
        private readonly Dictionary<string, CaravanPawnLoss> byVisit = new Dictionary<string, CaravanPawnLoss>();
        private readonly Dictionary<string, CaravanPawnLoss> latest = new Dictionary<string, CaravanPawnLoss>();
        private readonly HashSet<string> arrears = new HashSet<string>();
        public GameComponent_CaravanLosses(Game game) { }
        public static GameComponent_CaravanLosses Current => Verse.Current.Game?.GetComponent<GameComponent_CaravanLosses>();
        public IEnumerable<CaravanLossEntry> Entries(Faction faction) => entries.Where(e => faction == null || e.Faction == faction);
        public IEnumerable<Faction> RecordedFactions => entries.Select(e => e.Faction).Where(f => f != null).Distinct();
        public double Total(Faction faction, CaravanLossKind? kind = null)
            => Entries(faction).Where(e => !kind.HasValue || e.Kind == kind).Sum(e => e.Value);
        public CaravanPawnLoss LatestVictim(string pawnId) => latest.TryGetValue(pawnId, out var value) ? value : null;
        public void MarkDeparture(string pawnId)
        {
            if (latest.TryGetValue(pawnId, out var victim)) victim.Departed = true;
        }

        public CaravanPawnLoss GetOrCreate(Faction faction, string caravan, string map, int mapId, string pawnId, string pawnName,
            double marketValue, float health)
        {
            string key = caravan + "/" + pawnId;
            if (byVisit.TryGetValue(key, out var found)) return found;
            var victim = new CaravanPawnLoss { Faction = faction, CaravanKey = caravan, MapName = map, MapId = mapId,
                PawnId = pawnId, PawnName = pawnName, InitialValue = Money(marketValue), InitialHealth = Math.Max(0.0001f, health) };
            victims.Add(victim); byVisit[key] = victim; latest[pawnId] = victim;
            return victim;
        }

        public double RecordInjury(CaravanPawnLoss victim, float beforeHealth, float afterHealth, double injuryBefore)
        {
            if (victim.Dead || float.IsNaN(beforeHealth) || float.IsNaN(afterHealth)) return 0d;
            double target = Math.Min(victim.InitialValue, injuryBefore
                + victim.InitialValue * Math.Max(0d, beforeHealth - afterHealth) / victim.InitialHealth);
            double value = Money(Math.Max(0d, target - victim.InjuryValue));
            if (value <= 0d) return 0d;
            victim.InjuryValue = Math.Min(victim.InitialValue, Money(victim.InjuryValue + value));
            Add(victim, CaravanLossKind.Injury, victim.PawnName, 1, victim.InitialValue, value);
            return value;
        }

        public bool RecordDeath(CaravanPawnLoss victim)
        {
            if (victim.Dead) return false;
            victim.Dead = true;
            Add(victim, CaravanLossKind.Death, victim.PawnName, 1, victim.InitialValue,
                Money(Math.Max(0d, victim.InitialValue - victim.InjuryValue)));
            return true;
        }

        public void RecordDrop(CaravanPawnLoss victim, string defName, string label, int count, double unitValue)
        {
            if (count <= 0) return;
            Add(victim, CaravanLossKind.DroppedItem, label, count, Money(unitValue), Money(unitValue * count), defName);
        }

        public CaravanLossEntry RecordArrears(Faction faction, CaravanLossKind kind, string contractKey, string label,
            string location, int unpaidNotes, double silverPerNote)
        {
            if ((kind != CaravanLossKind.ForwardBaseOverdue && kind != CaravanLossKind.LoanOverdue)
                || faction == null || string.IsNullOrEmpty(contractKey) || unpaidNotes <= 0
                || silverPerNote <= 0 || double.IsNaN(silverPerNote) || double.IsInfinity(silverPerNote)) return null;
            if (!arrears.Add(kind + "/" + contractKey)) return null;
            var entry = new CaravanLossEntry { Faction = faction, FactionName = faction.Name, CaravanKey = contractKey,
                Subject = label, MapName = location, Tick = Find.TickManager.TicksGame, Kind = kind, Count = unpaidNotes,
                UnitValue = Money(silverPerNote), Value = Money(unpaidNotes * silverPerNote) };
            entries.Add(entry);
            return entry;
        }

        private void Add(CaravanPawnLoss victim, CaravanLossKind kind, string subject, int count, double unitValue, double value, string itemDef = null)
        {
            entries.Add(new CaravanLossEntry { Faction = victim.Faction, FactionName = victim.Faction?.Name,
                CaravanKey = victim.CaravanKey, MapName = victim.MapName, PawnId = victim.PawnId, PawnName = victim.PawnName,
                Tick = Find.TickManager.TicksGame, Kind = kind, Subject = subject, ItemDefName = itemDef,
                Count = count, UnitValue = unitValue, Value = value });
        }

        private static double Money(double value)
            => double.IsNaN(value) || double.IsInfinity(value) ? 0d : Math.Round(Math.Max(0d, value), 2, MidpointRounding.AwayFromZero);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref entries, "caravanLossEntries", LookMode.Deep);
            Scribe_Collections.Look(ref victims, "caravanLossVictims", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            if (entries == null) entries = new List<CaravanLossEntry>();
            if (victims == null) victims = new List<CaravanPawnLoss>();
            byVisit.Clear(); latest.Clear(); arrears.Clear();
            foreach (CaravanLossEntry entry in entries)
                if (entry.Kind == CaravanLossKind.ForwardBaseOverdue || entry.Kind == CaravanLossKind.LoanOverdue)
                    arrears.Add(entry.Kind + "/" + entry.CaravanKey);
            foreach (CaravanPawnLoss victim in victims)
            {
                if (victim.PlayerWounds == null) victim.PlayerWounds = new List<string>();
                byVisit[victim.CaravanKey + "/" + victim.PawnId] = victim;
                latest[victim.PawnId] = victim;
            }
        }
    }
}
