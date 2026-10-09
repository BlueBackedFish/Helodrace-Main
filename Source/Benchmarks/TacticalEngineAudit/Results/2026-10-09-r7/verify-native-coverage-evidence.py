"""Verify retained-return save XML and multi-squad actual-map evidence, not final R7/CPU."""
from pathlib import Path
import gzip
import hashlib
import json
import xml.etree.ElementTree as ET

base = Path(__file__).resolve().parent


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def verify_files(root):
    for relative, expected in read(root / "provenance.json")["files"].items():
        raw = (root / relative).read_bytes()
        assert hashlib.sha256(raw).hexdigest() == expected["sha256"], relative
        data = gzip.decompress(raw) if relative.endswith(".gz") else raw
        assert hashlib.sha256(data).hexdigest() == expected["uncompressedSha256"]
        assert len(data) == expected["uncompressedBytes"]


def verify_native(root, expected_hash):
    a, launcher = read(root / "audit.json"), read(root / "launcher.json")
    assert a["complete"] and a["error"] is None and a["isolationVerified"] and a["environmentControlled"]
    assert a["r7RetiredTypesAbsent"] and a["r7RetiredDefinitionsAbsent"] and a["newUnsafeEntries"] == 0
    assert a["newFunctionalComplete"] and a["newPhysicalPlansValid"] and a["newConnectedStacks"]
    assert launcher["assemblySha256"] == expected_hash and not launcher["methodProfile"] and not launcher["headless"]
    assert launcher["defaultEngine"] and a["engine"] == a["effectiveEngine"] == "new"
    log = gzip.decompress((root / "Player.log.gz").read_bytes()).decode("utf-8-sig")
    assert "Exception:" not in log
    return a, launcher, log


def verify_retained(root, expected_hash):
    a, launcher, log = verify_native(root, expected_hash)
    assert launcher["retainedReload"] and "-hdTacticalAuditRetainedReload=true" in launcher["arguments"]
    assert a["fixtureVersion"] == 26 and a["population"] == a["newEnteredByOrder"] == a["newEntryAssignmentsComplete"] == 12
    assert a["r7Reloads"] == 4 and a["newRoomsSecured"] == 3 and a["newRoomProgressComplete"]
    for field in ("r7ReloadJobsBound", "r7ReloadHistoryPreserved", "r7ReloadOpeningPreserved", "r7ReloadResponsePreserved",
                  "r7ReloadLiveGrenadePreserved", "r7ReloadRetainedReplanPreserved", "r7RetainedReplanResumed"):
        assert a[field], field
    assert "saving checkpoint 2 phase=Returning secured=455" in log
    assert "restored retained mission resumed with unchanged secured history HD_Raid_1::HD_Raid_1_Group_1" in log
    saved = ET.fromstring(gzip.decompress((root / "Saves/R7TacticalCheckpoint2.rws.gz").read_bytes()))
    commands = [node for node in saved.iter() if node.find("replanAfterReturn") is not None]
    assert len(commands) == 1
    command = commands[0]
    assert command.findtext("id") == "HD_Raid_1::HD_Raid_1_Group_1"
    assert command.findtext("phase") == "Returning" and command.findtext("replanAfterReturn") == "True"
    assert command.findtext("releaseAfterReturn", "False") == "False"
    assert len(command.find("securedCells")) == 455 and len(command.find("securedPlans")) == 1


previous = base / "retained-return-load-01"
verify_files(previous)
verify_retained(previous, "310ef50c086a3f2bbf48dbcb1373f304f7c4b4b4159cc8d99fe8869fe125eb6e")
root = base / "native-coverage-extensions-01"
verify_files(root)
j = read(root / "checks.json")
assert j["allSpecifiedPassed"] and not j["fullFunctionalQueue"] and not j["finalR7Complete"] and not j["cpuGateEvaluated"]
assert [r["name"] for r in j["records"]] == ["multi-map-scale", "reload-retained"]
assert all(r["status"] == "passed" and r["nativeExceptions"] == 0 for r in j["records"])
sha = j["pinned"]["Assemblies\\Helodrace.dll"]
assert sha == "4c9a76329188e8ccfc698c07441a5bc8112719fd023ab3efa9b5ed98ebd55fb9"
verify_retained(root / "reload-retained", sha)
a, launcher, log = verify_native(root / "multi-map-scale", sha)
assert a["fixtureVersion"] == 28 and a["population"] == a["r7SecondaryPopulation"] == a["newEnteredByOrder"] == 48
assert a["units"] == a["r7SecondaryUnits"] == a["newCompletedUnits"] == 4 and a["r7TwoMapTicks"] == 120
for field in ("r7TwoActualMaps", "r7GlobalBudgetShared", "r7CrossMapCommunicationBlocked", "r7RemovedMapClean",
              "r7RemainingMapPreserved", "r7RemainingMapCompleted"):
    assert a[field], field
assert a["r7TickRateMinimum"] == a["r7TickRateMaximum"] == 3
for suffix, limit in (("Jobs", 2), ("Returns", 1), ("Paths", 4), ("Plans", 1), ("Observations", 1), ("Communications", 2)):
    assert 0 < a["r7MaxGlobal" + suffix] <= limit
assert "squads=4/4 population=48/48" in log and "removed second map; clean=True survivor=True" in log
cleanup = base / "cleanup-diary-01"
verify_files(cleanup)
cj = read(cleanup / "checks.json")
assert cj["allSpecifiedPassed"] and not cj["finalR7Complete"] and not cj["cpuGateEvaluated"] and not cj["fullFunctionalQueue"]
assert cj["pinned"]["Assemblies\\Helodrace.dll"] == sha and cj["records"][0]["nativeExceptions"] == 0
ca, cl = read(cleanup / "cleanup/audit.json"), read(cleanup / "cleanup/launcher.json")
assert cl["assemblySha256"] == sha and not cl["methodProfile"] and not cl["headless"]
assert ca["complete"] and ca["error"] is None and ca["isolationVerified"] and ca["environmentControlled"]
assert ca["r7CleanupComplete"] and ca["r7IdleStable"] and ca["r7WorldOrganizationsCleared"]
assert ca["measuredTicks"] - ca["r7CleanupAt"] >= 240 and ca["r7TickRateMinimum"] == ca["r7TickRateMaximum"] == 3
for field in ("r7RemainingCommands", "r7RemainingOwners", "r7RemainingClaims", "r7RemainingLeases", "r7RemainingOpenings", "r7RemainingMessages"):
    assert ca[field] == 0
assert "Exception:" not in gzip.decompress((cleanup / "cleanup/Player.log.gz").read_bytes()).decode("utf-8-sig")
print("PASS: actual native Returning save intent/history and mission resumption; two48-pawn/four-squad maps share global budgets and survive real removal. Selected functional proof only, no final R7/CPU claim.")
print("PASS: same-DLL native exit/idle cleanup retry has no exception lines; earlier exception not reproduced or claimed fixed.")
