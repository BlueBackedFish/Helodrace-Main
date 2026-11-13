"""Blender adapter. All source-specific selections live in vehicle.json."""
from pathlib import Path
from collections import Counter
import json
import math
import struct
import sys

import bpy
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT))
from pipeline import (read_json, write_json, sha256, read_obj, inventory, partition,
                      transform, paint_levels, paint_rgb, linear, contact_direction)


def validate_rigs(profile):
    parts = {p["id"]: p for p in profile["parts"]}
    rigs, owners = {}, {}
    for rig in profile["rigs"]:
        if rig["id"] in rigs or rig["id"] in parts:
            raise ValueError("Rig IDs must be unique and distinct from part IDs")
        rigs[rig["id"]] = rig
        if len(rig["pivot"]) != 3 or not all(math.isfinite(v) for v in rig["pivot"]):
            raise ValueError("Rig pivot must be a finite canonical-space point")
        if rig.get("axis", "z") not in {"x", "y", "z"}:
            raise ValueError("Rig axis must be x / y / z")
        for part in rig["parts"]:
            if part not in parts or part in owners:
                raise ValueError("Unknown part or two rigs directly own the same part")
            owners[part] = rig["id"]
    for key in rigs:
        seen, current = set(), key
        while current is not None:
            if current in seen or current not in rigs:
                raise ValueError("Cyclic or missing rig parent")
            seen.add(current)
            current = rigs[current].get("parent")
    return rigs, owners


def value_for(normal, part, top, style):
    if "accent" in part:
        # Highlight exception: one bright value, optional darker side for its walls.
        accent = part["accent"]
        return accent["value"] if normal.z >= .5 else accent.get("side", accent["value"])
    special = style["roles"][part["role"]]
    mode = part["mode"]
    if mode == "cylinder":
        axis = Vector(part.get("cylinder_axis", [0, 0, 1]))
        if axis.length == 0:
            raise ValueError("Cylinder axis must be nonzero")
        axis.normalize()
        # A single side color across the entire circumference. End colors are optional.
        return part.get("cap", top) if abs(normal.dot(axis)) > .9 else part.get("side", top)
    if special is not None or mode == "flat":
        return top
    if mode == "two_tone":
        return top if normal.z >= .5 else part.get("side", top-24)
    if mode != "armor":
        raise ValueError(f"Unknown paint mode: {mode}")
    planes = style["planes"]
    if normal.z >= .78:
        key = "roof"
    elif normal.z < -.25:
        key = "underside"
    else:
        side = abs(normal.x) > abs(normal.y)
        key = ("side_" if side else "front_") + ("slope" if normal.z > .25 else "wall")
    # Per-part plane overrides are absolute display luminances, never world-height gradients.
    return part.get("planes", {}).get(key, planes[key] + top-planes["roof"])


def height_values(mesh, part, style):
    """Height-band paint (style `height_paint`): one value per near-planar surface.

    Connected faces whose normals stay within `plane_angle` of the region's first face form
    one surface; its area-weighted centre height picks one of the layer's ladder steps
    (higher = brighter). Walls and undersides sit `wall_steps` lower. Thin wall strips keep
    their surface's step so plate edges do not draw seams. Returns one value per polygon.

    Per-part exceptions: `height_steps` shifts every surface (negative = brighter),
    `slope_steps` adds to chamfers/slopes (normal z between the wall limit and .9),
    `plane_angle` overrides the grouping angle, `fixed` paints one value regardless of height.
    """
    from collections import defaultdict, deque
    spec = style["height_paint"]
    layer = spec["layers"][part["layer"]]
    ladder = layer.get("ladder", spec["ladder"])  # a layer may shift its own steps
    bands = layer["thresholds"]  # descending heights, len(ladder)-1
    if not all(style["minimum"] <= v <= style["maximum"] for v in ladder):
        raise ValueError(f"height_paint ladder for {part['layer']} is outside the palette range")
    if len(bands) != len(ladder)-1 or any(a <= b for a, b in zip(bands, bands[1:])):
        raise ValueError(f"height_paint thresholds for {part['layer']} must descend, one fewer than ladder steps")
    limit = math.cos(math.radians(part.get("plane_angle", spec.get("plane_angle", 12))))
    if "fixed" in part:
        return [part["fixed"]]*len(mesh.polygons)
    shift, slope_steps = part.get("height_steps", 0), part.get("slope_steps", 0)
    wall_z, wall_steps = spec.get("wall_normal_z", .25), spec.get("wall_steps", 1)
    min_wall = style.get("min_wall_height", .03)
    key = lambda co: tuple(round(c, 5) for c in co)
    edge_faces = defaultdict(list)
    for poly in mesh.polygons:
        ids = list(poly.vertices)
        for a, b in zip(ids, ids[1:]+ids[:1]):
            edge_faces[tuple(sorted((key(mesh.vertices[a].co), key(mesh.vertices[b].co))))].append(poly.index)
    neighbours = defaultdict(set)
    for faces in edge_faces.values():
        for f in faces:
            neighbours[f].update(g for g in faces if g != f)
    region = [-1]*len(mesh.polygons)
    values = [0]*len(mesh.polygons)
    for seed in mesh.polygons:
        if region[seed.index] >= 0:
            continue
        members, queue = [seed.index], deque([seed.index])
        region[seed.index] = seed.index
        while queue:
            for g in neighbours[queue.popleft()]:
                if region[g] < 0 and mesh.polygons[g].normal.dot(seed.normal) >= limit:
                    region[g] = seed.index
                    members.append(g)
                    queue.append(g)
        area = sum(mesh.polygons[f].area for f in members) or 1e-12
        z = sum(mesh.polygons[f].center.z*mesh.polygons[f].area for f in members)/area
        normal = sum((mesh.polygons[f].normal*mesh.polygons[f].area for f in members), Vector()).normalized()
        step = sum(1 for t in bands if z < t)+shift
        if wall_z <= normal.z < .9:
            step += slope_steps
        if normal.z < wall_z:
            zs = [mesh.vertices[v].co.z for f in members for v in mesh.polygons[f].vertices]
            if max(zs)-min(zs) >= min_wall:
                step += wall_steps
        value = ladder[max(0, min(step, len(ladder)-1))]
        for f in members:
            values[f] = value
    return values


def simplify_mesh(obj, part, style):
    """Opt-in polygon reduction (style `simplify`, per-part override or false).

    Welds split source vertices, collapse-decimates parts above `min_faces` to `collapse_ratio`,
    then merges near-coplanar faces (`planar_angle`) so flat armour is one polygon per plate.
    Patterned grille/grid patches are never reduced. Returns a report dict, or None if untouched.
    """
    import bmesh
    settings = style.get("simplify")
    if settings is None or part.get("simplify") is False or "grille" in part or "grid" in part:
        return None
    settings = {**settings, **(part.get("simplify") or {})}
    before = len(obj.data.polygons)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=settings.get("weld", 1e-5))
    bm.to_mesh(obj.data)
    bm.free()
    ratio = settings.get("collapse_ratio", 1)
    if ratio < 1 and before >= settings.get("min_faces", 200):
        modifier = obj.modifiers.new("Simplify", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = ratio
        modifier.use_collapse_triangulate = False
        depsgraph = bpy.context.evaluated_depsgraph_get()
        reduced = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
        obj.modifiers.remove(modifier)
        old = obj.data
        obj.data = reduced
        reduced.name = old.name
        bpy.data.meshes.remove(old)
    angle = settings.get("planar_angle", 0)
    if angle > 0:
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(angle), verts=bm.verts[:], edges=bm.edges[:])
        bm.to_mesh(obj.data)
        bm.free()
    obj.data.update()
    after = len(obj.data.polygons)
    return {"source_faces": before, "faces": after, "collapse_ratio": ratio if before >= settings.get("min_faces", 200) else 1,
            "planar_angle": angle}


def material(rgb, cache):
    key = tuple(round(c, 4) for c in rgb)
    if key in cache:
        return cache[key]
    mat = bpy.data.materials.new("Fixed " + ",".join(str(round(c)) for c in rgb))
    mat.use_nodes = True
    mat.diffuse_color = (*[linear(c) for c in rgb], 1)
    nodes = mat.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = mat.diffuse_color
    emission.inputs["Strength"].default_value = 1
    mat.node_tree.links.new(emission.outputs[0], output.inputs["Surface"])
    cache[key] = mat
    return mat


def display_value(linear_rgb):
    rgb = [255*(c*12.92 if c <= .0031308 else 1.055*c**(1/2.4)-.055) for c in linear_rgb[:3]]
    return .2126*rgb[0]+.7152*rgb[1]+.0722*rgb[2]


def repaint_imported(spec, objs, style, record):
    """Move an imported asset's paint materials onto this vehicle's palette tint.

    Only materials whose names start with one of `materials_prefix` are touched (the asset's
    own paint roles); metal, glass and other fixed-colour materials keep their colour. Each paint
    value keeps its ratio to the reference material, scaled so the reference becomes `to`.
    """
    settings = spec["repaint"]
    prefixes = tuple(settings["materials_prefix"])
    reference = bpy.data.materials[settings["reference"]]
    base = display_value(next(n for n in reference.node_tree.nodes if n.type == "EMISSION").inputs["Color"].default_value)
    factor = settings["to"]/base
    cache = {}
    for obj in objs:
        for slot in obj.material_slots:
            mat = slot.material
            if mat is None or not mat.name.startswith(prefixes):
                continue
            if mat.name not in cache:
                emission = next(n for n in mat.node_tree.nodes if n.type == "EMISSION")
                old = display_value(emission.inputs["Color"].default_value)
                value = min(style["maximum"], max(style["minimum"], old*factor))
                new = mat.copy()
                new.name = f"{spec['id']} repaint {mat.name}"
                rgb = paint_rgb(value, "paint", style)
                next(n for n in new.node_tree.nodes if n.type == "EMISSION").inputs["Color"].default_value = (*[linear(c) for c in rgb], 1)
                new.diffuse_color = (*[linear(c) for c in rgb], 1)
                cache[mat.name] = new
                record[mat.name] = {"from": round(old, 1), "to": round(value, 1)}
            slot.material = cache[mat.name]


def apply_smooth(obj, part, style):
    """Fixed per-corner gradient for large curved surfaces (style section 4).

    Normals are merged across coincident positions (hard edges above `crease` stay hard);
    value = low..high by how much the smoothed normal faces up. World-fixed, never view-dependent.
    Returns the per-face mean display value for reports and contact checks.
    """
    import math
    from collections import defaultdict
    spec = part["smooth"]
    low, high = spec["low"], spec["high"]
    if not (style["minimum"] <= low < high <= style["maximum"]):
        raise ValueError(f"Smooth range must be inside the palette and low < high: {part['id']}")
    crease = math.cos(math.radians(spec.get("crease", 60)))
    mesh = obj.data
    key = lambda co: tuple(round(c, 5) for c in co)
    by_position = defaultdict(list)
    for poly in mesh.polygons:
        for v in poly.vertices:
            by_position[key(mesh.vertices[v].co)].append(poly)
    attribute = mesh.color_attributes.new("Smooth paint", "FLOAT_COLOR", "CORNER")
    face_values = []
    for poly in mesh.polygons:
        values = []
        for loop in poly.loop_indices:
            co = mesh.vertices[mesh.loops[loop].vertex_index].co
            normal = Vector((0, 0, 0))
            for other in by_position[key(co)]:
                if other.normal.dot(poly.normal) >= crease:
                    normal += other.normal*other.area
            normal = normal.normalized() if normal.length else poly.normal
            # signed: whole -1..1 range (wheels/tyres seen below a skirt); default: up-facing only.
            value = low+(high-low)*((normal.z+1)/2 if spec.get("signed") else max(0.0, normal.z))
            values.append(value)
            rgb = paint_rgb(value, part["role"], style)
            attribute.data[loop].color = (*[linear(c) for c in rgb], 1)
        face_values.append(sum(values)/len(values))
    mat = bpy.data.materials.new(part["id"]+" smooth fixed gradient")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    output, emission = nodes.new("ShaderNodeOutputMaterial"), nodes.new("ShaderNodeEmission")
    color = nodes.new("ShaderNodeVertexColor")
    color.layer_name = "Smooth paint"
    mat.node_tree.links.new(color.outputs["Color"], emission.inputs["Color"])
    mat.node_tree.links.new(emission.outputs[0], output.inputs["Surface"])
    mesh.materials.clear()
    mesh.materials.append(mat)
    for poly in mesh.polygons:
        poly.material_index = 0
    return face_values


def add_grid(obj, part, style, ortho_scale):
    """Packed, fixed emission square tile; cell size is expressed in final 256px pixels."""
    settings = part["grid"]
    if not obj.data.polygons:
        return
    normal = sum((p.normal*p.area for p in obj.data.polygons), Vector()).normalized()
    if any(p.normal.dot(normal) < .995 for p in obj.data.polygons):
        raise ValueError(f"Grid {part['id']} is not one plane; extract separate planar patches first")
    u = Vector((1, 0, 0)) if abs(normal.x) < .9 else Vector((0, 1, 0))
    u = (u-normal*u.dot(normal)).normalized()
    v = normal.cross(u).normalized()
    size = min(style["render"]["sizes"])
    pitch = ortho_scale/size*settings.get("cell_pixels", 6)
    fraction = settings.get("bar_fraction", .3)
    if pitch <= 0 or not 0 < fraction < 1:
        raise ValueError("Grid pitch / bar_fraction must be positive and meaningful")
    uv = obj.data.uv_layers.new(name="Square grid")
    for loop in obj.data.loops:
        co = obj.data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = (co.dot(u)/pitch, co.dot(v)/pitch)
    values = settings.get("values", [84, 120])
    image = bpy.data.images.new(part["id"]+" square grid", 64, 64)
    # Byte images use sRGB; write display RGB so image sampling decodes it once.
    image.colorspace_settings.name = "sRGB"
    display_colors = [[c/255 for c in paint_rgb(y, "paint", style)] + [1] for y in values]
    pixels = []
    for y in range(64):
        for x in range(64):
            bar = ((x+.5)/64 < fraction) or ((y+.5)/64 < fraction)
            pixels.extend(display_colors[int(bar)])
    image.pixels.foreach_set(pixels)
    image.pack()
    mat = bpy.data.materials.new(part["id"]+" fixed grid")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = image
    texture.extension = "REPEAT"
    mapping = nodes.new("ShaderNodeUVMap")
    mapping.uv_map = uv.name
    mat.node_tree.links.new(mapping.outputs["UV"], texture.inputs["Vector"])
    mat.node_tree.links.new(texture.outputs["Color"], emission.inputs["Color"])
    mat.node_tree.links.new(emission.outputs[0], output.inputs["Surface"])
    mat["grid"] = True
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for poly in obj.data.polygons:
        poly.material_index = 0
    return {"pitch_world": pitch, "cell_pixels_at_smallest_output": settings.get("cell_pixels", 6),
            "bar_fraction": fraction, "projection_note": "Planar pitch; foreshortening still needs native-size review"}


def build(profile_path):
    (profile_path.parent/"output/report.json").unlink(missing_ok=True)
    profile = read_json(profile_path)
    root = profile_path.parent
    style_path = root/profile["style"]
    style = read_json(style_path)
    settings = style["render"]
    if not (isinstance(settings["master"], int) and settings["master"] >= 64 and
            settings["sizes"] and all(isinstance(s, int) and 16 <= s <= settings["master"] for s in settings["sizes"]) and
            0 < settings["padding"] < .45 and 0 < settings["hull_elevation"] < 90 and
            settings["turret_elevation"] == 90):
        raise ValueError("Invalid render settings; hull elevation 0..90, turret 90, positive sizes and padding required")
    for source in profile["sources"].values():
        if sha256(source["path"]) != source["sha256"]:
            raise ValueError("Source hash mismatch; face IDs may have changed")
    source = read_obj(profile["sources"]["obj"]["path"])
    source_points = [transform(co, profile["axes"]) for co in source["vertices"]]
    catalog = inventory(source, profile["axes"])
    selection = partition(source, profile)
    levels = paint_levels(profile, style)
    rigs, owners = validate_rigs(profile)
    weapon_layers = {w["id"]: w for w in profile.get("weapon_layers", [])}
    if weapon_layers.keys() & {"hull", "turret", "barrel"}:
        raise ValueError("Weapon layer ids must differ from hull / turret / barrel")
    if any(p["layer"] not in {"hull", "turret", "barrel", *weapon_layers} for p in profile["parts"]):
        raise ValueError("Main layers are hull / turret / barrel (+ declared weapon_layers); movable subparts use rigs")
    output_dir = root/"output"
    output_dir.mkdir(exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.render.film_transparent = True
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    # Blender dithers 8-bit output by default; that leaves +-1 noise on every flat face.
    scene.render.dither_intensity = 0
    scene.render.resolution_x = scene.render.resolution_y = style["render"]["master"]
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new("No lighting")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0
    objects, cache, paint_report = {}, {}, {}
    omitted, replaced, boxes, simplified = [], [], [], []
    for part in profile["parts"]:
        face_ids = selection[part["id"]]
        if part.get("action") == "omit":
            omitted += face_ids
            continue
        if not face_ids:
            continue
        source_indices = sorted({v for i in face_ids for v in source["faces"][i]["vertices"]})
        mapping = {v: i for i, v in enumerate(source_indices)}
        mesh = bpy.data.meshes.new(part["id"])
        boxed = part.get("action") == "box"
        if boxed:
            # Recorded simplification: one clean axis-aligned block over the source faces.
            co = [source_points[i] for i in source_indices]
            lo = [min(c[k] for c in co) for k in range(3)]
            hi = [max(c[k] for c in co) for k in range(3)]
            corners = [(x, y, z) for z in (lo[2], hi[2]) for y in (lo[1], hi[1]) for x in (lo[0], hi[0])]
            mesh.from_pydata(corners, [], [[0, 2, 3, 1], [4, 5, 7, 6], [0, 1, 5, 4],
                                           [1, 3, 7, 5], [3, 2, 6, 7], [2, 0, 4, 6]])
            replaced += face_ids
            boxes.append({"part": part["id"], "source_faces": len(face_ids), "bounds": [lo, hi],
                          "reason": part["reason"]})
        else:
            mesh.from_pydata([source_points[i] for i in source_indices], [],
                             [[mapping[v] for v in source["faces"][i]["vertices"]] for i in face_ids])
        mesh.update()
        obj = bpy.data.objects.new(part["id"]+" "+part["label"], mesh)
        scene.collection.objects.link(obj)
        obj["part_id"] = part["id"]
        obj["layer"] = part["layer"]
        reduced = None if boxed else simplify_mesh(obj, part, style)
        if reduced:
            mesh = obj.data
            replaced += face_ids
            simplified.append({"part": part["id"], **reduced})
        source_id = mesh.attributes.new(name="source_face_id", type="INT", domain="FACE")
        source_id.data.foreach_set("value", [-1]*len(mesh.polygons) if boxed or reduced else face_ids)
        original_uv = mesh.uv_layers.new(name="Source UV")
        for poly, face_id in zip(mesh.polygons, [] if boxed or reduced else face_ids):
            face = source["faces"][face_id]
            for loop_id, uv_id in zip(poly.loop_indices, face["uv"]):
                original_uv.data[loop_id].uv = source["uv"][uv_id] if uv_id is not None else (0, 0)
        slots, distribution, upward, walls = {}, Counter(), [], []
        min_wall = style.get("min_wall_height", .03)
        by_height = None
        if "height_paint" in style:
            if "accent" in part or "smooth" in part:
                raise ValueError(f"height_paint replaces accents and smooth gradients; remove them: {part['id']}")
            if part["role"] in style["height_paint"].get("roles", ["paint", "hatch", "equipment"]):
                by_height = height_values(mesh, part, style)
        for poly in mesh.polygons:
            value = value_for(poly.normal, part, levels[part["id"]], style)
            if by_height is not None:
                value = by_height[poly.index]
            # Sub-pixel strips (plate edges, bevels) take the part's top colour instead of
            # drawing hairline seams across flat armour (style section 4).
            elif -.25 <= poly.normal.z < .78 and "accent" not in part and part["mode"] in {"armor", "two_tone"}:
                zs = [(obj.matrix_world @ mesh.vertices[v].co).z for v in poly.vertices]
                if max(zs)-min(zs) < min_wall:
                    value = levels[part["id"]] if part["mode"] == "two_tone" else value_for(Vector((0, 0, 1)), part, levels[part["id"]], style)
            if not style["minimum"] <= value <= style["maximum"]:
                raise ValueError(f"Paint outside range: {part['id']} = {value}; fix its planes")
            rgb = tuple(part["rgb"]) if "rgb" in part else paint_rgb(value, part["role"], style)
            mat = material(rgb, cache)
            if mat.name not in slots:
                slots[mat.name] = len(mesh.materials)
                mesh.materials.append(mat)
            poly.material_index = slots[mat.name]
            distribution[value] += 1
            if poly.normal.z >= .78:
                upward.append(value)
            elif poly.normal.z >= -.25:
                walls.append(value)
        if "smooth" in part:
            if "accent" in part or "grille" in part or "grid" in part:
                raise ValueError(f"Smooth gradient cannot combine with accent/grille: {part['id']}")
            face_values = apply_smooth(obj, part, style)
            distribution, upward, walls = Counter(round(v) for v in face_values), [], []
            for poly, value in zip(mesh.polygons, face_values):
                if poly.normal.z >= .78:
                    upward.append(value)
                elif poly.normal.z >= -.25:
                    walls.append(value)
        objects[part["id"]] = obj
        paint_report[part["id"]] = {"top": levels[part["id"]], "luminances": dict(distribution),
                                   "smooth": part.get("smooth"),
                                   "area": sum(poly.area for poly in mesh.polygons),
                                   "upward_min_max": [min(upward), max(upward)] if upward else None,
                                   "wall_min_max": [min(walls), max(walls)] if walls else None,
                                   "source_faces": len(face_ids)}
    contact_checks = []
    for edge in profile["contacts"]:
        if any("grid" in p for p in profile["parts"] if p["id"] in {edge["lower"], edge["upper"]}):
            raise ValueError("Patterned grid parts are not TAN contact nodes; select their solid support separately")
        lower = paint_report.get(edge["lower"], {}).get("upward_min_max")
        upper = paint_report.get(edge["upper"], {}).get("upward_min_max")
        if not lower or not upper:
            raise ValueError("Contact needs visible upward faces on both retained parts; inspect its face selection")
        step = style["contact_step"]-.001
        direction = contact_direction(edge)
        check = {**edge, "direction": direction}
        if direction == "up":
            check["actual_upward_gap"] = upper[0]-lower[1]
        elif direction == "down":
            check["actual_downward_gap"] = lower[0]-upper[1]
        if direction != "same":
            gap = check.get("actual_upward_gap", check.get("actual_downward_gap"))
            if gap < step:
                raise ValueError(f"Actual upward paint reverses/weakens contact {edge}: {gap}; fix overrides")
        else:
            # Equal tops: the mounted part's whole visible wall/rim must sit a full step
            # below the base top, otherwise the boundary disappears.
            wall = paint_report[edge["upper"]]["wall_min_max"]
            if not wall:
                raise ValueError(f"'same' contact needs visible wall/rim faces on {edge['upper']}")
            check["actual_wall_gap"] = lower[0]-wall[1]
            if check["actual_wall_gap"] < step:
                raise ValueError(f"'same' contact wall is not a step darker than its base {edge}: "
                                 f"{check['actual_wall_gap']}; darken the mounted part's walls")
        contact_checks.append(check)
    accents = []
    total_area = sum(r["area"] for r in paint_report.values())
    for part in profile["parts"]:
        if "accent" not in part or part["id"] not in paint_report:
            continue
        base = paint_report.get(part["accent"]["over"], {}).get("upward_min_max")
        top = paint_report[part["id"]]["upward_min_max"] or [part["accent"]["value"]]*2
        if not base:
            raise ValueError(f"Accent base needs visible upward faces: {part['accent']}")
        gap = top[0]-base[1]
        if gap < style["contact_step"]-.001:
            raise ValueError(f"Accent {part['id']} is not a full step above its base: {gap}")
        accents.append({"part": part["id"], "over": part["accent"]["over"], "value": part["accent"]["value"],
                        "gap_over_base": gap, "area": paint_report[part["id"]]["area"], "reason": part["accent"]["reason"]})
    accent_fraction = sum(a["area"] for a in accents)/max(total_area, 1e-12)
    if accent_fraction > style.get("accent_max_area_fraction", .03):
        raise ValueError(f"Accents cover {accent_fraction:.3f} of the surface; highlights must stay small")
    if not objects:
        raise ValueError("Every source part was omitted")
    empties = {}
    for key, rig in rigs.items():
        empty = bpy.data.objects.new(key, None)
        scene.collection.objects.link(empty)
        empty.location = rig["pivot"]
        empty["pivot_confirmed"] = rig["confirmed"]
        empty["axis"] = rig.get("axis", "z")
        empties[key] = empty
    for key, rig in rigs.items():
        if rig.get("parent"):
            # Update before reading matrix_world; fresh empties may still have identity transforms.
            bpy.context.view_layer.update()
            world = empties[key].matrix_world.copy()
            empties[key].parent = empties[rig["parent"]]
            empties[key].matrix_world = world
    bpy.context.view_layer.update()
    for key, owner in owners.items():
        if key in objects:
            world = objects[key].matrix_world.copy()
            objects[key].parent = empties[owner]
            objects[key].matrix_world = world
    bpy.context.view_layer.update()
    for key, rig in rigs.items():
        if rig.get("animate"):
            if not rig["confirmed"]:
                raise ValueError("Animation requires a confirmed pivot")
            obj = empties[key]
            index = "xyz".index(rig.get("axis", "z"))
            for frame, angle in [(1, 0), (40, rig.get("angle", 60)), (80, 0)]:
                obj.rotation_euler[index] = math.radians(angle)
                obj.keyframe_insert(data_path="rotation_euler", frame=frame)
    scene.frame_set(1)
    bpy.context.view_layer.update()
    rest_matrices = {key: obj.matrix_world.copy() for key, obj in objects.items()}
    scene.frame_set(40)
    bpy.context.view_layer.update()
    for key, rig in rigs.items():
        if rig.get("animate"):
            actual = math.degrees(empties[key].rotation_euler["xyz".index(rig.get("axis", "z"))])
            if abs(actual-rig.get("angle", 60)) > .001:
                raise AssertionError("Rig action did not reach its configured angle")
    scene.frame_set(80)
    bpy.context.view_layer.update()
    for key, obj in objects.items():
        if any(abs(a-b) > 1e-5 for row_a, row_b in zip(obj.matrix_world, rest_matrices[key]) for a, b in zip(row_a, row_b)):
            raise AssertionError("Rig did not return to the source rest pose")
    scene.frame_set(1)
    # Existing separately built assets (e.g. a remote weapon station) placed into this vehicle:
    # appended unchanged (geometry, UVs, fixed-emission paint), one uniform scale + yaw + move.
    from mathutils import Matrix
    imported, repainted = [], {}
    for spec in profile.get("imported_models", []):
        if spec["layer"] not in weapon_layers and spec["layer"] not in {"hull", "turret", "barrel"}:
            raise ValueError(f"Imported model layer must be a main or weapon layer: {spec['id']}")
        blend = (root/spec["blend"]).resolve() if not Path(spec["blend"]).is_absolute() else Path(spec["blend"])
        if "sha256" in spec and sha256(blend) != spec["sha256"]:
            raise ValueError(f"Imported model changed; re-check its scale and placement: {blend}")
        with bpy.data.libraries.load(str(blend), link=False) as (source_data, target_data):
            target_data.objects = list(source_data.objects)
        place = (Matrix.Translation(Vector(spec["location"])) @ Matrix.Rotation(math.radians(spec.get("rotation_z_degrees", 0)), 4, "Z")
                 @ Matrix.Scale(spec["scale"], 4))
        for obj in target_data.objects:
            if obj is None or obj.type != "MESH":
                continue
            world = obj.matrix_world.copy()
            obj.parent = None
            scene.collection.objects.link(obj)
            obj.matrix_world = place @ world
            obj.animation_data_clear()
            key = f"import:{spec['id']}:{obj.name}"
            obj.data.name = key
            obj["part_id"], obj["layer"] = key, spec["layer"]
            objects[key] = obj
            imported.append({"model": spec["id"], "object": obj.name, "faces": len(obj.data.polygons)})
        if "repaint" in spec:
            repaint_imported(spec, [o for k, o in objects.items() if k.startswith(f"import:{spec['id']}:")], style, repainted)
    bpy.context.view_layer.update()
    all_points = [obj.matrix_world @ v.co for obj in objects.values() for v in obj.data.vertices]
    center = (Vector([min(p[i] for p in all_points) for i in range(3)]) +
              Vector([max(p[i] for p in all_points) for i in range(3)]))/2
    span = max(max(p[i] for p in all_points)-min(p[i] for p in all_points) for i in range(3))
    distance = max(span*4, 1e-6)
    camera_data = bpy.data.cameras.new("Orthographic")
    camera_data.type = "ORTHO"
    camera_data.clip_start = distance*.0001
    camera_data.clip_end = distance*10
    camera = bpy.data.objects.new("Orthographic", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera

    def aim(target, elevation, azimuth=180):
        e, a = math.radians(elevation), math.radians(azimuth)
        camera.location = Vector(target)+Vector((math.cos(e)*math.cos(a), math.cos(e)*math.sin(a), math.sin(e)))*distance
        camera.rotation_euler = (Vector(target)-camera.location).to_track_quat("-Z", "Y").to_euler()
        if elevation == 90:
            camera.location = Vector(target)+Vector((0, 0, distance))
            camera.rotation_euler = (0, 0, -math.pi/2)
        bpy.context.view_layer.update()

    # One common scale across source views and every layer. Fit both 64 and 90 degree views,
    # including off-center parts relative to the turret pivot.
    turret_rigs = [r for r in rigs.values() if r.get("usage") == "turret_yaw"]
    if len(turret_rigs) > 1:
        raise ValueError("Only one main turret_yaw rig is supported")
    pivot = Vector(turret_rigs[0]["pivot"]) if turret_rigs else center.copy()
    extent = 0
    for elevation, azimuth, target in [(style["render"]["hull_elevation"], 180, center), (90, 180, center),
                                        (0, 180, center), (0, 270, center), (30, 225, center),
                                        (30, 45, center), (90, 180, pivot)]:
        aim(target, elevation, azimuth)
        inverse = camera.matrix_world.inverted()
        for point in all_points:
            co = inverse @ point
            extent = max(extent, abs(co.x), abs(co.y))
    camera_data.ortho_scale = 2*extent/(1-2*style["render"]["padding"])
    grids = {}
    for part in profile["parts"]:
        if "grid" in part and part["id"] in objects:
            grids[part["id"]] = add_grid(objects[part["id"]], part, style, camera_data.ortho_scale)
        elif "grille" in part and part["id"] in objects:
            from shape_grilles import bind
            obj = objects[part["id"]]
            colors = [paint_rgb(y, "paint", style) for y in part["grille"].get("values", [64, 112, 144])]
            grids[part["id"]] = bind(obj, list(range(len(obj.data.polygons))), part["grille"], colors, part["id"])

    # Part-ID + camera-depth pass for the boundary-line postprocess (style section 4).
    id_names = {}
    for index, (key, obj) in enumerate(objects.items(), start=1):
        obj.color = ((index % 256)/255, (index//256)/255, 1, 1)
        id_names[index] = key
    depth_range = 8*camera_data.ortho_scale
    id_material = bpy.data.materials.new("Part ID + depth pass")
    id_material.use_nodes = True
    nodes = id_material.node_tree.nodes
    nodes.clear()
    info, camera_node = nodes.new("ShaderNodeObjectInfo"), nodes.new("ShaderNodeCameraData")
    split, combine = nodes.new("ShaderNodeSeparateColor"), nodes.new("ShaderNodeCombineColor")
    scale = nodes.new("ShaderNodeMath")
    scale.operation = "DIVIDE"
    scale.inputs[1].default_value = depth_range
    emit, out = nodes.new("ShaderNodeEmission"), nodes.new("ShaderNodeOutputMaterial")
    links = id_material.node_tree.links
    links.new(info.outputs["Color"], split.inputs["Color"])
    links.new(split.outputs["Red"], combine.inputs["Red"])
    links.new(split.outputs["Green"], combine.inputs["Green"])
    links.new(camera_node.outputs["View Z Depth"], scale.inputs[0])
    links.new(scale.outputs[0], combine.inputs["Blue"])
    links.new(combine.outputs["Color"], emit.inputs["Color"])
    links.new(emit.outputs[0], out.inputs["Surface"])

    def render_ids(name):
        settings = (scene.cycles.samples, scene.cycles.pixel_filter_type, scene.cycles.filter_width,
                    scene.view_settings.view_transform, scene.render.image_settings.color_depth,
                    scene.render.resolution_percentage)
        scene.cycles.samples, scene.cycles.pixel_filter_type, scene.cycles.filter_width = 1, "BOX", .01
        # Supersampled so boundary lines are anti-aliased even at the 1024px master.
        scene.render.resolution_percentage = 100*style.get("boundary_lines", {}).get("supersample", 4)
        scene.view_settings.view_transform = "Raw"
        scene.render.image_settings.color_depth = "16"
        bpy.context.view_layer.material_override = id_material
        png = output_dir/(name+"_ids.png")
        scene.render.filepath = str(png)
        bpy.ops.render.render(write_still=True)
        bpy.context.view_layer.material_override = None
        # External Pillow reads 16-bit RGBA as 8-bit; decode here and store exact arrays.
        import numpy as np
        image = bpy.data.images.load(str(png))
        image.colorspace_settings.name = "Non-Color"
        pixels = np.array(image.pixels[:], dtype=np.float64).reshape(image.size[1], image.size[0], 4)[::-1]
        bpy.data.images.remove(image)
        png.unlink()
        ident = np.round(pixels[..., 0]*255).astype(np.int32)+256*np.round(pixels[..., 1]*255).astype(np.int32)
        ident[pixels[..., 3] < .5] = 0
        np.savez_compressed(output_dir/(name+"_ids.npz"), ident=ident.astype(np.uint16),
                            depth=(pixels[..., 2]*depth_range).astype(np.float32))
        (scene.cycles.samples, scene.cycles.pixel_filter_type, scene.cycles.filter_width,
         scene.view_settings.view_transform, scene.render.image_settings.color_depth,
         scene.render.resolution_percentage) = settings

    # Destructible overlays: parts that can be removed or recoloured at runtime (optics, lids...).
    damage_specs = {d["id"]: d for d in profile.get("damage_layers", [])}
    damage_owner = {}
    for key, spec in damage_specs.items():
        for part_id in spec["parts"]:
            if part_id not in objects:
                raise ValueError(f"Damage layer {key} names a missing/omitted part: {part_id}")
            if objects[part_id]["layer"] != spec["layer"]:
                raise ValueError(f"Damage layer {key}: {part_id} is not on layer {spec['layer']}")
            if part_id in damage_owner:
                raise ValueError(f"Part {part_id} belongs to two damage layers")
            damage_owner[part_id] = key
    damage_views = {}

    def render(name, elevation, azimuth=180, layers=None, target=None, ids=False):
        visible = [o for o in objects.values() if layers is None or o["layer"] in layers]
        overlays = sorted({damage_owner[o["part_id"]] for o in visible if o["part_id"] in damage_owner}) if ids else []
        for obj in objects.values():
            obj.hide_render = obj not in visible or (ids and obj["part_id"] in damage_owner)
        aim(center if target is None else target, elevation, azimuth)
        scene.render.filepath = str(output_dir/(name+".png"))
        for stale in output_dir.glob(name+"_*"):
            if stale.name.startswith((name+"_flat", name+"_lines", name+"_ids", name+"__")):
                stale.unlink()
        bpy.ops.render.render(write_still=True)
        if ids:
            # ID/depth pass sees the damage parts too, so lines ring them on the base.
            for obj in visible:
                obj.hide_render = False
            render_ids(name)
            for key in overlays:
                # Overlay alone, cut by everything in front of it (holdout), same camera.
                for obj in visible:
                    owner = damage_owner.get(obj["part_id"])
                    obj.hide_render = owner is not None and owner != key
                    obj.is_holdout = owner != key
                scene.render.filepath = str(output_dir/f"{name}__{key}.png")
                bpy.ops.render.render(write_still=True)
                for obj in visible:
                    obj.is_holdout = False
            damage_views[name] = overlays
            for obj in visible:
                obj.hide_render = obj["part_id"] in damage_owner

    for name, elevation, azimuth in [("source_top", 90, 180), ("source_side", 0, 180),
                                      ("source_front", 0, 270), ("iso_front", 30, 225),
                                      ("iso_rear", 30, 45)]:
        render(name, elevation, azimuth)
    render("source_east", style["render"]["hull_elevation"])
    light_data = bpy.data.lights.new("Invariance test", "SUN")
    light_data.energy = 100
    light = bpy.data.objects.new("Invariance test", light_data)
    scene.collection.objects.link(light)
    render("lighting_check", style["render"]["hull_elevation"])
    bpy.data.objects.remove(light, do_unlink=True)
    # Game cardinal hulls (Vehicle Framework draws north/east/south/west). East stays the
    # reference; azimuths follow the M8 cardinal renders (east 180, north 90, west 0, south 270).
    hull_points = [obj.matrix_world @ v.co for obj in objects.values() if obj["layer"] == "hull" for v in obj.data.vertices]
    hull_center = (Vector([min(p[i] for p in hull_points) for i in range(3)]) +
                   Vector([max(p[i] for p in hull_points) for i in range(3)]))/2
    azimuths = {"east": 180, "north": 90, "west": 0, "south": 270}
    hull_directions = {}

    def pixel(point):
        view = world_to_camera_view(scene, camera, point)
        return [view.x*scene.render.resolution_x, (1-view.y)*scene.render.resolution_y]

    for direction in ["east", *[d for d in style["render"].get("hull_directions", []) if d != "east"]]:
        render("hull_"+direction, style["render"]["hull_elevation"], azimuths[direction], layers={"hull"}, ids=True)
        hull_directions[direction] = {"azimuth": azimuths[direction], "hull_center_pixels": pixel(hull_center),
                                      "turret_anchor_pixels": pixel(pivot)}
    aim(center, style["render"]["hull_elevation"])
    anchor = world_to_camera_view(scene, camera, pivot)
    anchor_pixel = [anchor.x*scene.render.resolution_x, (1-anchor.y)*scene.render.resolution_y]
    if any(o["layer"] in {"turret", "barrel"} for o in objects.values()):
        render("turret_full_east", 90, layers={"turret", "barrel"}, target=pivot, ids=True)
        render("turret_east", 90, layers={"turret"}, target=pivot, ids=True)
        render("barrel_east", 90, layers={"barrel"}, target=pivot, ids=True)
    # Separate weapon sprites (RimThunder MG_turret style) and external-asset mount points:
    # pixel positions are measured in the turret image (camera still aimed at the turret pivot).
    placements = {}
    for key, spec in [*weapon_layers.items(), *[(m["id"], m) for m in profile.get("mounts", [])]]:
        point = world_to_camera_view(scene, camera, Vector(spec["pivot"]))
        placements[key] = {"pivot": spec["pivot"], "kind": "weapon_layer" if key in weapon_layers else "mount",
                           "pixels_in_turret_top_origin": [point.x*scene.render.resolution_x,
                                                           (1-point.y)*scene.render.resolution_y],
                           **({"asset": spec["asset"]} if "asset" in spec else {}),
                           **({"reason": spec["reason"]} if "reason" in spec else {})}
    for key, spec in weapon_layers.items():
        if not any(o["layer"] == key for o in objects.values()):
            raise ValueError(f"Weapon layer {key} has no retained parts")
        # Render the weapon aimed straight forward (-Y) so the game can rotate the sprite.
        from mathutils import Matrix
        yaw = math.radians(spec.get("render_yaw_degrees", 0))
        turn = (Matrix.Translation(Vector(spec["pivot"])) @ Matrix.Rotation(yaw, 4, "Z")
                @ Matrix.Translation(-Vector(spec["pivot"])))
        moved = {o: o.matrix_world.copy() for o in objects.values() if o["layer"] == key}
        for o, world in moved.items():
            o.matrix_world = turn @ world
        bpy.context.view_layer.update()
        render(key+"_east", 90, layers={key}, target=Vector(spec["pivot"]), ids=True)
        for o, world in moved.items():
            o.matrix_world = world
        bpy.context.view_layer.update()
    for obj in objects.values():
        obj.hide_render = False
    aim(center, style["render"]["hull_elevation"])
    # Validate preserved coordinates / polygons against the source with per-face provenance.
    seen, degenerate = set(), []
    boxed_parts = {b["part"] for b in boxes} | {s["part"] for s in simplified}
    for key, obj in objects.items():
        if key in boxed_parts or key.startswith("import:"):
            continue
        mesh = obj.data
        for poly, face_id in zip(mesh.polygons, selection[key]):
            if face_id in seen:
                raise AssertionError("Duplicated source face")
            seen.add(face_id)
            for local_vertex, original_vertex in zip(poly.vertices, source["faces"][face_id]["vertices"]):
                if (obj.matrix_world @ mesh.vertices[local_vertex].co - Vector(source_points[original_vertex])).length > 1e-5*max(1, camera_data.ortho_scale):
                    raise AssertionError(f"Source geometry moved: {key} / {face_id}")
            if poly.area < 1e-12*camera_data.ortho_scale**2:
                degenerate.append(face_id)
    if seen | set(omitted) | set(replaced) != set(range(len(source["faces"]))) or seen & set(omitted) or (seen | set(omitted)) & set(replaced):
        raise AssertionError("Source provenance coverage failed")
    if any(o.type == "LIGHT" for o in scene.objects):
        raise AssertionError("Unexpected scene light")
    bpy.ops.wm.save_as_mainfile(filepath=str(output_dir/"vehicle.blend"))
    # Blender glTF recognizes direct RGB/texture -> surface as KHR_materials_unlit.
    for mat in bpy.data.materials:
        if not mat.use_nodes:
            continue
        nodes = mat.node_tree.nodes
        emission = next((n for n in nodes if n.type == "EMISSION"), None)
        if emission is None:
            continue
        output = next(n for n in nodes if n.type == "OUTPUT_MATERIAL")
        incoming = emission.inputs["Color"].links
        if incoming:
            socket = incoming[0].from_socket
        else:
            rgb = nodes.new("ShaderNodeRGB")
            rgb.outputs[0].default_value = emission.inputs["Color"].default_value
            socket = rgb.outputs[0]
        mat.node_tree.links.new(socket, output.inputs["Surface"])
    bpy.ops.export_scene.gltf(filepath=str(output_dir/"vehicle.glb"), export_format="GLB",
                              export_cameras=False, export_lights=False, export_animations=True)
    glb = (output_dir/"vehicle.glb").read_bytes()
    chunk_length = struct.unpack_from("<I", glb, 12)[0]
    gltf = json.loads(glb[20:20+chunk_length])
    if not all("KHR_materials_unlit" in m.get("extensions", {}) for m in gltf["materials"]):
        raise AssertionError("GLB contains a lit material")
    smooth_meshes = {p["id"] for p in profile["parts"] if "smooth" in p and p["id"] in objects}
    smooth_meshes |= {key for key in objects if key.startswith("import:")}  # keep the asset's own paint
    if any("COLOR_0" in p["attributes"] for m in gltf["meshes"] if m.get("name") not in smooth_meshes
           for p in m["primitives"]):
        raise AssertionError("Unexpected vertex colors modulate the GLB palette")
    for key in grids:
        mat = next(m for m in gltf["materials"] if m["name"] == grids[key].get("material_name", key+" fixed grid"))
        pbr = mat["pbrMetallicRoughness"]
        if "baseColorTexture" not in pbr or pbr.get("baseColorFactor", [1, 1, 1, 1]) != [1, 1, 1, 1]:
            raise AssertionError("GLB lost or tinted its grid texture")
    animation_count = len(gltf.get("animations", []))
    if animation_count != sum(bool(r.get("animate")) for r in rigs.values()):
        raise AssertionError("GLB animation count differs from the configured rigs")
    blockers = []
    if "blueprint" not in profile["sources"]:
        blockers.append("blueprint not supplied; OBJ-only draft")
    if not profile["axes"]["confirmed"]:
        blockers.append("axes / scale")
    blockers.extend("part: "+p["id"] for p in profile["parts"] if not p["confirmed"])
    blockers.extend("rig: "+r["id"] for r in rigs.values() if not r["confirmed"])
    blockers.extend("review: "+k for k, v in profile["review"].items() if not v)
    if degenerate:
        blockers.append("source degenerate faces; inspect rather than silently remove")
    if any(o["layer"] in {"turret", "barrel"} for o in objects.values()) and not turret_rigs:
        blockers.append("turret_yaw pivot not supplied; preview uses provisional bounding-box center")
    report = {"status": "pending_postprocess", "postprocess_passed": False, "blockers": blockers,
              "sources": profile["sources"], "profile_sha256": sha256(profile_path),
              "style_sha256": sha256(style_path), "source_faces": len(source["faces"]),
              "preserved_faces": len(seen), "explicitly_omitted_faces": len(omitted),
              "box_replaced_faces": len(replaced), "boxes": boxes, "simplified_parts": simplified,
              "rendered_faces": sum(len(o.data.polygons) for o in objects.values()),
              "imported_models": {"specs": profile.get("imported_models", []), "objects": imported,
                                  "repainted_materials": repainted},
              "accents": accents, "accent_area_fraction": accent_fraction,
              "degenerate_source_faces": degenerate, "source_coordinates_preserved": True,
              "unlit_glb": True, "glb_animation_count": animation_count,
              "mesh_count": len(objects), "paint": paint_report, "contact_checks": contact_checks,
              "grids": grids, "rigs": profile["rigs"], "rig_demo_angles_and_rest_return": True,
              "game_verified": profile["review"]["game"],
              "render": {**style["render"], "ortho_scale": camera_data.ortho_scale,
                         "hull_target": list(center), "turret_pivot": list(pivot),
                         "turret_anchor_pixels_top_origin": anchor_pixel,
                         "placements": placements,
                         "damage_layers": {"specs": profile.get("damage_layers", []), "views": damage_views},
                         "hull_directions": hull_directions, "hull_center": list(hull_center),
                         "top_pivot_pixels": [scene.render.resolution_x/2]*2,
                         "note": "turret_full includes barrel; never composite barrel twice",
                         "id_pass": {"ids": {str(k): v for k, v in id_names.items()},
                                     "depth_range": depth_range, "encoding": "16-bit RGB: R,G = id low/high byte, B = view depth / depth_range"}}}
    write_json(root/"inventory.json", catalog)
    write_json(output_dir/"report.json", report)
    print("Vehicle build passed source preservation and unlit GLB checks")


if __name__ == "__main__":
    build(Path(sys.argv[sys.argv.index("--")+1]).resolve())
