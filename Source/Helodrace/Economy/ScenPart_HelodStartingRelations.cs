using RimWorld;
using Verse;

namespace Helodrace.Economy;

// Helod scenarios open with the Mercantile Association (LOW) allied to the colony
// at a modest goodwill, and with LOW and the Vesenica State (HIGH) merely neutral
// toward each other. Runs once after world generation, before the colony lands.
public sealed class ScenPart_HelodStartingRelations : ScenPart
{
    public const string LowFaction = "HD_HelodCivilLowFaction";
    public const string HighFaction = "HD_HelodCivilHighFaction";
    public int lowGoodwill = 20;

    public override string Summary(Scenario scen) => "HD_StartingRelations_Summary".Translate(lowGoodwill);

    public override void PostWorldGenerate()
    {
        base.PostWorldGenerate();
        Apply(Find.FactionManager);
    }

    public void Apply(FactionManager manager)
    {
        Faction player = manager.OfPlayer;
        Faction low = manager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamedSilentFail(LowFaction));
        Faction high = manager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamedSilentFail(HighFaction));
        if (player != null && low != null) Set(player, low, FactionRelationKind.Ally, lowGoodwill);
        if (low != null && high != null) Set(low, high, FactionRelationKind.Neutral, 0);
    }

    private static void Set(Faction a, Faction b, FactionRelationKind kind, int goodwill)
    {
        a.SetRelation(new FactionRelation(b, kind) { baseGoodwill = goodwill });
        // SetRelation mirrors only the kind; keep goodwill symmetrical as world generation does.
        b.RelationWith(a).baseGoodwill = goodwill;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref lowGoodwill, "lowGoodwill", 20);
    }
}
