using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public enum HelodMarketStandard
    {
        Sthaler,
        Silver
    }

    public class HelodMarketAsset
    {
        public readonly string defName;
        public readonly string labelKey;
        public readonly float baseSilverValue;
        public readonly float volatility;
        public readonly float minFactor;
        public readonly float maxFactor;
        public readonly bool usesOilIndex;
        public readonly float spreadPct;
        public readonly int settlementHours;
        public readonly int settlementVarianceHours;

        public HelodMarketAsset(string defName, string labelKey, float baseSilverValue, float volatility, float minFactor, float maxFactor, bool usesOilIndex = false, float spreadPct = 0.02f, int settlementHours = 12, int settlementVarianceHours = 0)
        {
            this.defName = defName;
            this.labelKey = labelKey;
            this.baseSilverValue = baseSilverValue;
            this.volatility = volatility;
            this.minFactor = minFactor;
            this.maxFactor = maxFactor;
            this.usesOilIndex = usesOilIndex;
            this.spreadPct = spreadPct;
            this.settlementHours = settlementHours;
            this.settlementVarianceHours = settlementVarianceHours;
        }

        public string Label => labelKey.Translate();
    }

    public class HelodMarketObservation
    {
        public readonly int day;
        public readonly float? value;

        public HelodMarketObservation(int day, float? value)
        {
            this.day = day;
            this.value = value;
        }
    }

    public enum HelodMarketTradeSide
    {
        Buy,
        Sell
    }

    public class HelodPendingTrade
    {
        public string assetDefName;
        public string deliveryDefName;
        public HelodMarketTradeSide side;
        public int count;
        public int settleTick;
        public float unitPrice;
        public int settlementHours;
    }

    public class HelodMarketState : GameComponent
    {
        private const int HistoryLimit = 30;
        private const float GoldStandardSthalerSilverValue = 5f;
        private const string OilIndexKey = "HD_OilIndex";
        private const float OilIndexVolatility = 0.09f;
        private const float OilIndexMinFactor = 0.45f;
        private const float OilIndexMaxFactor = 2.10f;

        private HelodMarketStandard standard = HelodMarketStandard.Sthaler;
        private int nextUpdateTick;
        private List<string> assetKeys = new List<string>();
        private List<float> priceFactors = new List<float>();
        private List<string> historyKeys = new List<string>();
        private List<string> historyValues = new List<string>();
        private List<string> observedKeys = new List<string>();
        private List<string> observedValues = new List<string>();
        private List<string> pendingTradeValues = new List<string>();
        private int completedTradeCount;
        private float completedTradeVolume;

        private readonly Dictionary<string, float> factorByDef = new Dictionary<string, float>();
        private readonly Dictionary<string, List<float>> historyByDef = new Dictionary<string, List<float>>();
        private readonly Dictionary<string, List<HelodMarketObservation>> observedByDef = new Dictionary<string, List<HelodMarketObservation>>();
        private readonly List<HelodPendingTrade> pendingTrades = new List<HelodPendingTrade>();

        public static readonly List<HelodMarketAsset> Assets = new List<HelodMarketAsset>
        {
            new HelodMarketAsset("Gold", "HD_MarketAsset_Gold", 10f, 0.04f, 0.70f, 1.60f, false, 0.022f, 12),
            new HelodMarketAsset("HD_CrudeOil_Low", "HD_MarketAsset_CrudeOil", 1.5f, 0.0f, 0.45f, 2.10f, true, 0.045f, 720, 96),
            new HelodMarketAsset("HD_Diesel", "HD_MarketAsset_Diesel", 4.0f, 0.0f, 0.45f, 2.10f, true, 0.045f, 720, 96),
            new HelodMarketAsset("HD_Kerosene", "HD_MarketAsset_Kerosene", 3.5f, 0.0f, 0.45f, 2.10f, true, 0.045f, 720, 96),
            new HelodMarketAsset("HD_GovernmentBond", "HD_MarketAsset_GovernmentBond", 1000f, 0.025f, 0.85f, 1.25f, false, 0.018f, 24),
            new HelodMarketAsset("HD_ContinentalPetroleumBond", "HD_MarketAsset_ContinentalPetroleumBond", 500f, 0.08f, 0.45f, 2.20f, false, 0.055f, 48),
            new HelodMarketAsset("HD_Money", "HD_MarketAsset_Sthaler", 5f, 0.035f, 0.55f, 1.65f, false, 0.012f, 6)
        };

        public static readonly List<HelodMarketAsset> DisplayAssets = new List<HelodMarketAsset>
        {
            new HelodMarketAsset("Gold", "HD_MarketAsset_Gold", 10f, 0.04f, 0.70f, 1.60f, false, 0.022f, 12),
            new HelodMarketAsset(OilIndexKey, "HD_MarketAsset_OilIndex", 100f, 0.0f, OilIndexMinFactor, OilIndexMaxFactor, false, 0.045f, 720, 96),
            new HelodMarketAsset("HD_CrudeOil_Low", "HD_MarketAsset_CrudeOil", 1.5f, 0.0f, 0.45f, 2.10f, true, 0.045f, 720, 96),
            new HelodMarketAsset("HD_Diesel", "HD_MarketAsset_Diesel", 4.0f, 0.0f, 0.45f, 2.10f, true, 0.045f, 720, 96),
            new HelodMarketAsset("HD_Kerosene", "HD_MarketAsset_Kerosene", 3.5f, 0.0f, 0.45f, 2.10f, true, 0.045f, 720, 96),
            new HelodMarketAsset("HD_GovernmentBond", "HD_MarketAsset_GovernmentBond", 1000f, 0.025f, 0.85f, 1.25f, false, 0.018f, 24),
            new HelodMarketAsset("HD_ContinentalPetroleumBond", "HD_MarketAsset_ContinentalPetroleumBond", 500f, 0.08f, 0.45f, 2.20f, false, 0.055f, 48),
            new HelodMarketAsset("HD_Money", "HD_MarketAsset_Sthaler", 5f, 0.035f, 0.55f, 1.65f, false, 0.012f, 6)
        };

        public HelodMarketState(Game game)
        {
        }

        public HelodMarketStandard Standard => standard;

        public static HelodMarketState Current => Verse.Current.Game?.GetComponent<HelodMarketState>();

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            InitializeMissingData();
            nextUpdateTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            InitializeMissingData();
            if (nextUpdateTick <= Find.TickManager.TicksGame)
            {
                nextUpdateTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            ResolvePendingTrades();
            if (Find.TickManager.TicksGame >= nextUpdateTick)
            {
                UpdateMarket();
                nextUpdateTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref standard, "standard", HelodMarketStandard.Sthaler);
            Scribe_Values.Look(ref nextUpdateTick, "nextUpdateTick", 0);
            Scribe_Collections.Look(ref assetKeys, "assetKeys", LookMode.Value);
            Scribe_Collections.Look(ref priceFactors, "priceFactors", LookMode.Value);
            Scribe_Collections.Look(ref historyKeys, "historyKeys", LookMode.Value);
            Scribe_Collections.Look(ref historyValues, "historyValues", LookMode.Value);
            Scribe_Collections.Look(ref observedKeys, "observedKeys", LookMode.Value);
            Scribe_Collections.Look(ref observedValues, "observedValues", LookMode.Value);
            Scribe_Collections.Look(ref pendingTradeValues, "pendingTradeValues", LookMode.Value);
            Scribe_Values.Look(ref completedTradeCount, "completedTradeCount", 0);
            Scribe_Values.Look(ref completedTradeVolume, "completedTradeVolume", 0f);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildDictionaries();
                InitializeMissingData();
            }
        }

        public void SwitchToSilverStandard()
        {
            if (standard == HelodMarketStandard.Silver)
            {
                return;
            }

            standard = HelodMarketStandard.Silver;
            RecordCurrentPrices();
            SyncSaveLists();
        }

        public bool TryGetMarketValue(ThingDef def, out float value)
        {
            value = 0f;
            HelodMarketAsset asset = AssetFor(def);
            if (asset == null)
            {
                return false;
            }

            value = MarketValue(asset);
            return true;
        }

        public float MarketValue(HelodMarketAsset asset)
        {
            if (asset.defName == "HD_Money" && standard == HelodMarketStandard.Sthaler)
            {
                return asset.baseSilverValue;
            }

            if (asset.defName == "Gold" && standard == HelodMarketStandard.Sthaler)
            {
                return asset.baseSilverValue;
            }

            if (asset.defName == OilIndexKey)
            {
                return OilIndexValue;
            }

            float factor = asset.usesOilIndex ? OilIndexFactor : FactorFor(asset.defName);
            return Mathf.Max(0.01f, asset.baseSilverValue * factor);
        }

        public float OilIndexFactor => FactorFor(OilIndexKey);

        public float OilIndexValue => 100f * OilIndexFactor;

        public float SthalerSilverValue
        {
            get
            {
                HelodMarketAsset sthaler = Assets.Find(asset => asset.defName == "HD_Money");
                return sthaler == null ? GoldStandardSthalerSilverValue : Mathf.Max(0.01f, MarketValue(sthaler));
            }
        }

        public float QuotedPrice(HelodMarketAsset asset)
        {
            if (asset.defName == OilIndexKey)
            {
                return OilIndexValue;
            }

            float silverValue = MarketValue(asset);
            return asset.defName == "HD_Money" ? silverValue : silverValue / SthalerSilverValue;
        }

        public IReadOnlyList<HelodPendingTrade> PendingTrades => pendingTrades;

        public bool HasPendingTrade(string assetDefName)
        {
            return pendingTrades.Any(trade => trade.assetDefName == assetDefName);
        }

        public float CreditLimit(Map map)
        {
            float wealth = Mathf.Max(0f, map?.wealthWatcher?.WealthTotal ?? 0f);
            float wealthCredit = Mathf.Sqrt(Mathf.Max(wealth, 1f)) * 18f;
            float experienceCredit = completedTradeCount * 60f + Mathf.Sqrt(Mathf.Max(completedTradeVolume, 0f)) * 8f;
            float goodwillCredit = HelodGoodwill() * 4f;
            return Mathf.Max(100f, wealthCredit + experienceCredit + goodwillCredit);
        }

        public bool TryStartTrade(Map map, IntVec3 deliveryCell, HelodMarketAsset asset, HelodMarketTradeSide side, int count, int settlementHours, out string failReason)
        {
            failReason = null;
            if (map == null || count <= 0)
            {
                failReason = "HD_TelegraphTable_Trade_Failed".Translate();
                return false;
            }

            if (HasPendingTrade(asset.defName))
            {
                failReason = "HD_TelegraphTable_Trade_PendingLocked".Translate();
                return false;
            }

            ThingDef deliveryDef = DeliveryThingDef(asset);
            ThingDef currencyDef = CurrencyThingDef();
            if (deliveryDef == null || currencyDef == null)
            {
                failReason = "HD_TelegraphTable_Trade_Failed".Translate();
                return false;
            }

            float unitPrice = side == HelodMarketTradeSide.Buy ? AskPrice(asset) : BidPrice(asset);
            float notional = unitPrice * count;
            if (notional > CreditLimit(map))
            {
                failReason = "HD_TelegraphTable_Trade_CreditTooLow".Translate(notional.ToString("F0"), CreditLimit(map).ToString("F0"));
                return false;
            }

            if (side == HelodMarketTradeSide.Buy)
            {
                int currencyNeeded = Mathf.CeilToInt(notional);
                if (CountThing(map, currencyDef) < currencyNeeded)
                {
                    failReason = "HD_TelegraphTable_Trade_NotEnoughCurrency".Translate(currencyDef.label, currencyNeeded);
                    return false;
                }
            }
            else if (CountThing(map, deliveryDef) < count)
            {
                failReason = "HD_TelegraphTable_Trade_NotEnoughGoods".Translate(deliveryDef.label, count);
                return false;
            }

            pendingTrades.Add(new HelodPendingTrade
            {
                assetDefName = asset.defName,
                deliveryDefName = deliveryDef.defName,
                side = side,
                count = count,
                settleTick = Find.TickManager.TicksGame + settlementHours * GenDate.TicksPerHour,
                unitPrice = unitPrice,
                settlementHours = settlementHours
            });
            SyncSaveLists();
            return true;
        }

        public float BidPrice(HelodMarketAsset asset)
        {
            if (IsFixedGoldStandardGold(asset))
            {
                return QuotedPrice(asset);
            }

            return Mathf.Max(0.01f, QuotedPrice(asset) * (1f - asset.spreadPct * 0.5f));
        }

        public float AskPrice(HelodMarketAsset asset)
        {
            if (IsFixedGoldStandardGold(asset))
            {
                return QuotedPrice(asset);
            }

            return Mathf.Max(0.01f, QuotedPrice(asset) * (1f + asset.spreadPct * 0.5f));
        }

        public bool IsFixedGoldStandardGold(HelodMarketAsset asset)
        {
            return asset.defName == "Gold" && standard == HelodMarketStandard.Sthaler;
        }

        public string CurrencyLabel()
        {
            return "HD_MarketCurrency_Sthaler".Translate();
        }

        public string StandardLabel()
        {
            return standard == HelodMarketStandard.Sthaler ? "HD_MarketStandard_Gold".Translate() : "HD_MarketStandard_Credit".Translate();
        }

        public void ObserveAsset(HelodMarketAsset asset)
        {
            InitializeMissingData();
            int day = CurrentDay;
            float value = QuotedPrice(asset);
            if (!observedByDef.TryGetValue(asset.defName, out List<HelodMarketObservation> observations))
            {
                observations = new List<HelodMarketObservation>();
                observedByDef[asset.defName] = observations;
            }

            int existingIndex = observations.FindIndex(entry => entry.day == day);
            if (existingIndex >= 0)
            {
                observations[existingIndex] = new HelodMarketObservation(day, value);
            }
            else
            {
                observations.Add(new HelodMarketObservation(day, value));
            }

            observations.Sort((a, b) => a.day.CompareTo(b.day));
            while (observations.Count > HistoryLimit)
            {
                observations.RemoveAt(0);
            }

            SyncSaveLists();
        }

        public bool WasObservedToday(string defName)
        {
            InitializeMissingData();
            int day = CurrentDay;
            if (!observedByDef.TryGetValue(defName, out List<HelodMarketObservation> observations))
            {
                return false;
            }

            return observations.Any(entry => entry.day == day && entry.value.HasValue);
        }

        public List<HelodMarketObservation> ObservationLogFor(string defName)
        {
            InitializeMissingData();
            int today = CurrentDay;
            int firstDay = Mathf.Max(0, today - HistoryLimit + 1);
            Dictionary<int, float> valuesByDay = new Dictionary<int, float>();
            if (observedByDef.TryGetValue(defName, out List<HelodMarketObservation> observations))
            {
                foreach (HelodMarketObservation observation in observations)
                {
                    if (observation.value.HasValue)
                    {
                        valuesByDay[observation.day] = observation.value.Value;
                    }
                }
            }

            List<HelodMarketObservation> result = new List<HelodMarketObservation>();
            for (int day = today; day >= firstDay; day--)
            {
                if (valuesByDay.TryGetValue(day, out float value))
                {
                    result.Add(new HelodMarketObservation(day, value));
                }
                else
                {
                    result.Add(new HelodMarketObservation(day, null));
                }
            }

            return result;
        }

        public List<float> HistoryFor(string defName)
        {
            InitializeMissingData();
            if (historyByDef.TryGetValue(defName, out List<float> history))
            {
                return history;
            }

            return new List<float>();
        }

        public static HelodMarketAsset AssetFor(ThingDef def)
        {
            if (def == null)
            {
                return null;
            }

            return Assets.Find(asset => asset.defName == def.defName);
        }

        public static ThingDef DeliveryThingDef(HelodMarketAsset asset)
        {
            if (asset == null || asset.defName == OilIndexKey)
            {
                return null;
            }

            return DefDatabase<ThingDef>.GetNamedSilentFail(asset.defName);
        }

        public ThingDef CurrencyThingDef()
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail("HD_Money");
        }

        private static int CurrentDay => Find.TickManager?.TicksGame / GenDate.TicksPerDay ?? 0;

        private void UpdateMarket()
        {
            InitializeMissingData();
            float oilFactor = OilIndexFactor;
            float oilDrift = (1f - oilFactor) * 0.018f;
            float oilShock = Rand.Range(-OilIndexVolatility, OilIndexVolatility);
            factorByDef[OilIndexKey] = Mathf.Clamp(oilFactor + oilDrift + oilShock, OilIndexMinFactor, OilIndexMaxFactor);

            foreach (HelodMarketAsset asset in Assets)
            {
                if (asset.usesOilIndex)
                {
                    continue;
                }

                if (standard == HelodMarketStandard.Sthaler && (asset.defName == "HD_Money" || asset.defName == "Gold"))
                {
                    factorByDef[asset.defName] = 1f;
                    continue;
                }

                if (asset.volatility <= 0f)
                {
                    factorByDef[asset.defName] = Mathf.Clamp(FactorFor(asset.defName), asset.minFactor, asset.maxFactor);
                    continue;
                }

                float factor = FactorFor(asset.defName);
                float drift = (1f - factor) * 0.018f;
                float shock = Rand.Range(-asset.volatility, asset.volatility);
                factorByDef[asset.defName] = Mathf.Clamp(factor + drift + shock, asset.minFactor, asset.maxFactor);
            }

            RecordCurrentPrices();
            SyncSaveLists();
        }

        private void ResolvePendingTrades()
        {
            if (pendingTrades.Count == 0 || Find.Maps == null)
            {
                return;
            }

            int tick = Find.TickManager.TicksGame;
            for (int i = pendingTrades.Count - 1; i >= 0; i--)
            {
                HelodPendingTrade trade = pendingTrades[i];
                if (trade.settleTick > tick)
                {
                    continue;
                }

                Map map = Find.CurrentMap ?? Find.Maps.Find(m => m.IsPlayerHome);
                if (TryResolveTrade(map, trade))
                {
                    completedTradeCount++;
                    completedTradeVolume += trade.unitPrice * trade.count;
                }

                pendingTrades.RemoveAt(i);
            }

            SyncSaveLists();
        }

        private bool TryResolveTrade(Map map, HelodPendingTrade trade)
        {
            if (map == null)
            {
                return false;
            }

            ThingDef deliveryDef = DefDatabase<ThingDef>.GetNamedSilentFail(trade.deliveryDefName);
            ThingDef currencyDef = CurrencyThingDef();
            if (deliveryDef == null || currencyDef == null)
            {
                return false;
            }

            int currencyCount = Mathf.CeilToInt(trade.unitPrice * trade.count);
            if (trade.side == HelodMarketTradeSide.Buy)
            {
                if (!ConsumeThing(map, currencyDef, currencyCount))
                {
                    Messages.Message("HD_TelegraphTable_Trade_SettlementFailed".Translate(deliveryDef.label), MessageTypeDefOf.NegativeEvent);
                    return false;
                }

                SpawnThing(map, deliveryDef, trade.count);
            }
            else
            {
                if (!ConsumeThing(map, deliveryDef, trade.count))
                {
                    Messages.Message("HD_TelegraphTable_Trade_SettlementFailed".Translate(deliveryDef.label), MessageTypeDefOf.NegativeEvent);
                    return false;
                }

                SpawnThing(map, currencyDef, currencyCount);
            }

            Messages.Message("HD_TelegraphTable_Trade_Settled".Translate(deliveryDef.label, trade.count), MessageTypeDefOf.PositiveEvent);
            return true;
        }

        private void RecordCurrentPrices()
        {
            RecordPrice(OilIndexKey, OilIndexValue);

            foreach (HelodMarketAsset asset in Assets)
            {
                RecordPrice(asset.defName, QuotedPrice(asset));
            }
        }

        private void RecordPrice(string key, float value)
        {
            if (!historyByDef.TryGetValue(key, out List<float> history))
            {
                history = new List<float>();
                historyByDef[key] = history;
            }

            history.Add(value);
            while (history.Count > HistoryLimit)
            {
                history.RemoveAt(0);
            }
        }

        private float FactorFor(string defName)
        {
            if (factorByDef.TryGetValue(defName, out float factor))
            {
                return factor;
            }

            return 1f;
        }

        private void InitializeMissingData()
        {
            RebuildDictionaries();
            bool changed = false;
            if (!factorByDef.ContainsKey(OilIndexKey))
            {
                factorByDef[OilIndexKey] = 1f;
                changed = true;
            }

            if (!historyByDef.ContainsKey(OilIndexKey) || historyByDef[OilIndexKey].Count == 0)
            {
                historyByDef[OilIndexKey] = new List<float> { OilIndexValue };
                changed = true;
            }

            foreach (HelodMarketAsset asset in Assets)
            {
                if (!asset.usesOilIndex && !factorByDef.ContainsKey(asset.defName))
                {
                    factorByDef[asset.defName] = 1f;
                    changed = true;
                }

                if (!historyByDef.ContainsKey(asset.defName) || historyByDef[asset.defName].Count == 0)
                {
                    historyByDef[asset.defName] = new List<float> { QuotedPrice(asset) };
                    changed = true;
                }
            }

            if (changed)
            {
                SyncSaveLists();
            }
        }

        private void RebuildDictionaries()
        {
            factorByDef.Clear();
            historyByDef.Clear();
            observedByDef.Clear();
            pendingTrades.Clear();

            if (assetKeys != null && priceFactors != null)
            {
                int count = Math.Min(assetKeys.Count, priceFactors.Count);
                for (int i = 0; i < count; i++)
                {
                    factorByDef[assetKeys[i]] = priceFactors[i];
                }
            }

            if (historyKeys != null && historyValues != null)
            {
                int count = Math.Min(historyKeys.Count, historyValues.Count);
                for (int i = 0; i < count; i++)
                {
                    historyByDef[historyKeys[i]] = ParseHistory(historyValues[i]);
                }
            }

            if (observedKeys != null && observedValues != null)
            {
                int count = Math.Min(observedKeys.Count, observedValues.Count);
                for (int i = 0; i < count; i++)
                {
                    observedByDef[observedKeys[i]] = ParseObservations(observedValues[i]);
                }
            }

            if (pendingTradeValues != null)
            {
                foreach (string value in pendingTradeValues)
                {
                    HelodPendingTrade trade = ParsePendingTrade(value);
                    if (trade != null)
                    {
                        pendingTrades.Add(trade);
                    }
                }
            }
        }

        private void SyncSaveLists()
        {
            assetKeys = new List<string>();
            priceFactors = new List<float>();
            foreach (KeyValuePair<string, float> entry in factorByDef)
            {
                assetKeys.Add(entry.Key);
                priceFactors.Add(entry.Value);
            }

            historyKeys = new List<string>();
            historyValues = new List<string>();
            foreach (KeyValuePair<string, List<float>> entry in historyByDef)
            {
                historyKeys.Add(entry.Key);
                historyValues.Add(string.Join(",", entry.Value.ConvertAll(value => value.ToString("F3")).ToArray()));
            }

            observedKeys = new List<string>();
            observedValues = new List<string>();
            foreach (KeyValuePair<string, List<HelodMarketObservation>> entry in observedByDef)
            {
                observedKeys.Add(entry.Key);
                observedValues.Add(string.Join(",", entry.Value.ConvertAll(value => value.day + ":" + (value.value.HasValue ? value.value.Value.ToString("F3") : "null")).ToArray()));
            }

            pendingTradeValues = new List<string>();
            foreach (HelodPendingTrade trade in pendingTrades)
            {
                pendingTradeValues.Add(string.Join("|", new[]
                {
                    trade.assetDefName,
                    trade.deliveryDefName,
                    trade.side.ToString(),
                    trade.count.ToString(),
                    trade.settleTick.ToString(),
                    trade.unitPrice.ToString("F3"),
                    trade.settlementHours.ToString()
                }));
            }
        }

        private static List<float> ParseHistory(string saved)
        {
            List<float> result = new List<float>();
            if (saved.NullOrEmpty())
            {
                return result;
            }

            string[] parts = saved.Split(',');
            foreach (string part in parts)
            {
                if (float.TryParse(part, out float value))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private static List<HelodMarketObservation> ParseObservations(string saved)
        {
            List<HelodMarketObservation> result = new List<HelodMarketObservation>();
            if (saved.NullOrEmpty())
            {
                return result;
            }

            string[] parts = saved.Split(',');
            foreach (string part in parts)
            {
                string[] pair = part.Split(':');
                if (pair.Length != 2 || !int.TryParse(pair[0], out int day))
                {
                    continue;
                }

                if (pair[1] == "null")
                {
                    result.Add(new HelodMarketObservation(day, null));
                }
                else if (float.TryParse(pair[1], out float value))
                {
                    result.Add(new HelodMarketObservation(day, value));
                }
            }

            return result;
        }

        private static HelodPendingTrade ParsePendingTrade(string saved)
        {
            if (saved.NullOrEmpty())
            {
                return null;
            }

            string[] parts = saved.Split('|');
            if (parts.Length != 7
                || !Enum.TryParse(parts[2], out HelodMarketTradeSide side)
                || !int.TryParse(parts[3], out int count)
                || !int.TryParse(parts[4], out int settleTick)
                || !float.TryParse(parts[5], out float unitPrice)
                || !int.TryParse(parts[6], out int settlementHours))
            {
                return null;
            }

            return new HelodPendingTrade
            {
                assetDefName = parts[0],
                deliveryDefName = parts[1],
                side = side,
                count = count,
                settleTick = settleTick,
                unitPrice = unitPrice,
                settlementHours = settlementHours
            };
        }

        private static int CountThing(Map map, ThingDef def)
        {
            if (map == null || def == null)
            {
                return 0;
            }

            int count = 0;
            List<Thing> things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (!thing.Destroyed && !thing.Position.Fogged(map))
                {
                    count += thing.stackCount;
                }
            }

            return count;
        }

        private static bool ConsumeThing(Map map, ThingDef def, int count)
        {
            if (CountThing(map, def) < count)
            {
                return false;
            }

            int remaining = count;
            List<Thing> things = map.listerThings.ThingsOfDef(def).ToList();
            for (int i = 0; i < things.Count && remaining > 0; i++)
            {
                Thing thing = things[i];
                if (thing.Destroyed || thing.Position.Fogged(map))
                {
                    continue;
                }

                int taken = Mathf.Min(remaining, thing.stackCount);
                remaining -= taken;
                if (taken >= thing.stackCount)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    thing.stackCount -= taken;
                }
            }

            return true;
        }

        private static void SpawnThing(Map map, ThingDef def, int count)
        {
            IntVec3 cell = DropCellFinder.TradeDropSpot(map);
            int remaining = count;
            while (remaining > 0)
            {
                Thing thing = ThingMaker.MakeThing(def);
                thing.stackCount = Mathf.Min(remaining, def.stackLimit);
                remaining -= thing.stackCount;
                GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
            }
        }

        private static int HelodGoodwill()
        {
            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail("HD_HelodCivilLowFaction");
            Faction faction = def == null ? null : Find.FactionManager.FirstFactionOfDef(def);
            return faction?.PlayerGoodwill ?? 0;
        }
    }

    [HarmonyPatch(typeof(Thing), "get_MarketValue")]
    public static class Patch_Thing_MarketValue_HelodMarket
    {
        public static void Postfix(Thing __instance, ref float __result)
        {
            if (__instance?.def == null)
            {
                return;
            }

            if (HelodMarketState.Current?.TryGetMarketValue(__instance.def, out float marketValue) == true)
            {
                __result = marketValue;
            }
        }
    }

    [HarmonyPatch(typeof(StatExtension), "GetStatValue")]
    public static class Patch_StatExtension_GetStatValue_HelodMarket
    {
        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (thing?.def == null || stat != StatDefOf.MarketValue)
            {
                return;
            }

            if (HelodMarketState.Current?.TryGetMarketValue(thing.def, out float marketValue) == true)
            {
                __result = marketValue;
            }
        }
    }
}
