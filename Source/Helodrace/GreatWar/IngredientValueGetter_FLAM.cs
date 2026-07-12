using RimWorld;
using Verse;

namespace Helodrace
{
    public class IngredientValueGetter_FLAM : IngredientValueGetter
    {
        public override string BillRequirementsDescription(RecipeDef recipe, IngredientCount ingredient)
        {
            return ingredient.GetBaseCount().ToString();
        }

        public override float ValuePerUnitOf(ThingDef thingDef)
        {
            if (thingDef == ThingDefOf.Steel)
            {
                return 1f;
            }

            return thingDef.GetStatValueAbstract(StatDefOf.Nutrition);
        }
    }
}
