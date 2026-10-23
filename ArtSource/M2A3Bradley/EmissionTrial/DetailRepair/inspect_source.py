"""Audit original OBJ components against the current fixed-color model."""
import bpy, json, sys, hashlib
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent; ROOT=HERE.parents[1]
assert not (HERE/'source_audit.json').exists(),'Do not overwrite the pre-repair source audit'
sys.path.insert(0,str(ROOT))
from mesh_regions import regions,position_key
manifest=json.loads((ROOT/'ReferenceFit/manifest.json').read_text())
source=Path(manifest['source'])
assert hashlib.sha256(source.read_bytes()).hexdigest()==manifest['source_sha256']
vertices=[];parts={};group='';material=''
for line in source.open(encoding='utf-8'):
    t=line.split()
    if not t:continue
    if t[0]=='v':
        x,y,z=map(float,t[1:4]);vertices.append((-z,-x,y+.012))
    elif t[0]=='g':group='WT_'+t[1]
    elif t[0]=='usemtl':material=t[1]
    elif t[0]=='f':
        ids=[int(v.split('/')[0]) for v in t[1:]]
        parts.setdefault(group,[]).append(([vertices[i-1 if i>0 else i] for i in ids],material))
bpy.ops.wm.open_mainfile(filepath=str(HERE.parent/'bradley_emission_trial.blend'))
bpy.context.scene.frame_set(1)
existing={position_key(o.matrix_world@v.co) for o in bpy.context.scene.objects if o.type=='MESH' for v in o.data.vertices}
report={}
for name,items in parts.items():
    if name not in {'WT_bone_turret','WT_bone_commander_sight_v','WT_antenna_01','WT_antenna_02','WT_roll_08'}:continue
    lookup={};points=[];faces=[]
    for coordinates,_ in items:
        face=[]
        for co in coordinates:
            if co not in lookup:lookup[co]=len(points);points.append(co)
            face.append(lookup[co])
        faces.append(face)
    mesh=bpy.data.meshes.new('audit '+name);mesh.from_pydata(points,[],faces);mesh.update()
    records=[]
    for component,ids in enumerate(regions(mesh)):
        coords=[Vector(points[v]) for i in ids for v in mesh.polygons[i].vertices]
        lo=[min(p[k] for p in coords) for k in range(3)];hi=[max(p[k] for p in coords) for k in range(3)]
        keys={position_key(p) for p in coords}
        records.append({'component':component,'faces':len(ids),'seeds':ids[:3],'bounds':[lo,hi],
            'source_vertices_present':len(keys&existing),'source_vertices':len(keys),
            'source_face_ids':ids})
    obj=bpy.data.objects.get(name)
    report[name]={'source_faces':len(items),'current_faces':len(obj.data.polygons) if obj else 0,
        'hidden':obj.hide_render if obj else None,'components':records}
(HERE/'source_audit.json').write_text(json.dumps(report,indent=2)+'\n')
for name,data in report.items():
    print(name,data['source_faces'],data['current_faces'],'hidden',data['hidden'])
    for r in data['components']:
        if name=='WT_bone_turret' and (r['bounds'][1][2]<2.65 or r['faces']<10):continue
        print(' component',r['component'],'faces',r['faces'],'seed',r['seeds'],'bounds',[[round(v,4) for v in p] for p in r['bounds']],
              'present',r['source_vertices_present'],'/',r['source_vertices'])
