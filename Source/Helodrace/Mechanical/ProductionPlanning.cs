using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace;

public sealed class ProductionGoal : IExposable
{
    public ThingDef Product;
    public int Target, Completed;
    public void ExposeData()
    {
        Scribe_Defs.Look(ref Product, "product"); Scribe_Values.Look(ref Target, "target"); Scribe_Values.Look(ref Completed, "completed");
    }
}
public sealed class ProductionTicket : IExposable
{
    public Bill_Production Bill;
    public bool PausedByPlanner;
    public void ExposeData() { Scribe_References.Look(ref Bill, "bill"); Scribe_Values.Look(ref PausedByPlanner, "pausedByPlanner"); }
}

public sealed class MapComponent_ProductionPlanning : MapComponent
{
    private static readonly string[] Families = { "HD_MillGeneralMachinePart", "HD_MakePressComponent", "HD_MakeShaft", "HD_RollUniformSteelPlate", "HD_MakeBasicElectricalComponents", "HD_MakeBasicElectronicComponents" };
    private List<ProductionGoal> goals = new();
    private List<ProductionTicket> tickets = new();
    public bool Enabled = true;
    public MapComponent_ProductionPlanning(Map map) : base(map) { }
    public bool HasDesk => map.listerBuildings.allBuildingsColonist.Any(b => b is Building_ProductionPlanningDesk && b.Spawned);
    public IReadOnlyList<ProductionGoal> Goals { get { EnsureGoals(); return goals; } }
    public IReadOnlyList<ProductionTicket> Tickets => tickets;
    private void EnsureGoals()
    {
        foreach (string name in Families)
        {
            ThingDef product = DefDatabase<RecipeDef>.GetNamed(name).ProducedThingDef;
            if (!goals.Any(g => g.Product == product)) goals.Add(new ProductionGoal { Product = product });
        }
    }
    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving) { EnsureGoals(); Prune(); }
        Scribe_Values.Look(ref Enabled, "productionPlanningEnabled", true);
        Scribe_Collections.Look(ref goals, "productionGoals", LookMode.Deep);
        Scribe_Collections.Look(ref tickets, "productionTickets", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) { goals ??= new(); tickets ??= new(); }
    }
    private IEnumerable<Building_WorkTable> Tables => map.listerBuildings.allBuildingsColonist.OfType<Building_WorkTable>();
    private IEnumerable<Thing> StockItems()
    {
        foreach (Thing t in map.listerThings.AllThings)
            if (t.def.category == ThingCategory.Item && !t.IsForbidden(Faction.OfPlayer)) yield return t;
        foreach (Pawn p in map.mapPawns.AllPawnsSpawned.Where(p => p.Faction == Faction.OfPlayer))
        {
            if (p.inventory != null) foreach (Thing t in p.inventory.innerContainer) yield return t;
            if (p.carryTracker?.CarriedThing != null) yield return p.carryTracker.CarriedThing;
        }
    }
    public int Stock(ThingDef product) => StockItems().Where(t => t.def == product).Sum(t => t.stackCount);
    private static int Iterations(Bill_Production bill)
    {
        if (bill.suspended || bill.DeletedOrDereferenced) return 0;
        if (bill.repeatMode == BillRepeatModeDefOf.Forever) return 100000;
        if (bill.repeatMode == BillRepeatModeDefOf.TargetCount)
            return (int)Math.Ceiling(Math.Max(0, bill.targetCount - bill.recipe.WorkerCounter.CountProducts(bill)) / (double)bill.recipe.products[0].count);
        return Math.Max(0, bill.repeatCount);
    }
    public int Pending(ThingDef product)
    {
        return Tables.SelectMany(t => t.BillStack.Bills).OfType<Bill_Production>()
            .Where(b => b.recipe.ProducedThingDef == product && b.recipe.products.Count == 1)
            .Sum(b => Iterations(b) * b.recipe.products[0].count);
    }
    private static bool Valid(ProductionTicket ticket) => ticket.Bill != null && !ticket.Bill.DeletedOrDereferenced;
    private void Prune() => tickets.RemoveAll(t => !Valid(t));
    private bool Started(Bill_Production bill) => (bill is Bill_ProductionWithUft u && u.BoundUft != null)
        || map.mapPawns.AllPawnsSpawned.Any(p => p.CurJob?.bill == bill);
    private static Dictionary<ThingDef, int> Inputs(RecipeDef recipe, Bill bill = null)
    {
        var result = new Dictionary<ThingDef, int>();
        foreach (IngredientCount ingredient in recipe.ingredients)
        {
            if (!ingredient.IsFixedIngredient) return null;
            var def = ingredient.FixedIngredient;
            result.TryGetValue(def, out int n); result[def] = n + ingredient.CountRequiredOfFor(def, recipe, bill);
        }
        return result;
    }
    public override void MapComponentTick() { if (Find.TickManager.TicksGame % 250 == 0) Refresh(); }
    public void Refresh()
    {
        EnsureGoals(); Prune();
        bool running = Enabled && HasDesk;
        foreach (var ticket in tickets.ToList())
        {
            var bill = ticket.Bill;
            if (bill.repeatCount <= 0) { bill.billStack.Delete(bill); tickets.Remove(ticket); continue; }
            if (!running && !bill.suspended) { bill.suspended = true; ticket.PausedByPlanner = true; }
            else if (running && ticket.PausedByPlanner) { bill.suspended = false; ticket.PausedByPlanner = false; }
            var goal = goals.Single(g => g.Product == bill.recipe.ProducedThingDef);
            int pending = Pending(goal.Product) + (bill.suspended ? bill.repeatCount * bill.recipe.products[0].count : 0);
            // A batch that a pawn already started keeps its native unfinished item.
            if (!Started(bill) && goal.Target < Stock(goal.Product) + pending)
            { bill.billStack.Delete(bill); tickets.Remove(ticket); continue; }
        }
        if (!running) return;
        var tables = Tables.OrderBy(t => t.thingIDNumber).ToList();
        var available = StockItems().GroupBy(t => t.def).ToDictionary(g => g.Key, g => g.Sum(t => t.stackCount));
        // Budget existing orders first. Native pawn reservations remain authoritative.
        foreach (var bill in tables.SelectMany(t => t.BillStack.Bills).OfType<Bill_Production>())
        {
            int iterations = Iterations(bill);
            if (bill.repeatMode == BillRepeatModeDefOf.Forever) iterations = Math.Min(1, iterations);
            if (bill is Bill_ProductionWithUft u && u.BoundUft != null) iterations = Math.Max(0, iterations - 1);
            var inputs = Inputs(bill.recipe, bill); if (inputs == null) continue;
            foreach (var input in inputs) { available.TryGetValue(input.Key, out int n); available[input.Key] = Math.Max(0, n - input.Value * Math.Min(iterations, 100000)); }
        }
        for (int i = 0; i < goals.Count; i++)
        {
            var goal = goals[i]; goal.Target = Math.Clamp(goal.Target, 0, 10000);
            int shortage = goal.Target - Stock(goal.Product) - Pending(goal.Product);
            if (shortage <= 0 || tickets.Any(t => t.Bill.recipe.ProducedThingDef == goal.Product)) continue;
            foreach (int batch in new[] { 10, 5, 1 })
            {
                if (batch > shortage) continue;
                RecipeDef recipe = DefDatabase<RecipeDef>.GetNamed(Families[i] + (batch == 1 ? "" : "Bulk" + batch));
                var inputs = Inputs(recipe);
                if (!recipe.AvailableNow || inputs == null || inputs.Any(x => !available.TryGetValue(x.Key, out int n) || n < x.Value)) continue;
                var table = tables.Where(t => t.def.AllRecipes.Contains(recipe) && recipe.AvailableOnNow(t) && t.CurrentlyUsableForBills() && t.BillStack.Count < 15)
                    .OrderBy(t => t.BillStack.Bills.Count(b => b.ShouldDoNow()))
                    .ThenByDescending(t => t.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor)).FirstOrDefault();
                if (table == null) continue;
                var bill = (Bill_Production)recipe.MakeNewBill(); bill.repeatMode = BillRepeatModeDefOf.RepeatCount; bill.repeatCount = 1;
                bill.RenamableLabel = "HD_ProductionPlanning_Bill".Translate(recipe.LabelCap);
                bill.SetStoreMode(BillStoreModeDefOf.BestStockpile, null);
                table.BillStack.AddBill(bill); tickets.Add(new ProductionTicket { Bill = bill });
                foreach (var input in inputs) available[input.Key] -= input.Value;
                break;
            }
        }
    }
    public void RecordCompletion(Bill_Production bill)
    {
        if (!tickets.Any(t => t.Bill == bill)) return;
        var goal = goals.FirstOrDefault(g => g.Product == bill.recipe.ProducedThingDef);
        if (goal != null) goal.Completed += bill.recipe.products[0].count;
    }
}

[HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.Notify_IterationCompleted))]
public static class Patch_ProductionPlanningCompletion
{
    [HarmonyPostfix]
    public static void Postfix(Bill_Production __instance) => __instance.Map?.GetComponent<MapComponent_ProductionPlanning>().RecordCompletion(__instance);
}

public sealed class Building_ProductionPlanningDesk : Building
{
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var gizmo in base.GetGizmos()) yield return gizmo;
        if (Faction != Faction.OfPlayer) yield break;
        yield return new Command_Action { defaultLabel = "HD_ProductionPlanning_Open".Translate(), defaultDesc = "HD_ProductionPlanning_Description".Translate(),
            action = () => Find.WindowStack.Add(new Dialog_ProductionPlanning(Map.GetComponent<MapComponent_ProductionPlanning>())) };
    }
}
