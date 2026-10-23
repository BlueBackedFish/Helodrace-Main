"""M1A2-specific source mapping, fixed paint and provisional service pivots.

Uses common pipeline.py; never places M1A2 object names in the common painter.
Run after intake to reproduce vehicle.json from this source revision.
"""
from pathlib import Path
from collections import defaultdict
import sys
import numpy as np

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent/"VehiclePipeline"))
import pipeline as common


def configure():
    profile = common.read_json(ROOT/"vehicle.json")
    source = profile["sources"]["obj"]
    if common.sha256(source["path"]) != source["sha256"]:
        raise ValueError("Source changed; remap the M1A2 instead of reusing face selections")
    model = common.read_obj(source["path"])
    axes = {"forward": "x", "up": "y", "scale": 1, "confirmed": True}
    vertices = np.array([common.transform(v, axes) for v in model["vertices"]])
    groups = {g["group"]: g for g in model["groups"]}
    parts = []
    notes = []
    normal_cache = {}

    def surface(face_id):
        if face_id not in normal_cache:
            co = vertices[model["faces"][face_id]["vertices"]]
            normal = np.sum(np.cross(co[1:-1]-co[0], co[2:]-co[0]), axis=0)
            length = np.linalg.norm(normal)
            normal_cache[face_id] = (normal/max(length, 1e-12), np.mean(co, axis=0))
        return normal_cache[face_id]

    def extract(group, label, faces, **settings):
        if not faces:
            return None
        identifier = group["id"]+"_"+label
        owner = next(p for p in parts if p["id"] == group["id"])
        part = {**owner, "id": identifier, "label": label, "faces": sorted(faces), **settings}
        parts.append(part)
        return identifier

    for group in model["groups"]:
        name = group["group"]
        on_turret = (21 <= int(group["id"][1:]) <= 48 or name == "antenna_01")
        layer = "turret" if on_turret else "hull"
        if name in {"bone_gun", "gun_barrel"}:
            layer = "barrel"
        part = {"id": group["id"], "source_group": group["id"], "label": name,
                "role": "paint", "mode": "armor", "layer": layer,
                "top": 180 if on_turret else 168, "confirmed": True}
        if name.startswith("suspension_"):
            part.update(role="metal", mode="flat", top=76)
        elif name.startswith("wheel_"):
            part.update(mode="cylinder", cylinder_axis=[1, 0, 0], top=108, side=60, cap=108)
        elif name.startswith("track_"):
            part.update(role="track", mode="flat", top=56)
        elif name == "gun_barrel":
            part.update(mode="cylinder", cylinder_axis=[0, 1, 0], top=156, side=156, cap=156)
        elif name == "bone_gun":
            part.update(top=180)
        elif name.startswith("bone_mg_aa_v"):
            part.update(role="metal", mode="flat", top=64)
        elif name in {"mg_aa_mount_v_01", "mg_aa_mount_h_01", "bone_mg_aa_h"}:
            part.update(mode="two_tone", top=204, side=168)
        elif name == "optic_commander":
            # The group is the entire CITV housing, not a glass lens.
            part.update(role="equipment", mode="two_tone", top=204, side=156)
        elif name == "turret_01":
            part.update(role="equipment", mode="two_tone", top=192, side=156)
        elif name == "hatch_03":
            part.update(role="hatch", mode="two_tone", top=204, side=168)
        elif name == "hatch_04":
            part.update(role="hatch", mode="two_tone", top=216, side=180)
        elif name.startswith("hatch_"):
            part.update(role="hatch", mode="two_tone", top=192, side=156)
        elif name in {"ex_armor_01", "ex_armor_02"}:
            # Original blow-off panels, not ERA. Preserve both.
            part.update(top=204, mode="two_tone", side=168)
        elif name.startswith("ex_decor_") and on_turret:
            part.update(mode="two_tone", top=144, side=120)
        elif name.startswith("ex_lantern_"):
            part.update(mode="two_tone", top=180, side=144)
        elif name == "antenna_01":
            part.update(action="omit", reason="3.44m thin whip is subpixel in the 256px vehicle sprite; retain source mapping, omit only the separate whip group")
            notes.append({"part": part["id"], "reason": part["reason"], "faces": len(group["faces"])})
        parts.append(part)

        # Preserve source material subsets as explicit face selections. No source textures
        # or wear shading are baked into the output.
        subsets = defaultdict(list)
        for face_id in group["faces"]:
            material = model["faces"][face_id]["material"]
            if "inside_elfebein" in material:
                subsets["interior"].append(face_id)
            elif material in {"net_c", "net_f"}:
                subsets["grille_or_mesh"].append(face_id)
            elif material == "headlight_glass":
                subsets["headlamp_lens"].append(face_id)
            elif material.startswith("mg_") and not material.startswith("mg_mount"):
                subsets["gunmetal"].append(face_id)
            elif material == "smooth_barrel_a_smooth_barrel_a":
                subsets["bore"].append(face_id)
            elif material.startswith("water_barrel") or material.startswith("bag_"):
                subsets["stowage"].append(face_id)
        for key, faces in subsets.items():
            if key == "grille_or_mesh":
                clusters = []
                for face_id in faces:
                    normal, _ = surface(face_id)
                    match = next((c for c in clusters if np.dot(c[0], normal) > .9999), None)
                    if match is None:
                        clusters.append((normal, [face_id]))
                    else:
                        match[1].append(face_id)
                for i, (_, cluster) in enumerate(clusters):
                    extract(group, f"grille_patch_{i:02d}", cluster, role="paint", mode="flat", top=96,
                            grid={"cell_pixels": 5, "bar_fraction": .3, "values": [84, 120]})
                continue
            role, value = {"interior": ("recess", 64), "grille_or_mesh": ("recess", 96),
                           "headlamp_lens": ("optics", 204), "gunmetal": ("metal", 64),
                           "bore": ("recess", 40), "stowage": ("paint", 132)}[key]
            extract(group, key, faces, role=role, top=value, mode="flat")

    # The Abrams glacis is very shallow: a generic upward-normal threshold calls
    # it a roof. Select the actual exterior shell, not a whole height band.
    hull = groups["root"]
    hull_regions = common.components(model, hull["faces"], 1e-6)
    hull_shell = next(faces for faces in hull_regions if 16 in faces)
    material_faces = {face_id for part in parts if part["source_group"] == hull["id"]
                      for face_id in part.get("faces", [])}
    glacis, shoulders = [], []
    for face_id in hull_shell:
        if face_id in material_faces:
            continue
        normal, center = surface(face_id)
        if normal[2] > .3 and center[2] > 1.0:
            if center[1] < -.9 and abs(normal[1]) >= abs(normal[0]):
                glacis.append(face_id)
            elif abs(center[0]) > 1.1 and abs(normal[0]) > abs(normal[1]) and normal[2] < .985:
                shoulders.append(face_id)
    extract(hull, "front_glacis", glacis, mode="flat", top=144)
    extract(hull, "side_shoulders", shoulders, mode="flat", top=132)

    # Fit the yaw axis to the original circular bottom ring (a connected source shell).
    turret = groups["bone_turret"]
    regions = common.components(model, turret["faces"], 1e-6)
    ring = next(faces for faces in regions if 78837 in faces)
    coordinates = np.unique(vertices[[v for i in ring for v in model["faces"][i]["vertices"]]], axis=0)
    xy = coordinates[:, :2]
    fit, *_ = np.linalg.lstsq(np.column_stack((2*xy[:, 0], 2*xy[:, 1], np.ones(len(xy)))),
                             np.sum(xy*xy, axis=1), rcond=None)
    center = fit[:2]
    radius = float(np.sqrt(fit[2]+np.sum(center*center)))
    residual = float(np.max(np.abs(np.linalg.norm(xy-center, axis=1)-radius)))
    if residual > .002:
        raise ValueError("Turret source ring no longer matches its fitted yaw axis")
    # Z can be anywhere along a vertical yaw axis; select the hull/turret attachment
    # plane for the 64-degree hull -> 90-degree turret compositing anchor.
    pivot = [round(float(center[0]), 6), round(float(center[1]), 6), 1.56]
    turret_parts = [p["id"] for p in parts if p["layer"] == "turret" and p["label"] not in {"hatch_03", "hatch_04"}]
    gun_parts = [p["id"] for p in parts if p["layer"] == "barrel"]
    rigs = [
        {"id": "TURRET_YAW", "usage": "turret_yaw", "pivot": pivot, "axis": "z",
         "parts": turret_parts, "confirmed": True, "animate": True, "angle": 35,
         "evidence": f"Source annular component seed 78837; fitted radius {radius:.6f}, residual {residual:.6f}; anchor Z is hull attachment plane"},
        {"id": "GUN_ELEVATION", "parent": "TURRET_YAW", "pivot": [-.02, -1.03, 1.90],
         "axis": "x", "parts": gun_parts, "confirmed": False, "animate": False,
         "evidence": "Provisional source mantlet/trunnion axis; independent group preserved, engineering motion not verified"},
        {"id": "LOADER_HATCH", "parent": "TURRET_YAW", "pivot": [.30, .98, 2.42], "axis": "x",
         "parts": [groups["hatch_03"]["id"]], "confirmed": False, "animate": False,
         "evidence": "Provisional rear hinge line from source connected components"},
        {"id": "COMMANDER_HATCH", "parent": "TURRET_YAW", "pivot": [-.51, .96, 2.64], "axis": "x",
         "parts": [groups["hatch_04"]["id"]], "confirmed": False, "animate": False,
         "evidence": "Provisional rear support line; source hatch and cupola remain distinct"},
        {"id": "DRIVER_HATCH", "pivot": [-.42, -1.55, 1.48], "axis": "z",
         "parts": [p["id"] for p in parts if p["source_group"] == groups["hatch_01"]["id"]],
         "confirmed": False, "animate": False,
         "evidence": "Driver hatch needs lift/swing mechanism; do not fake a simple fully tested hinge"}]
    # Extracted subsets of a hatch must follow its hatch rig, not remain welded to the turret.
    for name, rig_id in [("hatch_03", "LOADER_HATCH"), ("hatch_04", "COMMANDER_HATCH")]:
        rig = next(r for r in rigs if r["id"] == rig_id)
        children = [p["id"] for p in parts if p["source_group"] == groups[name]["id"]]
        rigs[0]["parts"] = [key for key in rigs[0]["parts"] if key not in children]
        rig["parts"] = children
    profile.update(axes=axes, parts=parts, rigs=rigs,
                   variant_notes="M1A2 source OBJ; M1A1 body material names are shared asset labels, not proof of M1A1 geometry. Blueprint not supplied. No TUSK/ERA added.",
                   contacts=[{"lower": turret["id"], "upper": groups[name]["id"]}
                             for name in ["hatch_03", "hatch_04", "optic_commander", "ex_armor_01", "ex_armor_02"]])
    profile["review"] = {"blueprint": False, "style_reference": True,
                         "native_size": False, "geometry": False, "game": False}
    profile["style_references"] = {"rimthunder": "C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3724793908/Textures/Vehicles/M1A2",
                                   "gdls_dimensions": "https://international.gdls.com/english/products/ABRAMS/M1A2.pdf",
                                   "aiaa_arrangement": "https://aiaa.org/wp-content/uploads/2024/12/2023-24-heavy_lift_rfp.pdf",
                                   "blueprint_note": "Web text available; local PDF download unavailable; no blueprint match claimed"}
    common.partition(model, profile)
    common.paint_levels(profile, common.read_json(ROOT/"style.json"))
    common.write_json(ROOT/"vehicle.json", profile)
    common.write_json(ROOT/"mapping_report.json", {"source_sha256": source["sha256"], "ring_radius": radius,
                      "ring_residual": residual, "yaw_pivot": pivot, "omissions": notes,
                      "source_faces": len(model["faces"]), "parts": len(parts), "provisional_rigs": [r["id"] for r in rigs if not r["confirmed"]]})
    print(f"Configured {len(parts)} parts; source ring residual {residual:.6f}")


if __name__ == "__main__":
    configure()
