using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace;

// On a RecipeDef: the production planner never queues this recipe automatically (for example
// emergency conversions that consume finished vanilla parts or weapons).
public sealed class ProductionPlanningExcluded : DefModExtension { }

public sealed class ProductionIngredient
{
    // Fixed ingredient, or null when the filter allows several things.
    public ThingDef Def;
    public float Count;
    public string Label;
    // Allowed things for a non-fixed ingredient, in label order.
    public List<ThingDef> Options = new();
}

public sealed class ProductionRecipe
{
    public RecipeDef Recipe;
    public int Output;
    public bool Plannable;
    public List<ProductionIngredient> Ingredients = new();
    public HashSet<ThingDef> Users = new();
    public string Workbenches, Facilities;
}

public sealed class ProductionItem
{
    public ThingDef Def;
    public string Category;
    public List<ProductionRecipe> Recipes = new();
    // Automatic candidates, largest batch first.
    public List<ProductionRecipe> Plannable = new();
    public bool Raw => Recipes.Count == 0;
}

// Def-derived production data. Built once on first use; nothing here depends on a map.
public static class ProductionCatalog
{
    public const int MaxOptions = 12;
    private static List<ProductionItem> helod;
    private static Dictionary<ThingDef, List<RecipeDef>> producers;
    private static readonly Dictionary<ThingDef, ProductionItem> items = new();
    private static readonly Dictionary<(RecipeDef, ThingDef), ProductionRecipe> recipes = new();

    // Storable Helod items. Turret-mounted guns (destroyed on drop) and transient things with no
    // storage category, such as thrown clay pigeons, are not goods and are left out.
    public static bool IsHelodItem(ThingDef def) => def.defName.StartsWith("HD_") && def.category == ThingCategory.Item
        && !def.IsCorpse && !def.isUnfinishedThing && !def.destroyOnDrop && !def.thingCategories.NullOrEmpty()
        && !typeof(MinifiedThing).IsAssignableFrom(def.thingClass);

    // Every Helod item, grouped by first category then label.
    public static IReadOnlyList<ProductionItem> HelodItems
    {
        get
        {
            if (helod != null) return helod;
            helod = DefDatabase<ThingDef>.AllDefsListForReading.Where(IsHelodItem).Select(For)
                .OrderBy(i => i.Category).ThenBy(i => i.Def.label).ThenBy(i => i.Def.defName).ToList();
            return helod;
        }
    }

    public static ProductionItem For(ThingDef def)
    {
        if (items.TryGetValue(def, out var item)) return item;
        if (producers == null)
        {
            producers = new();
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.products.NullOrEmpty() || !recipe.AllRecipeUsers.Any()) continue;
                foreach (ThingDefCountClass product in recipe.products)
                {
                    if (!producers.TryGetValue(product.thingDef, out var list)) producers[product.thingDef] = list = new();
                    if (!list.Contains(recipe)) list.Add(recipe);
                }
            }
        }
        item = new ProductionItem { Def = def, Category = def.FirstThingCategory?.LabelCap.Resolve() ?? "HD_ProductionPlanning_Uncategorized".Translate().Resolve() };
        items[def] = item;
        if (producers.TryGetValue(def, out var made))
            foreach (RecipeDef recipe in made.OrderBy(r => r.products.First(p => p.thingDef == def).count).ThenBy(r => r.defName))
                item.Recipes.Add(Recipe(recipe, def));
        item.Plannable = item.Recipes.Where(r => r.Plannable).OrderByDescending(r => r.Output).ThenBy(r => r.Recipe.defName).ToList();
        return item;
    }

    private static ProductionRecipe Recipe(RecipeDef recipe, ThingDef product)
    {
        if (recipes.TryGetValue((recipe, product), out var info)) return info;
        var users = recipe.AllRecipeUsers.Distinct().ToList();
        info = new ProductionRecipe
        {
            Recipe = recipe, Output = recipe.products.First(p => p.thingDef == product).count,
            Plannable = recipe.products.Count == 1 && !recipe.HasModExtension<ProductionPlanningExcluded>(),
            Users = users.ToHashSet(), Workbenches = string.Join(", ", users.Select(u => u.LabelCap.Resolve())),
            Facilities = LinkedFacilityRecipes.RequiredFacilitiesText(recipe)
        };
        foreach (IngredientCount ingredient in recipe.ingredients)
        {
            if (ingredient.IsFixedIngredient)
            {
                ThingDef def = ingredient.FixedIngredient;
                info.Ingredients.Add(new ProductionIngredient { Def = def, Count = ingredient.CountRequiredOfFor(def, recipe), Label = def.LabelCap });
                continue;
            }
            var options = ingredient.filter.AllowedThingDefs.Where(d => recipe.fixedIngredientFilter == null || recipe.fixedIngredientFilter.Allows(d))
                .OrderBy(d => d.label).ToList();
            info.Ingredients.Add(new ProductionIngredient { Count = ingredient.GetBaseCount(), Label = ingredient.filter.Summary, Options = options });
        }
        recipes[(recipe, product)] = info;
        return info;
    }
}
