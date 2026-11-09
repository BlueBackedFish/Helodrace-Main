using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Helodrace.Tests.BodyVariants;

public sealed class Checks : GameComponent
{
    private int phase;
    private Pawn pawn, worldPawn;
    private Apparel armor, cap, originalShirt;
    private string savedSize;
    private bool started, load;
    private string output;
    public Checks(Game game) { }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref phase, "bodyVariantTestPhase");
        Scribe_Values.Look(ref savedSize, "bodyVariantTestSize");
        Scribe_References.Look(ref pawn, "bodyVariantTestPawn");
        Scribe_References.Look(ref worldPawn, "bodyVariantTestWorldPawn");
        Scribe_References.Look(ref armor, "bodyVariantTestArmor");
        Scribe_References.Look(ref cap, "bodyVariantTestCap");
        Scribe_References.Look(ref originalShirt, "bodyVariantTestShirt");
    }
    private void Check(bool value, string text)
    {
        if (!value) throw new InvalidOperationException(text);
        File.AppendAllText(Path.Combine(output, "checks.txt"), "PASS: " + text + "\n");
    }
    private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);
    private static Apparel Wear(Pawn p, string name)
    {
        var apparel = (Apparel)ThingMaker.MakeThing(Def(name), GenStuff.DefaultStuffFor(Def(name)));
        p.apparel.Wear(apparel, false);
        return apparel;
    }
    private void Graphic(Apparel apparel, string label)
    {
        Check(ApparelGraphicRecordGetter.TryGetGraphicApparel(apparel, pawn.story.bodyType, false, out var record), label + " graphic record");
        foreach (Rot4 facing in new[] { Rot4.South, Rot4.North, Rot4.East, Rot4.West })
            Check(record.graphic.MatAt(facing).mainTexture != BaseContent.BadTex, label + " " + facing + " texture");
    }
    public override void GameComponentTick()
    {
        if (started || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdBodyVariants", out output)) return;
        started = true;
        try
        {
            Directory.CreateDirectory(output);
            if (phase == 0) Run();
            else if (phase == 1)
            {
                Check(!RaidSettings.BodyVariants && !HelodBodyVariants.Active(pawn), "Reload honors changed global setting");
                Check(pawn.apparel.WornApparel.Any(a => a.def.defName == "HD_Apparel_Hoodie")
                    && pawn.apparel.WornApparel.Any(a => a.def.defName == "Apparel_Pants"), "Existing save changes back to original-body hoodie and pants");
                Check(pawn.apparel.WornApparel.Contains(armor) && pawn.apparel.WornApparel.Contains(cap), "Armor and headgear survive save/load and setting change");
                Check(HelodBodyVariants.SizeFor(pawn) == savedSize, "Saved body variant does not reroll while disabled");
                Check(originalShirt.Spawned && !originalShirt.Destroyed, "Removed original garment survives save/load");
                Check(worldPawn.apparel.WornApparel.Any(a => a.def.defName == "Apparel_Pants"), "World pawn migrates on loading with changed setting");
                Graphic(pawn.apparel.WornApparel.First(a => a.def.defName == "HD_Apparel_Hoodie"), "Original-body hoodie");
                Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(PawnRenderNodeWorker_Apparel_Body), "ScaleFor"))
                    ?.Postfixes.Any(p => p.PatchMethod.DeclaringType == typeof(Patch_HelodHoodieFit)) == true,
                    "Original-body hoodie fit patch is installed on actual 1.7 renderer");
                HelodraceBase.Instance.RaidOptions.bodyVariants = true;
                HelodBodyVariants.ApplyToLoadedPawns();
                Check(HelodBodyVariants.SizeFor(pawn) == savedSize, "Re-enabling restores saved variant");
                var hoodie = pawn.apparel.WornApparel.First(a => a.def.defName == "HD_Apparel_Hoodie");
                HelodBodyVariants.ApplyToLoadedPawns();
                Check(pawn.apparel.WornApparel.Contains(hoodie), "Repeated synchronization does not replace garments");
                File.AppendAllText(Path.Combine(output, "checks.txt"), "COMPLETE\n");
                Waylith.EngineApplication.Instance.Quit(0);
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL: " + ex + "\n");
            Log.Error(ex.ToString()); Waylith.EngineApplication.Instance.Quit(1);
        }
    }
    public override void GameComponentUpdate()
    {
        if (!load) return;
        load = false;
        GameDataSaveLoader.LoadGame("HelodBodyVariants");
    }
    private void Run()
    {
        File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 body variant integration checks\n");
        HelodraceBase.Instance.RaidOptions.bodyVariants = false;
        pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(HelodRace.ColonistKind, Faction.OfPlayer, fixedGender: Gender.Female));
        pawn.apparel.DestroyAll();
        GenSpawn.Spawn(pawn, Find.CurrentMap.Center, Find.CurrentMap);
        originalShirt = Wear(pawn, "Apparel_BasicShirt");
        originalShirt.HitPoints /= 2;
        Wear(pawn, "Apparel_Pants");
        armor = Wear(pawn, "Apparel_FlakVest");
        cap = Wear(pawn, "HD_Apparel_BaseballCap");
        Check(pawn.apparel.WornApparel.Contains(originalShirt), "Original-body clothing starts wearable");
        worldPawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(HelodRace.ColonistKind, Faction.OfPlayer));
        Find.WorldPawns.PassToWorld(worldPawn, PawnDiscardDecideMode.KeepForever);
        HelodraceBase.Instance.RaidOptions.bodyVariants = true;
        HelodBodyVariants.ApplyToLoadedPawns();
        Check(pawn.apparel.WornApparel.Any(a => a.def.defName == "HD_Apparel_Hoodie")
            && pawn.apparel.WornApparel.Any(a => a.def.defName == "HD_Apparel_UCPPants"), "Live enabling outfits existing adult with hoodie and fitted pants");
        Check(pawn.apparel.WornApparel.Contains(armor) && pawn.apparel.WornApparel.Contains(cap), "Body-independent armor and headgear stay worn");
        Check(originalShirt.Spawned && originalShirt.HitPoints < originalShirt.MaxHitPoints, "Removed garments keep their damage and are placed on the map");
        Check(worldPawn.apparel.WornApparel.Any(a => a.def.defName == "HD_Apparel_Hoodie"), "Off-map world pawn migrates");
        foreach (string blocked in new[] { "Apparel_BasicShirt", "Apparel_Duster", "Apparel_Pants", "HD_Apparel_WildWestFormalShirt" })
            Check(!HelodRace.CanWear(pawn, Def(blocked)), blocked + " is restricted without fitted variant art");
        foreach (string allowed in new[] { "Apparel_FlakVest", "HD_Apparel_IBTVAssault", "HD_Apparel_M1952AFlakJacket", "HD_WildWestMetalVest", "HD_Apparel_FASTMT", "HD_Apparel_UCPPants", "HD_Apparel_MOLLEBattleBelt", "HD_Apparel_GreatWarMedibag", "HD_Apparel_GreatWarCBRNPouch" })
            Check(HelodRace.CanWear(pawn, Def(allowed)), allowed + " remains wearable");
        var male = PawnGenerator.GeneratePawn(new PawnGenerationRequest(HelodRace.ColonistKind, Faction.OfPlayer, fixedGender: Gender.Male));
        Check(HelodBodyVariants.SizeFor(male) == "None", "Male Helod uses the flat variant");
        Find.WorldPawns.PassToWorld(male, PawnDiscardDecideMode.KeepForever);
        var child = PawnGenerator.GeneratePawn(new PawnGenerationRequest(HelodRace.ColonistKind, Faction.OfPlayer,
            fixedBiologicalAge: 6, fixedChronologicalAge: 6, developmentalStages: DevelopmentalStage.Child));
        Check(child.DevelopmentalStage.Child() && !HelodBodyVariants.Active(child), "Children retain their existing body art");
        Check(HelodRace.CanWear(child, Def("Apparel_BasicShirt"))
            && !Def("HD_Apparel_Hoodie").apparel.PawnCanWear(child, true), "Children keep normal clothing and cannot select adult-only hoodie");
        Find.WorldPawns.PassToWorld(child, PawnDiscardDecideMode.KeepForever);
        var human = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer));
        Check(!HelodBodyVariants.Active(human) && HelodRace.CanWear(human, Def("Apparel_BasicShirt"))
            && !HelodRace.CanWear(human, Def("HD_Apparel_Hoodie")), "Human apparel permissions remain unchanged");
        Find.WorldPawns.PassToWorld(human, PawnDiscardDecideMode.KeepForever);
        var comp = pawn.TryGetComp<CompHelodBodyVariant>();
        foreach (int size in Enumerable.Range(0, 6))
        {
            AccessTools.Field(typeof(CompHelodBodyVariant), "variant").SetValue(comp, size);
            var bodyNode = new PawnRenderNode_HelodBody(pawn, new PawnRenderNodeProperties(), pawn.Drawer.renderer.renderTree);
            foreach (Rot4 facing in new[] { Rot4.South, Rot4.North, Rot4.East, Rot4.West })
                Check(bodyNode.GraphicFor(pawn).MatAt(facing).mainTexture != BaseContent.BadTex,
                    HelodBodyVariants.Sizes[size] + " body graphic " + facing);
            foreach (string name in new[] { "HD_Apparel_Hoodie", "HD_Apparel_CarvalyUniform", "HD_Apparel_GreatWarTrenchArmyInner", "HD_Apparel_GreatWarTrenchArmyOuter", "HD_Apparel_GreatWarStormFrontInner", "HD_Apparel_GreatWarStormFrontOuter", "HD_Apparel_GreatWarStormFrontChestRig", "HD_Apparel_ACSUpper", "HD_Apparel_UCPBlouse", "HD_Apparel_UCPPants" })
            {
                var apparel = Wear(pawn, name);
                Check(HelodRace.CanWear(pawn, apparel.def), name + " fitted permission");
                Graphic(apparel, HelodBodyVariants.Sizes[size] + " " + name);
                pawn.apparel.Remove(apparel); apparel.Destroy();
            }
        }
        pawn.apparel.DestroyAll();
        Wear(pawn, "HD_Apparel_Hoodie"); Wear(pawn, "HD_Apparel_UCPPants");
        armor = Wear(pawn, "Apparel_FlakVest"); cap = Wear(pawn, "HD_Apparel_BaseballCap");
        savedSize = HelodBodyVariants.SizeFor(pawn);
        var fresh = PawnGenerator.GeneratePawn(new PawnGenerationRequest(HelodRace.ColonistKind, Faction.OfPlayer));
        var freshOutfit = fresh.apparel.WornApparel.ToArray();
        fresh.TryGetComp<CompHelodBodyVariant>().Synchronize();
        Check(freshOutfit.SequenceEqual(fresh.apparel.WornApparel), "Newly generated outfits are not replaced by migration");
        Find.WorldPawns.PassToWorld(fresh, PawnDiscardDecideMode.KeepForever);
        phase = 1;
        GameDataSaveLoader.SaveGame("HelodBodyVariants");
        HelodraceBase.Instance.RaidOptions.bodyVariants = false;
        load = true;
    }
}
