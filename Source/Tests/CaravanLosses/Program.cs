using System;
using System.Linq;
using System.Xml.Linq;
using Helodrace.Economy;
using RimWorld;
using Verse;
using Verse.AI.Group;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static DamageInfo PlayerHit => new DamageInfo { Amount = 10, Instigator = new Thing { Faction = Faction.OfPlayer }, Def = new DamageDef() };
    private static Pawn Merchant(Faction faction = null) => new Pawn { Faction = faction ?? new Faction(), MarketValue = 1000, Lord = new Lord() };
    private static GameComponent_CaravanLosses NewLedger()
    {
        var game = new Game(); Current.Game = game;
        var ledger = new GameComponent_CaravanLosses(game); game.Ledger = ledger;
        return ledger;
    }
    private static void Hit(Pawn pawn, float afterHealth)
    {
        Patch_CaravanLossDamage.Prefix(pawn, PlayerHit, out var frame);
        pawn.health.summaryHealth.SummaryHealthPercent = afterHealth;
        Patch_CaravanLossDamage.Postfix(frame); Patch_CaravanLossDamage.Finalizer(frame);
    }
    private static void Main()
    {
        var ledger = NewLedger(); var pawn = Merchant(); var faction = pawn.Faction;
        Check(CaravanLossFrame.Begin(pawn, new DamageInfo { Amount = 10, Instigator = new Thing { Faction = new Faction() }, Def = new DamageDef() }) == null,
            "Third-party attacks do not create claims");
        Check(CaravanLossFrame.Begin(pawn, new DamageInfo { Amount = 0, Instigator = new Thing { Faction = Faction.OfPlayer }, Def = new DamageDef() }) == null,
            "Zero-damage events are ignored");
        Check(CaravanLossFrame.Begin(pawn, new DamageInfo { Amount = 10, Instigator = new Thing { Faction = Faction.OfPlayer }, Def = new DamageDef { Violent = false } }) == null,
            "Nonviolent changes are not attacks");
        var stranger = Merchant(new Faction { DefName = "OutlanderCivil" });
        Check(CaravanLossFrame.Begin(stranger, PlayerHit) == null, "Unrelated factions are not added to the Helod ledger");
        var soldier = Merchant(); soldier.Lord.LordJob = new object();
        Check(CaravanLossFrame.Begin(soldier, PlayerHit) == null, "A non-caravan Helod soldier is not a merchant loss");
        Check(CaravanLossFrame.Begin(new Thing() as Pawn, PlayerHit) == null, "Unrelated damaged objects are ignored");

        var stock = new Thing { stackCount = 100, MarketValue = 2.5f, LabelNoCount = "Fuel", def = new ThingDef { defName = "Fuel" } };
        pawn.inventory.innerContainer.Add(stock);
        Patch_CaravanLossDamage.Prefix(pawn, PlayerHit, out var damageFrame);
        int callbacks = 0;
        var watch = damageFrame.WatchDrop(stock, (_, count) => callbacks += count);
        watch(new Thing { stackCount = 500 }, 40);
        watch(new Thing { stackCount = 560 }, 60);
        watch(stock, 100); // A duplicated callback must not count the source stack again.
        damageFrame.WatchDrop(new Thing(), null)?.Invoke(stock, 200);
        pawn.health.summaryHealth.SummaryHealthPercent = .8f;
        var wound = new Hediff_Injury { Severity = 5, Bleeding = true }; pawn.health.hediffSet.hediffs.Add(wound);
        Patch_CaravanLossDamage.Postfix(damageFrame); Patch_CaravanLossDamage.Postfix(damageFrame);
        Check(callbacks == 200 && ledger.Total(faction, CaravanLossKind.DroppedItem) == 250,
            "Merged drops use placed quantities, cap duplicate callbacks, and preserve the original callback");
        Check(ledger.Total(faction, CaravanLossKind.Injury) == 200 && ledger.Entries(faction).Count() == 3,
            "One attack records actual drops and a health-proportional injury once");
        Check(CaravanLossFrame.Current == null && faction.Goodwill == -80, "Loss accounting neither leaks its frame nor changes diplomatic goodwill");
        var victim = ledger.LatestVictim(pawn.Id);
        Check(victim.InitialValue == 1000 && victim.PlayerWounds.Contains(wound.Id), "Pre-attack pawn value and player injury attribution are retained");
        Check(ledger.Entries(faction).All(e => e.CaravanKey == "Lord_1" && e.PawnName == pawn.LabelShortCap && e.MapName == "Colony"),
            "Each entry keeps the caravan, pawn and settlement context");

        // A third-party wound between player attacks must not be billed retroactively.
        pawn.health.summaryHealth.SummaryHealthPercent = .6f;
        pawn.MarketValue = 100;
        Hit(pawn, .5f);
        Check(ledger.Total(faction, CaravanLossKind.Injury) == 300 && victim.InitialValue == 1000,
            "Later player attacks only record their own health change against the original pawn valuation");
        ledger = Reload(ledger); victim = ledger.LatestVictim(pawn.Id);
        Check(ledger.Total(faction) == 550 && victim.InjuryValue == 300 && victim.PlayerWounds.Contains(wound.Id),
            "Entries, category totals, pawn valuation and wound attribution survive save/load");
        Check(ledger.GetOrCreate(faction, "Lord_1", "Colony", 1, pawn.Id, pawn.LabelShortCap, 10, .5f) == victim,
            "Reloading a visit does not reset its valuation or duplicate a pawn account");
        pawn.Lord = null; // Downed pawns may lose their lord before a finishing blow.
        Patch_CaravanLossDamage.Prefix(pawn, PlayerHit, out var fatalFrame);
        Check(fatalFrame != null, "A downed caravan pawn remains associated with its original visit");
        Patch_CaravanLossDeath.Prefix(pawn, PlayerHit, wound, out var nestedDeath);
        Check(nestedDeath == null, "A death inside TakeDamage reuses the outer accounting frame");
        pawn.Dead = true;
        Patch_CaravanLossDeath.Postfix(nestedDeath); Patch_CaravanLossDamage.Postfix(fatalFrame);
        Check(ledger.Total(faction, CaravanLossKind.Death) == 700 && ledger.Total(faction) == 1250,
            "Death records only the remaining pawn value, keeping items separate and avoiding double-counted injuries");
        Check(!ledger.RecordDeath(victim) && ledger.RecordInjury(victim, 1, 0, 300) == 0 && ledger.Total(faction) == 1250,
            "Repeated death or injury notifications cannot charge a dead pawn again");
        ledger = Reload(ledger); victim = ledger.LatestVictim(pawn.Id);
        Check(victim.Dead && !ledger.RecordDeath(victim) && ledger.Total(faction) == 1250, "Death deduplication survives saving");

        DelayedAndNestedCases();
        DropHookAndLanguages();
        Console.WriteLine("PASS: " + checks + " caravan loss checks.");
    }

    private static void DelayedAndNestedCases()
    {
        var ledger = NewLedger(); var pawn = Merchant();
        Patch_CaravanLossDamage.Prefix(pawn, PlayerHit, out var first);
        pawn.health.summaryHealth.SummaryHealthPercent = .9f;
        Patch_CaravanLossDamage.Prefix(pawn, PlayerHit, out var inner);
        pawn.health.summaryHealth.SummaryHealthPercent = .8f;
        Patch_CaravanLossDamage.Postfix(inner);
        Check(CaravanLossFrame.Current == first, "Nested damage restores the outer frame");
        var wound = new Hediff_Injury { Severity = 2, Bleeding = true }; pawn.health.hediffSet.hediffs.Add(wound);
        Patch_CaravanLossDamage.Postfix(first);
        Check(ledger.Total(pawn.Faction) == 200 && CaravanLossFrame.Current == null, "Nested player damage is not counted twice");
        var victim = ledger.LatestVictim(pawn.Id);
        double before = ledger.Total(pawn.Faction);
        Patch_CaravanLossDamage.Prefix(pawn, PlayerHit, out var failed);
        Patch_CaravanLossDamage.Finalizer(failed);
        Check(CaravanLossFrame.Current == null && ledger.Total(pawn.Faction) == before, "An exception releases tracking without inventing an injury");

        var bloodLoss = new Hediff { def = HediffDefOf.BloodLoss };
        Patch_CaravanLossDowned.Prefix(null, bloodLoss, pawn, out var downedFrame);
        Check(downedFrame != null, "Downing from a recorded bleeding player wound is attributed after the attack");
        Patch_CaravanLossDowned.Postfix(downedFrame);
        Check(ledger.Total(pawn.Faction) == before, "Downing itself is not an extra pawn injury charge");
        Patch_CaravanLossDeath.Prefix(pawn, new DamageInfo { Amount = 10, Def = new DamageDef(), Instigator = new Thing { Faction = new Faction() } }, bloodLoss, out var thirdParty);
        Check(thirdParty == null, "A third-party finishing blow is not attributed to a previous player attack");
        Check(CaravanLossFrame.Begin(pawn, null, new Hediff()) == null, "Unrelated illness or death is not attributed to old wounds");
        wound.Bleeding = false;
        Check(CaravanLossFrame.Begin(pawn, null, bloodLoss) == null, "Healed player wounds do not claim later blood-loss deaths");
        wound.Bleeding = true;
        ledger = Reload(ledger);
        Patch_CaravanLossDeath.Prefix(pawn, null, bloodLoss, out var bleedDeath);
        Check(bleedDeath != null, "Saved player wounds can attribute a later bleeding death");
        pawn.Dead = true; Patch_CaravanLossDeath.Postfix(bleedDeath);
        Check(ledger.Total(pawn.Faction) == 1000, "Delayed bleeding death records the remaining value exactly once");

        var visitor = Merchant(); Hit(visitor, .8f);
        ledger.MarkDeparture(visitor.Id); visitor.Lord = null;
        ledger = Reload(ledger);
        Check(CaravanLossFrame.Begin(visitor, PlayerHit) == null, "A departed visitor cannot charge its old caravan account");
        visitor.Lord = new Lord { loadID = 2 };
        Hit(visitor, .7f);
        Check(ledger.LatestVictim(visitor.Id).CaravanKey == "Lord_2", "A new caravan visit creates a distinct historical account");
        var high = Merchant(new Faction { Name = "HIGH", DefName = "HD_HelodCivilHighFaction" });
        Hit(high, .9f);
        Check(ledger.Total(high.Faction) == 100, "HIGH caravans have separate loss records");
        Check(ledger.Entries(null).Count() > ledger.Entries(high.Faction).Count()
            && ledger.RecordedFactions.Contains(high.Faction), "The archive can browse all factions and each faction separately");

        var capped = ledger.GetOrCreate(high.Faction, "Cap", "Colony", 1, "CapPawn", "Guard", 100, 1);
        for (int i = 0; i < 20; i++) ledger.RecordInjury(capped, 1, .8f, capped.InjuryValue);
        ledger.RecordDeath(capped);
        Check(ledger.Entries(high.Faction).Where(e => e.PawnId == "CapPawn").Sum(e => e.Value) == 100,
            "Repeated injury and healing cannot make pawn losses exceed its pre-attack value");
    }

    private static void DropHookAndLanguages()
    {
        var methods = Patch_CaravanLossDrops.TargetMethods().ToList();
        Check(methods.Count == 2, "Both full-stack and partial-stack native drop paths are targeted");
        var ledger = NewLedger(); var pawn = Merchant(); var item = new Thing { stackCount = 10, MarketValue = 5 };
        pawn.equipment.AllEquipmentListForReading.Add(item);
        using (var frame = CaravanLossFrame.Begin(pawn, PlayerHit))
        {
            foreach (var method in methods)
            {
                var args = new object[8]; args[0] = item;
                int callback = Array.FindIndex(method.GetParameters(), p => p.ParameterType == typeof(Action<Thing, int>));
                Patch_CaravanLossDrops.Prefix(args, method);
                ((Action<Thing, int>)args[callback])(new Thing { stackCount = 2000 }, 3);
            }
            frame.Complete();
        }
        Check(ledger.Total(pawn.Faction) == 30 && ledger.Entries(pawn.Faction).Sum(e => e.Count) == 6,
            "Drop hooks record the callback's actual quantities, not the destination merged stack size");
        var english = XDocument.Load("Languages/English/Keyed/CaravanLosses.xml").Root.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);
        var korean = XDocument.Load("Languages/Korean (한국어)/Keyed/CaravanLosses.xml").Root.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);
        Check(english.Keys.OrderBy(k => k).SequenceEqual(korean.Keys.OrderBy(k => k)), "Loss ledger translations contain matching keys");
        foreach (string key in english.Keys)
            Check(System.Text.RegularExpressions.Regex.Matches(english[key], @"\{\d+\}").Select(m => m.Value).OrderBy(v => v)
                .SequenceEqual(System.Text.RegularExpressions.Regex.Matches(korean[key], @"\{\d+\}").Select(m => m.Value).OrderBy(v => v)),
                "Loss ledger translation placeholders match: " + key);
    }

    private static GameComponent_CaravanLosses Reload(GameComponent_CaravanLosses ledger)
    {
        Scribe.Data.Clear(); Scribe.mode = LoadSaveMode.Saving; ledger.ExposeData();
        var loaded = new GameComponent_CaravanLosses(Current.Game);
        Scribe.mode = LoadSaveMode.LoadingVars; loaded.ExposeData();
        Scribe.mode = LoadSaveMode.PostLoadInit; loaded.ExposeData();
        Scribe.mode = LoadSaveMode.Saving; Current.Game.Ledger = loaded;
        return loaded;
    }
}
