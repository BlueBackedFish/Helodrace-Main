"""Common-pipeline profile for the scratch-built M3A3-type IFV (`build_model.py`).

Geometry is authored per role, so parts map by group name; the only face-level extraction is by
material (glass / lens / bore / recess). Palette, contact directions, accents and damage keys follow
the same rules as `ArtSource/M3A3Bradley` (RimThunder M2A3 ladder 216/192/168/144/120/96) so the two
read as one fleet, but no source face IDs are involved. Writes vehicle.json, inventory.json,
analysis/ and mapping_report.json. Run `build_model.py` first.
"""
from pathlib import Path
import re
import shutil
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent/"VehiclePipeline"))
import pipeline as common
from images import parts_atlas
import build_model as geo

HULL_TOP, TURRET_TOP = 216, 192
TURRET_PLANES = {"roof": 192, "front_slope": 144, "side_slope": 120, "front_wall": 96, "side_wall": 96, "underside": 72}
HULL_PLANES = {"front_wall": 120, "side_wall": 120}  # RimThunder M2A3 wall 0.58 of roof; skirts brighter (0.70)
SKIRT_PLANES = {"side_wall": 144, "front_wall": 144, "side_slope": 168, "front_slope": 168}
WHEEL = dict(role="rubber", mode="cylinder", cylinder_axis=[1, 0, 0], top=40, side=40, cap=96,
             smooth={"low": 80, "high": 176, "signed": True})
TURRET_GROUPS = {"turret", "gunner_hatch", "gunner_hatch_handle", "commander_hatch", "commander_hatch_handle",
                 "isu_sight", "civ_guard", "civ_head", "smoke_launchers", "tow_launcher", "tow_mount", "tow_tubes",
                 "bustle_floor", "bustle_rack", "bustle_stowage", "antenna_bases"}
BARREL_GROUPS = {"mantlet", "gun_shroud", "gun_barrel", "coax"}
HANDLE = "Hatch / door grab handle highlight"
SUBSETS = {"glass": ("optics", 32), "lens": ("optics", 192), "bore": ("recess", 40)}
DAMAGE = [
    ("commanderViewer", "turret", ["civ_head"], "Commander's independent viewer (CIV)"),
    ("gunnerSight", "turret", ["isu_sight"], "Gunner's sight head"),
    ("leftHeadlight", "hull", ["headlight_l"], "Left headlight"),
    ("rightHeadlight", "hull", ["headlight_r"], "Right headlight"),
]


def settings(name, gid):
    """Role/paint for one authored group. Returns (part fields, contact (lower, direction) or None)."""
    if re.fullmatch(r"wheel_[lr]_\d\d|sprocket_[lr]|idler_[lr]|roller_[lr]_\d\d", name):
        return dict(WHEEL), None
    if name.startswith("track_"):
        return dict(role="track", mode="flat", top=40), None
    if name.startswith("suspension_"):
        return dict(role="metal", mode="flat", top=72), None
    if name.startswith("skirt_"):
        return dict(planes=dict(SKIRT_PLANES)), None
    if name in {"hull"} or name.startswith("side_armor_"):
        return dict(planes=dict(HULL_PLANES)), None
    if name in {"exhaust_grille", "engine_grille"}:
        rows = 5 if name == "exhaust_grille" else 6
        return dict(mode="flat", top=96, grille=dict(pattern="louvers", rows=rows, columns=1, bar_fraction=.7,
                                                     values=[120, 192, 168])), None
    if name == "engine_deck":
        return dict(mode="two_tone", side=144), ("hull", "down")
    if name in {"driver_hatch", "cargo_hatch"}:
        return dict(role="hatch", mode="two_tone", side=144), ("hull", "down")
    if name == "driver_periscopes":
        return dict(role="equipment", mode="two_tone", top=168, side=120), None
    if name == "trim_vane":
        return dict(role="hatch", mode="two_tone", top=168, side=144), None
    if name.startswith("headlight_"):
        return dict(role="equipment", mode="two_tone", top=192, side=144), None
    if name == "rear_ramp":
        return dict(role="hatch", mode="two_tone", top=192, side=144), None
    if name == "ramp_door":
        return dict(role="hatch", mode="two_tone", top=168, side=168), None
    if name.startswith("rear_stowage_"):
        return dict(mode="two_tone", top=192, side=144), None
    if name == "turret":
        return dict(planes=dict(TURRET_PLANES)), None
    if name == "gunner_hatch":
        return dict(role="hatch", mode="two_tone", side=120), ("turret", "up")
    if name == "commander_hatch":
        # Domed hatch: RimThunder dome 221 below the 233 roof -> smooth dome up to the roof value.
        return dict(role="hatch", mode="flat", top=168, smooth={"low": 144, "high": 192}), None
    if name == "isu_sight":
        return dict(role="equipment", mode="two_tone", top=HULL_TOP, side=144), ("turret", "up")
    if name == "civ_guard":
        return dict(role="equipment", mode="two_tone", side=120), ("turret", "up")
    if name == "civ_head":
        return dict(role="equipment", mode="two_tone", side=144), ("civ_guard", "down")
    if name == "tow_launcher":
        return dict(role="equipment", mode="two_tone", top=HULL_TOP, side=144), None
    if name == "tow_mount":
        return dict(role="equipment", mode="two_tone", top=168, side=120), None
    if name in {"tow_tubes", "bustle_floor"}:
        return dict(role="recess", mode="flat", top=40 if name == "tow_tubes" else 48), None
    if name == "bustle_rack":
        return dict(role="equipment", mode="two_tone", top=168, side=120), None
    if name == "bustle_stowage":
        return dict(role="equipment", mode="two_tone", top=192, side=144), None
    if name == "antenna_bases":
        return dict(role="metal", mode="flat", top=96), None
    if name == "mantlet":
        return dict(mode="two_tone", top=144, side=120), None
    if name == "gun_shroud":
        return dict(role="metal", mode="cylinder", cylinder_axis=[0, 1, 0], top=96, side=96, cap=72), None
    if name == "gun_barrel":
        return dict(role="metal", mode="cylinder", cylinder_axis=[0, 1, 0], top=96, side=96, cap=72,
                    smooth={"low": 72, "high": 144}), None
    if name == "coax":
        return dict(role="metal", mode="flat", top=96), None
    raise ValueError(f"Unmapped authored group: {name}")


ACCENTS = {  # group -> (base group, reason)
    "tow_hooks": ("hull", "Front tow hook highlight"),
    "lifting_eyes": ("hull", "Lifting eye highlight"),
    "driver_hatch_handle": ("driver_hatch", HANDLE),
    "cargo_hatch_handle": ("cargo_hatch", HANDLE),
    "trim_vane_handle": ("hull", "Trim vane release handle highlight"),
    "ramp_door_handle": ("rear_ramp", HANDLE),
    "gunner_hatch_handle": ("gunner_hatch", HANDLE),
    "commander_hatch_handle": ("turret", HANDLE),
    "smoke_launchers": ("turret", "Smoke grenade discharger highlight"),
}


def configure():
    obj = geo.OUTPUT
    if not obj.exists():
        raise ValueError("Run build_model.py first")
    if not (ROOT/"style.json").exists():
        # Snapshot of the M3A3 ladder so both Bradleys share values; later common edits do not leak in.
        shutil.copyfile(ROOT.parent/"M3A3Bradley/style.json", ROOT/"style.json")
    style = common.read_json(ROOT/"style.json")
    style.update(name="m3a3_scratch_rimthunder_ladder_v1",
                 description="Scratch-built M3A3-type IFV; copy of the M3A3 Bradley ladder (hull roof 216, turret roof 192). Not an approved standard.")
    common.write_json(ROOT/"style.json", style)

    model = common.read_obj(obj)
    axes = {"forward": "-y", "up": "z", "scale": 1, "confirmed": True}
    groups = {g["group"]: g for g in model["groups"]}
    parts, contacts = [], []
    for group in model["groups"]:
        name = group["group"]
        layer = "barrel" if name in BARREL_GROUPS else "turret" if name in TURRET_GROUPS else "hull"
        part = {"id": name, "source_group": group["id"], "label": name, "role": "paint", "mode": "armor",
                "layer": layer, "top": TURRET_TOP if layer == "turret" else HULL_TOP, "confirmed": True}
        if name in ACCENTS:
            base, reason = ACCENTS[name]
            part.update(mode="flat", accent={"value": 240, "side": 192, "over": base, "reason": reason})
        else:
            fields, contact = settings(name, group["id"])
            part.update(fields)
            if contact:
                contacts.append({"lower": contact[0], "upper": name, "direction": contact[1]})
        parts.append(part)
        by_material = {}
        for face_id in group["faces"]:
            material = model["faces"][face_id]["material"]
            if material in SUBSETS:
                by_material.setdefault(material, []).append(face_id)
        for material, faces in by_material.items():
            role, value = SUBSETS[material]
            parts.append({"id": f"{name}_{material}", "source_group": group["id"], "label": f"{name}_{material}",
                          "faces": sorted(faces), "role": role, "mode": "flat", "layer": layer, "top": value,
                          "confirmed": True})

    def owned(*names):
        return [p["id"] for p in parts if p["label"] in names or p["label"].split("_glass")[0] in names
                or p["label"].split("_lens")[0] in names or p["label"].split("_bore")[0] in names]

    gunner = owned("gunner_hatch", "gunner_hatch_handle")
    commander = owned("commander_hatch", "commander_hatch_handle")
    ramp, door = owned("rear_ramp"), owned("ramp_door", "ramp_door_handle")
    driver, cargo = owned("driver_hatch", "driver_hatch_handle"), owned("cargo_hatch", "cargo_hatch_handle")
    special = set(gunner+commander)
    rigs = [
        {"id": "TURRET_YAW", "usage": "turret_yaw", "pivot": list(geo.TURRET_PIVOT), "axis": "z",
         "parts": [p["id"] for p in parts if p["layer"] == "turret" and p["id"] not in special],
         "confirmed": True, "animate": True, "angle": 35,
         "evidence": "Authored turret yaw axis (build_model.TURRET_PIVOT), placed at the reference M3A3 ring centre; anchor Z = hull roof"},
        {"id": "GUN_ELEVATION", "parent": "TURRET_YAW", "pivot": list(geo.GUN_TRUNNION), "axis": "x",
         "parts": [p["id"] for p in parts if p["layer"] == "barrel"], "confirmed": True, "animate": False,
         "evidence": "Authored trunnion at the back of the mantlet on the gun axis"},
        {"id": "GUNNER_HATCH", "parent": "TURRET_YAW", "pivot": list(geo.GUNNER_HATCH_HINGE), "axis": "x",
         "parts": gunner, "confirmed": True, "animate": False, "evidence": "Authored rear hinge barrel"},
        {"id": "COMMANDER_HATCH", "parent": "TURRET_YAW", "pivot": list(geo.COMMANDER_HATCH_HINGE), "axis": "x",
         "parts": commander, "confirmed": True, "animate": False, "evidence": "Authored rear edge of the dome"},
        {"id": "DRIVER_HATCH", "pivot": list(geo.DRIVER_HATCH_HINGE), "axis": "x", "parts": driver,
         "confirmed": True, "animate": False, "evidence": "Authored hinge block at the hatch rear"},
        {"id": "CARGO_HATCH", "pivot": list(geo.CARGO_HATCH_HINGE), "axis": "x", "parts": cargo,
         "confirmed": True, "animate": False, "evidence": "Authored rear hinge barrels"},
        {"id": "REAR_RAMP", "pivot": list(geo.RAMP_HINGE), "axis": "x", "parts": ramp, "confirmed": True,
         "animate": True, "angle": -75, "evidence": "Authored bottom hinge line of the ramp; opens rearward and down"},
        {"id": "RAMP_DOOR", "parent": "REAR_RAMP", "pivot": list(geo.DOOR_HINGE), "axis": "z", "parts": door,
         "confirmed": True, "animate": False, "evidence": "Authored vertical hinge on the door's outboard edge"},
    ]
    damage_layers = [{"id": key, "layer": layer, "parts": owned(*names), "label": label}
                     for key, layer, names, label in DAMAGE]
    profile = {
        "schema": 1, "name": "M3A3 Bradley (scratch-built)",
        "sources": {"obj": {"path": str(obj), "sha256": common.sha256(obj)}},
        "axes": axes, "style": "style.json",
        "variant_notes": ("Scratch-built M3A3-type IFV: new authored geometry (build_model.py). Proportions and equipment "
                          "placement taken from the War Thunder M3A3 extract and ArtSource/M3A3Bradley; no source faces "
                          "reused. Base vehicle, no BUSK. Optics and headlights are destructible overlays (no isolated "
                          "ammunition store, so no ammunition lid)."),
        "blueprint_views": {}, "parts": parts, "rigs": rigs, "contacts": contacts,
        "review": {"blueprint": False, "style_reference": False, "native_size": False, "geometry": False, "game": False},
        "damage_layers": damage_layers,
        "references": {
            "proportion_obj": "C:/Users/human/Downloads/Dagor-Asset-Explorer-1.0.0/Dagor-Asset-Explorer-1.0.0/ext/m3a3_bradley_0.obj",
            "proportion_obj_sha256": "8cf128f1c44037f3caff8db7104a83ff6c6e70b05d06a1e89dbe5b5ab420b813",
            "previous_model": "ArtSource/M3A3Bradley"},
        "style_references": {"rimthunder": "C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3724793908/Textures/Vehicles/M2A3"},
    }
    common.partition(model, profile)
    levels = common.paint_levels(profile, style)
    common.write_json(ROOT/"vehicle.json", profile)
    common.write_json(ROOT/"inventory.json", common.inventory(model, axes))
    parts_atlas(model, axes, ROOT/"analysis")
    common.write_json(ROOT/"mapping_report.json", {
        "source_sha256": profile["sources"]["obj"]["sha256"], "source_faces": len(model["faces"]),
        "groups": len(model["groups"]), "parts": len(parts), "contacts": contacts,
        "contact_levels": {c["upper"]: levels[c["upper"]] for c in contacts},
        "damage_layers": damage_layers, "animated_rigs": [r["id"] for r in rigs if r.get("animate")]})
    print(f"Configured {len(parts)} parts from {len(model['groups'])} groups / {len(model['faces'])} faces")


if __name__ == "__main__":
    configure()
