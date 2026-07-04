using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_M1917HMG_GunnerAccuracy
    {
        private static readonly FieldInfo FactorFromShooterAndDistField =
            AccessTools.Field(typeof(ShotReport), "factorFromShooterAndDist");

        public static void Postfix(Thing caster, ref ShotReport __result)
        {
            if (caster?.def?.defName != "HD_Building_M1917HMG")
            {
                return;
            }

            Pawn gunner = caster.TryGetComp<CompMannable>()?.ManningPawn;
            if (gunner == null || FactorFromShooterAndDistField == null)
            {
                return;
            }

            float gunnerAccuracy = gunner.GetStatValue(StatDefOf.ShootingAccuracyPawn);
            object boxedReport = __result;
            float currentFactor = (float)FactorFromShooterAndDistField.GetValue(boxedReport);
            FactorFromShooterAndDistField.SetValue(boxedReport, currentFactor * gunnerAccuracy);
            __result = (ShotReport)boxedReport;
        }
    }
}
