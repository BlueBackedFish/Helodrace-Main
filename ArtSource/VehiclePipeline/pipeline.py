"""OBJ + blueprint intake and deterministic vehicle-art orchestration (no bpy)."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
AXES = {"x": (1, 0, 0), "y": (0, 1, 0), "z": (0, 0, 1)}


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def write_json(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def sha256(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def axis(name):
    sign = -1 if name.startswith("-") else 1
    return tuple(sign * n for n in AXES[name.lstrip("+-")])


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


def transform(co, axes):
    """Canonical vehicle space: forward -Y, up +Z; orthonormal, no reflection."""
    forward, up = axis(axes["forward"]), axis(axes["up"])
    if dot(forward, up):
        raise ValueError("Forward and up must use different axes")
    right = cross(up, forward)
    scale = axes["scale"]
    if not math.isfinite(scale) or scale <= 0:
        raise ValueError("Scale must be finite and positive")
    return (dot(co, right)*scale, -dot(co, forward)*scale, dot(co, up)*scale)


def bounds(points):
    return [[min(p[i] for p in points) for i in range(3)],
            [max(p[i] for p in points) for i in range(3)]]


def read_obj(path):
    """Retain every face once, global face IDs, group/object/material labels and UVs.

    OBJ normals are not used for paint. No welding, decimation or material-based
    geometry filtering occurs. Repeated group declarations accumulate safely.
    A face belonging to multiple named groups is stored once under that group set.
    """
    vertices, uv, faces, groups = [], [], [], {}
    obj, group, material, normal_count = "", "default", "", 0

    def index(value, count, line):
        number = int(value)
        result = number-1 if number > 0 else count+number
        if number == 0 or not 0 <= result < count:
            raise ValueError(f"Invalid OBJ index at line {line}: {value}")
        return result

    with Path(path).open(encoding="utf-8-sig", errors="strict") as stream:
        for line_number, raw in enumerate(stream, 1):
            fields = raw.partition("#")[0].split()
            if not fields:
                continue
            command, values = fields[0], fields[1:]
            if command == "v":
                co = tuple(float(v) for v in values[:3])
                if len(co) != 3 or not all(math.isfinite(v) for v in co):
                    raise ValueError(f"Invalid vertex at line {line_number}")
                vertices.append(co)
            elif command == "vt":
                uv.append(tuple(float(v) for v in (values + ["0"])[:2]))
            elif command == "vn":
                normal_count += 1
            elif command == "o":
                obj = " ".join(values)
            elif command == "g":
                group = " ".join(values) or "default"
            elif command == "usemtl":
                material = " ".join(values)
            elif command == "f":
                if len(values) < 3:
                    raise ValueError(f"Face with fewer than 3 corners at line {line_number}")
                vi, ti = [], []
                for value in values:
                    corner = value.split("/")
                    vi.append(index(corner[0], len(vertices), line_number))
                    ti.append(index(corner[1], len(uv), line_number) if len(corner) > 1 and corner[1] else None)
                    if len(corner) > 2 and corner[2]:
                        index(corner[2], normal_count, line_number)
                key = (obj, group)
                if key not in groups:
                    groups[key] = {"id": f"g{len(groups):04d}", "object": obj, "group": group, "faces": []}
                face_id = len(faces)
                faces.append({"vertices": vi, "uv": ti, "material": material})
                groups[key]["faces"].append(face_id)
            elif command in {"l", "p", "curv", "curv2", "surf"}:
                raise ValueError(f"Non-polygon geometry ({command}) at line {line_number}; convert it to mesh first")
    if not faces:
        raise ValueError("OBJ has no polygon faces")
    return {"vertices": vertices, "uv": uv, "faces": faces, "groups": list(groups.values())}


def guess_role(name):
    """Suggestions only. Unknown names preserve geometry and require review."""
    words = name.lower().replace("-", "_").replace(" ", "_").split("_")
    for tokens, role, mode in [
        ({"glass", "optic", "optics", "lens"}, "optics", "flat"),
        ({"rubber", "tire", "tyre"}, "rubber", "flat"),
        ({"track", "tracks"}, "track", "flat"),
        ({"barrel", "gun"}, "metal", "flat"),
        ({"hatch", "door", "ramp"}, "hatch", "armor"),
        ({"scope", "periscope", "sensor", "equipment"}, "equipment", "armor")]:
        if set(words) & tokens:
            return role, mode
    return "paint", "armor"


def inventory(model, axes):
    verts = [transform(v, axes) for v in model["vertices"]]
    entries = []
    for group in model["groups"]:
        indices = {v for i in group["faces"] for v in model["faces"][i]["vertices"]}
        materials = defaultdict(int)
        for i in group["faces"]:
            materials[model["faces"][i]["material"]] += 1
        entries.append({**group, "bounds": bounds([verts[i] for i in indices]),
                        "materials": dict(materials), "face_count": len(group["faces"])})
    return {"vertex_count": len(verts), "face_count": len(model["faces"]),
            "bounds": bounds(verts), "groups": entries}


def components(model, face_ids, tolerance):
    """Position-connected source face sets, including seams with duplicated vertices.

    The spatial index only selects faces. It never welds or edits source geometry.
    Search neighboring buckets so an exact bucket boundary does not split a seam.
    """
    from itertools import product
    if not math.isfinite(tolerance) or tolerance <= 0:
        raise ValueError("Component tolerance must be finite and positive")
    parent = {}
    buckets = defaultdict(list)
    face_sets = defaultdict(list)

    def find(v):
        parent.setdefault(v, v)
        while parent[v] != v:
            parent[v] = parent[parent[v]]
            v = parent[v]
        return v

    def join(a, b):
        a, b = find(a), find(b)
        if a != b:
            parent[b] = a

    vertices = sorted({v for i in face_ids for v in model["faces"][i]["vertices"]})
    for v in vertices:
        co = model["vertices"][v]
        cell = tuple(math.floor(c/tolerance) for c in co)
        find(v)
        for offset in product((-1, 0, 1), repeat=3):
            for other in buckets[tuple(a+b for a, b in zip(cell, offset))]:
                delta = [a-b for a, b in zip(co, model["vertices"][other])]
                if dot(delta, delta) <= tolerance*tolerance:
                    join(v, other)
        buckets[cell].append(v)
    for face_id in face_ids:
        verts = model["faces"][face_id]["vertices"]
        for v in verts[1:]:
            join(verts[0], v)
    for face_id in face_ids:
        face_sets[find(model["faces"][face_id]["vertices"][0])].append(face_id)
    return sorted(face_sets.values(), key=lambda item: (-len(item), min(item)))


def inspect_components(args):
    path = Path(args.profile).resolve()
    profile = read_json(path)
    source = profile["sources"]["obj"]
    if sha256(source["path"]) != source["sha256"]:
        raise ValueError("Source hash mismatch")
    model = read_obj(source["path"])
    group = next(g for g in model["groups"] if g["id"] == args.group)
    span = max(b-a for a, b in zip(*bounds(model["vertices"])))
    tolerance = args.tolerance if args.tolerance is not None else span*1e-7
    result = components(model, group["faces"], tolerance)
    directory = path.parent/"analysis"
    directory.mkdir(exist_ok=True)
    report = [{"seed": min(fs), "faces": fs, "face_count": len(fs),
               "bounds": bounds([transform(model["vertices"][v], profile["axes"])
                                 for i in fs for v in model["faces"][i]["vertices"]])} for fs in result]
    write_json(directory/(args.group+"_components.json"),
               {"source_sha256": source["sha256"], "selection_tolerance_source_units": tolerance,
                "components": report})
    if args.seed is not None:
        if not args.part_id:
            raise ValueError("--part-id is required with --seed")
        matches = [fs for fs in result if args.seed in fs]
        if len(matches) != 1:
            raise ValueError("Seed does not belong to this source group")
        profile["parts"].append({"id": args.part_id, "source_group": args.group,
                                 "label": args.part_id, "faces": matches[0], "role": "paint",
                                 "mode": "armor", "layer": "hull", "confirmed": False})
        partition(model, profile)  # Reject double extraction before writing any changed profile.
        write_json(path, profile)
    print(f"{args.group}: {len(result)} components -> {directory}")


def prepare(args):
    from PIL import Image
    from images import parts_atlas
    target = Path(args.output).resolve()
    if target.exists():
        raise ValueError("Output already exists; edit vehicle.json and rebuild instead of resetting it")
    obj = Path(args.obj).resolve()
    blueprint = Path(args.blueprint).resolve() if args.blueprint else None
    # Validate both inputs before creating a project. The original stays untouched.
    if blueprint:
        with Image.open(blueprint) as reference:
            reference.verify()
    model = read_obj(obj)
    blueprint_hash = sha256(blueprint) if blueprint else None
    axes = {"forward": args.forward, "up": args.up, "scale": args.scale, "confirmed": False}
    catalog = inventory(model, axes)
    parts = []
    for group in model["groups"]:
        name = group["group"] if group["group"] != "default" else group["object"]
        role, mode = guess_role(name)
        materials = Counter(model["faces"][i]["material"] for i in group["faces"])
        turret_fraction = sum(count for name, count in materials.items() if "turret" in name.lower())/len(group["faces"])
        words = name.lower().replace("_", " ").split()
        layer = "barrel" if role == "metal" and set(words) & {"gun", "barrel"} else (
            "turret" if "turret" in words or turret_fraction > .5 else "hull")
        parts.append({"id": group["id"], "source_group": group["id"], "label": name,
                      "role": role, "mode": mode, "layer": layer, "confirmed": False})
    target.mkdir(parents=True)
    shutil.copyfile(ROOT/"presets/rimthunder_tan.json", target/"style.json")
    profile = {"schema": 1, "name": args.name or target.name,
               "sources": {"obj": {"path": str(obj), "sha256": sha256(obj)}},
               "axes": axes, "style": "style.json", "variant_notes": "미확인",
               "blueprint_views": {}, "parts": parts, "rigs": [], "contacts": [],
               "review": {"blueprint": False, "style_reference": False,
                          "native_size": False, "geometry": False, "game": False}}
    if blueprint:
        profile["sources"]["blueprint"] = {"path": str(blueprint), "sha256": blueprint_hash}
    write_json(target/"vehicle.json", profile)
    write_json(target/"inventory.json", catalog)
    parts_atlas(model, axes, target/"analysis")
    brief = (ROOT.parent/"VEHICLE_ART_BRIEF_TEMPLATE.md").read_text(encoding="utf-8")
    brief = brief.replace("<차량명/형식>", profile["name"], 1)
    for placeholder, value in {
        "- 원본 3D 경로 및 파일 버전/해시:": f"- 원본 3D 경로 및 파일 버전/해시: `{obj}` / SHA256 `{profile['sources']['obj']['sha256']}`",
        "- 청사진 경로:": f"- 청사진 경로: `{blueprint}` / SHA256 `{blueprint_hash}`" if blueprint else "- 청사진 경로: 미제공 — OBJ 초안만 생성, 청사진 비율 검수 미실시",
        "- 원본 단위·앞 방향·위 방향:": f"- 원본 단위·앞 방향·위 방향: 단위 미확인 / 임시 앞 {axes['forward']}, 위 {axes['up']} (검수 필요)",
        "- 팔레트/재질 설정 위치:": "- 팔레트/재질 설정 위치: `style.json`; 부품별 예외 `vehicle.json`",
    }.items():
        brief = brief.replace(placeholder, value, 1)
    (target/"ART_BRIEF.md").write_text(brief + "\n\n자동 수집: `inventory.json`; 재생성 설정: `vehicle.json`; 검수 상태: `output/report.json`.\n", encoding="utf-8")
    print(f"Prepared {len(parts)} groups / {len(model['faces'])} faces: {target/'vehicle.json'}")


def partition(model, profile):
    """Assign every source face exactly once; explicit face overrides extract parts.

    Subsets use global, zero-based source face IDs. Empty base remainders are legal.
    No omission is allowed without an explicit action + reason in the profile.
    """
    groups = {g["id"]: g for g in model["groups"]}
    assigned, base, owners = {}, {}, {}
    ids = set()
    for part in profile["parts"]:
        if part["id"] in ids:
            raise ValueError(f"Duplicate part ID: {part['id']}")
        ids.add(part["id"])
        group = groups[part["source_group"]]
        if "faces" not in part:
            if group["id"] in base:
                raise ValueError(f"Two default owners for {group['id']}")
            base[group["id"]] = part
            continue
        subset = set(part["faces"])
        if len(subset) != len(part["faces"]) or not subset <= set(group["faces"]):
            raise ValueError(f"Duplicate/out-of-group source faces: {part['id']}")
        assigned[part["id"]] = sorted(subset)
        for i in subset:
            if i in owners:
                raise ValueError(f"Face {i} extracted twice")
            owners[i] = part["id"]
    for group in model["groups"]:
        remaining = [i for i in group["faces"] if i not in owners]
        if remaining and group["id"] not in base:
            raise ValueError(f"Unassigned source group: {group['id']}")
        if group["id"] in base:
            part = base[group["id"]]
            assigned[part["id"]] = remaining
            owners.update({i: part["id"] for i in remaining})
    if len(owners) != len(model["faces"]):
        raise ValueError("Source face coverage is incomplete")
    for part in profile["parts"]:
        if part.get("action", "keep") not in {"keep", "omit"}:
            raise ValueError("Actions: keep / omit; replacement geometry needs a separate recorded edit")
        if part.get("action") == "omit" and (not part.get("reason") or not part.get("confirmed")):
            raise ValueError(f"Omission needs confirmation and reason: {part['id']}")
    return assigned


def paint_levels(profile, style):
    """Apply contact constraints only to authored top colors, never world Z."""
    parts = {p["id"]: p for p in profile["parts"]}
    parents = defaultdict(list)
    for edge in profile["contacts"]:
        if edge["upper"] not in parts or edge["lower"] not in parts:
            raise ValueError("Contact references an unknown part")
        parents[edge["upper"]].append(edge["lower"])
    result, visiting = {}, set()

    def solve(key):
        if key in visiting:
            raise ValueError("Cyclic paint contact graph")
        if key in result:
            return result[key]
        visiting.add(key)
        p = parts[key]
        special = style["roles"][p["role"]]
        value = p.get("top", special if special is not None else style["planes"]["roof"])
        for lower in parents[key]:
            if special is not None or style["roles"][parts[lower]["role"]] is not None:
                raise ValueError("Dark material exceptions do not belong in TAN contact constraints")
            value = max(value, solve(lower) + style["contact_step"])
        if not style["minimum"] <= value <= style["maximum"]:
            raise ValueError(f"Contact palette exceeds headroom: {key} = {value}; revise authored bases")
        result[key] = value
        visiting.remove(key)
        return value

    for key in parts:
        solve(key)
    return result


def paint_rgb(value, role, style):
    # Defined in display space, then converted once for Blender emission.
    tint = style["tint"]
    y = dot(tint, (.2126, .7152, .0722))
    saturation = max(.24, min(1, (value-65)/165))
    if role in {"metal", "rubber", "track", "optics", "recess"}:
        saturation = .08
    ratios = [v/y for v in tint]
    deviation = max(ratios)-1
    saturation = min(saturation, (255-value)/(value*deviation)) if deviation > 0 else saturation
    return tuple(value*(1+(ratio-1)*saturation) for ratio in ratios)


def linear(value):
    v = value/255
    return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4


def run(args):
    profile_path = Path(args.profile).resolve()
    (profile_path.parent/"output/report.json").unlink(missing_ok=True)
    profile = read_json(profile_path)
    for source in profile["sources"].values():
        if sha256(source["path"]) != source["sha256"]:
            raise ValueError("Source hash changed; create a new intake / remap source face IDs")
    blender = args.blender or shutil.which("blender")
    if not blender:
        candidates = sorted(Path("C:/Program Files/Blender Foundation").glob("Blender */blender.exe"))
        blender = str(candidates[-1]) if candidates else None
    if not blender:
        raise ValueError("Blender not found; pass --blender <absolute executable path>")
    subprocess.run([blender, "-b", "-t", str(args.threads), "--python-exit-code", "1",
                    "--python", str(ROOT/"blender_build.py"), "--", str(profile_path)], check=True)
    from images import finish
    finish(profile_path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    init = commands.add_parser("prepare", help="Create a new vehicle project; never overwrite one")
    init.add_argument("--obj", required=True)
    init.add_argument("--blueprint", help="Optional for OBJ-only drafts; absence remains a review blocker")
    init.add_argument("--output", required=True)
    init.add_argument("--name")
    init.add_argument("--forward", choices=["x", "-x", "y", "-y", "z", "-z"], default="z")
    init.add_argument("--up", choices=["x", "-x", "y", "-y", "z", "-z"], default="y")
    init.add_argument("--scale", type=float, default=1)
    init.set_defaults(handler=prepare)
    build = commands.add_parser("build", help="Build, render, export and report review blockers")
    build.add_argument("profile")
    build.add_argument("--blender")
    build.add_argument("--threads", type=int, default=8)
    build.set_defaults(handler=run)
    component = commands.add_parser("components", help="Find source-connected parts; optionally extract by seed face")
    component.add_argument("profile")
    component.add_argument("--group", required=True)
    component.add_argument("--tolerance", type=float)
    component.add_argument("--seed", type=int)
    component.add_argument("--part-id")
    component.set_defaults(handler=inspect_components)
    args = parser.parse_args()
    try:
        args.handler(args)
    except (ValueError, KeyError, OSError) as error:
        parser.exit(1, f"Vehicle pipeline: {error}\n")


if __name__ == "__main__":
    main()
