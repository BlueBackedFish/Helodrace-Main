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
    if any(p["layer"] not in {"hull", "turret", "barrel"} for p in profile["parts"]):
        raise ValueError("Main layers are hull / turret / barrel; movable subparts use rigs")
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
    omitted = []
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
        mesh.from_pydata([source_points[i] for i in source_indices], [],
                         [[mapping[v] for v in source["faces"][i]["vertices"]] for i in face_ids])
        mesh.update()
        obj = bpy.data.objects.new(part["id"]+" "+part["label"], mesh)
        scene.collection.objects.link(obj)
        obj["part_id"] = part["id"]
        obj["layer"] = part["layer"]
        source_id = mesh.attributes.new(name="source_face_id", type="INT", domain="FACE")
        source_id.data.foreach_set("value", face_ids)
        original_uv = mesh.uv_layers.new(name="Source UV")
        for poly, face_id in zip(mesh.polygons, face_ids):
            face = source["faces"][face_id]
            for loop_id, uv_id in zip(poly.loop_indices, face["uv"]):
                original_uv.data[loop_id].uv = source["uv"][uv_id] if uv_id is not None else (0, 0)
        slots, distribution, upward, walls = {}, Counter(), [], []
        for poly in mesh.polygons:
            value = value_for(poly.normal, part, levels[part["id"]], style)
            if not style["minimum"] <= value <= style["maximum"]:
                raise ValueError(f"Paint outside range: {part['id']} = {value}; fix its planes")
            rgb = paint_rgb(value, part["role"], style)
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
        objects[part["id"]] = obj
        paint_report[part["id"]] = {"top": levels[part["id"]], "luminances": dict(distribution),
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

    def render(name, elevation, azimuth=180, layers=None, target=None):
        for obj in objects.values():
            obj.hide_render = layers is not None and obj["layer"] not in layers
        aim(center if target is None else target, elevation, azimuth)
        scene.render.filepath = str(output_dir/(name+".png"))
        bpy.ops.render.render(write_still=True)

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
    render("hull_east", style["render"]["hull_elevation"], layers={"hull"})
    anchor = world_to_camera_view(scene, camera, pivot)
    anchor_pixel = [anchor.x*scene.render.resolution_x, (1-anchor.y)*scene.render.resolution_y]
    if any(o["layer"] in {"turret", "barrel"} for o in objects.values()):
        render("turret_full_east", 90, layers={"turret", "barrel"}, target=pivot)
        render("turret_east", 90, layers={"turret"}, target=pivot)
        render("barrel_east", 90, layers={"barrel"}, target=pivot)
    for obj in objects.values():
        obj.hide_render = False
    aim(center, style["render"]["hull_elevation"])
    # Validate preserved coordinates / polygons against the source with per-face provenance.
    seen, degenerate = set(), []
    for key, obj in objects.items():
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
    if seen | set(omitted) != set(range(len(source["faces"]))) or seen & set(omitted):
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
    if any("COLOR_0" in p["attributes"] for m in gltf["meshes"] for p in m["primitives"]):
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
              "degenerate_source_faces": degenerate, "source_coordinates_preserved": True,
              "unlit_glb": True, "glb_animation_count": animation_count,
              "mesh_count": len(objects), "paint": paint_report, "contact_checks": contact_checks,
              "grids": grids, "rigs": profile["rigs"], "rig_demo_angles_and_rest_return": True,
              "game_verified": profile["review"]["game"],
              "render": {**style["render"], "ortho_scale": camera_data.ortho_scale,
                         "hull_target": list(center), "turret_pivot": list(pivot),
                         "turret_anchor_pixels_top_origin": anchor_pixel,
                         "top_pivot_pixels": [scene.render.resolution_x/2]*2,
                         "note": "turret_full includes barrel; never composite barrel twice"}}
    write_json(root/"inventory.json", catalog)
    write_json(output_dir/"report.json", report)
    print("Vehicle build passed source preservation and unlit GLB checks")


if __name__ == "__main__":
    build(Path(sys.argv[sys.argv.index("--")+1]).resolve())
