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

        // Keep this list synchronized with Helod's whiteApparelList in
        // Defs/Helod/Race/GeneralRace.xml. Missing DLC defs are skipped.
        private static readonly string[] CompatibleVanillaApparelDefNames =
        {
            "Apparel_AdvancedHelmet",
            "Apparel_Beret",
            "Apparel_BowlerHat",
            "Apparel_BasicShirt",
            "Apparel_Cape",
            "Apparel_ArmorCataphract",
            "Apparel_ArmorHelmetCataphract",
            "Apparel_ClothMask",
            "Apparel_CollarShirt",
            "Apparel_Coronet",
            "Apparel_Corset",
            "Apparel_CowboyHat",
            "Apparel_Crown",
            "Apparel_CrownStellic",
            "Apparel_Duster",
            "Apparel_FlakJacket",
            "Apparel_FlakPants",
            "Apparel_FlakVest",
            "Apparel_HatHood",
            "Apparel_Jacket",
            "Apparel_Pants",
            "Apparel_FirefoampopPack",
            "Apparel_Parka",
            "Apparel_PlateArmor",
            "Apparel_PowerArmor",
            "Apparel_PowerArmorHelmet",
            "Apparel_PsychicFoilHelmet",
            "Apparel_PsyfocusHelmet",
            "Apparel_ArmorRecon",
            "Apparel_ArmorHelmetRecon",
            "Apparel_Robe",
            "Apparel_SimpleHelmet",
            "Apparel_ShieldBelt",
            "Apparel_SmokepopBelt",
            "Apparel_TribalA",
            "Apparel_TribalHeaddress",
            "Apparel_Tuque",
            "Apparel_WarVeil",
            "Apparel_WarMask",
            "Apparel_PsychicShockLance",
            "Apparel_PsychicInsanityLance",
            "OrbitalTargeterBombardment",
            "OrbitalTargeterPowerBeam",
            "TornadoGenerator",
            "Apparel_PackJump",
            "Apparel_PackBroadshield",
            "Apparel_PackControl",
            "Apparel_PackBandwidth",
            "Apparel_PackTox",
            "Apparel_ShardPsychicShockLance",
            "Apparel_ShardPsychicInsanityLance",
            "Apparel_BiomutationLance",
            "Apparel_DisruptorFlarePack",
            "Apparel_PackTurret",
            "Apparel_DeadlifePack",
            "Apparel_PackHunter",
            "Apparel_CerebrexNode"
        };

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

            List<ThingDef> compatibleApparel = CompatibleVanillaApparelDefs()
                .OrderBy(def => def.defName)
                .ToList();

            IntVec3 itemOrigin = origin + new IntVec3(
                0,
                0,
                (RowsFor(buildings.Count) + 1) * BuildingSpacing);
            IntVec3 apparelOrigin = itemOrigin + new IntVec3(
                0,
                0,
                (RowsFor(items.Count) + 2) * ItemSpacing);
            CellRect testArea = TestAreaFor(
                origin,
                itemOrigin,
                apparelOrigin,
                buildings.Count,
                items.Count,
                compatibleApparel.Count);
            PrepareTestArea(map, testArea);

            int spawnedBuildings = SpawnDefs(buildings, map, origin, BuildingSpacing, 1);
            int spawnedItems = SpawnDefs(items, map, itemOrigin, ItemSpacing, 75);
            int spawnedCompatibleApparel = SpawnDefs(
                compatibleApparel,
                map,
                apparelOrigin,
                ItemSpacing,
                1);

            Messages.Message(
                $"Spawned {spawnedBuildings}/{buildings.Count} Helodrace buildings, "
                + $"{spawnedItems}/{items.Count} items, and "
                + $"{spawnedCompatibleApparel}/{compatibleApparel.Count} "
                + "compatible vanilla apparel in a separate group.",
                MessageTypeDefOf.PositiveEvent,
                false);
        }

        private static int RowsFor(int count)
        {
            return Math.Max(1, (count + Columns - 1) / Columns);
        }

        private static CellRect TestAreaFor(
            IntVec3 buildingOrigin,
            IntVec3 itemOrigin,
            IntVec3 apparelOrigin,
            int buildingCount,
            int itemCount,
            int apparelCount)
        {
            int buildingColumns = Math.Min(Columns, Math.Max(1, buildingCount));
            int itemColumns = Math.Min(Columns, Math.Max(1, itemCount));
            int apparelColumns = Math.Min(Columns, Math.Max(1, apparelCount));
            int buildingRows = RowsFor(buildingCount);
            int itemRows = RowsFor(itemCount);
            int apparelRows = RowsFor(apparelCount);
            int maxX = Math.Max(
                Math.Max(
                    buildingOrigin.x
                        + (buildingColumns - 1) * BuildingSpacing
                        + BuildingSpacing - 1,
                    itemOrigin.x
                        + (itemColumns - 1) * ItemSpacing
                        + ItemSpacing - 1),
                apparelOrigin.x
                    + (apparelColumns - 1) * ItemSpacing
                    + ItemSpacing - 1);
            int maxZ = Math.Max(
                Math.Max(
                    buildingOrigin.z
                        + (buildingRows - 1) * BuildingSpacing
                        + BuildingSpacing - 1,
                    itemOrigin.z
                        + (itemRows - 1) * ItemSpacing
                        + ItemSpacing - 1),
                apparelOrigin.z
                    + (apparelRows - 1) * ItemSpacing
                    + ItemSpacing - 1);

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

        private static IEnumerable<ThingDef> CompatibleVanillaApparelDefs()
        {
            for (int i = 0; i < CompatibleVanillaApparelDefNames.Length; i++)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(
                    CompatibleVanillaApparelDefNames[i]);
                if (def != null
                    && def.category == ThingCategory.Item
                    && def.IsApparel)
                {
                    yield return def;
                }
            }
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
