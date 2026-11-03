"""Reproducible theoretical oil flows from current XML; no game balance edits.

python Source/Analysis/StageI/oil_theory.py --game-root <installed 1.7 directory>
"""
from pathlib import Path
import argparse
import csv
import json
import struct
import re
import xml.etree.ElementTree as ET

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager

ROOT = Path(__file__).resolve().parents[3]
DAY, RARE = 60000, 250


def f32(x):
    return struct.unpack("f", struct.pack("f", x))[0]


def definitions(folder):
    result = {}
    for path in folder.rglob("*.xml"):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue
        for node in root:
            name = node.findtext("defName")
            if name:
                result[name] = node
    return result


def comp(node, suffix):
    return next(x for x in node.findall("comps/li") if x.get("Class", "").endswith(suffix))


def number(node, path):
    return float(node.findtext(path))


def model(label, pump, quality, tower, recipe, prices, steam_rate, idle, days=90):
    producer = comp(pump, "CompProperties_OilProducer")
    amount = int(number(producer, "amount"))
    period = number(producer, "daysToProduce")
    loss = number(producer, "pressureLossPerProduction")
    floor = number(producer, "minimumPressure")
    work = number(recipe, "workAmount") / number(tower, "statBases/WorkTableWorkSpeedFactor")
    intake = number(recipe, "ingredients/li/count")
    outputs = {x.tag: float(x.text) for x in recipe.find("products")}
    tower_power = number(comp(tower, "CompProperties_Power"), "basePowerConsumption")
    fuel_rate = number(comp(tower, "CompProperties_IndustrialRefuelable"), "fuelConsumptionRate")
    electric_pump = pump.find("comps/li[@Class='CompProperties_Power']")
    pump_watts = number(electric_pump, "basePowerConsumption") if electric_pump is not None else 0
    wood_per_day = steam_rate if electric_pump is None else 0
    mechanical = pump.find("comps/li[@Class='Helodrace.CompProperties_MechanicalUser']")
    shaft_watts = number(mechanical, "mechanicalPowerRequired") if mechanical is not None else 0
    pressure, progress, stock = f32(1), f32(0), 0
    events, jobs = [], []
    last_finish = 0
    for tick in range(RARE, days * DAY + 1, RARE):
        speed = f32(f32(quality) * pressure)  # rated mechanical speed = 1.
        progress = f32(progress + f32(f32(RARE / DAY) * speed))
        if progress >= period:
            progress = f32(max(0, progress - period))
            pressure = f32(max(f32(floor), f32(pressure - f32(loss))))
            events.append((tick, amount, pressure))
            stock += amount
            while stock >= intake:
                begin = max(tick, last_finish)
                last_finish = begin + work
                jobs.append((begin, last_finish))
                stock -= intake
    assert abs(events[0][0] - DAY * period / quality) <= RARE * 2
    assert all(floor - 1e-6 <= p <= 1 for _, _, p in events)
    rows = []
    for day in range(1, days + 1):
        tick = day * DAY
        extracted = sum(n for t, n, _ in events if t <= tick)
        begun = sum(a <= tick for a, b in jobs)
        finished = sum(b <= tick for a, b in jobs)
        active_ticks = sum(max(0, min(tick, b) - a) for a, b in jobs if a < tick)
        burned = fuel_rate * active_ticks / DAY
        row = dict(case=label, day=day, crude_extracted=extracted,
                   crude_stock=extracted - intake * begun, crude_in_process=intake * (begun - finished),
                   refinery_batches=finished, pressure=next((p for t, _, p in reversed(events) if t <= tick), 1),
                   refinery_work_hours=active_ticks / 2500,
                   diesel_burned=burned, wood_used=wood_per_day * day,
                   shaft_watts=shaft_watts,
                   electrical_kwh=(tower_power * idle * day * 24 + tower_power * (1-idle) * active_ticks / 2500 + pump_watts * day * 24) / 1000)
        for name, qty in outputs.items():
            row[name] = qty * finished
        row["diesel_net"] = row["HD_Diesel"] - burned
        row["net_product_base_value"] = sum(row[n] * prices[n] for n in outputs) - burned * prices["HD_Diesel"]
        row["chemfuel_kerosene_base_value"] = row["Chemfuel"] * prices["Chemfuel"] + row["HD_Kerosene"] * prices["HD_Kerosene"]
        row["example_notes_at_sell_0_7"] = row["chemfuel_kerosene_base_value"] * .7 / 5.25
        assert extracted == row["crude_stock"] + row["crude_in_process"] + intake * finished
        assert 0 <= begun - finished <= 1
        rows.append(row)
    initial_raw = amount * quality / period
    return rows, dict(case=label, quality=quality, pressure_start=1, pressure_floor=floor,
                      rated_crude_per_day=initial_raw, floor_crude_per_day=initial_raw*floor,
                      first_crude_day=events[0][0]/DAY, first_refined_day=jobs[0][1]/DAY,
                      refinery_raw_capacity_per_day=DAY/work*intake,
                      diesel_per_refinery_batch=fuel_rate*work/DAY,
                      peak_electrical_watts=pump_watts+tower_power,
                      wood_per_day=wood_per_day, products=outputs)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--game-root", type=Path, default=Path(r"C:/Program Files (x86)/Steam/steamapps/common/RimWorld"))
    args = parser.parse_args()
    defs = definitions(ROOT / "Defs")
    core = definitions(args.game_root / "Data/Core/Defs")
    prices = {n: number((defs if n in defs else core)[n], "statBases/MarketValue")
              for n in ["Chemfuel", "HD_Naphtha", "HD_Kerosene", "HD_Diesel", "WoodLog"]}
    tower, recipe = defs["HD_DistillationTower"], defs["HD_FractionateHeavyCrudeOil"]
    steam_rate = number(comp(defs["HD_SmallSteamEngine"], "CompProperties_IndustrialRefuelable"), "fuelConsumptionRate")
    source = (ROOT / "Source/Helodrace/Mechanical/IndustrialWorktablePower.cs").read_text(encoding="utf-8-sig")
    idle = float(re.search(r"public float idlePowerFactor = ([\d.]+)f;", source)[1])
    a, meta_a = model("A · 80% 유정 / 목제 펌프잭", defs["HD_BasicPumpJack"], .8, tower, recipe, prices, steam_rate, idle)
    b, meta_b = model("B · 120% 유정 / 철제 펌프잭", defs["HD_MetalPumpJack"], 1.2, tower, recipe, prices, steam_rate, idle)
    # Continuous initial steady-state bounds, independent of batch/pressure simulation.
    assert meta_a["rated_crude_per_day"] == 40 and meta_b["rated_crude_per_day"] == 90
    assert meta_a["refinery_raw_capacity_per_day"] == 1500
    assert abs(meta_a["diesel_per_refinery_batch"] - 2/3) < 1e-9
    out = ROOT / "Docs/Data/StageI"
    out.mkdir(parents=True, exist_ok=True)
    with (out / "oil_daily.csv").open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(a[0])); writer.writeheader(); writer.writerows(a+b)
    (out / "oil_model.json").write_text(json.dumps(dict(prices=prices, cases=[meta_a,meta_b],
        assumptions=dict(pressure_recovery=False, external_electricity=True, rated_RPM=True, labor_speed=1,
                         travel_and_hauling=False, power_grace_and_polling=False,
                         initial_diesel_seed_required=True)), ensure_ascii=False, indent=2), encoding="utf-8")
    font = Path("C:/Windows/Fonts/malgun.ttf")
    if font.exists(): font_manager.fontManager.addfont(str(font)); plt.rcParams["font.family"] = "Malgun Gothic"
    plt.rcParams["axes.unicode_minus"] = False
    plt.rcParams["svg.hashsalt"] = "Helod-StageI-Oil"
    fig, axes = plt.subplots(2, 2, figsize=(13.5, 9), layout="constrained")
    colors = ["#9a661f", "#187b91"]
    for rows, meta, color in zip([a,b], [meta_a,meta_b], colors):
        days = [r["day"] for r in rows]
        axes[0,0].plot(days, [r["crude_extracted"] for r in rows], label=meta["case"], color=color, linewidth=2)
        axes[0,1].plot(days, [r["pressure"]*100 for r in rows], label=meta["case"], color=color, linewidth=2)
        axes[1,1].plot(days, [r["electrical_kwh"] for r in rows], label=meta["case"], color=color, linewidth=2)
    axes[0,0].set(title="누적 원유 추출 · 압력 하강 포함", xlabel="경과 일", ylabel="원유 개수"); axes[0,0].legend(fontsize=8)
    axes[0,1].set(title="유정 압력 · 바닥 35%", xlabel="경과 일", ylabel="압력 (%)", ylim=(30,103))
    labels = ["화학연료", "나프타", "등유", "디젤 순산출"]
    for index, (rows, color) in enumerate(zip([a,b], colors)):
        r=rows[29]; values=[r["Chemfuel"],r["HD_Naphtha"],r["HD_Kerosene"],r["diesel_net"]]
        axes[1,0].bar([x+index*.36 for x in range(4)], values, width=.36, color=color, label="AB"[index])
    axes[1,0].set(title="30일 정제 산출 · 탑 디젤 소모 차감", ylabel="생산 개수", xticks=[x+.18 for x in range(4)], xticklabels=labels); axes[1,0].legend()
    axes[1,1].set(title="누적 외부 전력 요구량 · 탑 유휴 10% 포함", xlabel="경과 일", ylabel="kWh")
    for ax in axes.flat: ax.grid(axis="y", alpha=.18); ax.set_axisbelow(True)
    fig.suptitle("유정 1개 + 증류탑 1개 이론 비교", fontsize=19)
    fig.text(.5, -.018, f"초기 압력 100% · 정격 동력 · 작업자 속도 100% · 목제 펌프용 증기기관은 목재 {steam_rate:g}개/일 별도 사용 · 실측 아님", ha="center", fontsize=10)
    fig.savefig(out / "oil_comparison.png", dpi=160, bbox_inches="tight")
    svg = out / "oil_comparison.svg"
    fig.savefig(svg, bbox_inches="tight", metadata={"Date": None})
    svg.write_text("\n".join(line.rstrip() for line in svg.read_text(encoding="utf-8").splitlines()) + "\n", encoding="utf-8")
    plt.close(fig)
    print(json.dumps(dict(cases=[meta_a,meta_b], day30=[a[29],b[29]], day90=[a[89],b[89]]), ensure_ascii=False, indent=2))


if __name__ == "__main__": main()
