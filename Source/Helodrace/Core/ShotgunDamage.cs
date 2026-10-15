using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(DamageWorker_AddInjury), "GetExactPartFromDamageInfo")]
    public static class Patch_DamageWorker_ShotgunSurface
    {
        public static bool Prefix(DamageInfo dinfo, Pawn pawn, ref BodyPartRecord __result)
        {
            ShotgunImpact impact = ShotgunSpreadContext.Impact;
            if (impact?.pawn != pawn || impact.damageDef != dinfo.Def || impact.internalDamage || dinfo.HitPart != null) return true;
            if (impact.selectedSurface)
            {
                __result = ShotgunBallistics.IsPresent(pawn, impact.surface) ? impact.surface : null;
                return false;
            }
            ShotgunBodyProfile profile = ShotgunBodyProfile.For(pawn.RaceProps.body);
            if (profile.surfaces.Count == 0) return true;
            BodyPartRecord centre = impact.shell.CentreFor(pawn, dinfo.Def);
            __result = profile.ChooseSurface(pawn, centre, dinfo, impact.relativeSpread);
            impact.surface = __result;
            impact.selectedSurface = true;
            return false;
        }
    }

    internal sealed class ShotgunPenetration
    {
        internal List<BodyPartRecord> path;
        internal float budget;

        internal static ShotgunPenetration Plan(ShotgunImpact impact, Pawn pawn, DamageInfo damage, float total)
        {
            if (impact == null || impact.pawn != pawn || impact.surface != damage.HitPart
                || impact.damageDef != damage.Def || impact.internalDamage || !damage.AllowDamagePropagation
                || damage.InstantPermanentInjury || !(total > 0f) || impact.pellet == null) return null;
            ShotgunBodyProfile profile = ShotgunBodyProfile.For(pawn.RaceProps.body);
            BodyPartRecord organ = profile.ChooseInternal(pawn, impact.surface, damage.Def);
            if (organ == null) return null;
            List<BodyPartRecord> path = ShotgunBodyProfile.InternalPath(impact.surface, organ);
            if (path.Count == 0) return null;
            float chance = impact.pellet.Settings.internalPenetrationChance * impact.energy
                * Math.Min(1f, total / Math.Max(0.01f, impact.incomingDamage));
            // Solid intervening structures resist deeper penetration regardless
            // of their DefName (bone, shell, custom exoskeleton, etc.).
            for (int i = 0; i < path.Count - 1; i++)
                if (path[i].def.IsSolid(path[i], pawn.health.hediffSet.hediffs)) chance *= 0.65f;
            return Rand.Chance(chance) ? new ShotgunPenetration { path = path, budget = total * 0.4f } : null;
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "FinalizeAndAddInjury", new[]
    {
        typeof(Pawn), typeof(float), typeof(DamageInfo), typeof(DamageWorker.DamageResult)
    })]
    public static class Patch_DamageWorker_ShotgunPenetration
    {
        private delegate float FinalizeInjury(DamageWorker_AddInjury worker, Pawn pawn, float damage,
            DamageInfo info, DamageWorker.DamageResult result);
        private static readonly FinalizeInjury apply = AccessTools.MethodDelegate<FinalizeInjury>(AccessTools.Method(
            typeof(DamageWorker_AddInjury), "FinalizeAndAddInjury", new[]
            { typeof(Pawn), typeof(float), typeof(DamageInfo), typeof(DamageWorker.DamageResult) }));

        internal static void Prefix(Pawn pawn, ref float totalDamage, DamageInfo dinfo, out ShotgunPenetration __state)
        {
            __state = ShotgunPenetration.Plan(ShotgunSpreadContext.Impact, pawn, dinfo, totalDamage);
            if (__state != null) totalDamage -= __state.budget;
        }

        internal static void Postfix(DamageWorker_AddInjury __instance, Pawn pawn, DamageInfo dinfo,
            DamageWorker.DamageResult result, ShotgunPenetration __state, ref float __result)
        {
            if (__state == null || __result <= 0f || pawn.Dead) return;
            ShotgunImpact impact = ShotgunSpreadContext.Impact;
            impact.internalDamage = true;
            try
            {
                float remaining = __state.budget;
                for (int i = 0; i < __state.path.Count; i++)
                {
                    BodyPartRecord part = __state.path[i];
                    if (pawn.Dead || !ShotgunBallistics.IsPresent(pawn, part)) break;
                    float amount = remaining / (__state.path.Count - i);
                    remaining -= amount;
                    DamageInfo inner = dinfo;
                    inner.SetHitPart(part);
                    inner.SetAmount(amount);
                    inner.SetAllowDamagePropagation(false);
                    // Armor and incoming-damage factors have already been
                    // applied once at the surface. This helper finalizes only.
                    __result += apply(__instance, pawn, amount, inner, result);
                }
            }
            finally { impact.internalDamage = false; }
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "ApplySmallPawnDamagePropagation")]
    public static class Patch_DamageWorker_ShotgunSmallPawn
    {
        public static bool Prefix(Pawn pawn)
        {
            ShotgunImpact impact = ShotgunSpreadContext.Impact;
            return impact?.pawn != pawn || impact.surface == null;
        }
    }
}
