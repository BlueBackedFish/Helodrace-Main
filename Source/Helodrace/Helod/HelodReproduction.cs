using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    // Helods only conceive with Helods. Romance, lovin', engagement and
    // marriage stay open across the Helod/human line.
    public static class HelodReproduction
    {
        // Exactly one side is a Helod. Null partners are left to vanilla.
        public static bool CrossRace(Pawn first, Pawn second)
            => first != null && second != null && HelodRace.IsHelod(first) != HelodRace.IsHelod(second);

        public static AcceptanceReport CrossRaceReport(Pawn first, Pawn second)
            => "HD_Race_CrossRaceReproduction".Translate(first.Named("PAWN1"), second.Named("PAWN2")).Resolve();

        // Vanilla romance and compatibility return 0 when pawn defs differ.
        // Treat a Helod as a human for that comparison only.
        public static ThingDef RomanceRace(Thing thing)
            => HelodRace.IsHelod(thing as Pawn) ? ThingDefOf.Human : thing.def;

        internal static Pawn OvumMother(HumanOvum ovum)
            => ovum.GetComp<CompHasPawnSources>()?.pawnSources?.FirstOrDefault();
    }

    [HarmonyPatch]
    public static class Patch_HelodCrossRaceRomance
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.SecondaryLovinChanceFactor));
            yield return AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.CompatibilityWith));
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            FieldInfo def = AccessTools.Field(typeof(Thing), nameof(Thing.def));
            MethodInfo replacement = AccessTools.Method(typeof(HelodReproduction), nameof(HelodReproduction.RomanceRace));
            List<CodeInstruction> codes = instructions.ToList();
            int count = 0;
            foreach (CodeInstruction instruction in codes)
            {
                if (instruction.LoadsField(def))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    count++;
                }
            }
            // HAR, when its transpiler runs first, rewrites the same def comparison
            // into RaceProps.Humanlike, which already admits Helod/human pairs.
            if (count != 2 && !(count == 0 && ComparesHumanlike(codes)))
                throw new InvalidOperationException("Race comparison in " + __originalMethod.Name + " no longer matches.");
            return codes;
        }

        private static bool ComparesHumanlike(List<CodeInstruction> codes)
        {
            MethodInfo raceProps = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.RaceProps));
            MethodInfo humanlike = AccessTools.PropertyGetter(typeof(RaceProperties), nameof(RaceProperties.Humanlike));
            int pairs = 0;
            for (int i = 0; i + 1 < codes.Count; i++)
                if (codes[i].Calls(raceProps) && codes[i + 1].Calls(humanlike)) pairs++;
            return pairs >= 2;
        }
    }

    [HarmonyPatch(typeof(PregnancyUtility), nameof(PregnancyUtility.CanEverProduceChild))]
    public static class Patch_HelodCanProduceChild
    {
        public static void Postfix(Pawn first, Pawn second, ref AcceptanceReport __result)
        {
            if (__result.Accepted && HelodReproduction.CrossRace(first, second))
                __result = HelodReproduction.CrossRaceReport(first, second);
        }
    }

    // Lovin' reads the chance directly instead of CanEverProduceChild. Patch
    // both levels so the rule survives inlining of the outer wrapper.
    [HarmonyPatch]
    public static class Patch_HelodPartnerPregnancyChance
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PregnancyUtility), nameof(PregnancyUtility.PregnancyChanceForPartners));
            yield return AccessTools.Method(typeof(PregnancyUtility), "PregnancyChanceForPartnersWithoutPregnancyApproach");
        }

        public static void Postfix(Pawn woman, Pawn man, ref float __result)
        {
            if (HelodReproduction.CrossRace(woman, man)) __result = 0f;
        }
    }

    [HarmonyPatch(typeof(HumanOvum), "CanFertilizeReport")]
    public static class Patch_HelodOvumFertilize
    {
        public static void Postfix(HumanOvum __instance, Pawn pawn, ref AcceptanceReport __result)
        {
            Pawn mother = HelodReproduction.OvumMother(__instance);
            if (__result.Accepted && HelodReproduction.CrossRace(mother, pawn))
                __result = HelodReproduction.CrossRaceReport(mother, pawn);
        }
    }

    [HarmonyPatch(typeof(HumanOvum), nameof(HumanOvum.ProduceEmbryo))]
    public static class Patch_HelodOvumEmbryo
    {
        // Last guard for an order queued before the report rejected it.
        public static bool Prefix(HumanOvum __instance, Pawn father, ref Thing __result)
        {
            if (!HelodReproduction.CrossRace(HelodReproduction.OvumMother(__instance), father)) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(HumanEmbryo), "CanImplantReport")]
    public static class Patch_HelodEmbryoImplant
    {
        // A surrogate must share the embryo's race; otherwise a human pairing
        // could still be born as a Helod.
        public static void Postfix(HumanEmbryo __instance, Pawn pawn, ref AcceptanceReport __result)
        {
            Pawn parent = __instance.Mother ?? __instance.Father;
            if (__result.Accepted && HelodReproduction.CrossRace(parent, pawn))
                __result = HelodReproduction.CrossRaceReport(parent, pawn);
        }
    }
}
