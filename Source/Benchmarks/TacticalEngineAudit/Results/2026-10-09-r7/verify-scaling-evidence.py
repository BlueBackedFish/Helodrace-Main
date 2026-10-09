"""Check preserved failed scale runs and speed/idle smoke tests; no final CPU gate."""
from pathlib import Path
import gzip
import hashlib
import json
import re

base = Path(__file__).resolve().parent


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


native = read(base / "native-speed-control-source.json")
native_il = (base / "native-speed-control-il.txt").read_bytes()
assert hashlib.sha256(native_il).hexdigest() == native["excerptSha256"]
assert b"Verse.DebugViewSettings.neverForceNormalSpeed" in native_il
assert b"Verse.TimeSlower.get_ForcedNormalSpeed" in native_il


def run(name, fixture_version=33, controlled=True):
    directory = base / name
    provenance = read(directory / "provenance.json")
    for relative, expected in provenance["files"].items():
        raw = (directory / relative).read_bytes()
        assert hashlib.sha256(raw).hexdigest() == expected["sha256"], relative
        data = gzip.decompress(raw) if relative.endswith(".gz") else raw
        assert hashlib.sha256(data).hexdigest() == expected["uncompressedSha256"]
        assert len(data) == expected["uncompressedBytes"]
    audit = read(directory / "audit.json")
    captures = list((directory / "profiles").glob("capture-*.json"))
    assert len(captures) == 1
    capture = read(captures[0])
    launcher = read(directory / "launcher.json")
    assert audit["complete"] and audit["error"] is None and audit["isolationVerified"]
    assert capture["complete"] and capture["dropped"] == 0 and all(m["exceptions"] == 0 for m in capture["methods"])
    assert not launcher["headless"] and launcher["methodProfile"]
    assert audit["fixtureVersion"] == capture["benchmark"]["fixtureVersion"] == fixture_version
    assert audit["environmentControlled"] == controlled
    assert (not audit["unexpectedPawns"]) == controlled
    assert audit["r7RetiredTypesAbsent"] and audit["r7RetiredDefinitionsAbsent"]
    assert audit["speed"] == capture["speed"] == launcher["speed"]
    assert audit["r7RateSamples"] > 0
    assert f'-hdTacticalAuditSpeed={audit["speed"]}' in launcher["arguments"]
    assert capture["assemblySha256"] == launcher["assemblySha256"]
    tick = next(m for m in capture["methods"] if m["method"].startswith("Verse.TickManager.DoSingleTick("))
    ticks = capture["endTick"] - capture["startTick"]
    assert tick["cpuMeasured"] and tick["threadCpuMs"] > 0 and tick["calls"] == ticks
    assert audit["sampleTicks"] <= ticks <= audit["sampleTicks"] + 10
    return audit, capture, launcher


failed = [run(f"scale-408-{engine}-3x-attempt-01") for engine in ("new", "vanilla")]
new, vanilla = (r[0] for r in failed)
for audit, capture, launcher in failed:
    assert launcher["assemblySha256"] == "2da9297b9d72d33c4740e8846725e532ff75b17035c5cd0af756708fed0c99f8"
    assert audit["population"] == audit["alive"] == 408 and audit["units"] == 34
    assert audit["r7TickRateMinimum"] == 1 and audit["r7TickRateMaximum"] == 3
    assert "r7AutoSlowdownDisabled" not in audit  # Before speed-control correction.
assert new["mapFingerprint"] == vanilla["mapFingerprint"] and new["pawnFingerprint"] == vanilla["pawnFingerprint"]
assert not new["newFunctionalComplete"] and new["newCompletedUnits"] == 6
assert new["newEnteredByOrder"] == 116 and new["newUnsafeEntries"] == 0
assert new["r7MaximumDueDelay"] == 14 and new["r7MaximumDueCommands"] == 16
assert sum(":Pending opening=" in command for command in new["newCommands"]) == 18
print("PASS: matched 408-pawn diagnostic preserves failed progress and variable speed; excluded from CPU gate.")

hashes = set()
for speed in (1, 3):
    audit, capture, launcher = run(f"speed-empty-new-{speed}x")
    hashes.add(capture["assemblySha256"])
    assert launcher["defaultEngine"] and not any(arg.startswith("-hdTacticalEngine=") for arg in launcher["arguments"])
    assert audit["population"] == audit["units"] == 0 and not audit["newCommands"]
    assert audit["newJobsIssued"] == audit["r7SchedulerAdvances"] == 0
    assert audit["r7AutoSlowdownDisabled"]
    assert audit["r7TickRateMinimum"] == audit["r7TickRateMaximum"] == speed
    assert not audit["newFunctionalComplete"]  # No fabricated nonempty entry success.
    print("PASS: actual native speed", speed, "and zero-pawn idle; smoke test, not full scaling/CPU gate.")
assert hashes == {"4dce23a94f2fc490eac51c648f7abe64f32b041c3ed90860cd1076ba71186fa9"}

for name, completed, entered, pending, sha in (
    ("scale-408-new-3x-long-attempt-02", 25, 335, 3,
     "4dce23a94f2fc490eac51c648f7abe64f32b041c3ed90860cd1076ba71186fa9"),
    ("scale-408-gapfix-new-3x-attempt-03", 17, 252, 9,
     "45193f460c7606f8b2ef0787cf5bf4bf6d8ac4fb2463190b0a17446903a1bc96"),
    ("scale-408-release-approach-new-3x-attempt-04", 24, 313, 3,
     "893bed172034c8b4d73640430a48dc8b83f00a1870d9f7fbc61a60c7515ae9b6"),
):
    audit, capture, launcher = run(name)
    assert launcher["assemblySha256"] == sha
    assert audit["population"] == audit["alive"] == 408 and audit["units"] == 34
    assert audit["r7AutoSlowdownDisabled"]
    assert audit["r7TickRateMinimum"] == audit["r7TickRateMaximum"] == 3
    assert not audit["newFunctionalComplete"]
    assert audit["newCompletedUnits"] == completed and audit["newEnteredByOrder"] == entered
    assert sum(":Pending opening=" in command for command in audit["newCommands"]) == pending
    assert audit["newUnsafeEntries"] == 0
    assert audit["newContactResponses"] == audit["newContactResumes"]
    if "gapfix" in name:
        assert any("(100, 0, 104)=" in item and ":Clear:" in item for item in audit["r7PortalLeases"])
        assert any("(97, 0, 105)=" in item and "HD_Raid_15::" in item for item in audit["r7ClaimOwners"])
        assert len(audit["r7CommandLayers"]) == len(audit["newCommands"])
    if "release-approach" in name:
        assert audit["newJobFailures"] == 176
        assert not any(":Clear:" in lease for lease in audit["r7PortalLeases"])
        retirement = next(m for m in capture["methods"] if ".RetireEntryApproach(" in m["method"])
        assert retirement["calls"] == 77 and retirement["inclusiveMs"] > 0
    print("PASS:", name, "preserves actual fixed speed, incomplete progress and diagnostic-only classification.")

audit, capture, launcher = run("wide-opening")
assert audit["fixtureCase"] == "wide-opening" and audit["caseTriggered"]
assert audit["population"] == audit["alive"] == 12 and audit["units"] == 1
assert audit["newWideOpeningReused"] and audit["newFunctionalComplete"]
assert audit["newCompletedUnits"] == 1 and audit["newEnteredByOrder"] == 12
assert audit["newConnectedStacks"] and audit["newPhysicalPlansValid"] and audit["newRoomProgressComplete"]
assert audit["newUnsafeEntries"] == 0
assert "opening=(100, 0, 118)" in audit["newCommands"][0]
assert audit["r7AutoSlowdownDisabled"] and audit["r7TickRateMinimum"] == audit["r7TickRateMaximum"] == 3
assert any("TacticalLocalPlanner.Find(" in method["method"] for method in capture["methods"])
print("PASS: native five-cell opening regression; 12-pawn functional proof, not final CPU/scaling gate.")

audit, capture, launcher = run("release-approach-contact", fixture_version=18)
assert launcher["assemblySha256"] == "55d75dbce0c264091a2d32af4711efd4996c1663d90f5d4da320957b73423bb9"
assert audit["fixtureCase"] == "r4-contact-drill" and audit["population"] == audit["alive"] == 12
assert audit["newFunctionalComplete"] and audit["newCompletedUnits"] == 1 and audit["newEnteredByOrder"] == 12
assert all(audit[key] for key in ("newContactDrillComplete", "newContactMemoryFrozen",
                                "newContactPlanPreserved", "newUnseenDoorIgnored"))
assert audit["newContactResumes"] == 3 and audit["newUnsafeEntries"] == 0
assert not audit["r7PortalLeases"] and not audit["r7ClaimOwners"]
assert next(m for m in capture["methods"] if ".RetireEntryApproach(" in m["method"])["calls"] == 3
print("PASS: final approach/overlay cleanup DLL completes native three-direction contact regression; not full R7/CPU gate.")

audit, capture, launcher = run("scale-408-busy-fallback-new-3x-attempt-05")
assert launcher["assemblySha256"] == "12d4f08526fa1d5547ce30a3e06290b13092bc537ddf44d6c3652a30149334ca"
assert audit["population"] == audit["alive"] == 408 and audit["units"] == 34
assert not audit["newFunctionalComplete"] and audit["newCompletedUnits"] == 15
assert audit["newEnteredByOrder"] == 276 and audit["newBusyOpeningFallbacks"] == 308
assert audit["newUnsafeEntries"] == 0 and audit["r7TickRateMinimum"] == audit["r7TickRateMaximum"] == 3
assert sum(":Pending opening=" in c for c in audit["newCommands"]) == 5
print("PASS: busy-only fallback executes in native 408-pawn test; incomplete overall progress remains excluded.")

audit, capture, launcher = run("busy-fallback-assignment")
assert launcher["assemblySha256"] == "613f39692396ce1919d6f85fbc55db08575baeb32df98e086f6a2da57f47283f"
assert audit["population"] == audit["alive"] == 48 and audit["units"] == 4
assert not audit["newFunctionalComplete"] and audit["newCompletedUnits"] == 2
assert len(audit["r7MissionAssignments"]) == 4
assert sum(":agreement=Finished:" in c and ":areaSecured=True:" in c for c in audit["r7MissionAssignments"]) == 2
assert any(":phase=Complete:" in c and ":goalSecured=False:" in c for c in audit["r7MissionAssignments"])
assert sum(":Enter:" in c and ":contact=(120, 0, 110):" in c for c in audit["r7CommandLayers"]) == 2
assert audit["newContactResponses"] > audit["newContactResumes"]
print("PASS: actual cooperative area completion and live-contact holds recorded separately; no whole-mission pass fabricated.")

wildlife = read(base / "native-wildlife-spawner-source.json")
raw = (base / wildlife["excerpt"]).read_bytes()
assert hashlib.sha256(raw).hexdigest() == wildlife["excerptSha256"]
il = gzip.decompress(raw)
assert hashlib.sha256(il).hexdigest() == wildlife["uncompressedSha256"]
assert b"METHOD RimWorld.WildAnimalSpawner.WildAnimalSpawnerTick" in il
assert b"noAnimals" not in il

cqb_events = ["3000:(180, 0, 180)", "4200:(114, 0, 108)", "5100:(180, 0, 180)",
              "6500:(99, 0, 108)", "7500:(180, 0, 180)"]
for name, functional, physical in (("timed-cqb-new-48-attempt-01", False, False),
                                    ("timed-cqb-new-48-attempt-02", True, True)):
    audit, capture, launcher = run(name, fixture_version=34)
    assert audit["population"] == audit["alive"] == 48 and audit["units"] == 4
    assert audit["fixtureCase"] == "r7-cqb-cpu" and audit["r7CqbStimulusComplete"]
    assert audit["r7CqbEvents"] == cqb_events
    assert audit["newCompletedUnits"] == 4 and audit["newEnteredByOrder"] == 48
    assert audit["newPhysicalPlansValid"] == physical and audit["newFunctionalComplete"] == functional
    assert audit["newUnsafeEntries"] == 0 and audit["r7TickRateMinimum"] == audit["r7TickRateMaximum"] == 3
    assert "r7WildlifeSpawnerDisabled" not in audit  # Before the ambient-spawner correction.
    print("PASS:", name, "preserves footprint failure/fixed functional evidence; not final CPU gate.")

rejected, rejected_capture, rejected_launcher = run("timed-cqb-vanilla-48-attempt-01", fixture_version=34, controlled=False)
fixed, fixed_capture, fixed_launcher = run("timed-cqb-new-48-attempt-02", fixture_version=34)
assert rejected["unexpectedPawns"] == ["Crow38712:Crow"]
assert rejected["r7CqbStimulusComplete"] and rejected["r7CqbEvents"] == cqb_events
assert rejected_launcher["assemblySha256"] == fixed_launcher["assemblySha256"]
assert rejected["mapFingerprint"] == fixed["mapFingerprint"] and rejected["pawnFingerprint"] == fixed["pawnFingerprint"]
assert "Uncontrolled ambient pawns/incidents" in read(base / "timed-cqb-48-rejected-comparison.json")["error"]
print("PASS: same-build CQB Vanilla ambient crow and rejected comparison preserved; no CPU pass fabricated.")

audit, capture, launcher = run("timed-cqb-controlled-vanilla-48-3x", fixture_version=34)
assert audit["engine"] == "vanilla" and audit["population"] == audit["alive"] == audit["entered"] == 48
assert audit["r7WildlifeSpawnerDisabled"] and audit["r7CqbStimulusComplete"] and audit["r7CqbEvents"] == cqb_events
assert audit["r7TickRateMinimum"] == audit["r7TickRateMaximum"] == 3
assert not audit["newCommands"] and audit["newJobsIssued"] == audit["r7SchedulerAdvances"] == 0
print("PASS: corrected native Vanilla controls actual wildlife routine; zero ambient pawns and all fixed inputs/48 entries.")

new, nc, nl = run("timed-cqb-controlled-new-48-3x", fixture_version=34)
vanilla, vc, vl = run("timed-cqb-controlled-vanilla-48-3x", fixture_version=34)
assert nc["assemblySha256"] == vc["assemblySha256"] == "7ec756256741d4617817ef87d0f1c365e27e19b2edc8cb54710f29dc3e8a4ef1"
assert new["mapFingerprint"] == vanilla["mapFingerprint"] and new["pawnFingerprint"] == vanilla["pawnFingerprint"]
assert new["r7WildlifeSpawnerDisabled"] and new["r7CqbStimulusComplete"] and new["r7CqbEvents"] == cqb_events
assert new["newCompletedUnits"] == 4 and new["newEnteredByOrder"] == 48 and new["newFunctionalComplete"]
assert new["newPhysicalPlansValid"] and new["newConnectedStacks"] and new["newRoomProgressComplete"]
assert new["newAllCompleteTick"] == 5953 and new["newJobFailures"] == 41 and new["newUnsafeEntries"] == 0
assert new["r7TickRateMinimum"] == new["r7TickRateMaximum"] == 3
group = read(base / "timed-cqb-controlled-48-single-comparison.json")["groups"][0]
assert group["baselineRuns"] == group["candidateRuns"] == 1
assert group["within2x"] and group["newAiFunctionalComplete"]
assert not group["newAiPerformanceGateEligible"] and not group["newAiFixedWindowCpuGatePassed"]
def cpu_per_tick(capture, window=False):
    cpu = capture["mainThreadWindowCpuMs"] if window else next(m["threadCpuMs"] for m in capture["methods"]
        if m["method"].startswith("Verse.TickManager.DoSingleTick("))
    return cpu / (capture["endTick"] - capture["startTick"])
assert abs(group["tickCpuRatio"] - cpu_per_tick(nc) / cpu_per_tick(vc)) < 1e-12
assert abs(group["windowCpuRatio"] - cpu_per_tick(nc, True) / cpu_per_tick(vc, True)) < 1e-12
print("PASS: controlled same-build 48-pawn CPU pair matches raw OS CPU; single-run evidence remains ineligible for final gate.")

audit, capture, launcher = run("timed-cqb-controlled-new-408-3x-attempt-01", fixture_version=34)
assert capture["assemblySha256"] == nc["assemblySha256"]
assert launcher["defaultEngine"] and not any(arg.startswith("-hdTacticalEngine=") for arg in launcher["arguments"])
assert audit["population"] == audit["alive"] == 408 and audit["units"] == 34
assert audit["r7CqbStimulusComplete"] and audit["r7CqbEvents"] == cqb_events and audit["r7WildlifeSpawnerDisabled"]
assert audit["r7TickRateMinimum"] == audit["r7TickRateMaximum"] == 3
assert not audit["newFunctionalComplete"] and not audit["newRoomProgressComplete"]
assert not audit["newPhysicalPlansValid"] and not audit["newConnectedStacks"]
assert audit["newCompletedUnits"] == 24 and audit["newEnteredByOrder"] == 312
assert sum(":Pending opening=" in command for command in audit["newCommands"]) == 3
assert audit["newBusyOpeningFallbacks"] == 302 and audit["newJobFailures"] == 116
assert audit["newContactResponses"] == audit["newContactResumes"] == 6 and audit["newUnsafeEntries"] == 0
assert all(":contact=none:" in s and ":contactRestoring=False:" in s for s in audit["r7CommandLayers"])
assert audit["r7MaximumDueDelay"] == 10 and audit["r7MaximumDueCommands"] == 16
print("PASS: fixed-input 408-pawn test preserves unfinished progress after enemy retreat; not a final performance/functional pass.")

fixed, fc, fl = run("identification-phase-new-408-3x-attempt-02", fixture_version=34)
assert fc["assemblySha256"] == "281597c35a2442688efd3fb753b99036f4994e1eed81cac9544075db3f43a198"
assert fixed["mapFingerprint"] == audit["mapFingerprint"] and fixed["pawnFingerprint"] == audit["pawnFingerprint"]
assert fixed["r7CqbStimulusComplete"] and fixed["r7CqbEvents"] == cqb_events and fixed["r7WildlifeSpawnerDisabled"]
assert fixed["population"] == fixed["alive"] == 408 and fixed["units"] == 34
assert fixed["r7TickRateMinimum"] == fixed["r7TickRateMaximum"] == 3
assert not fixed["newFunctionalComplete"] and fixed["newAllCompleteTick"] == -1
assert fixed["newCompletedUnits"] == 30 and fixed["newEnteredByOrder"] == 360 and fixed["newEverEnteredByOrder"] == 384
assert sum(":Pending opening=" in command for command in fixed["newCommands"]) == 2
assert sum(":identifying=True:" in command for command in fixed["r7CommandLayers"]) == 2
assert fixed["newJobFailures"] == 64 and fixed["newUnsafeEntries"] == 0
assert fixed["newContactResponses"] == fixed["newContactResumes"] == 10
assert fixed["r7MaximumDueDelay"] == 10 and fixed["r7MaximumDueCommands"] == 16
old_repeated = [re.findall(r"\(-?\d+, 0, -?\d+\)", value) for value in audit["newSecuredPortals"]]
assert sum(len(portals) != len(set(portals)) for portals in old_repeated) == 3
for value in fixed["newSecuredPortals"]:
    portals = re.findall(r"\(-?\d+, 0, -?\d+\)", value)
    assert len(portals) == len(set(portals))
assert next(m for m in fc["methods"] if ".AdvanceCoordination(" in m["method"])["calls"] == 8126
print("PASS: identification fix retains unique current opening histories and records 30/34 completion without claiming final CPU/progression success.")
