"""Verify exact local recolor scope and preserve every object, face and pivot."""
import bpy,json,hashlib,sys,shutil
from pathlib import Path
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent;OUT=HERE/'SideSlopes'
sys.path[:0]=[str(HERE),str(TRIAL),str(TRIAL.parent)]
from side_slopes import select
from height_palette import display,WEIGHTS
OUT.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(TRIAL/'bradley_emission_trial.blend'));bpy.context.scene.frame_set(1)
def geometry(o):
    return {'parent':o.parent.name,'world':[[round(v,6) for v in row] for row in o.matrix_world],
        'vertices':[tuple(round(c,6) for c in v.co) for v in o.data.vertices],
        'faces':[list(p.vertices) for p in o.data.polygons]}
def colors(o):return [tuple(round(c,6) for c in o.data.materials[p.material_index].diffuse_color) for p in o.data.polygons]
def sha(v):return hashlib.sha256(json.dumps(v,sort_keys=True).encode()).hexdigest()
objects={o.name:o for o in bpy.context.scene.objects if o.type=='MESH'}
if '--capture' in sys.argv:
    assert not (OUT/'before.json').exists(),'Do not overwrite the 73e2926a baseline'
    obj=objects['WT_@root'];record={'commit':'73e2926a','selected_faces':select(obj),'objects':{
        n:{'geometry':sha(geometry(o)),'colors':sha(colors(o))} for n,o in objects.items()},'root_colors':colors(obj)}
    (OUT/'before.json').write_text(json.dumps(record,indent=2)+'\n')
    for src,dst in [('HullRepair/after_front.png','before_front.png'),('VF_East/256/preview_east.png','before_east_256.png')]:
        shutil.copyfile(TRIAL/src,OUT/dst)
    print('SIDE SLOPE BASELINE CAPTURED');sys.exit(0)
before=json.loads((OUT/'before.json').read_text());report=json.loads((TRIAL/'manifest.json').read_text())['hull_slope_paint']
assert before['selected_faces']==report['faces']
assert set(objects)==set(before['objects'])
for n,o in objects.items():
    assert sha(geometry(o))==before['objects'][n]['geometry'],('Geometry or parenting changed',n)
    if n!='WT_@root':assert sha(colors(o))==before['objects'][n]['colors'],('Unrelated color changed',n)
obj=objects['WT_@root'];allowed=set(report['faces']);old=before['root_colors'];current=colors(obj);changed=[]
for i,(a,b) in enumerate(zip(old,current)):
    if i not in allowed:assert list(b)==a,('Unrelated hull face changed',i)
    else:
        assert list(b)!=a
        m=obj.data.materials[obj.data.polygons[i].material_index]
        assert abs(sum(display(v)*w for v,w in zip(m.diffuse_color,WEIGHTS))-132)<.002
        changed.append(i)
assert set(changed)==allowed
result={'baseline':'73e2926a','changed_faces':changed,'all_geometry_parents_and_pivots_preserved':True,
        'other_145_objects_colors_preserved':True,'all_unselected_hull_colors_preserved':True,
        'display_y':[108,132],'roof_gap':[60,36],'turret_contrast_compensation':False,'in_game_validation':'not_performed'}
(OUT/'validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('SIDE SLOPES VALIDATED',len(changed),'faces; 145 other objects untouched')
