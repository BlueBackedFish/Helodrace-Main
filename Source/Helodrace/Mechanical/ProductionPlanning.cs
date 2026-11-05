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
    // The goal this bill serves; a recipe may make other products as well.
    public ThingDef Product;
    public bool PausedByPlanner;
    public int Output => OutputOf(Bill.recipe, Product);
    public static int OutputOf(RecipeDef recipe, ThingDef product)
    {
        foreach (ThingDefCountClass p in recipe.products) if (p.thingDef == product) return p.count;
        return 0;
    }
    public void ExposeData()
    {
        Scribe_References.Look(ref Bill, "bill"); Scribe_Defs.Look(ref Product, "product"); Scribe_Values.Look(ref PausedByPlanner, "pausedByPlanner");
    }
}

public sealed class MapComponent_ProductionPlanning : MapComponent
{
    public const int MaxTarget = 10000;
    private List<ProductionGoal> goals = new();
    private readonly Dictionary<ThingDef, ProductionGoal> goalIndex = new();
    private List<ProductionTicket> tickets = new();
    public bool Enabled = true;
    // Map counts are rebuilt in one pass and reused by every goal, window row and refresh.
    private readonly Dictionary<ThingDef, int> stock = new(), pending = new();
    private int countedTick = -1;
    private double countedRealtime;
    public MapComponent_ProductionPlanning(Map map) : base(map) { }
    private static ThingDef deskDef;
    public bool HasDesk => map.listerBuildings.ColonistsHaveBuilding(deskDef ??= DefDatabase<ThingDef>.GetNamed("HD_ProductionPlanningDesk"));
    public IReadOnlyList<ProductionGoal> Goals => goals;
    public IReadOnlyList<ProductionTicket> Tickets => tickets;
    public ProductionGoal GoalFor(ThingDef product)
    {
        if (goalIndex.TryGetValue(product, out var goal)) return goal;
        goal = new ProductionGoal { Product = product }; goals.Add(goal); goalIndex[product] = goal; return goal;
    }
    public bool TryGetGoal(ThingDef product, out ProductionGoal goal) => goalIndex.TryGetValue(product, out goal);
    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving) { Prune(); goals.RemoveAll(g => g.Target <= 0 && g.Completed <= 0); RebuildIndex(); }
        Scribe_Values.Look(ref Enabled, "productionPlanningEnabled", true);
        Scribe_Collections.Look(ref goals, "productionGoals", LookMode.Deep);
        Scribe_Collections.Look(ref tickets, "productionTickets", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) { goals ??= new(); tickets ??= new(); goals.RemoveAll(g => g.Product == null); RebuildIndex(); }
    }
    private void RebuildIndex() { goalIndex.Clear(); foreach (var goal in goals) goalIndex[goal.Product] = goal; }

    public void InvalidateCounts() => countedTick = -1;
    private void EnsureCounts()
    {
        int tick = Find.TickManager.TicksGame; double now = Waylith.Time.RealtimeSinceStartupAsDouble;
        if (countedTick >= 0 && (tick == countedTick ? now - countedRealtime < 2 : tick - countedTick < 60 && now - countedRealtime < 0.5)) return;
        countedTick = tick; countedRealtime = now; stock.Clear(); pending.Clear();
        foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            if (t.def.category == ThingCategory.Item && !t.IsForbidden(Faction.OfPlayer)) Add(stock, t.def, t.stackCount);
        foreach (Pawn p in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
        {
            if (p.inventory != null) foreach (Thing t in p.inventory.innerContainer) Add(stock, t.def, t.stackCount);
            if (p.carryTracker?.CarriedThing is Thing carried) Add(stock, carried.def, carried.stackCount);
        }
        foreach (Building building in map.listerBuildings.allBuildingsColonist)
            if (building is Building_WorkTable table)
                foreach (Bill bill in table.BillStack.Bills)
                    if (bill is Bill_Production production)
                    {
                        int iterations = Iterations(production);
                        foreach (ThingDefCountClass product in production.recipe.products) Add(pending, product.thingDef, iterations * product.count);
                    }
    }
    private static void Add(Dictionary<ThingDef, int> counts, ThingDef def, int n) { counts.TryGetValue(def, out int old); counts[def] = old + n; }
    private void AddBill(Bill_Production bill, int iterations) { foreach (ThingDefCountClass p in bill.recipe.products) Add(pending, p.thingDef, iterations * p.count); }
    public int Stock(ThingDef product) { EnsureCounts(); return stock.TryGetValue(product, out int n) ? n : 0; }
    public int Pending(ThingDef product) { EnsureCounts(); return pending.TryGetValue(product, out int n) ? n : 0; }

    private static int Iterations(Bill_Production bill)
    {
        if (bill.suspended || bill.DeletedOrDereferenced) return 0;
        if (bill.repeatMode == BillRepeatModeDefOf.Forever) return 100000;
        if (bill.repeatMode == BillRepeatModeDefOf.TargetCount)
            return (int)Math.Ceiling(Math.Max(0, bill.targetCount - bill.recipe.WorkerCounter.CountProducts(bill)) / (double)Math.Max(1, bill.recipe.products.Count > 0 ? bill.recipe.products[0].count : 1));
        return Math.Max(0, bill.repeatCount);
    }
    private static bool Valid(ProductionTicket ticket) => ticket.Bill != null && !ticket.Bill.DeletedOrDereferenced && ticket.Product != null;
    private void Prune() => tickets.RemoveAll(t => !Valid(t));
    private bool Started(Bill_Production bill) => (bill is Bill_ProductionWithUft u && u.BoundUft != null)
        || map.mapPawns.AllPawnsSpawned.Any(p => p.CurJob?.bill == bill);

    // Deducts `times` batches of the bill's materials from `available`. A fixed ingredient uses its thing;
    // a filtered ingredient uses the single allowed thing with the most stock. With `partial`, reserves what exists.
    private static bool Reserve(Bill bill, Dictionary<ThingDef, int> available, int times, bool partial)
    {
        if (times <= 0) return true;
        var taken = new Dictionary<ThingDef, int>();
        int Free(ThingDef d) => (available.TryGetValue(d, out int n) ? n : 0) - (taken.TryGetValue(d, out int t) ? t : 0);
        foreach (IngredientCount ingredient in bill.recipe.ingredients)
        {
            ThingDef best = null; int need = 0;
            if (ingredient.IsFixedIngredient) { best = ingredient.FixedIngredient; need = ingredient.CountRequiredOfFor(best, bill.recipe, bill) * times; }
            else
                foreach (ThingDef def in available.Keys)
                {
                    if (!ingredient.filter.Allows(def) || !bill.ingredientFilter.Allows(def)) continue;
                    int n = ingredient.CountRequiredOfFor(def, bill.recipe, bill) * times;
                    if (best == null || Free(def) - n > Free(best) - need) { best = def; need = n; }
                }
            if (best == null) { if (partial) continue; return false; }
            if (Free(best) < need && !partial) return false;
            taken[best] = (taken.TryGetValue(best, out int already) ? already : 0) + Math.Min(need, Math.Max(0, Free(best)));
        }
        foreach (var t in taken) available[t.Key] = Math.Max(0, (available.TryGetValue(t.Key, out int n) ? n : 0) - t.Value);
        return true;
    }

    private static Building_WorkTable FindTable(ProductionRecipe recipe, List<Building_WorkTable> tables) =>
        tables.Where(t => recipe.Users.Contains(t.def) && recipe.Recipe.AvailableOnNow(t) && t.CurrentlyUsableForBills() && t.BillStack.Count < 15)
            .OrderBy(t => t.BillStack.Bills.Count(b => b.ShouldDoNow()))
            .ThenByDescending(t => t.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor)).FirstOrDefault();
    private static bool Queueable(ProductionRecipe recipe, List<Building_WorkTable> tables) => recipe.Recipe.AvailableNow && FindTable(recipe, tables) != null;

    public override void MapComponentTick()
    {
        if (Find.TickManager.TicksGame % 250 != 0 || tickets.Count == 0 && !goals.Any(g => g.Target > 0)) return;
        Refresh();
    }
    public void Refresh()
    {
        Prune(); InvalidateCounts(); EnsureCounts();
        bool running = Enabled && HasDesk;
        var tables = map.listerBuildings.allBuildingsColonist.OfType<Building_WorkTable>().OrderBy(t => t.thingIDNumber).ToList();
        foreach (var ticket in tickets.ToList())
        {
            var bill = ticket.Bill; ThingDef product = ticket.Product; int contribution = bill.repeatCount * ticket.Output;
            if (bill.repeatCount <= 0) { bill.billStack.Delete(bill); tickets.Remove(ticket); continue; }
            if (!running && !bill.suspended) { bill.suspended = true; ticket.PausedByPlanner = true; AddBill(bill, -bill.repeatCount); }
            else if (running && ticket.PausedByPlanner) { bill.suspended = false; ticket.PausedByPlanner = false; AddBill(bill, bill.repeatCount); }
            var goal = GoalFor(product);
            int surplus = Stock(product) + Pending(product) + (bill.suspended ? contribution : 0) - goal.Target;
            // A batch that a pawn already started keeps its native unfinished item. An unstarted batch is replaced
            // when it is no longer needed, or when a smaller queueable batch would overshoot less.
            if (Started(bill) || surplus <= 0) continue;
            if (surplus < contribution && !ProductionCatalog.For(product).Plannable.Any(r => r.Output < ticket.Output && Queueable(r, tables))) continue;
            if (!bill.suspended) AddBill(bill, -bill.repeatCount);
            bill.billStack.Delete(bill); tickets.Remove(ticket);
        }
        if (!running) return;
        var available = new Dictionary<ThingDef, int>(stock);
        // Budget existing orders first. Native pawn reservations remain authoritative.
        foreach (var table in tables)
            foreach (Bill b in table.BillStack.Bills)
            {
                if (b is not Bill_Production bill) continue;
                int iterations = Iterations(bill);
                if (bill.repeatMode == BillRepeatModeDefOf.Forever) iterations = Math.Min(1, iterations);
                if (bill is Bill_ProductionWithUft u && u.BoundUft != null) iterations = Math.Max(0, iterations - 1);
                Reserve(bill, available, Math.Min(iterations, 100000), partial: true);
            }
        var owned = tickets.Select(t => t.Product).ToHashSet();
        foreach (var goal in goals)
        {
            goal.Target = Math.Clamp(goal.Target, 0, MaxTarget);
            if (goal.Target <= 0 || owned.Contains(goal.Product)) continue;
            int shortage = goal.Target - Stock(goal.Product) - Pending(goal.Product);
            if (shortage <= 0) continue;
            var candidates = ProductionCatalog.For(goal.Product).Plannable;
            // Largest batch that fits the deficit; only when every batch is larger, the smallest one.
            var order = candidates.Where(r => r.Output <= shortage).ToList();
            if (order.Count == 0) order = candidates.OrderBy(r => r.Output).ToList();
            foreach (var recipe in order)
            {
                if (!recipe.Recipe.AvailableNow) continue;
                var table = FindTable(recipe, tables);
                if (table == null) continue;
                var bill = (Bill_Production)recipe.Recipe.MakeNewBill();
                if (!Reserve(bill, available, 1, partial: false)) continue;
                bill.repeatMode = BillRepeatModeDefOf.RepeatCount; bill.repeatCount = 1;
                bill.RenamableLabel = "HD_ProductionPlanning_Bill".Translate(recipe.Recipe.LabelCap);
                bill.SetStoreMode(BillStoreModeDefOf.BestStockpile, null);
                table.BillStack.AddBill(bill); tickets.Add(new ProductionTicket { Bill = bill, Product = goal.Product }); owned.Add(goal.Product);
                AddBill(bill, 1);
                break;
            }
        }
    }
    public void RecordCompletion(Bill_Production bill)
    {
        var ticket = tickets.FirstOrDefault(t => t.Bill == bill);
        if (ticket == null) return;
        GoalFor(ticket.Product).Completed += ticket.Output;
        InvalidateCounts();
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
