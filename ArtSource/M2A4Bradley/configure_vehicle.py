"""M2A4 Bradley (WARNO mod GLB) on the common pipeline: first pass, TAN paint only.

Source is a low-poly game model whose detail lives in MERDC camouflage textures. Following the
house rules (fixed Emission, per-face TAN ladder) the source textures are dropped and every face is
painted from role/plane. The hull and turret are each a single mesh, so hatches, optics and
headlights are not yet separated (no contacts or damage layers on them in this pass).
Vehicle node names stay in this file only. Run after intake to rewrite vehicle.json.
"""
from pathlib import Path
from collections import defaultdict
import re
import sys
import numpy as np

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent/"VehiclePipeline"))
import pipeline as common

HULL_TOP, TURRET_TOP = 216, 192
THIN_REASON = ("Sub-pixel rod/rail/wire/whip antenna (middle dimension < 3 cm, length > 12 cm): thinner than 1px "
               "at 256px, removed to reduce model noise (style section 3)")
WHIP_REASON = "Whip antenna (0.5-2.1 m tall, < 6 cm thick): subpixel at 256px, selected per connected component"
NO_THIN_CLEANUP =("Roue_", "Chenille_", "Canon_", "Axe_Canon_01", "Axe_Canon_04")
TURRET = {"Tourelle_01", "Fist", "Axe_Canon_02"}
BARREL = {"Axe_Canon_01", "Canon_01", "Axe_Canon_04", "Canon_04"}
# GLB node origins (Blender world, Z-up +X forward) converted to canonical (by, -bx, bz).
TURRET_YAW = [-0.187, 0.2863, 2.0646]      # Tourelle_01
GUN_TRUNNION = [-0.1851, -0.1588, 2.4667]  # Axe_Canon_01
TOW_PIVOT = [0.6918, -0.0391, 2.3528]      # Axe_Canon_02


def configure():
    profile = common.read_json(ROOT/"vehicle.json")
    source = profile["sources"]["obj"]
    if common.sha256(source["path"]) != source["sha256"]:
        raise ValueError("Source changed; re-run convert_glb.py and remap instead of reusing face selections")
    model = common.read_obj(source["path"])
    axes = {"forward": "x", "up": "y", "scale": 1, "confirmed": True}
    vertices = np.array([common.transform(v, axes) for v in model["vertices"]])
    groups = {g["object"]: g for g in model["groups"]}
    parts, omissions, contacts = [], defaultdict(int), []

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

    turret_planes = {"roof": 192, "front_slope": 144, "side_slope": 120, "front_wall": 96, "side_wall": 96, "underside": 72}
    for group in model["groups"]:
        name = group["object"]
        layer = "barrel" if name in BARREL else "turret" if name in TURRET else "hull"
        part = {"id": group["id"], "source_group": group["id"], "label": name, "role": "paint",
                "mode": "armor", "layer": layer, "top": TURRET_TOP if layer == "turret" else HULL_TOP,
                "confirmed": True}
        if name == "Tourelle_01":
            part["planes"] = dict(turret_planes)
        elif name.startswith("Roue_"):
            part.update(role="rubber", mode="cylinder", cylinder_axis=[1, 0, 0], top=40, side=40, cap=96,
                        smooth={"low": 80, "high": 176, "signed": True})
        elif name.startswith("Chenille_"):
            part.update(role="track", mode="flat", top=40)
        elif name == "Canon_01":
            # 25 mm M242 barrel: large curved metal surface, smooth band (RimThunder M2A3 92/255).
            part.update(role="metal", mode="cylinder", cylinder_axis=[0, 1, 0], top=96, side=96, cap=72,
                        smooth={"low": 72, "high": 144})
        elif name in {"Axe_Canon_04", "Canon_04"}:
            part.update(role="metal", mode="flat", top=96)  # coaxial M240C
        elif name == "Axe_Canon_01":
            part.update(mode="two_tone", top=144, side=120)  # mantlet / gun shroud
        elif name == "Axe_Canon_02":
            # TOW launcher box above the 192 turret roof -> 216 (up), dark walls.
            part.update(role="equipment", mode="two_tone", top=HULL_TOP, side=144)
            contacts.append({"lower": groups["Tourelle_01"]["id"], "upper": group["id"], "direction": "up"})
        elif name == "Fist":
            # Separate turret add-ons (sights, boxes, launchers): equipment tops above the roof.
            part.update(role="equipment", mode="two_tone", top=HULL_TOP, side=144)
            contacts.append({"lower": groups["Tourelle_01"]["id"], "upper": group["id"], "direction": "up"})
        parts.append(part)

        lenses = [f for f in group["faces"] if model["faces"][f]["material"].startswith("Lights")]
        if lenses:
            extract(group, "lamp_lens", lenses, role="optics", mode="flat", top=192)

        if part["role"] in {"paint", "equipment"} and not name.startswith(NO_THIN_CLEANUP):
            taken = {f for p in parts if p["source_group"] == group["id"] and p["id"] != group["id"] for f in p.get("faces", [])}
            rest = [f for f in group["faces"] if f not in taken]
            for comp in common.components(model, rest, 1e-6):
                size = np.ptp(vertices[[v for f in comp for v in model["faces"][f]["vertices"]]], axis=0)
                if size[2] > .5 and max(size[0], size[1]) < .06:
                    # Whip antennas are modelled 4-6 cm thick in this low-poly source (5 found: 0.54-2.08 m tall).
                    item = extract(group, f"whip_{min(comp)}", comp)
                    next(p for p in parts if p["id"] == item).update(action="omit", reason=WHIP_REASON)
                    omissions[WHIP_REASON] += len(comp)
                    continue
                d = np.sort(size)
                if d[1] < .03 and d[2] > .12:
                    item = extract(group, f"cleanup_{min(comp)}", comp)
                    next(p for p in parts if p["id"] == item).update(action="omit", reason=THIN_REASON)
                    omissions[THIN_REASON] += len(comp)

    kept = [p for p in parts if p.get("action") != "omit"]
    rigs = [
        {"id": "TURRET_YAW", "usage": "turret_yaw", "pivot": TURRET_YAW, "axis": "z",
         "parts": [p["id"] for p in kept if p["layer"] == "turret" and p["source_group"] != groups["Axe_Canon_02"]["id"]],
         "confirmed": True, "animate": True, "angle": 35,
         "evidence": "GLB node Tourelle_01 origin (source rig turret yaw)"},
        {"id": "GUN_ELEVATION", "parent": "TURRET_YAW", "pivot": GUN_TRUNNION, "axis": "x",
         "parts": [p["id"] for p in kept if p["layer"] == "barrel"], "confirmed": True, "animate": False,
         "evidence": "GLB node Axe_Canon_01 origin; coaxial M240 (Axe_Canon_04) is its child in the source"},
        {"id": "TOW_ELEVATION", "parent": "TURRET_YAW", "pivot": TOW_PIVOT, "axis": "x",
         "parts": [p["id"] for p in kept if p["source_group"] == groups["Axe_Canon_02"]["id"]],
         "confirmed": True, "animate": False, "evidence": "GLB node Axe_Canon_02 origin"}]
    profile.update(axes=axes, parts=parts, rigs=rigs, contacts=contacts, damage_layers=[],
                   variant_notes=("M2A4 Bradley, WARNO mod GLB converted to OBJ (convert_glb.py). First pass: TAN paint only. "
                                  "Hull/turret are single meshes; hatches, optics, headlights, grilles and accents not yet separated."))
    profile["review"] = {"blueprint": False, "style_reference": False, "native_size": False, "geometry": False, "game": False}
    profile["style_references"] = {"rimthunder": "C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3724793908/Textures/Vehicles/M2A3"}
    common.partition(model, profile)
    levels = common.paint_levels(profile, common.read_json(ROOT/"style.json"))
    common.write_json(ROOT/"vehicle.json", profile)
    common.write_json(ROOT/"mapping_report.json", {
        "source_sha256": source["sha256"], "yaw_pivot": TURRET_YAW,
        "omitted_faces_by_reason": dict(omissions), "source_faces": len(model["faces"]),
        "parts": len(parts), "contacts": contacts, "contact_levels": {c["upper"]: levels[c["upper"]] for c in contacts}})
    print(f"Configured {len(parts)} parts; omitted {dict(omissions)}")


if __name__ == "__main__":
    configure()
