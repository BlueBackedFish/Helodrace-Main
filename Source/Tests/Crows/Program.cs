using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Helodrace;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

internal static class Program
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static Building_CrowsComputer Console(Map map) => new Building_CrowsComputer { Map = map, Faction = Faction.OfPlayer };
    private static Building_Crows Turret(Map map)
    {
        var turret = new Building_Crows { Map = map, Faction = Faction.OfPlayer };
        turret.TestVerb = new Verb_Crows { caster = turret };
        return turret;
    }

    private static void Main()
    {
        CrowsDefOf.HD_OperateCrows = new JobDef();
        var map = new Map(); var console = Console(map); var turret = Turret(map);
        Check(!turret.AttackVerb.Available(), "Unlinked CROWS acquired a target.");
        console.Connect(turret);
        Check(console.Connected && turret.Computer == console, "Connection is not reciprocal.");
        Check(console.Graphic.Path.Contains("CROWSController"), "Controller not displayed after linking.");
        Check(!turret.AttackVerb.Available(), "Linked but unmanned CROWS can fire.");
        var pawn = new Pawn { Map = map, Faction = Faction.OfPlayer, Position = console.InteractionCell };
        map.AtControls.Add(pawn);
        Check(!turret.CanFireNow, "Merely standing beside the desk counted as operating.");
        Job job = JobMaker.MakeJob(CrowsDefOf.HD_OperateCrows, console, turret);
        pawn.jobs.CurJob = job;
        var driver = new JobDriver_OperateCrows { pawn = pawn, job = job, CurToilIndex = 0 };
        pawn.jobs.curDriver = driver;
        Check(!turret.CanFireNow, "Walking to the computer counted as operating.");
        driver.CurToilIndex = 1;
        Check(turret.CanFireNow && turret.AttackVerb.Available(), "Seated operator cannot enable CROWS.");
        turret.AttackVerb.TestShot();
        Check(turret.AttackVerb.Shots == 1, "Manned CROWS failed to fire.");

        // Availability and TryCastShot both gate the next shot of an existing burst.
        foreach (Action disable in new Action[] {
            () => pawn.jobs.CurJob = null,
            () => pawn.Downed = true,
            () => pawn.InMentalState = true,
            () => pawn.Position = new IntVec3(9),
            () => pawn.Faction = new Faction(),
            () => pawn.health.capacities.Enabled = false,
            () => console.Power.PowerOn = false,
            () => turret.Power.PowerOn = false,
            () => console.Flick.SwitchIsOn = false,
            () => turret.Flick.SwitchIsOn = false,
            () => map.gameConditionManager.Disabled = true,
            () => turret.IsStunned = true,
            () => turret.Forbidden = true })
        {
            disable();
            Check(!turret.AttackVerb.Available(), "Control loss did not block acquisition/burst availability.");
            turret.AttackVerb.TestShot();
            Check(turret.AttackVerb.Shots == 1, "Another burst shot escaped after losing control.");
            turret.SetWarmupForTest(); turret.DoTickForTest();
            Check(turret.WarmupForTest == 0 && !turret.CurrentTarget.IsValid && turret.AttackVerb.ResetCalls > 0,
                "Control loss did not cancel warmup, current aim and burst.");
            pawn.jobs.CurJob = job; pawn.Downed = pawn.InMentalState = false;
            pawn.Position = console.InteractionCell; pawn.Faction = Faction.OfPlayer;
            pawn.health.capacities.Enabled = true; console.Power.PowerOn = turret.Power.PowerOn = true;
            console.Flick.SwitchIsOn = turret.Flick.SwitchIsOn = true;
            map.gameConditionManager.Disabled = false; turret.IsStunned = turret.Forbidden = false;
            Check(turret.CanFireNow, "Control could not resume after clearing the condition.");
        }
        Check(turret.BaseTicks == 13, "Unmanned tick skipped vanilla building/comps processing.");
        var otherConsole = Console(map); otherConsole.Connect(turret);
        Check(!console.Connected && console.LinkedTurret == null && turret.Computer == otherConsole,
            "Taking over a turret left its old console connected.");
        Check(!console.Graphic.Path.Contains("CROWSController"), "Controller remained after disconnecting.");
        var otherTurret = Turret(map); otherConsole.Connect(otherTurret);
        Check(turret.Computer == null && otherTurret.Computer == otherConsole, "Changing stations left the old station armed.");
        var foreign = Turret(new Map()); otherConsole.Connect(foreign);
        Check(otherConsole.LinkedTurret == otherTurret && foreign.Computer == null, "Cross-map link accepted.");
        foreign = Turret(map); foreign.Faction = new Faction(); otherConsole.Connect(foreign);
        Check(otherConsole.LinkedTurret == otherTurret && foreign.Computer == null, "Cross-faction link accepted.");
        otherConsole.ExposeData(); otherTurret.ExposeData();
        Check(Scribe_References.Values["crowsTurret"] == otherTurret && Scribe_References.Values["crowsComputer"] == otherConsole,
            "Connection references are not persisted on both buildings.");
        otherTurret.DeSpawn();
        Check(otherConsole.LinkedTurret == null && otherTurret.Computer == null, "Removing turret did not clean the connection.");
        otherTurret.Spawned = true; otherConsole.Connect(otherTurret); otherConsole.DeSpawn();
        Check(otherTurret.Computer == null, "Removing computer did not disarm the turret.");
        otherConsole.Spawned = true; otherConsole.Connect(otherTurret); otherTurret.SetFaction(new Faction());
        Check(!otherConsole.Connected && otherTurret.Computer == null, "Capturing turret retained access.");
        VerifyDrawing(); VerifyDefinitions();
        System.Console.WriteLine("PASS: reciprocal links, controller visibility, physical operator/job, 13 immediate shot/warmup interruption cases, reassignment, ownership/map guards, persisted references, despawn, five-layer recoil and definitions.");
    }

    private static void VerifyDrawing()
    {
        var turret = Turret(new Map()); var comp = new CompCrowsGraphic { parent = turret };
        turret.GraphicComp = comp;
        Find.TickManager.TicksGame = 100; comp.PostDraw();
        var rest = Graphics.Draws.ToArray(); Graphics.Draws.Clear();
        Check(rest.Length == 5, "CROWS lost an art layer.");
        Check(rest.Select(x => x.material.Path.Split('/').Last()).SequenceEqual(new[] {
            "HD_CROWSII_M2_Base", "HD_CROWSII_M2_Frame", "HD_CROWSII_M2Barrel", "HD_CROWSII_M2Body", "HD_CROWSII_M2_FeedOverlay" }), "Layer draw order changed.");
        comp.NotifyShotFired(); comp.PostDraw();
        for (int i=0;i<5;i++)
        {
            Check(Math.Abs(Graphics.Draws[i].matrix.pos.z-rest[i].matrix.pos.z-(i==2 ? -.0375f : 0f))<.00001f,
                "Recoil moved a fixed part or barrel travel is wrong.");
            if(i>0) Check(Graphics.Draws[i].matrix.pos.y>Graphics.Draws[i-1].matrix.pos.y,"Draw layer heights overlap.");
        }
    }

    private static void VerifyDefinitions()
    {
        DirectoryInfo root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName,"Defs/ModernWar/Buildings/Crows.xml"))) root=root.Parent;
        string path=root.FullName;
        var xml=XDocument.Load(Path.Combine(path,"Defs/ModernWar/Buildings/Crows.xml"));
        var defs=xml.Root.Elements("ThingDef").ToDictionary(x=>(string)x.Element("defName"));
        Check(defs.Count==3,"Missing buildings or gun definition.");
        Check((string)defs["HD_CrowsComputer"].Element("thingClass")==typeof(Building_CrowsComputer).FullName,"Wrong computer class.");
        Check((string)defs["HD_CrowsComputer"].Element("drawerType")=="RealtimeOnly","Connection graphic can remain baked in the map mesh.");
        Check((string)defs["HD_Building_CrowsII"].Element("thingClass")==typeof(Building_Crows).FullName,"Wrong turret class.");
        Check(!defs["HD_Building_CrowsII"].Descendants().Any(x=>((string)x.Attribute("Class"))=="CompProperties_Mannable"),"CROWS allowed direct manning.");
        var verb=defs["HD_Gun_CrowsII"].Element("verbs").Element("li");
        Check((string)verb.Element("verbClass")==typeof(Verb_Crows).FullName,"CROWS gun bypassed its per-shot operator guard.");
        Check((string)verb.Element("consumeFuelPerShot")=="1","CROWS does not consume ammunition.");
        var job=XDocument.Load(Path.Combine(path,"Defs/ModernWar/Jobs_Crows.xml"));
        Check((string)job.Root.Element("JobDef").Element("driverClass")==typeof(JobDriver_OperateCrows).FullName,"Wrong operating job.");
        foreach(var p in defs.Values.SelectMany(x=>x.Descendants("texPath").Concat(x.Descendants("uiIconPath"))))
            Check(File.Exists(Path.Combine(path,"Textures",p.Value+".png"))||File.Exists(Path.Combine(path,"Textures",p.Value+"_north.png")),"Missing texture "+p.Value);
        foreach(string d in new[]{"north","east","south","west"})
            Check(File.Exists(Path.Combine(path,"Textures/Buildings/ComputerDesk/CROWSController/HD_ComputerDesk_CROWS_"+d+".png")),"Missing connected computer direction.");
        var en=XDocument.Load(Path.Combine(path,"Languages/English/Keyed/Crows.xml"));
        var ko=XDocument.Load(Path.Combine(path,"Languages/Korean (한국어)/Keyed/Crows.xml"));
        Check(en.Root.Elements().Select(x=>x.Name.LocalName).OrderBy(x=>x).SequenceEqual(ko.Root.Elements().Select(x=>x.Name.LocalName).OrderBy(x=>x)),"Missing Korean/English key.");
    }
}
