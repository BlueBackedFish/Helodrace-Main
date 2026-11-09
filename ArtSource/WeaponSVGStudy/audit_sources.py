"""Read existing KRA archives and build evidence for the SVG texture study."""

import argparse
from collections import Counter
from hashlib import sha256
from io import BytesIO
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET
import zipfile

from PIL import Image, ImageDraw, ImageFont


REPO = Path(__file__).resolve().parents[2]
SOURCES = {
    "HD_M16A1": "ColdWar/HD_M16A1.png",
    "HD_M16A4": "ModernWar/HD_M16A4.png",
    "HD_M4A1": "ModernWar/HD_M4A1.png",
    "HD_MK18MOD1": "ModernWar/HD_MK18MOD1.png",
    "HD_Glock19": "ModernWar/HD_Glock19.png",
    "HD_m1911": "GreatWar/HD_m1911.png",
    "HD_Thompsonm1a1": "GreatWar/HD_Thompsonm1a1.png",
    "m1903": "GreatWar/SprM1903.png",
    "K2": None,
}


def tag(element):
    return element.tag.rsplit("}", 1)[-1]


def rgba(data):
    return Image.open(BytesIO(data)).convert("RGBA")


def inspect(path, runtime):
    before = sha256(path.read_bytes()).hexdigest()
    with zipfile.ZipFile(path) as archive:
        document = ET.fromstring(archive.read("maindoc.xml"))
        image = next(e for e in document.iter() if tag(e) == "IMAGE")
        layers = [e for e in document.iter() if tag(e) == "layer"]
        references = [e.attrib for e in document.iter() if tag(e) == "referenceimage"]
        refs = {r["src"]: rgba(archive.read(r["src"])) for r in references}
        merged = rgba(archive.read("mergedimage.png"))
        fills, tags, widths = Counter(), Counter(), Counter()
        svgs = [n for n in archive.namelist() if n.endswith(".shapelayer/content.svg")]
        for name in svgs:
            for element in ET.fromstring(archive.read(name)).iter():
                tags[tag(element)] += 1
                if "fill" in element.attrib:
                    fills[element.get("fill")] += 1
                if "stroke-width" in element.attrib:
                    widths[element.get("stroke-width")] += 1
        # Read only the explicitly typed Photoshop layer-style pixel size field.
        # This is not a general ASL parser; retain the raw value as evidence.
        sizes = []
        marker = b"Sz  UntF#Pxl"
        for name in archive.namelist():
            if name.endswith(".asl"):
                data = archive.read(name)
                offset = data.find(marker)
                if offset >= 0:
                    sizes.append(struct.unpack_from(">d", data, offset + len(marker))[0])
    runtime_image = Image.open(runtime).convert("RGBA") if runtime else None
    result = {
        "source": path.name,
        "source_sha256": before,
        "source_unchanged": before == sha256(path.read_bytes()).hexdigest(),
        "canvas": [int(image.get("width")), int(image.get("height"))],
        "dpi": float(image.get("x-res")),
        "merged_alpha_bbox": merged.getbbox(),
        "references": [dict(r, pixel_size=refs[r["src"]].size) for r in references],
        "layer_types": dict(Counter(e.get("nodetype") for e in layers)),
        "vector_layer_count": len(svgs),
        "svg_element_counts": dict(tags),
        "svg_fill_counts": dict(fills.most_common()),
        "svg_stroke_width_counts": dict(widths.most_common()),
        "layer_style_size_px": sizes,
        "runtime": str(runtime.relative_to(REPO)) if runtime else None,
        "runtime_pixels_equal_merged": (
            runtime_image.size == merged.size and runtime_image.tobytes() == merged.tobytes()
        ) if runtime_image else None,
    }
    return result, merged, refs


def paste_fitted(board, image, box, crop=False):
    image = image.copy()
    if crop:
        image = image.crop(image.getbbox())
    image.thumbnail((box[2] - box[0], box[3] - box[1]), Image.LANCZOS)
    background = Image.new("RGBA", image.size, (190, 194, 200, 255))
    background.alpha_composite(image)
    board.paste(background.convert("RGB"), (
        box[0] + (box[2] - box[0] - image.width) // 2,
        box[1] + (box[3] - box[1] - image.height) // 2,
    ))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=REPO.parent / ".texture")
    parser.add_argument("--output", type=Path, default=Path(__file__).parent / "Review")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    records, images = [], {}
    for name, runtime in SOURCES.items():
        record, merged, refs = inspect(
            args.source_root / (name + ".kra"),
            REPO / "Textures/Weapons" / runtime if runtime else None,
        )
        records.append(record)
        images[name] = (merged, refs)
    assert all(r["source_unchanged"] for r in records)
    assert all(r["runtime_pixels_equal_merged"] is not False for r in records)
    (args.output / "audit.json").write_text(
        json.dumps(records, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 20)
    board = Image.new("RGB", (1500, 980), (231, 233, 236))
    draw = ImageDraw.Draw(board)
    for x, title in [(15, "Existing texture (cropped)"), (515, "Embedded photo: shape reference"),
                     (1015, "Embedded sprite: style reference")]:
        draw.text((x, 15), title, fill="black", font=font)
    # These classifications follow visual inspection, not archive filenames.
    rows = [("HD_M16A1", 2, 1), ("HD_Thompsonm1a1", 0, 1), ("HD_Glock19", None, 0)]
    for row, (name, photo, style) in enumerate(rows):
        y = 60 + row * 305
        draw.text((15, y), name, fill="black", font=font)
        merged, refs = images[name]
        paste_fitted(board, merged, (15, y + 35, 485, y + 275), crop=True)
        for column, index in [(1, photo), (2, style)]:
            if index is None:
                draw.text((515, y + 100), "No embedded photo in this KRA", fill="black", font=font)
            else:
                paste_fitted(board, refs["reference_images/" + str(index) + ".png"],
                             (column * 500 + 15, y + 35, column * 500 + 485, y + 275))
    board.save(args.output / "reference_comparison.png")
    board = Image.new("RGB", (1000, 60 + 300 * 6), (220, 223, 227))
    draw = ImageDraw.Draw(board)
    draw.text((15, 12), "Full canvas at 256 / 128 px; same canvas size does not mean same gun scale",
              fill="black", font=font)
    for row, name in enumerate(["HD_M16A1", "HD_M4A1", "HD_MK18MOD1", "HD_Glock19",
                                 "HD_Thompsonm1a1", "m1903"]):
        y = 60 + row * 300
        draw.text((15, y + 15), name, fill="black", font=font)
        for x, size in [(280, 256), (620, 128)]:
            im = images[name][0].resize((size, size), Image.LANCZOS)
            paste_fitted(board, im, (x, y, x + size, y + 256))
    board.save(args.output / "reduction_review.png")
    print("Read-only audit: %s sources; %s runtime pixel matches; all source hashes unchanged." % (
        len(records), sum(r["runtime_pixels_equal_merged"] is True for r in records)))


if __name__ == "__main__":
    main()
