"""Verify real contact exposure and High operator loss, retaining failed inputs."""
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


def native(root, case):
    a = read(root / case / "audit.json")
    l = read(root / case / "launcher.json")
    j = read(root / "checks.json")
    assert a["complete"] and a["error"] is None
    assert a["environmentControlled"] and a["isolationVerified"]
    assert a["r7RetiredTypesAbsent"] and a["r7RetiredDefinitionsAbsent"]
    assert a["r7TickRateMinimum"] == a["r7TickRateMaximum"] == 3
    assert not l["methodProfile"] and not l["headless"] and l["defaultEngine"]
    for field, source in (("assemblySha256", "Assemblies\\Helodrace.dll"),
                          ("newJobDefinitionsSha256", "Defs\\Organization\\NewTacticalJobs.xml"),
                          ("breachDefinitionsSha256", "Defs\\ColdWar\\BreachExplosive_ColdWar.xml"),
                          ("hammerJobDefinitionsSha256", "Defs\\GreatWar\\Sledgehammer_Breach.xml")):
        assert l[field] == j["pinned"][source]
    log = gzip.decompress((root / case / "Player.log.gz").read_bytes()).decode("utf-8-sig")
    assert not re.search(r"Exception:|Exception while|Error in ", log)
    assert a["newUnsafeEntries"] == 0
    return a, log


failed = base / "functional-bab4-attempt-04-stop"
files(failed)
j = read(failed / "checks.json")
assert j["fullFunctionalQueue"] and len(j["requestedCases"]) == 44 and len(j["records"]) == 19
# Only the failing case is archived; earlier pass statuses remain a journal.
assert all(r["status"] == "passed" for r in j["records"][:18])
assert j["records"][-1]["status"] == "failed"
old, _ = native(failed, "r4-contact-drill")
assert not old["newContactDrillComplete"] and not old["newFunctionalComplete"]
assert old["newRearResponses"] > 0 and old["newDoorResponses"] > 0
assert old["newOpposedResponses"] == 0 and old["newContactResumes"] == 2

attempt = base / "contact-casualty-attempt-01"
files(attempt)
j = read(attempt / "checks.json")
assert [r["status"] for r in j["records"]] == ["passed", "failed"]
assert not j["allSpecifiedPassed"] and not j["fullFunctionalQueue"]
contact, _ = native(attempt, "r4-contact-drill")
assert contact["newContactDrillComplete"] and contact["newFunctionalComplete"]
before, _ = native(attempt, "casualty")
assert before["newFunctionalComplete"] and not before["caseTriggered"]
assert before["alive"] == before["population"] == 13
assert before["newChargesInstalled"] == before["newChargeDetonations"] == 1

repeat = base / "contact-casualty-attempt-02"
files(repeat)
rj = read(repeat / "checks.json")
assert len(rj["records"]) == 1 and rj["records"][0]["status"] == "failed"
assert not rj["allSpecifiedPassed"]
ra, _ = native(repeat, "r4-contact-drill")
assert not ra["newFunctionalComplete"] and ra["newOpposedResponses"] == 0 and ra["newContactResumes"] == 2
assert "open=True" in ra["newCommands"][0]

root = base / "contact-casualty-03"
files(root)
j = read(root / "checks.json")
assert j["requestedCases"] == ["r4-contact-drill", "casualty"]
assert j["allSpecifiedPassed"] and not j["fullFunctionalQueue"]
assert not j["finalR7Complete"] and not j["cpuGateEvaluated"]
for case in j["requestedCases"]:
    a, log = native(root, case)
    assert a["newFunctionalComplete"] and a["newConnectedStacks"] and a["newPhysicalPlansValid"]
    assert a["units"] == a["newCompletedUnits"] == 1
    assert a["newEnteredByOrder"] == a["newEntryAssignmentsComplete"] == a["alive"]
    assert a["newAllCompleteTick"] > 0
    if case == "r4-contact-drill":
        assert a["population"] == a["alive"] == 12 and a["newRoomsSecured"] == 3
        for flag in ("newContactDrillComplete", "newContactMemoryFrozen", "newContactPlanPreserved", "newUnseenDoorIgnored"):
            assert a[flag]
        for counter in ("newRearResponses", "newDoorResponses", "newOpposedResponses"):
            assert a[counter] >= 1
        assert a["newContactResumes"] == 3 and a["newContactGuardJobs"] >= 6
        assert sum("opposed doorway held " in event for event in a["newContactEvents"]) == 1
        assert sum("opposed doorway hold restored=False" in event for event in a["newContactEvents"]) == 1
        assert sum("opposed LOS witness " in event for event in a["newContactEvents"]) == 1
    else:
        assert a["population"] == 13 and a["alive"] == 12
        assert a["caseTriggered"] and a["newCasualtyContinued"]
        assert a["caseLossPhase"] == "Breach" and a["caseLossJob"] == "HD_NewTacticalInstallCharge"
        assert a["caseLossPawnId"] > 0 and a["caseLossTick"] > 0
        assert a["newChargesInstalled"] >= 1 and a["newChargeDetonations"] >= 1
        assert "job=HD_NewTacticalInstallCharge dead=True" in log
        # The fixture selected its actual gear; it did not force a hammer loadout.
        for field in ("mapFingerprint", "pawnFingerprint", "population", "seed", "sampleTicks", "warmupTicks"):
            assert a[field] == before[field], field
print("PASS: actual rear/door/opposed observations and three resumptions, restored real doorway, actual High C4 installer death and all12 survivor completion. Failed inputs retained; selected two cases, not all44 or final CPU/R7.")
