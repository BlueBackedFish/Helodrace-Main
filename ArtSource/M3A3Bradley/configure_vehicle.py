"""M3A3 Bradley rework on the common pipeline (base vehicle, BUSK deferred).

Height-band paint (style.json `height_paint`, 2026-11-13 user decision): no contact graph, highlight
accents or smooth gradients. Polygon reduction via style `simplify` plus per-part collapse for curved
fittings. Grille panels, sub-pixel cleanup and boundary lines as before. New: destructible
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
FASTENER_REASON = ("User-requested flat armour: remove separate shallow rivet / bolt heads; "
                   "preserve the continuous backing plate and functional fittings")


def is_fastener(model, vertices, faces):
    """Recognise the source's open/closed hexagonal and round fastener-head meshes.

    Test topology and a shallow, near-round footprint in its own principal frame,
    rather than deleting every small component (which would also lose hinges).
    The immutable source hash below pins these templates to this particular OBJ.
    """
    co = np.unique(vertices[[v for f in faces for v in model["faces"][f]["vertices"]]], axis=0)
    if (len(faces), len(co)) not in {
        (16, 12), (20, 12), (22, 16), (24, 14), (28, 16), (28, 20),
        (31, 20), (33, 20), (34, 24), (36, 20), (44, 24), (48, 32),
        (54, 30), (56, 20), (60, 32),
    }:
        return False
    _, _, frame = np.linalg.svd(co-co.mean(axis=0), full_matrices=False)
    thickness, width, diameter = np.sort(np.ptp(co @ frame.T, axis=0))
    return .004 <= diameter <= .08 and width >= .70*diameter and thickness <= .65*width


TOW = {"roll_05", "rocket_mount_01", "roll_03", "roll_04", "roll_06", "roll_07", "roll_08"}
# Curved fittings collapse-decimated before the planar merge (style `simplify`): fewer segments on
# wheels, hatches, lamps and guns so their curves read as a few flat facets. Big armour plates stay
# planar-merge only; collapse there leaves jagged shards along the silhouette.
CURVED_PREFIXES = ("wheel_", "track_", "hatch_", "ex_lantern_", "mg_twin", "roll_", "gun_barrel", "ex_decor_01",
                   "turret_inside", "suspension_", "bone_commander_sight_v")
CURVED_SIMPLIFY = {"collapse_ratio": .3}
FDE_RGB = [160, 140, 106]  # Flat Dark Earth (display sRGB)
# Destructible overlays (runtime: remove, or swap to the charred variant). Optics and lights only: no isolated ammunition store.
DAMAGE = [
    ("commanderViewer", "turret", ["bone_commander_sight_v"], "Commander's independent viewer (CIV)"),
    ("gunnerSight", "turret", ["roll_02"], "Gunner's ISU sight head"),
    ("leftHeadlight", "hull", ["ex_lantern_l_01"], "Left headlight"),
    ("rightHeadlight", "hull", ["ex_lantern_r_01"], "Right headlight"),
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
        for key in ("planes", "side", "cylinder_axis", "cap", "simplify", "height_steps", "slope_steps", "plane_angle", "rgb", "inflate"):
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
            part.update(role="rubber", mode="cylinder", cylinder_axis=[1, 0, 0], top=40, side=40, cap=96)
        elif name.startswith("track_"):
            part.update(role="track", mode="flat", top=40)
        elif name == "gun_barrel":
            # RimThunder M2A3 barrel is dark metal (92/255).
            part.update(role="metal", mode="cylinder", cylinder_axis=[0, 1, 0], top=64, side=64, cap=48)
        elif name == "mg_twin":
            part.update(role="metal", mode="flat", top=56)
        elif name in {"bone_gun", "ex_decor_01"}:
            # Mantlet / gun shroud: wide grouping so the faceted cone is not cut into two tones.
            part.update(mode="two_tone", top=144, side=120, plane_angle=45)
        elif name.startswith("ex_lantern_"):
            part.update(height_steps=-1)  # lamp guard/base one TAN step brighter; inner parts below
        elif name == "hatch_06":
            part.update(role="hatch", mode="two_tone", side=144, height_steps=-1)  # brighter than the glacis
        elif name == "hatch_05":
            part.update(role="hatch", mode="two_tone", side=144, slope_steps=1)
        elif name == "hatch_01":
            part.update(role="hatch", mode="two_tone", side=144)
        elif name == "hatch_04":
            part.update(role="hatch", mode="two_tone", top=192, side=144)  # rear personnel door
        elif name == "hatch_02":
            # Gunner hatch: same band as the turret roof, so a half step brighter (in-between value).
            part.update(role="hatch", mode="flat", top=168, slope_steps=1, height_steps=-.5)
        elif name == "hatch_03":
            part.update(role="hatch", mode="two_tone", side=120)
        elif name == "ex_armor_03":
            part.update(role="equipment", mode="two_tone", side=120)  # CIV armoured guard
        elif name in TOW:
            # RimThunder TOW box 255 above the 233 turret roof -> 216 (up), dark walls.
            part.update(role="equipment", mode="two_tone", top=HULL_TOP, side=144)
        elif name in {"bone_commander_sight_v", "roll_02", "roll_01"}:
            part.update(role="equipment", mode="two_tone", top=HULL_TOP, side=144)
        elif name.startswith("ex_decor") or name.startswith("ex_armor"):
            part.update(mode="two_tone", top=192 if layer == "hull" else 168, side=144)
        if name.startswith(CURVED_PREFIXES):
            part["simplify"] = dict(CURVED_SIMPLIFY)
        parts.append(part)

        if part.get("action") == "omit":
            continue
        # Heads are separate shells on top of intact plates, so omission leaves
        # a flat surface without cutting holes or changing the plate vertices.
        fastener_faces = set()
        if not name.startswith(("wheel_", "track_", "suspension_")) and name != "turret_inside":
            for comp in common.components(model, group["faces"], 1e-6):
                if is_fastener(model, vertices, comp):
                    fastener_faces.update(comp)
            if fastener_faces:
                extract(group, "fasteners", fastener_faces, action="omit", reason=FASTENER_REASON)
                omissions[FASTENER_REASON] += len(fastener_faces)
        subsets = defaultdict(list)
        for face_id in group["faces"]:
            if face_id in fastener_faces:
                continue
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

        if layer != "barrel" and part["role"] == "paint" and not name.startswith(NO_THIN_CLEANUP):
            taken = {f for p in parts if p["source_group"] == group["id"] and p["id"] != group["id"] for f in p.get("faces", [])}
            rest = [f for f in group["faces"] if f not in taken]
            for comp in common.components(model, rest, 1e-6):
                d = np.sort(np.ptp(vertices[[v for f in comp for v in model["faces"][f]["vertices"]]], axis=0))
                if d[1] < .03 and d[2] > .12:
                    item = extract(group, f"cleanup_{min(comp)}", comp)
                    next(p for p in parts if p["id"] == item).update(action="omit", reason=THIN_REASON)
                    omissions[THIN_REASON] += len(comp)

    # Colour detail exceptions (2026-11-13 user list). Seeds are source face IDs inside one
    # connected component; the pinned source hash keeps them valid.
    def take(name, seeds, label, **settings):
        group = groups[name]
        taken = {f for p in parts if p["source_group"] == group["id"] and p["id"] != group["id"] for f in p.get("faces", [])}
        rest = [f for f in group["faces"] if f not in taken]
        faces = set()
        for comp in common.components(model, rest, 1e-6):
            if set(seeds) & set(comp):
                faces.update(comp)
        missing = [s for s in seeds if s not in faces]
        if missing:
            raise ValueError(f"Detail seeds not found in {name}: {missing}")
        return extract(group, label, faces, **settings)

    hinge = dict(role="paint", mode="flat", fixed=145)
    axis = dict(role="metal", mode="flat", top=88)
    handle = dict(height_steps=2)
    # Turret gunner hatch: opening handle, hinge knuckles + arms, hinge pin.
    take("hatch_02", [204787], "handle", **handle)
    take("hatch_02", [204651, 204591, 204535, 204455, 204559], "hinge", **hinge)
    take("hatch_02", [206037], "hinge_axis", **axis)
    take("hatch_03", [203314], "handle", **handle)
    take("hatch_03", [202458, 202460, 202478], "hinge", **hinge)
    take("bone_turret", [178884], "hatch_03_hinge_axis", **axis)  # pin is modelled in the turret body
    # Headlamps: the two largest front-projected shells (guard, base) keep the brighter housing
    # step; lamp bodies, bezels and back plate inside them sit one step darker (height_steps 0).
    # Shells reaching outside the guard (the cable) stay with the housing.
    for name in ("ex_lantern_l_01",):
        group = groups[name]
        taken = {f for p in parts if p["source_group"] == group["id"] and p["id"] != group["id"] for f in p.get("faces", [])}
        shells = []
        for comp in common.components(model, [f for f in group["faces"] if f not in taken], 1e-6):
            co = vertices[[v for f in comp for v in model["faces"][f]["vertices"]]]
            shells.append((np.ptp(co[:, 0])*np.ptp(co[:, 2]), co.min(0), co.max(0), comp))
        shells.sort(key=lambda s: -s[0])
        lo = np.minimum(shells[0][1], shells[1][1])-.005
        hi = np.maximum(shells[0][2], shells[1][2])+.005
        inner = [f for _, a, b, comp in shells[2:] if np.all(a >= lo) and np.all(b <= hi) for f in comp]
        extract(group, "inner", inner, height_steps=0)
    # The right lamp's shells split differently; mirror the left classification face by face.
    from scipy.spatial import cKDTree
    left_inner = next(p for p in parts if p["id"] == groups["ex_lantern_l_01"]["id"]+"_inner")["faces"]
    left_rest = [f for f in groups["ex_lantern_l_01"]["faces"]
                 if f not in {g for p in parts if p["source_group"] == groups["ex_lantern_l_01"]["id"] and p["id"] != groups["ex_lantern_l_01"]["id"] for g in p.get("faces", [])}]
    pool = list(left_inner)+left_rest
    tree = cKDTree([surface(f)[1] for f in pool])
    inner_set = set(left_inner)
    group = groups["ex_lantern_r_01"]
    taken = {f for p in parts if p["source_group"] == group["id"] and p["id"] != group["id"] for f in p.get("faces", [])}
    right = [f for f in group["faces"] if f not in taken]
    _, nearest = tree.query([surface(f)[1]*np.array([-1, 1, 1]) for f in right])
    extract(group, "inner", [f for f, k in zip(right, nearest) if pool[k] in inner_set], height_steps=0)
    # Commander's viewer roof handles: grips and posts Flat Dark Earth. Its round base plate
    # sits a half step below the viewer head so the two heights read apart.
    take("ex_armor_03", [207399], "base_plate", height_steps=.5)
    # Smoke grenade dischargers live in the turret body mesh: four tubes + mount per side.
    take("bone_turret", [155431, 156316, 155688, 159134, 156406, 159300, 157462, 156000, 170382, 169880],
         "smoke_dischargers", role="metal", mode="flat", top=88)
    take("bone_commander_sight_v", [208013, 208389], "handle_grip", rgb=FDE_RGB, mode="flat")
    take("bone_commander_sight_v", [207937, 208103, 208205, 208287], "handle_post", rgb=FDE_RGB, mode="flat")
    # Hull rear cargo hatch: same treatment as the turret hatch (no separate hinge pin modelled).
    take("hatch_05", [82018, 82030, 82042, 82054], "handle", **handle)
    take("hatch_05", [81994, 81996, 81998], "hinge", **hinge)
    # Rear personnel door: hinge and handles as fixed exceptions.
    take("hatch_04", [109776], "hinge", **hinge)
    take("hatch_04", [109794, 109806], "handle", role="paint", mode="flat", fixed=128)
    # Crescent recess on the glacis between hatch_06 and the left headlight: one step darker.
    take("@root", [65681], "glacis_recess", height_steps=1)
    # Cables on the rear ramp: near-black.
    take("@root", [40629, 22095, 22113, 22100, 22101], "ramp_cables", role="recess", mode="flat", top=25)
    # Skirt grab handles: grips keep their colour, mounting feet darker and flat.
    for name in [g["group"] for g in model["groups"] if re.fullmatch(r"ex_armor_[lr]_0\d", g["group"])]:
        group = groups[name]
        taken = {f for p in parts if p["source_group"] == group["id"] and p["id"] != group["id"] for f in p.get("faces", [])}
        for comp in common.components(model, [f for f in group["faces"] if f not in taken], 1e-6):
            if len(comp) < 200:
                continue
            co = vertices[[v for f in comp for v in model["faces"][f]["vertices"]]]
            plate = np.abs(co[:, 0]).min()
            feet = [f for f in comp if abs(surface(f)[1][0])-plate < .025]
            grip = [f for f in comp if f not in set(feet)]
            # Thickened ~1 cm all round: sub-pixel loops otherwise vanish at 256px.
            extract(group, f"handle_feet_{min(comp)}", feet, role="paint", mode="flat", fixed=128, inflate=.01)
            extract(group, f"handle_grip_{min(comp)}", grip, inflate=.01)
    # Right-side engine louvre above the skirts: one mid value instead of height bands.
    take("@root", [493], "side_louvre", role="paint", mode="flat", fixed=179)

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
                                  "later task. Optics and headlights are destructible overlays (no isolated ammunition store, so no ammunition lid). "
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
