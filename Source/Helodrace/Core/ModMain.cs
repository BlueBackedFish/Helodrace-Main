using HarmonyLib;
using Verse;
using RimWorld;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace Helodrace
{
    [StaticConstructorOnStartup]
    public static class HelodraceMod
    {
        static HelodraceMod()
        {
            var harmony = new Harmony("YourName.Helodrace");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            // Dynamically add CompProperties_Lubricant to all Raw Meat (vanilla auto-generates these, so XML patching is unreliable)
            ThingCategoryDef meatCat = ThingCategoryDefOf.MeatRaw;
            int count = 0;
            
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.thingCategories != null && def.thingCategories.Contains(meatCat))
                {
                    if (def.comps == null)
                    {
                        def.comps = new List<CompProperties>();
                    }
                    
                    if (!def.comps.Any(c => c is CompProperties_Lubricant))
                    {
                        // Raw meat is terrible lubricant, so it only lasts ~6 hours
                        def.comps.Add(new CompProperties_Lubricant { lubeMtbHours = 6f }); 
                        count++;
                    }
                }
            }

            Log.Message($"Helodrace initialized. Added Lubricant comp to {count} meat types.");
            RemoveQualityFromHelodraceGuns();
        }

        private static void RemoveQualityFromHelodraceGuns()
        {
            int count = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!IsHelodraceGun(def) || def.comps == null)
                {
                    continue;
                }

                int removed = def.comps.RemoveAll(comp => comp?.compClass == typeof(CompQuality));
                if (removed > 0)
                {
                    count++;
                }
            }

            Log.Message($"Helodrace initialized. Removed quality comp from {count} Helodrace guns.");
        }

        private static bool IsHelodraceGun(ThingDef def)
        {
            return def?.defName != null
                && def.defName.StartsWith("HD_Gun_")
                && def.defName.EndsWith("_Weapon");
        }
    }

    public class HelodraceBase : Mod
    {
        public HelodraceBase(ModContentPack content) : base(content)
        {
            Log.Message("Helodrace Mod loaded.");
        }
    }

    [HarmonyPatch(typeof(LifeStageWorker_HumanlikeAdult), nameof(LifeStageWorker_HumanlikeAdult.Notify_LifeStageStarted))]
    public static class Fix_LifeStageNullRef
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, LifeStageDef previousLifeStage)
        {
            if (pawn == null || pawn.story == null)
            {
                return false; // Skip the original method and all postfixes if components are not yet fully generated (e.g., during early pawn component generation)
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ExpectationsUtility), nameof(ExpectationsUtility.CurrentExpectationFor), new[] { typeof(Pawn) })]
    public static class Patch_CurrentExpectationFor
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn p, ref ExpectationDef __result)
        {
            if (p?.genes != null)
            {
                // If the pawn has the Arrogant Leader gene, increase their expectations by 1 tier (Expectations +1)
                if (p.genes.HasActiveGene(DefDatabase<GeneDef>.GetNamed("HD_Gene_ArrogantLeader")))
                {
                    __result = GetHigherExpectation(__result);
                }
            }
        }

        private static ExpectationDef GetHigherExpectation(ExpectationDef current)
        {
            if (current == null) return current;

            List<ExpectationDef> allExpectations = DefDatabase<ExpectationDef>.AllDefsListForReading;
            if (allExpectations == null) return current;

            // Sort expectations by 'order'
            var sorted = allExpectations.OrderBy(e => e.order).ToList();
            int index = sorted.IndexOf(current);
            if (index >= 0 && index < sorted.Count - 1)
            {
                return sorted[index + 1]; // Return the next higher expectation level!
            }
            return current;
        }
    }
}
