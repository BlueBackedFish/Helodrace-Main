using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
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
