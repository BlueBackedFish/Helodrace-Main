using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace;

public sealed class Building_WorkTable_FiberSpinningModule : Building_WorkTable
{
    private static FacilityLevel reactionRequirement;

    // Any linked, running facility with the chemical reaction field drives the spinner.
    public Building_WorkTable OperationalReactor
        => LinkedFacilityRecipes.SatisfyingFacility(reactionRequirement ??= new FacilityLevel
        {
            field = DefDatabase<FacilityFieldDef>.GetNamed("HD_ChemicalReaction"),
            level = 1
        }, this) is Building_WorkTable reactor && reactor.Faction == Faction ? reactor : null;

    public override void UsedThisTick()
    {
        base.UsedThisTick();
        // The reactor processes the polymer while the operator draws the fiber.
        // Its ordinary power-load and heat behavior therefore follows this job.
        OperationalReactor?.UsedThisTick();
    }

    public override string GetInspectString()
        => base.GetInspectString() + "\n" + (OperationalReactor is { } reactor
            ? "HD_FiberSpinner_Linked".Translate(reactor.LabelShort).Resolve()
            : "HD_FiberSpinner_NeedsReactor".Translate().Resolve());
}

[HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.UsableForBillsAfterFueling))]
internal static class FiberSpinningModuleUsability
{
    private static void Postfix(Building_WorkTable __instance, ref bool __result)
    {
        if (__result && __instance is Building_WorkTable_FiberSpinningModule module)
            __result = module.OperationalReactor != null;
    }
}
