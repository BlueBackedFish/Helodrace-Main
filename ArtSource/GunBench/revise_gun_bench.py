"""Edit the existing scene in place, then export all three sprite views.

blender --background --python revise_gun_bench.py
This script is idempotent; it never deletes or rebuilds the existing model.
"""
import bpy
import os
import math
import json
from mathutils import Vector, Matrix

OUT = os.path.dirname(os.path.abspath(__file__))
PATH = os.path.join(OUT, 'gun_bench.blend')
bpy.ops.wm.open_mainfile(filepath=PATH)
scene = bpy.context.scene

# Reference-family colors are specified in display sRGB, then converted to
# linear emission values so Standard view transform reproduces those colors.
def linear(c):
    c /= 255
    return c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4

colors = {'SteelGray': (132, 131, 127), 'WorktopGray': (99, 103, 102),
          'Charcoal': (63, 65, 64), 'DarkCyan': (37, 76, 86)}
for mat in bpy.data.materials:
    for family, rgb in colors.items():
        if mat.name.startswith(family + '_'):
            shade = 1 if '_Top' in mat.name else (.86 if '_Front' in mat.name else .73)
            color = tuple(linear(c * shade) for c in rgb) + (1,)
            mat.diffuse_color = color
            for node in mat.node_tree.nodes:
                if node.type == 'EMISSION':
                    node.inputs['Color'].default_value = color

def warm_material():
    mat = bpy.data.materials.get('WarmToolGray') or bpy.data.materials.new('WarmToolGray')
    mat.use_nodes = True
    mat.node_tree.nodes.clear()
    node = mat.node_tree.nodes.new('ShaderNodeEmission')
    mat.diffuse_color = tuple(linear(c) for c in (91, 85, 77)) + (1,)
    node.inputs['Color'].default_value = mat.diffuse_color
    output = mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
    mat.node_tree.links.new(node.outputs[0], output.inputs['Surface'])
    return mat

warm = warm_material()
if not scene.get('reference_alignment_v1'):
    # Reshape the original upright vertices, preserving their bottom anchors.
    obj = bpy.data.objects['05_ShelfFrame']
    for vertex in obj.data.vertices:
        world = obj.matrix_world @ vertex.co
        world.z = .905 + (world.z - .905) * (.59 / .97)
        vertex.co = obj.matrix_world.inverted() @ world
    for name in ['06_Shelf', '10_Magazines', '11_BarrelParts']:
        bpy.data.objects[name].location.z -= .38

    # The same four rifle blocks get a slight diagonal for overhead readability.
    rifle = bpy.data.objects['08_Rifle']
    pivot = Vector((.39, -.145, 1.34))
    rotation = Matrix.Rotation(math.radians(18), 4, 'Z')
    for vertex in rifle.data.vertices:
        world = rifle.matrix_world @ vertex.co
        vertex.co = rifle.matrix_world.inverted() @ (pivot + rotation @ (world - pivot))

    # Broad warm-gray stock and hammer handle echo the reference work surfaces.
    for name, first_face, last_face in [('08_Rifle', 0, 6), ('09_Hammer', 0, 6)]:
        obj = bpy.data.objects[name]
        obj.data.materials.append(warm)
        for face in list(obj.data.polygons)[first_face:last_face]:
            face.material_index = len(obj.data.materials) - 1
    scene['reference_alignment_v1'] = True

# Keep the hammer head clear of the shelf in the steeper North projection.
if not scene.get('hammer_visibility_v1'):
    hammer = bpy.data.objects['09_Hammer']
    pivot = Vector((-.90, -.12, .99))
    rotation = Matrix.Rotation(math.radians(65), 4, 'Z')
    for vertex in hammer.data.vertices:
        world = hammer.matrix_world @ vertex.co
        world = pivot + rotation @ (world - pivot) + Vector((0, -.13, 0))
        vertex.co = hammer.matrix_world.inverted() @ world
    scene['hammer_visibility_v1'] = True

# Shared target, projection scale, and exact 64-degree downward camera angle.
target = Vector((0, 0, .85))
elevation = math.radians(64)
for name, azimuth in [('North', 90), ('South', 270), ('East', 0)]:
    camera = bpy.data.objects['Camera_' + name]
    a = math.radians(azimuth)
    camera.location = target + 8 * Vector((math.cos(a)*math.cos(elevation),
                                          math.sin(a)*math.cos(elevation), math.sin(elevation)))
    camera.rotation_euler = (target-camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 3.65
    camera['downward_angle_degrees'] = 64
    camera['target_center'] = list(target)

scene.render.resolution_x = scene.render.resolution_y = 512
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
for name in ['North', 'South', 'East']:
    scene.camera = bpy.data.objects['Camera_' + name]
    scene.render.filepath = os.path.join(OUT, 'gun_bench_' + name.lower() + '.png')
    bpy.ops.render.render(write_still=True)

scene.camera = bpy.data.objects['Camera_South']
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=PATH)
meshes = [o for o in scene.objects if o.type == 'MESH']
assert len(meshes) == 11
assert sum(len(o.data.vertices) for o in meshes) == 208
with open(os.path.join(OUT, 'asset_manifest.json'), 'w') as f:
    json.dump({'blender_version': bpy.app.version_string, 'mesh_components': len(meshes),
               'vertices': sum(len(o.data.vertices) for o in meshes),
               'footprint': [3, 1], 'elevation_degrees': 64, 'ortho_scale': 3.65,
               'resolution': [512, 512], 'camera_convention': 'North +Y, South -Y, East +X',
               'target_center': list(target), 'reference_alignment': True,
               'palette_srgb': colors}, f, indent=2)
