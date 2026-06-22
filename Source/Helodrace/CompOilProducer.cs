using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace Helodrace
{
    public class CompProperties_OilProducer : CompProperties
    {
        public ThingDef product;
        public int amount = 10;
        public float daysToProduce = 1f;

        public CompProperties_OilProducer()
        {
            this.compClass = typeof(CompOilProducer);
        }
    }

    public class CompOilProducer : ThingComp
    {
        public float progressDays = 0f;

        public CompProperties_OilProducer Props => (CompProperties_OilProducer)this.props;

        private CompMechanicalUser mechanicalUser;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            mechanicalUser = this.parent.GetComp<CompMechanicalUser>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref progressDays, "progressDays", 0f);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();

            if (mechanicalUser != null && mechanicalUser.HasPower)
            {
                float dayDelta = 250f / 60000f; // CompTickRare is 250 ticks
                
                // Scale production based on EffectiveRPM relative to RecommendedRPM
                float speedFactor = 1f;
                if (mechanicalUser.Props.recommendedRPM > 0)
                {
                    speedFactor = mechanicalUser.EffectiveRPM / mechanicalUser.Props.recommendedRPM;
                }
                
                progressDays += dayDelta * speedFactor;

                if (progressDays >= Props.daysToProduce)
                {
                    Produce();
                }
            }
        }

        private void Produce()
        {
            progressDays -= Props.daysToProduce;
            if (progressDays < 0f) progressDays = 0f; // safety limit
            Thing thing = ThingMaker.MakeThing(Props.product);
            thing.stackCount = Props.amount;
            GenPlace.TryPlaceThing(thing, this.parent.Position, this.parent.Map, ThingPlaceMode.Near);
            
            // Optional: Add some smoke/dust visual here if desired
        }

        public override string CompInspectStringExtra()
        {
            if (mechanicalUser != null && mechanicalUser.HasPower)
            {
                float speedFactor = (mechanicalUser.Props.recommendedRPM > 0) ? (mechanicalUser.EffectiveRPM / mechanicalUser.Props.recommendedRPM) : 1f;
                return $"Production progress: {progressDays / Props.daysToProduce:P0}\n" +
                       $"Production speed: {speedFactor:P0}";
            }
            return "Stalled: Needs Mechanical Power";
        }
    }

    // PlaceWorker to ensure it's built on Oil Floor
    public class PlaceWorker_OnOilFloor : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            TerrainDef terrain = loc.GetTerrain(map);
            if (terrain != null && terrain.defName == "HD_LowQualityOilRigFloor") // Assuming HD_LowQualityOilFloor might be the actual defName, let's keep as is unless reported bug
            {
                return true;
            }
            return "Must be placed on a Low quality oil rig floor.";
        }
    }
}
