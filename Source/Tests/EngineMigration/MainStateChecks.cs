using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Tests
{
    internal static class MainStateChecks
    {
        private static readonly IntVec3 FieldCell = new IntVec3(15, 0, 15);
        private static readonly IntVec3 UnrecordedCell = new IntVec3(16, 0, 15);
        private const string CarrierTag = "helodrace.tests.state.carrier";

        internal static void Create(Action<bool, string> check)
        {
            Map map = Find.CurrentMap;
            var fields = map.GetComponent<MapComponent_OilFields>();
            var terrain = DefDatabase<TerrainDef>.GetNamed(MapComponent_OilFields.OilFieldTerrainDefName);
            map.terrainGrid.SetTerrain(FieldCell, terrain);
            map.terrainGrid.SetTerrain(UnrecordedCell, terrain);
            check(fields.FieldAt(UnrecordedCell) == null, "terrain alone does not invent an oil-field record");
            var record = fields.CreateOrReplace(FieldCell, .8f, .65f);
            fields.ReducePressure(record, .05f, .1f);
            check(Math.Abs(record.pressure - .6f) < .0001f, "current field creation and pressure reduction work");

            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"), Faction.OfPlayer, canGeneratePawnRelations: false));
            pawn.questTags ??= new List<string>();
            pawn.questTags.Add(CarrierTag);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 15), map);
            check(map.mapPawns.AllPawnsSpawned.Contains(pawn), "state carrier spawned on the actual map");
            var bagDefinition = DefDatabase<ThingDef>.GetNamed("HD_Apparel_M6RocketBag");
            var bag = (Apparel)ThingMaker.MakeThing(bagDefinition, GenStuff.DefaultStuffFor(bagDefinition));
            pawn.apparel.Wear(bag, dropReplacedApparel: true);
            check(pawn.apparel.WornApparel.Contains(bag), "Helod carrier can wear the loadout harness");
            var ammo = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Rocket_M6A3HEAT"));
            ammo.stackCount = 7;
            check(pawn.inventory.innerContainer.TryAdd(ammo), "physical rockets placed in pawn inventory");
            bag.GetComp<CompM6RocketBag>().SetDesiredCount(ammo.def, 7);
            Verify(check);
        }

        internal static void Verify(Action<bool, string> check)
        {
            Map map = Find.CurrentMap;
            var fields = map.GetComponent<MapComponent_OilFields>();
            var record = fields.FieldAt(FieldCell);
            check(record != null && Math.Abs(record.quality - .8f) < .0001f && Math.Abs(record.pressure - .6f) < .0001f,
                "oil-field cell, quality and pressure survive current save/load");
            check(fields.FieldAt(UnrecordedCell) == null, "unrecorded terrain stays unrecorded after load");
            Pawn pawn = map.mapPawns.AllPawnsSpawned.Single(p => p.questTags?.Contains(CarrierTag) == true);
            var bag = pawn.apparel.WornApparel.Single(a => a.def.defName == "HD_Apparel_M6RocketBag");
            var ammunition = DefDatabase<ThingDef>.GetNamed("HD_Rocket_M6A3HEAT");
            check(bag.GetComp<CompM6RocketBag>().DesiredCountFor(ammunition) == 7, "current ammo target survives save/load");
            check(pawn.inventory.innerContainer.Where(t => t.def == ammunition).Sum(t => t.stackCount) == 7,
                "physical ammo survives without duplication or migration");
            check(typeof(CompM6RocketBag).GetField("storedAmmo", BindingFlags.Instance | BindingFlags.NonPublic) == null
                && typeof(CompM6RocketBag).GetMethod("CompTick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly) == null,
                "ammo bag has no retired storage field or per-tick migration");
            var gasMethod = AccessTools.Method(typeof(GasUtility), nameof(GasUtility.PawnGasEffectsTickInterval));
            check(Harmony.GetPatchInfo(gasMethod)?.Postfixes.Any(p => p.PatchMethod.DeclaringType?.Name == "Patch_HelodGasEffects") == true,
                "custom gas effects bind the actual 1.7 interval API");
            check(DefDatabase<TerrainDef>.GetNamedSilentFail("HD_LowQualityOilRigFloor") == null, "retired oil terrain is absent");
        }
    }
}
