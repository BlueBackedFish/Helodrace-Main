using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests.IndustryStage3;

public sealed partial class Checks
{
    private IEnumerable<int> BarrelChecks()
    {
        foreach (string name in new[] { "HD_LowQualityBarrel", "HD_ShotgunBarrel", "HD_WildWestBarrel" })
            Check(DefDatabase<ThingDef>.GetNamedSilentFail(name) == null, "Removed intermediate has no live definition: " + name);
        foreach (string name in new[] { "HD_MakeLowQualityBarrel", "HD_MakeShotgunBarrel", "HD_MakeWildWestBarrel",
            "HD_MakeLowQualityBarrel_BasicWorkbench", "HD_MakeShotgunBarrel_BasicWorkbench", "HD_MakeWildWestBarrel_BasicWorkbench" })
            Check(DefDatabase<RecipeDef>.GetNamedSilentFail(name) == null, "Removed machining recipe has no live definition: " + name);
        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            Check(def.costList == null || def.costList.All(c => c.thingDef != null), "Construction cost references resolve: " + def.defName);
        var recipe = Def<RecipeDef>("HD_Make_Gun_SprMOne_Weapon");
        Check(recipe.ingredients.All(i => i.filter.AllowedThingDefs.All(d => d != null)), "Folded firearm material filters resolve");
        Check(recipe.workAmount == 37500f && recipe.ingredients.Single(i => i.filter.Allows(Def<ThingDef>("HD_CarbonSteel"))).GetBaseCount() == 50f,
            "Raw firearm recipe includes its old barrel's steel and machining work");
        var bench = (Building_WorkTable)Spawn("HD_BasicWorkbench", 0, -10);
        Check(!recipe.Worker.AvailableOnNow(bench), "Firearm assembly still requires its linked machining facility");
        Spawn("HD_TreadleLathe", 4, -10);
        yield return 120;
        Check(recipe.Worker.AvailableOnNow(bench), "Existing treadle lathe still enables early firearm assembly");
        var bill = Bill(bench, recipe.defName);
        var materials = recipe.ingredients.Select(i => (def: i.filter.AnyAllowedDef, count: i.CountRequiredOfFor(i.filter.AnyAllowedDef, recipe, bill))).ToList();
        var before = materials.ToDictionary(i => i.def, i => Count(i.def));
        int products = Count(recipe.ProducedThingDef);
        foreach (int ticks in CompleteBill(bench, bill, false)) yield return ticks;
        Check(Count(recipe.ProducedThingDef) == products + 1, "Actual firearm production finishes without any barrel intermediate");
        foreach (var material in materials)
            Check(Count(material.def) == before[material.def] - material.count, "Actual firearm consumes exactly its folded material cost: " + material.def.defName);
        var low = Find.FactionManager.AllFactionsListForReading.First(f => f.def.defName == "HD_HelodCivilLowFaction");
        Pawn gunner = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Def<PawnKindDef>("HD_GW_HelodBazooka"), low,
            canGeneratePawnRelations: false, fixedBiologicalAge: 25, fixedChronologicalAge: 25));
        Check(gunner.equipment.Primary?.def.weaponTags?.Contains("HD_GreatWarRecoillessWeapon") == true,
            "Bazooka gunner still generates with its launcher after material-based weapon value changes");
    }
}
