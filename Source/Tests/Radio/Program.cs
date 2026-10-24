using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Helodrace;
using RimWorld;
using Verse;
using Verse.AI;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static Faction Channel(string name) => new Faction { Name = name, def = new FactionDef { defName = name } };

    private static void Main()
    {
        var low = Channel("HD_HelodCivilLowFaction");
        var high = Channel("HD_HelodCivilHighFaction");
        low.PlayerGoodwill = -80;
        low.PlayerRelationKind = FactionRelationKind.Hostile;
        var other = Channel("OutlanderCivil");
        var defeated = Channel("HD_HelodCivilLowFaction"); defeated.defeated = true;
        Find.FactionManager.AllFactionsVisibleInViewOrder.AddRange(new[] { low, high, other, defeated, Faction.OfPlayer });
        var radio = new Building_Radio();
        var pawn = new Pawn();
        Check(radio.CanUseNow, "Power alone makes a newly constructed radio usable, without installation, cells, leader or business-hour checks");
        Check(Building_Radio.Channels.SequenceEqual(new[] { low, high }), "Only surviving LOW/HIGH HAM channels appear, including a hostile faction");
        Check(!Building_Radio.IsChannelAvailable(null), "Missing channels are rejected");
        var options = radio.GetFloatMenuOptions(pawn).ToList();
        Check(options.Count == 3 && options.All(o => o.action != null), "Listening and both HAM channels are selectable");
        Check(ReferenceEquals(options[1].Icon, low.def.FactionIcon) && ReferenceEquals(options[1].Color, low.Color)
            && options[1].Label.Contains("-80") && options[1].Label.Contains("Hostile"), "Channel menus retain native faction icons, relation and signed goodwill");
        options[1].action();
        Check(pawn.jobs.Last.def.defName == "HD_ChatOnRadio" && pawn.jobs.Last.commTarget == low
            && pawn.jobs.Last.targetA.Thing == radio, "Selecting a channel orders the correct radio and faction");
        options[0].action();
        Check(pawn.jobs.Last.def.defName == "HD_ListenRadio" && pawn.jobs.Last.commTarget == null, "Broadcast listening needs no faction contact");

        radio.Power.PowerOn = false;
        Check(!radio.CanUseNow && radio.GetFloatMenuOptions(pawn).All(o => o.action == null), "Power loss disables all manual radio use");
        radio.Power.PowerOn = true; radio.Map.gameConditionManager.Disabled = true;
        Check(!radio.CanUseNow, "Solar flares disable the electrical radio");
        radio.Map.gameConditionManager.Disabled = false; radio.Power = null;
        Check(!radio.CanUseNow, "A missing power component cannot act as a free radio");
        radio.Power = new CompPowerTrader(); radio.Spawned = false;
        Check(!radio.CanUseNow, "Unspawned radios are unavailable");
        radio.Spawned = true;
        pawn.health.capacities.Talking = false;
        options = radio.GetFloatMenuOptions(pawn).ToList();
        Check(options[0].action != null && options.Skip(1).All(o => o.action == null), "Unable-to-talk pawns can still listen");
        pawn.health.capacities.Hearing = false;
        Check(radio.GetFloatMenuOptions(pawn).All(o => o.action == null), "Hearing is required for both activities");
        pawn.health.capacities.Hearing = pawn.health.capacities.Talking = true;
        pawn.Reachable = false;
        Check(radio.UseFailure(pawn, false) == "NoPath", "Unreachable radios report no path");
        pawn.Reachable = true; pawn.SpotAvailable = false;
        Check(radio.GetFloatMenuOptions(pawn).All(o => o.action == null), "A reserved interaction spot blocks both activities");
        pawn.SpotAvailable = true;

        var listenGiver = new JoyGiver_Radio();
        var chatGiver = new JoyGiver_RadioChat();
        Check(listenGiver.Play(pawn, radio)?.commTarget == null, "Automatic listening does not pick a faction");
        Check(chatGiver.Play(pawn, radio)?.commTarget == low, "Automatic conversation picks a valid HAM channel");
        radio.Power.PowerOn = false;
        Check(listenGiver.Play(pawn, radio) == null && chatGiver.Play(pawn, radio) == null, "Unpowered radios cannot provide automatic joy jobs");
        radio.Power.PowerOn = true;
        low.defeated = high.defeated = true;
        Check(chatGiver.Play(pawn, radio) == null && listenGiver.Play(pawn, radio) != null, "Broadcasts remain usable when no HAM faction channel exists");
        low.defeated = high.defeated = false;

        var driver = new JobDriver_ChatOnRadio { pawn = pawn, job = radio.MakeRadioJob(new JobDef(), low) };
        Check(driver.TryMakePreToilReservations(false), "A radio job reserves the building and listening spot");
        pawn.SpotAvailable = false;
        Check(!driver.TryMakePreToilReservations(false), "Jobs cannot reserve an occupied spot");
        pawn.SpotAvailable = true;
        var toils = driver.Toils();
        Check(!driver.Failures.Any(f => f()), "A powered chat job can continue");
        toils.Last().initAction();
        toils.Last().initAction();
        Check(radio.Map.Ham.Calls == 1 && low.Calls == 0, "Automatic chats consult the existing daily rumor component once and never open windows");
        toils.Last().tickIntervalAction(60);
        Check(JoyUtility.Ticks == 60 && pawn.ComfortTicks == 60, "Radio sessions accumulate normal recreation and chair comfort over time");
        toils.Last().Finish();
        Check(JoyUtility.RoomThoughts == 1, "Finishing grants the normal recreation-room thought");
        radio.Power.PowerOn = false;
        Check(driver.Failures.Any(f => f()), "Power cuts interrupt an ongoing session");
        radio.Power.PowerOn = true; pawn.health.capacities.Talking = false;
        Check(driver.Failures.Any(f => f()), "Lost talking capacity interrupts conversation");
        pawn.health.capacities.Talking = true; low.defeated = true;
        Check(driver.Failures.Any(f => f()), "An invalidated channel interrupts conversation");
        low.defeated = false;
        driver.ExposeData();
        Scribe_Values.Loading = true;
        var restored = new JobDriver_ChatOnRadio { pawn = pawn, job = driver.job };
        restored.ExposeData(); restored.Toils().Last().initAction();
        Check(radio.Map.Ham.Calls == 1, "Loading an ongoing chat cannot consume another rumor");
        Scribe_Values.Loading = false;
        var manual = new JobDriver_ChatOnRadio { pawn = pawn, job = radio.MakeRadioJob(new JobDef(), high) };
        manual.job.playerForced = true;
        Check(driver.IsSameJobAs(manual.job) == false && driver.IsSameJobAs(driver.job) == null,
            "Manual channel selection restarts even the same automatic conversation so the window can open");
        manual.Toils().Last().initAction();
        Check(high.Calls == 1 && radio.Map.Ham.Calls == 1, "Manual chat uses the selected faction's native negotiation window; rumors wait for its chatter choice");
        var listening = new JobDriver_ListenRadio { pawn = pawn, job = radio.MakeRadioJob(new JobDef()) };
        var listeningToils = listening.Toils(); listeningToils.Last().initAction();
        Check(low.Calls == 0 && high.Calls == 1 && radio.Map.Ham.Calls == 1, "Listening never opens faction dialogue or generates missions");
        pawn.health.capacities.Talking = false;
        Check(!listening.Failures.Any(f => f()), "Listening continues without speech");
        pawn.health.capacities.Hearing = false;
        Check(listening.Failures.Any(f => f()), "Lost hearing interrupts listening");

        Definitions();
        Console.WriteLine("PASS: " + checks + " radio checks.");
    }

    private static void Definitions()
    {
        var radio = XDocument.Load("Defs/WildWest/Buildings/Radio.xml").Root.Element("ThingDef");
        Check((string)radio.Element("size") == "(1,1)" && (string)radio.Element("designationCategory") == "Joy", "A 1x1 radio is buildable under recreation");
        Check((string)radio.Element("graphicData").Element("drawSize") == "(1.5,1.5)", "Radio rendering uses half the milling machine's 3x3 size");
        foreach (var direction in new[] { "north", "east", "south" })
            Check(File.Exists("Textures/Buildings/HD_Radio_" + direction + ".png"), "Radio directional texture exists: " + direction);
        Check(radio.Element("comps").Elements().Any(e => (string)e.Element("compClass") == "CompPowerTrader"
            && (int)e.Element("basePowerConsumption") > 0), "The radio requires an electrical power connection");
        Check(!radio.Element("comps").Elements().Any(e => ((string)e.Attribute("Class") ?? "").Contains("Telegraph")
            || ((string)e.Attribute("Class") ?? "").Contains("Refuelable")), "No wired installation or consumable cell component is attached");
        var definitions = XDocument.Load("Defs/WildWest/RadioRecreation.xml").Root;
        foreach (var giver in definitions.Elements("JoyGiverDef"))
        {
            var job = definitions.Elements("JobDef").Single(j => (string)j.Element("defName") == (string)giver.Element("jobDef"));
            Check((string)giver.Element("joyKind") == (string)job.Element("joyKind") && (int)job.Element("joyDuration") > 0,
                "Each automatic joy giver has a matching timed recreation job");
            Check(giver.Element("requiredCapacities").Elements().Any(e => e.Value == "Hearing"), "Both activities require hearing");
        }
        var english = XDocument.Load("Languages/English/Keyed/Radio.xml").Root.Elements();
        var korean = XDocument.Load("Languages/Korean (한국어)/Keyed/Radio.xml").Root.Elements();
        Check(english.Select(e => e.Name).SequenceEqual(korean.Select(e => e.Name)), "Radio translations cover both languages");
        Check(korean.Single(e => e.Name == "HD_Radio_Channel").Value.Contains("채널") && !korean.Any(e => e.Value.Contains("회선")), "Radio controls use channel terminology");
    }
}
