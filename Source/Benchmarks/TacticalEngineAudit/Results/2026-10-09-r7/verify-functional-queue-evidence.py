"""Verify actual queue smoke evidence; not the final functional or CPU gate."""
from pathlib import Path
import gzip
import hashlib
import json

base = Path(__file__).resolve().parent


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


for name in ("functional-queue-smoke-01", "functional-queue-smoke-02"):
    root = base / name
    for relative, expected in read(root / "provenance.json")["files"].items():
        raw = (root / relative).read_bytes()
        assert hashlib.sha256(raw).hexdigest() == expected["sha256"]
        data = gzip.decompress(raw) if relative.endswith(".gz") else raw
        assert hashlib.sha256(data).hexdigest() == expected["uncompressedSha256"]
        assert len(data) == expected["uncompressedBytes"]
    journal = read(root / "checks.json")
    assert not journal["finalR7Complete"] and not journal["cpuGateEvaluated"] and not journal["fullFunctionalQueue"]
    assert journal["pinned"]["Assemblies\\Helodrace.dll"] == "ab1487ea04c48d258becca0f9b934388276f0bd0a49d3eace6e86a21e69a06b0"
    for record in journal["records"]:
        audit = read(root / record["name"] / "audit.json")
        launcher = read(root / record["name"] / "launcher.json")
        assert audit["complete"] and audit["error"] is None and audit["isolationVerified"] and audit["environmentControlled"]
        assert audit["r7RetiredTypesAbsent"] and audit["r7RetiredDefinitionsAbsent"] and audit["newUnsafeEntries"] == 0
        assert launcher["assemblySha256"] == journal["pinned"]["Assemblies\\Helodrace.dll"]
        assert not launcher["methodProfile"] and not launcher["headless"] and launcher["defaultEngine"]
        if record["name"] == "reload-cqb":
            assert audit["r7Reloads"] == 4 and audit["newFunctionalComplete"] and audit["newEnteredByOrder"] == 12
            assert audit["newRoomProgressComplete"] and audit["r7TickRateMinimum"] == 1 and audit["r7TickRateMaximum"] == 3
            for field in ("r7ReloadJobsBound", "r7ReloadHistoryPreserved", "r7ReloadOpeningPreserved",
                          "r7ReloadResponsePreserved", "r7ReloadLiveGrenadePreserved"):
                assert audit[field], field
        else:
            assert record["name"] == "cleanup" and audit["r7CleanupComplete"] and audit["r7IdleStable"]
            assert audit["r7WorldOrganizationsCleared"]
            # The original queue accepted the state flags but missed a runtime
            # exception. Preserve that fact; the corrected queue rejects it.
            assert "NullReferenceException" in gzip.decompress((root / "cleanup/Player.log.gz").read_bytes()).decode("utf-8")
    if name.endswith("01"):
        assert not journal["allSpecifiedPassed"] and journal["records"][-1]["status"] == "failed"
    else:
        assert journal["allSpecifiedPassed"] and journal["records"][0]["status"] == "passed"
        log = gzip.decompress((root / "reload-cqb/Player.log.gz").read_bytes()).decode("utf-8")
        assert "Exception:" not in log
        assert journal["records"][0]["actualMinimumSpeed"] == 1 and journal["records"][0]["actualMaximumSpeed"] == 3
print("PASS: native four-load CQB smoke retains actual1..3 load speed; earlier policy rejection and cleanup runtime exception remain explicit, no full R7/CPU pass.")
