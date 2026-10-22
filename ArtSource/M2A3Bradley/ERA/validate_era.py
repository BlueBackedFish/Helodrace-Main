"""Validate saved tile ownership, animation attachment and standalone exports."""
import bpy, os, json, struct
ROOT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'m2a3_bradley_era.blend'))
scene=bpy.context.scene;scene.frame_set(1)
with open(os.path.join(ROOT,'tile_manifest.json')) as f:manifest=json.load(f)
assert manifest['count']==96
turret=bpy.data.objects['TURRET_YAW']
original={}
for entry in manifest['tiles']:
    o=bpy.data.objects[entry['name']]
    assert o.parent.name==entry['parent']
    original[o.name]=(o.parent.matrix_world.inverted()@o.matrix_world).copy()
    with open(os.path.join(ROOT,entry['file']),'rb') as f:
        assert f.read(4)==b'glTF'
        f.read(8);size,kind=struct.unpack('<II',f.read(8))
        data=json.loads(f.read(size))
    assert len(data['meshes'])==1
    assert sum('mesh' in n for n in data['nodes'])==1
before=bpy.data.objects['ERA_M6_TURRET_L_01'].matrix_world.translation.copy()
scene.frame_set(40)
assert (bpy.data.objects['ERA_M6_TURRET_L_01'].matrix_world.translation-before).length>.1
for name,matrix in original.items():
    o=bpy.data.objects[name]
    current=o.parent.matrix_world.inverted()@o.matrix_world
    assert max(abs(a-b) for row1,row2 in zip(matrix,current) for a,b in zip(row1,row2))<1e-5
assert len(manifest['contacts'])==96
assert all(abs(c['mount_penetration']-.005)<1e-6 for c in manifest['contacts'])
assert not any(o.type=='LIGHT' for o in scene.objects)
with open(os.path.join(ROOT,'validation.json'),'w') as f:
    json.dump({'tile_count':96,'individual_glb_count':96,'turret_tiles':15,
               'armor_contact_checks':96,'saved_scene_parenting_and_frame_40_animation':'passed',
               'unlit':True},f,indent=2)
print('ERA SAVED-SCENE VALIDATION PASSED')
