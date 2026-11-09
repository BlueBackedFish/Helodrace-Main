"""Extend the read-only SVG reference study to M167 and towed howitzers."""

import argparse
from hashlib import sha256
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from audit_sources import REPO, inspect, paste_fitted


SOURCES = ["HD_M167VADS_UI.kra", "HD_M114Howitzer_.kra", "HD_M2A2LightHowitzer.kra"]
PREFIXES = ["HD_M167VADS_", "HD_M114Howitzer_", "HD_M2A2LightHowitzer_"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=REPO.parent / ".texture")
    parser.add_argument("--output", type=Path, default=Path(__file__).parent / "Review")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    records, images = [], {}
    for name in SOURCES:
        record, merged, refs = inspect(args.source_root / name, None)
        records.append(record)
        images[name] = (merged, refs)
    runtime_checks = []
    for runtime in sorted((REPO / "Textures/Buildings/Security").glob("*.png")):
        if not any(runtime.name.startswith(prefix) for prefix in PREFIXES):
            continue
        source = args.source_root / runtime.name
        before = sha256(source.read_bytes()).hexdigest()
        left = Image.open(source).convert("RGBA")
        right = Image.open(runtime).convert("RGBA")
        runtime_checks.append({
            "runtime": runtime.relative_to(REPO).as_posix(),
            "source_sha256": before,
            "source_unchanged": before == sha256(source.read_bytes()).hexdigest(),
            "canvas": left.size,
            "alpha_bbox": left.getbbox(),
            "pixels_equal": left.size == right.size and left.tobytes() == right.tobytes(),
        })
    assert runtime_checks, "No runtime artillery textures were found"
    assert all(r["source_unchanged"] for r in records + runtime_checks)
    assert all(r["pixels_equal"] for r in runtime_checks)
    pairs = []
    for plain in sorted((REPO / "Textures/Buildings/Security").glob("*.png")):
        if not any(plain.name.startswith(prefix) for prefix in PREFIXES):
            continue
        for suffix in ["Outline", "OutLine"]:
            outlined = plain.with_name(plain.stem + suffix + ".png")
            if not outlined.exists():
                continue
            a = Image.open(plain).convert("RGBA").getbbox()
            b = Image.open(outlined).convert("RGBA").getbbox()
            pairs.append({
                "base": plain.name, "outline": outlined.name,
                "bbox_extension_left_top_right_bottom": [a[0]-b[0], a[1]-b[1], b[2]-a[2], b[3]-a[3]],
            })
            break
    data = {"sources": records, "runtime_checks": runtime_checks, "outline_pairs": pairs}
    (args.output / "artillery_audit.json").write_text(
        json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 18)
    board = Image.new("RGB", (1500, 1160), (227, 230, 233))
    draw = ImageDraw.Draw(board)
    for x, title in [(15, "Exported artwork"), (515, "Embedded shape reference"),
                     (1015, "Embedded style reference")]:
        draw.text((x, 15), title, fill="black", font=font)
    rows = [
        (SOURCES[0], "M167 VADS", "HD_M167VADS_Test.png", 1, 0),
        (SOURCES[1], "M114 howitzer", "HD_M114Howitzer_UI.png", None, 0),
        (SOURCES[2], "M2A2 light howitzer", "HD_M2A2LightHowitzer_UI.png", 0, 1),
    ]
    for row, (name, title, exported, shape_index, style_index) in enumerate(rows):
        y = 55 + row * 365
        draw.text((15, y), title, fill="black", font=font)
        artwork = Image.open(args.source_root / exported).convert("RGBA")
        paste_fitted(board, artwork, (15, y + 30, 485, y + 345), crop=True)
        refs = images[name][1]
        for column, index in [(1, shape_index), (2, style_index)]:
            if index is None:
                draw.text((515, y + 130), "No embedded shape image in this KRA", fill="black", font=font)
                continue
            paste_fitted(board, refs["reference_images/%s.png" % index],
                         (column * 500 + 15, y + 30, column * 500 + 485, y + 345))
    board.save(args.output / "artillery_reference_comparison.png")

    # Keep the archived M167 visibility state separate from its complete export.
    board = Image.new("RGB", (1200, 740), (225, 228, 231))
    draw = ImageDraw.Draw(board)
    draw.text((15, 10), "M167: archived visibility state and independently exported parts", fill="black", font=font)
    parts = [
        ("KRA merged: partial state", images[SOURCES[0]][0]),
        ("Complete test export", Image.open(args.source_root / "HD_M167VADS_Test.png").convert("RGBA")),
        ("Base", Image.open(args.source_root / "HD_M167VADS_Base.png").convert("RGBA")),
        ("Top", Image.open(args.source_root / "HD_M167VADS_Top.png").convert("RGBA")),
        ("Barrel state 1", Image.open(args.source_root / "HD_M167VADS_1Barrel.png").convert("RGBA")),
        ("Barrel state 4", Image.open(args.source_root / "HD_M167VADS_4Barrel.png").convert("RGBA")),
    ]
    for i, (label, image) in enumerate(parts):
        x = i % 3 * 400
        y = 55 + i // 3 * 340
        draw.text((x + 10, y), label, fill="black", font=font)
        paste_fitted(board, image, (x + 10, y + 30, x + 390, y + 315), crop=True)
    board.save(args.output / "m167_export_parts.png")
    print("Read-only artillery audit: %s KRA sources; %s runtime pixel matches; %s outline pairs." % (
        len(records), len(runtime_checks), len(pairs)))


if __name__ == "__main__":
    main()
