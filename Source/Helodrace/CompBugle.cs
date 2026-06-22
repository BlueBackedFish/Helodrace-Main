using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace
{
    public class CompProperties_Bugle : CompProperties
    {
        public float radius = 18f;
        public int cooldownTicks = 30000;
        public HediffDef hediff;
        public string soundDef = "HD_BugleCharge";

        public CompProperties_Bugle()
        {
            compClass = typeof(CompBugle);
        }
    }

    public class CompBugle : ThingComp
    {
        private int nextUseTick;

        public CompProperties_Bugle Props => (CompProperties_Bugle)props;

        private Pawn Wearer
        {
            get
            {
                if (parent.ParentHolder is Pawn_ApparelTracker apparelTracker)
                {
                    return apparelTracker.pawn;
                }

                return null;
            }
        }

        private int TicksUntilReady => nextUseTick - Find.TickManager.TicksGame;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextUseTick, "nextUseTick", 0);
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn wearer = Wearer;
            if (wearer == null || wearer.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel = "HD_Bugle_Command_Label".Translate(),
                defaultDesc = "HD_Bugle_Command_Desc".Translate(Props.radius.ToString("F0")),
                icon = ContentFinder<Texture2D>.Get(parent.def.graphicData.texPath, true),
                action = SoundCharge
            };

            if (TicksUntilReady > 0)
            {
                command.Disable("HD_Bugle_Command_Cooldown".Translate(TicksUntilReady.ToStringTicksToPeriod()));
            }

            yield return command;
        }

        private void SoundCharge()
        {
            Pawn wearer = Wearer;
            if (wearer?.Map == null || Props.hediff == null || TicksUntilReady > 0)
            {
                return;
            }

            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail(Props.soundDef);
            sound?.PlayOneShot(new TargetInfo(wearer.Position, wearer.Map));

            int affected = 0;
            foreach (Pawn pawn in GenRadial.RadialDistinctThingsAround(wearer.Position, wearer.Map, Props.radius, true).OfType<Pawn>())
            {
                if (!CanReceiveCommand(wearer, pawn))
                {
                    continue;
                }

                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }

                Hediff hediff = HediffMaker.MakeHediff(Props.hediff, pawn);
                hediff.Severity = 1f;
                pawn.health.AddHediff(hediff);
                affected++;
            }

            nextUseTick = Find.TickManager.TicksGame + Props.cooldownTicks;
            Messages.Message("HD_Bugle_Command_Message".Translate(wearer.LabelShort, affected), wearer, MessageTypeDefOf.PositiveEvent);
        }

        private bool CanReceiveCommand(Pawn wearer, Pawn pawn)
        {
            return pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Faction == wearer.Faction
                && pawn.RaceProps.Humanlike;
        }
    }
}
