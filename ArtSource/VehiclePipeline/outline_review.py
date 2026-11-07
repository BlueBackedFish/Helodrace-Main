"""Black per-layer silhouette outlines, applied before hull/turret composition.

This is deterministic review postprocessing. Original model and render layers stay
unchanged; outline width is defined at 256px and scaled with output resolution.
"""
from pathlib import Path
import argparse
import math
import numpy as np
from PIL import Image, ImageDraw

from pipeline import read_json, write_json, sha256
from images import composite_linear, resize_linear, check_margin, font


def black_outline(source, radius):
    image = source.convert("RGBA")
    rgba = np.asarray(image)
    alpha = rgba[:, :, 3]
    padding = math.ceil(radius+.5)
    padded = np.pad(alpha, padding)
    grown = np.zeros_like(alpha)
    # Soft-edged disc: offsets on the rim contribute by their fractional distance, so the stroke
    # keeps an anti-aliased edge at the master size instead of a stepped integer disc.
    grown = grown.astype(np.float32)
    for dy in range(-padding-1, padding+2):
        for dx in range(-padding-1, padding+2):
            weight = min(1.0, max(0.0, radius+.5-math.hypot(dx, dy)))
            if weight <= 0 or abs(dy) > padding or abs(dx) > padding:
                continue
            np.maximum(grown, weight*padded[padding+dy:padding+dy+image.height,
                                            padding+dx:padding+dx+image.width], out=grown)
    grown = np.round(grown).astype(alpha.dtype)
    backdrop = np.zeros_like(rgba)
    backdrop[:, :, 3] = grown
    result = composite_linear(Image.fromarray(backdrop), image, (0, 0))
    pixels = np.asarray(result)
    # The stroke cannot repaint opaque source pixels or introduce colored halos.
    assert np.array_equal(pixels[alpha == 255], rgba[alpha == 255])
    assert not pixels[(alpha == 0) & (grown > 0), :3].any()
    assert np.all(pixels[:, :, 3] >= alpha)
    return result


def build_review(profile_path, width_256=2):
    if not math.isfinite(width_256) or width_256 <= 0:
        raise ValueError("Outline width must be finite and positive")
    profile_path = Path(profile_path).resolve()
    profile = read_json(profile_path)
    root = profile_path.parent/"output"
    report = read_json(root/"report.json")
    if report["profile_sha256"] != sha256(profile_path) or not report.get("postprocess_passed"):
        raise ValueError("Build the current vehicle profile successfully before outlining its review layers")
    render = report["render"]
    master = render["master"]
    target = root/"OutlinedReview"
    target.mkdir(exist_ok=True)
    names = ["hull_east"]
    if any(p["layer"] in {"turret", "barrel"} and p["id"] in report["paint"] for p in profile["parts"]):
        names += ["turret_east", "barrel_east", "turret_full_east"]
    weapon_names = [w["id"]+"_east" for w in profile.get("weapon_layers", [])]
    names += weapon_names
    hashes = {name: sha256(root/(name+".png")) for name in names}
    anchor = [round(v) for v in render["turret_anchor_pixels_top_origin"]]
    offset = [v-master//2 for v in anchor]
    # Outline at the master resolution, then linear-light downscale: smooth anti-aliased strokes.
    outlined = {name: black_outline(Image.open(root/(name+".png")), width_256*master/256) for name in names}
    hull = outlined["hull_east"]
    if "turret_full_east" in outlined:
        turret = outlined["turret_full_east"]
        box = turret.getchannel("A").getbbox()
        assert box and box[0]+offset[0] >= 0 and box[1]+offset[1] >= 0
        assert box[2]+offset[0] <= master and box[3]+offset[1] <= master
        preview = composite_linear(hull, turret, tuple(offset))
        for name in weapon_names:
            spot = render["placements"][name[:-5]]["pixels_in_turret_top_origin"]
            preview = composite_linear(preview, outlined[name], (offset[0]+round(spot[0])-master//2,
                                                                 offset[1]+round(spot[1])-master//2))
        outlined["preview_east"] = preview
    else:
        outlined["preview_east"] = hull.copy()
    bounds = {}
    for size in [master, *render["sizes"]]:
        directory = target/str(size)
        directory.mkdir(exist_ok=True)
        for name, source in outlined.items():
            image = source if size == master else resize_linear(source, size)
            bounds[f"{size}/{name}"] = check_margin(image, name)
            image.save(directory/(name+".png"))
        write_json(directory/"pivots.json", {"size": [size, size], "turret_top_pivot": [size/2]*2,
                   "hull_turret_anchor": [v*size/master for v in anchor],
                   "outline_pixels": width_256*size/256, "stroke": "black, each layer before compositing",
                   "turret_full_includes_barrel": True})
    native = Image.new("RGB", (sum(render["sizes"])+16*(len(render["sizes"])+1), max(render["sizes"])+78), (68, 71, 74))
    draw = ImageDraw.Draw(native)
    x = 16
    for size in render["sizes"]:
        image = Image.open(target/str(size)/"preview_east.png")
        native.paste(image, (x, 42), image)
        draw.text((x, 12), f"{size}px / 검은 윤곽선 {width_256*size/256:g}px", font=font(), fill="white")
        x += size+16
    native.save(target/"native_sizes.png")
    assert hashes == {name: sha256(root/(name+".png")) for name in names}
    write_json(target/"report.json", {"source_report_sha256": sha256(root/"report.json"),
               "source_layer_sha256": hashes, "outline_width_at_256": width_256,
               "color": [0, 0, 0], "method": "circular alpha dilation; black beneath each original layer",
               "composition": "outline hull, turret and weapon layers separately at the master size, composite at the original pivot, then linear-light downscale",
               "source_pixels_opaque_unchanged": True, "source_files_unchanged": True,
               "alpha_bounds": bounds, "outline_is_review_postprocess": True})
    print(f"Outlined review saved: {target}")
    return target


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile")
    parser.add_argument("--width-256", type=float, default=2)
    args = parser.parse_args()
    build_review(args.profile, args.width_256)
