using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Vector2 = System.Numerics.Vector2;
using Rect = Waylith.Math.Rect;
using Color = Waylith.Math.Color;

namespace Helodrace;

// Left: every Helod item with stock, pending, planner-made count and target. Right: the selected item's
// production tree (item -> alternative recipes -> ingredients -> ... -> raw resources). Def data comes from
// ProductionCatalog; both lists are flattened only when their inputs change and only visible rows are drawn.
public sealed class Dialog_ProductionPlanning : Window
{
    private const float RowHeight = 28, RecipeRowHeight = 44, Indent = 20, ListWidth = 520;
    private const int MaxDepth = 16, MaxTreeRows = 2500;
    private static readonly Color Muted = new(0.65f, 0.65f, 0.65f, 1), RawColor = new(0.55f, 0.8f, 0.55f, 1),
        CycleColor = new(0.95f, 0.6f, 0.35f, 1), ManualColor = new(0.85f, 0.75f, 0.45f, 1), Guide = new(1, 1, 1, 0.12f);

    private enum RowKind { Category, Item, Recipe, Options, More }
    private sealed class Row
    {
        public RowKind Kind; public int Depth; public string Key; public ThingDef Def; public ProductionItem Item;
        public ProductionRecipe Recipe; public ProductionIngredient Ingredient; public float Amount; public int Extra;
        public bool Expandable, Cycle; public float Y, Height;
    }

    private readonly MapComponent_ProductionPlanning plan;
    private readonly Dictionary<ThingDef, string> buffers = new();
    private readonly HashSet<string> collapsedCategories = new(), expanded = new();
    private readonly List<Row> listRows = new(), treeRows = new();
    private string search = string.Empty, listKey;
    private bool onlyPlanned, treeDirty = true, treeTruncated;
    private double listBuilt;
    private float listHeight, treeHeight;
    private Vector2 listScroll, treeScroll;
    public ThingDef Selected { get; private set; }

    public Dialog_ProductionPlanning(MapComponent_ProductionPlanning plan)
    {
        this.plan = plan; doCloseX = true; forcePause = true; absorbInputAroundWindow = true;
        var first = plan.Goals.FirstOrDefault(g => g.Target > 0)?.Product ?? ProductionCatalog.HelodItems.FirstOrDefault(i => !i.Raw)?.Def;
        if (first != null) Select(first);
    }
    public override UnityEngine.Vector2 InitialSize => new Vector2(1200, 780).ToGame();
    public int VisibleTreeRows { get { if (treeDirty) BuildTree(); return treeRows.Count; } }

    public void Select(ThingDef def)
    {
        Selected = def; expanded.Clear(); treeScroll = Vector2.Zero; treeDirty = true;
        expanded.Add(def.defName);
        var item = ProductionCatalog.For(def);
        if (item.Recipes.Count == 1) expanded.Add(def.defName + "|" + item.Recipes[0].Recipe.defName);
    }
    public void ExpandAll() { BuildTree(expandAll: true); }
    public void CollapseAll() { if (Selected != null) { expanded.Clear(); expanded.Add(Selected.defName); treeDirty = true; } }

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
            Widgets.CheckboxLabeled(new Rect(rect.x, y, 260, 28).ToGame(), "HD_ProductionPlanning_Enabled".Translate(), ref plan.Enabled);
            Widgets.CheckboxLabeled(new Rect(rect.x + 280, y, 220, 28).ToGame(), "HD_ProductionPlanning_OnlyPlanned".Translate(), ref onlyPlanned);
            Widgets.Label(new Rect(rect.x + ListWidth + 16, y + 2, 70, 28).ToGame(), "HD_ProductionPlanning_Search".Translate());
            search = Widgets.TextField(new Rect(rect.x + ListWidth + 90, y, 260, 28).ToGame(), search ?? string.Empty);
            float bodyTop = y + 36, bodyBottom = rect.yMax - 40;
            DrawList(new Rect(rect.x, bodyTop, ListWidth, bodyBottom - bodyTop));
            DrawTree(new Rect(rect.x + ListWidth + 16, bodyTop, rect.width - ListWidth - 16, bodyBottom - bodyTop));
            Text.Font = GameFont.Tiny; GUI(Muted);
            Widgets.Label(new Rect(rect.x, bodyBottom + 4, rect.width, 36).ToGame(), "HD_ProductionPlanning_Status".Translate());
        }
        finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI(Color.white); }
    }
    private static void GUI(Color color) => UnityEngine.GUI.color = color.ToGame();

    private void BuildList()
    {
        double now = Waylith.Time.RealtimeSinceStartupAsDouble;
        string key = search + "\u0001" + onlyPlanned + "\u0001" + string.Join(",", collapsedCategories);
        if (key == listKey && (!onlyPlanned || now - listBuilt < 0.5)) return;
        listKey = key; listBuilt = now; listRows.Clear();
        string category = null; float y = 0;
        foreach (var item in ProductionCatalog.HelodItems)
        {
            if (search.Length > 0 && item.Def.label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                && item.Def.defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (onlyPlanned && !(plan.TryGetGoal(item.Def, out var goal) && (goal.Target > 0 || goal.Completed > 0))) continue;
            if (item.Category != category)
            {
                category = item.Category;
                listRows.Add(new Row { Kind = RowKind.Category, Key = category, Y = y, Height = RowHeight }); y += RowHeight;
            }
            if (collapsedCategories.Contains(category)) continue;
            listRows.Add(new Row { Kind = RowKind.Item, Def = item.Def, Item = item, Y = y, Height = RowHeight }); y += RowHeight;
        }
        listHeight = y;
    }

    private void DrawList(Rect area)
    {
        BuildList();
        Text.Font = GameFont.Tiny; GUI(Muted); Text.Anchor = UnityEngine.TextAnchor.MiddleLeft;
        float x = area.x;
        Widgets.Label(new Rect(x + 30, area.y, 220, 22).ToGame(), "HD_ProductionPlanning_Item".Translate());
        Text.Anchor = UnityEngine.TextAnchor.MiddleRight;
        Widgets.Label(new Rect(x + 256, area.y, 60, 22).ToGame(), "HD_ProductionPlanning_Stock".Translate());
        Widgets.Label(new Rect(x + 318, area.y, 60, 22).ToGame(), "HD_ProductionPlanning_Pending".Translate());
        Widgets.Label(new Rect(x + 380, area.y, 50, 22).ToGame(), "HD_ProductionPlanning_Made".Translate());
        Widgets.Label(new Rect(x + 432, area.y, 66, 22).ToGame(), "HD_ProductionPlanning_Target".Translate());
        GUI(Color.white); Text.Font = GameFont.Small;
        Rect outRect = new(area.x, area.y + 24, area.width, area.height - 24);
        Rect view = new(0, 0, outRect.width - 16, Math.Max(listHeight, outRect.height));
        Widgets.DrawMenuSection(outRect.ToGame());
        UnityEngine.Vector2 scroll = listScroll.ToGame();
        Widgets.BeginScrollView(outRect.ToGame(), ref scroll, view.ToGame());
        listScroll = scroll.ToNative();
        int first = FirstVisible(listRows, listScroll.Y);
        for (int i = first; i < listRows.Count && listRows[i].Y < listScroll.Y + outRect.height; i++) DrawListRow(listRows[i], view.width);
        Widgets.EndScrollView();
        Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
    }

    private static int FirstVisible(List<Row> rows, float top)
    {
        int lo = 0, hi = rows.Count - 1;
        while (lo < hi) { int mid = (lo + hi) / 2; if (rows[mid].Y + rows[mid].Height <= top) lo = mid + 1; else hi = mid; }
        return Math.Max(0, lo);
    }

    private void DrawListRow(Row row, float width)
    {
        Rect rect = new(0, row.Y, width, row.Height);
        Text.Anchor = UnityEngine.TextAnchor.MiddleLeft;
        if (row.Kind == RowKind.Category)
        {
            Widgets.DrawLightHighlight(rect.ToGame());
            bool collapsed = collapsedCategories.Contains(row.Key);
            Widgets.Label(new Rect(6, row.Y, width - 6, row.Height).ToGame(), (collapsed ? "▶ " : "▼ ") + row.Key);
            if (Widgets.ButtonInvisible(rect.ToGame())) { if (!collapsed) collapsedCategories.Add(row.Key); else collapsedCategories.Remove(row.Key); }
            return;
        }
        ThingDef def = row.Def;
        if (def == Selected) Widgets.DrawHighlightSelected(rect.ToGame()); else Widgets.DrawHighlightIfMouseover(rect.ToGame());
        Widgets.ThingIcon(new Rect(4, row.Y + 2, 24, 24).ToGame(), def);
        Widgets.Label(new Rect(32, row.Y, 222, row.Height).ToGame(), def.LabelCap.Resolve().Truncate(222));
        plan.TryGetGoal(def, out var goal);
        Text.Anchor = UnityEngine.TextAnchor.MiddleRight;
        Widgets.Label(new Rect(256, row.Y, 60, row.Height).ToGame(), plan.Stock(def).ToString());
        Widgets.Label(new Rect(318, row.Y, 60, row.Height).ToGame(), plan.Pending(def).ToString());
        Widgets.Label(new Rect(380, row.Y, 50, row.Height).ToGame(), (goal?.Completed ?? 0).ToString());
        if (Widgets.ButtonInvisible(new Rect(0, row.Y, 432, row.Height).ToGame())) Select(def);
        Rect target = new(438, row.Y + 2, 62, row.Height - 4);
        if (row.Item.Plannable.Count == 0)
        {
            GUI(row.Item.Raw ? RawColor : Muted); Text.Anchor = UnityEngine.TextAnchor.MiddleCenter;
            Widgets.Label(target.ToGame(), "—");
            TooltipHandler.TipRegion(target.ToGame(), (row.Item.Raw ? "HD_ProductionPlanning_RawTip" : "HD_ProductionPlanning_NotPlannable").Translate());
            GUI(Color.white); return;
        }
        int value = goal?.Target ?? 0, before = value;
        buffers.TryGetValue(def, out string buffer);
        Widgets.TextFieldNumeric(target.ToGame(), ref value, ref buffer, 0, MapComponent_ProductionPlanning.MaxTarget);
        buffers[def] = buffer;
        if (value != before) plan.GoalFor(def).Target = value;
    }

    private void BuildTree(bool expandAll = false)
    {
        treeRows.Clear(); treeTruncated = false; treeHeight = 0;
        if (Selected != null) AddItem(Selected, 1, 0, Selected.defName, new HashSet<ThingDef>(), expandAll);
        treeDirty = false;
    }
    private bool Add(Row row)
    {
        if (treeRows.Count >= MaxTreeRows) { treeTruncated = true; return false; }
        row.Y = treeHeight; treeHeight += row.Height; treeRows.Add(row); return true;
    }
    private void AddItem(ThingDef def, float amount, int depth, string key, HashSet<ThingDef> ancestors, bool expandAll)
    {
        var item = ProductionCatalog.For(def);
        bool cycle = ancestors.Contains(def);
        var row = new Row { Kind = RowKind.Item, Def = def, Item = item, Amount = amount, Depth = depth, Key = key, Height = RowHeight,
            Cycle = cycle, Expandable = !cycle && !item.Raw && depth < MaxDepth };
        if (!Add(row) || !row.Expandable) return;
        if (expandAll) expanded.Add(key);
        if (!expanded.Contains(key)) return;
        ancestors.Add(def);
        foreach (var recipe in item.Recipes)
        {
            string recipeKey = key + "|" + recipe.Recipe.defName; float batches = amount / recipe.Output;
            if (!Add(new Row { Kind = RowKind.Recipe, Recipe = recipe, Def = def, Amount = batches, Depth = depth + 1, Key = recipeKey, Height = RecipeRowHeight, Expandable = recipe.Ingredients.Count > 0 })) break;
            if (expandAll) expanded.Add(recipeKey);
            if (!expanded.Contains(recipeKey)) continue;
            foreach (var ingredient in recipe.Ingredients)
            {
                float need = batches * ingredient.Count;
                if (ingredient.Def != null) { AddItem(ingredient.Def, need, depth + 2, recipeKey + "/" + ingredient.Def.defName, ancestors, expandAll); continue; }
                string optionsKey = recipeKey + "/" + ingredient.Label;
                if (!Add(new Row { Kind = RowKind.Options, Ingredient = ingredient, Amount = need, Depth = depth + 2, Key = optionsKey, Height = RowHeight, Expandable = ingredient.Options.Count > 0 })) break;
                // Alternatives are not expanded by "expand all": a broad filter would multiply the tree.
                if (!expanded.Contains(optionsKey)) continue;
                foreach (ThingDef option in ingredient.Options.Take(ProductionCatalog.MaxOptions))
                    AddItem(option, need, depth + 3, optionsKey + "/" + option.defName, ancestors, false);
                if (ingredient.Options.Count > ProductionCatalog.MaxOptions)
                    Add(new Row { Kind = RowKind.More, Extra = ingredient.Options.Count - ProductionCatalog.MaxOptions, Depth = depth + 3, Height = RowHeight });
            }
        }
        ancestors.Remove(def);
    }

    private void DrawTree(Rect area)
    {
        if (Selected == null)
        {
            Widgets.Label(area.ToGame(), "HD_ProductionPlanning_SelectItem".Translate()); return;
        }
        if (treeDirty) BuildTree();
        var item = ProductionCatalog.For(Selected);
        Widgets.ThingIcon(new Rect(area.x, area.y, 44, 44).ToGame(), Selected);
        Text.Font = GameFont.Medium; Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
        Widgets.Label(new Rect(area.x + 52, area.y - 2, area.width - 300, 32).ToGame(), Selected.LabelCap.Resolve().Truncate(area.width - 300));
        Text.Font = GameFont.Tiny; GUI(Muted);
        plan.TryGetGoal(Selected, out var goal);
        Widgets.Label(new Rect(area.x + 52, area.y + 28, area.width - 300, 18).ToGame(), "HD_ProductionPlanning_Summary".Translate(plan.Stock(Selected), plan.Pending(Selected), goal?.Target ?? 0, item.Recipes.Count));
        GUI(Color.white); Text.Font = GameFont.Small;
        if (Widgets.ButtonText(new Rect(area.xMax - 240, area.y + 4, 116, 30).ToGame(), "HD_ProductionPlanning_ExpandAll".Translate())) ExpandAll();
        if (Widgets.ButtonText(new Rect(area.xMax - 118, area.y + 4, 116, 30).ToGame(), "HD_ProductionPlanning_CollapseAll".Translate())) CollapseAll();
        Text.Font = GameFont.Tiny; GUI(Muted);
        Widgets.Label(new Rect(area.x, area.y + 48, area.width, 18).ToGame(), (treeTruncated ? "HD_ProductionPlanning_TreeLimit".Translate(MaxTreeRows) : "HD_ProductionPlanning_PerUnit".Translate()));
        GUI(Color.white); Text.Font = GameFont.Small;
        Rect outRect = new(area.x, area.y + 68, area.width, area.height - 68);
        Rect view = new(0, 0, outRect.width - 16, Math.Max(treeHeight, outRect.height));
        Widgets.DrawMenuSection(outRect.ToGame());
        UnityEngine.Vector2 scroll = treeScroll.ToGame();
        Widgets.BeginScrollView(outRect.ToGame(), ref scroll, view.ToGame());
        treeScroll = scroll.ToNative();
        int first = FirstVisible(treeRows, treeScroll.Y);
        for (int i = first; i < treeRows.Count && treeRows[i].Y < treeScroll.Y + outRect.height; i++)
            if (DrawTreeRow(treeRows[i], view.width)) break;
        Widgets.EndScrollView();
        Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
    }

    // Returns true when the click changed the tree, so the stale rows are not drawn this frame.
    private bool DrawTreeRow(Row row, float width)
    {
        float x = 6 + row.Depth * Indent;
        Rect rect = new(0, row.Y, width, row.Height);
        for (int d = 0; d < row.Depth; d++) Widgets.DrawBoxSolid(new Rect(6 + d * Indent + 7, row.Y, 1, row.Height).ToGame(), Guide.ToGame());
        Widgets.DrawHighlightIfMouseover(rect.ToGame());
        Text.Anchor = UnityEngine.TextAnchor.MiddleLeft; Text.Font = GameFont.Small;
        bool open = row.Key != null && expanded.Contains(row.Key);
        if (row.Expandable) Widgets.Label(new Rect(x, row.Y, 16, Math.Min(row.Height, RowHeight)).ToGame(), open ? "▼" : "▶");
        x += 18;
        bool changed = false;
        switch (row.Kind)
        {
            case RowKind.Item:
            {
                Widgets.ThingIcon(new Rect(x, row.Y + 2, 24, 24).ToGame(), row.Def); x += 28;
                string label = row.Def.LabelCap.Resolve() + "  ×" + Amount(row.Amount);
                Widgets.Label(new Rect(x, row.Y, width - x - 250, row.Height).ToGame(), label.Truncate(width - x - 250));
                Text.Anchor = UnityEngine.TextAnchor.MiddleRight; GUI(Muted);
                Widgets.Label(new Rect(width - 246, row.Y, 100, row.Height).ToGame(), "HD_ProductionPlanning_StockShort".Translate(plan.Stock(row.Def)));
                if (row.Cycle || row.Item.Raw)
                {
                    GUI(row.Cycle ? CycleColor : RawColor);
                    Rect tag = new(width - 144, row.Y, 76, row.Height);
                    Widgets.Label(tag.ToGame(), (row.Cycle ? "HD_ProductionPlanning_Cycle" : "HD_ProductionPlanning_Raw").Translate());
                    TooltipHandler.TipRegion(tag.ToGame(), (row.Cycle ? "HD_ProductionPlanning_CycleTip" : "HD_ProductionPlanning_RawTip").Translate());
                }
                GUI(Color.white);
                if (row.Depth > 0 && Widgets.ButtonText(new Rect(width - 64, row.Y + 3, 60, row.Height - 6).ToGame(), "HD_ProductionPlanning_Focus".Translate()))
                { Select(row.Def); return true; }
                break;
            }
            case RowKind.Recipe:
            {
                var recipe = row.Recipe;
                Widgets.Label(new Rect(x, row.Y, width - x - 120, 24).ToGame(),
                    "HD_ProductionPlanning_RecipeLine".Translate(recipe.Recipe.LabelCap, recipe.Output, Amount(row.Amount)).Resolve().Truncate(width - x - 120));
                Text.Font = GameFont.Tiny; GUI(Muted);
                string detail = "HD_ProductionPlanning_RecipeDetail".Translate(recipe.Workbenches, recipe.Facilities).Resolve();
                Widgets.Label(new Rect(x, row.Y + 22, width - x - 8, 20).ToGame(), detail.Truncate(width - x - 8));
                if (!recipe.Plannable)
                {
                    GUI(ManualColor); Text.Anchor = UnityEngine.TextAnchor.MiddleRight;
                    Widgets.Label(new Rect(width - 116, row.Y, 112, 24).ToGame(), "HD_ProductionPlanning_ManualOnly".Translate());
                }
                GUI(Color.white); Text.Font = GameFont.Small;
                string tip = recipe.Recipe.LabelCap.Resolve() + "\n" + detail;
                if (!recipe.Plannable) tip += "\n" + "HD_ProductionPlanning_ManualOnlyTip".Translate().Resolve();
                TooltipHandler.TipRegion(rect.ToGame(), tip);
                break;
            }
            case RowKind.Options:
                Widgets.Label(new Rect(x, row.Y, width - x - 8, row.Height).ToGame(),
                    "HD_ProductionPlanning_AnyOf".Translate(row.Ingredient.Label, Amount(row.Amount)).Resolve().Truncate(width - x - 8));
                break;
            case RowKind.More:
                GUI(Muted); Widgets.Label(new Rect(x, row.Y, width - x - 8, row.Height).ToGame(), "HD_ProductionPlanning_More".Translate(row.Extra)); GUI(Color.white);
                break;
        }
        if (row.Expandable && Widgets.ButtonInvisible(new Rect(0, row.Y, width - 70, row.Height).ToGame()))
        {
            if (open) expanded.Remove(row.Key);
            else
            {
                expanded.Add(row.Key);
                // A single-recipe item opens straight to its ingredients.
                if (row.Kind == RowKind.Item && row.Item.Recipes.Count == 1) expanded.Add(row.Key + "|" + row.Item.Recipes[0].Recipe.defName);
            }
            treeDirty = true; changed = true;
        }
        return changed;
    }

    private static string Amount(float value) => value.ToString(value >= 100 ? "0" : "0.##");

    public override void PostClose() { base.PostClose(); plan.Refresh(); }
}
