"""Verify actual CPU-window smoke evidence; never claim the final R7 CPU gate."""
from pathlib import Path
import gzip
import hashlib
import json
import math
import re
import statistics

base = Path(__file__).resolve().parent
sha = "899e853a1fe8501fa77aab69ddb8de8b7100512a8f64288b92bb44b3d0b7718d"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def near(a, b):
    assert math.isclose(a, b, rel_tol=1e-10, abs_tol=1e-8), (a, b)


runs = {}
for kind in ("new", "vanilla", "unprofiled"):
    root = base / f"cpu-windows-{kind}-smoke-01"
    for relative, expected in read(root / "provenance.json")["files"].items():
        raw = (root / relative).read_bytes()
        assert hashlib.sha256(raw).hexdigest() == expected["sha256"], relative
        data = gzip.decompress(raw) if relative.endswith(".gz") else raw
        assert hashlib.sha256(data).hexdigest() == expected["uncompressedSha256"]
        assert len(data) == expected["uncompressedBytes"]
    a, l = read(root / "audit.json"), read(root / "launcher.json")
    assert a["complete"] and a["error"] is None and a["environmentControlled"] and a["isolationVerified"]
    assert a["r7RetiredTypesAbsent"] and a["r7RetiredDefinitionsAbsent"] and not a["unexpectedPawns"]
    assert a["population"] == a["entered"] == 12 and a["units"] == 1 and a["newUnsafeEntries"] == 0
    assert a["r7TickRateMinimum"] == a["r7TickRateMaximum"] == a["speed"] == 3
    assert a["warmupTicks"] == 0 and a["measuredTicks"] == a["sampleTicks"] == 4000
    assert a["fixtureCase"] == "normal" and a["workload"] == "sapper-wall"
    assert l["assemblySha256"] == sha and l["cpuWindows"] and not l["headless"] and not l["reload"]
    assert l["methodProfile"] == (kind != "unprofiled") and l["defaultEngine"] == (kind != "vanilla")
    log = gzip.decompress((root / "Player.log.gz").read_bytes()).decode("utf-8-sig")
    assert not re.search(r"Exception:|Exception while|Error in ", log)
    for key in ("r7ColdMainCpuMs", "r7ColdProcessCpuMs", "r7ColdWallMs"):
        assert math.isfinite(a[key]) and a[key] > 0
    points = a["r7CpuCheckpoints"]
    assert 2 <= len(points) <= 128 and points[0]["label"] == "start" and points[-1]["label"] == "end"
    assert points[0]["tick"] == 1 and points[-1]["tick"] == 4001
    fixed = [p for p in points if p["label"] == "fixed"]
    assert [p["requestedOffset"] for p in fixed] == list(range(500, 4001, 500))
    for p in points:
        assert p["tick"] - points[0]["tick"] == p["requestedOffset"]
        for key in ("mainCpuMs", "processCpuMs", "wallSeconds"):
            assert math.isfinite(p[key]) and p[key] >= 0
        for key in ("tickCalls", "tickCpuMs", "tickElapsedMs"):
            assert p[key] is None if kind == "unprofiled" else p[key] is not None and p[key] >= 0
        if kind != "unprofiled":
            assert p["tickCalls"] == p["tick"] - points[0]["tick"]
    for previous, current in zip(points, points[1:]):
        for key in ("tick", "frame", "mainCpuMs", "processCpuMs", "wallSeconds"):
            assert current[key] >= previous[key]
        if kind != "unprofiled":
            for key in ("tickCalls", "tickCpuMs", "tickElapsedMs"):
                assert current[key] >= previous[key]
    if kind == "vanilla":
        assert a["engine"] == a["effectiveEngine"] == "vanilla" and a["r7FirstVerifiedMissionTick"] == -1
        assert not a["newCommands"] and a["newJobsIssued"] == a["r7SchedulerAdvances"] == 0
        assert all(p["activeCommands"] == -1 and p["phases"] == "vanilla" for p in points)
    else:
        assert a["engine"] == a["effectiveEngine"] == "new" and a["newFunctionalComplete"]
        assert a["newPhysicalPlansValid"] and a["newConnectedStacks"]
        assert a["newEnteredByOrder"] == a["newEntryAssignmentsComplete"] == 12
        milestone, = [p for p in points if p["label"] == "first-verified-mission"]
        assert milestone["requestedOffset"] == a["r7FirstVerifiedMissionTick"] == (1710 if kind == "new" else 1651)
        assert milestone["activeCommands"] == 0 and milestone["phases"] == "Complete:1"
    if kind == "unprofiled":
        assert not (root / "profiles").exists()
        near(points[-1]["mainCpuMs"], a["uninstrumentedMainCpuMs"])
        near(points[-1]["processCpuMs"], a["uninstrumentedProcessCpuMs"])
        c = None
    else:
        c = read(next((root / "profiles").glob("capture-*.json")))
        assert c["schema"] == 9 and c["complete"] and c["dropped"] == 0 and c["assemblySha256"] == sha
        assert c["cpuCheckpoints"] == points and c["startTick"] == 1 and c["endTick"] == 4001
        tick, = [m for m in c["methods"] if m["method"] == "Verse.TickManager.DoSingleTick()"]
        assert tick["cpuMeasured"] and tick["calls"] == 4000 and tick["exceptions"] == 0
        near(points[-1]["tickCpuMs"], tick["threadCpuMs"])
        near(points[-1]["tickElapsedMs"], tick["inclusiveMs"])
        near(points[-1]["mainCpuMs"], c["mainThreadWindowCpuMs"])
        near(points[-1]["processCpuMs"], c["processWindowCpuMs"])
    runs[kind] = (a, l, c, points)

n, v, u = (runs[k] for k in ("new", "vanilla", "unprofiled"))
for key in ("mapFingerprint", "pawnFingerprint", "seed", "speed", "workload", "fixtureVersion"):
    assert n[0][key] == v[0][key] == u[0][key], key
for key in ("assemblySha256", "newJobDefinitionsSha256", "breachDefinitionsSha256", "hammerJobDefinitionsSha256"):
    assert n[1][key] == v[1][key] == u[1][key], key
comparison = read(base / "cpu-windows-smoke-comparison.json")
assert not comparison["unmatchedBaseline"] and not comparison["unmatchedCandidate"]
group, = comparison["groups"]
assert group["baselineRuns"] == group["candidateRuns"] == 1
assert group["within2x"] and group["newAiFunctionalComplete"]
assert not group["newAiPerformanceGateEligible"] and not group["newAiFixedWindowCpuGatePassed"]
near(group["tickCpuRatio"], n[3][-1]["tickCpuMs"] / v[3][-1]["tickCpuMs"])
near(group["windowCpuRatio"], n[3][-1]["mainCpuMs"] / v[3][-1]["mainCpuMs"])
summary = read(base / "cpu-windows-smoke-summary.json")
assert summary["assemblySha256"] == sha and not summary["finalR7Complete"] and not summary["cpuGatePassed"]
for key in ("tickCpuRatio", "windowCpuRatio"):
    near(summary["groupRatios"][key], group[key])
for kind in ("new", "vanilla"):
    a, _, c, points = runs[kind]
    ref = statistics.median(c["reference"]["sampleMs"])
    fixed = [points[0]] + [p for p in points if p["label"] == "fixed"]
    intervals = summary["intervals"][f"cpu-windows-{kind}-smoke-01"]
    assert len(intervals) == len(fixed) - 1
    for previous, current, interval in zip(fixed, fixed[1:], intervals):
        dt = current["tick"] - previous["tick"]
        assert interval["fromTick"] == previous["tick"] and interval["toTick"] == current["tick"]
        assert interval["fromPhase"] == previous["phases"] and interval["toPhase"] == current["phases"]
        near(interval["tickCpuMsPerTick"], (current["tickCpuMs"] - previous["tickCpuMs"]) / dt)
        near(interval["mainCpuMsPerTick"], (current["mainCpuMs"] - previous["mainCpuMs"]) / dt)
        near(interval["rootElapsedCorePercent"], (current["tickElapsedMs"] - previous["tickElapsedMs"]) / dt / ref * 100)
for kind, (a, _, _, _) in runs.items():
    assert summary["coldFixture"][f"cpu-windows-{kind}-smoke-01"] == {
        k: a[k] for k in ("r7ColdMainCpuMs", "r7ColdProcessCpuMs", "r7ColdWallMs")}
np, vp = ([p for p in r[3] if p["label"] == "fixed" and p["requestedOffset"] == 1500][0] for r in (n, v))
near(summary["first1500Ratios"]["tickCpu"], np["tickCpuMs"] / vp["tickCpuMs"])
near(summary["first1500Ratios"]["mainCpu"], np["mainCpuMs"] / vp["mainCpuMs"])
print("PASS: same-DLL actual CPU-window/profile/unprofiled smoke; raw hashes, closed root totals, milestones, interval arithmetic and single-pair exclusion verified. Not final R7/CPU acceptance.")
