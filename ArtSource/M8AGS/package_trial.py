"""Package four cardinals, per-layer black outlines and actual-size style review."""
from pathlib import Path
import sys
import json
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent / "VehiclePipeline"))
from pipeline import read_json, write_json, sha256
from images import font, resize_linear, composite_linear, check_margin
from outline_review import black_outline, build_review


def body_width(image, width):
    image = image.convert("RGBA")
    image = image.crop(image.getchannel("A").getbbox())
    return image.resize((width, round(width * image.height / image.width)), Image.Resampling.LANCZOS)


def main():
    output = ROOT / "output"
    profile = read_json(ROOT / "vehicle.json")
    report = read_json(output / "report.json")
    if report["profile_sha256"] != sha256(ROOT / "vehicle.json"):
        raise ValueError("Build the current mapping before publishing")
    metadata = read_json(output / "CardinalMasters/pivots.json")
    if metadata["source_model_sha256"] != sha256(output / "vehicle.blend"):
        raise ValueError("Cardinal masters belong to a different BLEND")
    # Required common outline workflow, with the original EAST render left intact.
    outline = build_review(ROOT / "vehicle.json")
    out = output / "Textures"
    evidence = dict(source_model_sha256=sha256(output / "vehicle.blend"),
                    outline_width_at_256=1, source_layers_unchanged=True,
                    filter="linear-light premultiplied-alpha Lanczos", sizes={})
    source_hashes = {}
    for direction, values in metadata["directions"].items():
        source = output if direction == "east" else output / "CardinalMasters"
        layers = {key: Image.open(source / f"{key}_{direction}.png").convert("RGBA")
                  for key in ("hull", "turret", "barrel", "turret_full")}
        source_hashes.update({str((source / f"{key}_{direction}.png").relative_to(output)):
            sha256(source / f"{key}_{direction}.png") for key in layers})
        master = metadata["master"]
        anchor = [round(v) for v in values["hull_turret_anchor"]]
        offset = [v - master // 2 for v in anchor]
        for outlined in (False, True):
            current = {key: black_outline(image, master / 256) if outlined else image
                       for key, image in layers.items()}
            current["preview"] = composite_linear(current["hull"], current["turret_full"], offset)
            for size in (256, 512):
                dest = out / ("Outlined" if outlined else "Plain") / str(size)
                dest.mkdir(parents=True, exist_ok=True)
                for key, image in current.items():
                    result = resize_linear(image, size)
                    check_margin(result, f"{key}_{direction}")
                    result.save(dest / f"{key}_{direction}.png")
                record = dict(size=[size, size],
                    hull_turret_anchor=[v * size / master for v in anchor],
                    raw_hull_turret_anchor=[v * size / master for v in values["hull_turret_anchor"]],
                    turret_pivot=[size / 2] * 2, turret_full_includes_barrel=True,
                    pixel_scale_per_source_unit=size / metadata["ortho_scale"])
                evidence["sizes"].setdefault(str(size), {})[direction] = record
    for size, data in evidence["sizes"].items():
        for group in ("Plain", "Outlined"):
            write_json(out / group / size / "pivots.json", data)
    for path, digest in source_hashes.items():
        assert sha256(output / path) == digest, "Publication modified a source layer"
    evidence["source_layer_hashes"] = source_hashes
    evidence["textures"] = {str(p.relative_to(out)): sha256(p) for p in out.rglob("*.png")}
    write_json(out / "manifest.json", evidence)

    board = Image.new("RGB", (1120, 900), (65, 68, 71))
    draw = ImageDraw.Draw(board)
    draw.text((20, 14), "M8 AGS LEVEL 3 / 고정 TAN · 원본 형상 · 레이어별 검은 윤곽선", font=font(21), fill="white")
    reference = Path(profile["style_references"]["rimthunder"])
    for x, title, hull, turret in [
        (20, "RimThunder M1A2 / 스타일 참고, 다른 차종", Image.open(reference / "vehicle_east.png"),
         Image.open(reference / "vehicle_turret.png").rotate(-90, expand=True)),
        (580, "제공 XM8 모델 / LEVEL 3", Image.open(out / "Outlined/512/hull_east.png"),
         Image.open(out / "Outlined/512/turret_east.png"))]:
        draw.text((x, 57), title, font=font(), fill="white")
        for image, width, y in [(hull, 480, 100), (turret, 330, 420)]:
            image = body_width(image, width)
            board.paste(image, (x + (500 - width) // 2, y), image)
    draw.text((20, 816), "차체 외피 폭 480px / 포탑 외피 길이 330px로 맞춘 표현 비교. M8 실루엣 기준은 제공 OBJ.", font=font(), fill="white")
    draw.text((20, 850), "레퍼런스 픽셀 복사 없음 · 청사진 미제공 · 게임 내 표시 미검증", font=font(), fill=(220, 220, 220))
    board.save(output / "style_comparison.png")
    sprite = Image.open(out / "Outlined/512/preview_east.png").convert("RGBA")
    backgrounds = Image.new("RGB", (1536, 550))
    draw = ImageDraw.Draw(backgrounds)
    for i, (color, label) in enumerate([((245, 245, 242), "밝은 배경"), ((125, 128, 130), "중간 배경"), ((30, 32, 35), "어두운 배경")]):
        backgrounds.paste(color, (i * 512, 0, (i + 1) * 512, 550))
        backgrounds.paste(sprite, (i * 512, 38), sprite)
        draw.text((i * 512 + 12, 10), label, font=font(), fill=(10, 10, 10) if i < 2 else "white")
    backgrounds.save(output / "alpha_backgrounds.png")
    directions = Image.new("RGB", (1024, 1080), (65, 68, 71))
    draw = ImageDraw.Draw(directions)
    for i, direction in enumerate(("north", "east", "south", "west")):
        x, y = i % 2 * 512, i // 2 * 540
        sprite = Image.open(out / f"Outlined/512/preview_{direction}.png").convert("RGBA")
        directions.paste(sprite, (x, y + 28), sprite)
        draw.text((x + 15, y + 5), direction.upper() + " / 512px", font=font(), fill="white")
    directions.save(output / "cardinal_review.png")
    for path in out.rglob("*.png"):
        image = np.asarray(Image.open(path).convert("RGBA"))
        assert not image[image[:, :, 3] == 0, :3].any(), f"Hidden RGB in {path}"
    assert sha256(output / "vehicle.blend") == report["model_sha256"]
    print(f"PASS: {len(evidence['textures'])} transparent textures, four cardinal pivots, per-layer outlines and unchanged source BLEND/layers")


if __name__ == "__main__":
    main()
