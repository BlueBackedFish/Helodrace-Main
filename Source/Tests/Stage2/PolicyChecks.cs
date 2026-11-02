using System;
using System.Linq;
using HarmonyLib;
using Helodrace.Economy;
using RimWorld;
using Verse;

namespace Helodrace.Tests.Stage2;

public sealed partial class Checks
{
    private void PolicyChecks()
    {
        var ledger = GameComponent_FinancialGoodwill.Current;
        foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
        {
            bool eligible = faction.def.defName is "HD_HelodCivilLowFaction" or "HD_HelodCivilHighFaction";
            Check(GameComponent_FinancialGoodwill.Supports(faction) == eligible, "Financial faction classification: " + faction.def.defName);
            if (!eligible)
                Check(!ledger.TrySet(faction, 80f) && ledger.Adjust(faction, 10f) == 0f
                    && ledger.RecordServicePayment(faction, 250) == 0f && !ledger.TrySpend(faction, 1f)
                    && ledger.Get(faction) == 0f, "Non-Helod ledger rejects all balance operations: " + faction.def.defName);
        }
        var foreign = new Faction { def = new FactionDef { defName = "ExternalRaceFaction" } };
        Check(!ledger.TrySet(foreign, 100f) && ledger.Get(foreign) == 0f, "External faction definitions cannot receive financial reputation");
        Faction ordinary = Find.FactionManager.AllFactionsListForReading.First(f => !f.IsPlayer
            && !f.def.permanentEnemy && !GameComponent_FinancialGoodwill.Supports(f) && !f.Hidden);
        int before = ordinary.PlayerGoodwill;
        ordinary.TryAffectGoodwillWith(Faction.OfPlayer, 10, canSendMessage: false, canSendHostilityLetter: false, reason: HistoryEventDefOf.GaveGift);
        Check(ordinary.PlayerGoodwill > before && ledger.Get(ordinary) == 0f,
            "Actual non-Helod gift diplomacy increases without financial reputation");
        ledger.TrySet(Low, 0f);
        float reward = FinancialGoodwillRules.HalfReward(Low.CalculateAdjustedGoodwillChange(Faction.OfPlayer, 10));
        Low.TryAffectGoodwillWith(Faction.OfPlayer, 10, canSendMessage: false, canSendHostilityLetter: false, reason: HistoryEventDefOf.GaveGift);
        Check(ledger.Get(Low) == reward && reward > 0f, "Actual LOW gift callback awards the separate half reward");
        ledger.TrySet(Low, 0f);
        Check(ledger.RecordServicePayment(Low, 250) == 12.5f && ledger.Get(Low) == 12.5f,
            "250 STL service payment grants exactly 12.5 financial goodwill");

        var radio = Def<ThingDef>("HD_Radio");
        Check(radio.designationCategory.defName == "HD_HelodTechnology_Build"
            && radio.costList.Single(c => c.thingDef.defName == "HD_BasicElectronicComponents").count == 4
            && radio.costList.All(c => c.thingDef.defName != "HD_BasicElectricalComponents"),
            "Resolved radio definition uses the technology tab and four electronic components");
        Check(radio.building.joyKind == Def<JoyKindDef>("Television")
            && Def<JoyGiverDef>("HD_ListenRadio").giverClass == typeof(JoyGiver_RadioListening),
            "Resolved radio retains its listening recreation");

        Check(actor.def == ThingDefOf.Human, "Ordinary Core colonist generation remains human");
        ThingDef exclusive = Def<ThingDef>("HD_WildWestMetalVest");
        var pair = new ThingStuffPair(exclusive, ThingDefOf.Steel);
        Check(!HelodRace.CanWear(actor, exclusive)
            && !(bool)AccessTools.Method(typeof(PawnApparelGenerator), "CanUseStuff").Invoke(null, new object[] { actor, pair })
            && !(bool)AccessTools.Method(typeof(PawnApparelGenerator), "CanUsePair").Invoke(null, new object[] { pair, actor, 100000f, true, 0 }),
            "Initial human apparel candidates reject Helod-only clothing before allocation");
        foreach (string kindName in new[] { "Slave", "Refugee", "SpaceRefugee" })
        {
            var kind = Def<PawnKindDef>(kindName);
            for (int i = 0; i < 100; i++)
            {
                var request = new PawnGenerationRequest(kind, Faction.OfPlayer, canGeneratePawnRelations: false);
                Patch_HelodPawnRequest.Prefix(ref request);
                Check(request.KindDef == kind && request.ForcedXenotype == null,
                    "Ordinary generation request unchanged: " + kindName + "/" + i);
            }
            Pawn generated = NewPawn(kind, Faction.OfPlayer, map.Center + new IntVec3(4, 0, 0));
            Check(generated.def == ThingDefOf.Human, "Actual " + kindName + " generation remains human");
            generated.DeSpawn();
        }
        foreach (string factionName in new[] { "PlayerColony", "HD_HelodPlayerColony" })
        {
            var request = new PawnGenerationRequest(PawnKindDefOf.Colonist,
                new Faction { def = Def<FactionDef>(factionName) });
            Patch_HelodStartingPawn.Postfix(ref request);
            Check((request.KindDef == HelodRace.ColonistKind) == (factionName == "HD_HelodPlayerColony"),
                "Starting pawn substitution limited to the authored Helod colony: " + factionName);
        }
        Pawn helod = NewPawn(HelodRace.ColonistKind, Faction.OfPlayer, map.Center + new IntVec3(5, 0, 0));
        Check(HelodRace.IsHelod(helod) && HelodRace.Settings.xenotypeList.Contains(helod.genes.Xenotype),
            "Explicit Helod pawn generation retains its race and authored xenotype");
        helod.DeSpawn();
        var forced = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forcedXenotype: HelodRace.Settings.xenotypeList[0]);
        Pawn scenarioPawn = PawnGenerator.GeneratePawn(forced);
        Check(HelodRace.IsHelod(scenarioPawn), "Scenario-requested Helod xenotypes still generate the Helod race");
        Check(typeof(HelodRace).Assembly.GetType("Helodrace.Patch_HelodWanderer") == null,
            "Both ordinary wanderer incident and quest substitution hooks are removed");
        Check(!(bool)AccessTools.Field(typeof(HelodRace).Assembly.GetType("Helodrace.MapComponent_TacticalEngineAudit"), "GeneratingFixture").GetValue(null),
            "Tactical fixture generation remains inactive in an ordinary game");
    }
}
