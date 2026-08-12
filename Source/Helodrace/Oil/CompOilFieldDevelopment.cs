using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace Helodrace
{
    public class CompProperties_OilFieldDevelopment : CompProperties
    {
        public float totalDaysNeeded = 7f;
        public float mtbDaysConfirmation = 3f;
        public float mtbDaysMaintenance = 0.6f; // On average, needs work once a day
        public string floorDefName = MapComponent_OilFields.OilFieldTerrainDefName;
        public IntVec3 floorOffset = new IntVec3(0, 0, 0);
        public float minimumQuality = 0.75f;
        public float maximumQuality = 1.25f;
        public float initialPressure = 1f;

        public CompProperties_OilFieldDevelopment()
        {
            this.compClass = typeof(CompOilFieldDevelopment);
        }
    }

    public class CompOilFieldDevelopment : ThingComp
    {
        public float currentProgressDays = 0f;
        public bool needsMaintenance = false;
        public bool developmentComplete = false;
        private CompMechanicalUser mechanicalUser;

        public CompProperties_OilFieldDevelopment Props => (CompProperties_OilFieldDevelopment)this.props;

        public bool IsPoweredBySteam
        {
            get
            {
                return mechanicalUser != null && mechanicalUser.HasPower;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            mechanicalUser = parent.GetComp<CompMechanicalUser>();

            IntVec3 targetPos = parent.Position + Props.floorOffset;
            MapComponent_OilFields oilFields = parent.Map?.GetComponent<MapComponent_OilFields>();
            if (oilFields != null && oilFields.IsOilFieldTerrain(targetPos))
            {
                oilFields.FieldAtOrCreateLegacy(targetPos);
                developmentComplete = true;
                needsMaintenance = false;
                currentProgressDays = Props.totalDaysNeeded;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentProgressDays, "currentProgressDays", 0f);
            Scribe_Values.Look(ref needsMaintenance, "needsMaintenance", false);
            Scribe_Values.Look(ref developmentComplete, "developmentComplete", false);
        }

        public override void CompTick()
        {
            base.CompTick();

            // The cable-tool rig uses a Normal ticker because its explosive comp
            // requires one. ThingWithComps therefore calls CompTick, not
            // CompTickRare, so throttle the development simulation here.
            if (parent.IsHashIntervalTick(GenTicks.TickRareInterval))
            {
                TickDevelopment(GenTicks.TickRareInterval);
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            TickDevelopment(GenTicks.TickRareInterval);
        }

        private void TickDevelopment(int intervalTicks)
        {

            if (!developmentComplete && IsPoweredBySteam && !needsMaintenance)
            {
                // Progress
                float dayDelta = intervalTicks / 60000f;
                currentProgressDays += dayDelta;

                // Spawn a burst of dust particles to maintain visual feedback at rare tick rate
                if (this.parent.Map != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 loc = (this.parent.Position + Props.floorOffset).ToVector3Shifted();
                        loc += new Vector3(Rand.Range(-0.4f, 0.4f), 0, Rand.Range(-0.4f, 0.4f));
                        FleckMaker.ThrowDustPuff(loc, this.parent.Map, Rand.Range(0.8f, 1.3f));
                    }
                }

                // Roll for "Strike Oil" (Random Finish) - MTB 7 Days
                if (Rand.MTBEventOccurs(Props.mtbDaysConfirmation, 60000f, intervalTicks))
                {
                    Messages.Message("HD_OilFieldStruckEarly".Translate(this.parent.Label), this.parent, MessageTypeDefOf.PositiveEvent);
                    FinishDevelopment();
                    return;
                }

                // Roll for Maintenance Needed (MTB 1 Day)
                if (Rand.MTBEventOccurs(Props.mtbDaysMaintenance, 60000f, intervalTicks))
                {
                    needsMaintenance = true;
                    Messages.Message("HD_OilFieldNeedsMaintenance".Translate(this.parent.Label), this.parent, MessageTypeDefOf.CautionInput);
                }

                // Check for guaranteed completion (15 days)
                if (currentProgressDays >= Props.totalDaysNeeded)
                {
                    FinishDevelopment();
                }
            }
        }

        public void Notify_WorkPerformed()
        {
            needsMaintenance = false;
        }

        private void FinishDevelopment()
        {
            IntVec3 targetPos = this.parent.Position + Props.floorOffset;
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamed(Props.floorDefName, false);
            if (floor != null && targetPos.InBounds(this.parent.Map))
            {
                TerrainDef previousTerrain = targetPos.GetTerrain(parent.Map);
                if (previousTerrain != null && !previousTerrain.layerable)
                {
                    parent.Map.terrainGrid.SetUnderTerrain(targetPos, previousTerrain);
                }
                this.parent.Map.terrainGrid.SetTerrain(targetPos, floor);
                float quality = Rand.Range(Props.minimumQuality, Props.maximumQuality);
                this.parent.Map.GetComponent<MapComponent_OilFields>()
                    .CreateOrReplace(targetPos, quality, Props.initialPressure);
                Messages.Message("HD_OilFieldDevelopmentComplete".Translate(this.parent.Label), this.parent, MessageTypeDefOf.PositiveEvent);
            }
            currentProgressDays = Props.totalDaysNeeded;
            needsMaintenance = false;
            developmentComplete = true;
        }

        public override string CompInspectStringExtra()
        {
            if (developmentComplete)
            {
                return "HD_OilFieldDevelopment_CompleteInspect".Translate();
            }

            string s = "HD_OilFieldDevelopment_Progress".Translate(
                currentProgressDays.ToString("F2"),
                Props.totalDaysNeeded.ToString("F0"));
            if (needsMaintenance)
            {
                s += "\n<color=orange>" + "HD_OilFieldDevelopment_NeedsMaintenance".Translate() + "</color>";
            }
            else if (IsPoweredBySteam)
            {
                s += "\n" + "HD_OilFieldDevelopment_Drilling".Translate();
            }
            else
            {
                s += "\n" + "HD_OilFieldDevelopment_NeedsPower".Translate();
            }
            return s;
        }
    }
}
