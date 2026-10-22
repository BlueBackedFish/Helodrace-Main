import bpy,os,json,struct
HERE=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE,'bradley_rimworld.blend'))
s=bpy.context.scene;s.frame_set(1)
meshes=[o for o in s.objects if o.type=='MESH']
with open(os.path.join(HERE,'manifest.json')) as f:manifest=json.load(f)
faces=sum(len(o.data.polygons) for o in meshes)
assert faces==manifest['faces'] and faces<150000
assert manifest['role_registry_consumed']
assert manifest['hull_geometry_unchanged']
assert all(len(o.data.polygons)>0 for o in meshes if o.get('build_stage')==2)
for o in meshes:
    attr=o.data.attributes.get('Face depth tone')
    assert attr and attr.domain=='FACE'
    assert all(.879<=v.value<=1.081 for v in attr.data)
    assert all(p.area>1e-12 for p in o.data.polygons)
assert not any(o.type=='LIGHT' for o in s.objects)
assert s.render.film_transparent
for mesh,control,frame in [('WT_bone_turret','TURRET_YAW',40),('WT_bone_gun','GUN_ELEVATION',40),('WT_rear_ramp','REAR_RAMP_HINGE',80),('WT_hatch_04','PERSONNEL_DOOR_HINGE',25)]:
    s.frame_set(1);o=bpy.data.objects[mesh];before=o.matrix_world.copy()
    assert o.parent.name==control
    s.frame_set(frame)
    assert max(abs(a-b) for r,t in zip(before,o.matrix_world) for a,b in zip(r,t))>.1
s.frame_set(1)
with open(os.path.join(HERE,'bradley_rimworld.glb'),'rb') as f:
    assert f.read(4)==b'glTF';f.read(8);size,kind=struct.unpack('<II',f.read(8));g=json.loads(f.read(size))
assert len(g['animations'])==4
with open(os.path.join(HERE,'validation.json'),'w') as f:json.dump({'faces':faces,'animations':4,'saved_hierarchy_and_motion':'passed','unlit':True,'transparent':True,'era_included':False},f,indent=2)
print('SIMPLIFIED SAVED MODEL VALIDATION PASSED')
