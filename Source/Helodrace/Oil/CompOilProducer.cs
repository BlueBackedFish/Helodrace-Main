using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace Helodrace
{
    public class CompProperties_OilProducer : CompProperties
    {
        public ThingDef product;
        public int amount = 10;
        public float daysToProduce = 1f;
        public float pressureLossPerProduction = 0.01f;
        public float minimumPressure = 0.35f;

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
        private MapComponent_OilFields oilFields;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            mechanicalUser = this.parent.GetComp<CompMechanicalUser>();
            oilFields = this.parent.Map?.GetComponent<MapComponent_OilFields>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref progressDays, "progressDays", 0f);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();

            OilFieldRecord field = OilField;
            if (mechanicalUser != null && mechanicalUser.HasPower && field != null)
            {
                float dayDelta = 250f / 60000f; // CompTickRare is 250 ticks
                float speedFactor = MechanicalSpeedFactor * field.quality * field.pressure;
                
                progressDays += dayDelta * speedFactor;

                if (progressDays >= Props.daysToProduce)
                {
                    Produce(field);
                }
            }
        }

        private void Produce(OilFieldRecord field)
        {
            Thing thing = ThingMaker.MakeThing(Props.product);
            thing.stackCount = Props.amount;
            if (!GenPlace.TryPlaceThing(thing, this.parent.Position, this.parent.Map, ThingPlaceMode.Near))
            {
                thing.Destroy();
                return;
            }

            progressDays = Mathf.Max(0f, progressDays - Props.daysToProduce);
            oilFields.ReducePressure(field, Props.pressureLossPerProduction, Props.minimumPressure);
        }

        public override string CompInspectStringExtra()
        {
            OilFieldRecord field = OilField;
            if (field == null)
            {
                return "HD_OilProducer_NoField".Translate();
            }

            float speedFactor = MechanicalSpeedFactor * field.quality * field.pressure;
            string status = mechanicalUser != null && mechanicalUser.HasPower
                ? "HD_OilProducer_Operating".Translate()
                : "HD_OilProducer_NeedsPower".Translate();

            return "HD_OilProducer_Inspect".Translate(
                ToPercent(progressDays / Props.daysToProduce),
                ToPercent(speedFactor),
                ToPercent(field.quality),
                ToPercent(field.pressure),
                status);
        }

        private static string ToPercent(float value)
        {
            return (value * 100f).ToString("F0") + "%";
        }

        private OilFieldRecord OilField
        {
            get
            {
                if (oilFields == null && parent.Map != null)
                {
                    oilFields = parent.Map.GetComponent<MapComponent_OilFields>();
                }

                if (oilFields == null)
                {
                    return null;
                }

                OilFieldRecord field = oilFields.FieldAtOrCreateLegacy(parent.Position);
                if (field != null || !parent.Spawned)
                {
                    return field;
                }

                // Older saves and differently-sized drilling rigs can leave the
                // developed field under another cell of the pump's footprint.
                foreach (IntVec3 cell in parent.OccupiedRect())
                {
                    field = oilFields.FieldAtOrCreateLegacy(cell);
                    if (field != null)
                    {
                        return field;
                    }
                }

                return null;
            }
        }

        private float MechanicalSpeedFactor =>
            mechanicalUser != null && mechanicalUser.Props.recommendedRPM > 0f
                ? mechanicalUser.EffectiveRPM / mechanicalUser.Props.recommendedRPM
                : 1f;
    }

    // PlaceWorker to ensure it's built on Oil Floor
    public class PlaceWorker_OnOilFloor : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            MapComponent_OilFields oilFields = map.GetComponent<MapComponent_OilFields>();
            foreach (IntVec3 cell in GenAdj.OccupiedRect(loc, rot, checkingDef.Size))
            {
                if (oilFields.IsOilFieldTerrain(cell))
                {
                    return true;
                }
            }
            return "HD_OilProducer_MustPlaceOnField".Translate();
        }
    }
}
