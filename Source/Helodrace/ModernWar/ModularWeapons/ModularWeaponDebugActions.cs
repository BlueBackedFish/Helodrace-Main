using LudeonTK;
using RimWorld;
using Verse;

namespace Helodrace.ModernWar
{
    public static class ModularWeaponDebugActions
    {
        [DebugAction(
            "Helodrace",
            "Spawn modular M4 development carbine",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnDevelopmentCarbine()
        {
            SpawnAndSelect(
                "HD_Gun_ModularM4_Test_Weapon",
                "modular M4 development carbine");
        }

        [DebugAction(
            "Helodrace",
            "Spawn modular M16A4 development rifle",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnDevelopmentM16A4()
        {
            SpawnAndSelect(
                "HD_Gun_ModularM16A4_Test_Weapon",
                "modular M16A4 development rifle");
        }

        [DebugAction(
            "Helodrace",
            "Spawn modular MP5 development submachine gun",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnDevelopmentMP5()
        {
            SpawnAndSelect(
                "HD_Gun_ModularMP5_Test_Weapon",
                "modular MP5 development submachine gun");
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

        private static void SpawnAndSelect(string defName, string label)
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
