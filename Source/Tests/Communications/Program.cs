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
        var ham = new HamRadioSchedule();
        Check(ham.CanCheck(0), "The first day can receive a mission");
        Check(ham.CheckDay(0, 0.14999f), "A roll below 15 percent receives mission traffic");
        Check(!ham.CheckDay(0, 0f), "Repeated use cannot receive another mission that day");
        Check(!ham.CheckDay(1, 0.15f), "A roll at the threshold is ordinary chatter");
        Check(!ham.CheckDay(1, 0f), "A failed daily roll cannot be rerolled on another console");
        Check(ham.CheckDay(2, 0f), "A later day can receive another mission");
        Check(!ham.CheckDay(1, 0f), "Clock rollback does not reopen a checked day");
        var restored = new HamRadioSchedule { LastCheckedDay = ham.LastCheckedDay };
        Check(!restored.CanCheck(2), "Persisted day prevents a same-day retry after loading");
        Check(restored.CanCheck(3), "Persisted state permits the next day");
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
        int conversations = 0, missionChecks = 0;
        var root = HamRadioDialog.Create("Test Helod faction", () => "Chatter " + ++conversations, () => "Mission reply " + ++missionChecks);
        Check(root.text.Contains("Test Helod faction"), "The vanilla negotiation window receives a faction-specific HAM greeting");
        Check(root.options.Count == 3 && root.options[0].text == "HD_HAM_Chat" && root.options[1].text == "HD_HAM_AskMission",
            "Root offers chatter, mission inquiry and disconnect instead of vanilla faction services");
        Check(conversations == 0 && missionChecks == 0, "Opening the dialogue cannot silently consume the daily mission check");
        var chatter = root.options[0].linkLateBind();
        Check(chatter.text == "Chatter 1" && conversations == 1 && missionChecks == 0, "Choosing chatter displays a reply without rolling a mission");
        Check(chatter.options.Count == 2 && chatter.options[0].link == root && !chatter.options[0].resolveTree,
            "Chatter returns to the same root inside the existing dialogue window");
        var mission = root.options[1].linkLateBind();
        Check(mission.text == "Mission reply 1" && missionChecks == 1 && conversations == 1, "Mission inquiry executes once when selected and shows its result in the dialogue");
        Check(mission.options.Count == 2 && mission.options[0].link == root, "Mission replies can return to the root without reopening comms");
        Check(root.options[2].resolveTree && chatter.options[1].resolveTree && mission.options[1].resolveTree,
            "Every dialogue stage can disconnect through the vanilla tree close behavior");
        Check(root.options[2].link == null && root.options[2].linkLateBind == null, "Disconnect never triggers a new reply or mission check");
        Check(!ReferenceEquals(chatter, mission) && !ReferenceEquals(chatter.options[1], root.options[2]),
            "Response nodes and close options have independent dialogue ownership");
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
