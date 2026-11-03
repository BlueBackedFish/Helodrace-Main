"""Current XML oil/diesel balance at the user's 100%-quality, 85%-pressure reference.
Run from the repository root; output is theoretical continuous throughput, not AI uptime.
"""
import csv
import json
import math
from pathlib import Path
from oil_theory import ROOT, definitions, comp, number
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager


def main():
    defs = definitions(ROOT / "Defs")
    pump = comp(defs["HD_MetalPumpJack"], "CompProperties_OilProducer")
    cable = comp(defs["HD_CableToolRig"], "CompProperties_OilFieldDevelopment")
    rotary = comp(defs["HD_RotaryDrill"], "CompProperties_OilFieldDevelopment")
    engine = defs["HD_IndustrialDieselEngine"]
    fuel = comp(engine, "CompProperties_IndustrialRefuelable")
    tower = defs["HD_DistillationTower"]
    recipe = defs["HD_FractionateHeavyCrudeOil"]
    nominal = number(pump, "amount") / number(pump, "daysToProduce")
    work_ticks = number(recipe, "workAmount") / number(tower, "statBases/WorkTableWorkSpeedFactor")
    intake = number(recipe, "ingredients/li/count")
    tower_fuel = number(comp(tower, "CompProperties_IndustrialRefuelable"), "fuelConsumptionRate")
    burn_batch = tower_fuel * work_ticks / 60000
    diesel_batch = number(recipe, "products/HD_Diesel")
    engine_daily = number(fuel, "fuelConsumptionRate")
    net_per_crude = (diesel_batch - burn_batch) / intake
    baseline_crude = 2 * nominal * 1 * .85
    baseline_net = baseline_crude * net_per_crude
    break_pressure = engine_daily / (2 * nominal * net_per_crude)
    rows = []
    for quality in [.75, .8, 1, 1.2, 1.3]:
        for pressure in [1, .85, .7, .35]:
            raw = 2 * nominal * quality * pressure
            rows.append(dict(pumps=2, quality=quality, pressure=pressure, crude_per_day=raw,
                diesel_gross=raw / intake * diesel_batch,
                tower_diesel=raw / intake * burn_batch,
                diesel_after_tower=raw * net_per_crude, engine_diesel=engine_daily,
                diesel_surplus=raw * net_per_crude - engine_daily))
    pump_watts = number(comp(defs["HD_MetalPumpJack"], "CompProperties_Power"), "basePowerConsumption")
    tower_watts = number(comp(tower, "CompProperties_Power"), "basePowerConsumption")
    generator_props = comp(defs["HD_EarlyIndustrialGenerator"], "CompProperties_MechanicalGenerator")
    rated = number(generator_props, "ratedMechanicalInput")
    # Defaults are the product's actual CompProperties_MechanicalGenerator values.
    peak = float(generator_props.findtext("peakEfficiency", ".8"))
    minimum = float(generator_props.findtext("minimumEfficiency", ".6"))
    target = 2 * pump_watts + tower_watts
    a = (peak - minimum) / rated
    shaft = 2 * target / (minimum + math.sqrt(minimum**2 + 4 * a * target))
    normal = number(comp(engine, "CompProperties_MechanicalEmitter"), "recommendedPower")
    assert number(cable, "minimumQuality") == .75 and number(cable, "maximumQuality") == .8
    assert number(rotary, "minimumQuality") == 1 and number(rotary, "maximumQuality") == 1.3
    assert nominal == 75 and engine_daily == 20
    assert abs(baseline_crude - 127.5) < 1e-8 and abs(baseline_net - 24.65) < 1e-8
    assert baseline_net >= engine_daily * 1.2 and .68 < break_pressure < .70
    assert shaft < normal and abs(target - 4800) < .01
    # One best legal rotary well at the reference pressure must not replace the intended pair.
    assert nominal * 1.3 * .85 * net_per_crude < engine_daily
    data = dict(reference=dict(pumps=2, quality=1, pressure=.85, crude_per_day=baseline_crude,
        diesel_gross=baseline_crude / intake * diesel_batch, tower_diesel=baseline_crude / intake * burn_batch,
        diesel_net=baseline_net, engine_diesel=engine_daily, surplus=baseline_net-engine_daily,
        supply_margin_over_consumption=baseline_net/engine_daily-1,
        minimum_relative_uptime=engine_daily/baseline_net,
        break_even_pressure=break_pressure, engine_tank_days=number(fuel,"fuelCapacity")/engine_daily,
        tower_work_hours_per_day=baseline_crude/intake*work_ticks/2500,
        generator_target_W=target, generator_shaft_W=shaft, engine_normal_shaft_W=normal),
        assumptions=dict(reference_pressure_is_not_locked=True, tower_speed=1.5,
            worker_speed=1, hauling_and_refill_delay=False, seed_diesel_required=True,
            tower_grace_and_polling_excluded=True), rows=rows)
    out = ROOT / "Docs/Data/StageI"
    (out / "oil_engine_balance.json").write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    with (out / "oil_engine_balance.csv").open("w",encoding="utf-8-sig",newline="") as stream:
        writer=csv.DictWriter(stream,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
    font=Path("C:/Windows/Fonts/malgun.ttf")
    if font.exists(): font_manager.fontManager.addfont(str(font)); plt.rcParams["font.family"]="Malgun Gothic"
    plt.rcParams["axes.unicode_minus"]=False
    fig,ax=plt.subplots(figsize=(10,5.6),layout="constrained")
    for quality,color in zip([.8,1,1.2,1.3],["#a67c52","#147d92","#3f73c2","#8260b5"]):
        pressures=list(range(35,101));ax.plot(pressures,[2*nominal*quality*p/100*net_per_crude for p in pressures],label=f"품질 {quality:.0%} 유정 2기",color=color,linewidth=2)
    ax.axhline(engine_daily,color="#bb3e36",linestyle="--",label="디젤 엔진 소비 20개/일")
    ax.axvline(85,color="#75828c",linestyle=":")
    ax.scatter([85],[baseline_net],color="#147d92",zorder=5)
    ax.annotate("기준: 100% 유정 × 2기, 압력 85%\n순디젤 24.65 / 소비 20 / 여유 4.65",(85,baseline_net),xytext=(40,31),arrowprops=dict(arrowstyle="->",color="#147d92"),fontsize=10)
    ax.scatter([break_pressure*100],[engine_daily],color="#bb3e36",zorder=5)
    ax.annotate(f"100% 유정 2기 손익분기 압력 {break_pressure:.1%}",(break_pressure*100,engine_daily),xytext=(37,15),arrowprops=dict(arrowstyle="->",color="#bb3e36"),fontsize=9)
    ax.set(xlabel="유정 압력 (%)",ylabel="증류탑 연료 차감 후 디젤 (개/일)",title="철제 펌프잭 2기 + 증류탑 → 디젤 엔진 1기",xlim=(35,100),ylim=(0,40))
    ax.grid(alpha=.2);ax.legend(loc="lower right",fontsize=9)
    fig.text(.5,-.02,"펌프 정격 75 원유/일 · 작업 속도 100% · 연속 환산 이론값 · 실제 압력은 추출마다 감소",ha="center",fontsize=9)
    fig.savefig(out / "oil_engine_balance.png",dpi=160,bbox_inches="tight");plt.close(fig)
    print(json.dumps(data["reference"],ensure_ascii=False,indent=2))

if __name__ == "__main__": main()
