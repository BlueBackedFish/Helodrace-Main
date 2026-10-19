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
        Definitions();
        Console.WriteLine("PASS: " + checks + " communications checks.");
    }

    private static void HamRouting()
    {
        Check(HamRadioRoutingRules.IsHamConsole("CommsConsole", false), "A base-class vanilla console still routes to HAM");
        Check(HamRadioRoutingRules.IsHamConsole("CommsConsole", true), "The patched HAM subclass routes to HAM");
        Check(HamRadioRoutingRules.IsHamConsole("CustomHam", true), "A HAM-derived console remains HAM");
        Check(!HamRadioRoutingRules.IsHamConsole("OtherConsole", false), "Unrelated console types retain their own behavior");
        Check(HamRadioRoutingRules.BlocksVanillaFaction("HD_HelodCivilLowFaction"), "LOW cannot use the vanilla faction dialogue");
        Check(!HamRadioRoutingRules.BlocksVanillaFaction("HD_HelodCivilHighFaction"), "The LOW restriction does not become a global faction restriction");
        Check(!HamRadioRoutingRules.BlocksVanillaFaction(null), "Unspecified factions do not match LOW");
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
