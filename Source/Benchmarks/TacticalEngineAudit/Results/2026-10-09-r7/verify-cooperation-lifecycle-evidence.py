"""Read-only native evidence: terminal LOW failure and strict completion history."""
from pathlib import Path
import gzip
import hashlib
import json
import re

BASE = Path(__file__).resolve().parent


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def archive(name, invalid=()):
    root = BASE / name
    manifest = read(root / "provenance.json")
    for relative, expected in manifest["files"].items():
        raw = (root / relative).read_bytes()
        data = gzip.decompress(raw) if relative.endswith(".gz") else raw
        assert hashlib.sha256(raw).hexdigest() == expected["sha256"], relative
        assert hashlib.sha256(data).hexdigest() == expected["uncompressedSha256"]
        assert len(data) == expected["uncompressedBytes"]
    journal = read(root / "checks.json")
    assert not journal["finalR7Complete"] and not journal["cpuGateEvaluated"]
    for name in manifest["archivedCases"]:
        a, launcher = read(root / name / "audit.json"), read(root / name / "launcher.json")
        if name in invalid:
            assert name == "shared-entry" and not a["complete"] and not a["newFunctionalComplete"]
            assert "Shared fixture needs sapper-door." in a["error"]
            assert a["fixtureCase"] == "r5-shared" and launcher["workload"] == "sapper-wall"
            continue
        assert a["complete"] and a["error"] is None
        assert a["environmentControlled"] and a["isolationVerified"]
        assert a["r7RetiredTypesAbsent"] and a["r7RetiredDefinitionsAbsent"]
        assert a["newUnsafeEntries"] == 0
        assert 1 <= a["r7TickRateMinimum"] <= a["r7TickRateMaximum"] <= 3
        if not launcher["reload"]:
            assert a["r7TickRateMinimum"] == a["r7TickRateMaximum"] == 3
        assert not launcher["headless"] and not launcher["methodProfile"] and launcher["defaultEngine"]
        for field, definition in [("assemblySha256", "Assemblies\\Helodrace.dll"),
                                  ("newJobDefinitionsSha256", "Defs\\Organization\\NewTacticalJobs.xml"),
                                  ("breachDefinitionsSha256", "Defs\\ColdWar\\BreachExplosive_ColdWar.xml"),
                                  ("hammerJobDefinitionsSha256", "Defs\\GreatWar\\Sledgehammer_Breach.xml")]:
            assert launcher[field] == journal["pinned"][definition]
        log = gzip.decompress((root / name / "Player.log.gz").read_bytes()).decode("utf-8-sig")
        assert not re.search(r"Exception:|Exception while|Error in ", log)
    return root, journal


failed, journal = archive("functional-e4e4-attempt-05-stop")
assert journal["fullFunctionalQueue"] and len(journal["requestedCases"]) == 44
assert len(journal["records"]) == 21 and not journal["allSpecifiedPassed"]
assert all(r["status"] == "passed" for r in journal["records"][:20])
assert journal["records"][-1]["name"] == "low-cooperation" and journal["records"][-1]["status"] == "failed"
before = read(failed / "low-cooperation" / "audit.json")
assert before["alive"] == before["entered"] == before["newEnteredByOrder"] == 24
assert before["newRoomProgressComplete"] and not before["newCooperationComplete"] and not before["newFunctionalComplete"]
assert before["newAgreementsConfirmed"] == before["newCooperationStarts"] == 2
assert all(":Aborted " in s for s in before["newCooperationStates"])
for name in read(failed / "provenance.json")["archivedCases"][:-1]:
    assert read(failed / name / "audit.json")["newFunctionalComplete"]
print("PASS: terminal first20 partial success and actual LOW deadline failure retained; no final44/CPU acceptance.")

attempt, aj = archive("cooperation-lifecycle-attempt-01", invalid=("shared-entry",))
assert [r["status"] for r in aj["records"]] == ["passed", "failed"] and not aj["allSpecifiedPassed"]
first = read(attempt / "low-cooperation" / "audit.json")
assert first["newCooperationCompletedTick"] == 3948 and first["newCooperationMilestonePreserved"]
assert first["newFunctionalComplete"] and first["newRoomProgressComplete"]
assert all(":Aborted " in s for s in first["newCooperationStates"])
assert all("peerFinished=False peerGoalReported=False" in s for s in first["newCooperationCompletionStates"])
assert first["newEntryAssignmentsComplete"] == first["newEnteredByOrder"] == first["alive"] == 24
print("PASS: actual LOW joint completion recorded before later independent recovery; invalid shared wall input excluded, error retained.")

second, sj = archive("cooperation-lifecycle-attempt-02")
assert [r["status"] for r in sj["records"]] == ["passed", "failed"] and not sj["allSpecifiedPassed"]
shared = read(second / "shared-entry" / "audit.json")
assert shared["complete"] and shared["alive"] == shared["entered"] == 24
assert shared["newEntryAssignmentsComplete"] == shared["newEnteredByOrder"] == 24
assert shared["newCompletedUnits"] == 2 and shared["newRoomProgressComplete"]
assert not shared["newFunctionalComplete"] and not shared["newSharedEntranceProgress"]
assert shared["newCooperationCompletedTick"] == -1 and not shared["newCooperationMilestonePreserved"]
assert shared["newAgreementsConfirmed"] == 2 and shared["newCooperationStarts"] == 1
assert any(":None " in s for s in shared["newCooperationStates"])
assert any("direct=True" in s and "passed=False" in s for s in shared["newCommands"])
assert read(second / "shared-entry" / "launcher.json")["workload"] == "sapper-door"
print("PASS: actual shared-entry command/agreement/first passage loss retained despite all24 eventually entering; independent direct completion rejected.")

retention, rj = archive("shared-entry-retention-attempt-01")
assert len(rj["records"]) == 1 and rj["records"][0]["status"] == "failed"
assert not rj["allSpecifiedPassed"]
still = read(retention / "shared-entry" / "audit.json")
assert not still["newFunctionalComplete"] and still["newSharedEntranceProgress"]
assert still["newCooperationCompletedTick"] == -1 and not still["newCooperationMilestonePreserved"]
assert any("A=Pending/Agreed B=Complete/Finished" in s for s in still["newCooperationEvents"])
assert still["newCooperationStarts"] == 1 and any(":None " in s for s in still["newCooperationStates"])
print("PASS: retaining agreement alone left A pending through deadline; later physical door passage did not restore original cooperation, native failure excluded.")

fixed = BASE / "shared-entry-retention-02"
root, journal = archive(fixed.name)
assert journal["allSpecifiedPassed"] and not journal["fullFunctionalQueue"]
assert journal["requestedCases"] == ["shared-entry"]
a = read(root / "shared-entry" / "audit.json")
assert read(root / "shared-entry" / "launcher.json")["workload"] == "sapper-door"
assert a["alive"] == a["newEnteredByOrder"] == a["newEntryAssignmentsComplete"] == 24
assert a["newFunctionalComplete"] and a["newSharedEntranceProgress"]
assert a["newCooperationComplete"] and a["newCooperationMilestonePreserved"]
assert a["newCooperationStarts"] == 2 and a["newAgreementsConfirmed"] == 2
assert a["newCompletedUnits"] == 2 and 0 < a["newCooperationCompletedTick"] < a["sampleTicks"]
assert len(a["newCooperationCompletionStates"]) == len(a["newCooperationCompletionAgendas"]) == 2
assert a["newCooperationCompletionAgendas"][0] == a["newCooperationCompletionAgendas"][1]
assert all(":Finished phase=Complete ownAreaSecured=True " in s and "plans=(100, 0, 108)" in s
           for s in a["newCooperationCompletionStates"])
assert all("(100, 0, 108)" in s for s in a["newSecuredPortals"])
assert not any("direct=True" in s for s in a["newCommands"])
print("PASS: both original squads actually use the same door and preserve passage/allocation history; bounded existing-portal fix, selected1 only.")

for field in ["mapFingerprint", "pawnFingerprint", "population", "units", "seed", "sampleTicks", "warmupTicks", "fixtureVersion"]:
    assert a[field] == still[field], field
print("PASS: actual before/after initial map/gear/population/input window match; differing DLLs are functional comparison only, never paired CPU proof.")
