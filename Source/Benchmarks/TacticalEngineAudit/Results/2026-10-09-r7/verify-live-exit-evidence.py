"""Audit live physical effects after real raid departure; no CPU gate claim."""
from pathlib import Path
import gzip
import hashlib
import json

base = Path(__file__).resolve().parent
hashes = set()
for kind in ("grenade", "charge"):
    directory = base / ("live-" + kind + "-exit")
    read = lambda path: json.loads(path.read_text(encoding="utf-8-sig"))
    provenance = read(directory / "provenance.json")
    for relative, expected in provenance["files"].items():
        raw = (directory / relative).read_bytes()
        assert hashlib.sha256(raw).hexdigest() == expected["sha256"], relative
        data = gzip.decompress(raw) if relative.endswith(".gz") else raw
        assert hashlib.sha256(data).hexdigest() == expected["uncompressedSha256"] and len(data) == expected["uncompressedBytes"]
    launcher = read(directory / "launcher.json")
    hashes.add(launcher["assemblySha256"])
    assert launcher["headless"] and not launcher["methodProfile"] and launcher["defaultEngine"]
    audit = read(directory / "audit.json")
    assert audit["complete"] and audit["error"] is None and audit["population"] == 12
    assert audit["fixtureVersion"] == 32 and audit["r7ExitEffectKind"] == kind and audit["r7ExitEffectId"] > 0
    for field in ["isolationVerified", "r7RetiredTypesAbsent", "r7RetiredDefinitionsAbsent", "caseTriggered",
                  "r7ExitDuringLiveEffect", "r7EffectSurvivedExit", "r7EffectDetonated", "r7EffectDrained",
                  "r7CleanupComplete", "r7WorldOrganizationsCleared", "r7IdleStable"]:
        assert audit[field], field
    assert 0 < audit["r7ExitEffectAt"] < audit["r7ExitEffectDrainedAt"] <= audit["r7CleanupAt"]
    # lastProgressTick is the last physical movement, not the collection end.
    # These non-reload fixtures start sampling at warmup=0; use the actual tick window.
    assert audit["warmupTicks"] == 0 and audit["measuredTicks"] - audit["r7CleanupAt"] >= 240
    for field in ["r7RemainingCommands", "r7RemainingOwners", "r7RemainingClaims", "r7RemainingLeases", "r7RemainingOpenings", "r7RemainingMessages", "newUnsafeEntries"]:
        assert audit[field] == 0, field
    if kind == "charge":
        assert audit["r7EffectWallDestroyed"] and audit["r7ExitEffectFragments"] == 18
    print("PASS:", kind, "live at", audit["r7ExitEffectAt"], "drained", audit["r7ExitEffectDrainedAt"], "retired", audit["r7CleanupAt"])
assert hashes == {"93b18dd3aea098cd28680438175cdbfd330b111c6e3e536fc3ca372f5ab536a7"}
print("PASS: exact preserved raw evidence and same DLL; scaling, speed and final CPU comparisons remain outstanding.")
