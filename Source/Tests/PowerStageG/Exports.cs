using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests.PowerStageG;

public sealed partial class Checks
{
    private static string Csv(params object[] cells) => string.Join(",", cells.Select(v => "\"" +
        (v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : v?.ToString() ?? "").Replace("\"", "\"\"") + "\""));
    private void ExportBudgetTables()
    {
        var devices = new List<string> { "def,electric_rated_W,electric_waiting_W,electric_generation_capacity_W,mechanical_rated_W,mechanical_waiting_W,rated_RPM,rated_torque_Nm,source_normal_W,source_max_W,fuel_units_per_day,fuel_capacity,allowed_fuels,default_generator_target_W,default_generator_shaft_W" };
        var recipes = new List<string> { "table,recipe,products,reference_work_ticks,electric_Wd_per_batch,mechanical_Wd_per_batch,process_fuel_per_batch,primary_product,primary_count,electric_Wd_per_primary_unit,mechanical_Wd_per_primary_unit" };
        foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("HD_") && typeof(Building).IsAssignableFrom(d.thingClass)).OrderBy(d => d.defName))
        {
            var power = def.comps?.OfType<CompProperties_Power>().FirstOrDefault();
            var user = def.comps?.OfType<CompProperties_MechanicalUser>().FirstOrDefault();
            var source = def.comps?.OfType<CompProperties_MechanicalEmitter>().FirstOrDefault();
            var fuel = def.comps?.OfType<CompProperties_Refuelable>().FirstOrDefault();
            var generator = def.comps?.OfType<CompProperties_MechanicalGenerator>().FirstOrDefault();
            if (power == null && user == null && source == null && fuel == null) continue;
            float electric = MathF.Max(0, power?.PowerConsumption ?? 0), mechanical = user?.mechanicalPowerRequired ?? 0;
            var industrial = def.comps?.OfType<CompProperties_IndustrialWorktablePower>().FirstOrDefault();
            float electricIdle = electric * (industrial?.idlePowerFactor ?? 1f);
            if (industrial != null) Check(def.ConnectToPower && power.compClass == typeof(CompPowerTrader), "Industrial workshop retains the native electrical connection contract: " + def.defName);
            float mechanicalIdle = def.IsWorkTable && user != null ? mechanical * user.idlePowerFactor : mechanical;
            if (generator != null) mechanicalIdle = MechanicalPowerMath.MechanicalInputFor(generator.defaultTargetElectricalOutput, generator.ratedMechanicalInput, generator.minimumEfficiency, generator.peakEfficiency);
            float rpm = user?.recommendedRPM ?? (source != null ? generatorBuilding.GetComp<CompMechanicalUser>().Network.NominalGridRPM : 0f);
            float torquePower = user?.mechanicalPowerRequired ?? source?.recommendedPower ?? 0f;
            devices.Add(Csv(def.defName, electric, electricIdle, MathF.Max(0f, -(power?.PowerConsumption ?? 0f)), mechanical, mechanicalIdle, rpm,
                MechanicalPowerMath.TorqueForPower(torquePower, rpm), source?.recommendedPower ?? 0,
                source?.maxPossiblePower ?? 0, fuel?.fuelConsumptionRate ?? 0, fuel?.fuelCapacity ?? 0,
                fuel == null ? "" : string.Join("|", fuel.fuelFilter.AllowedThingDefs.Select(d => d.defName).OrderBy(n => n)),
                generator?.defaultTargetElectricalOutput ?? 0, generator == null ? 0 : mechanicalIdle));
            if (!def.IsWorkTable || electric == 0 && mechanical == 0 && fuel == null) continue;
            float speed = def.GetStatValueAbstract(StatDefOf.WorkTableWorkSpeedFactor, GenStuff.DefaultStuffFor(def));
            foreach (var recipe in def.AllRecipes.Where(r => r.products?.Count > 0).OrderBy(r => r.defName))
            {
                var product = recipe.products[0];
                float ticks = recipe.WorkAmountForStuff(GenStuff.DefaultStuffFor(product.thingDef)) / MathF.Max(.01f, speed);
                float days = ticks / GenDate.TicksPerDay;
                recipes.Add(Csv(def.defName, recipe.defName, string.Join("|", recipe.products.Select(p => p.thingDef.defName + ":" + p.count)),
                    ticks, electric * days, mechanical * days, (fuel?.fuelConsumptionRate ?? 0) * days,
                    product.thingDef.defName, product.count, electric * days / product.count, mechanical * days / product.count));
            }
        }
        File.WriteAllLines(Path.Combine(output, "devices.csv"), devices);
        File.WriteAllLines(Path.Combine(output, "recipe-energy.csv"), recipes);
        Write("EXPORT: " + (devices.Count - 1) + " powered/fuelled devices; " + (recipes.Count - 1) + " worktable-recipe energy rows");
    }
}