"""Confirm vehicle-style painting changes do not change geometry or cameras."""
import bpy, os, json, subprocess, tempfile
OUT=os.path.dirname(os.path.abspath(__file__))
ROOT=os.path.abspath(os.path.join(OUT,'../..'))
BASE='93679faa'
def snapshot(path):
    bpy.ops.wm.open_mainfile(filepath=path)
    result={}
    for o in bpy.context.scene.objects:
        record={'type':o.type,'matrix':[list(row) for row in o.matrix_world]}
        if o.type=='MESH':
            record['vertices']=[list(v.co) for v in o.data.vertices]
            record['faces']=[list(p.vertices) for p in o.data.polygons]
            record['colors']=[list(o.data.materials[p.material_index].node_tree.nodes.get('Emission').inputs[0].default_value) for p in o.data.polygons]
        elif o.type=='CAMERA':record['projection']=[o.data.type,o.data.ortho_scale]
        result[o.name]=record
    return result
with tempfile.TemporaryDirectory(prefix='paint_check_',dir=OUT) as temp:
    path=os.path.join(temp,'before.blend')
    with open(path,'wb') as f:
        f.write(subprocess.check_output(['git','show',BASE+':ArtSource/CollinsRadioDesk/collins_radio_desk.blend'],cwd=ROOT))
    before=snapshot(path)
after=snapshot(os.path.join(OUT,'collins_radio_desk.blend'))
assert before.keys()==after.keys(),'Object list changed'
changes={}
for name in before:
    a=dict(before[name]);b=dict(after[name])
    ac=a.pop('colors',[]);bc=b.pop('colors',[])
    assert a==b,'Geometry/camera changed: '+name
    count=sum(x!=y for x,y in zip(ac,bc))
    if count:
        assert name.startswith(('Collins_', 'Microphone_')),name
        assert name.endswith('_cabinet') or (len(a['vertices'])==32 and len(a['faces'])==18),name
        changes[name]=count
assert changes,'No painted faces changed'
with open(os.path.join(OUT,'paint_validation.json'),'w') as f:
    json.dump({'baseline_commit':BASE,'same_objects_geometry_transforms_cameras':True,'changed_face_counts':changes},f,indent=2)
print('Verified geometry/cameras unchanged; painted objects:',len(changes))
