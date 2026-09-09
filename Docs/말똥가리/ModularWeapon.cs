using Verse;

namespace LGModularWeapons
{
    // Thing class for modular weapons.
    //
    // WHY THIS EXISTS: Adaptive Storage Framework draws stored items through its own
    // PrintData pipeline. Its fast path (OptimizedPrintData) prints a single plane straight
    // from the item's Material and never calls Thing.Print or Graphic.Print, so ThingComp
    // .PostPrintOnto never runs and every attachment vanished while the weapon sat in a
    // storage unit or display rack.
    //
    // ASF picks that fast path only for Thing subclasses that do NOT override Print or Draw
    // (OptimizedPrintData.CompatibleThingTypes is built from
    // "WithThingSubclassesNotOverridingPrintOrDraw"). Overriding Print here takes this weapon
    // out of the fast path, so ASF falls through to UnsupportedThingPrintData, which calls
    // thing.Print(layer) - the normal route that reaches our comp.
    //
    // Behaviour is otherwise identical to ThingWithComps, and this needs no reference to ASF:
    // it is just a type shape that the framework's own compatibility test respects.
    public class ModularWeapon : ThingWithComps
    {
        public override void Print(SectionLayer layer)
        {
            base.Print(layer);
        }
    }
}
