using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_MannedTurretGunnerAccuracy
    {
        private static readonly FieldInfo FactorFromShooterAndDistField =
            AccessTools.Field(typeof(ShotReport), "factorFromShooterAndDist");

        public static void Postfix(Thing caster, ref ShotReport __result)
        {
            if (caster == null)
            {
                return;
            }

            Pawn gunner = caster.TryGetComp<CompMannable>()?.ManningPawn;
            if (gunner == null || FactorFromShooterAndDistField == null)
            {
                return;
            }

            int shootingLevel = gunner.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 10;
            float skillFactor = 0.75f + shootingLevel * 0.025f;
            float conditionFactor = Mathf.Clamp(gunner.GetStatValue(StatDefOf.ShootingAccuracyPawn), 0.85f, 1.10f);
            float gunnerAccuracy = skillFactor * conditionFactor;

            object boxedReport = __result;
            float currentFactor = (float)FactorFromShooterAndDistField.GetValue(boxedReport);
            FactorFromShooterAndDistField.SetValue(boxedReport, Mathf.Max(currentFactor, gunnerAccuracy));
            __result = (ShotReport)boxedReport;
        }
    }
}
