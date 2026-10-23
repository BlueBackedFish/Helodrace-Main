"""Validate split provenance, local hinge motion, stable mounts and unlit exports."""
import bpy,json,hashlib,struct,math
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent
report=json.loads((HERE/'manifest.json').read_text());cfg=report['config']
assert hashlib.sha256((TRIAL/cfg['source']).read_bytes()).hexdigest()==report['source_sha256']
def snapshot():
    result={}
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        result[o.name]={'world':[list(row) for row in o.matrix_world],'parent':o.parent.name,
            'faces':[[tuple(o.matrix_world@o.data.vertices[v].co) for v in p.vertices] for p in o.data.polygons],
            'colors':[tuple(o.data.materials[p.material_index].diffuse_color) for p in o.data.polygons]}
    return result
def same_faces(a,b):
    return len(a)==len(b) and all(len(pa)==len(pb) and all((Vector(va)-Vector(vb)).length<2e-6 for va,vb in zip(pa,pb)) for pa,pb in zip(a,b))
bpy.ops.wm.open_mainfile(filepath=str(TRIAL/cfg['source']));bpy.context.scene.frame_set(1);before=snapshot()
bpy.ops.wm.open_mainfile(filepath=str(HERE/'bradley_tow_deploy.blend'));s=bpy.context.scene;s.frame_set(1);after=snapshot()
assert set(after)==set(before)|{cfg['fixed_bracket_object']}
for name,record in before.items():
    if name==cfg['split_source']:continue
    assert same_faces(after[name]['faces'],record['faces']),name
    assert after[name]['colors']==record['colors'],name
    if name not in cfg['moving_objects']:assert after[name]['parent']==record['parent'],name
for name,ids in report['split_face_provenance'].items():
    assert same_faces(after[name]['faces'],[before[cfg['split_source']]['faces'][i] for i in ids])
    assert after[name]['colors']==[before[cfg['split_source']]['colors'][i] for i in ids]
assert sum(len(o.data.polygons) for o in s.objects if o.type=='MESH')==report['source_faces']
assert len(after)==report['full_meshes']
assert bpy.data.objects[cfg['control']].parent.name=='TURRET_YAW'
assert all(bpy.data.objects[n].parent.name==cfg['control'] for n in cfg['moving_objects'])
assert bpy.data.objects['WT_roll_04'].data.polygons,'Separate front protective plate lost'
assert bpy.data.objects['WT_roll_04'].parent.name=='TURRET_YAW','Front protective plate must be fixed to the turret'
for o in s.objects:
    if o.name!=cfg['control'] and o.animation_data:o.animation_data.action=None
s.frame_set(1);bpy.context.view_layer.update();fixed={o.name:o.matrix_world.copy() for o in s.objects if o.type=='MESH' and o.name not in cfg['moving_objects']}
pivot=bpy.data.objects[cfg['control']];axis=Vector(cfg['pivot_world']);bounds={}
for frame in [1,20,40,60,90,120]:
    s.frame_set(frame);bpy.context.view_layer.update()
    for name,matrix in fixed.items():
        assert max(abs(a-b) for ra,rb in zip(matrix,bpy.data.objects[name].matrix_world) for a,b in zip(ra,rb))<2e-6,(frame,name)
    assert (pivot.matrix_world.translation-axis).length<2e-6
    if frame in [20,60,120]:
        vertices=[bpy.data.objects[n].matrix_world@v.co for n in cfg['moving_objects'] for v in bpy.data.objects[n].data.vertices]
        bounds[str(frame)]=[[min(p[k] for p in vertices) for k in range(3)],[max(p[k] for p in vertices) for k in range(3)]]
    if frame==60:assert abs(pivot.rotation_euler.y+math.pi/2)<1e-6
    if frame==120:assert abs(pivot.rotation_euler.y)<1e-6
assert bounds['20']==bounds['120'],'Launcher does not close back to its initial pose'
assert bounds['60'][0][2]>2.72,'Deployed launcher must rise above the roof'
assert bounds['60'][1][0]>bounds['20'][1][0]+.1,'Launcher must deploy outward'
full_base=after
bpy.ops.wm.open_mainfile(filepath=str(HERE/'tow_launcher.blend'));s=bpy.context.scene;s.frame_set(1);standalone=snapshot()
assert len(standalone)==8
assert bpy.data.objects[cfg['control']].matrix_world.translation.length<2e-6
for name,record in standalone.items():
    expected=[[tuple(Vector(v)-axis) for v in face] for face in full_base[name]['faces']]
    assert same_faces(record['faces'],expected),name
    assert record['colors']==full_base[name]['colors']
s.frame_set(60);assert abs(bpy.data.objects[cfg['control']].rotation_euler.y+math.pi/2)<1e-6
exports={}
for name,animations in [('bradley_tow_deploy.glb',5),('tow_launcher.glb',1)]:
    with open(HERE/name,'rb') as f:
        assert f.read(4)==b'glTF';f.read(8);size,kind=struct.unpack('<II',f.read(8));g=json.loads(f.read(size))
    assert len(g['animations'])==animations,(name,[a['name'] for a in g['animations']])
    assert all('KHR_materials_unlit' in m['extensions'] for m in g['materials'])
    if name=='bradley_tow_deploy.glb':
        grille=next(m for m in g['materials'] if m['name']=='Game grille')
        assert 'baseColorTexture' in grille['pbrMetallicRoughness'],'Do not replace the grille grid with a solid GLB fallback'
    assert all('COLOR_0' not in p['attributes'] for m in g['meshes'] for p in m['primitives'])
    exports[name]={'animations':[a['name'] for a in g['animations']],'unlit':True}
validation={'original_stowed_surfaces_and_colors_preserved':True,'only_fixed_bracket_faces_split':142,'total_vehicle_faces':report['source_faces'],
    'full_meshes':report['full_meshes'],'moving_parts':cfg['moving_objects'],'front_protective_plate_fixed':True,
    'fixed_mounts_stable_through_motion':True,'closes_exactly_to_original_pose':True,
    'pose_bounds_world':bounds,'standalone_meshes':8,'standalone_hinge_at_origin':True,'exports':exports,
    'pivot_fit':'source-based approximate game animation; not engineering kinematics','in_game_validation':'not_performed'}
(HERE/'validation.json').write_text(json.dumps(validation,indent=2)+'\n')
print('TOW SPLIT AND MOTION VALIDATED',json.dumps(validation))
