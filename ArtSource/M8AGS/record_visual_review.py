"""Record explicit visual inspection after opening the review images.

This only updates review metadata; it never grants blueprint/game/mechanism approval.
Rebuilding with configure_vehicle.py deliberately resets these manual review flags.
"""
from copy import deepcopy
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent / "VehiclePipeline"))
from pipeline import read_json, write_json, sha256


def main():
    output = ROOT / "output"
    profile_path = ROOT / "vehicle.json"
    report_path = output / "report.json"
    profile = read_json(profile_path)
    report = read_json(report_path)
    previous_profile = sha256(profile_path)
    assert report["profile_sha256"] == previous_profile
    assert report["model_sha256"] == sha256(output / "vehicle.blend")
    assert report["source_coordinates_preserved"] and report["postprocess_passed"]
    assert report["lighting_max_difference_255"] == 0 and report["unlit_glb"]
    manifest = read_json(output / "Textures/manifest.json")
    assert len(manifest["textures"]) == 80
    for path, digest in manifest["textures"].items():
        assert sha256(output / "Textures" / path) == digest
    artifacts = ["cardinal_review.png", "style_comparison.png", "alpha_backgrounds.png",
                 "OutlinedReview/native_sizes.png", "iso_front.png", "iso_rear.png"]
    evidence = {name: sha256(output / name) for name in artifacts}
    old = deepcopy(profile)
    for key in ("style_reference", "native_size", "geometry"):
        profile["review"][key] = True
    old.pop("review")
    unchanged = deepcopy(profile)
    unchanged.pop("review")
    assert old == unchanged, "Review recording may not alter geometry, paint or mapping"
    assert not profile["review"]["blueprint"] and not profile["review"]["game"]
    write_json(profile_path, profile)
    report["profile_sha256"] = sha256(profile_path)
    report["blockers"] = [b for b in report["blockers"]
                          if b not in {"review: style_reference", "review: native_size", "review: geometry"}]
    report["visual_review"] = dict(date="2026-10-25", artifacts=evidence,
        previous_build_profile_sha256=previous_profile,
        metadata_only_review_update=True,
        scope="Four cardinal previews, 256/512 native size, same-body-width RimThunder style comparison, three backgrounds, source part atlas and front/rear isometric geometry inspection",
        limits="No blueprint certification, exhaustive self-intersection audit, exact hatch/elevation mechanism or in-game validation; five source zero-area faces retained")
    # Remaining draft blockers are intentional limits, not failed output validation.
    report["status"] = "draft" if report["blockers"] else "reviewed"
    write_json(report_path, report)
    outline_path = output / "OutlinedReview/report.json"
    outline = read_json(outline_path)
    outline["source_report_sha256"] = sha256(report_path)
    write_json(outline_path, outline)
    print("Recorded visual review only; source model/textures unchanged, blueprint/game and precise mechanism checks remain unverified")


if __name__ == "__main__":
    main()
