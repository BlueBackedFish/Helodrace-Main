using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using Helodrace;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static float FightChance(HelodConfrontationWeights w) => w.fight / (w.reconcile + w.disperse + w.fight);
    private static float ReconcileChance(HelodConfrontationWeights w) => w.reconcile / (w.reconcile + w.disperse + w.fight);

    private static void Main(string[] args)
    {
        foreach (float mood in new[] { 0f, 0.2f, 0.5f, 0.8f, 1f })
        foreach (float opinion in new[] { -100f, -50f, 0f, 50f, 100f })
        foreach (float aggression in new[] { 0.6f, 1f, 1.8f })
        {
            var alone = HelodConfrontationOutcomes.Calculate(mood, mood, opinion, opinion, 0f, aggression);
            var mediated = HelodConfrontationOutcomes.Calculate(mood, mood, opinion, opinion, 1f, aggression);
            Check(FightChance(mediated) < FightChance(alone), "Mediation must reduce the chance of fighting.");
            Check(ReconcileChance(mediated) > ReconcileChance(alone), "Mediation must improve reconciliation.");
            foreach (var w in new[] { alone, mediated })
            {
                Check(w.reconcile > 0 && w.disperse > 0 && w.fight > 0, "All three outcomes must remain possible.");
                Check(w.Choose(0) == HelodConfrontationOutcome.Reconcile, "First probability segment.");
                Check(w.Choose((w.reconcile + w.disperse * 0.5f) / (w.reconcile + w.disperse + w.fight))
                    == HelodConfrontationOutcome.Disperse, "Middle probability segment.");
                Check(w.Choose(1) == HelodConfrontationOutcome.Fight, "Last probability segment.");
            }
        }
        var angry = HelodConfrontationOutcomes.Calculate(0, 0, -100, -100, 0, 1.8f);
        var calm = HelodConfrontationOutcomes.Calculate(1, 1, 100, 100, 0, 0.6f);
        Check(FightChance(angry) > FightChance(calm), "Mood, relations and personality must affect escalation.");
        var oneDistressed = HelodConfrontationOutcomes.Calculate(0, 1, 0, 0, 0, 1);
        var bothNeutral = HelodConfrontationOutcomes.Calculate(0.5f, 0.5f, 0, 0, 0, 1);
        Check(FightChance(oneDistressed) > FightChance(bothNeutral), "One distressed participant must not be hidden by the mean mood.");
        var forward = HelodConfrontationOutcomes.Calculate(0.2f, 0.8f, -40, 70, 0.6f, 1.2f);
        var reverse = HelodConfrontationOutcomes.Calculate(0.8f, 0.2f, 70, -40, 0.6f, 1.2f);
        Check(Math.Abs(FightChance(forward) - FightChance(reverse)) < 0.00001f, "Participant ordering must not change the outcome weights.");

        DirectoryInfo root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Defs"))) root = root.Parent;
        Check(root != null, "Repository root not found.");
        var defs = XDocument.Load(Path.Combine(root.FullName, "Defs/Helod/Social/HelodConfrontation.xml"));
        Check(defs.Root.Elements("JobDef").All(x => (string)x.Element("neverShowWeapon") == "true"), "Confrontation jobs must keep weapons hidden.");
        Check((string)defs.Root.Element("MentalStateDef").Element("stateClass") == "Helodrace.MentalState_HelodConfrontation", "Mental-state binding.");
        var patch = XDocument.Load(Path.Combine(root.FullName, "Patches/HelodConfrontation.xml"));
        if (args.Length > 0)
        {
            var vanilla = XDocument.Load(Path.Combine(args[0], "Data/Core/Defs/ThinkTreeDefs/SubTrees_Misc.xml"));
            string xpath = (string)patch.Root.Element("Operation").Element("xpath");
            Check(vanilla.XPathSelectElements(xpath).Count() == 1, "Think-tree insertion must match exactly one vanilla branch.");
        }
        foreach (string language in new[] { "English", "Korean (한국어)" })
        {
            var keyed = XDocument.Load(Path.Combine(root.FullName, "Languages", language, "Keyed/HelodConfrontation.xml"));
            Check(keyed.Root.Elements().Count() == 4, "All outcome notifications must be translated.");
        }
        Console.WriteLine("PASS: 75 balance scenarios, mediation, all outcome segments, asymmetric mood, participant ordering, definitions and translations.");
        Console.WriteLine($"Fight chance: distressed/hostile {FightChance(angry):P1}; calm/friendly {FightChance(calm):P1}.");
    }
}
