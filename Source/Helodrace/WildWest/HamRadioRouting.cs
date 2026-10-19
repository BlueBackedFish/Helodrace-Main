using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    public static class HamRadioUtility
    {
        public static bool IsHamFaction(ICommunicable target)
            => target is Faction faction && HamRadioRoutingRules.IsHamFaction(faction.def?.defName);

        public static DiaNode CreateDialog(Pawn negotiator, Faction faction)
            => HamRadioDialog.Create(faction.Name,
                () => ("HD_HAM_Chatter" + Rand.RangeInclusive(1, 6)).Translate(),
                () => negotiator?.Map?.GetComponent<MapComponent_HamRadio>().TryReceiveMission(negotiator, faction)
                    ?? "HD_HAM_NoMission".Translate());
    }

    // Keep the vanilla contact option, icon, goodwill, comms job and negotiation
    // window. Only replace the dialogue content for LOW and HIGH.
    [HarmonyPatch(typeof(FactionDialogMaker), nameof(FactionDialogMaker.FactionDialogFor))]
    public static class Patch_HamFactionDialogue
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn negotiator, Faction faction, ref DiaNode __result)
        {
            if (!HamRadioUtility.IsHamFaction(faction)) return true;
            __result = HamRadioUtility.CreateDialog(negotiator, faction);
            return false;
        }
    }
}
