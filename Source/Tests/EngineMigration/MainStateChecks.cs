using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Helodrace.ModernWar;

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
            CreateEquipment(pawn, check);
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
            VerifyEquipment(pawn, check);
        }

        private static void CreateEquipment(Pawn pawn, Action<bool, string> check)
        {
            var preset = DefDatabase<ModularArmorPresetDef>.GetNamed("HD_ArmorPreset_IBTV_Rifleman");
            var armor = (Apparel)ThingMaker.MakeThing(preset.armorDef, GenStuff.DefaultStuffFor(preset.armorDef));
            var armorComp = armor.GetComp<CompModularArmor>();
            check(armorComp.TryApplyPreset(preset, out string reason), "actual armor preset applied: " + reason);
            var record = armorComp.InstalledParts.First(p => p.CanSwapFrontRearPlates && p.PlateSet?.TryGetComp<CompArmorPlateSet>() != null);
            var plates = record.PlateSet.TryGetComp<CompArmorPlateSet>();
            plates.ApplyDamage(1, 2);
            plates.ApplyDamage(2, 5);
            check(record.SwapFrontRearPlates(), "front/rear plate order changed through current API");
            check(pawn.inventory.innerContainer.TryAdd(armor), "configured armor stored in actual inventory");
            File.WriteAllText(SnapshotPath("armor"), StablePreset(ModularPresetXmlExporter.ExportArmor(armorComp)));

            var weaponDef = DefDatabase<ThingDef>.GetNamed("HD_Gun_M16A4_Weapon");
            var weapon = (ThingWithComps)ThingMaker.MakeThing(weaponDef, GenStuff.DefaultStuffFor(weaponDef));
            check(pawn.inventory.innerContainer.TryAdd(weapon), "default modular weapon stored in actual inventory");
            File.WriteAllText(SnapshotPath("weapon"), StablePreset(ModularPresetXmlExporter.ExportWeapon(weapon.GetComp<CompModularWeaponNode>())));
        }

        private static void VerifyEquipment(Pawn pawn, Action<bool, string> check)
        {
            var armor = (ThingWithComps)pawn.inventory.innerContainer.Single(t => t.def.defName == "HD_Apparel_IBTVAssault");
            var armorComp = armor.GetComp<CompModularArmor>();
            string actualArmor = StablePreset(ModularPresetXmlExporter.ExportArmor(armorComp));
            File.WriteAllText(SnapshotPath("armor").Replace("-before-save", "-current"), actualArmor);
            check(actualArmor == File.ReadAllText(SnapshotPath("armor")),
                "armor parts, PALS panels and current grid coordinates survive save/load");
            var record = armorComp.InstalledParts.Single(p => p.CanSwapFrontRearPlates && p.PlateSet?.TryGetComp<CompArmorPlateSet>() != null);
            var plates = record.PlateSet.TryGetComp<CompArmorPlateSet>();
            check(plates.HitPointsFor(1) == Math.Max(0, plates.MaxHitPointsPerPlate - 2)
                && plates.HitPointsFor(2) == Math.Max(0, plates.MaxHitPointsPerPlate - 5),
                "independent current plate durability survives without upgrade scaling");
            check(record.plateOrderSwapped && record.PlateNumberForFacing(ModularArmorFacing.Front) == 2,
                "front/rear plate order survives current save/load");
            check(typeof(InstalledModularArmorPart).GetField("directionalPlates") == null
                && typeof(CompArmorPlateSet).GetField("durabilityVersion", BindingFlags.Instance | BindingFlags.NonPublic) == null
                && typeof(CompModularArmor).GetField("palsGridDoubled", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                "retired directional plate and PALS migration schema absent");

            var weapon = (ThingWithComps)pawn.inventory.innerContainer.Single(t => t.def.defName == "HD_Gun_M16A4_Weapon");
            var weaponComp = weapon.GetComp<CompModularWeaponNode>();
            string actualWeapon = StablePreset(ModularPresetXmlExporter.ExportWeapon(weaponComp));
            File.WriteAllText(SnapshotPath("weapon").Replace("-before-save", "-current"), actualWeapon);
            check(actualWeapon == File.ReadAllText(SnapshotPath("weapon")),
                "current weapon sockets, mounts and instance offsets survive save/load");
            check(weaponComp.RenderSnapshot().Count > 3, "actual weapon render tree builds without legacy rail reassignment");
            check(typeof(ModularAttachmentMount).GetField("railOccupancy") == null,
                "mount schema contains only current contact and physical lengths");
        }

        private static string SnapshotPath(string name)
        {
            if (!GenCommandLine.TryGetCommandLineArg("hdMigrationOutput", out string folder)) throw new InvalidOperationException("Missing evidence directory");
            return Path.Combine(folder, name + "-before-save.xml");
        }

        private static string StablePreset(string xml)
        {
            var document = XDocument.Parse(xml);
            // Export names contain the current timestamp, independent of equipment state.
            document.Root.Elements().Single().Element("defName").Value = "MigrationSnapshot";
            return document.ToString(SaveOptions.DisableFormatting);
        }
    }
}
