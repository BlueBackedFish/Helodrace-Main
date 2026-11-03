using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Helodrace;
using RimWorld;
using Verse;

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
        var other = Channel("OutlanderCivil");
        Find.FactionManager.AllFactionsVisibleInViewOrder.AddRange(new[] { low, high, other });
        var radio = new Building_Radio();
        var pawn = new Pawn { Map = radio.Map };
        var terminal = new CompTelegraphTable { parent = radio, props = new CompProperties_TelegraphTable { wireless = true } };
        Check(radio.CanUseNow && terminal.ConnectionFailure == null && terminal.Provider == low,
            "An uninstalled radio discovers LOW company services without a saved wired provider");
        Check(Building_Radio.Frequencies.SequenceEqual(new[] { low, high }), "Recreation uses only LOW/HIGH HAM frequencies");
        Check(!Building_Radio.IsFrequencyAvailable(null), "Missing HAM frequencies are unavailable");
        Check(terminal.HasTransmissionCapacity(4) && terminal.TryTransmit() && terminal.StoredPrimaryCells == 0,
            "All company requests can transmit on power alone without fuel");
        foreach (int hour in new[] { 0, 7, 8, 17, 18, 23 })
        {
            radio.Map.Network.Hour = hour;
            Check(terminal.ConnectionFailure == null && terminal.TransmissionFailure(low) == null, "Wireless service is open at hour " + hour);
        }
        Check(terminal.TransmissionFailure(high) != null && terminal.TransmissionFailure(other) != null,
            "Company services retain the LOW-only provider restriction");
        low.Hostile = true;
        Check(terminal.TransmissionFailure(low) != null && terminal.TransmissionFailure(low, true) == null,
            "Hostile trade uses the existing financial authorization path, while hostile military services remain blocked");
        low.Hostile = false;
        var options = terminal.CompFloatMenuOptions(pawn).ToList();
        Check(options.Count == 1 && options[0].action != null, "The manual menu offers only the company directory, never forced recreation");
        options[0].action();
        Check(pawn.jobs.Last.def.defName == "HD_UseTelegraphTable" && pawn.jobs.Last.targetA.Thing == radio,
            "Radio company access opens the shared telegraph service workflow");
        low.defeated = true;
        Check(terminal.ConnectionFailure != null && terminal.Provider == null, "HIGH cannot replace a missing LOW company provider");
        low.defeated = false;
        radio.Power.PowerOn = false;
        Check(!terminal.TryTransmit() && terminal.CompFloatMenuOptions(pawn).Single().action == null,
            "Loss of power blocks service execution and the service menu");
        radio.Power.PowerOn = true; radio.Map.gameConditionManager.Disabled = true;
        Check(!radio.CanUseNow && terminal.ConnectionFailure != null, "Solar flares interrupt wireless service");
        radio.Map.gameConditionManager.Disabled = false; radio.Power = null;
        Check(!radio.CanUseNow, "Missing electrical components cannot supply free service");
        radio.Power = new CompPowerTrader();
        var wired = new CompTelegraphTable { parent = new Building { Map = radio.Map, Fuel = new CompRefuelable { Fuel = 2 } }, props = new CompProperties_TelegraphTable() };
        Check(wired.ConnectionFailure != null, "Wired terminals still need installation");
        radio.Map.Network.Provider = low; radio.Map.Network.Ready = 0; radio.Map.Network.Hour = 2;
        Check(wired.ConnectionFailure != null && terminal.ConnectionFailure == null, "Only wired service closes at night");
        radio.Map.Network.Hour = 12;
        Check(wired.ConnectionFailure == null && !wired.HasTransmissionCapacity(3) && wired.TryTransmit() && wired.StoredPrimaryCells == 1,
            "Wired terminals preserve their real primary-cell budget and consumption");

        var listenGiver = new JoyGiver_RadioListening();
        var chatGiver = new JoyGiver_RadioChat();
        ListeningArea(radio, pawn, listenGiver);
        Check(listenGiver.Play(pawn, radio) != null && chatGiver.Play(pawn, radio)?.commTarget == low,
            "Both activities can be autonomously selected by the recreation system");
        pawn.health.capacities.Talking = false;
        Check(listenGiver.Play(pawn, radio) != null && chatGiver.Play(pawn, radio) == null, "Speech is needed for chatting only");
        pawn.health.capacities.Hearing = false;
        Check(listenGiver.Play(pawn, radio) == null && chatGiver.Play(pawn, radio) == null, "Both activities require hearing");
        pawn.health.capacities.Hearing = pawn.health.capacities.Talking = true;
        low.defeated = high.defeated = true;
        Check(chatGiver.Play(pawn, radio) == null && listenGiver.Play(pawn, radio) != null,
            "Broadcast listening does not need an existing faction frequency");
        low.defeated = high.defeated = false;
        var job = chatGiver.Play(pawn, radio);
        var driver = new JobDriver_ChatOnRadio { pawn = pawn, job = job };
        Check(driver.TryMakePreToilReservations(false), "Chatter reserves its operator spot");
        var toils = driver.Toils();
        toils.Last().initAction(); toils.Last().initAction();
        Check(radio.Map.Ham.Calls == 1 && low.Calls == 0, "Autonomous chatter consults the shared rumor calendar once, without opening dialogue windows");
        radio.JoyFactor=1.35f;
        toils.Last().tickIntervalAction(60);
        Check(JoyUtility.Ticks == 60 && pawn.ComfortTicks == 60, "Chatting gains normal recreation and chair comfort");
        Check(JoyUtility.LastSource==null&&Math.Abs(JoyUtility.LastFactor-1.35f)<.0001f,
            "Social chatter avoids the TV building kind while preserving its quality joy multiplier");
        driver.ExposeData(); Scribe_Values.Loading = true;
        var restored = new JobDriver_ChatOnRadio { pawn = pawn, job = job };
        restored.ExposeData(); restored.Toils().Last().initAction(); Scribe_Values.Loading = false;
        Check(radio.Map.Ham.Calls == 1, "Loading ongoing chatter cannot generate a second rumor");
        var listening = new JobDriver_ListenRadio { pawn = pawn, job = listenGiver.Play(pawn, radio) };
        Check(ReferenceEquals(listening.Toils().Single(), JobDriver_WatchTelevision.NativeToil),
            "Listening delegates its toils to the native TV driver");
        radio.Power.PowerOn = false;
        Check(driver.Failures.Any(f => f()) && listening.Failures.Any(f => f()), "Power failure interrupts both ongoing activities");
        Check(listenGiver.Play(pawn, radio) == null && chatGiver.Play(pawn, radio) == null, "Unpowered radios cannot attract recreation jobs");
        Definitions();
        Console.WriteLine("PASS: " + checks + " radio checks.");
    }
    private static void ListeningArea(Building_Radio radio, Pawn pawn, JoyGiver_RadioListening giver)
    {
        foreach (var cell in new[] { new IntVec3(4, 0, 0), new IntVec3(-4, 0, 0), new IntVec3(0, 0, 4), new IntVec3(0, 0, -4), new IntVec3(2, 0, 2) })
            Check(RadioListeningUtility.InRange(radio, cell), "All directions and nearby diagonals are inside the listening circle");
        Check(!RadioListeningUtility.InRange(radio, new IntVec3(4, 0, 4))
            && !RadioListeningUtility.InRange(radio, new IntVec3(0, 0, 5)), "The circle excludes square corners and cells past its radius");
        var chairCell = new IntVec3(0, 0, -3);
        var chair = new Building { Map = radio.Map, Position = chairCell };
        chair.def.building.isSittable = true;
        radio.Map.Seats.Add(chairCell, chair);
        var job = giver.Play(pawn, radio);
        Check(job.targetB.Cell.Equals(chairCell) && job.targetC.Thing == chair, "Listening prefers an available chair behind the radio");
        radio.Map.Forbidden.Add(chairCell);
        Check(!giver.Play(pawn, radio).targetB.Cell.Equals(chairCell), "Forbidden chairs cannot be selected");
        radio.Map.Forbidden.Clear(); radio.Map.Reserved.Add(chairCell);
        Check(!giver.Play(pawn, radio).targetB.Cell.Equals(chairCell), "Reserved destinations cannot be selected");
        radio.Map.Reserved.Clear(); radio.Map.Rooms.Add(chairCell, new object());
        Check(!giver.Play(pawn, radio).targetB.Cell.Equals(chairCell), "Listening stays in the radio's room");
        radio.Map.Rooms.Clear(); pawn.Position = chairCell;
        Check(giver.CanPlayInBed(pawn, radio), "A bed behind the radio is eligible within the listening circle");
        pawn.Position = new IntVec3(4, 0, 4);
        Check(!giver.CanPlayInBed(pawn, radio), "Beds outside the circle are rejected");
        pawn.Position = new IntVec3(0, 0, 0); radio.Map.Seats.Clear();
    }
    private static void Definitions()
    {
        var radio = XDocument.Load("Defs/WildWest/Buildings/Radio.xml").Root.Element("ThingDef");
        Check((string)radio.Element("designationCategory") == "HD_HelodTechnology_Build"
            && (int)radio.Element("costList").Element("HD_BasicElectronicComponents") == 4
            && radio.Element("costList").Element("HD_BasicElectricalComponents") == null,
            "Radio belongs to technology construction and uses electronic components");
        var recipes = XDocument.Load("Defs/Recipes/Recipes_Helod.xml").Root.Elements("RecipeDef");
        var electronicRecipe = recipes.Single(r => (string)r.Element("products")?.Element("HD_BasicElectronicComponents") == "1");
        var research = XDocument.Load("Defs/Research/ResearchProjects_Helod.xml").Root.Elements("ResearchProjectDef");
        var wireless = research.Single(r => (string)r.Element("defName") == "HelodWireless");
        Check(wireless.Element("prerequisites").Elements().Any(p => p.Value == (string)electronicRecipe.Element("researchPrerequisite"))
            && electronicRecipe.Element("recipeUsers").Elements().Any(u => u.Value == "HD_BasicWorkbench"),
            "Radio research already unlocks self-produced electronic components at the basic workbench");
        Check((string)radio.Element("size") == "(1,1)" && (string)radio.Element("graphicData").Element("drawSize") == "(1.5,1.5)", "Radio footprint and half-size texture are preserved");
        Check(radio.Element("comps").Elements().Any(e => (string)e.Element("wireless") == "true"), "The radio is configured as a wireless company terminal");
        Check((string)radio.Element("building").Element("joyKind") == "Television"
            && (float)radio.Element("specialDisplayRadius") == 4f && radio.Element("placeWorkers") == null,
            "Radio uses TV recreation with a circular radius overlay, without the rectangular TV viewing area");
        var defs = XDocument.Load("Defs/WildWest/RadioRecreation.xml").Root;
        var listen = defs.Elements("JoyGiverDef").Single(e => (string)e.Element("defName") == "HD_ListenRadio");
        Check((string)listen.Element("canDoWhileInBed") == "true" && (string)listen.Element("joyKind") == "Television",
            "Listening allows native bedside TV-style recreation");
        Check(!defs.Elements("JoyKindDef").Any(), "The redundant separate listening recreation category is removed");
        foreach (var giver in defs.Elements("JoyGiverDef"))
            Check(defs.Elements("JobDef").Any(j => (string)j.Element("defName") == (string)giver.Element("jobDef")
                && (string)j.Element("joyKind") == (string)giver.Element("joyKind")), "Automatic giver and job categories match");
        var english = XDocument.Load("Languages/English/Keyed/Radio.xml").Root.Elements();
        var korean = XDocument.Load("Languages/Korean (한국어)/Keyed/Radio.xml").Root.Elements();
        Check(english.Select(e => e.Name).SequenceEqual(korean.Select(e => e.Name)), "Company frequency translations match");
        Check(korean.Any(e => e.Value.Contains("주파수")) && !korean.Any(e => e.Value.Contains("회선") || e.Value.Contains("채널")), "Radio controls consistently use frequencies");
        Check(!english.Any(e => e.Value.Contains("24")) && !korean.Any(e => e.Value.Contains("24")), "The radio header has no 24-hour display");
    }
}
