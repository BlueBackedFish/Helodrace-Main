using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace.SquadAI
{
    // New raid lords register their organized squads at once; the periodic
    // discovery also catches staged raids, reinforcements and loaded games.
    [SquadTactical, HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord))]
    public static class Patch_SquadAI_RaidCreated
    {
        public static void Postfix(Lord __result)
        {
            if (__result?.Map != null) MapComponent_SquadAI.For(__result.Map)?.Register(__result, Find.TickManager.TicksGame);
        }
    }

    // Vanilla assault toils rewrite all duties when pawns join or the toil
    // starts; squads that are fighting keep theirs.
    [SquadTactical, HarmonyPatch]
    public static class Patch_SquadAI_KeepDuties
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (System.Type toil in new[] { typeof(LordToil_AssaultColony), typeof(LordToil_AssaultColonySappers), typeof(LordToil_AssaultColonyBreaching) })
            {
                MethodInfo method = AccessTools.DeclaredMethod(toil, nameof(LordToil.UpdateAllDuties));
                if (method != null) yield return method;
            }
        }
        public static void Postfix(LordToil __instance)
        {
            Lord lord = __instance.lord;
            if (lord?.Map != null) MapComponent_SquadAI.For(lord.Map)?.ReapplyAfterVanilla(lord);
        }
    }

    // Being hit reveals the shooter to the victim's squad.
    [SquadTactical, HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_SquadAI_Harmed
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo)
        {
            if (dinfo.Instigator == null || __instance.MapHeld == null) return;
            MapComponent_SquadAI.For(__instance.MapHeld)?.Harmed(__instance, dinfo.Instigator);
        }
    }

    // With the squad AI not selected, saved squad duties fall back to the lord's own.
    public sealed class MapComponent_SquadDutyCleanup : MapComponent
    {
        public MapComponent_SquadDutyCleanup(Map map) : base(map) { }
        public override void FinalizeInit()
        {
            if (TacticalEngineSelection.Kind == TacticalEngineKind.Squad) return;
            foreach (Lord lord in map.lordManager.lords)
            {
                bool owned = false;
                foreach (Pawn pawn in lord.ownedPawns) owned |= SquadDuties.Owned(pawn.mindState?.duty?.def);
                if (!owned) continue;
                lord.CurLordToil?.UpdateAllDuties();
                foreach (Pawn pawn in lord.ownedPawns)
                    if (SquadDuties.Owned(pawn.mindState?.duty?.def)) pawn.mindState.duty = null;
            }
        }
    }
}
