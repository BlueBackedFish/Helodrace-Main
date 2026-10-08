"""Verify raw hashes and the exact native retirement milestones, not a CPU gate."""
from pathlib import Path
import gzip
import hashlib
import json
import subprocess

base = Path(__file__).resolve().parent
repo = base.parents[4]
sha = lambda blob: hashlib.sha256(blob).hexdigest()
read = lambda path: json.loads(path.read_text(encoding="utf-8-sig"))
artifact_path = repo / "Source/Benchmarks/TacticalEngineAudit/retired-artifacts.json"
inventory = read(artifact_path)
assert len(inventory["files"]) == 109
for item in inventory["files"]:
    blob = subprocess.check_output(["git", "-c", "safe.directory=" + repo.as_posix(), "show", inventory["baseCommit"] + ":" + item["path"]])
    assert sha(blob) == item["sha256"] and len(blob) == item["bytes"], item["path"]
    assert not (repo / item["path"]).exists(), item["path"]
for directory in sorted(base.glob("removal-*")):
    provenance = read(directory / "provenance.json")
    for relative, expected in provenance["files"].items():
        raw = (directory / relative).read_bytes()
        assert sha(raw) == expected["sha256"], relative
        data = gzip.decompress(raw) if relative.endswith(".gz") else raw
        assert sha(data) == expected["uncompressedSha256"] and len(data) == expected["uncompressedBytes"], relative
    print("HASH OK:", directory.name)

folders = ["removal-final-default", "removal-final-cleanup-profile", "removal-final-vanilla"]
hashes = {read(base / folder / "launcher.json")["assemblySha256"] for folder in folders}
assert len(hashes) == 1 and sha((repo / "Assemblies/Helodrace.dll").read_bytes()) in hashes
for folder in folders:
    audit = read(base / folder / "audit.json")
    assert audit["complete"] and audit["error"] is None and audit["isolationVerified"]
    assert audit["r7RetiredTypesAbsent"] and audit["r7RetiredDefinitionsAbsent"]
    assert audit["legacyComponents"] == [] and audit["installedLegacyHooks"] == []

audit = read(base / "removal-final-default/audit.json")
assert read(base / "removal-final-default/launcher.json")["defaultEngine"]
assert audit["engine"] == audit["effectiveEngine"] == "new" and audit["population"] == 12
assert audit["r7Reloads"] == 4 and audit["newFunctionalComplete"] and audit["newRoomsSecured"] == 3
for field in ["r7ReloadJobsBound", "r7ReloadToilStatePreserved", "r7ReloadHistoryPreserved", "r7ReloadOpeningPreserved",
              "r7ReloadLiveGrenadePreserved", "r7ReloadContactsPreserved", "r7ReloadResponsePreserved", "newPhysicalPlansValid", "newRoomProgressComplete"]:
    assert audit[field], field
assert audit["newUnsafeEntries"] == 0
audit = read(base / "removal-final-cleanup-profile/audit.json")
assert audit["r7CleanupComplete"] and audit["r7IdleStable"] and audit["r7WorldOrganizationsCleared"]
for field in ["r7RemainingCommands", "r7RemainingOwners", "r7RemainingClaims", "r7RemainingLeases", "r7RemainingOpenings", "r7RemainingMessages"]:
    assert audit[field] == 0, field
captures = list((base / "removal-final-cleanup-profile/profiles").glob("capture-*.json.gz"))
assert len(captures) == 1
capture = json.loads(gzip.decompress(captures[0].read_bytes()))
assert capture["complete"] and capture["dropped"] == 0 and capture["reference"]["iterations"] == 10000
assert len(capture["methods"]) == 42
for method in capture["methods"]:
    assert method["exceptions"] == 0 and method["depthLimitCalls"] == 0
    if method["method"].startswith("Helodrace.Tactics."):
        assert method["calls"] > 0, method["method"]
audit = read(base / "removal-final-vanilla/audit.json")
assert audit["engine"] == "vanilla" and audit["newComponents"] == [] and audit["installedNewHooks"] == []
failure = read(base / "removal-profile-reload-failure/audit.json")
assert not failure["complete"] and "A method capture is already active" in failure["error"]
print("PASS: native default/reload, exit cleanup, real method capture, vanilla isolation and preserved failure; final CPU gate remains unproven.")
