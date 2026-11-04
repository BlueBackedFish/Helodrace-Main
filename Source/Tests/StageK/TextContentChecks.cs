using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private IEnumerable<int> TextContentChecks(Pawn worker)
    {
        foreach(string name in new[]{"HD_CrowsComputer","HD_Building_CrowsII","HD_InstalledBreachCharge",
            "HD_RotaryDrill","HD_TelegraphTable","HD_Radio","HD_TCCC_HemostaticAgent"})
        {
            var def=Def<ThingDef>(name);
            var extension=def.GetModExtension<ContentUsageExtension>();
            Check(extension!=null&&extension.helpKey.CanTranslate(),"Operating information translation resolves: "+name);
            var entries=def.SpecialDisplayStats(StatRequest.For(def,null)).ToList();
            Check(entries.Count(e=>e.LabelCap=="HD_ContentUsage_Title".Translate().Resolve())==1,
                "Exactly one operating information entry in actual info stats: "+name);
        }
        Check(!Def<ThingDef>("HD_Apparel_WildWestFormalShirt").SpecialDisplayStats(StatRequest.For(Def<ThingDef>("HD_Apparel_WildWestFormalShirt"),null))
            .Any(e=>e.LabelCap=="HD_ContentUsage_Title".Translate().Resolve()),"Unannotated items receive no empty operating information");
        var bench=(Building_WorkTable)ItemBuilding("HD_GunSmithTable",20,-9);
        yield return 600;
        foreach(var pair in new[]{("HD_Make20x102mmMixedRounds","HelodAntiAirMachineGun"),
            ("HD_MakeBGM71AMissile","HelodWireGuidedMissile")})
        {
            var recipe=Def<RecipeDef>(pair.Item1);
            Check(recipe.AllRecipeUsers.SequenceEqual(new[]{bench.def}),"Ammunition is made exclusively at weapons workbench: "+pair.Item1);
            Check(recipe.researchPrerequisite==Def<ResearchProjectDef>(pair.Item2),"Ammunition shares its weapon's research gate: "+pair.Item1);
            Find.ResearchManager.ResetAllProgress();
            Check(!recipe.AvailableNow,"Unresearched ammunition is unavailable: "+pair.Item1);
            foreach(int ticks in ItemBill(worker,bench,pair.Item1))yield return ticks;
        }
        Check(Def<ThingDef>("HD_Building_M167VADS").GetCompProperties<CompProperties_Refuelable>().fuelFilter.Allows(Def<ThingDef>("HD_20x102mmMixedRound")),
            "Newly crafted mixed ammunition is accepted by M167");
        var clothing=ItemClothingBench(worker);yield return 1200;
        foreach(int ticks in ItemBill(worker,clothing,"Make_HD_Apparel_ShieldIronHideRiot"))yield return ticks;
    }
}
