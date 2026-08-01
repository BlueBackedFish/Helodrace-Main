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
        private const int TestAreaPadding = 2;

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

            IntVec3 itemOrigin = origin + new IntVec3(0, 0, ((buildings.Count + Columns - 1) / Columns + 1) * BuildingSpacing);
            CellRect testArea = TestAreaFor(origin, itemOrigin, buildings.Count, items.Count);
            PrepareTestArea(map, testArea);

            int spawnedBuildings = SpawnDefs(buildings, map, origin, BuildingSpacing, 1);
            int spawnedItems = SpawnDefs(items, map, itemOrigin, ItemSpacing, 75);

            Messages.Message(
                $"Spawned {spawnedBuildings}/{buildings.Count} Helodrace buildings and {spawnedItems}/{items.Count} items.",
                MessageTypeDefOf.PositiveEvent,
                false);
        }

        private static CellRect TestAreaFor(IntVec3 buildingOrigin, IntVec3 itemOrigin, int buildingCount, int itemCount)
        {
            int buildingColumns = Math.Min(Columns, Math.Max(1, buildingCount));
            int itemColumns = Math.Min(Columns, Math.Max(1, itemCount));
            int buildingRows = Math.Max(1, (buildingCount + Columns - 1) / Columns);
            int itemRows = Math.Max(1, (itemCount + Columns - 1) / Columns);
            int maxX = Math.Max(
                buildingOrigin.x + (buildingColumns - 1) * BuildingSpacing + BuildingSpacing - 1,
                itemOrigin.x + (itemColumns - 1) * ItemSpacing + ItemSpacing - 1);
            int maxZ = Math.Max(
                buildingOrigin.z + (buildingRows - 1) * BuildingSpacing + BuildingSpacing - 1,
                itemOrigin.z + (itemRows - 1) * ItemSpacing + ItemSpacing - 1);

            return CellRect.FromLimits(
                buildingOrigin.x - TestAreaPadding,
                buildingOrigin.z - TestAreaPadding,
                maxX + TestAreaPadding,
                maxZ + TestAreaPadding);
        }

        private static void PrepareTestArea(Map map, CellRect area)
        {
            TerrainDef floor = TerrainDef.Named("Concrete");

            foreach (IntVec3 cell in area.Cells.Where(cell => cell.InBounds(map)))
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (thing is Pawn)
                    {
                        continue;
                    }

                    ThingCategory category = thing.def.category;
                    if (category == ThingCategory.Building
                        || category == ThingCategory.Item
                        || category == ThingCategory.Plant
                        || category == ThingCategory.Filth)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }

                map.terrainGrid.SetTerrain(cell, floor);
                map.snowGrid.SetDepth(cell, 0f);
            }
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
