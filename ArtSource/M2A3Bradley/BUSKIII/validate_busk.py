"""Validate the cosmetic kit and preserved source before delivery (Blender)."""
from pathlib import Path
from collections import Counter
import hashlib, json, struct, sys, math
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from check_cases import overlaps

OUT = HERE/'output'
manifest = json.loads((OUT/'manifest.json').read_text())
source = Path(manifest['source'])
assert hashlib.sha256(source.read_bytes()).hexdigest() == manifest['source_sha256']


def snapshot(obj):
    return {
        'geometry': hashlib.sha256(repr((
            [tuple(v.co) for v in obj.data.vertices],
            [tuple(p.vertices) for p in obj.data.polygons],
            [tuple(obj.data.materials[p.material_index].diffuse_color) for p in obj.data.polygons]
        )).encode()).hexdigest(),
        'parent': obj.parent.name if obj.parent else None,
        'matrix': obj.matrix_world.copy(),
    }


def glb(path):
    data = path.read_bytes()
    magic, version, total = struct.unpack_from('<III', data)
    assert magic == 0x46546c67 and version == 2 and total == len(data), path
    length, kind = struct.unpack_from('<II', data, 12)
    assert kind == 0x4e4f534a
    return json.loads(data[20:20+length])


bpy.ops.wm.open_mainfile(filepath=str(source))
bpy.context.scene.frame_set(1)
bpy.context.view_layer.update()
base = {o.name:snapshot(o) for o in bpy.context.scene.objects if o.type == 'MESH'}
base_actions = {o.name:o.animation_data.action.name for o in bpy.context.scene.objects
                if o.animation_data and o.animation_data.action}
bpy.ops.wm.open_mainfile(filepath=str(OUT/'bradley_busk_iii.blend'))
scene = bpy.context.scene
scene.frame_set(1)
bpy.context.view_layer.update()
changes = {c['object']:c for c in manifest['base_transforms_unchanged_except']}
assert len(base) == manifest['base_meshes']
for name, old in base.items():
    now = snapshot(bpy.data.objects[name])
    assert now['geometry'] == old['geometry'] and now['parent'] == old['parent'], name
    expected = old['matrix'].copy()
    if name in changes:
        expected.translation.z += changes[name]['world_raise_z']
    assert max(abs(now['matrix'][i][j]-expected[i][j]) for i in range(4) for j in range(4)) < 1e-5, name
for name, action in base_actions.items():
    assert bpy.data.objects[name].animation_data.action.name == action
assert not any(o.type == 'LIGHT' for o in scene.objects)

records = manifest['objects']
counts = Counter(r['family'] for r in records)
assert counts == Counter(M3=18,M4=8,M5=55,M6=7,M7=8,LOWER_SKIRT=28)
assert len({bpy.data.objects[r['name']].data.as_pointer() for r in records}) == 124
assert not overlaps(records), overlaps(records)
assert len([o for o in scene.objects if o.get('removable_tile')]) == 124
for record in records:
    obj = bpy.data.objects[record['name']]
    assert obj.parent.name == record['parent']
    assert (obj.matrix_world.translation-Vector(record['case_center'])).length < 1e-5
    assert all(p.area > 1e-9 for p in obj.data.polygons), obj.name
    assert len(obj.data.polygons) == (24 if record['family'] == 'LOWER_SKIRT' else 18), obj.name
    assert all(-.006 <= mount['gap'] <= .85 for mount in record['mounts'])
    # All kit materials are constant Emission, independent of light or view.
    for mat in obj.data.materials:
        nodes = mat.node_tree.nodes
        e = next(n for n in nodes if n.type == 'EMISSION')
        assert not e.inputs['Color'].is_linked and e.inputs['Strength'].default_value == 1
    part = glb(OUT/record['independent_file'])
    assert len(part['meshes']) == 1 and not part.get('animations'), record['name']
    assert all(Vector(n.get('translation',[0,0,0])).length < 1e-6 for n in part['nodes'] if 'mesh' in n)
    assert all('KHR_materials_unlit' in m.get('extensions', {}) for m in part['materials'])

# Check that the kit obeys the existing yaw pivot and leaves hull cases fixed.
yaw = bpy.data.objects['TURRET_YAW']
old_rotation = yaw.rotation_euler.copy()
old_action = yaw.animation_data.action if yaw.animation_data else None
if yaw.animation_data:
    yaw.animation_data.action = None
before = {r['name']:bpy.data.objects[r['name']].matrix_world.translation.copy() for r in records}
yaw.rotation_euler.z += math.radians(35)
bpy.context.view_layer.update()
moved = 0
for r in records:
    point = bpy.data.objects[r['name']].matrix_world.translation
    distance = (point-before[r['name']]).length
    if r['parent'] == 'BUSK_TURRET_KIT':
        assert distance > .05, r['name']
        moved += 1
    else:
        assert distance < 1e-6, r['name']
yaw.rotation_euler = old_rotation
if yaw.animation_data:
    yaw.animation_data.action = old_action
bpy.context.view_layer.update()
assert moved == 15
full = glb(OUT/'bradley_busk_iii.glb')
assert len(full.get('animations', [])) == 5
assert full.get('images') and full.get('textures'), 'Packed grille texture missing'
assert all('KHR_materials_unlit' in m.get('extensions', {}) for m in full['materials'])
result = {
    'status':'exterior draft; dimensioned real-vehicle blueprint unavailable',
    'source_hash_unchanged':True, 'base_meshes':len(base), 'base_faces':manifest['base_faces'],
    'base_geometry_color_parent_unchanged':True, 'base_transform_exceptions':list(changes),
    'independent_case_meshes_and_glbs':len(records), 'counts':dict(counts),
    'case_body_collision_tolerance_m':.002, 'case_body_collisions':[],
    'mount_reach_checked':True, 'turret_cases_follow_yaw':moved, 'hull_cases_remain_fixed':109,
    'preserved_action_bindings':base_actions, 'glb_animation_clips':len(full['animations']),
    'glb_all_materials_unlit':True, 'glb_packed_grille_texture':True,
    'engineering_dimensions_verified':False, 'game_validation_performed':False,
    'target_busk_kit_inventory_verified':False,
    'limitations':['No full animation sweep collision test', 'No certified family-to-location mapping',
                   'Lower skirt divisions are replica exterior observations, not XM33 inventory'],
}
(OUT/'validation.json').write_text(json.dumps(result, indent=2)+'\n')
print('BUSK VALIDATION PASSED: source preserved, 124 independent case meshes/GLBs, no case-body overlaps, yaw parenting, 5 clips, unlit grille')
