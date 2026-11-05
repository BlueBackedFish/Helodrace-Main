using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Vector2 = System.Numerics.Vector2;
using Rect = Waylith.Math.Rect;
using Color = Waylith.Math.Color;

namespace Helodrace;

// Left: Helod resources and intermediates with stock, pending, planner-made count and target. Right: the
// selected item's production flowchart. Inputs flow left to right into recipe boxes and on to the product
// at the right edge: raw material -> recipe -> intermediate -> recipe -> product. Def data comes from
// ProductionCatalog; the chart is laid out only when it changes and only visible boxes and lines are drawn.
public sealed class Dialog_ProductionPlanning : Window
{
    private const float RowHeight = 28, ListWidth = 520;
    private const float BoxWidth = 220, BoxHeight = 60, Lane = 72, Gap = 56, Pad = 16, Badge = 16, OpenWidth = 40, OpenHeight = 16;
    private const int MaxDepth = 24, MaxNodes = 1500;
    private static readonly Color Muted = new(0.65f, 0.65f, 0.65f, 1), RawColor = new(0.55f, 0.8f, 0.55f, 1),
        CycleColor = new(0.95f, 0.6f, 0.35f, 1), ManualColor = new(0.85f, 0.75f, 0.45f, 1), RootColor = new(0.95f, 0.82f, 0.4f, 1),
        ItemFill = new(0.15f, 0.17f, 0.19f, 1), ItemBorder = new(0.45f, 0.5f, 0.55f, 1),
        RecipeFill = new(0.12f, 0.16f, 0.23f, 1), RecipeBorder = new(0.4f, 0.55f, 0.8f, 1),
        OptionsFill = new(0.19f, 0.17f, 0.12f, 1), OptionsBorder = new(0.6f, 0.52f, 0.35f, 1),
        Wire = new(0.6f, 0.66f, 0.72f, 0.85f);

    private enum Kind { Category, Item, Recipe, Options, More }
    private sealed class Row { public Kind Kind; public string Key; public ProductionItem Item; public float Y; }
    private sealed class Node
    {
        public Kind Kind; public int Depth; public string Key; public ThingDef Def; public ProductionItem Item;
        public ProductionRecipe Recipe; public ProductionIngredient Ingredient; public float Amount; public int Extra;
        public bool Expandable, Cycle; public Node Parent; public readonly List<Node> Inputs = new(); public Rect Box;
    }
    private enum Expansion { Keep, Primary, All }

    private readonly MapComponent_ProductionPlanning plan;
    // Edit text per item with the target it was typed for; a target changed elsewhere resets the text.
    private readonly Dictionary<(ThingDef, bool), (int Value, string Text)> buffers = new();
    private readonly HashSet<string> collapsedCategories = new(), expanded = new();
    private readonly List<Row> listRows = new();
    private readonly List<Node> nodes = new();
    private string search = string.Empty, listKey;
    private bool onlyPlanned, chartDirty = true, chartTruncated, scrollToProduct, pressing;
    private Expansion pendingExpansion = Expansion.Primary;
    private double listBuilt;
    private float listHeight, dragDistance;
    private Vector2 listScroll, chartScroll, chartSize;
    public ThingDef Selected { get; private set; }

    public Dialog_ProductionPlanning(MapComponent_ProductionPlanning plan)
    {
        this.plan = plan; doCloseX = true; forcePause = true; absorbInputAroundWindow = true;
        var first = plan.Goals.FirstOrDefault(g => g.Target > 0 && ProductionCatalog.IsPlanMaterial(g.Product))?.Product
            ?? ProductionCatalog.PlanItems.FirstOrDefault(i => i.Plannable.Count > 0)?.Def;
        if (first != null) Select(first);
    }
    public override UnityEngine.Vector2 InitialSize => new Vector2(Math.Min(1500, UI.screenWidth - 40), Math.Min(860, UI.screenHeight - 40)).ToGame();
    public int VisibleNodes { get { EnsureChart(); return nodes.Count; } }

    // Opens an item with its primary route (first recipe of every item) laid out down to raw materials.
    public void Select(ThingDef def)
    {
        Selected = def; expanded.Clear(); pendingExpansion = Expansion.Primary; chartDirty = true; scrollToProduct = true;
    }
    public void ExpandAll() { pendingExpansion = Expansion.All; chartDirty = true; }
    public void CollapseAll() { if (Selected != null) { expanded.Clear(); expanded.Add(Selected.defName); pendingExpansion = Expansion.Keep; chartDirty = true; } }
    public void Toggle(string key)
    {
        if (!expanded.Remove(key)) expanded.Add(key);
        pendingExpansion = Expansion.Keep; chartDirty = true;
    }

    public override void DoWindowContents(UnityEngine.Rect inRect)
    {
        Rect rect = inRect.ToNative();
        var font = Text.Font; var anchor = Text.Anchor; bool wrap = Text.WordWrap;
        try
        {
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(rect.x, rect.y, rect.width, 36).ToGame(), "HD_ProductionPlanning_Title".Translate());
            Text.Font = GameFont.Tiny; GUI(Muted);
            Widgets.Label(new Rect(rect.x, rect.y + 36, rect.width - 40, 34).ToGame(), "HD_ProductionPlanning_Description".Translate());
            GUI(Color.white); Text.Font = GameFont.Small;
            float y = rect.y + 74;
            Widgets.CheckboxLabeled(new Rect(rect.x, y, 240, 28).ToGame(), "HD_ProductionPlanning_Enabled".Translate(), ref plan.Enabled);
            Widgets.CheckboxLabeled(new Rect(rect.x + 256, y, 210, 28).ToGame(), "HD_ProductionPlanning_OnlyPlanned".Translate(), ref onlyPlanned);
            Widgets.Label(new Rect(rect.x + ListWidth + 16, y + 2, 70, 28).ToGame(), "HD_ProductionPlanning_Search".Translate());
            search = Widgets.TextField(new Rect(rect.x + ListWidth + 90, y, 240, 28).ToGame(), search ?? string.Empty);
            float bodyTop = y + 36, bodyBottom = rect.yMax - 40;
            DrawList(new Rect(rect.x, bodyTop, ListWidth, bodyBottom - bodyTop));
            DrawChart(new Rect(rect.x + ListWidth + 16, bodyTop, rect.width - ListWidth - 16, bodyBottom - bodyTop));
            Text.Font = GameFont.Tiny; GUI(Muted); Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
            Widgets.Label(new Rect(rect.x, bodyBottom + 4, rect.width, 36).ToGame(), "HD_ProductionPlanning_Status".Translate());
        }
        finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI(Color.white); }
    }
    private static void GUI(Color color) => UnityEngine.GUI.color = color.ToGame();

    // ---------------- Item list ----------------

    private void BuildList()
    {
        double now = Waylith.Time.RealtimeSinceStartupAsDouble;
        string key = search + "\u0001" + onlyPlanned + "\u0001" + string.Join(",", collapsedCategories);
        if (key == listKey && (!onlyPlanned || now - listBuilt < 0.5)) return;
        listKey = key; listBuilt = now; listRows.Clear();
        string category = null; float y = 0;
        foreach (var item in ProductionCatalog.PlanItems)
        {
            if (search.Length > 0 && item.Def.label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                && item.Def.defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (onlyPlanned && !(plan.TryGetGoal(item.Def, out var goal) && (goal.Target > 0 || goal.Completed > 0))) continue;
            if (item.Category != category)
            {
                category = item.Category;
                listRows.Add(new Row { Kind = Kind.Category, Key = category, Y = y }); y += RowHeight;
            }
            if (collapsedCategories.Contains(category)) continue;
            listRows.Add(new Row { Kind = Kind.Item, Item = item, Y = y }); y += RowHeight;
        }
        listHeight = y;
    }

    private void DrawList(Rect area)
    {
        BuildList();
        Text.Font = GameFont.Tiny; GUI(Muted); Text.Anchor = UnityEngine.TextAnchor.MiddleLeft;
        float x = area.x;
        Widgets.Label(new Rect(x + 30, area.y, 170, 22).ToGame(), "HD_ProductionPlanning_Item".Translate());
        Text.Anchor = UnityEngine.TextAnchor.MiddleRight;
        Widgets.Label(new Rect(x + 190, area.y, 56, 22).ToGame(), "HD_ProductionPlanning_Stock".Translate());
        Widgets.Label(new Rect(x + 248, area.y, 56, 22).ToGame(), "HD_ProductionPlanning_Pending".Translate());
        Widgets.Label(new Rect(x + 306, area.y, 46, 22).ToGame(), "HD_ProductionPlanning_Made".Translate());
        Text.Anchor = UnityEngine.TextAnchor.MiddleCenter;
        Rect targetHead = new(x + 356, area.y, 64, 22), makeHead = new(x + 424, area.y, 64, 22);
        Widgets.Label(targetHead.ToGame(), "HD_ProductionPlanning_Target".Translate());
        Widgets.Label(makeHead.ToGame(), "HD_ProductionPlanning_Make".Translate());
        TooltipHandler.TipRegion(targetHead.ToGame(), "HD_ProductionPlanning_TargetTip".Translate());
        TooltipHandler.TipRegion(makeHead.ToGame(), "HD_ProductionPlanning_MakeTip".Translate());
        GUI(Color.white); Text.Font = GameFont.Small;
        Rect outRect = new(area.x, area.y + 24, area.width, area.height - 24);
        Rect view = new(0, 0, outRect.width - 16, Math.Max(listHeight, outRect.height));
        Widgets.DrawMenuSection(outRect.ToGame());
        UnityEngine.Vector2 scroll = listScroll.ToGame();
        Widgets.BeginScrollView(outRect.ToGame(), ref scroll, view.ToGame());
        listScroll = scroll.ToNative();
        int first = Math.Max(0, (int)(listScroll.Y / RowHeight) - 1);
        for (int i = first; i < listRows.Count && listRows[i].Y < listScroll.Y + outRect.height; i++) DrawListRow(listRows[i], view.width);
        Widgets.EndScrollView();
        Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
    }

    private void DrawListRow(Row row, float width)
    {
        Rect rect = new(0, row.Y, width, RowHeight);
        Text.Anchor = UnityEngine.TextAnchor.MiddleLeft;
        if (row.Kind == Kind.Category)
        {
            Widgets.DrawLightHighlight(rect.ToGame());
            bool collapsed = collapsedCategories.Contains(row.Key);
            Widgets.Label(new Rect(6, row.Y, width - 6, RowHeight).ToGame(), (collapsed ? "▶ " : "▼ ") + row.Key);
            if (Widgets.ButtonInvisible(rect.ToGame())) { if (!collapsed) collapsedCategories.Add(row.Key); else collapsedCategories.Remove(row.Key); }
            return;
        }
        ThingDef def = row.Item.Def;
        if (def == Selected) Widgets.DrawHighlightSelected(rect.ToGame()); else Widgets.DrawHighlightIfMouseover(rect.ToGame());
        Widgets.ThingIcon(new Rect(4, row.Y + 2, 24, 24).ToGame(), def);
        Widgets.Label(new Rect(32, row.Y, 156, RowHeight).ToGame(), def.LabelCap.Resolve().Truncate(156));
        plan.TryGetGoal(def, out var goal);
        Text.Anchor = UnityEngine.TextAnchor.MiddleRight;
        Widgets.Label(new Rect(190, row.Y, 56, RowHeight).ToGame(), plan.Stock(def).ToString());
        Rect pendingRect = new(248, row.Y, 56, RowHeight);
        Widgets.Label(pendingRect.ToGame(), PendingText(def));
        if (plan.PendingIsEndless(def)) TooltipHandler.TipRegion(pendingRect.ToGame(), "HD_ProductionPlanning_EndlessTip".Translate());
        Widgets.Label(new Rect(306, row.Y, 46, RowHeight).ToGame(), (goal?.Completed ?? 0).ToString());
        if (Widgets.ButtonInvisible(new Rect(0, row.Y, 354, RowHeight).ToGame())) Select(def);
        Rect target = new(358, row.Y + 2, 60, RowHeight - 4), make = new(426, row.Y + 2, 60, RowHeight - 4);
        if (row.Item.Plannable.Count == 0)
        {
            GUI(row.Item.Raw ? RawColor : Muted); Text.Anchor = UnityEngine.TextAnchor.MiddleCenter;
            Rect both = new(358, row.Y, 128, RowHeight);
            Widgets.Label(both.ToGame(), "—");
            TooltipHandler.TipRegion(both.ToGame(), (row.Item.Raw ? "HD_ProductionPlanning_RawTip" : "HD_ProductionPlanning_NotPlannable").Translate());
            GUI(Color.white); return;
        }
        int targetValue = NumberField(def, false, target, goal?.Target ?? 0);
        if (targetValue != (goal?.Target ?? 0)) plan.GoalFor(def).Target = targetValue;
        int makeValue = NumberField(def, true, make, goal?.Quantity ?? 0);
        if (makeValue != (goal?.Quantity ?? 0)) plan.GoalFor(def).Quantity = makeValue;
    }

    private int NumberField(ThingDef def, bool quantity, Rect rect, int value)
    {
        string buffer = buffers.TryGetValue((def, quantity), out var edit) && edit.Value == value ? edit.Text : null;
        Widgets.TextFieldNumeric(rect.ToGame(), ref value, ref buffer, 0, MapComponent_ProductionPlanning.MaxTarget);
        buffers[(def, quantity)] = (value, buffer);
        TooltipHandler.TipRegion(rect.ToGame(), (quantity ? "HD_ProductionPlanning_MakeTip" : "HD_ProductionPlanning_TargetTip").Translate());
        return value;
    }

    private string PendingText(ThingDef def) => plan.PendingIsEndless(def) ? "∞" : plan.Pending(def).ToString();

    // ---------------- Flowchart ----------------

    private void EnsureChart()
    {
        if (!chartDirty) return;
        nodes.Clear(); chartTruncated = false;
        if (Selected != null)
        {
            var root = AddItem(Selected, 1, 0, Selected.defName, null, new HashSet<ThingDef>(), pendingExpansion);
            int slot = 0, deepest = 0;
            Place(root, ref slot, ref deepest);
            float step = BoxWidth + Gap;
            foreach (var node in nodes)
                node.Box = new Rect(Pad + (deepest - node.Depth) * step, node.Box.y, BoxWidth, node.Kind == Kind.More ? 28 : BoxHeight);
            chartSize = new Vector2(Pad * 2 + (deepest + 1) * step - Gap, Pad * 2 + slot * Lane);
        }
        pendingExpansion = Expansion.Keep; chartDirty = false;
    }

    // Leaves take the next lane; a box sits level with the middle of its inputs.
    private static void Place(Node node, ref int slot, ref int deepest)
    {
        deepest = Math.Max(deepest, node.Depth);
        float y;
        if (node.Inputs.Count == 0) y = Pad + slot++ * Lane;
        else
        {
            foreach (var input in node.Inputs) Place(input, ref slot, ref deepest);
            y = (node.Inputs[0].Box.y + node.Inputs[^1].Box.y) / 2;
        }
        node.Box = new Rect(0, y, BoxWidth, BoxHeight);
    }

    private Node Add(Node node, Node parent)
    {
        if (nodes.Count >= MaxNodes) { chartTruncated = true; return null; }
        node.Parent = parent; parent?.Inputs.Add(node); nodes.Add(node); return node;
    }

    private Node AddItem(ThingDef def, float amount, int depth, string key, Node parent, HashSet<ThingDef> ancestors, Expansion mode)
    {
        var item = ProductionCatalog.For(def);
        bool cycle = ancestors.Contains(def);
        var node = Add(new Node { Kind = Kind.Item, Def = def, Item = item, Amount = amount, Depth = depth, Key = key, Cycle = cycle,
            Expandable = !cycle && !item.Raw && depth + 2 <= MaxDepth }, parent);
        if (node == null || !node.Expandable) return node;
        if (mode != Expansion.Keep) expanded.Add(key);
        if (!expanded.Contains(key)) return node;
        ancestors.Add(def);
        for (int r = 0; r < item.Recipes.Count; r++)
        {
            var recipe = item.Recipes[r];
            string recipeKey = key + "|" + recipe.Recipe.defName; float batches = amount / recipe.Output;
            var recipeNode = Add(new Node { Kind = Kind.Recipe, Recipe = recipe, Def = def, Amount = batches, Depth = depth + 1, Key = recipeKey,
                Expandable = recipe.Ingredients.Count > 0 }, node);
            if (recipeNode == null) break;
            if (mode == Expansion.All || mode == Expansion.Primary && r == 0) expanded.Add(recipeKey);
            if (!expanded.Contains(recipeKey)) continue;
            // An alternative recipe opened by hand shows its inputs, but their own routes stay folded.
            Expansion inner = mode == Expansion.All || mode == Expansion.Primary && r == 0 ? mode : Expansion.Keep;
            foreach (var ingredient in recipe.Ingredients)
            {
                float need = batches * ingredient.Count;
                if (ingredient.Def != null) { AddItem(ingredient.Def, need, depth + 2, recipeKey + "/" + ingredient.Def.defName, recipeNode, ancestors, inner); continue; }
                string optionsKey = recipeKey + "/" + ingredient.Label;
                var options = Add(new Node { Kind = Kind.Options, Ingredient = ingredient, Amount = need, Depth = depth + 2, Key = optionsKey,
                    Expandable = ingredient.Options.Count > 0 && depth + 3 <= MaxDepth }, recipeNode);
                // Alternatives of a broad filter are never opened automatically.
                if (options == null || !options.Expandable || !expanded.Contains(optionsKey)) continue;
                foreach (ThingDef option in ingredient.Options.Take(ProductionCatalog.MaxOptions))
                    AddItem(option, need, depth + 3, optionsKey + "/" + option.defName, options, ancestors, Expansion.Keep);
                if (ingredient.Options.Count > ProductionCatalog.MaxOptions)
                    Add(new Node { Kind = Kind.More, Extra = ingredient.Options.Count - ProductionCatalog.MaxOptions, Depth = depth + 3 }, options);
            }
        }
        ancestors.Remove(def);
        return node;
    }

    private void DrawChart(Rect area)
    {
        if (Selected == null) { Widgets.Label(area.ToGame(), "HD_ProductionPlanning_SelectItem".Translate()); return; }
        EnsureChart();
        var item = ProductionCatalog.For(Selected);
        Widgets.ThingIcon(new Rect(area.x, area.y, 44, 44).ToGame(), Selected);
        Text.Font = GameFont.Medium; Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
        Widgets.Label(new Rect(area.x + 52, area.y - 2, area.width - 300, 32).ToGame(), Selected.LabelCap.Resolve().Truncate(area.width - 300));
        Text.Font = GameFont.Tiny; GUI(Muted);
        plan.TryGetGoal(Selected, out var goal);
        Widgets.Label(new Rect(area.x + 52, area.y + 28, area.width - 300, 18).ToGame(), "HD_ProductionPlanning_Summary".Translate(plan.Stock(Selected), PendingText(Selected), goal?.Target ?? 0, item.Recipes.Count, goal?.Quantity ?? 0));
        GUI(Color.white); Text.Font = GameFont.Small;
        if (Widgets.ButtonText(new Rect(area.xMax - 240, area.y + 4, 116, 30).ToGame(), "HD_ProductionPlanning_ExpandAll".Translate())) ExpandAll();
        if (Widgets.ButtonText(new Rect(area.xMax - 118, area.y + 4, 116, 30).ToGame(), "HD_ProductionPlanning_CollapseAll".Translate())) CollapseAll();
        EnsureChart();
        Text.Font = GameFont.Tiny; GUI(Muted);
        Widgets.Label(new Rect(area.x, area.y + 48, area.width, 18).ToGame(), chartTruncated ? "HD_ProductionPlanning_TreeLimit".Translate(MaxNodes) : "HD_ProductionPlanning_PerUnit".Translate());
        GUI(Color.white); Text.Font = GameFont.Small;

        Rect outRect = new(area.x, area.y + 68, area.width, area.height - 68);
        Rect view = new(0, 0, Math.Max(chartSize.X, outRect.width - 16), Math.Max(chartSize.Y, outRect.height - 16));
        if (scrollToProduct && Event.Repaint)
        {
            // The product sits at the right edge, level with the middle of the chart.
            var root = nodes.Count > 0 ? nodes[0].Box : default;
            chartScroll = new Vector2(Math.Max(0, view.width - (outRect.width - 16)), Math.Clamp(root.y + BoxHeight / 2 - outRect.height / 2, 0, Math.Max(0, view.height - outRect.height + 16)));
            scrollToProduct = false;
        }
        HandleChartInput(outRect, view);
        Widgets.DrawMenuSection(outRect.ToGame());
        UnityEngine.Vector2 scroll = chartScroll.ToGame();
        Widgets.BeginScrollView(outRect.ToGame(), ref scroll, view.ToGame(), showScrollbars: true);
        chartScroll = scroll.ToNative();
        Rect visible = new(chartScroll.X - Gap, chartScroll.Y - Lane, outRect.width + Gap * 2, outRect.height + Lane * 2);
        foreach (var node in nodes)
            if (node.Parent != null && (Overlaps(node.Box, visible) || Overlaps(node.Parent.Box, visible))) DrawWire(node.Box, node.Parent.Box);
        foreach (var node in nodes)
            if (Overlaps(node.Box, visible)) DrawNode(node);
        Widgets.EndScrollView();
        Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
    }

    private static bool Overlaps(Rect a, Rect b) => a.x < b.xMax && a.xMax > b.x && a.y < b.yMax && a.yMax > b.y;

    // Orthogonal connector from an input's right edge into its consumer's left edge, with an arrowhead.
    private static void DrawWire(Rect from, Rect to)
    {
        var color = Wire.ToGame();
        float y1 = from.y + from.height / 2, y2 = to.y + to.height / 2, mid = from.xMax + Gap / 2;
        Widgets.DrawBoxSolid(new Rect(from.xMax, y1 - 1, mid - from.xMax + 1, 2).ToGame(), color);
        Widgets.DrawBoxSolid(new Rect(mid - 1, Math.Min(y1, y2) - 1, 2, Math.Abs(y2 - y1) + 2).ToGame(), color);
        Widgets.DrawBoxSolid(new Rect(mid, y2 - 1, to.x - mid - 6, 2).ToGame(), color);
        for (int i = 0; i < 6; i++) Widgets.DrawBoxSolid(new Rect(to.x - 6 + i, y2 - 5 + i * 5f / 6, 1, 10 - i * 10f / 6).ToGame(), color);
    }

    private void DrawNode(Node node)
    {
        Rect box = node.Box;
        Color fill = node.Kind == Kind.Recipe ? RecipeFill : node.Kind is Kind.Options or Kind.More ? OptionsFill : ItemFill;
        Color border = node.Kind switch
        {
            Kind.Recipe => node.Recipe.Plannable ? RecipeBorder : ManualColor,
            Kind.Options or Kind.More => OptionsBorder,
            _ => node.Depth == 0 ? RootColor : node.Cycle ? CycleColor : node.Item.Raw ? RawColor : ItemBorder
        };
        Widgets.DrawBoxSolidWithOutline(box.ToGame(), fill.ToGame(), border.ToGame(), node.Depth == 0 ? 2 : 1);
        Widgets.DrawHighlightIfMouseover(box.ToGame());
        Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
        float x = box.x + 6, right = box.xMax - 6;
        switch (node.Kind)
        {
            case Kind.Item:
            {
                Widgets.ThingIcon(new Rect(x, box.y + 14, 32, 32).ToGame(), node.Def); x += 38;
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(x, box.y + 4, right - x, 24).ToGame(), node.Def.LabelCap.Resolve().Truncate(right - x));
                Text.Font = GameFont.Tiny; GUI(Muted);
                Widgets.Label(new Rect(x, box.y + 26, right - x, 16).ToGame(), ("×" + Amount(node.Amount) + "  ·  " + "HD_ProductionPlanning_StockShort".Translate(plan.Stock(node.Def)).Resolve()).Truncate(right - x));
                if (node.Cycle || node.Item.Raw)
                {
                    GUI(node.Cycle ? CycleColor : RawColor);
                    Widgets.Label(new Rect(x, box.y + 41, right - x - OpenWidth, 16).ToGame(), (node.Cycle ? "HD_ProductionPlanning_Cycle" : "HD_ProductionPlanning_Raw").Translate());
                    TooltipHandler.TipRegion(box.ToGame(), (node.Cycle ? "HD_ProductionPlanning_CycleTip" : "HD_ProductionPlanning_RawTip").Translate());
                }
                GUI(Color.white);
                if (node.Depth > 0)
                {
                    Rect open = OpenButton(box);
                    Widgets.DrawBoxSolidWithOutline(open.ToGame(), new Color(0.25f, 0.27f, 0.3f, 1).ToGame(), ItemBorder.ToGame());
                    Text.Anchor = UnityEngine.TextAnchor.MiddleCenter;
                    Widgets.Label(open.ToGame(), "HD_ProductionPlanning_Focus".Translate());
                    TooltipHandler.TipRegion(open.ToGame(), "HD_ProductionPlanning_FocusTip".Translate(node.Def.LabelCap));
                }
                break;
            }
            case Kind.Recipe:
            {
                var recipe = node.Recipe;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(x, box.y + 2, right - x, 16).ToGame(), recipe.Recipe.LabelCap.Resolve().Truncate(right - x));
                GUI(Muted);
                Widgets.Label(new Rect(x, box.y + 16, right - x, 16).ToGame(), "HD_ProductionPlanning_Batches".Translate(recipe.Output, Amount(node.Amount)).Resolve().Truncate(right - x));
                Widgets.Label(new Rect(x, box.y + 30, right - x, 16).ToGame(), "HD_ProductionPlanning_Workbench".Translate(recipe.Workbenches).Resolve().Truncate(right - x));
                Widgets.Label(new Rect(x, box.y + 44, right - x, 16).ToGame(), "HD_ProductionPlanning_Facilities".Translate(recipe.Facilities).Resolve().Truncate(right - x));
                GUI(Color.white);
                string tip = recipe.Recipe.LabelCap.Resolve() + "\n" + "HD_ProductionPlanning_Batches".Translate(recipe.Output, Amount(node.Amount)).Resolve()
                    + "\n" + "HD_ProductionPlanning_Workbench".Translate(recipe.Workbenches).Resolve() + "\n" + "HD_ProductionPlanning_Facilities".Translate(recipe.Facilities).Resolve();
                if (!recipe.Plannable) tip += "\n" + "HD_ProductionPlanning_ManualOnlyTip".Translate().Resolve();
                TooltipHandler.TipRegion(box.ToGame(), tip);
                break;
            }
            case Kind.Options:
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(x, box.y + 6, right - x, 32).ToGame(), "HD_ProductionPlanning_AnyOf".Translate(node.Ingredient.Label).Resolve());
                GUI(Muted); Widgets.Label(new Rect(x, box.y + 40, right - x, 16).ToGame(), "×" + Amount(node.Amount)); GUI(Color.white);
                break;
            case Kind.More:
                Text.Font = GameFont.Tiny; GUI(Muted);
                Widgets.Label(new Rect(x, box.y + 6, right - x, 18).ToGame(), "HD_ProductionPlanning_More".Translate(node.Extra)); GUI(Color.white);
                break;
        }
        if (node.Expandable)
        {
            // Inputs enter from the left, so the fold badge sits on the left edge.
            Rect badge = BadgeRect(box);
            Widgets.DrawBoxSolidWithOutline(badge.ToGame(), border.ToGame(), fill.ToGame());
            Text.Font = GameFont.Small; Text.Anchor = UnityEngine.TextAnchor.MiddleCenter; GUI(new Color(0.05f, 0.05f, 0.05f, 1));
            Widgets.Label(badge.ToGame(), expanded.Contains(node.Key) ? "−" : "+");
            GUI(Color.white);
        }
        Text.Font = GameFont.Small; Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
    }
    private static Rect BadgeRect(Rect box) => new(box.x - Badge / 2, box.y + box.height / 2 - Badge / 2, Badge, Badge);
    private static Rect OpenButton(Rect box) => new(box.xMax - OpenWidth - 4, box.yMax - OpenHeight - 3, OpenWidth, OpenHeight);

    // Drag pans the chart; a click without drag folds a box or opens an input item.
    private void HandleChartInput(Rect outRect, Rect view)
    {
        var e = UnityEngine.Event.current;
        Rect content = new(outRect.x, outRect.y, outRect.width - 16, outRect.height - 16);
        Vector2 mouse = e.mousePosition.ToNative();
        bool inside = content.Contains(mouse);
        if (e.type == UnityEngine.EventType.MouseDown && e.button == 0 && inside) { pressing = true; dragDistance = 0; e.Use(); }
        else if (e.type == UnityEngine.EventType.MouseDrag && pressing)
        {
            Vector2 delta = e.delta.ToNative();
            dragDistance += Math.Abs(delta.X) + Math.Abs(delta.Y);
            chartScroll = new Vector2(Math.Clamp(chartScroll.X - delta.X, 0, Math.Max(0, view.width - content.width)),
                Math.Clamp(chartScroll.Y - delta.Y, 0, Math.Max(0, view.height - content.height)));
            e.Use();
        }
        else if (e.type == UnityEngine.EventType.MouseUp && pressing)
        {
            pressing = false; e.Use();
            if (dragDistance < 4 && inside) Click(mouse - new Vector2(outRect.x, outRect.y) + chartScroll);
        }
    }

    private void Click(Vector2 point)
    {
        foreach (var node in nodes)
        {
            if (node.Kind == Kind.Item && node.Depth > 0 && OpenButton(node.Box).Contains(point)) { Select(node.Def); return; }
            Rect hit = new(node.Box.x - Badge / 2, node.Box.y, node.Box.width + Badge / 2, node.Box.height);
            if (node.Expandable && hit.Contains(point)) { Toggle(node.Key); return; }
        }
    }

    private static class Event { public static bool Repaint => UnityEngine.Event.current.type == UnityEngine.EventType.Repaint; }
    private static string Amount(float value) => value.ToString(value >= 100 ? "0" : "0.##");

    public override void PostClose() { base.PostClose(); plan.Refresh(); }
}
