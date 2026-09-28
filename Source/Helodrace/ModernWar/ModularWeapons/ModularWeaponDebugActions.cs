using LudeonTK;
using RimWorld;
using Verse;

namespace Helodrace.ModernWar
{
    public static class ModularWeaponDebugActions
    {
        [DebugAction(
            "Helodrace",
            "Spawn modular M4A1",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnModularM4A1()
        {
            SpawnAndSelect(
                "HD_Gun_M4A1_Weapon",
                "modular M4A1");
        }

        [DebugAction(
            "Helodrace",
            "Spawn modular M16A4",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnModularM16A4()
        {
            SpawnAndSelect(
                "HD_Gun_M16A4_Weapon",
                "modular M16A4");
        }

        [DebugAction("Helodrace", "Spawn modular M16A3",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnModularM16A3()
        {
            SpawnAndSelect("HD_Gun_M16A3_Weapon", "modular M16A3");
        }

        [DebugAction(
            "Helodrace",
            "Spawn modular MP5",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnModularMP5()
        {
            SpawnAndSelect(
                "HD_Gun_MP5_Weapon",
                "modular MP5");
        }

        [DebugAction(
            "Helodrace",
            "Spawn assembled M4 upper receiver",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnAssembledUpperReceiver()
        {
            // This is a normal part Thing. Its Def supplies a barrel and handguard as
            // children, proving that a partial assembly can exist without a gun root.
            SpawnAndSelect(
                "HD_ModularPart_UpperReceiver_M4A1",
                "assembled M4 upper receiver");
        }

        [DebugAction("Helodrace", "Spawn modular M14",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnModularM14()
        {
            SpawnAndSelect("HD_Gun_M14_Weapon", "modular M14");
        }

        [DebugAction("Helodrace", "Spawn modular M1911",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnModularM1911()
        {
            SpawnAndSelect("HD_Gun_M1911_Weapon", "modular M1911");
        }

        [DebugAction("Helodrace", "Spawn modular P320",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnModularP320()
        {
            SpawnAndSelect("HD_Gun_P320_Weapon", "modular P320");
        }

        [DebugAction("Helodrace", "Spawn modular Flux Raider",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnModularFluxRaider()
        {
            SpawnAndSelect(
                "HD_Gun_P320_Weapon",
                "modular Flux Raider",
                "HD_WeaponPreset_P320_RaiderKit");
        }

        private static void SpawnAndSelect(
            string defName, string label, string presetDefName = null)
        {
            Map map = Find.CurrentMap;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (map == null || def == null)
            {
                Messages.Message(
                    "The " + label + " Def is unavailable.",
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map)) cell = map.Center;
            Thing thing = ThingMaker.MakeThing(def);
            if (presetDefName != null)
            {
                ModularWeaponPresetDef preset =
                    DefDatabase<ModularWeaponPresetDef>.GetNamedSilentFail(presetDefName);
                CompModularWeaponNode root = thing.TryGetComp<CompModularWeaponNode>();
                string rejection = null;
                if (preset == null || root == null
                    || !root.TryApplyPreset(preset, out rejection))
                {
                    thing.Destroy(DestroyMode.Vanish);
                    Messages.Message(
                        rejection ?? "The " + label + " preset is unavailable.",
                        MessageTypeDefOf.RejectInput,
                        false);
                    return;
                }
            }
            if (!GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near))
            {
                thing.Destroy(DestroyMode.Vanish);
                Messages.Message(
                    "Could not place the " + label + ".",
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            Find.Selector.ClearSelection();
            Find.Selector.Select(thing);
        }
    }
}
