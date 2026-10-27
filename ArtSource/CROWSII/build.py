"""Paint the provided M153 FBX, including its original mounted M2HB."""
from pathlib import Path
from collections import defaultdict
import bpy,sys,json,hashlib,os
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(ROOT.parent/'PropAssets'));import modeling as m
import surface_paint as surfaces
import m2_detail
EXT=Path(os.environ.get('HELODRACE_REFERENCE_EXT',str(Path.home()/'Downloads/Dagor-Asset-Explorer-1.0.0/Dagor-Asset-Explorer-1.0.0/ext')))
SOURCE=EXT/'machine gun/m153-crows-untextured/source/M153 CROWS bevel.fbx'
scene=m.start();bpy.ops.import_scene.fbx(filepath=str(SOURCE))

def components(vertices,polygons):
    parent=list(range(len(vertices)));at={}
    def find(i):
        while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
        return i
    def union(a,b):parent[find(a)]=find(b)
    for i,v in enumerate(vertices):
        key=tuple(round(c,5) for c in v)
        if key in at:union(i,at[key])
        else:at[key]=i
    for p in polygons:
        for i in p.vertices[1:]:union(p.vertices[0],i)
    groups=defaultdict(list)
    for p in polygons:groups[find(p.vertices[0])].append(p.index)
    info={}
    for key,faces in groups.items():
        points=[vertices[i] for f in faces for i in polygons[f].vertices];lo=Vector([min(v[j] for v in points) for j in range(3)]);hi=Vector([max(v[j] for v in points) for j in range(3)])
        for f in faces:info[f]=(key,lo,hi)
    return info

original=[o for o in scene.objects if o.type=='MESH'];assert {o.name for o in original}=={'.50CAL','.50CAL SLEEVE','.50CAL BARREL','Cylinder.003'}
records=[]
for o in original:
    vertices=[o.matrix_world@v.co for v in o.data.vertices];parts=components(vertices,o.data.polygons);faces=[]
    for p in o.data.polygons:
        group,lo,hi=parts[p.index];center=sum((vertices[i] for i in p.vertices),Vector())/len(p.vertices);oldmat=o.data.materials[p.material_index].name
        if o.name=='.50CAL BARREL':layer='barrel';paint='metal'
        elif o.name.startswith('.50CAL'):layer='body';paint='rubber' if center.y<.36 else 'metal'
        else:
            layer='base' if group==4541 else 'frame';paint='fde'
            if group==4541 or group==2996:paint='fde_dark'
            if oldmat=='bolt':paint='fastener'
            elif group==6342:paint='feed'
            elif group in [4680,4790]:paint='metal'
            elif group==4242:paint='fde_lid'
            elif group in [3453,3630,494]:paint='fde_inset'
        faces.append(dict(indices=list(p.vertices),uv={uv.name:[tuple(uv.data[i].uv) for i in p.loop_indices] for uv in o.data.uv_layers},index=p.index,layer=layer,paint=paint,component=group))
    records.append(dict(name=o.name,vertices=vertices,faces=faces))
body=next(r for r in records if r['name']=='Cylinder.003');base_ids={i for f in body['faces'] if f['component']==4541 for i in f['indices']};assert base_ids
base_points=[body['vertices'][i] for i in base_ids];pivot=Vector(((min(v.x for v in base_points)+max(v.x for v in base_points))/2,(min(v.y for v in base_points)+max(v.y for v in base_points))/2,min(v.z for v in base_points)))
gun_points=[v for r in records if r['name'].startswith('.50CAL') for v in r['vertices']]
source_gun_length=max(v.y for v in gun_points)-min(v.y for v in gun_points);scale=2.1421875/source_gun_length
transform=Matrix.Diagonal((scale,scale,scale,1))@Matrix.Translation(-pivot)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
palette={name:m.material(name,rgb) for name,rgb in dict(fde=(143,123,92),fde_dark=(117,101,75),fde_lid=(153,132,99),fde_inset=(130,111,84),metal=(79,86,84),rubber=(34,40,38),feed=(48,56,52),fastener=(102,108,100),glass=(29,43,46),glass_inner=(44,65,69),glass_mark=(72,96,98)).items()}
palette.update({'paint_'+name:m.material('paint_'+name,rgb) for name,rgb in surfaces.palette().items()});keys=list(palette)
palette.update({'m2_'+name:m.material('m2_'+name,rgb) for name,rgb in m2_detail.PALETTE.items()});keys=list(palette)
# These are authored source component selections, not geometry simplification.
# Broad box/platform planes have direction roles. All other small rounded
# mechanisms remain single-color; no normal gradient is applied to cylinders.
PLANAR_COMPONENTS={917:'equipment',2413:'equipment',2503:'equipment',2602:'equipment',2996:'support',3162:'equipment',3902:'support_cap',4000:'equipment',4242:'lid',4372:'equipment',4611:'equipment',5112:'equipment',5255:'equipment',5424:'equipment',5581:'support_cap',6403:'equipment'}
# Source .50CAL connected components inspected against their actual bounds:
# upper cover/rear cover, feed cover, rear/front sights, charging handle/pins.
M2_COMPONENT_ROLES={5090:'cover',2812:'cover',3757:'cover',4503:'sight',4766:'sight',4277:'sight',833:'handle',955:'handle',2690:'handle',3195:'handle',672:'fastener',3017:'fastener',3131:'fastener',5318:'fastener'}
for r in records:
    grouped=defaultdict(list)
    for f in r['faces']:grouped[f['layer']].append(f)
    for layer,faces in grouped.items():
        used=sorted({i for f in faces for i in f['indices']});mapping={old:new for new,old in enumerate(used)}
        mesh=bpy.data.meshes.new(r['name']+' '+layer);mesh.from_pydata([transform@r['vertices'][i] for i in used],[],[[mapping[i] for i in f['indices']] for f in faces]);mesh.update()
        for key in keys:mesh.materials.append(palette[key])
        for p,f in zip(mesh.polygons,faces):
            key=f['paint'];group=f['component']
            if r['name']=='Cylinder.003' and key.startswith('fde'):
                if group==4541:
                    # Vertical pedestal: uniform cylindrical wall, separate cap.
                    key='paint_support_cap_top' if p.normal.z>.96 else 'paint_support_top'
                elif group in PLANAR_COMPONENTS:key='paint_'+PLANAR_COMPONENTS[group]+'_'+surfaces.face(p.normal)
                else:key='paint_support_cap_top'
            elif r['name']=='.50CAL SLEEVE':key='m2_'+m2_detail.sleeve(p.center,p.normal,'CROWS')
            elif r['name']=='.50CAL BARREL':key='m2_'+m2_detail.barrel(p.center,p.normal,'CROWS')
            elif r['name']=='.50CAL' and key=='metal':
                role=M2_COMPONENT_ROLES.get(group,'receiver')
                if p.normal.z<-.65:role='recess'
                elif p.normal.z<.65 and role in ['receiver','cover']:role='receiver_wall' if role=='receiver' else 'cover_wall'
                key='m2_'+role
            p.material_index=keys.index(key)
        for name in faces[0]['uv']:
            uv=mesh.uv_layers.new(name=name)
            for p,f in zip(mesh.polygons,faces):
                for index,co in zip(p.loop_indices,f['uv'][name]):uv.data[index].uv=co
        vi=mesh.attributes.new('source_vertex','INT','POINT');fi=mesh.attributes.new('source_face','INT','FACE')
        for dst,i in zip(vi.data,used):dst.value=i
        for dst,f in zip(fi.data,faces):dst.value=f['index']
        obj=bpy.data.objects.new(r['name']+' / '+layer,mesh);scene.collection.objects.link(obj);obj['layer']=layer;obj['source_object']=r['name']

def optical_panel(name,x,z,w,h,mat,y=1.970):
    points=[transform@Vector(p) for p in [(x-w/2,y,z-h/2),(x+w/2,y,z-h/2),(x+w/2,y,z+h/2),(x-w/2,y,z+h/2)]]
    o=m.panel(name,points,mat);o['layer']='frame';o['art_addition']='fixed-color optical window on source sensor housing'
optical_panel('Thermal window rim',2.449,5.077,.175,.213,palette['rubber'])
optical_panel('Thermal window',2.449,5.077,.143,.178,palette['glass'],1.973)
for name,radius,mat,y in [('Day lens rim',.081,palette['rubber'],1.970),('Day lens glass',.061,palette['glass_inner'],1.976)]:
    o=m.cylinder(name,transform@Vector((2.772,y,5.078)),transform@Vector((2.772,y+.004,5.078)),radius*scale,mat,40);o['layer']='frame';o['art_addition']='optical lens on source sensor housing'
optical_panel('Day lens fixed coating mark',2.772,5.104,.068,.014,palette['glass_mark'],1.984)
meshes=[o for o in scene.objects if o.type=='MESH'];assert sum(len(o.data.polygons) for o in meshes if 'source_object' in o)==sum(len(r['faces']) for r in records)
# Validate source vertices, every polygon and every UV loop after the layer split.
seen=defaultdict(set);loops=0
for o in meshes:
    if 'source_object' not in o:continue
    r=next(r for r in records if r['name']==o['source_object']);vi=o.data.attributes['source_vertex'];fi=o.data.attributes['source_face']
    for v,i in zip(o.data.vertices,vi.data):assert (v.co-transform@r['vertices'][i.value]).length<1e-5
    for p,i in zip(o.data.polygons,fi.data):
        assert i.value not in seen[r['name']];seen[r['name']].add(i.value);f=r['faces'][i.value];assert [vi.data[v].value for v in p.vertices]==f['indices']
        for uv in o.data.uv_layers:
            for index,expected in zip(p.loop_indices,f['uv'][uv.name]):assert (uv.data[index].uv-Vector(expected)).length<1e-6;loops+=1
for r in records:assert seen[r['name']]==set(range(len(r['faces'])))
top=m.camera('Camera_Top',0,90,3.2);top.location=(0,0,10);top.rotation_euler=(0,0,0)
for part in ['base','frame','body','barrel','gun','top','full']:
    for o in meshes:o.hide_render=not (part=='full' or part=='gun' and o['layer'] in ['body','barrel'] or part=='top' and o['layer']!='base' or o['layer']==part)
    m.render(OUT/(part+'_raw.png'),top,2048)
for o in meshes:o.hide_render=False
for direction,azimuth in [('north',-90),('east',0),('south',90),('west',180)]:
    cam=m.camera('Camera_'+direction,azimuth,64,3.6);m.render(OUT/(direction+'_raw.png'),cam,2048)
scene.camera=top;scene['muzzle_direction']='+Y';scene['swivel_origin']=[0,0,0];m.save(OUT/'crows_ii_m2.blend')
meta=dict(source=str(SOURCE),source_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),source_objects=[dict(name=r['name'],vertices=len(r['vertices']),faces=len(r['faces'])) for r in records],source_faces_preserved=sum(len(v) for v in seen.values()),source_uv_loops_preserved=loops,source_pivot=list(pivot),source_to_world_scale=scale,m2_visible_length_tiles=2.1421875,top_draw_size=[3.2,3.2],directional_draw_size=[3.6,3.6],pivot=[0,0,0],master_resolution=2048,native_resolution=1024,outline_world_tiles=.026,projection_top=90,projection_directions=64,paint='FDE hue; authored vehicle-style support levels, directional planes and desaturated dark paint',m2_included_in_provided_model=True,base_source_component=4541,feed_chute_source_component=6342,optical_housing_source_component=917,added_optical_window_meshes=5,gameplay_definition_added=False)
(OUT/'metadata.json').write_text(json.dumps(meta,indent=2)+'\n');print('CROWS_MODEL_PASS: source M153 and mounted M2 geometry/UVs preserved; fixed FDE emission',flush=True)
report=surfaces.report();report['planar_source_components']=PLANAR_COMPONENTS
(OUT/'paint_validation.json').write_text(json.dumps(report,indent=2)+'\n')
report=m2_detail.report('CROWS');report['receiver_component_roles']=M2_COMPONENT_ROLES
(OUT/'detail_paint.json').write_text(json.dumps(report,indent=2)+'\n')
