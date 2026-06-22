using RimWorld;
using Verse;

namespace Helodrace
{
    public class CompProperties_MoltenIronCooling : CompProperties
    {
        public int ticksToCool = GenDate.TicksPerDay / 2;
        public ThingDef product;
        public int productCount = 1;

        public CompProperties_MoltenIronCooling()
        {
            compClass = typeof(CompMoltenIronCooling);
        }
    }

    public class CompMoltenIronCooling : ThingComp
    {
        private int ticksUntilCool;

        public CompProperties_MoltenIronCooling Props => (CompProperties_MoltenIronCooling)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (ticksUntilCool <= 0)
            {
                ticksUntilCool = Props.ticksToCool;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksUntilCool, "ticksUntilCool", 0);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned || Props.product == null)
            {
                return;
            }

            ticksUntilCool -= GenTicks.TickRareInterval;
            if (ticksUntilCool <= 0)
            {
                CoolIntoProduct();
            }
        }

        public override string CompInspectStringExtra()
        {
            int remainingTicks = ticksUntilCool > 0 ? ticksUntilCool : Props.ticksToCool;
            return "HD_MoltenIronCoolingTimeRemaining".Translate(remainingTicks.ToStringTicksToPeriod());
        }

        private void CoolIntoProduct()
        {
            Map map = parent.Map;
            IntVec3 position = parent.Position;
            Thing product = ThingMaker.MakeThing(Props.product);
            product.stackCount = Props.productCount;

            parent.Destroy(DestroyMode.Vanish);
            GenPlace.TryPlaceThing(product, position, map, ThingPlaceMode.Near);
        }
    }
}
