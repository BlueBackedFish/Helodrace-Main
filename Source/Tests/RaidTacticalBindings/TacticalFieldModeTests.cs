using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using Helodrace;
using Helodrace.Tactics;
using Verse;

internal static class TacticalFieldModeTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        Check(TacticalFieldPolicy.Prepare(false,false,false,30,10000,TacticalObservedMotion.Approaching),
            "A distant approaching observation must prepare cover before incoming damage.");
        foreach (var motion in new[]{TacticalObservedMotion.Unknown,TacticalObservedMotion.Stationary,TacticalObservedMotion.Crossing,TacticalObservedMotion.Leaving})
            Check(!TacticalFieldPolicy.Prepare(false,false,false,30,10000,motion),"A distant passive sighting must preserve the attacking mission.");
        var props = new CompProperties_SharpshooterWeapon(); props.ResolveReferences(null);
        Check(!props.DefensiveAI,"Untagged guns do not receive automatic modes.");
        props.aiModeTags = new(){"Defensive"}; props.ResolveReferences(null);
        Check(props.DefensiveAI,"Explicit tags are resolved once, outside ticks.");
        var comp = new CompSharpshooterWeapon { parent = (ThingWithComps)RuntimeHelpers.GetUninitializedObject(typeof(ThingWithComps)) };
        comp.SetMode(true); comp.SetMode(true);
        Check(comp.altModeActive,"Repeated desired ON is idempotent.");
        comp.aiModeOwned = true; comp.ReleaseAIMode(); comp.ReleaseAIMode();
        Check(!comp.altModeActive && !comp.aiModeOwned,"AI cleanup idempotently releases only owned modes.");
        comp.SetMode(true); comp.ReleaseAIMode();
        Check(comp.altModeActive,"Unowned manual modes survive cleanup.");
        comp.aiModeOwned = true; comp.PerformSwitch();
        Check(!comp.aiModeOwned && !comp.altModeActive,"Manual switching takes ownership away from AI.");
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
        var defs = XDocument.Load(Path.Combine(root,"Defs/GreatWar/Items/Weapons_GreatWar.xml"));
        string[] tagged = defs.Root.Elements("ThingDef").Where(x=>x.Descendants("aiModeTags").Any(t=>t.Elements("li").Any(v=>v.Value=="Defensive")))
            .Select(x=>x.Element("defName").Value).OrderBy(x=>x).ToArray();
        Check(tagged.SequenceEqual(new[]{"HD_Gun_M1903A4_Weapon","HD_Gun_M1918A2HAR_Weapon","HD_Gun_M1919A6LMG_Weapon","HD_Gun_M1D_Weapon"}.OrderBy(x=>x)),
            "Only reviewed long range/automatic defensive modes are tagged.");
        Console.WriteLine("PASS: " + checks + " field approach and defensive desired-mode checks.");
    }
}
