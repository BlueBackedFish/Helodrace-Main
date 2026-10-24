using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class Building_Radio : Building
    {
        public bool CanUseNow => Spawned && Faction == Faction.OfPlayer
            && this.TryGetComp<CompPowerTrader>()?.PowerOn == true
            && !Map.gameConditionManager.ElectricityDisabled(Map);

        // These are local HAM operators, not faction leaders or orbital traders.
        // Their channels are available around the clock, independently of telegraph lines.
        public static bool IsChannelAvailable(Faction faction)
            => faction != null && !faction.IsPlayer && !faction.defeated
                && HamRadioUtility.IsHamFaction(faction);

        public static IEnumerable<Faction> Channels
            => Find.FactionManager.AllFactionsVisibleInViewOrder.Where(IsChannelAvailable);

        public string UseFailure(Pawn pawn, bool chat)
        {
            if (!CanUseNow) return "HD_Radio_NoPower".Translate();
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Hearing))
                return "HD_Radio_NoHearing".Translate();
            if (chat && !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return "HD_Radio_NoTalking".Translate();
            if (!pawn.CanReach(this, PathEndMode.InteractionCell, Danger.Deadly))
                return "NoPath".Translate();
            if (!pawn.CanReserve(this) || !pawn.CanReserveSittableOrSpot(InteractionCell))
                return "Reserved".Translate();
            return null;
        }

        public Job MakeRadioJob(JobDef jobDef, Faction channel = null)
        {
            Job job = JobMaker.MakeJob(jobDef, this, InteractionCell);
            job.commTarget = channel;
            return job;
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(pawn)) yield return option;
            if (pawn?.Faction != Faction.OfPlayer || Faction != Faction.OfPlayer) yield break;

            string listen = "HD_Radio_Listen".Translate();
            string failure = UseFailure(pawn, false);
            yield return new FloatMenuOption(failure == null ? listen : listen + ": " + failure,
                failure == null ? (System.Action)(() => pawn.jobs.TryTakeOrderedJob(
                    MakeRadioJob(DefDatabase<JobDef>.GetNamed("HD_ListenRadio")))) : null);

            string chatFailure = UseFailure(pawn, true);
            bool anyChannel = false;
            foreach (Faction channel in Channels)
            {
                anyChannel = true;
                // Match vanilla's faction icon, relationship and signed goodwill display.
                string label = "HD_Radio_Channel".Translate(channel.Name) + " ("
                    + channel.PlayerRelationKind.GetLabelCap() + ", " + channel.PlayerGoodwill.ToStringWithSign() + ")";
                if (chatFailure != null) label += ": " + chatFailure;
                yield return new FloatMenuOption(label,
                    chatFailure == null ? (System.Action)(() => pawn.jobs.TryTakeOrderedJob(
                        MakeRadioJob(DefDatabase<JobDef>.GetNamed("HD_ChatOnRadio"), channel))) : null,
                    channel.def.FactionIcon, channel.Color);
            }
            if (!anyChannel) yield return new FloatMenuOption("HD_Radio_NoChannels".Translate(), null);
        }
    }

    public class JoyGiver_Radio : JoyGiver_InteractBuildingInteractionCell
    {
        protected virtual bool IsChat => false;

        protected override bool CanInteractWith(Pawn pawn, Thing thing, bool inBed)
            => thing is Building_Radio radio && radio.CanUseNow
                && (!IsChat || Building_Radio.Channels.Any())
                && base.CanInteractWith(pawn, thing, inBed);

        protected override Job TryGivePlayJob(Pawn pawn, Thing thing)
        {
            if (!(thing is Building_Radio radio) || radio.UseFailure(pawn, IsChat) != null) return null;
            Job job = base.TryGivePlayJob(pawn, thing);
            if (job != null && IsChat)
            {
                if (!Building_Radio.Channels.TryRandomElement(out Faction channel)) return null;
                job.commTarget = channel;
            }
            return job;
        }
    }

    public sealed class JoyGiver_RadioChat : JoyGiver_Radio
    {
        protected override bool IsChat => true;
    }
}
