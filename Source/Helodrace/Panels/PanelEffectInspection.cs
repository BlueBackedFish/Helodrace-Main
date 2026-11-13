using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Panels
{
    [HarmonyPatch(typeof(ThingWithComps),nameof(ThingWithComps.GetInspectString))]
    public static class PanelEffectInspection
    {
        static void Postfix(ThingWithComps __instance,ref string __result)
        {
            if(!__instance.Spawned)return;
            if(__instance is Building_WallPanel panel)
            {
                if(panel.Properties.thermalResistance>0)
                    __result+="\n"+"HD_Panel_ThermalResistance".Translate((PanelRules.IsActive(panel)?panel.Properties.thermalResistance*(panel.GetComp<CompPolymerCore>()?.FractionRemaining??1):0).ToString("F1"));
                if(panel.Properties.electromagneticShielding) __result+="\n"+"HD_Panel_ShieldLining".Translate();
                if(panel.Properties.aramid) __result+="\n"+"HD_Panel_AramidEffect".Translate();
            }
            else if(__instance is Building host && PanelRules.IsHost(host.def))
            {
                float ns=PanelThermal.Resistance(host,0),ew=PanelThermal.Resistance(host,1);
                if(ns>0||ew>0)__result+="\n"+"HD_Panel_HeatTransferAxes".Translate((100*PanelThermal.Factor(ns)).ToString("F1"),(100*PanelThermal.Factor(ew)).ToString("F1"));
            }
            if(__instance.TryGetComp<CompPower>()==null)return;
            ShieldingReport report=__instance.Map.GetComponent<MapComponent_PanelShielding>().Report(__instance.GetRoom());
            if(report.coveredFaces>0)
                __result+="\n"+"HD_Panel_ShieldCoverage".Translate(report.coveredFaces,report.totalFaces,report.roofHoles,report.openDoors+report.leaks);
            if(PanelShielding.SolarActive(__instance.Map))
                __result+="\n"+(PanelShielding.Protected(__instance)?"HD_Panel_SolarProtected":"HD_Panel_SolarExposed").Translate();
        }
    }
}
