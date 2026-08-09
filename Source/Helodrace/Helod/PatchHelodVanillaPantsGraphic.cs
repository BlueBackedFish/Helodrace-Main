using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(
        typeof(ApparelGraphicRecordGetter),
        nameof(ApparelGraphicRecordGetter.TryGetGraphicApparel))]
    public static class PatchHelodVanillaPantsGraphic
    {
        private const string HelodRaceDefName = "Helod";
        private const string PantsDefName = "Apparel_Pants";
        private const string FlakPantsDefName = "Apparel_FlakPants";
        private const string FlakVestDefName = "Apparel_FlakVest";
        private const string FemaleBodyTypeDefName = "Female";
        private const string PantsGraphicPath =
            "Helod/VanillaApparel/Pants/Pants_Female";
        private const string FlakPantsGraphicPath =
            "Helod/VanillaApparel/FlakPants/FlakPants_Female";
        private const string FlakVestGraphicPath =
            "Helod/VanillaApparel/FlakVest/FlakVest_Female";

        [HarmonyPostfix]
        public static void Postfix(
            Apparel apparel,
            BodyTypeDef bodyType,
            ref ApparelGraphicRecord rec,
            ref bool __result)
        {
            if (apparel?.Wearer?.def?.defName != HelodRaceDefName
                || bodyType?.defName != FemaleBodyTypeDefName)
            {
                return;
            }

            string graphicPath;
            switch (apparel.def.defName)
            {
                case PantsDefName:
                    graphicPath = PantsGraphicPath;
                    break;
                case FlakPantsDefName:
                    graphicPath = FlakPantsGraphicPath;
                    break;
                case FlakVestDefName:
                    graphicPath = FlakVestGraphicPath;
                    break;
                default:
                    return;
            }

            Graphic graphic = GraphicDatabase.Get<Graphic_Multi>(
                graphicPath,
                ShaderDatabase.CutoutComplex,
                Vector2.one,
                apparel.DrawColor);
            rec = new ApparelGraphicRecord(graphic, apparel);
            __result = true;
        }
    }
}
