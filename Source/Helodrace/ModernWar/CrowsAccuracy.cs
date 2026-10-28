using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public static class CrowsAccuracy
    {
        // Retain 30% of the weather penalty, rather than 30% of the remaining
        // accuracy: vanilla 50% weather accuracy therefore becomes 85%.
        public static float WeatherFactor(float original) =>
            1f - (1f - Mathf.Clamp01(original)) * 0.3f;
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_CrowsWeatherAccuracy
    {
        private static readonly FieldInfo WeatherField = AccessTools.Field(typeof(ShotReport), "factorFromWeather");

        public static void Postfix(Thing caster, ref ShotReport __result)
        {
            if (!(caster is Building_Crows) || WeatherField == null) return;
            object report = __result;
            float original = (float)WeatherField.GetValue(report);
            WeatherField.SetValue(report, CrowsAccuracy.WeatherFactor(original));
            __result = (ShotReport)report;
        }
    }
}
