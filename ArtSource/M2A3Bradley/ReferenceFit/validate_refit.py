"""Check saved OBJ reconstruction, geometry bounds and animation hierarchy."""
import bpy,os,json,struct
from mathutils import Vector
HERE=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE,'m3a3_bradley_refit.blend'))
scene=bpy.context.scene;scene.frame_set(1)
with open(os.path.join(HERE,'source_groups.json')) as f:groups=json.load(f)
meshes=[o for o in scene.objects if o.type=='MESH']
assert len(meshes)==112
assert sum(len(o.data.polygons) for o in meshes)==223389
for name,record in groups.items():
    o=bpy.data.objects['WT_'+name]
    assert o.parent.name==record['parent']
    points=[o.matrix_world@v.co for v in o.data.vertices]
    for axis in range(3):
        assert abs(min(p[axis] for p in points)-record['bounds'][0][axis])<1e-5
        assert abs(max(p[axis] for p in points)-record['bounds'][1][axis])<1e-5
assert bpy.data.objects['WT_hatch_04'].parent.parent==bpy.data.objects['REAR_RAMP_HINGE']
before={name:bpy.data.objects[name].matrix_world.copy() for name in ['WT_@root','WT_bone_turret','WT_rear_ramp','WT_hatch_04']}
def difference(a,b):return max(abs(x-y) for ra,rb in zip(a,b) for x,y in zip(ra,rb))
scene.frame_set(40)
assert difference(before['WT_@root'],bpy.data.objects['WT_@root'].matrix_world)<1e-6
assert difference(before['WT_bone_turret'],bpy.data.objects['WT_bone_turret'].matrix_world)>.1
scene.frame_set(25)
assert difference(before['WT_hatch_04'],bpy.data.objects['WT_hatch_04'].matrix_world)>.1
scene.frame_set(80)
assert difference(before['WT_rear_ramp'],bpy.data.objects['WT_rear_ramp'].matrix_world)>.1
with open(os.path.join(HERE,'m3a3_bradley_refit.glb'),'rb') as f:
    assert f.read(4)==b'glTF'
    f.read(8);size,kind=struct.unpack('<II',f.read(8));gltf=json.loads(f.read(size))
assert len(gltf['meshes'])==112 and len(gltf['animations'])==4
assert all(im.packed_file for im in bpy.data.images if im.source=='FILE' and 'm3a3' in im.name)
assert not any(o.type=='LIGHT' for o in scene.objects)
with open(os.path.join(HERE,'validation.json'),'w') as f:
    json.dump({'mesh_count':112,'source_faces_preserved':223389,'all_source_group_bounds':'passed',
               'turret_door_ramp_animation':'passed','glb_meshes':112,'glb_animations':4,
               'packed_textures':True,'unlit':True},f,indent=2)
print('SAVED REFIT VALIDATION PASSED')
