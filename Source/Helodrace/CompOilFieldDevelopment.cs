using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace Helodrace
{
    public class CompProperties_OilFieldDevelopment : CompProperties
    {
        public float totalDaysNeeded = 15f;
        public float mtbDaysConfirmation = 0.1f;
        public float mtbDaysMaintenance = 1f; // On average, needs work once a day
        public string floorDefName = "HD_LowQualityOilRigFloor";
        public IntVec3 floorOffset = new IntVec3(0, 0, 0);

        public CompProperties_OilFieldDevelopment()
        {
            this.compClass = typeof(CompOilFieldDevelopment);
        }
    }

    public class CompOilFieldDevelopment : ThingComp
    {
        public float currentProgressDays = 0f;
        public bool needsMaintenance = false;

        public CompProperties_OilFieldDevelopment Props => (CompProperties_OilFieldDevelopment)this.props;

        public bool IsPoweredBySteam
        {
            get
            {
                var userComp = this.parent.GetComp<CompMechanicalUser>();
                return userComp != null && userComp.HasPower;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentProgressDays, "currentProgressDays", 0f);
            Scribe_Values.Look(ref needsMaintenance, "needsMaintenance", false);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();

            if (IsPoweredBySteam && !needsMaintenance)
            {
                // Progress
                float dayDelta = 250f / 60000f;
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
                // MTBEventOccurs checkInterval is 250 for CompTickRare
                if (Rand.MTBEventOccurs(Props.mtbDaysConfirmation, 60000f, 250f))
                {
                    Messages.Message("HD_OilFieldStruckEarly".Translate(this.parent.Label), this.parent, MessageTypeDefOf.PositiveEvent);
                    FinishDevelopment();
                    return;
                }

                // Roll for Maintenance Needed (MTB 1 Day)
                if (Rand.MTBEventOccurs(Props.mtbDaysMaintenance, 60000f, 250f))
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
                this.parent.Map.terrainGrid.SetTerrain(targetPos, floor);
                Messages.Message("HD_OilFieldDevelopmentComplete".Translate(this.parent.Label), this.parent, MessageTypeDefOf.PositiveEvent);
            }
            currentProgressDays = 0;
            needsMaintenance = false;
        }

        public override string CompInspectStringExtra()
        {
            string s = $"Development Progress: {currentProgressDays:F1} / {Props.totalDaysNeeded:F0} days";
            if (needsMaintenance)
            {
                s += "\n<color=orange>Idle: Needs Maintenance Work</color>";
            }
            else if (IsPoweredBySteam)
            {
                s += "\nOperational: Drilling (Chance to strike oil!)";
            }
            else
            {
                s += "\nIdle: Needs Steam Power";
            }
            return s;
        }
    }
}
