using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace;

internal static class WorktableLoad
{
    internal static bool IsActive(int lastWorkTick)
    {
        int now = Find.TickManager.TicksGame;
        return lastWorkTick >= 0 && now >= lastWorkTick && now - lastWorkTick <= 90;
    }
}

public class CompProperties_IndustrialWorktablePower : CompProperties
{
    public float idlePowerFactor = .1f;
    public CompProperties_IndustrialWorktablePower() { compClass = typeof(CompIndustrialWorktablePower); }
    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        foreach (string error in base.ConfigErrors(parentDef)) yield return error;
        if (idlePowerFactor < 0 || idlePowerFactor > 1) yield return "idle power factor must be between zero and one";
    }
}

// Keep the native CompPowerTrader: ThingDef.ConnectToPower checks its exact type.
public class CompIndustrialWorktablePower : ThingComp
{
    private int lastWorkTick = -1;
    private CompPowerTrader power;
    public bool IsWorking => WorktableLoad.IsActive(lastWorkTick);
    public float IdlePowerFactor => ((CompProperties_IndustrialWorktablePower)props).idlePowerFactor;
    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        power = parent.GetComp<CompPowerTrader>();
        RefreshLoad();
    }
    public void NotifyUsedThisTick()
    {
        bool active = IsWorking;
        lastWorkTick = Find.TickManager.TicksGame;
        if (!active) RefreshLoad();
    }
    public override void CompTick()
    {
        base.CompTick();
        if (parent.IsHashIntervalTick(60)) RefreshLoad();
    }
    public override void CompTickRare() { base.CompTickRare(); RefreshLoad(); }
    public override void ReceiveCompSignal(string signal)
    {
        base.ReceiveCompSignal(signal);
        if (signal == CompPowerTrader.PowerTurnedOnSignal || signal == CompPowerTrader.PowerTurnedOffSignal
            || signal == CompFlickable.FlickedOnSignal || signal == CompFlickable.FlickedOffSignal) RefreshLoad();
    }
    private void RefreshLoad()
    {
        if (power == null) return;
        // Prospective demand while unpowered lets the native grid budget a restart.
        power.PowerOutput = parent.Spawned && FlickUtility.WantsToBeOn(parent) && !parent.IsBrokenDown()
            ? -power.Props.PowerConsumption * (IsWorking ? 1f : IdlePowerFactor) : 0f;
    }
    public override string CompInspectStringExtra() => power == null ? null
        : "HD_IndustrialWorktable_Power".Translate(power.Props.PowerConsumption.ToString("F0"),
            (power.Props.PowerConsumption * IdlePowerFactor).ToString("F0")).Resolve();
}

[HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.UsedThisTick))]
internal static class IndustrialWorktableUsage
{
    private static void Postfix(Building_WorkTable __instance)
    {
        __instance.GetComp<CompMechanicalUser>()?.NotifyUsedThisTick();
        __instance.GetComp<CompIndustrialWorktablePower>()?.NotifyUsedThisTick();
    }
}