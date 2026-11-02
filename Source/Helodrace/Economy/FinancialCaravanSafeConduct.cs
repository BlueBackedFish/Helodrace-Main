using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace.Economy
{
    public sealed class LordJob_FinancialCaravan : LordJob_TradeWithColony
    {
        private bool breached;
        public const string BreachMemo = "HD_FinancialCaravanBreach";
        public bool Protected => !breached;
        public LordJob_FinancialCaravan() { }
        public LordJob_FinancialCaravan(Faction faction, IntVec3 chillSpot) : base(faction, chillSpot) { }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref breached, "financialSafeConductBreached", false);
        }
        public override StateGraph CreateGraph()
        {
            StateGraph graph = base.CreateGraph();
            foreach (Transition transition in graph.transitions)
                for (int i = 0; i < transition.triggers.Count; i++)
                    if (transition.triggers[i] is Trigger_BecamePlayerEnemy)
                        transition.triggers[i] = new Trigger_Memo(BreachMemo);
            return graph;
        }
        public void Breach()
        {
            if (breached) return;
            breached = true;
            lord.ReceiveMemo(BreachMemo);
        }
    }

    public static class FinancialCaravanUtility
    {
        public const string Tag = "HD_FinancialCaravan";
        private const string LordTag = Tag + ":";
        public static bool IsProtected(Thing thing)
            => thing is Pawn pawn && GameComponent_FinancialGoodwill.Supports(pawn.Faction)
                && pawn.Spawned && pawn.GetLord()?.LordJob is LordJob_FinancialCaravan job && job.Protected;
        public static bool TraderFactionHostile(Faction a, Faction b, Pawn trader)
            => !IsProtected(trader) && a.HostileTo(b);

        public static void Register(Lord lord)
        {
            foreach (Pawn pawn in lord.ownedPawns)
            {
                if (pawn.questTags == null) pawn.questTags = new List<string>();
                if (!pawn.questTags.Contains(Tag)) pawn.questTags.Add(Tag);
                string cohort = LordTag + lord.loadID;
                if (!pawn.questTags.Contains(cohort)) pawn.questTags.Add(cohort);
            }
        }

        public static bool Breach(Pawn pawn)
        {
            Lord lord = pawn.Spawned ? pawn.GetLord() : null;
            if (lord == null)
            {
                string cohort = pawn.questTags?.FirstOrDefault(t => t.StartsWith(LordTag, StringComparison.Ordinal));
                Map map = pawn.Map ?? pawn.Corpse?.Map;
                if (cohort != null && map != null && int.TryParse(cohort.Substring(LordTag.Length), out int id))
                    lord = map.lordManager.lords.FirstOrDefault(l => l.loadID == id);
            }
            if (!(lord?.LordJob is LordJob_FinancialCaravan job) || !job.Protected) return false;
            job.Breach();
            return true;
        }

        public static void ClearTags(Pawn pawn)
            => pawn.questTags?.RemoveAll(t => t == Tag || t.StartsWith(LordTag, StringComparison.Ordinal));
    }

    [HarmonyPatch(typeof(FactionUtility), nameof(FactionUtility.HostileTo))]
    public static class Patch_FinancialCaravanGenerationHostility
    {
        [HarmonyPrefix]
        public static bool Prefix([HarmonyArgument(0)] Faction a, [HarmonyArgument(1)] Faction b, ref bool __result)
        {
            if (!FinancialCaravanScope.Covers(a, b)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord))]
    public static class Patch_FinancialCaravanLord
    {
        [HarmonyPrefix]
        public static void Prefix([HarmonyArgument(0)] Faction faction, [HarmonyArgument(1)] ref LordJob lordJob)
        {
            if (!FinancialCaravanScope.IsGenerating(faction) || !(lordJob is LordJob_TradeWithColony)) return;
            IntVec3 chillSpot = (IntVec3)AccessTools.Field(typeof(LordJob_TradeWithColony), "chillSpot").GetValue(lordJob);
            lordJob = new LordJob_FinancialCaravan(faction, chillSpot);
        }
        [HarmonyPostfix]
        public static void Postfix(Lord __result)
        {
            if (__result?.LordJob is LordJob_FinancialCaravan) FinancialCaravanUtility.Register(__result);
        }
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) })]
    public static class Patch_FinancialCaravanThingHostility
    {
        [HarmonyPrefix]
        public static bool Prefix([HarmonyArgument(0)] Thing a, [HarmonyArgument(1)] Thing b, ref bool __result)
        {
            // World generation queries hostility before the player faction exists.
            if (!(FinancialCaravanUtility.IsProtected(a) && b?.Faction == Faction.OfPlayer)
                && !(FinancialCaravanUtility.IsProtected(b) && a?.Faction == Faction.OfPlayer)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Faction) })]
    public static class Patch_FinancialCaravanFactionHostility
    {
        [HarmonyPrefix]
        public static bool Prefix([HarmonyArgument(0)] Thing t, [HarmonyArgument(1)] Faction fac, ref bool __result)
        {
            if (!FinancialCaravanUtility.IsProtected(t) || fac != Faction.OfPlayer) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_TraderTracker), nameof(Pawn_TraderTracker.CanTradeNow), MethodType.Getter)]
    public static class Patch_FinancialCaravanCanTrade
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(FactionUtility), nameof(FactionUtility.HostileTo));
            MethodInfo replacement = AccessTools.Method(typeof(FinancialCaravanUtility), nameof(FinancialCaravanUtility.TraderFactionHostile));
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn_TraderTracker), "pawn"));
                    instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Trader hostility check no longer matches.");
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_MemberTookDamage))]
    public static class Patch_FinancialCaravanPlayerDamage
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, [HarmonyArgument(0)] Pawn member, [HarmonyArgument(1)] DamageInfo dinfo)
        {
            if (member?.questTags?.Contains(FinancialCaravanUtility.Tag) != true || member.Faction != __instance
                || dinfo.Instigator?.Faction != Faction.OfPlayer || dinfo.Def?.ExternalViolenceFor(member) != true) return true;
            int penalty = FinancialGoodwillRules.CaravanAttackPenalty(dinfo.Amount);
            if (penalty == 0) return true;
            int financialPenalty = Faction.OfPlayer.CalculateAdjustedGoodwillChange(__instance, penalty);
            Faction.OfPlayer.TryAffectGoodwillWith(__instance, penalty, reason: HistoryEventDefOf.AttackedMember, lookTarget: member);
            GameComponent_FinancialGoodwill.Current?.Adjust(__instance, financialPenalty);
            if (FinancialCaravanUtility.Breach(member))
                Messages.Message("HD_Company_SafeConductBroken".Translate(__instance.Name, -financialPenalty), member, MessageTypeDefOf.NegativeEvent);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    public static class Patch_FinancialCaravanDeparture
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (!__instance.Dead)
            {
                FinancialCaravanUtility.ClearTags(__instance);
                GameComponent_CaravanLosses.Current?.MarkDeparture(__instance.GetUniqueLoadID());
            }
        }
    }
}
