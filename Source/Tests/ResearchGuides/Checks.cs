using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Helodrace.Research;
using RimWorld;
using Verse;

namespace Helodrace.Tests.ResearchGuides;

// Actual-game checks for research guide letters and the guide window.
public sealed class Checks : GameComponent
{
    private bool started, complete;
    private string output;
    private IEnumerator<int> steps;
    public Checks(Game game) { }

    private void Write(string t) => File.AppendAllText(Path.Combine(output, "checks.txt"), t + "\n");
    private void Check(bool ok, string t) { if (!ok) throw new InvalidOperationException(t); Write("PASS: " + t); }
    private static List<ChoiceLetter_ResearchGuide> GuideLetters()
        => Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_ResearchGuide>().ToList();

    public override void GameComponentTick()
    {
        if (started || complete || Find.CurrentMap == null) return;
        if (!GenCommandLine.TryGetCommandLineArg("hdResearchGuides", out output)) return;
        started = true;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 research guide checks\n");
        Find.TickManager.Pause();
        DebugSettings.enableStoryteller = false;
        // Quicktest maps can spawn corpses without a body type, which breaks map drawing.
        foreach (Pawn p in Find.CurrentMap.mapPawns.AllPawnsSpawned.Concat(Find.CurrentMap.listerThings
            .ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>().Select(c => c.InnerPawn)))
            if (p.story != null && p.story.bodyType == null) p.story.bodyType = p.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male;
        steps = Run().GetEnumerator();
    }

    public override void GameComponentUpdate()
    {
        if (complete || steps == null) return;
        try
        {
            if (!steps.MoveNext())
            {
                complete = true;
                Write("COMPLETE");
                Root.Shutdown();
            }
        }
        catch (Exception e)
        {
            complete = true;
            Write("FAIL: " + e);
            Root.Shutdown();
        }
    }

    private IEnumerable<int> Run()
    {
        var guides = DefDatabase<ResearchGuideDef>.AllDefsListForReading;
        string[] expected = { "HD_Guide_MechanicalPower", "HD_Guide_LineShaft", "HD_Guide_Telegraph", "HD_Guide_OilDevelopment", "HD_Guide_Generator", "HD_Guide_Radio",
            "HD_Guide_MachiningFacilities", "HD_Guide_PressingFacilities", "HD_Guide_SteelConverting", "HD_Guide_ChemicalReaction" };
        Check(expected.All(n => guides.Any(g => g.defName == n)), "all guides load");
        Check(guides.All(g => g.research != null && !g.guideText.NullOrEmpty() && !g.label.NullOrEmpty()), "every guide has research, title and text");
        Check(DefDatabase<LetterDef>.GetNamed("HD_ResearchGuideLetter").letterClass == typeof(ChoiceLetter_ResearchGuide), "guide letter def uses the guide letter class");

        var component = GameComponent_ResearchGuides.Current;
        Check(component != null, "guide component is present");

        // Research finished at game start is recorded without letters.
        component.CheckResearch();
        int before = GuideLetters().Count;
        Check(before == 0, "starting research sends no guide letters");
        Check(guides.Where(g => g.research.IsFinished).All(component.HasShown), "starting research is recorded as shown");

        ResearchGuideDef target = guides.First(g => !g.research.IsFinished);
        Find.ResearchManager.FinishProject(target.research, false, null, false);
        component.CheckResearch();
        var letters = GuideLetters();
        Check(letters.Count == before + 1 && letters.Last().guide == target, "newly finished research sends one guide letter: " + target.defName);
        component.CheckResearch();
        Check(GuideLetters().Count == before + 1, "a guide letter is sent only once");

        ChoiceLetter_ResearchGuide letter = letters.Last();
        Check(letter.Choices.Count() == 2, "letter offers read and close options");
        letter.OpenLetter();
        Check(!GuideLetters().Contains(letter), "opening the letter removes it from the letter stack");
        Check(Find.Archive.ArchivablesListForReading.Contains(letter), "the letter stays in the archive");
        var window = Find.WindowStack.WindowOfType<Dialog_ResearchGuide>();
        Check(window != null, "opening the letter shows the guide window");
        for (int i = 0; i < 5; i++) yield return i;
        Check(Find.WindowStack.IsOpen(window), "guide window draws for several frames");
        window.Close(false);
        yield return 0;

        // Every guide window draws without errors.
        foreach (ResearchGuideDef guide in guides)
        {
            var dialog = new Dialog_ResearchGuide(guide);
            Find.WindowStack.Add(dialog);
            yield return 0;
            yield return 0;
            Check(Find.WindowStack.IsOpen(dialog), "guide window draws: " + guide.defName);
            dialog.Close(false);
            yield return 0;
        }
    }
}
