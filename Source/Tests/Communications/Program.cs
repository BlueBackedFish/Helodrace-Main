using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Helodrace;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        HamSchedule();
        HamRumors();
        CompanyRequests();
        HamRouting();
        HamDialogue();
        Definitions();
        Console.WriteLine("PASS: " + checks + " communications checks.");
    }

    private static void HamRouting()
    {
        Check(HamRadioRoutingRules.IsHamFaction("HD_HelodCivilLowFaction"), "LOW uses HAM instead of vanilla faction services");
        Check(HamRadioRoutingRules.IsHamFaction("HD_HelodCivilHighFaction"), "HIGH uses HAM instead of vanilla faction services");
        foreach (string defName in new[] { null, "", "OutlanderCivil", "OutlanderRough", "TribeCivil", "Empire", "Pirate", "Mechanoid",
            "HD_HelodPlayerColony", "HD_HelodCivilLowFactionOther", "CommsConsole" })
            Check(!HamRadioRoutingRules.IsHamFaction(defName), "Only the exact LOW/HIGH faction definitions use HAM: " + defName);
        Check(!File.Exists("Patches/HamRadio.xml"), "The global console class, label and boss-call XML replacement is removed");
        Check(!File.Exists("Languages/Korean (한국어)/DefInjected/ThingDef/HamRadio.xml"), "The vanilla console keeps its normal translated label and description");
        var english = XDocument.Load("Languages/English/Keyed/HamRadio.xml").Root.Elements().ToList();
        var korean = XDocument.Load("Languages/Korean (한국어)/Keyed/HamRadio.xml").Root.Elements().ToList();
        Check(english.Select(e => e.Name).SequenceEqual(korean.Select(e => e.Name)), "HAM translations have matching keys");
        Check(english.Single(e => e.Name == "HD_HAM_Greeting").Value.Contains("{0}") && korean.Single(e => e.Name == "HD_HAM_Greeting").Value.Contains("{0}"),
            "The HAM greeting identifies the selected faction");
        Check(!File.Exists("Defs/WildWest/Jobs_HamRadio.xml"), "HAM retains the vanilla comms job instead of using a separate job definition");
        Check(!File.Exists("Languages/Korean (한국어)/DefInjected/JobDef/Jobs_HamRadio.xml"), "The obsolete separate HAM job translation is removed");
        foreach (Match key in Regex.Matches(File.ReadAllText("Source/Helodrace/WildWest/HamRadioDialog.cs")
            + File.ReadAllText("Source/Helodrace/WildWest/HamRadio.cs"), "\"(HD_HAM_[A-Za-z]+)\""))
            Check(english.Any(e => e.Name.LocalName == key.Groups[1].Value), "HAM dialogue and mission replies are translated");
    }

    private static void HamDialogue()
    {
        int conversations = 0;
        var root = HamRadioDialog.Create("Test Helod faction", () => "Chatter " + ++conversations);
        Check(root.text.Contains("Test Helod faction"), "The vanilla negotiation window receives a faction-specific HAM greeting");
        Check(root.options.Count == 2 && root.options[0].text == "HD_HAM_Chat" && root.options[1].resolveTree,
            "Root offers only chatter and disconnect, with no mission inquiry");
        Check(conversations == 0, "Opening the dialogue does not consume or reveal a prepared rumor");
        var chatter = root.options[0].linkLateBind();
        Check(chatter.text == "Chatter 1" && conversations == 1, "Choosing chatter displays its normal or rumor reply inside the window");
        Check(chatter.options.Count == 2 && chatter.options[0].link == root && !chatter.options[0].resolveTree,
            "Chatter returns to the same root inside the existing dialogue window");
        var repeated = root.options[0].linkLateBind();
        Check(repeated.text == "Chatter 2" && conversations == 2, "Returning to the root allows more ordinary chatter");
        Check(root.options[1].resolveTree && chatter.options[1].resolveTree && repeated.options[1].resolveTree,
            "Every dialogue stage can disconnect through the vanilla tree close behavior");
        Check(root.options[1].link == null && root.options[1].linkLateBind == null, "Disconnect never triggers a new reply or rumor");
        Check(!ReferenceEquals(chatter, repeated) && !ReferenceEquals(chatter.options[1], root.options[1]),
            "Response nodes and close options have independent dialogue ownership");
    }

    private static void HamSchedule()
    {
        const int world = 1234, map = 1;
        int yes = Enumerable.Range(0, 200).First(d => HamRadioSchedule.IsRumorDay(d, world, map));
        int no = Enumerable.Range(0, 200).First(d => !HamRadioSchedule.IsRumorDay(d, world, map));
        var schedule = new HamRadioSchedule();
        Check(!schedule.TryConsumeRumor(yes), "An unprepared day cannot generate a rumor");
        schedule.PrepareDay(yes, world, map);
        Check(schedule.PreparedDay == yes && schedule.HasRumor && !schedule.RumorConsumed, "The day's rumor is decided before chatting");
        Check(schedule.TryConsumeRumor(yes) && !schedule.TryConsumeRumor(yes), "A prepared rumor can be consumed once only");
        schedule.PrepareDay(yes, world + 99, map + 99);
        Check(!schedule.TryConsumeRumor(yes), "Preparing the same day again cannot reset consumed state");
        var absent = new HamRadioSchedule(); absent.PrepareDay(no, world, map);
        Check(!absent.HasRumor && !absent.TryConsumeRumor(no), "A no-rumor day never yields a mission");
        absent.PrepareDay(no, 9876, 77);
        Check(!absent.HasRumor, "Changing inputs after preparation cannot reroll the current day");
        var restored = new HamRadioSchedule { PreparedDay = yes, HasRumor = true, RumorConsumed = true };
        restored.PrepareDay(yes, world, map);
        Check(!restored.TryConsumeRumor(yes), "Persisted consumed state prevents same-day duplicates");
        Check(!restored.TryConsumeRumor(yes - 1), "A rumor cannot be consumed under the wrong date");
        restored.PrepareDay(yes - 1, world, map);
        Check(restored.PreparedDay == yes && restored.RumorConsumed, "Clock rollback does not reset the active day");
        restored.PrepareDay(yes + 1, world, map);
        Check(restored.PreparedDay == yes + 1 && !restored.RumorConsumed, "The next day gets its own unconsumed result");
        int count = Enumerable.Range(0, 10000).Count(d => HamRadioSchedule.IsRumorDay(d, world, map));
        Check(count > 1300 && count < 1700, "The fixed calendar retains the approved 15-percent daily chance");
        Check(Enumerable.Range(0, 200).Any(d => HamRadioSchedule.IsRumorDay(d, world, map) != HamRadioSchedule.IsRumorDay(d, world + 1, map)),
            "Different worlds do not share one universal rumor calendar");
        Check(Enumerable.Range(0, 200).Any(d => HamRadioSchedule.IsRumorDay(d, world, map) != HamRadioSchedule.IsRumorDay(d, world, map + 1)),
            "Different settlements have independent calendars");
    }

    private static void HamRumors()
    {
        const int seed = 1234, mapId = 1;
        int yes = Enumerable.Range(0, 200).First(d => HamRadioSchedule.IsRumorDay(d, seed, mapId));
        int no = Enumerable.Range(0, 200).First(d => !HamRadioSchedule.IsRumorDay(d, seed, mapId));
        Action<int> clock = ticks => { Verse.Find.TickManager.TicksAbs = ticks; Verse.Find.TickManager.TicksGame = ticks; };
        var map = new Verse.Map { uniqueID = mapId };
        var pawn = new Verse.Pawn { Map = map };
        var low = new RimWorld.Faction { def = new RimWorld.FactionDef { defName = "HD_HelodCivilLowFaction" } };
        var high = new RimWorld.Faction { def = new RimWorld.FactionDef { defName = "HD_HelodCivilHighFaction" } };
        var worker = RimWorld.IncidentDefOf.GiveQuest_Random.Worker;
        Verse.Find.World.info.Seed = seed; Verse.Find.WorldGrid.Longitude = 0;
        clock(yes * 60000); worker.Calls = 0; worker.Success = true;
        var component = new MapComponent_HamRadio(map); component.FinalizeInit();
        Verse.Scribe_Values.Loading = false; Verse.Scribe_Values.Stored.Clear(); component.ExposeData();
        Check((int)Verse.Scribe_Values.Stored["hamRumorDay"] == yes && (bool)Verse.Scribe_Values.Stored["hamHasRumor"]
            && !(bool)Verse.Scribe_Values.Stored["hamRumorConsumed"] && worker.Calls == 0,
            "Map initialization prepares and saves today's result before any contact or quest generation");
        var pendingSave = new System.Collections.Generic.Dictionary<string, object>(Verse.Scribe_Values.Stored);
        Check(component.Chat(pawn, low).Contains("HD_HAM_Rumor") && worker.Calls == 1, "A positive day reveals one mission as a rumor inside normal chatter");
        Check(component.Chat(pawn, high) == "Ordinary chatter" && worker.Calls == 1, "Switching LOW/HIGH contacts cannot reveal another rumor");
        for (int i = 0; i < 100; i++) component.Chat(pawn, low);
        Check(worker.Calls == 1, "Repeated chatter cannot produce additional quest attempts");
        component.ExposeData();
        var loaded = new MapComponent_HamRadio(map);
        Verse.Scribe_Values.Loading = true; loaded.ExposeData(); Verse.Scribe_Values.Loading = false; loaded.FinalizeInit();
        Check(loaded.Chat(pawn, high) == "Ordinary chatter" && worker.Calls == 1, "Save/load preserves rumor consumption across faction contacts");

        Verse.Scribe_Values.Stored = pendingSave;
        var pending = new MapComponent_HamRadio(map);
        Verse.Scribe_Values.Loading = true; pending.ExposeData(); Verse.Scribe_Values.Loading = false; pending.FinalizeInit();
        worker.Success = false;
        Check(pending.Chat(pawn, low) == "Ordinary chatter" && worker.Calls == 2, "An unavailable quest keeps the response as ordinary chatter");
        worker.Success = true;
        Check(pending.Chat(pawn, high) == "Ordinary chatter" && worker.Calls == 2, "Failed quest generation is consumed and cannot be retried by clicking");

        clock(no * 60000); var empty = new MapComponent_HamRadio(map); empty.FinalizeInit();
        Check(Enumerable.Range(0, 100).All(_ => empty.Chat(pawn, low) == "Ordinary chatter"), "Repeated chatter on no-rumor days always stays ordinary");
        Check(worker.Calls == 2, "No-rumor days never attempt quest generation");
        clock(yes * 60000); var guarded = new MapComponent_HamRadio(map); guarded.FinalizeInit();
        Check(guarded.Chat(pawn, new RimWorld.Faction()) == "Ordinary chatter" && worker.Calls == 2, "Other factions cannot consume a prepared rumor");
        Check(guarded.Chat(pawn, low).Contains("HD_HAM_Rumor") && worker.Calls == 3, "An excluded contact leaves the valid LOW/HIGH rumor available");

        Verse.Find.WorldGrid.Longitude = 90;
        clock(10 * 60000 - 15001); var midnight = new MapComponent_HamRadio(map); midnight.FinalizeInit(); midnight.ExposeData();
        Check((int)Verse.Scribe_Values.Stored["hamRumorDay"] == 9, "Daily planning follows the settlement's local day");
        clock(10 * 60000 - 15000); midnight.MapComponentTick(); midnight.ExposeData();
        Check((int)Verse.Scribe_Values.Stored["hamRumorDay"] == 10
            && (bool)Verse.Scribe_Values.Stored["hamHasRumor"] == HamRadioSchedule.IsRumorDay(10, seed, mapId),
            "Local midnight prepares the next result automatically without chatting");
        Verse.Find.WorldGrid.Longitude = 0;
    }

    private static void Definitions()
    {
        foreach (var entry in new[]
        {
            ("Defs/Finance/TraderKinds_Finance.xml", TelegraphCompanies.FinancialTrader, TelegraphCompany.NationalIndustrialCredit),
            ("Defs/Finance/TraderKinds_Commercial.xml", TelegraphCompanies.CommercialTrader, TelegraphCompany.CentralHardwareSupply)
        })
        {
            var def = XDocument.Load(entry.Item1).Root.Elements("TraderKindDef").Single(d => (string)d.Element("defName") == entry.Item2);
            Check((string)def.Element("label") == TelegraphCompanies.Name(entry.Item3), "Arriving caravan label matches its company tab");
        }
        var kinds = XDocument.Load("Defs/Helod/Pawns/PawnKinds_GreatWar.xml").Root.Elements("PawnKindDef").ToList();
        Check(kinds.Any(d => (string)d.Element("defName") == TelegraphCompanies.EliteGuard), "Elite guard definition exists");
        Check(kinds.Any(d => (string)d.Element("defName") == TelegraphCompanies.EliteCaptain), "Elite captain definition exists");
        var english = XDocument.Load("Languages/English/Keyed/TelegraphCompanies.xml").Root.Elements().ToList();
        var korean = XDocument.Load("Languages/Korean (한국어)/Keyed/TelegraphCompanies.xml").Root.Elements().ToList();
        Check(english.Select(e => e.Name).OrderBy(n => n.ToString()).SequenceEqual(korean.Select(e => e.Name).OrderBy(n => n.ToString())),
            "English and Korean company text has matching keys");
        Check(english.Select(e => e.Name).Distinct().Count() == english.Count, "No duplicate company translation keys");

        var giftEnglish = XDocument.Load("Languages/English/Keyed/FinancialGoodwill.xml").Root.Elements().ToList();
        var giftKorean = XDocument.Load("Languages/Korean (한국어)/Keyed/FinancialGoodwill.xml").Root.Elements().ToList();
        Check(giftEnglish.Select(e => e.Name).OrderBy(n => n.ToString()).SequenceEqual(giftKorean.Select(e => e.Name).OrderBy(n => n.ToString())),
            "English and Korean financial gift text has matching keys");
        Check(giftEnglish.Select(e => e.Name).Distinct().Count() == giftEnglish.Count, "No duplicate financial gift translation keys");
        foreach (Match key in Regex.Matches(File.ReadAllText("Source/Helodrace/Economy/FinancialGiftPatches.cs"), "\"(HD_FinancialGift_[A-Za-z]+)\""))
            Check(giftEnglish.Any(e => e.Name.LocalName == key.Groups[1].Value), "Every financial gift UI and message key is translated");
        foreach (var entry in giftEnglish)
        {
            var translated = giftKorean.Single(e => e.Name == entry.Name);
            var pattern = @"\{\d+\}";
            Check(Regex.Matches(entry.Value, pattern).Select(m => m.Value).SequenceEqual(Regex.Matches(translated.Value, pattern).Select(m => m.Value)),
                "Financial gift text preserves translation arguments");
        }
        var financialSituation = XDocument.Load("Defs/Finance/GoodwillSituations_Financial.xml").Root.Element("GoodwillSituationDef");
        Check((string)financialSituation.Element("workerClass") == "Helodrace.Economy.GoodwillSituationWorker_Financial",
            "Natural goodwill definition uses the financial ledger worker");
        var situationTranslation = XDocument.Load("Languages/Korean (한국어)/DefInjected/GoodwillSituationDef/FinancialGoodwill.xml");
        Check(situationTranslation.Root.Element((string)financialSituation.Element("defName") + ".label") != null,
            "The financial natural-goodwill situation has a Korean label");
        Check(!File.Exists("Source/Helodrace/WildWest/TelegraphFactionComms.cs"), "The obsolete generic faction dialogue has been removed");
    }

    private static void CompanyRequests()
    {
        Check(TelegraphCompanies.Name(TelegraphCompany.NationalIndustrialCredit) == "National Industrial Credit Corporation", "Financial company name");
        Check(TelegraphCompanies.Name(TelegraphCompany.CentralHardwareSupply) == "Central Hardware & Supply Co.", "Commercial company name");
        Check(TelegraphCompanies.Name(TelegraphCompany.PioneerOverseasService) == "Pioneer Overseas Service Company", "Contract company name matches its contract");
        Check(TelegraphCompanies.Name(TelegraphCompany.HarringtonProtective) == "Harrington Protective Services", "Security company name");
        Check(TelegraphCompanies.FinancialTrader != TelegraphCompanies.CommercialTrader, "Companies use separate exact trader kinds");
        Check(TelegraphCompanies.HarringtonGuardCount == 6 && TelegraphCompanies.HarringtonCost == 500
            && TelegraphCompanies.HarringtonDurationTicks == 60000, "Approved Harrington terms");
        Check(TelegraphCompanies.EliteGuard == "HD_GW_HelodEliteCaravanGuard"
            && TelegraphCompanies.EliteCaptain == "HD_GW_HelodEliteCaravanGuardCaptain", "Harrington uses elite caravan guards");
        var book = new CompanyDispatchBook();
        Check(book.TrySchedule(0, true, false, 60000, 60000), "Financial caravan can be requested at game start");
        Check(book.CanRequest(0, false, true), "Financial request does not block the commercial company");
        Check(book.TrySchedule(0, false, true, 60000, 120000), "Both companies can be requested at the same tick");
        Check(book.Financial.ArrivalTick == 60000 && book.Commercial.ArrivalTick == 120000, "Each caravan keeps its own arrival schedule");
        Check(!book.TrySchedule(1, true, true, 60000, 60000), "In-transit caravans cannot be requested twice");
        book.Financial.Complete();
        Check(!book.Financial.CanRequest(239999), "Arrival does not skip the four-day cooldown");
        Check(book.Financial.CanRequest(240000), "Cooldown ends at its exact boundary");
        Check(book.Commercial.Pending && book.Commercial.ArrivalTick == 120000, "Completing one caravan never clears the other");
        int before = book.Financial.LastRequestTick;
        Check(!book.TrySchedule(240000, true, true, 60000, 60000), "A blocked member rejects a combined request");
        Check(book.Financial.LastRequestTick == before && !book.Financial.Pending, "Combined rejection does not partially schedule or reset cooldowns");
        book.Commercial.Complete();
        Check(book.TrySchedule(240000, true, true, 60000, 120000), "Both companies can be requested atomically after cooldown");
        Check(book.Financial.Retry(300000) && book.Financial.ArrivalTick == 302500, "Arrival failure retries after one hour");
        Check(book.Commercial.ArrivalTick == 360000, "Retrying one company does not delay the other");
        Check(!book.Financial.Retry(480000) && !book.Financial.Pending, "Impossible arrival expires instead of retrying forever");
        var loaded = new CompanyDispatchSchedule
        {
            LastRequestTick = book.Commercial.LastRequestTick,
            ArrivalTick = book.Commercial.ArrivalTick,
            ExpiryTick = book.Commercial.ExpiryTick
        };
        Check(loaded.Pending && !loaded.CanRequest(360000), "Persisted company state retains a dispatched caravan");
        loaded.Complete();
        Check(!loaded.CanRequest(479999) && loaded.CanRequest(480000), "Persisted cooldown retains its original deadline");
        var invalid = new CompanyDispatchBook();
        Check(!invalid.TrySchedule(0, false, false, 60000, 60000), "Empty batch cannot charge or schedule a request");
        Check(!invalid.TrySchedule(0, true, true, 60000, 59999)
            && !invalid.Financial.Pending && !invalid.Commercial.Pending, "Invalid travel time rejects the entire batch");
    }
}
