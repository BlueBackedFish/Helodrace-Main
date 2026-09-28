"""Rebuild with: blender --background --python build_gun_bench.py"""
import bpy
import math
import os
import json
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

# Flat emission colors, with deliberately broad face-value separation.
def palette(name, color):
    result = []
    for suffix, factor in [('Top', 1.0), ('Front', .72), ('Side', .52)]:
        mat = bpy.data.materials.new(name + '_' + suffix)
        mat.diffuse_color = (*[c * factor for c in color], 1)
        mat.use_nodes = True
        nodes = mat.node_tree.nodes
        nodes.clear()
        emission = nodes.new('ShaderNodeEmission')
        emission.inputs['Color'].default_value = mat.diffuse_color
        output = nodes.new('ShaderNodeOutputMaterial')
        mat.node_tree.links.new(emission.outputs[0], output.inputs['Surface'])
        result.append(mat)
    return result

gray = palette('SteelGray', (.43, .48, .50))
light = palette('WorktopGray', (.66, .70, .71))
dark = palette('Charcoal', (.12, .16, .18))
cyan = palette('DarkCyan', (.035, .30, .34))

groups = {}
def box(name, center, size, mats, group, angle=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for mat in mats:
        obj.data.materials.append(mat)
    for face in obj.data.polygons:
        face.material_index = 0 if face.normal.z > .5 else (1 if abs(face.normal.y) > .5 else 2)
    obj.rotation_euler.z = angle
    groups.setdefault(group, []).append(obj)
    return obj

# 3 x 1 tile bench; +Y is the raised rear shelf.
box('Thick_work_surface', (0, 0, .87), (3, 1, .16), light, '01_Worktop')
box('Left_drawer_cabinet', (-1.03, 0, .42), (.70, .82, .76), gray, '02_DrawerCabinet')
for i in range(3):
    box('Cyan_drawer_%d' % (i+1), (-1.03, -.423, .24+i*.22), (.58, .045, .17), cyan, '03_DrawerFronts')
box('Right_slab_leg', (1.20, 0, .42), (.20, .85, .76), gray, '04_Frame')
box('Low_cross_brace', (.20, .30, .22), (1.96, .14, .18), dark, '04_Frame')
for x in [-1.30, 1.30]:
    box('Shelf_upright', (x, .38, 1.39), (.13, .13, .97), dark, '05_ShelfFrame')
box('Raised_rear_shelf', (0, .37, 1.86), (2.83, .30, .12), gray, '06_Shelf')
box('Shelf_cyan_edge', (0, .205, 1.86), (2.83, .035, .085), cyan, '06_Shelf')

# Chunky vise with a clear jaw gap and one oversized screw lever.
box('Vise_base', (.39, -.14, 1.00), (.57, .45, .10), dark, '07_Vise')
for y in [-.29, -.015]:
    box('Vise_jaw', (.39, y, 1.15), (.34, .13, .26), cyan, '07_Vise')
box('Vise_screw', (.39, -.43, 1.10), (.09, .23, .09), gray, '07_Vise')
box('Vise_cross_handle', (.39, -.53, 1.10), (.30, .06, .06), light, '07_Vise')

# Stylized rifle supported by the vise: broad stock, receiver, barrel, grip.
box('Rifle_stock', (-.22, -.145, 1.31), (.48, .19, .16), gray, '08_Rifle')
box('Rifle_receiver', (.24, -.145, 1.34), (.48, .13, .13), dark, '08_Rifle')
box('Rifle_barrel', (.83, -.145, 1.36), (.70, .065, .065), dark, '08_Rifle')
box('Rifle_grip', (.09, -.145, 1.23), (.10, .10, .18), dark, '08_Rifle')

# A large hammer, two magazines, and a pair of spare barrels only.
box('Hammer_handle', (-.90, -.12, .99), (.10, .48, .07), dark, '09_Hammer', -.35)
box('Hammer_head', (-.965, .075, 1.025), (.33, .14, .12), gray, '09_Hammer', -.35)
for x in [-.55, -.23]:
    box('Spare_magazine', (x, .36, 1.97), (.19, .20, .11), dark, '10_Magazines')
for x in [.58, .83]:
    box('Spare_barrel', (x, .37, 1.975), (.09, .24, .11), dark, '11_BarrelParts')

# Join logical components without adding vertices or hidden detail.
for name, objects in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    if len(objects) > 1:
        bpy.ops.object.join()
    bpy.context.object.name = name

# Cardinal viewpoints describe camera position: North is +Y, East is +X.
scene = bpy.context.scene
target = Vector((0, 0, .85))
distance = 8
elevation = math.radians(26)
for name, azimuth in [('North', 90), ('South', 270), ('East', 0)]:
    a = math.radians(azimuth)
    offset = Vector((math.cos(a)*math.cos(elevation), math.sin(a)*math.cos(elevation), math.sin(elevation))) * distance
    data = bpy.data.cameras.new('Camera_' + name)
    data.type = 'ORTHO'
    data.ortho_scale = 3.65
    obj = bpy.data.objects.new('Camera_' + name, data)
    scene.collection.objects.link(obj)
    obj.location = target + offset
    obj.rotation_euler = (target-obj.location).to_track_quat('-Z', 'Y').to_euler()
    obj['downward_angle_degrees'] = 26
    obj['target_center'] = list(target)

# Identical transparent render configuration; emission requires no lights.
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 512
scene.render.resolution_y = 512
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.film_transparent = True
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.view_settings.exposure = 0
scene.view_settings.gamma = 1
scene.world.color = (0, 0, 0)

for name in ['North', 'South', 'East']:
    scene.camera = bpy.data.objects['Camera_' + name]
    scene.render.filepath = os.path.join(OUT, 'gun_bench_' + name.lower() + '.png')
    bpy.ops.render.render(write_still=True)
scene.camera = bpy.data.objects['Camera_South']
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'gun_bench.blend'))
meshes = [o for o in scene.objects if o.type == 'MESH']
with open(os.path.join(OUT, 'asset_manifest.json'), 'w') as f:
    json.dump({'blender_version': bpy.app.version_string, 'mesh_components': len(meshes),
               'vertices': sum(len(o.data.vertices) for o in meshes),
               'footprint': [3, 1], 'elevation_degrees': 26, 'ortho_scale': 3.65,
               'resolution': [512, 512], 'camera_convention': 'North +Y, South -Y, East +X',
               'target_center': list(target)}, f, indent=2)

# Apply the reference-alignment pass to keep a full rebuild consistent with
# the current edited scene. Ordinary edits use revise_gun_bench.py directly.
exec(compile(open(os.path.join(OUT, 'revise_gun_bench.py'), encoding='utf-8').read(),
             os.path.join(OUT, 'revise_gun_bench.py'), 'exec'))
