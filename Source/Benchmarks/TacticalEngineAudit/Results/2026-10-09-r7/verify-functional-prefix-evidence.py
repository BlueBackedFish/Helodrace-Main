"""Verify first10 final-DLL functional originals, not the full R7 gate."""
from pathlib import Path
import gzip
import hashlib
import json
import re
import sys

builds = {
    "functional-b766-prefix-10": "b766672aebc910fa5ae82e5a7ee4ae477cde4b61485fa5c9324f54a453db21a1",
    "functional-e4e4-prefix-10": "e4e4c4d6621a056a77ca16dfb4a5777a64c0277517887c0bff7593e5a0e4e841",
}
name = sys.argv[1] if len(sys.argv) > 1 else "functional-b766-prefix-10"
assert len(sys.argv) <= 2 and name in builds, "Select one known archived prefix, never mix builds."
root = Path(__file__).resolve().parent / name


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


p = read(root / "provenance.json")
for relative, expected in p["files"].items():
    raw = (root / relative).read_bytes()
    assert hashlib.sha256(raw).hexdigest() == expected["sha256"], relative
    data = gzip.decompress(raw) if relative.endswith(".gz") else raw
    assert hashlib.sha256(data).hexdigest() == expected["uncompressedSha256"]
    assert len(data) == expected["uncompressedBytes"]
j = read(root / "checks.json")
names = p["archivedCases"]
assert len(names) == 10 and len(set(names)) == 10 and len(j["requestedCases"]) == 44
assert not j["finalR7Complete"] and not j["cpuGateEvaluated"] and not j["allSpecifiedPassed"]
assert j["pinned"]["Assemblies\\Helodrace.dll"] == builds[name]
assert [r["name"] for r in j["records"][:10]] == names
assert all(r["status"] == "passed" and r["nativeExceptions"] == 0 for r in j["records"][:10])
minimums = {
    "door": {"newDoorFaults": 1},
    "narrow": {"newContactResponses": 1, "newContactResumes": 1},
    "interrupt": {"interruptionTick": 1},
    "recovery": {"newToolRecoveriesStarted": 1, "newToolRecoveriesCompleted": 1},
    "cutter": {"newCutterJobsStarted": 1},
    "cutter-active-recovery": {"newCutterJobsStarted": 2, "newToolRecoveriesCompleted": 1, "caseCuttingTicks": 20},
    "charge-recovery": {"newChargesInstalled": 1, "newChargeDetonations": 1, "newChargeWaits": 1, "newToolRecoveriesCompleted": 1},
    "charge-fuse-casualty": {"newChargesInstalled": 1, "newChargeDetonations": 1, "newChargeWaits": 1, "newChargeOperatorTransfers": 1},
    "charge-change": {"newChargesInstalled": 1, "newChargeDetonations": 1, "newChargeWaits": 1},
}
content = {"assemblySha256": "Assemblies\\Helodrace.dll", "newJobDefinitionsSha256": "Defs\\Organization\\NewTacticalJobs.xml",
           "breachDefinitionsSha256": "Defs\\ColdWar\\BreachExplosive_ColdWar.xml", "hammerJobDefinitionsSha256": "Defs\\GreatWar\\Sledgehammer_Breach.xml"}
for name in names:
    a, l = read(root / name / "audit.json"), read(root / name / "launcher.json")
    assert a["complete"] and a["error"] is None and a["isolationVerified"] and a["environmentControlled"]
    assert a["newFunctionalComplete"] and a["newPhysicalPlansValid"] and a["newConnectedStacks"]
    assert a["r7RetiredTypesAbsent"] and a["r7RetiredDefinitionsAbsent"] and not a["unexpectedPawns"]
    assert a["newUnsafeEntries"] == 0 and a["newCompletedUnits"] == a["units"] == 1
    assert a["population"] == 12 and a["newEntryAssignmentsComplete"] == a["newEnteredByOrder"] == a["alive"]
    assert 0 <= a["newAllCompleteTick"] <= a["measuredTicks"] == a["sampleTicks"]
    assert a["r7TickRateMinimum"] == a["r7TickRateMaximum"] == 3 and a["warmupTicks"] == 0
    assert a["engine"] == a["effectiveEngine"] == "new" and l["defaultEngine"] and not l["methodProfile"] and not l["headless"]
    assert a["fixtureCase"] == ("normal" if name == "door" else name)
    assert a["workload"] == ("sapper-door" if name == "door" else "sapper-wall")
    for key, file in content.items():
        assert l[key] == j["pinned"][file]
    log = gzip.decompress((root / name / "Player.log.gz").read_bytes()).decode("utf-8-sig")
    assert not re.search(r"Exception:|Exception while|Error in ", log)
    for field, minimum in minimums.get(name, {}).items():
        assert a[field] >= minimum, (name, field)
    if name in ("narrow", "interrupt", "recovery", "cutter-active-recovery", "charge-recovery", "charge-fuse-casualty", "charge-change"):
        assert a["caseTriggered"]
    if name == "narrow":
        assert a["r7FixtureActorWithdrawn"] and a["r7FixtureContactBeforeWithdrawal"]
        assert "narrow defender withdrawn at 6000 observed=True" in log and a["newAllCompleteTick"] > 6000
    if name == "cutter-active-recovery":
        assert a["newActiveCutterRecovered"]
print("PASS:41 original files/SHA and first10 same-DLL actual functional actions. Explicit partial scope; full44, CPU scaling/repeats and final R7 remain unproven.")
