using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace Helodrace
{
    [HarmonyPatch]
    public static class Patch_HelodWanderer
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(IncidentWorker_WandererJoin), "GeneratePawn");
            yield return AccessTools.Method(typeof(QuestNode_Root_WandererJoin_WalkIn), "GeneratePawn_NewTemp");
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(PawnGenerator), "GeneratePawn", new[] { typeof(PawnGenerationRequest) });
            MethodInfo replacement = AccessTools.Method(typeof(Patch_HelodWanderer), nameof(GeneratePawn));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.operand = replacement;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Helod wanderer generation hook no longer matches the game.");
        }

        public static Pawn GeneratePawn(PawnGenerationRequest request)
        {
            string faction = Faction.OfPlayerSilentFail?.def.defName;
            HelodRaceExtension settings = HelodRace.Settings;
            if (settings != null && (faction == "PlayerColony" || faction == "HD_HelodPlayerColony")
                && request.KindDef?.race == ThingDefOf.Human
                && request.ForcedXenotype == null && request.ForcedCustomXenotype == null
                && !request.AllowedDevelopmentalStages.Newborn() && Rand.Chance(settings.wandererChance))
            {
                request.PawnKindDefGetter = null;
                request.KindDef = DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist");
            }
            return PawnGenerator.GeneratePawn(request);
        }
    }

    [HarmonyPatch(typeof(Verb_MeleeAttackDamage), "DamageInfosToApply")]
    public static class Patch_HelodSocialFight
    {
        private static readonly FieldInfo ArmorPenetration = AccessTools.Field(typeof(DamageInfo), "armorPenetrationInt");

        public static void Postfix(Verb_MeleeAttackDamage __instance, ref IEnumerable<DamageInfo> __result)
        {
            Pawn pawn = __instance.CasterPawn;
            if (HelodRace.IsHelod(pawn) && pawn.CurJobDef == JobDefOf.SocialFight)
                __result = LimitDamage(__result, HelodRace.Settings.socialFightDamageLimit);
        }

        private static IEnumerable<DamageInfo> LimitDamage(IEnumerable<DamageInfo> source, float limit)
        {
            foreach (DamageInfo original in source)
            {
                // Box a copy to preserve all damage metadata while clearing penetration.
                object boxed = original;
                ArmorPenetration.SetValue(boxed, 0f);
                DamageInfo damage = (DamageInfo)boxed;
                damage.SetAmount(Math.Min(damage.Amount, limit));
                yield return damage;
            }
        }
    }
}
