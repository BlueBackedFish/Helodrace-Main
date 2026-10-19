using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Helodrace.Economy
{
    public static class FinancialGiftUtility
    {
        public static int GiftGoodwill(Faction faction)
            => GameComponent_FinancialGoodwill.Current?.Get(faction) ?? 0;
    }

    // Both caravan trading and transport-pod gifts eventually use this reason.
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class Patch_FinancialGiftReward
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, Faction other, int goodwillChange, HistoryEventDef reason, ref bool __result)
        {
            var ledger = GameComponent_FinancialGoodwill.Current;
            if (reason != HistoryEventDefOf.GaveGift || goodwillChange <= 0 || ledger == null
                || (__instance.IsPlayer == other.IsPlayer)) return true;
            if (!__instance.CanChangeGoodwillFor(other, goodwillChange))
            {
                __result = false;
                return false;
            }
            Faction recipient = __instance.IsPlayer ? other : __instance;
            int gained = ledger.AddGift(recipient, goodwillChange);
            if (gained > 0)
            {
                Find.HistoryEventsManager.RecordEvent(new HistoryEvent(HistoryEventDefOf.GaveGift,
                    recipient.Named(HistoryEventArgsNames.AffectedFaction), gained.Named(HistoryEventArgsNames.CustomGoodwill)), true);
                Messages.Message("HD_FinancialGift_Received".Translate(recipient.Name, gained, ledger.Get(recipient)), MessageTypeDefOf.PositiveEvent);
            }
            __result = true;
            return false;
        }
    }

    // Evaluate diminishing returns and cap warnings against financial reputation,
    // including previews and gifts made while normal diplomatic goodwill is 100.
    [HarmonyPatch]
    public static class Patch_FinancialGiftValuation
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(FactionGiftUtility), "PostProcessedGoodwillChange");
            foreach (MethodInfo method in typeof(FactionGiftUtility).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (method.Name == nameof(FactionGiftUtility.GiveGift)) yield return method;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.PlayerGoodwill));
            MethodInfo replacement = AccessTools.Method(typeof(FinancialGiftUtility), nameof(FinancialGiftUtility.GiftGoodwill));
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("The vanilla gift goodwill lookup no longer matches.");
        }
    }

    [HarmonyPatch(typeof(FactionGiftUtility), "SendGiftNotAppreciatedMessage")]
    public static class Patch_FinancialGiftCapMessage
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction giveTo, GlobalTargetInfo lookTarget)
        {
            Messages.Message("HD_FinancialGift_Maximum".Translate(giveTo.Name), lookTarget, MessageTypeDefOf.NeutralEvent);
            return false;
        }
    }

    [HarmonyPatch(typeof(Faction), "CheckReachNaturalGoodwill")]
    public static class Patch_FinancialNaturalRecovery
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance)
            => GameComponent_FinancialGoodwill.Current?.ReplacesVanillaRecovery(__instance) != true;
    }

    [HarmonyPatch]
    public static class Patch_FinancialGiftTradeText
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Dialog_Trade), nameof(Dialog_Trade.DoWindowContents));
            yield return AccessTools.Method(typeof(FactionGiftUtility), nameof(FactionGiftUtility.OfferGiftsCommand));
            yield return AccessTools.Method(typeof(TransportersArrivalAction_GiveGift), nameof(TransportersArrivalAction_GiveGift.GetFloatMenuOptions));
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldstr)
                {
                    switch ((string)instruction.operand)
                    {
                        case "OfferGifts":
                        case "CommandOfferGifts": instruction.operand = "HD_FinancialGift_Offer"; count++; break;
                        case "GiftModeTip": instruction.operand = "HD_FinancialGift_Tip"; count++; break;
                        case "CommandOfferGiftsDesc": instruction.operand = "HD_FinancialGift_CommandTip"; count++; break;
                        case "GiveGiftViaTransportPods": instruction.operand = "HD_FinancialGift_Pods"; count++; break;
                    }
                }
                yield return instruction;
            }
            if (count == 0) throw new InvalidOperationException("The vanilla gift description no longer matches.");
        }
    }
}
