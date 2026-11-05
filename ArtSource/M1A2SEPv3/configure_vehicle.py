"""M1A2 SEPv3 mapping: base vehicle without camouflage nets or TUSK ERA.

Uses the common pipeline with RimThunder-style contact directions
(VEHICLE_ART_STYLE.md section 5). Vehicle object names stay in this file only.
Run after intake to reproduce vehicle.json from this source revision.
"""
from pathlib import Path
from collections import defaultdict
import re
import sys
import numpy as np

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent/"VehiclePipeline"))
import pipeline as common

NETS = {"gun_barrel_net", "bone_mg_aa_h_01_net", "turret_net", "turret_net_01", "body_net", "body_net_01"}
NET_REASON = ("Camouflage net (Barracuda) removed on user request; the extracted net meshes also "
              "lost their skinning transform and float away from the vehicle")
ERA_REASON = "TUSK reactive armour tile; optional kit kept out of the base vehicle (style section 6)"
RAIL_REASON = "TUSK ERA mounting rail/frame on the skirts or turret sides; only needed with the ERA tiles"
WHIP_REASON = "2.5 m thin whip antenna is subpixel at 256px; separate whip group only"

# Ladder values (style.json planes: roof 216 / slope 192,168 / wall 144).
HULL_TOP, TURRET_TOP, STEP = 216, 192, 24
CHEEK_Y, MANTLET_HALF_WIDTH = -.3, .35  # canonical metres; front is -Y


def is_turret(index, name):
    return 29 <= index <= 86 and name not in {"bone_gun", "gun_barrel"}


def configure():
    profile = common.read_json(ROOT/"vehicle.json")
    source = profile["sources"]["obj"]
    if common.sha256(source["path"]) != source["sha256"]:
        raise ValueError("Source changed; remap the M1A2 SEPv3 instead of reusing face selections")
    model = common.read_obj(source["path"])
    axes = {"forward": "x", "up": "y", "scale": 1, "confirmed": True}
    vertices = np.array([common.transform(v, axes) for v in model["vertices"]])
    groups = {g["group"]: g for g in model["groups"]}
    parts, omissions, contacts = [], defaultdict(int), []
    normal_cache = {}

    def surface(face_id):
        if face_id not in normal_cache:
            co = vertices[model["faces"][face_id]["vertices"]]
            normal = np.sum(np.cross(co[1:-1]-co[0], co[2:]-co[0]), axis=0)
            normal_cache[face_id] = (normal/max(np.linalg.norm(normal), 1e-12), np.mean(co, axis=0))
        return normal_cache[face_id]

    def extract(group, label, faces, **settings):
        if not faces:
            return None
        owner = next(p for p in parts if p["id"] == group["id"])
        part = {**owner, "id": group["id"]+"_"+label, "label": label, "faces": sorted(faces), **settings}
        for key in ("planes", "side", "cylinder_axis", "cap"):
            if key not in settings:
                part.pop(key, None)
        parts.append(part)
        return part["id"]

    def omit(part, reason):
        part.update(action="omit", reason=reason)
        omissions[reason] += len(groups[part["label"]]["faces"])

    for group in model["groups"]:
        name, index = group["group"], int(group["id"][1:])
        layer = "barrel" if name in {"bone_gun", "gun_barrel"} else "turret" if is_turret(index, name) else "hull"
        part = {"id": group["id"], "source_group": group["id"], "label": name, "role": "paint",
                "mode": "armor", "layer": layer, "top": TURRET_TOP if layer == "turret" else HULL_TOP,
                "confirmed": True}
        body = re.fullmatch(r"ex_armor_body_[lr]_(\d+)", name)
        if name in NETS:
            omit(part, NET_REASON)
        elif name.startswith("ex_era_"):
            omit(part, ERA_REASON)
        elif body and 7 <= int(body.group(1)) <= 18:
            omit(part, RAIL_REASON)
        elif re.fullmatch(r"ex_armor_turret_[lr]_0[3-8]", name):
            omit(part, RAIL_REASON)
        elif name.startswith("antenna_"):
            omit(part, WHIP_REASON)
        elif body:
            # Base side skirts: RimThunder M1A2 skirt face is 0.58 of the roof (147/255); use the
            # 144 ladder step (0.67). Their top edge keeps the roof value as a thin highlight line.
            part.update(planes={"side_wall": 144, "front_wall": 144, "side_slope": 168, "front_slope": 168})
        elif name.startswith("suspension_"):
            part.update(role="metal", mode="flat", top=72)
        elif name.startswith("wheel_"):
            # Dark tyre tread, lighter outer disc so the road wheels read below the skirt.
            part.update(role="rubber", mode="cylinder", cylinder_axis=[1, 0, 0], top=40, side=40, cap=96)
        elif name.startswith("track_"):
            part.update(role="track", mode="flat", top=40)
        elif name == "gun_barrel":
            part.update(mode="cylinder", cylinder_axis=[0, 1, 0], top=168, side=168, cap=144)
        elif name == "bone_gun":
            part.update(top=TURRET_TOP)
        elif name.startswith("hatch_") and layer == "hull":
            # Hull deck hatches and engine panels: one step below the hull roof (down).
            part.update(role="hatch", mode="two_tone", side=144)
            contacts.append({"lower": groups["root"]["id"], "upper": group["id"], "direction": "down"})
        elif name in {"hatch_03", "hatch_04", "ex_armor_05", "ex_armor_06", "optic_commander"}:
            # Turret-top hatches, blow-out panels and CITV: back to the top step (up), dark walls.
            part.update(role="hatch" if name.startswith("hatch") else "equipment", mode="two_tone", side=144)
            contacts.append({"lower": groups["bone_turret"]["id"], "upper": group["id"], "direction": "up"})
        elif name in {"bone_mg_aa_h_01", "bone_mg_aa_v_01", "mg_aa_mount_v_01", "bone_turret_02", "optic_gun"}:
            part.update(role="equipment", mode="two_tone", top=HULL_TOP, side=144)  # CROWS
        elif name in {"bone_mg_aa_h", "bone_mg_aa_v", "ex_armor_turret_01", "ex_armor_turret_02",
                      "ex_armor_turret_03", "ex_armor_04"}:
            part.update(role="equipment", mode="two_tone", top=HULL_TOP, side=144)  # loader MG and shield
        elif layer == "turret" and (name.startswith("ex_decor") or name in {"ex_armor_07", "ex_armor_08"}):
            part.update(mode="two_tone", top=168, side=120)  # bustle rack and stowage boxes
        elif name.startswith("ex_lantern") or (layer == "hull" and name.startswith("ex_decor")):
            part.update(mode="two_tone", top=192, side=144)
        parts.append(part)

        if part.get("action") == "omit":
            continue
        subsets = defaultdict(list)
        for face_id in group["faces"]:
            material = model["faces"][face_id]["material"]
            if "inside_elfebein" in material:
                subsets["interior"].append(face_id)
            elif material in {"net_c", "net_f"}:
                subsets["grille"].append(face_id)
            elif material == "headlight_glass":
                subsets["headlamp_lens"].append(face_id)
            elif material in {"side_glass", "dynamic_glass_chrome"}:
                subsets["glass"].append(face_id)
            elif material.startswith("mg_") or material == "chain_a":
                subsets["gunmetal"].append(face_id)
            elif material == "smooth_barrel_a_smooth_barrel_a":
                subsets["bore"].append(face_id)
        for key, faces in subsets.items():
            if key == "grille":
                clusters = []
                for face_id in faces:
                    normal, _ = surface(face_id)
                    match = next((c for c in clusters if np.dot(c[0], normal) > .9999), None)
                    (clusters.append((normal, [face_id])) if match is None else match[1].append(face_id))
                for i, (_, cluster) in enumerate(clusters):
                    extract(group, f"grille_patch_{i:02d}", cluster, role="paint", mode="flat", top=96,
                            grid={"cell_pixels": 5, "bar_fraction": .3, "values": [72, 120]})
                continue
            role, value = {"interior": ("recess", 48), "headlamp_lens": ("optics", 192),
                           "glass": ("optics", 32), "gunmetal": ("metal", 72), "bore": ("recess", 40)}[key]
            extract(group, key, faces, role=role, top=value, mode="flat")

    # The Abrams glacis is very shallow; a generic upward-normal threshold calls it roof.
    # Select the actual exterior shell (largest connected component of the hull group).
    hull = groups["root"]
    hull_shell = max(common.components(model, hull["faces"], 1e-6), key=len)
    extracted = {f for p in parts if p["source_group"] == hull["id"] for f in p.get("faces", [])}
    glacis, shoulders = [], []
    for face_id in hull_shell:
        if face_id in extracted:
            continue
        normal, center = surface(face_id)
        if normal[2] > .3 and center[2] > 1.0:
            if center[1] < -.9 and abs(normal[1]) >= abs(normal[0]):
                glacis.append(face_id)
            elif abs(center[0]) > 1.1 and abs(normal[0]) > abs(normal[1]) and normal[2] < .985:
                shoulders.append(face_id)
    extract(hull, "front_glacis", glacis, mode="flat", top=192)
    extract(hull, "side_shoulders", shoulders, mode="flat", top=168)

    # The whole Abrams turret roof tilts slightly forward, so normals alone cannot find the
    # front armour cheeks. RimThunder M1A2 paints the cheek tops ~2-3 ladder steps below the
    # roof (150 vs 233); select them by position (ahead of the hatches, outside the mantlet).
    turret_group = groups["bone_turret"]
    turret_shell = max(common.components(model, turret_group["faces"], 1e-6), key=len)
    taken = {f for p in parts if p["source_group"] == turret_group["id"] for f in p.get("faces", [])}
    cheeks, turret_sides = [], []
    for face_id in turret_shell:
        if face_id in taken:
            continue
        normal, center = surface(face_id)
        if normal[2] >= .9 and center[1] < CHEEK_Y and abs(center[0]) > MANTLET_HALF_WIDTH:
            cheeks.append(face_id)
        elif .3 < normal[2] < .9 and abs(normal[0]) > abs(normal[1]):
            turret_sides.append(face_id)
    extract(turret_group, "front_cheeks", cheeks, mode="flat", top=TURRET_TOP-2*STEP)
    extract(turret_group, "side_slopes", turret_sides, mode="flat", top=TURRET_TOP-STEP)

    # Yaw axis: fit a circle to the flattest round connected shell of the turret group.
    turret = groups["bone_turret"]
    best = None
    for faces in common.components(model, turret["faces"], 1e-6):
        if len(faces) < 24:
            continue
        co = np.unique(vertices[[v for i in faces for v in model["faces"][i]["vertices"]]], axis=0)
        if np.ptp(co[:, 2]) > .25:
            continue
        xy = co[:, :2]
        fit, *_ = np.linalg.lstsq(np.column_stack((2*xy[:, 0], 2*xy[:, 1], np.ones(len(xy)))),
                                 np.sum(xy*xy, axis=1), rcond=None)
        center = fit[:2]
        radius = float(np.sqrt(fit[2]+center @ center))
        residual = float(np.max(np.abs(np.linalg.norm(xy-center, axis=1)-radius)))
        if .8 < radius < 1.3 and residual < .002 and (best is None or co[:, 2].mean() < best[3]):
            best = (center, radius, residual, float(co[:, 2].mean()), min(faces))
    if best is None:
        raise ValueError("No circular turret ring found; select the yaw axis manually")
    center, radius, residual, ring_z, seed = best
    # Any Z on the vertical yaw axis is valid; use the hull roof just outside the ring so the
    # 64-degree hull -> 90-degree turret compositing anchor sits on the attachment plane.
    roof = [surface(f)[1][2] for f in hull_shell if surface(f)[0][2] > .98
            and radius < np.linalg.norm(surface(f)[1][:2]-center) < radius+.35]
    if not roof:
        raise ValueError("No hull roof faces around the turret ring")
    attach_z = float(np.median(roof))
    pivot = [round(float(center[0]), 6), round(float(center[1]), 6), round(attach_z, 3)]

    def hinge(name):
        co = vertices[[v for i in groups[name]["faces"] for v in model["faces"][i]["vertices"]]]
        low, high = co.min(axis=0), co.max(axis=0)
        return [round(float((low[0]+high[0])/2), 3), round(float(high[1]), 3), round(float(low[2]), 3)]

    kept = [p for p in parts if p.get("action") != "omit"]
    hatches = {name: [p["id"] for p in kept if p["source_group"] == groups[name]["id"]]
               for name in ["hatch_03", "hatch_04", "hatch_01"]}
    hatch_ids = {i for ids in hatches.values() for i in ids}
    rigs = [
        {"id": "TURRET_YAW", "usage": "turret_yaw", "pivot": pivot, "axis": "z",
         "parts": [p["id"] for p in kept if p["layer"] == "turret" and p["id"] not in hatch_ids],
         "confirmed": True, "animate": True, "angle": 35,
         "evidence": f"Turret ring shell seed {seed}; fitted radius {radius:.6f}, residual {residual:.6f}; anchor Z = median hull roof Z around the ring"},
        {"id": "GUN_ELEVATION", "parent": "TURRET_YAW", "pivot": [-.02, -1.0, 1.90], "axis": "x",
         "parts": [p["id"] for p in kept if p["layer"] == "barrel"], "confirmed": False, "animate": False,
         "evidence": "Provisional trunnion near the mantlet/barrel axis; engineering motion not verified"},
        {"id": "LOADER_HATCH", "parent": "TURRET_YAW", "pivot": hinge("hatch_03"), "axis": "x",
         "parts": hatches["hatch_03"], "confirmed": False, "animate": False,
         "evidence": "Provisional rear edge of the source hatch bounds"},
        {"id": "COMMANDER_HATCH", "parent": "TURRET_YAW", "pivot": hinge("hatch_04"), "axis": "x",
         "parts": hatches["hatch_04"], "confirmed": False, "animate": False,
         "evidence": "Provisional rear edge of the source hatch bounds"},
        {"id": "DRIVER_HATCH", "pivot": hinge("hatch_01"), "axis": "z", "parts": hatches["hatch_01"],
         "confirmed": False, "animate": False,
         "evidence": "Driver hatch needs a lift/swing mechanism; not a verified simple hinge"}]
    profile.update(axes=axes, parts=parts, rigs=rigs, contacts=contacts,
                   variant_notes=("M1A2 SEPv3 source OBJ (War Thunder extract). Base vehicle: camouflage nets, "
                                  "TUSK ERA tiles and their rails/frames and whip antennas omitted. Loader shield, "
                                  "CROWS and CITV kept. RimThunder reference is its base M1A2 (SEP generation not "
                                  "stated); SEPv3 detail differences are recorded in ART_BRIEF.md. No blueprint."))
    profile["review"] = {"blueprint": False, "style_reference": False, "native_size": False,
                         "geometry": False, "game": False}
    profile["style_references"] = {
        "rimthunder": "C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3724793908/Textures/Vehicles/M1A2",
        "rimthunder_tusk": "C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3724793908/Textures/Vehicles/M1A2/TUSK"}
    common.partition(model, profile)
    levels = common.paint_levels(profile, common.read_json(ROOT/"style.json"))
    common.write_json(ROOT/"vehicle.json", profile)
    common.write_json(ROOT/"mapping_report.json", {
        "source_sha256": source["sha256"], "ring_seed": seed, "ring_radius": radius,
        "ring_residual": residual, "yaw_pivot": pivot, "omitted_faces_by_reason": dict(omissions),
        "source_faces": len(model["faces"]), "parts": len(parts), "contacts": contacts,
        "contact_levels": {c["upper"]: levels[c["upper"]] for c in contacts},
        "provisional_rigs": [r["id"] for r in rigs if not r["confirmed"]]})
    print(f"Configured {len(parts)} parts; omitted {dict(omissions)}; ring residual {residual:.6f}")


if __name__ == "__main__":
    configure()
