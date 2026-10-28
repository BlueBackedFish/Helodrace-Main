using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed class HeavyMachineGunAccuracyExtension : DefModExtension
    {
        public float gunnerBaseAccuracy = 0.5f;
    }

    public static class HeavyMachineGunAccuracy
    {
        public static float Correct(float originalGunnerAccuracy, float x)
        {
            x = Mathf.Clamp01(x);
            return Mathf.Clamp01(x + originalGunnerAccuracy * x);
        }

        public static Pawn Gunner(Thing caster)
        {
            if (caster is Building_Crows crows) return crows.Computer?.Operator;
            CompMannable manning = caster?.TryGetComp<CompMannable>();
            Pawn gunner = manning?.MannedNow == true ? manning.ManningPawn : null;
            return gunner?.Spawned == true && !gunner.Dead && !gunner.Downed
                && gunner.Map == caster.Map && gunner.Faction == caster.Faction ? gunner : null;
        }
    }

    // Correct the common shooter-factor calculation, so the tooltip and actual
    // projectile use the same bounded result. Equipment, weather, target size,
    // posture and cover remain separate vanilla ShotReport factors.
    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitFactorFromShooter))]
    public static class Patch_HeavyMachineGunGunnerAccuracy
    {
        public static void Postfix(Thing caster, float distance, ref float __result)
        {
            HeavyMachineGunAccuracyExtension settings = caster?.def?.GetModExtension<HeavyMachineGunAccuracyExtension>();
            if (settings == null) return;
            Pawn gunner = HeavyMachineGunAccuracy.Gunner(caster);
            if (gunner == null) return;

            // Vanilla applies the pawn's real shooting stat, range factors and
            // distance decay once. Do not multiply Shooting skill a second time
            // or replace the report with a distance-independent skill bonus.
            float original = ShotReport.HitFactorFromShooter(gunner, distance);
            __result = HeavyMachineGunAccuracy.Correct(original, settings.gunnerBaseAccuracy);
        }
    }
}
