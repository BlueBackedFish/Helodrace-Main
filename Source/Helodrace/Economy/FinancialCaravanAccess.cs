using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Economy
{
    public static class FinancialCaravanAccess
    {
        public static AcceptanceReport CanTradeWith(Pawn negotiator, Faction faction, TraderKindDef kind, Pawn trader)
        {
            if (negotiator?.Faction != Faction.OfPlayer || trader?.Faction != faction || trader?.TraderKind != kind
                || !FinancialCaravanUtility.IsProtected(trader))
                return FactionUtility.CanTradeWith(negotiator, faction, kind);
            // Run the original skill and real royal-permit requirements. The vanilla
            // menu misleadingly labels every rejection here as a missing royal title.
            using (FinancialCaravanScope.Enter(faction))
                return FactionUtility.CanTradeWith(negotiator, faction, kind);
        }

        public static bool DoorMachinesLike(Faction doorFaction, Pawn pawn)
        {
            if (doorFaction != Faction.OfPlayer || !FinancialCaravanUtility.IsProtected(pawn))
                return GenAI.MachinesLike(doorFaction, pawn);
            // Preserve the native prisoner/animal checks and the rest of PawnCanOpen.
            using (FinancialCaravanScope.Enter(pawn.Faction))
                return GenAI.MachinesLike(doorFaction, pawn);
        }
    }

    [HarmonyPatch]
    public static class Patch_FinancialCaravanTradeMenu
    {
        public static MethodBase TargetMethod()
            => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(FloatMenuOptionProvider_Trade), "GetOptionsFor",
                new[] { typeof(Pawn), typeof(FloatMenuContext) }));

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo original = AccessTools.Method(typeof(FactionUtility), nameof(FactionUtility.CanTradeWith));
            MethodInfo replacement = AccessTools.Method(typeof(FinancialCaravanAccess), nameof(FinancialCaravanAccess.CanTradeWith));
            FieldInfo clickedPawn = AccessTools.Field(__originalMethod.DeclaringType, "clickedPawn")
                ?? throw new InvalidOperationException("Trade menu clicked pawn no longer matches.");
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Ldfld, clickedPawn);
                    instruction.opcode = OpCodes.Call; instruction.operand = replacement;
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Trade menu faction eligibility check no longer matches.");
        }
    }

    [HarmonyPatch(typeof(Building_Door), nameof(Building_Door.PawnCanOpen))]
    public static class Patch_FinancialCaravanDoorAccess
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(GenAI), nameof(GenAI.MachinesLike));
            MethodInfo replacement = AccessTools.Method(typeof(FinancialCaravanAccess), nameof(FinancialCaravanAccess.DoorMachinesLike));
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call; instruction.operand = replacement;
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Door faction access check no longer matches.");
        }
    }
}
