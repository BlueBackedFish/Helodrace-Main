"""Measure RimThunder vehicle textures against our renders (read-only audit).

Reports per layer: painted-pixel value percentiles, flat value ladder (large
single-value regions), exact-flatness of neighbouring pixels and unpainted
(mask-black) material values. Reference pixels are only measured, never copied.

python ArtSource/RimThunderAudit/measure_reference.py [--ours <png> ...]
"""
from pathlib import Path
import argparse
import json
import numpy as np
from PIL import Image
from scipy import ndimage

WORKSHOP = Path("C:/Program Files (x86)/Steam/steamapps/workshop/content/294100")
SETS = {
    "M2A3": ("3724793908/Textures/Vehicles/M2A3", "vehicle_turret_full", "vehicle_turret_m"),
    "M2A3_BUSK": ("3724793908/Textures/Vehicles/M2A3/BUSK", "vehicle_turret_full", "vehicle_turret_m"),
    "M1A2": ("3724793908/Textures/Vehicles/M1A2", "vehicle_turret_full", "vehicle_turret_m"),
    "M10": ("3724793908/Textures/Vehicles/M10", "vehicle_turret_full", "vehicle_turret_m"),
    "M18": ("3038760170/Textures/M18", "MainTurret_full", "MainTurret_full_m"),
}


def luminance(rgb):
    return np.round(0.2126*rgb[..., 0]+0.7152*rgb[..., 1]+0.0722*rgb[..., 2]).astype(int)


def measure(image, mask=None, outline_below=20, region_min=400):
    a = np.asarray(Image.open(image).convert("RGBA")).astype(int)
    opaque = a[..., 3] >= 250
    value = luminance(a[..., :3])
    if mask:
        m = np.asarray(Image.open(mask).convert("RGB")).astype(int)
        paint, unpainted = opaque & (m[..., 0] > 128), opaque & (m[..., 0] <= 128)
    else:
        paint, unpainted = opaque, opaque & False
    outline = opaque & (value < outline_below)
    paint &= ~outline
    pair = opaque[:, 1:] & opaque[:, :-1]
    step = np.abs(np.diff(a[..., :3], axis=1)).max(-1)[pair]
    ladder = {}
    for v in np.unique(value[paint]):
        lab, k = ndimage.label(paint & (value == v))
        if k:
            big = int((ndimage.sum(np.ones_like(lab), lab, range(1, k+1)) > region_min).sum())
            if big:
                ladder[int(v)] = big
    result = dict(
        paint_percentiles_10_50_90=[int(x) for x in np.percentile(value[paint], [10, 50, 90])],
        flat_region_values=ladder,
        neighbour_identical_pct=round(100*float(np.mean(step == 0)), 1),
        neighbour_off_by_one_pct=round(100*float(np.mean(step == 1)), 1),
        outline_fraction=round(float(outline.sum()/opaque.sum()), 3))
    if unpainted.any():
        result["unpainted_median"] = int(np.median(value[unpainted]))
        result["unpainted_fraction"] = round(float(unpainted.sum()/opaque.sum()), 3)
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ours", nargs="*", default=[])
    args = parser.parse_args()
    report = {}
    for name, (folder, turret, turret_mask) in SETS.items():
        root = WORKSHOP/folder
        report[name+"_hull"] = measure(root/"vehicle_east.png", root/"vehicle_eastm.png")
        report[name+"_turret"] = measure(root/(turret+".png"), root/(turret_mask+".png"))
    for path in args.ours:
        report["ours:"+Path(path).name] = measure(path)
    print(json.dumps(report, indent=1))


if __name__ == "__main__":
    main()
