using System;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests.RoyaltyApparel;

// Actual-game check: Royalty apparel with Helod-fitted art is wearable and drawn with the Helod
// texture, helmets fold Helod ears, and Royalty apparel without Helod art stays blocked.
public sealed class Checks : GameComponent
{
    private static readonly (string def, bool foldsEars)[] Fitted =
    {
        ("Apparel_Beret", true),
        ("Apparel_ArmorHelmetCataphract", true),
        ("Apparel_PsyfocusHelmet", true),
        ("Apparel_Coronet", false),
        ("Apparel_Crown", false),
        ("Apparel_CrownStellic", false),
        ("Apparel_ArmorCataphract", false),
        ("Apparel_Corset", false),
        ("Apparel_PackBroadshield", false),
    };
    private static readonly string[] Unfitted = { "Apparel_RobeRoyal", "Apparel_PsyfocusRobe", "Apparel_HatTop", "Apparel_ArmorMarineHelmetPrestige" };
    private bool done;
    private string output;
    public Checks(Game game) { }
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + "\n");
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Write("PASS: " + text); }

    public override void GameComponentTick()
    {
        if (done || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdRoyaltyApparel", out output)) return;
        done = true;
        try { Run(); Write("COMPLETE"); Waylith.EngineApplication.Instance.Quit(0); }
        catch (Exception e) { Write("FAIL: " + e); Log.Error(e.ToString()); Waylith.EngineApplication.Instance.Quit(1); }
    }

    private void Run()
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 Helod Royalty apparel check\n");
        Map map = Find.CurrentMap;
        Check(ModsConfig.RoyaltyActive, "Royalty is active");
        Pawn helod = PawnGenerator.GeneratePawn(new PawnGenerationRequest(HelodRace.ColonistKind, Faction.OfPlayer));
        GenSpawn.Spawn(helod, map.Center, map);
        Check(HelodRace.IsHelod(helod), "Test pawn is a Helod (body " + helod.story.bodyType?.defName + ")");
        HediffDef covered = DefDatabase<HediffDef>.GetNamed("HD_HelodCoveredEars");

        foreach ((string defName, bool foldsEars) in Fitted)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(defName);
            helod.apparel.DestroyAll();
            Check(HelodRace.CanWear(helod, def), defName + " is wearable by Helods");
            Apparel apparel = (Apparel)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            helod.apparel.Wear(apparel, false);
            Check(helod.apparel.WornApparel.Contains(apparel), defName + " is worn");
            string path = apparel.WornGraphicPath;
            Check(path != null && path.StartsWith("Helod/VanillaApparel/"), defName + " uses the Helod texture (" + path + ")");
            Check(ApparelGraphicRecordGetter.TryGetGraphicApparel(apparel, helod.story.bodyType, false, out ApparelGraphicRecord record)
                && record.graphic?.MatSouth?.mainTexture != null && record.graphic.MatSouth.mainTexture != BaseContent.BadTex,
                defName + " Helod texture loads (" + record.graphic?.MatSouth?.mainTexture?.name + ")");
            bool folded = helod.health.hediffSet.HasHediff(covered);
            Check(folded == foldsEars, defName + (foldsEars ? " folds Helod ears" : " leaves Helod ears up"));
            Check(HelodCoveredEarsUtility.HidesEars(helod) == foldsEars, defName + (foldsEars ? " hides the ear render nodes" : " keeps the ears drawn"));
        }
        helod.apparel.DestroyAll();
        Check(!helod.health.hediffSet.HasHediff(covered), "Ears unfold after the helmet is removed");

        foreach (string defName in Unfitted)
            Check(!HelodRace.CanWear(helod, DefDatabase<ThingDef>.GetNamed(defName)), defName + " (no Helod art) stays blocked for Helods");
    }
}
