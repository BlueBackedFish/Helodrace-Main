using System;
using HarmonyLib;
using Verse;

namespace Helodrace.Tactical
{
    public static class TacticalWeaponRules
    {
        private static readonly System.Reflection.PropertyInfo BurstProperty =
            AccessTools.Property(typeof(Verb), "BurstShotCount");

        public static int BurstCount(Verb verb) => verb == null ? 0
            : Math.Max(1, (int)BurstProperty.GetValue(verb, null));

        public static bool AllowsMultiTarget(int burstCount, float cooldownSeconds)
            => burstCount >= 3 || cooldownSeconds <= 1f;

        public static bool AllowsMultiTarget(Pawn pawn, Verb verb)
            => verb != null && AllowsMultiTarget(BurstCount(verb), verb.verbProps.AdjustedCooldown(verb, pawn));

        public static int CoordinatedShots(int burstCount) => Math.Max(1, (burstCount + 1) / 2);
    }
}
