"""Verify failed permanent contact and five actual structural resumption cases."""
from pathlib import Path
import gzip
import hashlib
import json
import re

base = Path(__file__).resolve().parent


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def files(root):
    for relative, expected in read(root / "provenance.json")["files"].items():
        raw = (root / relative).read_bytes()
        data = gzip.decompress(raw) if relative.endswith(".gz") else raw
        assert hashlib.sha256(raw).hexdigest() == expected["sha256"], relative
        assert hashlib.sha256(data).hexdigest() == expected["uncompressedSha256"]
        assert len(data) == expected["uncompressedBytes"]


old = base / "functional-b766-attempt-02-stop"
files(old)
oj = read(old / "checks.json")
assert not oj["finalR7Complete"] and not oj["cpuGateEvaluated"] and not oj["allSpecifiedPassed"]
assert len(oj["records"]) == 12 and all(r["status"] == "passed" for r in oj["records"][:11])
assert oj["records"][-1]["name"] == "unexpected-hole" and oj["records"][-1]["status"] == "failed"
before = read(old / "unexpected-hole/audit.json")
assert before["complete"] and before["error"] is None and before["caseTriggered"] and not before["newFunctionalComplete"]
assert before["newContactResponses"] == 1 and before["newContactResumes"] == 0 and before["newRoomsSecured"] == 1
assert "contactLastSeen=9918" in before["r7CommandLayers"][0] and before["newUnsafeEntries"] == 0
wide = read(old / "wide-opening/audit.json")
assert wide["newFunctionalComplete"] and wide["newWideOpeningReused"]
root = base / "structural-withdrawal-01"
files(root)
j = read(root / "checks.json")
names = ["narrow", "unexpected-hole", "inside-goal", "room-recovery", "door-contact"]
assert j["requestedCases"] == names and [r["name"] for r in j["records"]] == names
assert j["allSpecifiedPassed"] and not j["fullFunctionalQueue"] and not j["finalR7Complete"] and not j["cpuGateEvaluated"]
sha = "eeb5b979a8ccc9143c0400a22e1e0a04f55ceed7dd2c95ba1f2329064e919378"
assert j["pinned"]["Assemblies\\Helodrace.dll"] == sha
complete_ticks = dict(zip(names, [6426, 7477, 8676, 7773, 7025]))
for name in names:
    a, l = read(root / name / "audit.json"), read(root / name / "launcher.json")
    assert a["complete"] and a["error"] is None and a["environmentControlled"] and a["isolationVerified"]
    assert a["newFunctionalComplete"] and a["newConnectedStacks"] and a["newPhysicalPlansValid"]
    assert a["newUnsafeEntries"] == 0 and a["r7RetiredTypesAbsent"] and a["r7RetiredDefinitionsAbsent"]
    assert a["population"] == 12 and a["units"] == a["newCompletedUnits"] == 1
    assert a["newEnteredByOrder"] == a["newEntryAssignmentsComplete"] == a["alive"] == (11 if name == "room-recovery" else 12)
    assert a["r7TickRateMinimum"] == a["r7TickRateMaximum"] == 3 and a["measuredTicks"] == 10000
    assert a["r7FixtureActorWithdrawn"] and a["r7FixtureActorWithdrawTick"] == 6000 and a["r7FixtureContactBeforeWithdrawal"]
    assert a["newContactResponses"] == a["newContactResumes"] == 1 and a["newAllCompleteTick"] == complete_ticks[name]
    assert l["assemblySha256"] == sha and l["defaultEngine"] and not l["methodProfile"] and not l["headless"]
    for field, file in (("newJobDefinitionsSha256", "Defs\\Organization\\NewTacticalJobs.xml"),
                        ("breachDefinitionsSha256", "Defs\\ColdWar\\BreachExplosive_ColdWar.xml"),
                        ("hammerJobDefinitionsSha256", "Defs\\GreatWar\\Sledgehammer_Breach.xml")):
        assert l[field] == j["pinned"][file]
    log = gzip.decompress((root / name / "Player.log.gz").read_bytes()).decode("utf-8-sig")
    assert not re.search(r"Exception:|Exception while|Error in ", log)
    assert f"R7 {name} defender withdrawn at 6000 observed=True" in log
    if name in ("unexpected-hole", "inside-goal", "room-recovery"):
        assert a["newRoomProgressComplete"] and a["newRoomsSecured"] == 3
    if name == "unexpected-hole":
        assert a["caseTriggered"] and a["newUnexpectedOpeningReused"]
        assert "(120, 0, 119)" in a["newSecuredPortals"][0]
        for key in ("mapFingerprint", "pawnFingerprint", "population", "seed", "sampleTicks", "warmupTicks"):
            assert a[key] == before[key], key
    elif name == "inside-goal":
        assert a["newDirectObjectiveCleared"]
    elif name == "room-recovery":
        assert a["caseTriggered"] and a["caseLossPhase"] == "Clear" and a["newRoomRecoveryContinued"]
        assert a["newToolRecoveriesCompleted"] == 1 and a["newRoomToolRecoveryWaits"] == 4
    elif name == "door-contact":
        assert a["caseTriggered"] and a["newDoorContactObserved"] and a["caseContactTile"]
        assert a["newSupportThrows"] == 1 and a["newSupportWaits"] == 28 and a["newSupportReturns"] == 1
print("PASS: failed permanent-contact/hole input preserved; same initial geometry/pawns, actual fixed6000 withdrawal/contact/resumption and five full structural cases. Not full44 or final R7/CPU.")
