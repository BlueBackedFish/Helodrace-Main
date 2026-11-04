using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    internal static class ArmorInstantCrafting
    {
        private const float MaterialSearchRadius = 12f;

        internal static bool CanCraft(ThingDef def) => def != null
            && DefDatabase<ModularArmorPartDef>.AllDefsListForReading
                .Any(p => p.RequiredThingDef == def && p.CanInstantCraft);

        internal static bool TryCosts(IEnumerable<ThingDef> parts,
            out Dictionary<ThingDef, int> costs)
        {
            costs = new Dictionary<ThingDef, int>();
            foreach (ThingDef def in parts)
            {
                if (!CanCraft(def) || def.costList.NullOrEmpty()) return false;
                foreach (var cost in def.costList)
                {
                    if (cost.thingDef == null || cost.count <= 0) return false;
                    costs.TryGetValue(cost.thingDef, out int current);
                    costs[cost.thingDef] = current + cost.count;
                }
            }
            return true;
        }

        internal static bool Requirements(IEnumerable<ThingDef> parts, Thing bench, out string rejection)
        {
            rejection = null;
            foreach (ThingDef def in parts.Distinct())
            {
                var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("Make_" + def.defName);
                if (!CanCraft(def) || recipe == null || !recipe.AllRecipeUsers.Contains(bench?.def))
                    rejection = "HD_ArmorCraft_CannotCraft".Translate(def.LabelCap);
                else if (!recipe.AvailableNow)
                    rejection = "HD_ArmorCraft_ResearchRequired".Translate(def.LabelCap);
                else if (!recipe.Worker.AvailableOnNow(bench))
                    rejection = "HD_ArmorCraft_FacilityRequired".Translate(def.LabelCap,
                        LinkedFacilityRecipes.RequiredFacilitiesText(recipe));
                if (rejection != null) return false;
            }
            return true;
        }

        internal static bool AllocateMaterials(IEnumerable<ThingDef> parts, Thing bench, Pawn worker,
            Dictionary<Thing, int> allocated, List<Thing> sources, List<int> counts, out string rejection)
        {
            rejection = null;
            if (!Requirements(parts, bench, out rejection) || !TryCosts(parts, out var costs)) return false;
            foreach (var cost in costs)
            {
                int remaining = cost.Value;
                var stock = (worker?.inventory?.innerContainer?.ToList() ?? new List<Thing>())
                    .Concat(bench?.Map?.listerThings.ThingsOfDef(cost.Key) ?? Enumerable.Empty<Thing>())
                    .Where(t => t.def == cost.Key && !t.Destroyed
                        && (!t.Spawned || t.Position.InHorDistOf(bench.Position, MaterialSearchRadius)
                            && worker != null && !t.IsForbidden(worker)
                            && worker.CanReserveAndReach(t, PathEndMode.ClosestTouch, Danger.Some)))
                    .OrderBy(t => t.Spawned ? 1 : 0).ThenBy(t => t.PositionHeld.DistanceToSquared(bench.Position));
                foreach (Thing source in stock)
                {
                    allocated.TryGetValue(source, out int used);
                    int count = Math.Min(remaining, source.stackCount - used);
                    if (count <= 0) continue;
                    int capacity = worker?.carryTracker.MaxStackSpaceEver(cost.Key) ?? 0;
                    if (capacity <= 0) continue;
                    for (int left = count; left > 0;)
                    {
                        int portion = Math.Min(left, capacity);
                        sources.Add(source); counts.Add(portion); left -= portion;
                    }
                    allocated[source] = used + count;
                    remaining -= count;
                    if (remaining == 0) break;
                }
                if (remaining == 0) continue;
                rejection = "HD_ModularWeapon_MaterialRequired".Translate(cost.Key.LabelCap, cost.Value);
                return false;
            }
            return true;
        }

        internal static bool CanCraftNow(ModularArmorPartDef part, Thing bench, Pawn worker,
            out string rejection) => AllocateMaterials(new[] { part.RequiredThingDef }, bench, worker,
                new Dictionary<Thing, int>(), new List<Thing>(), new List<int>(), out rejection);

        internal static string CostText(ModularArmorPartDef part) =>
            part.RequiredThingDef?.costList.NullOrEmpty() != false ? string.Empty
                : string.Join(", ", part.RequiredThingDef.costList.Select(c => c.thingDef.LabelCap + " x" + c.count));
    }

    public sealed partial class CompModularArmor
    {
        internal bool CompleteCustomizationWithCrafting(List<ModularArmorEdit> plan, string baseline,
            List<Thing> collected, int physicalCount, List<ThingDef> instantParts, Thing bench, Pawn worker)
        {
            if (!CompClothingWorkbench.CanWorkAt(worker, bench, this)
                || ConfigurationSignature() != baseline || !ValidateEditorPlan(plan)
                || physicalCount < 0 || physicalCount > collected.Count
                || !ArmorInstantCrafting.Requirements(instantParts, bench, out _)
                || !ArmorInstantCrafting.TryCosts(instantParts, out var costs)) return false;
            var physical = collected.Take(physicalCount).ToList();
            var materials = collected.Skip(physicalCount).ToList();
            if (collected.Any(t => t == null || t.Destroyed || !worker.inventory.innerContainer.Contains(t))
                || physical.Any(t => t.stackCount != 1)
                || materials.GroupBy(t => t.def).Count() != costs.Count
                || costs.Any(c => materials.Where(t => t.def == c.Key).Sum(t => t.stackCount) != c.Value)) return false;
            var needed = plan.Where(e => e.part.RequiredThingDef != null && ReusableSource(e) == null)
                .GroupBy(e => e.part.RequiredThingDef).ToDictionary(g => g.Key, g => g.Count());
            var supplied = physical.Select(t => t.def).Concat(instantParts).GroupBy(d => d)
                .ToDictionary(g => g.Key, g => g.Count());
            if (needed.Count != supplied.Count || needed.Any(n => !supplied.TryGetValue(n.Key, out int count) || count != n.Value)) return false;

            var made = new List<Thing>();
            try
            {
                foreach (var def in instantParts) made.Add(ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def)));
                // No raw materials are destroyed until the existing all-or-nothing
                // configuration transfer has validated and installed every item.
                if (!CompleteCustomization(plan, baseline, physical.Concat(made).ToList())) return false;
                foreach (Thing material in materials)
                {
                    worker.inventory.innerContainer.Remove(material);
                    material.Destroy(DestroyMode.Vanish);
                }
                return true;
            }
            finally
            {
                foreach (Thing item in made)
                    if (!InstalledParts.Any(r => r.InstalledItem == item) && !item.Destroyed)
                        item.Destroy(DestroyMode.Vanish);
            }
        }
    }
}
