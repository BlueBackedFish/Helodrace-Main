"""Find and show source parts when assigning per-part colour exceptions (HEIGHT_PAINT_GUIDE.md).

Run from the repository root. Every command reads the vehicle profile (vehicle.json) and its
pinned source OBJ; face IDs printed here are the global IDs `configure_vehicle.py` uses as seeds.

  heights    <profile>                          up-facing paint area by height, per layer
  components <profile> <group>...               connected shells of a group (seed, size, bounds)
  region     <profile> <group> <lo> <hi>        shells of one group inside a box, e.g. "-1,2.9,.3" "1,3.3,2.1"
  zoom       <profile> <view> <out.png> <key>...  crop of a built view, parts outlined in red
  at         <profile> <view> <x0,y0,x1,y1>     which parts cover a 1024px pixel box of a built view
  sheet      <profile> <group> <out.png>        front/top plot of a group's shells, coloured per shell

`<group>` is a source group ID (g0000) as in vehicle.json `source_group`. `<view>` is a built layer
name such as hull_east, hull_south, turret_east, turret_full_east. `<key>` matches a part ID prefix.
"""
from pathlib import Path
from collections import Counter
import json
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import pipeline as P


def load(profile_path):
    profile = P.read_json(profile_path)
    model = P.read_obj(profile["sources"]["obj"]["path"])
    vertices = np.array([P.transform(v, profile["axes"]) for v in model["vertices"]])
    return profile, model, vertices, P.partition(model, profile)


def face_normal(model, vertices, f):
    co = vertices[model["faces"][f]["vertices"]]
    n = np.sum(np.cross(co[1:-1]-co[0], co[2:]-co[0]), axis=0)
    area = np.linalg.norm(n)/2
    return (n/(2*area) if area else n), area, co


def shells(model, vertices, faces, limit=40):
    for c in sorted(P.components(model, faces, 1e-6), key=len, reverse=True)[:limit]:
        co = vertices[[v for f in c for v in model["faces"][f]["vertices"]]]
        n = np.mean([face_normal(model, vertices, f)[0] for f in c], axis=0)
        lo, hi = co.min(0), co.max(0)
        print(f"  seed {min(c):7d} n={len(c):5d} x {lo[0]:+.2f}..{hi[0]:+.2f} y {lo[1]:+.2f}..{hi[1]:+.2f} "
              f"z {lo[2]:.2f}..{hi[2]:.2f} size {np.round(hi-lo, 2)} meanN {np.round(n, 2)}")


def kept_faces(profile, selection, group):
    return [f for p in profile["parts"] if p["source_group"] == group and p.get("action") != "omit"
            for f in selection[p["id"]]]


def heights(profile_path):
    profile, model, vertices, selection = load(profile_path)
    for layer in sorted({p["layer"] for p in profile["parts"]}):
        zs, ws = [], []
        for p in profile["parts"]:
            if p["layer"] != layer or p.get("action") == "omit" or p["role"] not in ("paint", "hatch", "equipment"):
                continue
            for f in selection[p["id"]]:
                n, a, co = face_normal(model, vertices, f)
                if a and n[2] >= .78:
                    zs.append(co[:, 2].mean())
                    ws.append(a)
        if not zs:
            continue
        zs, ws = np.array(zs), np.array(ws)
        print(f"{layer}: up-facing z {zs.min():.2f}..{zs.max():.2f}")
        hist, edges = np.histogram(zs, bins=np.arange(np.floor(zs.min()*20)/20, zs.max()+.05, .05), weights=ws)
        for c, lo in zip(hist, edges):
            if c > .01:
                print(f"  {lo:5.2f} m  {c:7.3f} m2  " + "#"*int(min(60, c*6)))


def components(profile_path, *groups):
    profile, model, vertices, selection = load(profile_path)
    for g in groups:
        print("==", g, [(p["id"], p.get("action", "keep"), len(selection[p["id"]]))
                        for p in profile["parts"] if p["source_group"] == g])
        shells(model, vertices, kept_faces(profile, selection, g))


def region(profile_path, group, lo, hi):
    profile, model, vertices, selection = load(profile_path)
    lo, hi = np.array([float(v) for v in lo.split(",")]), np.array([float(v) for v in hi.split(",")])
    faces = [f for f in kept_faces(profile, selection, group)
             if np.all((vertices[model["faces"][f]["vertices"]].mean(0) >= lo) &
                       (vertices[model["faces"][f]["vertices"]].mean(0) <= hi))]
    print(f"{len(faces)} faces of {group} inside the box")
    shells(model, vertices, faces, 30)


def _view(profile_path, view):
    from PIL import Image
    root = Path(profile_path).parent/"output"
    report = json.loads((root/"report.json").read_text(encoding="utf-8"))
    ids = {int(k): v.split(" ")[0] for k, v in report["render"]["id_pass"]["ids"].items()}
    ident = np.load(root/f"{view}_ids.npz")["ident"]
    ident = ident[::ident.shape[0]//report["render"]["master"], ::ident.shape[0]//report["render"]["master"]]
    flat = root/f"{view}_flat.png"
    image = Image.open(flat if flat.exists() else root/f"{view}.png").convert("RGBA")
    background = Image.new("RGBA", image.size, (255, 255, 255, 255))
    background.alpha_composite(image)
    return ids, ident, np.array(background.convert("RGB"))


def zoom(profile_path, view, out, *keys):
    from PIL import Image
    ids, ident, rgb = _view(profile_path, view)
    mask = np.isin(ident, [i for i, v in ids.items() if v.startswith(keys)])
    if not mask.any():
        raise SystemExit(f"No visible pixels for {keys} in {view}")
    edge = mask & ~(np.roll(mask, 1, 0) & np.roll(mask, -1, 0) & np.roll(mask, 1, 1) & np.roll(mask, -1, 1))
    marked = rgb.copy()
    marked[edge] = (255, 0, 0)
    ys, xs = np.nonzero(mask)
    box = (max(xs.min()-25, 0), max(ys.min()-25, 0), min(xs.max()+25, rgb.shape[1]), min(ys.max()+25, rgb.shape[0]))
    scale = max(1, min(6, 500//max(box[2]-box[0], box[3]-box[1])))
    left, right = Image.fromarray(rgb).crop(box), Image.fromarray(marked).crop(box)
    w, h = left.size
    sheet = Image.new("RGB", (w*2*scale+10, h*scale), (255, 255, 255))
    sheet.paste(left.resize((w*scale, h*scale), Image.NEAREST), (0, 0))
    sheet.paste(right.resize((w*scale, h*scale), Image.NEAREST), (w*scale+10, 0))
    sheet.save(out)
    print(out, sorted({ids[i] for i in np.unique(ident[mask])}))


def at(profile_path, view, box):
    ids, ident, _ = _view(profile_path, view)
    x0, y0, x1, y1 = [int(v) for v in box.split(",")]
    for key, n in Counter(ident[y0:y1, x0:x1].ravel().tolist()).most_common(8):
        print(f"  {ids.get(key, 'background')}: {n} px")


def sheet(profile_path, group, out):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.collections import PolyCollection
    profile, model, vertices, selection = load(profile_path)
    comps = sorted(P.components(model, kept_faces(profile, selection, group), 1e-6), key=len, reverse=True)
    cmap = plt.get_cmap("tab20")
    fig, axes = plt.subplots(1, 2, figsize=(16, 8))
    for ax, (title, a, b, depth, sign) in zip(axes, [("front (-y)", 0, 2, 1, -1), ("top (+z)", 0, 1, 2, 1)]):
        polys, colours, keys = [], [], []
        for i, c in enumerate(comps):
            for f in c:
                co = vertices[model["faces"][f]["vertices"]]
                polys.append(co[:, [a, b]])
                keys.append(sign*co[:, depth].mean())
                colours.append(cmap(i % 20))
        order = np.argsort(keys)
        ax.add_collection(PolyCollection([polys[i] for i in order], facecolors=[colours[i] for i in order],
                                         edgecolors="k", linewidths=.1))
        ax.autoscale()
        ax.set_aspect("equal")
        ax.set_title(title)
    handles = [plt.Rectangle((0, 0), 1, 1, color=cmap(i % 20)) for i in range(min(20, len(comps)))]
    axes[1].legend(handles, [f"seed {min(c)} n={len(c)}" for c in comps[:20]], loc="center left",
                   bbox_to_anchor=(1, .5), fontsize=8)
    plt.tight_layout()
    plt.savefig(out, dpi=80)
    print(out)


COMMANDS = {"heights": heights, "components": components, "region": region, "zoom": zoom, "at": at, "sheet": sheet}

if __name__ == "__main__":
    if len(sys.argv) < 3 or sys.argv[1] not in COMMANDS:
        raise SystemExit(__doc__)
    COMMANDS[sys.argv[1]](*sys.argv[2:])
