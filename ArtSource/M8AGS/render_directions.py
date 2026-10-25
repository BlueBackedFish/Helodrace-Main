"""Render the three remaining cardinals from the preserved common-pipeline BLEND.

Run with Blender --background --python render_directions.py. No model is edited or saved.
"""
from pathlib import Path
import json
import math
import bpy
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "output"
report = json.loads((OUT / "report.json").read_text(encoding="utf-8"))
bpy.ops.wm.open_mainfile(filepath=str(OUT / "vehicle.blend"))
scene = bpy.context.scene
camera = scene.camera
meshes = [o for o in scene.objects if o.type == "MESH" and "layer" in o]
center = Vector(report["render"]["hull_target"])
pivot = Vector(report["render"]["turret_pivot"])
distance = report["render"]["ortho_scale"] * 4
target = OUT / "CardinalMasters"
target.mkdir(exist_ok=True)
metadata = dict(source_model_sha256=report["model_sha256"],
                master=report["render"]["master"], ortho_scale=camera.data.ortho_scale,
                note="Original source geometry and fixed emission materials; no model resave or baked outlines",
                directions={})
angles = dict(east=(180, -90), north=(90, 180), west=(0, 90), south=(270, 0))
for direction, (azimuth, top_rotation) in angles.items():
    if direction == "east":
        metadata["directions"][direction] = dict(
            hull_turret_anchor=report["render"]["turret_anchor_pixels_top_origin"],
            turret_pivot=[report["render"]["master"] / 2] * 2,
            source_prefix="../", azimuth=azimuth)
        continue
    for layer in ("hull", "turret", "barrel", "turret_full"):
        allowed = {"turret", "barrel"} if layer == "turret_full" else {layer}
        for obj in meshes:
            obj.hide_render = obj["layer"] not in allowed
        if layer == "hull":
            elevation = math.radians(report["render"]["hull_elevation"])
            az = math.radians(azimuth)
            camera.location = center + Vector((math.cos(elevation) * math.cos(az),
                math.cos(elevation) * math.sin(az), math.sin(elevation))) * distance
            camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        else:
            camera.location = pivot + Vector((0, 0, distance))
            camera.rotation_euler = (0, 0, math.radians(top_rotation))
        bpy.context.view_layer.update()
        if layer == "hull":
            anchor = world_to_camera_view(scene, camera, pivot)
            metadata["directions"][direction] = dict(
                hull_turret_anchor=[anchor.x * scene.render.resolution_x,
                                    (1 - anchor.y) * scene.render.resolution_y],
                turret_pivot=[scene.render.resolution_x / 2] * 2,
                source_prefix="", azimuth=azimuth)
        scene.render.filepath = str(target / f"{layer}_{direction}.png")
        bpy.ops.render.render(write_still=True)
(target / "pivots.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
print("M8 Level III cardinal masters rendered without changing the source BLEND")
