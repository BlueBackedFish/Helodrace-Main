"""M3A3 Bradley rework on the common pipeline (base vehicle, BUSK deferred).

Same rules as M1A2 SEPv3: RimThunder M2A3 value ladder, contact directions, accents, grille
panels, sub-pixel cleanup, large-curve smooth shading, boundary lines. New: destructible
`damage_layers` for optics and the ammunition lid so the runtime can remove or char them.
Vehicle object names stay in this file only. Run after intake to rewrite vehicle.json.
"""
from pathlib import Path
from collections import defaultdict
import re
import sys
import numpy as np

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent/"VehiclePipeline"))
import pipeline as common

HULL_TOP, TURRET_TOP, STEP = 216, 192, 24
WHIP_REASON = "1.8 m thin whip antenna is subpixel at 256px; separate whip group only"
THIN_REASON = ("Sub-pixel rod/rail/wire (middle dimension < 3 cm, length > 12 cm): thinner than 1px at "
               "256px, removed to reduce model noise (style section 3)")
NO_THIN_CLEANUP = ("wheel_", "track_", "suspension_", "gun_barrel", "bone_gun", "mg_twin")
TOW = {"roll_05", "rocket_mount_01", "roll_03", "roll_04", "roll_06", "roll_07", "roll_08"}
ACCENTS = {  # whole groups -> (base group, reason)
    **{n: ("@root", "Tow shackle / towing pintle highlight") for n in
       ["ex_decor_l_01", "ex_decor_r_01", "ex_decor_l_02", "ex_decor_r_02", "ex_decor_04", "ex_decor_l_05", "ex_decor_r_06"]},
    **{n: ("@root", "Head light housing highlight; lens stays a separate optics colour") for n in
       ["ex_lantern_l_01", "ex_lantern_r_01"]},
    **{n: ("bone_turret", "Smoke grenade discharger highlight") for n in ["ex_decor_02", "ex_decor_03"]}}
HANDLE_GROUPS = {"hatch_01", "hatch_02", "hatch_03", "hatch_05"}
EYE_GROUPS = {"@root", "bone_turret"}
# Destructible overlays (runtime: remove, or swap to the charred variant). Optics + ammunition lid.
DAMAGE = [
    ("commanderViewer", "turret", ["bone_commander_sight_v"], "Commander's independent viewer (CIV)"),
    ("gunnerSight", "turret", ["roll_02"], "Gunner's ISU sight head"),
    ("leftHeadlight", "hull", ["ex_lantern_l_01"], "Left headlight"),
    ("rightHeadlight", "hull", ["ex_lantern_r_01"], "Right headlight"),
    ("cargoHatch", "hull", ["hatch_05"], "Upper rear cargo hatch over the TOW / 25 mm stowage (ammunition lid)"),
]


def configure():
    profile = common.read_json(ROOT/"vehicle.json")
    source = profile["sources"]["obj"]
    if common.sha256(source["path"]) != source["sha256"]:
        raise ValueError("Source changed; remap the M3A3 instead of reusing face selections")
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
        for key in ("planes", "side", "cylinder_axis", "cap", "accent", "smooth"):
            if key not in settings:
                part.pop(key, None)
        parts.append(part)
        return part["id"]

    def omit(part, reason):
        part.update(action="omit", reason=reason)
        omissions[reason] += len(groups[part["label"]]["faces"])

    turret_groups = {"bone_turret", "roll_01", "roll_02", "ex_decor_02", "ex_decor_03", "hatch_02", "hatch_03",
                     "ex_armor_03", "bone_commander_sight_v", "antenna_01", "antenna_02", "turret_inside"} | TOW
    barrel_groups = {"bone_gun", "gun_barrel", "mg_twin", "ex_decor_01"}
    turret_planes = {"roof": 192, "front_slope": 144, "side_slope": 120, "front_wall": 96, "side_wall": 96, "underside": 72}
    for group in model["groups"]:
        name = group["group"]
        layer = "barrel" if name in barrel_groups else "turret" if name in turret_groups else "hull"
        part = {"id": group["id"], "source_group": group["id"], "label": name, "role": "paint",
                "mode": "armor", "layer": layer, "top": TURRET_TOP if layer == "turret" else HULL_TOP,
                "confirmed": True}
        if layer == "turret":
            part["planes"] = dict(turret_planes)
        skirt = re.fullmatch(r"ex_armor_[lr]_0\d", name)
        if name.startswith("antenna_"):
            omit(part, WHIP_REASON)
        elif name == "turret_inside":
            part.update(role="recess", mode="flat", top=48)
        elif skirt:
            # RimThunder M2A3 skirt 179/255 -> 144; top edge keeps the roof value as a highlight line.
            part.update(planes={"side_wall": 144, "front_wall": 144, "side_slope": 168, "front_slope": 168})
        elif name.startswith("suspension_"):
            part.update(role="metal", mode="flat", top=72)
        elif name.startswith("wheel_"):
            part.update(role="rubber", mode="cylinder", cylinder_axis=[1, 0, 0], top=40, side=40, cap=96,
                        smooth={"low": 80, "high": 176, "signed": True})
        elif name.startswith("track_"):
            part.update(role="track", mode="flat", top=40)
        elif name == "gun_barrel":
            # RimThunder M2A3 barrel is dark metal (92/255): large curved surface, smooth band.
            part.update(role="metal", mode="cylinder", cylinder_axis=[0, 1, 0], top=96, side=96, cap=72,
                        smooth={"low": 72, "high": 144})
        elif name == "mg_twin":
            part.update(role="metal", mode="flat", top=96)
        elif name in {"bone_gun", "ex_decor_01"}:
            part.update(mode="two_tone", top=144, side=120)  # mantlet / gun shroud (RimThunder 179/233)
        elif name in {"hatch_01", "hatch_06", "hatch_05"}:
            part.update(role="hatch", mode="two_tone", side=144)
            contacts.append({"lower": groups["@root"]["id"], "upper": group["id"], "direction": "down"})
        elif name == "hatch_04":
            part.update(role="hatch", mode="two_tone", top=192, side=144)  # rear personnel door
        elif name == "hatch_02":
            # Commander's domed hatch: RimThunder 221 below the 233 roof -> smooth dome below the roof.
            part.update(role="hatch", mode="flat", top=168, smooth={"low": 144, "high": 192})
        elif name == "hatch_03":
            part.update(role="hatch", mode="two_tone", side=120)
            contacts.append({"lower": groups["bone_turret"]["id"], "upper": group["id"], "direction": "up"})
        elif name == "ex_armor_03":
            part.update(role="equipment", mode="two_tone", side=120)  # CIV armoured guard
            contacts.append({"lower": groups["bone_turret"]["id"], "upper": group["id"], "direction": "up"})
        elif name in TOW:
            # RimThunder TOW box 255 above the 233 turret roof -> 216 (up), dark walls.
            part.update(role="equipment", mode="two_tone", top=HULL_TOP, side=144)
        elif name in {"bone_commander_sight_v", "roll_02", "roll_01"}:
            part.update(role="equipment", mode="two_tone", top=HULL_TOP, side=144)
        elif name in ACCENTS:
            base, reason = ACCENTS[name]
            part.update(mode="flat", accent={"value": 240, "side": 192, "over": groups[base]["id"], "reason": reason})
        elif name.startswith("ex_decor") or name.startswith("ex_armor"):
            part.update(mode="two_tone", top=192 if layer == "hull" else 168, side=144)
        parts.append(part)

        if part.get("action") == "omit":
            continue
        subsets = defaultdict(list)
        for face_id in group["faces"]:
            material = model["faces"][face_id]["material"]
            if material in {"net_f", "net_c"}:
                subsets["grille"].append(face_id)
            elif material == "headlight_glass":
                subsets["headlamp_lens"].append(face_id)
            elif material in {"optic_glass", "dynamic_glass_chrome"}:
                subsets["glass"].append(face_id)
            elif material.startswith("rifled_barrel"):
                subsets["bore"].append(face_id)
        for key, faces in subsets.items():
            if key == "grille":
                for i, panel in enumerate(common.components(model, faces, 1e-6)):
                    co = vertices[[v for f in panel for v in model["faces"][f]["vertices"]]]
                    longest = float(np.max(np.ptp(co, axis=0)))
                    grille = dict(pattern="louvers", rows=int(np.clip(round(longest/.2), 3, 6)),
                                  columns=1, bar_fraction=.7, values=[120, 192, 168])
                    extract(group, f"grille_{i:02d}", panel, role="paint", mode="flat", top=96, grille=grille)
                continue
            role, value = {"headlamp_lens": ("optics", 192), "glass": ("optics", 32), "bore": ("recess", 40)}[key]
            extract(group, key, faces, role=role, top=value, mode="flat")

        if name in HANDLE_GROUPS | EYE_GROUPS:
            taken = {f for p in parts if p["source_group"] == group["id"] and p["id"] != group["id"] for f in p.get("faces", [])}
            rest = [f for f in group["faces"] if f not in taken]
            for comp in common.components(model, rest, 1e-6):
                co = vertices[[v for f in comp for v in model["faces"][f]["vertices"]]]
                size = np.ptp(co, axis=0)
                aligned = np.mean([np.max(np.abs(surface(f)[0])) > .97 for f in comp])
                if name in HANDLE_GROUPS:
                    keep = len(comp) >= 20 and .04 <= size.max() <= .2 and size[2] >= .02
                    reason = "Hatch handle / hinge highlight"
                else:
                    keep = (len(comp) >= 40 and .08 <= size.max() <= .2 and size[2] >= .35*size.max()
                            and aligned < .35 and co[:, 2].max() > (1.7 if name == "@root" else 2.6))
                    reason = "Lifting / tie-down eye highlight"
                if keep:
                    extract(group, f"accent_{min(comp)}", comp, mode="flat",
                            accent={"value": 240, "side": 192, "over": group["id"], "reason": reason})

        if layer != "barrel" and part["role"] == "paint" and "accent" not in part and not name.startswith(NO_THIN_CLEANUP):
            taken = {f for p in parts if p["source_group"] == group["id"] and p["id"] != group["id"] for f in p.get("faces", [])}
            rest = [f for f in group["faces"] if f not in taken]
            for comp in common.components(model, rest, 1e-6):
                d = np.sort(np.ptp(vertices[[v for f in comp for v in model["faces"][f]["vertices"]]], axis=0))
                if d[1] < .03 and d[2] > .12:
                    item = extract(group, f"cleanup_{min(comp)}", comp)
                    next(p for p in parts if p["id"] == item).update(action="omit", reason=THIN_REASON)
                    omissions[THIN_REASON] += len(comp)

    # Turret yaw axis: flattest round shell of the turret group, anchored on the hull roof.
    hull = groups["@root"]
    hull_shell = max(common.components(model, hull["faces"], 1e-6), key=len)
    # The Bradley turret group has no ring shell; the turret basket floor (turret_inside) is a clean
    # circle about the yaw axis. Fit its lowest vertices.
    best = None
    for faces in common.components(model, groups["turret_inside"]["faces"], 1e-6):
        if len(faces) < 100:
            continue
        co = np.unique(vertices[[v for i in faces for v in model["faces"][i]["vertices"]]], axis=0)
        low = co[co[:, 2] <= np.percentile(co[:, 2], 15)]
        xy = low[:, :2]
        fit, *_ = np.linalg.lstsq(np.column_stack((2*xy[:, 0], 2*xy[:, 1], np.ones(len(xy)))),
                                 np.sum(xy*xy, axis=1), rcond=None)
        center = fit[:2]
        radius = float(np.sqrt(max(fit[2]+center @ center, 0)))
        residual = float(np.max(np.abs(np.linalg.norm(xy-center, axis=1)-radius)))
        if .5 < radius < 1.3 and residual < .02 and (best is None or residual < best[2]):
            best = (center, radius, residual, float(low[:, 2].mean()), min(faces))
    if best is None:
        raise ValueError("No circular turret ring found; select the yaw axis manually")
    center, radius, residual, ring_z, seed = best
    roof = [surface(f)[1][2] for f in hull_shell if surface(f)[0][2] > .98
            and radius < np.linalg.norm(surface(f)[1][:2]-center) < radius+.35]
    attach_z = float(np.median(roof)) if roof else ring_z
    pivot = [round(float(center[0]), 6), round(float(center[1]), 6), round(attach_z, 3)]

    kept = [p for p in parts if p.get("action") != "omit"]
    turret_hatches = {p["id"] for p in kept if p["source_group"] in {groups["hatch_02"]["id"], groups["hatch_03"]["id"]}}
    rigs = [
        {"id": "TURRET_YAW", "usage": "turret_yaw", "pivot": pivot, "axis": "z",
         "parts": [p["id"] for p in kept if p["layer"] == "turret" and p["id"] not in turret_hatches],
         "confirmed": True, "animate": True, "angle": 35,
         "evidence": f"Turret basket floor circle seed {seed}; fitted radius {radius:.6f}, residual {residual:.6f}; anchor Z = hull roof"},
        {"id": "GUN_ELEVATION", "parent": "TURRET_YAW", "pivot": [-.25, -.6, 2.45], "axis": "x",
         "parts": [p["id"] for p in kept if p["layer"] == "barrel"], "confirmed": False, "animate": False,
         "evidence": "Provisional M242 trunnion; not verified"},
        {"id": "TURRET_HATCHES", "parent": "TURRET_YAW", "pivot": pivot, "axis": "z", "parts": sorted(turret_hatches),
         "confirmed": False, "animate": False, "evidence": "Hatches follow the turret; hinge axes not verified"}]
    damage_layers = []
    for key, layer, names, label in DAMAGE:
        ids = [p["id"] for p in kept if p["source_group"] in {groups[n]["id"] for n in names}]
        damage_layers.append({"id": key, "layer": layer, "parts": ids, "label": label})
    profile.update(axes=axes, parts=parts, rigs=rigs, contacts=contacts, damage_layers=damage_layers,
                   variant_notes=("M3A3 Bradley source OBJ (War Thunder extract), base vehicle. BUSK III kit deferred to a "
                                  "later task. Optics and the cargo (ammunition) hatch are destructible overlays. "
                                  "Rear ramp, TOW deploy and gun elevation rigs are not yet set."))
    profile["review"] = {"blueprint": False, "style_reference": False, "native_size": False, "geometry": False, "game": False}
    profile["style_references"] = {"rimthunder": "C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3724793908/Textures/Vehicles/M2A3"}
    common.partition(model, profile)
    levels = common.paint_levels(profile, common.read_json(ROOT/"style.json"))
    common.write_json(ROOT/"vehicle.json", profile)
    common.write_json(ROOT/"mapping_report.json", {
        "source_sha256": source["sha256"], "ring_seed": seed, "ring_radius": radius, "ring_residual": residual,
        "yaw_pivot": pivot, "omitted_faces_by_reason": dict(omissions), "source_faces": len(model["faces"]),
        "parts": len(parts), "contacts": contacts, "contact_levels": {c["upper"]: levels[c["upper"]] for c in contacts},
        "damage_layers": damage_layers, "provisional_rigs": [r["id"] for r in rigs if not r["confirmed"]]})
    print(f"Configured {len(parts)} parts; omitted {dict(omissions)}; ring residual {residual:.6f}; pivot {pivot}")


if __name__ == "__main__":
    configure()
