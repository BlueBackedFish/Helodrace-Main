"""Preserved Bradley variant + corner-fitted grilles, flat ramp rope and separate lamps."""
from pathlib import Path
import hashlib
import json
import sys
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT.parent / "VehiclePipeline"))
from shape_grilles import bind, prepare_export
from mesh_regions import regions
from height_palette import final_paint, display

cfg = json.loads((HERE / "settings.json").read_text())
variant = sys.argv[sys.argv.index("--") + 1]
spec = cfg["sources"][variant]
source = (HERE / spec["path"]).resolve()
assert hashlib.sha256(source.read_bytes()).hexdigest() == spec["sha256"]
bpy.ops.wm.open_mainfile(filepath=str(source))
scene = bpy.context.scene
scene.frame_set(1)
bpy.context.view_layer.update()
meshes = [o for o in scene.objects if o.type == "MESH"]
paint = json.loads((ROOT / "part_contrast.json").read_text())["height_first_paint"]


def geometry(obj):
    return repr(([tuple(v.co) for v in obj.data.vertices], [tuple(p.vertices) for p in obj.data.polygons],
                 [tuple(row) for row in obj.matrix_world],
                 [[tuple(x.uv) for x in uv.data] for uv in obj.data.uv_layers])).encode()


before = {o.name: dict(geometry=geometry(o), uv_count=len(o.data.uv_layers),
          colors=[o.data.materials[p.material_index].name for p in o.data.polygons]) for o in meshes}
grilles = []
allowed = {}
for item in cfg["grilles"]:
    obj = bpy.data.objects[item["object"]]
    colors = []
    for value in item["values"]:
        rgb, _, _ = final_paint(Vector((0, 0, 1)), "hull_assembly", 1, paint, final_value=value)
        colors.append([display(c) for c in rgb])
    rec = bind(obj, item["faces"], item, colors, "Bradley " + item["name"])
    rec.update(object=obj.name, faces=item["faces"])
    grilles.append(rec)
    allowed.setdefault(obj.name, set()).update(item["faces"])

flat_recolors = []
for item in cfg.get("flat_recolors", []):
    # Reassign listed faces to an existing fixed-Emission palette material.
    target = bpy.data.materials[item["to"]]
    for name, faces in item["faces"].items():
        obj = bpy.data.objects[name]
        if target.name not in obj.data.materials:
            obj.data.materials.append(target)
        slot = list(obj.data.materials).index(target)
        for i in faces:
            polygon = obj.data.polygons[i]
            assert obj.data.materials[polygon.material_index].name == item["from"], (name, i)
            polygon.material_index = slot
        allowed.setdefault(name, set()).update(faces)
    flat_recolors.append(dict(name=item["name"], role=item["role"], to=item["to"],
                              faces=sum(len(f) for f in item["faces"].values())))

rope = cfg["ramp_rope"]
obj = bpy.data.objects[rope["object"]]
faces = next(fs for fs in regions(obj.data) if rope["component_seed"] in fs)
assert len(faces) == rope["expected_faces"]
rgb, _, _ = final_paint(Vector((0, 0, 1)), "hull_assembly", 1, paint, final_value=rope["luminance"])
mat = bpy.data.materials.new("Rear ramp rope | single fixed color")
mat.use_nodes = True
mat.diffuse_color = (*rgb, 1)
nodes = mat.node_tree.nodes
nodes.clear()
emission = nodes.new("ShaderNodeEmission")
emission.inputs["Color"].default_value = (*rgb, 1)
output = nodes.new("ShaderNodeOutputMaterial")
mat.node_tree.links.new(emission.outputs[0], output.inputs["Surface"])
obj.data.materials.append(mat)
for i in faces:
    obj.data.polygons[i].material_index = len(obj.data.materials)-1
allowed[obj.name] = set(faces)

# Lamps belong to the vehicle, not the removable armor-case inventory. Their
# raised BUSK supports remain fixed kit geometry when a lamp is destroyed.
collection = bpy.data.collections.new("Independent vehicle headlights")
scene.collection.children.link(collection)
for key, name in cfg["headlights"].items():
    obj = bpy.data.objects[name]
    world = obj.matrix_world.copy()
    parent = bpy.data.objects.new(key, None)
    scene.collection.objects.link(parent)
    parent.parent = bpy.data.objects["HULL"]
    parent.matrix_world = world.copy()
    obj.parent = parent
    obj.matrix_world = world
    obj["vehicle_component"] = key
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    collection.objects.link(obj)
bpy.context.view_layer.update()
for obj in meshes:
    # Extra, dedicated UV maps are allowed; original coordinates/UVs stay exact.
    new_uvs = list(obj.data.uv_layers)[before[obj.name]["uv_count"]:]
    extra = [(uv.name, [tuple(x.uv) for x in uv.data]) for uv in new_uvs]
    for uv in new_uvs:
        obj.data.uv_layers.remove(uv)
    assert geometry(obj) == before[obj.name]["geometry"], ("Geometry or original UV changed", obj.name)
    for name, data in extra:
        uv = obj.data.uv_layers.new(name=name)
        for loop, value in zip(uv.data, data):
            loop.uv = value
    for polygon, original in zip(obj.data.polygons, before[obj.name]["colors"]):
        if polygon.index not in allowed.get(obj.name, set()):
            assert obj.data.materials[polygon.material_index].name == original, ("Unrelated paint changed", obj.name, polygon.index)
assert not any(o.type == "LIGHT" for o in scene.objects)
out = HERE / "output" / variant
out.mkdir(parents=True, exist_ok=True)
model = out / "vehicle.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(model), compress=True)
for material in {m.name:m for o in meshes for m in o.data.materials if m}.values():
    if not material.use_nodes or prepare_export(material):
        continue
    output = next((n for n in material.node_tree.nodes if n.type == "OUTPUT_MATERIAL"), None)
    emission = next((n for n in material.node_tree.nodes if n.type == "EMISSION"), None)
    if output and emission:
        if emission.inputs["Color"].links:
            socket = emission.inputs["Color"].links[0].from_socket
        else:
            node = material.node_tree.nodes.new("ShaderNodeRGB")
            node.outputs[0].default_value = emission.inputs["Color"].default_value
            socket = node.outputs[0]
        material.node_tree.links.new(socket, output.inputs["Surface"])
bpy.ops.export_scene.gltf(filepath=str(out / "vehicle.glb"), export_format="GLB", export_cameras=False,
                          export_lights=False, export_animations=True)
report = dict(variant=variant, source=str(source), source_sha256=spec["sha256"],
    model_sha256=hashlib.sha256(model.read_bytes()).hexdigest(), source_geometry_original_uv_and_other_paint_unchanged=True,
    grilles=grilles, flat_recolors=flat_recolors, rope=dict(object=rope["object"], faces=faces, uniform_emission_rgb=list(rgb)),
    headlights=cfg["headlights"], lamp_brackets_remain_fixed=True,
    independent_armor_cases=sum(bool(o.get("removable_tile")) for o in meshes))
(out / "validation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
assert hashlib.sha256(source.read_bytes()).hexdigest() == spec["sha256"]
print("BRADLEY_SURFACE_PASS", variant)
