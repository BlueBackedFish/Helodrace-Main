"""M8 AGS Level III source mapping. Run after the common OBJ intake.

Every source-specific name, selection and pivot belongs here, not in the shared painter.
"""
from collections import defaultdict
from pathlib import Path
import sys
import numpy as np

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent / "VehiclePipeline"))
import pipeline as common


def configure():
    profile = common.read_json(ROOT / "vehicle.json")
    source = profile["sources"]["obj"]
    if common.sha256(source["path"]) != source["sha256"]:
        raise ValueError("Source revision changed; remap face selections before building")
    model = common.read_obj(source["path"])
    axes = dict(forward="x", up="y", scale=1, confirmed=True)
    vertices = np.array([common.transform(v, axes) for v in model["vertices"]])
    parts, omissions = [], []
    groups = {g["group"]: g for g in model["groups"]}

    def extract(group, suffix, faces, **settings):
        if not faces:
            return
        owner = next(p for p in parts if p["id"] == group["id"])
        parts.append({**owner, "id": group["id"] + "_" + suffix,
                      "label": group["group"] + " / " + suffix,
                      "faces": sorted(faces), **settings})

    def normal(face):
        co = vertices[model["faces"][face]["vertices"]]
        n = np.sum(np.cross(co[1:-1] - co[0], co[2:] - co[0]), axis=0)
        return n / max(np.linalg.norm(n), 1e-12)

    for index, group in enumerate(model["groups"]):
        name = group["group"]
        turret = 25 <= index <= 77 or name.startswith("antenna_")
        layer = "barrel" if name in {"bone_gun", "gun_barrel", "gun_mask_01", "ex_armor_turret_01", "ex_armor_turret_02"} else "turret" if turret else "hull"
        part = dict(id=group["id"], source_group=group["id"], label=name,
                    role="paint", mode="armor", layer=layer, top=180 if turret else 168,
                    confirmed=True)
        if name.startswith("track_"):
            part.update(role="track", mode="flat", top=56)
        elif name.startswith("suspension_"):
            part.update(role="metal", mode="flat", top=76)
        elif name.startswith("wheel_"):
            part.update(mode="cylinder", cylinder_axis=[1, 0, 0], side=60, cap=120, top=120)
        elif name == "gun_barrel":
            part.update(mode="cylinder", cylinder_axis=[0, 1, 0], side=156, cap=156, top=156)
        elif name == "bone_mg_gun_twin_01":
            part.update(role="metal", mode="flat", top=68)
        elif name.startswith("bone_mg_aa_"):
            part.update(mode="two_tone", top=204, side=156)
        elif name.startswith("hatch_"):
            part.update(role="hatch", mode="two_tone", top=204 if turret else 192, side=168 if turret else 156)
        elif name.startswith("ex_decor_"):
            part.update(mode="two_tone", top=156, side=120)
        elif name.startswith("ex_lantern_"):
            part.update(mode="two_tone", top=192, side=144)
        elif name in {"ex_armor_02", "ex_armor_03", "ex_armor_04", "ex_armor_05", "ex_armor_06", "ex_armor_07", "ex_armor_08", "ex_armor_09", "ex_armor_10"}:
            # Shallow Level III frontal blocks are not roof panels. The generic
            # normal threshold merges them with the roof; retain their own steps.
            part.update(top=156, planes=dict(roof=156, front_slope=144, side_slope=132,
                                            front_wall=120, side_wall=108, underside=72))
        elif name.startswith("antenna_"):
            part.update(action="omit", reason="Separate 2.49 m whip is subpixel at 256px; retain antenna bases in bone_turret and preserve omitted source faces in the manifest")
        # These are the low, unarmored headlight positions. Level III has the raised
        # originals ex_lantern_[lr]_01 above its additional frontal armor.
        if name in {"ex_lantern_l_02", "ex_lantern_r_02"}:
            part.update(action="omit", reason="Alternative low headlight assembly intersects Level III frontal armor; use the supplied raised _01 assembly")
        parts.append(part)
        if part.get("action") == "omit":
            omissions.append(dict(part=part["id"], source_group=name, faces=len(group["faces"]), reason=part["reason"]))
            continue
        subsets = defaultdict(list)
        for face in group["faces"]:
            material = model["faces"][face]["material"]
            if material in {"optic_glass", "us_turret_glass"}:
                subsets["optics"].append(face)
            elif material == "headlight_glass":
                subsets["headlight"].append(face)
            elif material in {"net_f", "net_n"}:
                subsets["grille"].append(face)
            elif material in {"mg_browning_m2hb", "mg_browning_m2hb_ammo", "mg_l37a1"}:
                subsets["gunmetal"].append(face)
            elif material == "rifled_barrel_b_rifled_barrel_b":
                subsets["bore"].append(face)
        for key, faces in subsets.items():
            if key == "grille":
                planes = []
                for face in faces:
                    n = normal(face)
                    cluster = next((c for c in planes if np.dot(c[0], n) > .9999), None)
                    if cluster is None:
                        planes.append((n, [face]))
                    else:
                        cluster[1].append(face)
                for i, (_, selection) in enumerate(planes):
                    extract(group, f"grille_{i:02d}", selection, role="paint", mode="flat", top=96,
                            grid=dict(cell_pixels=5, bar_fraction=.3, values=[84, 120]))
            else:
                role, top = {"optics": ("optics", 36), "headlight": ("optics", 192),
                             "gunmetal": ("metal", 68), "bore": ("recess", 32)}[key]
                extract(group, key, faces, role=role, mode="flat", top=top)

    # Select only the original forward sloping skin, leaving roof-mounted fittings
    # and the explicitly mapped Level III armor pieces in their own paint roles.
    hull = groups["@root"]
    front = []
    for face in hull["faces"]:
        n = normal(face)
        center = vertices[model["faces"][face]["vertices"]].mean(axis=0)
        if (center[1] < -1.2 and center[2] > .9 and .35 < n[2] < .985
                and abs(n[1]) > abs(n[0])):
            front.append(face)
    extract(hull, "front_glacis", front, mode="flat", top=144)

    # Fit the two concentric 24-sided race rings rather than using a bounding-box center.
    group = groups["bone_turret"]
    ids = sorted({v for f in group["faces"] for v in model["faces"][f]["vertices"]})
    points = np.unique(vertices[ids], axis=0)
    ring = points[abs(points[:, 2] - 1.4529) < .00005, :2]
    radius_guess = np.linalg.norm(ring - [0, -.0192], axis=1)
    ring = ring[radius_guess >= np.median(radius_guess)]
    fit, *_ = np.linalg.lstsq(np.column_stack((2 * ring, np.ones(len(ring)))), (ring * ring).sum(axis=1), rcond=None)
    radius = float(np.sqrt(fit[2] + np.dot(fit[:2], fit[:2])))
    residual = float(abs(np.linalg.norm(ring - fit[:2], axis=1) - radius).max())
    if len(ring) != 24 or residual > .0001:
        raise ValueError("Turret race no longer matches this source mapping")
    pivot = [round(float(fit[0]), 7), round(float(fit[1]), 7), 1.5271]
    commander = groups["hatch_04"]["id"]
    driver = groups["hatch_03"]["id"]
    roof = groups["hatch_05"]["id"]
    sight = groups["hatch_06"]["id"]
    rig_parts = lambda prefix: [p["id"] for p in parts if p["source_group"] == prefix]
    independent = set(rig_parts(commander) + rig_parts(driver) + rig_parts(roof))
    profile.update(axes=axes, parts=parts,
        variant_notes="XM8 source with supplied Level II/III appliqué and raised Level III headlights; only the Level III output is produced. No Level I/II variants, invented ERA or source shape reduction. Physical dimensions are source units, not certified production-M8 measurements.",
        rigs=[
            dict(id="TURRET_YAW", usage="turret_yaw", pivot=pivot, axis="z",
                 parts=[p["id"] for p in parts if p["layer"] == "turret" and p["id"] not in independent],
                 confirmed=True, animate=True, angle=35,
                 evidence=f"24-point outer race at source Z=1.4529, radius={radius:.7f}, residual={residual:.7f}; anchor Z is the upper mounting plane"),
            dict(id="GUN_ELEVATION", parent="TURRET_YAW", pivot=[0, -.55, 1.96], axis="x",
                 parts=[p["id"] for p in parts if p["layer"] == "barrel"], confirmed=False, animate=False,
                 evidence="Source mantlet/trunnion region; independent hierarchy retained, exact mechanical elevation not validated"),
            dict(id="COMMANDER_HATCH", parent="TURRET_YAW", pivot=[-.44, 1.0, 2.4], axis="x",
                 parts=rig_parts(commander), confirmed=False, animate=False, evidence="Original hatch assembly; provisional hinge line"),
            dict(id="GUN_ROOF_COVER", parent="TURRET_YAW", pivot=[0, .38, 2.4], axis="x",
                 parts=rig_parts(roof), confirmed=False, animate=False, evidence="Original linked cover, no unverified motion enabled"),
            dict(id="DRIVER_HATCH", pivot=[0, -1.35, 1.4], axis="x", parts=rig_parts(driver),
                 confirmed=False, animate=False, evidence="Original driver hatch; provisional mechanism, no unverified animation")],
        contacts=[dict(lower="g0025", upper=key) for key in [commander, roof, sight]] + [dict(lower="g0000", upper=driver)])
    profile["review"] = dict(blueprint=False, style_reference=False, native_size=False, geometry=False, game=False)
    profile["style_references"] = dict(
        rimthunder="C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3724793908/Textures/Vehicles/M1A2",
        legion="C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3786789710/Textures/Things/Vehicles/LGIFV",
        same_vehicle_rimthunder="Not present in installed RimThunder; M1A2 is used for style only",
        army_characteristics="https://digital.library.unt.edu/ark:/67531/metadc18619/m1/48/",
        army_manual="https://generalstaff.org/MAN/USA/FM017-15_1996.pdf",
        blueprint_note="No user-provided blueprint; original XM8 geometry is preserved, production-M8 dimensions are comparison data only")
    common.partition(model, profile)
    common.paint_levels(profile, common.read_json(ROOT / "style.json"))
    common.write_json(ROOT / "vehicle.json", profile)
    common.write_json(ROOT / "mapping_report.json", dict(source_sha256=source["sha256"],
        source_groups=len(model["groups"]), source_faces=len(model["faces"]), parts=len(parts),
        omissions=omissions, ring_radius=radius, ring_residual=residual, yaw_pivot=pivot,
        provisional_rigs=[r["id"] for r in profile["rigs"] if not r["confirmed"]]))
    print(f"Configured {len(parts)} Level III parts; 24-point yaw race residual {residual:.7f}")


if __name__ == "__main__":
    configure()
