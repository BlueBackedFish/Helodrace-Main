"""Verify actual narrow-room contact and post-withdrawal entry; preserve failure."""
from pathlib import Path
import gzip
import hashlib
import json
import re

base = Path(__file__).resolve().parent


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def verify(root):
    for relative, expected in read(root / "provenance.json")["files"].items():
        raw = (root / relative).read_bytes()
        assert hashlib.sha256(raw).hexdigest() == expected["sha256"], relative
        data = gzip.decompress(raw) if relative.endswith(".gz") else raw
        assert hashlib.sha256(data).hexdigest() == expected["uncompressedSha256"]
        assert len(data) == expected["uncompressedBytes"]
    j = read(root / "checks.json")
    a, l = read(root / "narrow/audit.json"), read(root / "narrow/launcher.json")
    assert not j["finalR7Complete"] and not j["cpuGateEvaluated"]
    assert j["pinned"]["Assemblies\\Helodrace.dll"] == l["assemblySha256"]
    assert a["complete"] and a["error"] is None and a["environmentControlled"] and a["isolationVerified"]
    assert a["population"] == 12 and a["units"] == 1 and a["fixtureCase"] == "narrow"
    assert a["newPhysicalPlansValid"] and a["newConnectedStacks"] and a["newUnsafeEntries"] == a["newJobFailures"] == 0
    assert a["r7TickRateMinimum"] == a["r7TickRateMaximum"] == 3 and a["measuredTicks"] == 10000
    assert not l["methodProfile"] and not l["headless"] and l["defaultEngine"]
    log = gzip.decompress((root / "narrow/Player.log.gz").read_bytes()).decode("utf-8-sig")
    assert not re.search(r"Exception:|Exception while|Error in ", log)
    return j, a, l, log


old, before, old_launcher, _ = verify(base / "functional-queue-899e-attempt-01")
assert old["fullFunctionalQueue"] and not old["allSpecifiedPassed"] and len(old["requestedCases"]) == 44
assert [(r["name"], r["status"]) for r in old["records"]] == [("door", "passed"), ("narrow", "failed")]
assert old["records"][1]["error"] == "Full native functional completion failed."
assert not before["newFunctionalComplete"] and before["newContactResponses"] == 1 and before["newContactResumes"] == 0
assert before["newContactShots"] == 45 and before["newEnteredByOrder"] == before["newEntryAssignmentsComplete"] == 0
assert "contactLastSeen=9977" in before["r7CommandLayers"][0]
assert old_launcher["assemblySha256"] == "899e853a1fe8501fa77aab69ddb8de8b7100512a8f64288b92bb44b3d0b7718d"
new, after, launcher, log = verify(base / "narrow-withdrawal-01")
assert not new["fullFunctionalQueue"] and new["allSpecifiedPassed"] and new["requestedCases"] == ["narrow"]
assert new["records"][0]["status"] == "passed" and new["records"][0]["nativeExceptions"] == 0
assert launcher["assemblySha256"] == "b766672aebc910fa5ae82e5a7ee4ae477cde4b61485fa5c9324f54a453db21a1"
for field in ("r7FixtureActorWithdrawn", "r7FixtureContactBeforeWithdrawal", "caseTriggered", "newFunctionalComplete"):
    assert after[field], field
assert "R7 narrow defender withdrawn at 6000 observed=True" in log
assert after["newContactResponses"] == after["newContactResumes"] == 1 and after["newContactShots"] == 27
assert after["newEnteredByOrder"] == after["newEntryAssignmentsComplete"] == 12
assert after["newCompletedUnits"] == 1 and after["newAllCompleteTick"] == 6455
for key in ("mapFingerprint", "pawnFingerprint", "seed", "warmupTicks", "sampleTicks", "population", "speed"):
    assert before[key] == after[key], key
for key in ("newJobDefinitionsSha256", "breachDefinitionsSha256", "hammerJobDefinitionsSha256"):
    assert old_launcher[key] == launcher[key], key
print("PASS: failed permanent-defender narrow fixture preserved; same initial geometry/pawns, actual contact/shots, fixed6000 withdrawal and6455 full entry. Selected proof only, no final R7/CPU acceptance.")
