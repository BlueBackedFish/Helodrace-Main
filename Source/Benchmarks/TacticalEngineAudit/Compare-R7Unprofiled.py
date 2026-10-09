"""Read-only comparison of native unprofiled OS main/process CPU windows.

This is a control for the profiled tick-CPU gate, never a replacement for it.
Usage: python Compare-R7Unprofiled.py VANILLA_DIRECTORY NEW_DIRECTORY
Each directory may contain multiple run-*/audit.json files.
"""
import argparse
import gzip
import json
import math
from pathlib import Path
import re
import statistics

CONTENT = {
    "assemblySha256": "Assemblies\\Helodrace.dll",
    "newJobDefinitionsSha256": "Defs\\Organization\\NewTacticalJobs.xml",
    "breachDefinitionsSha256": "Defs\\ColdWar\\BreachExplosive_ColdWar.xml",
    "hammerJobDefinitionsSha256": "Defs\\GreatWar\\Sledgehammer_Breach.xml",
}
CQB_EVENTS = ["3000:(180, 0, 180)", "4200:(114, 0, 108)", "5100:(180, 0, 180)",
              "6500:(99, 0, 108)", "7500:(180, 0, 180)"]
FIELD_EVENTS = ["400:fixed exterior actor exposed (84, 0, 150)",
                "1500:fixed exterior actor approached (84, 0, 125)",
                "2200:fixed exterior actor hidden"]


def require(value, message):
    if not value:
        raise ValueError(message)


def text(path):
    if path.exists():
        return path.read_text(encoding="utf-8-sig")
    return gzip.decompress(Path(str(path) + ".gz").read_bytes()).decode("utf-8-sig")


def finite(value, positive=False):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and (value > 0 if positive else value >= 0)


def read_run(path):
    a = json.loads(text(path / "audit.json"))
    l = json.loads(text(path / "launcher.json"))
    log = text(path / "Player.log")
    require(a.get("complete") and a.get("error") is None and a.get("isolationVerified")
            and a.get("environmentControlled") and a.get("unexpectedPawns") == [], "Incomplete/uncontrolled native run")
    require(a.get("fixtureVersion") in (33, 34) and a.get("fixtureCase") in ("normal", "r7-cqb-cpu", "r6-field-cpu"),
            "Only fixed-input R7 CPU cases are comparable")
    require(a.get("r7RetiredTypesAbsent") and a.get("r7RetiredDefinitionsAbsent")
            and a.get("r7AutoSlowdownDisabled") and a.get("r7WildlifeSpawnerDisabled"), "Retirement/rate/ambient control missing")
    require(not re.search(r"Exception:|Exception while|Error in ", log), "Native exception/error")
    require(l.get("methodProfile") is False and l.get("cpuWindows") is True
            and l.get("headless") is False and a.get("headless") is False
            and not l.get("reload") and not l.get("retainedReload"), "Not an unprofiled normal-graphics CPU-window run")
    engine = a.get("engine")
    require(engine in ("new", "vanilla") and a.get("effectiveEngine") == engine
            and l.get("engine") == engine and a.get("newEngineImplemented")
            and l.get("defaultEngine") == (engine == "new"), "Engine/fallback/default mismatch")
    for field in ("requestedPopulation", "fixtureCase", "workload", "seed", "warmupTicks", "sampleTicks", "speed"):
        require(l.get(field) == a.get(field), "Launcher/audit mismatch: " + field)
    require(a["speed"] in (1, 3) and a.get("r7RateSamples", 0) > 0
            and a.get("r7TickRateMinimum") == a["speed"] == a.get("r7TickRateMaximum"), "Actual speed mismatch")
    require(a.get("warmupTicks") == 0 and a.get("measuredTicks") == a["sampleTicks"]
            and a.get("alive") == a["population"], "Preparation/window/population mismatch")
    for field in CONTENT:
        require(isinstance(l.get(field), str) and re.fullmatch(r"[0-9a-f]{64}", l[field]), "Missing content hash: " + field)
    if a["fixtureCase"] == "r7-cqb-cpu":
        require(a["fixtureVersion"] == 34 and a["sampleTicks"] >= 7500
                and a.get("r7CqbStimulusComplete") and a.get("r7CqbEvents") == CQB_EVENTS, "CQB fixed timeline incomplete")
    if a["fixtureCase"] == "r6-field-cpu":
        require(a["sampleTicks"] >= 2200 and a.get("newFieldEvents") == FIELD_EVENTS, "Field fixed timeline incomplete")
    if a["population"] > 0:
        require(a.get("moved", 0) >= a["population"] * .95 and a.get("entered", 0) > 0, "No actual movement/entry progression")
        if a["workload"] == "sapper-wall":
            require(a.get("sapperEligiblePawns", 0) > 0, "No eligible vanilla sapper")
        if engine == "new":
            require(a.get("newFunctionalComplete") and a.get("newPhysicalPlansValid")
                    and a.get("newConnectedStacks") and a.get("newCompletedUnits") == a["units"]
                    and a.get("newEnteredByOrder") == a["population"] and a.get("newUnsafeEntries") == 0,
                    "New AI functional/safety proof incomplete")
    else:
        require(a.get("units") == 0 and a.get("newJobsIssued") == 0 and a.get("r7SchedulerAdvances") == 0
                and a.get("newCommands") == [], "Empty fixture performed tactical work")
    points = a.get("r7CpuCheckpoints")
    require(isinstance(points, list) and 2 <= len(points) <= 128
            and points[0]["label"] == "start" and points[-1]["label"] == "end"
            and points[-1]["tick"] - points[0]["tick"] == a["measuredTicks"], "CPU window bounds missing/inconsistent")
    for p in points:
        require(all(p.get(k) is None for k in ("tickCalls", "tickCpuMs", "tickElapsedMs")),
                "Unprofiled data must not invent tick-root CPU")
        require(all(finite(p.get(k)) for k in ("mainCpuMs", "processCpuMs", "wallSeconds", "tick")), "Invalid CPU sample")
        require(p["processCpuMs"] >= p["mainCpuMs"], "Process CPU below main CPU")
    require(points[0]["mainCpuMs"] == points[0]["processCpuMs"] == 0, "CPU window did not start at zero")
    for x, y in zip(points, points[1:]):
        # Fixed, first-completion and end boundaries can share a native tick.
        require(y["tick"] >= x["tick"] and all(y[k] >= x[k] for k in ("mainCpuMs", "processCpuMs", "wallSeconds")), "Nonmonotonic CPU windows")
    end = points[-1]
    for field, key in (("uninstrumentedMainCpuMs", "mainCpuMs"), ("uninstrumentedProcessCpuMs", "processCpuMs")):
        require(finite(a.get(field), positive=True) and a[field] == end[key], "CPU total/checkpoint mismatch: " + field)
    require(all(finite(a.get(k)) for k in ("r7ColdMainCpuMs", "r7ColdProcessCpuMs", "r7ColdWallMs")), "Missing fixture-initialization cost")
    game = re.findall(r"^RimWorld .+$", log, re.M)
    unity = re.findall(r"^Initialize engine version: .+$", log, re.M)
    mods = re.findall(r"Initializing new game with mods:\s*((?:\s*  - [^\r\n]+[\r\n]+)+)", log)
    require(len(game) == len(unity) == len(mods) == 1, "Runtime/mod evidence missing")
    identity = [arg for arg in l.get("arguments", []) if "-savedatafolder=" in arg]
    require(len(identity) == 1, "Native run identity missing")
    args = l["arguments"]
    graphics = ["-screen-fullscreen", "0", "-screen-width", "800", "-screen-height", "600"]
    require(any(args[i:i + len(graphics)] == graphics for i in range(len(args)))
            and "-nographics" not in args and "-batchmode" not in args, "Graphics window conditions differ")
    conditions = {k: a[k] for k in ("fixtureVersion", "fixtureCase", "requestedPopulation", "population", "units",
                                      "radioOperators", "workload", "mapFingerprint", "pawnFingerprint", "seed",
                                      "speed", "warmupTicks", "sampleTicks")}
    require(conditions["mapFingerprint"] and conditions["pawnFingerprint"], "Geometry/gear fingerprint missing")
    conditions.update({k: l[k] for k in CONTENT})
    conditions.update(high=l["high"], game=game[0].strip(), unity=unity[0].strip(),
                      mods=re.findall(r"  - ([^\r\n]+)", mods[0]))
    return {"path": str(path), "identity": identity[0], "engine": engine, "conditions": conditions, "audit": a,
            "mainCpuMsPerTick": end["mainCpuMs"] / a["measuredTicks"],
            "processCpuMsPerTick": end["processCpuMs"] / a["measuredTicks"]}


def compare(baseline, candidate):
    require(baseline and candidate and all(r["engine"] == "vanilla" for r in baseline)
            and all(r["engine"] == "new" for r in candidate), "Need actual vanilla baseline and new candidate")
    require(len({r["identity"] for r in baseline + candidate}) == len(baseline + candidate), "Duplicate native run identity")
    require(all(r["conditions"] == baseline[0]["conditions"] for r in baseline + candidate), "Build/input/runtime conditions differ")

    def stats(runs, field):
        values = [r[field] for r in runs]
        return {"median": statistics.median(values), "min": min(values), "max": max(values), "values": values}

    bm, cm = stats(baseline, "mainCpuMsPerTick"), stats(candidate, "mainCpuMsPerTick")
    bp, cp = stats(baseline, "processCpuMsPerTick"), stats(candidate, "processCpuMsPerTick")
    ratio = cm["median"] / bm["median"]
    eligible = len(baseline) >= 3 and len(candidate) >= 3
    common = set.intersection(*[{p["requestedOffset"] for p in r["audit"]["r7CpuCheckpoints"] if p["label"] == "fixed"}
                               for r in baseline + candidate])
    fixed = []
    for offset in sorted(common):
        costs = []
        for runs in (baseline, candidate):
            values = []
            for r in runs:
                ps = r["audit"]["r7CpuCheckpoints"]
                p = next(p for p in ps if p["label"] == "fixed" and p["requestedOffset"] == offset)
                values.append(p["mainCpuMs"] / (p["tick"] - ps[0]["tick"]))
            costs.append(statistics.median(values))
        fixed.append({"offset": offset, "baselineMainCpuMsPerTick": costs[0], "candidateMainCpuMsPerTick": costs[1],
                      "ratio": costs[1] / costs[0] if costs[0] > 0 else None})
    return {"scope": "Unprofiled whole-main-thread CPU control only", "finalR7Complete": False, "tickCpuGateEvaluated": False,
            "warning": "No tick-root CPU, method timing or Core reference in unprofiled runs. Final audit must separately verify alternating matrix order and the current functional-build pins. Fixed checkpoints sample phases; first completion does not prove permanent idle. Fixture initialization excludes Unity/world boot.",
            "conditions": baseline[0]["conditions"], "baselineRuns": len(baseline), "candidateRuns": len(candidate),
            "baselineMainCpuMsPerTick": bm, "candidateMainCpuMsPerTick": cm, "mainCpuRatio": ratio,
            "baselineProcessCpuMsPerTick": bp, "candidateProcessCpuMsPerTick": cp,
            "repeatedControlEligible": eligible, "within2x": ratio <= 2, "unprofiledMainControlPassed": eligible and ratio <= 2,
            "fixedCumulativeWindows": fixed,
            "baselineEvidence": baseline, "candidateEvidence": candidate}


def load(root):
    paths = sorted(root.rglob("audit.json"))
    require(paths, "No native audit.json files: " + str(root))
    return [read_run(p.parent) for p in paths]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(compare(load(args.baseline), load(args.candidate)), ensure_ascii=False, indent=2, allow_nan=False))
    except (ValueError, KeyError, OSError) as error:
        parser.exit(1, "Comparison rejected: " + str(error) + "\n")
