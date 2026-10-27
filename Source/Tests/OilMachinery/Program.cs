using System;
using System.Linq;
using Helodrace;
using RimWorld;
using Verse;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Building Machine(Map map, params ThingComp[] comps)
    {
        var b = new Building { Map = map, Spawned = true };
        map.listerBuildings.allBuildingsColonist.Add(b);
        foreach (var c in comps) { c.parent = b; b.comps.Add(c); }
        foreach (var c in comps) c.PostSpawnSetup(false);
        return b;
    }
    static void Main()
    {
        var map = new Map();
        var fuel = new CompRefuelable { HasFuel = true };
        var flick = new CompFlickable { SwitchIsOn = true };
        var drill = new CompOilFieldDevelopment { props = new CompProperties_OilFieldDevelopment { totalDaysNeeded = 3, minimumQuality = 1, maximumQuality = 1.5f } };
        var rig = Machine(map, flick, fuel, drill);
        Check(drill.IsWorking, "Fueled rotary rig must run without a shaft network.");
        drill.CompTick();
        Check(fuel.Notifications == 1 && drill.currentProgressDays > 0, "Active drilling consumes fuel and advances.");
        float paused = drill.currentProgressDays;
        drill.needsMaintenance = true; drill.CompTick();
        Check(fuel.Notifications == 1 && drill.currentProgressDays == paused, "Maintenance must stop fuel use and drilling.");
        var worker = new WorkGiver_DevelopOilField(); var pawn = new Pawn { Map = map };
        Check(worker.PotentialWorkThingsGlobal(pawn).Contains(rig) && worker.HasJobOnThing(pawn, rig), "Rotary rig must receive the existing maintenance job.");
        drill.Notify_WorkPerformed();
        Check(!worker.HasJobOnThing(pawn, rig), "A serviced rig must not immediately request maintenance again.");
        fuel.HasFuel = false; drill.CompTick();
        Check(fuel.Notifications == 1 && drill.currentProgressDays == paused, "Empty fuel tank must pause progress.");
        fuel.HasFuel = true; flick.SwitchIsOn = false; drill.CompTick();
        Check(!drill.IsWorking && fuel.Notifications == 1, "Switched-off rig must not consume fuel.");
        flick.SwitchIsOn = true;
        for (int i = 0; i < 721; i++) drill.CompTickRare();
        Check(drill.developmentComplete && map.Fields.Field != null, "Drilling must create the field at its guaranteed completion time.");
        drill.CompTick(); Check(fuel.Notifications == 1, "Completed drilling must not consume fuel.");
        Check(!worker.HasJobOnThing(pawn, rig, true), "Completed rig must not receive maintenance jobs.");

        var electric = new CompPowerTrader { PowerOn = true };
        var producer = new CompOilProducer { props = new CompProperties_OilProducer { amount = 100, product = new ThingDef() } };
        var pump = Machine(map, electric, new CompFlickable { SwitchIsOn = true }, producer);
        // The field can be below another footprint cell after a differently-sized rig is removed.
        map.Fields.FieldPosition = new IntVec3(1, 0, 0);
        for (int i = 0; i < 241; i++) producer.CompTickRare();
        Check(GenPlace.Last?.stackCount == 100 && map.Fields.PressureReductions == 1, "Electric pump must produce oil and reduce field pressure.");
        Check(map.Fields.Field.pressure < 1, "Oil extraction must deplete pressure.");
        paused = producer.progressDays; electric.PowerOn = false; producer.CompTickRare();
        Check(producer.progressDays == paused, "Power outage must pause electric extraction.");
        electric.PowerOn = true; pump.GetComp<CompFlickable>().SwitchIsOn = false; producer.CompTickRare();
        Check(producer.progressDays == paused, "Flick switch must pause extraction.");

        var shaft = new CompMechanicalUser { HasPower = true };
        var oldProducer = new CompOilProducer { props = new CompProperties_OilProducer { amount = 50, product = new ThingDef() } };
        var basic = Machine(map, shaft, oldProducer);
        oldProducer.CompTickRare(); Check(oldProducer.progressDays > 0, "Basic mechanical pump must still operate.");
        paused = oldProducer.progressDays; shaft.HasPower = false; oldProducer.CompTickRare();
        Check(oldProducer.progressDays == paused, "Disconnected mechanical pump must stop.");
        Machine(map); Check(worker.PotentialWorkThingsGlobal(pawn).Count() == 1, "Maintenance scanner must exclude unrelated buildings.");
        Check(!OilMachineOperation.IsPowered(new Building()), "A machine without a supply must remain off.");

        var heat = new CompOperatingHeatPusher { props = new CompProperties_OperatingHeatPusher() };
        heat.parent = pump; pump.GetComp<CompFlickable>().SwitchIsOn = true;
        heat.CompTickRare(); Check(GenTemperature.Heat > 0, "Powered electric machinery must emit operating heat.");
        float previousHeat = GenTemperature.Heat; electric.PowerOn = false; heat.CompTickRare();
        Check(GenTemperature.Heat == previousHeat, "Unpowered machinery must not emit operating heat.");
        heat.parent = rig; heat.CompTickRare();
        Check(GenTemperature.Heat == previousHeat, "Completed drilling must not emit heat.");
        Console.WriteLine("PASS: diesel drilling/fuel pauses, completion, rotary maintenance coverage, electric extraction/pressure, mechanical regression, and operating heat.");
    }
}
