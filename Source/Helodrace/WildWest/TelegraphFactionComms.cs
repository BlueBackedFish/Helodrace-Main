using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    // Keep the vanilla dialogue's request costs, goodwill checks, and cooldowns.
    // Once connected, the call uses its already-consumed primary cell.
    public sealed class Dialog_TelegraphFactionComms : Dialog_NodeTree
    {
        private static readonly Func<Faction, bool> LeaderAvailable = AccessTools.MethodDelegate<Func<Faction, bool>>(
            AccessTools.Method(typeof(Faction), "LeaderIsAvailableToTalk"));
        private readonly MapComponent_TelegraphNetwork network;
        private readonly Pawn negotiator;
        private readonly Faction provider;

        public static bool CanContact(Faction faction) => faction != null && LeaderAvailable(faction);

        public Dialog_TelegraphFactionComms(MapComponent_TelegraphNetwork network, Pawn negotiator, Faction provider)
            : base(FactionDialogMaker.FactionDialogFor(negotiator, provider), false, true, provider.Name)
        {
            this.network = network;
            this.negotiator = negotiator;
            this.provider = provider;
            forcePause = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            TelegraphConnectionStatus status = network.Status();
            if (status != TelegraphConnectionStatus.Online || network.Provider != provider)
            {
                Widgets.Label(rect, network.StatusText(status));
                return;
            }
            if (negotiator.Dead || negotiator.Downed || !negotiator.Spawned
                || !negotiator.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                || negotiator.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
            {
                Widgets.Label(rect, "HD_Telegraph_OperatorUnavailable".Translate());
                return;
            }
            base.DoWindowContents(rect);
        }
    }
}
