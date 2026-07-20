using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace Helodrace
{
    public static class DebugActions
    {
        private const string DefPrefix = "HD_";
        private const int Columns = 10;
        private const int BuildingSpacing = 7;
        private const int ItemSpacing = 2;

        [DebugAction("Helodrace", "Spawn all Helodrace buildings and items", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnAllHelodraceBuildingsAndItems()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            IntVec3 origin = UI.MouseCell();
            if (!origin.InBounds(map))
            {
                origin = map.Center;
            }

            List<ThingDef> buildings = SpawnableHelodraceDefs()
                .Where(def => def.category == ThingCategory.Building)
                .OrderBy(def => def.defName)
                .ToList();

            List<ThingDef> items = SpawnableHelodraceDefs()
                .Where(def => def.category == ThingCategory.Item)
                .OrderBy(def => def.defName)
                .ToList();

            int spawnedBuildings = SpawnDefs(buildings, map, origin, BuildingSpacing, 1);
            IntVec3 itemOrigin = origin + new IntVec3(0, 0, ((buildings.Count + Columns - 1) / Columns + 1) * BuildingSpacing);
            int spawnedItems = SpawnDefs(items, map, itemOrigin, ItemSpacing, 75);

            Messages.Message(
                $"Spawned {spawnedBuildings}/{buildings.Count} Helodrace buildings and {spawnedItems}/{items.Count} items.",
                MessageTypeDefOf.PositiveEvent,
                false);
        }

        private static IEnumerable<ThingDef> SpawnableHelodraceDefs()
        {
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.defName.StartsWith(DefPrefix, StringComparison.Ordinal)
                    && !def.IsBlueprint
                    && !def.IsFrame
                    && (def.category == ThingCategory.Building || def.category == ThingCategory.Item));
        }

        private static int SpawnDefs(List<ThingDef> defs, Map map, IntVec3 origin, int spacing, int maxStackCount)
        {
            int spawned = 0;

            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                IntVec3 target = origin + new IntVec3((i % Columns) * spacing, 0, (i / Columns) * spacing);
                if (!target.InBounds(map))
                {
                    target = origin;
                }

                try
                {
                    Thing thing = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
                    if (thing.def.category == ThingCategory.Item)
                    {
                        thing.stackCount = Math.Max(1, Math.Min(thing.def.stackLimit, maxStackCount));
                    }

                    if (thing.def.CanHaveFaction)
                    {
                        thing.SetFactionDirect(Faction.OfPlayer);
                    }

                    if (GenPlace.TryPlaceThing(thing, target, map, ThingPlaceMode.Near))
                    {
                        spawned++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Helodrace] Failed to spawn debug thing {def.defName}: {ex}");
                }
            }

            return spawned;
        }
    }
}
