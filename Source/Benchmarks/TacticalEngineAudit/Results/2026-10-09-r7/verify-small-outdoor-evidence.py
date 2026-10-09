"""Verify real small-room conservation and outdoor smoke/resumption evidence."""
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


def no_exceptions(root, case):
    log = gzip.decompress((root / case / "Player.log.gz").read_bytes()).decode("utf-8-sig")
    assert not re.search(r"Exception:|Exception while|Error in ", log)
    return log


old = base / "functional-eeb5-attempt-03-stop"
files(old)
j = read(old / "checks.json")
assert j["fullFunctionalQueue"] and len(j["requestedCases"]) == 44 and len(j["records"]) == 17
# These statuses are journal evidence only: this archive retains the failed case.
assert all(r["status"] == "passed" for r in j["records"][:16])
assert j["records"][-1]["name"] == "small-unseen" and j["records"][-1]["status"] == "failed"
assert not j["allSpecifiedPassed"] and not j["finalR7Complete"] and not j["cpuGateEvaluated"]
before_small = read(old / "small-unseen/audit.json")
assert before_small["complete"] and before_small["error"] is None
assert not before_small["newFunctionalComplete"] and not before_small["newSmallRoomSupportSaved"]
assert before_small["newContactsSeen"] > 0 and before_small["newFieldShots"] > 0
assert before_small["entered"] == 0 and "field=Defending" in before_small["r7CommandLayers"][0]
no_exceptions(old, "small-unseen")

attempt = base / "small-outdoor-attempt-01"
files(attempt)
j = read(attempt / "checks.json")
assert not j["fullFunctionalQueue"] and not j["allSpecifiedPassed"]
assert [r["status"] for r in j["records"]] == ["passed", "failed"]
first_small = read(attempt / "small-unseen/audit.json")
assert first_small["newFunctionalComplete"] and first_small["newSmallRoomSupportSaved"]
assert first_small["newContactsSeen"] == first_small["newSupportThrows"] == 0
before_outdoor = read(attempt / "outdoor-opening/audit.json")
assert before_outdoor["complete"] and before_outdoor["error"] is None and not before_outdoor["newFunctionalComplete"]
assert before_outdoor["newOutdoorSmokeSeen"] and before_outdoor["newOutdoorSmokeUsed"]
assert before_outdoor["newSupportThrows"] > 0 and before_outdoor["newSupportReturns"] > 0
assert before_outdoor["newContactsSeen"] > 0 and before_outdoor["newFieldShots"] > 0
assert "field=Defending" in before_outdoor["r7CommandLayers"][0]
# This older collector left non-drill field counters at default zero. That is
# not proof that the physically observed Defending response never happened.
for case in ("small-unseen", "outdoor-opening"):
    no_exceptions(attempt, case)

reset = base / "small-outdoor-attempt-02"
files(reset)
rj = read(reset / "checks.json")
assert [r["status"] for r in rj["records"]] == ["passed", "failed"] and not rj["allSpecifiedPassed"]
ra = read(reset / "outdoor-opening/audit.json")
assert ra["newOutdoorSmokeSeen"] and not ra["newOutdoorSmokeUsed"] and not ra["newFunctionalComplete"]
assert ra["newFieldResponses"] == ra["newFieldResumes"] == 1
assert ra["newCompletedUnits"] == 1 and ra["newEnteredByOrder"] == ra["newEntryAssignmentsComplete"] == 12
assert ra["newAllCompleteTick"] == 6514 and ra["newClassifiedRoomCells"] == [-1]
assert ra["r7FixtureActorWithdrawTick"] == 6000 and ra["r7FixtureContactBeforeWithdrawal"]
assert ra["newUnsafeEntries"] == 0 and ra["error"] is None
for case in ("small-unseen", "outdoor-opening"):
    no_exceptions(reset, case)

root = base / "small-outdoor-03"
files(root)
j = read(root / "checks.json")
names = ["small-unseen", "outdoor-opening"]
assert j["requestedCases"] == names and [r["name"] for r in j["records"]] == names
assert j["allSpecifiedPassed"] and not j["fullFunctionalQueue"] and not j["finalR7Complete"] and not j["cpuGateEvaluated"]
sha = "bab4bfeed41b636435ecdf4af6790ece01e0e1feb01e073fdbc9e4a2c7a60593"
assert j["pinned"]["Assemblies\\Helodrace.dll"] == sha
for case in names:
    a, l = read(root / case / "audit.json"), read(root / case / "launcher.json")
    assert a["complete"] and a["error"] is None and a["environmentControlled"] and a["isolationVerified"]
    assert a["newFunctionalComplete"] and a["newConnectedStacks"] and a["newPhysicalPlansValid"]
    assert a["newUnsafeEntries"] == 0 and a["r7RetiredTypesAbsent"] and a["r7RetiredDefinitionsAbsent"]
    assert a["population"] == a["alive"] == a["newEnteredByOrder"] == a["newEntryAssignmentsComplete"] == 12
    assert a["units"] == a["newCompletedUnits"] == 1 and a["newAllCompleteTick"] > 0
    assert a["r7TickRateMinimum"] == a["r7TickRateMaximum"] == 3 and a["measuredTicks"] == 10000
    assert l["assemblySha256"] == sha and l["defaultEngine"] and not l["methodProfile"] and not l["headless"]
    for field, file in (("newJobDefinitionsSha256", "Defs\\Organization\\NewTacticalJobs.xml"),
                        ("breachDefinitionsSha256", "Defs\\ColdWar\\BreachExplosive_ColdWar.xml"),
                        ("hammerJobDefinitionsSha256", "Defs\\GreatWar\\Sledgehammer_Breach.xml")):
        assert l[field] == j["pinned"][file]
    no_exceptions(root, case)
    before = before_small if case == "small-unseen" else before_outdoor
    for key in ("mapFingerprint", "pawnFingerprint", "population", "seed", "sampleTicks", "warmupTicks"):
        assert a[key] == before[key], key
    if case == "small-unseen":
        assert a["newSmallRoomSupportSaved"] and a["newClassifiedRoomCells"] == [16]
        assert a["newContactsSeen"] == a["newSupportThrows"] == a["newFieldResponses"] == a["newFieldResumes"] == 0
    else:
        assert a["newOutdoorSmokeSeen"] and a["newOutdoorSmokeUsed"]
        assert a["r7FixtureActorWithdrawn"] and a["r7FixtureActorWithdrawTick"] == 6000 and a["r7FixtureContactBeforeWithdrawal"]
        assert a["newSupportThrows"] >= 1 and a["newSupportWaits"] >= 1 and a["newSupportReturns"] >= 1
        assert a["newFieldResponses"] >= 1 and a["newFieldResumes"] >= 1
        assert a["newContactsSeen"] > 0
        assert len(a["newOutdoorSmokeActions"]) == 1
        for proof in (":projectile=HD_Projectile_M8_Round", ":launched=True", ":outdoors=True", ":returned=True", ":effectsCleared=True"):
            assert proof in a["newOutdoorSmokeActions"][0], proof
print("PASS: preserved failed fixtures, actual 16-cell/no-contact/no-throw conservation and outdoor smoke/contact/fixed6000 withdrawal/field resumption. Two selected cases, not all44 or final CPU/R7.")
