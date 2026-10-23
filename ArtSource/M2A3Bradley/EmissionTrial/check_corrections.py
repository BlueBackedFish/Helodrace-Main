"""Capture and validate this correction against b3fdf6f6 without touching geometry."""
import bpy,sys,json,hashlib
from bpy_extras.object_utils import world_to_camera_view
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(__file__).resolve().parent;AUDIT=OUT/'Corrections'
sys.path[:0]=[str(ROOT),str(OUT)]
from height_palette import display,WEIGHTS
from paint_corrections import allowed_faces,select_faces
cfg=json.loads((OUT/'settings.json').read_text())['corrections']
uniform_rolls={t['object'] for t in cfg.get('uniform_rolls',[])}
legacy_cfg={k:v for k,v in cfg.items() if k!='uniform_rolls'}
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'RimWorld/bradley_rimworld.blend'));bpy.context.scene.frame_set(1)
allowed=allowed_faces(bpy.context.scene,legacy_cfg)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'bradley_emission_trial.blend'));bpy.context.scene.frame_set(1)
def color(obj,p):return tuple(round(c,6) for c in obj.data.materials[p.material_index].diffuse_color[:3])
def snapshot():
    result={}
    for obj in bpy.context.scene.objects:
        if obj.type!='MESH':continue
        ids=set(allowed.get(obj.name,[]));outside=[];inside={}
        for p in obj.data.polygons:
            rgb=color(obj,p)
            if p.index in ids:inside[str(p.index)]=rgb
            else:outside.append((p.index,rgb))
        result[obj.name]={'outside_hash':hashlib.sha256(str(outside).encode()).hexdigest(),'outside_faces':len(outside),'inside':inside}
    return result
current=snapshot()
if '--capture' in sys.argv:
    assert not (AUDIT/'before_colors.json').exists(),'Recorded b3fdf6f6 baseline already exists; do not replace it'
    (AUDIT/'before_colors.json').write_text(json.dumps({'commit':cfg['previous_trial_commit'],'allowed_faces':allowed,'objects':current},indent=2)+'\n')
    print('CORRECTION BASELINE CAPTURED',sum(len(v) for v in allowed.values()),'allowed faces');sys.exit(0)
before=json.loads((AUDIT/'before_colors.json').read_text());assert before['allowed_faces']==allowed
assert before['objects'].keys()==current.keys()
changes={}
for name,record in current.items():
    # These three entire painted objects now have a separate ce465c14 audit;
    # the older b3fdf6f6 snapshot cannot mask their newly requested changes.
    if name in uniform_rolls:continue
    assert record['outside_hash']==before['objects'][name]['outside_hash'],('Unrelated paint changed',name)
    previous=before['objects'][name]['inside']
    changed=sum(tuple(rgb)!=tuple(previous[i]) for i,rgb in record['inside'].items())
    if changed:changes[name]=changed
assert 'WT_bone_gun' not in changes
def y(obj,p):return sum(display(v)*w for v,w in zip(obj.data.materials[p.material_index].diffuse_color[:3],WEIGHTS))
measured={}
for target in cfg['bright_surfaces']:
    obj=bpy.data.objects[target['object']];ids=select_faces(obj,target)
    values=[y(obj,obj.data.polygons[i]) for i in ids]
    assert all(abs(v-target['display_luminance'])<.002 for v in values)
    measured[target['name']]={'object':obj.name,'faces':len(ids),'range':[min(values),max(values)]}
repair=cfg['uniform_linkage'];obj=bpy.data.objects[repair['object']]
report=json.loads((OUT/'manifest.json').read_text())['surface_corrections'][obj.name]
for group in report:
    colors={color(obj,obj.data.polygons[i]) for i in group['faces']}
    assert len(colors)==1,('Same coherent surface has split colors',group['faces'])
values=[y(obj,p) for p in obj.data.polygons]
assert {round(v) for v in values}<={repair['top'],repair['side'],repair['underside']}
handles=[]
for name,ids in allowed.items():
    if name!='WT_hatch_02':continue
    obj=bpy.data.objects[name];handles=[y(obj,obj.data.polygons[i]) for i in ids]
assert handles and max(handles)<233 and min(handles)>207
assert max(handles)-216<17,'Handle should be only slightly brighter than the flat lid'
target=cfg['reverted_wrong_target'];obj=bpy.data.objects[target['object']]
reverted=[y(obj,p) for p in obj.data.polygons if p.normal.z>=target['minimum_normal_z']]
assert max(reverted)<230,'Wrong previous brightness target still active'
validation={'compared_with':cfg['previous_trial_commit'],'only_requested_faces_changed':True,'changed_faces':changes,
'wt_bone_gun_all_colors_unchanged':True,'bright_surfaces':measured,
'handle_display_y':[min(handles),max(handles)],'linkage_001_colors':sorted({round(v) for v in values}),
'linkage_001_coherent_regions':len(report),'wrong_previous_target_restored':True,'in_game_validation':'not_performed'}
validation['uniform_roll_objects_checked_by']='FlatRolls/check_rolls.py'
pose=json.loads((OUT/'camera_views.json').read_text())['closeup_front']
s=bpy.context.scene;camera=bpy.data.objects['Camera_iso_front'];camera.location=pose['location'];camera.rotation_euler=pose['rotation_euler']
camera.data.ortho_scale=pose['ortho_scale'];s.camera=camera;s.render.resolution_x=s.render.resolution_y=pose['size'];bpy.context.view_layer.update()
boxes={}
for label,name,ids in [('handle','WT_hatch_02',allowed['WT_hatch_02']),
    ('linkage_001',repair['object'],list(range(len(bpy.data.objects[repair['object']].data.polygons)))),
    ('linkage_025','WT_LinkageSupport_025',allowed['WT_LinkageSupport_025']),
    ('turret_plate','WT_bone_turret',allowed['WT_bone_turret'])]:
    obj=bpy.data.objects[name]
    points=[world_to_camera_view(s,camera,obj.matrix_world@obj.data.vertices[v].co) for i in ids for v in obj.data.polygons[i].vertices]
    boxes[label]=[min(p.x for p in points)*768,(1-max(p.y for p in points))*768,max(p.x for p in points)*768,(1-min(p.y for p in points))*768]
validation['front_image_boxes']=boxes
(AUDIT/'validation.json').write_text(json.dumps(validation,indent=2)+'\n')
print('TARGETED CORRECTIONS VALIDATED',json.dumps(validation))
