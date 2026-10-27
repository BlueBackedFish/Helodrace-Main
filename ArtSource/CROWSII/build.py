"""Retain the provided M153 station and mount the canonical M2HB QCB model."""
from pathlib import Path
from collections import defaultdict
import bpy,sys,json,hashlib,os
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(ROOT.parent/'PropAssets'));import modeling as m
import surface_paint as surfaces
sys.path.insert(0,str(ROOT));import feed_alignment
ARTICULATION=json.loads((ROOT/'articulation.json').read_text())
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
reference_gun=[o.matrix_world@v.co for o in original if o.name.startswith('.50CAL') for v in o.data.vertices]
reference_barrel=[o.matrix_world@v.co for o in original if o.name=='.50CAL BARREL' for v in o.data.vertices]
records=[]
for o in original:
    if o.name!='Cylinder.003':continue
    vertices=[o.matrix_world@v.co for v in o.data.vertices];parts=components(vertices,o.data.polygons);faces=[]
    for p in o.data.polygons:
        group,lo,hi=parts[p.index];center=sum((vertices[i] for i in p.vertices),Vector())/len(p.vertices);oldmat=o.data.materials[p.material_index].name
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
source_gun_length=max(v.y for v in reference_gun)-min(v.y for v in reference_gun);scale=2.1421875/source_gun_length
transform=Matrix.Diagonal((scale,scale,scale,1))@Matrix.Translation(-pivot)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
palette={name:m.material(name,rgb) for name,rgb in dict(fde=(143,123,92),fde_dark=(117,101,75),fde_lid=(153,132,99),fde_inset=(130,111,84),metal=(79,86,84),rubber=(34,40,38),feed=(48,56,52),fastener=(102,108,100),glass=(29,43,46),glass_inner=(44,65,69),glass_mark=(72,96,98)).items()}
palette.update({'paint_'+name:m.material('paint_'+name,rgb) for name,rgb in surfaces.palette().items()});keys=list(palette)
# These are authored source component selections, not geometry simplification.
# Broad box/platform planes have direction roles. All other small rounded
# mechanisms remain single-color; no normal gradient is applied to cylinders.
PLANAR_COMPONENTS={917:'equipment',2413:'equipment',2503:'equipment',2602:'equipment',2996:'support',3162:'equipment',3902:'support_cap',4000:'equipment',4242:'lid',4372:'equipment',4611:'equipment',5112:'equipment',5255:'equipment',5424:'equipment',5581:'support_cap',6403:'equipment'}
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

# The ground-gun model is the single source for both copies of the QCB weapon.
# Keep the station's box, chute and cradle; omit the donor's tripod/ammunition.
DONOR=ROOT.parent/'HeavyMachineGuns/output/M2HB/model.blend'
donor_meta=json.loads(DONOR.with_name('metadata.json').read_text());assert donor_meta['variant']=='M2HB QCB'
with bpy.data.libraries.load(str(DONOR),link=False) as (available,loaded):loaded.objects=list(available.objects)
donors=[]
feed_reference=[]
for o in loaded.objects:
    if o.type=='MESH' and o.get('pose') in ARTICULATION['feed_reference_poses']:
        assert o.parent is None
        feed_reference.extend(o.matrix_basis@v.co for v in o.data.vertices)
    if o.type=='MESH' and o.get('pose')=='gun' and o.get('layer') in ['body','barrel']:
        scene.collection.objects.link(o);o.hide_render=False;donors.append(o)
    else:bpy.data.objects.remove(o,do_unlink=True)
assert donors and len([o for o in donors if o.get('art_addition')=='QCB visual kit'])==8
bpy.context.view_layer.update()
def muzzle_axis(points,epsilon):
    front=max(v.y for v in points);ring=[v for v in points if v.y>front-epsilon]
    return Vector(((min(v.x for v in ring)+max(v.x for v in ring))/2,front,(min(v.z for v in ring)+max(v.z for v in ring))/2))
old_tip=transform@muzzle_axis(reference_barrel,.002)
donor_barrel=[o.matrix_world@v.co for o in donors if o.get('source_object')=='Barrel' for v in o.data.vertices]
donor_tip=muzzle_axis(donor_barrel,.001);donor_scale=donor_meta['model_to_world']
mount_transform=Matrix.Translation(old_tip-donor_tip*donor_scale)@Matrix.Diagonal((donor_scale,donor_scale,donor_scale,1))
donor_summary=[]
for o in donors:
    original_name=o.name;before=o.matrix_world.copy();points=[before@v.co for v in o.data.vertices]
    o.matrix_world=mount_transform@before;o['donor_object']=original_name;o.name='M2QCB / '+original_name
    for v,p in zip(o.data.vertices,points):assert (o.matrix_world@v.co-mount_transform@p).length<1e-6
    donor_summary.append(dict(object=original_name,layer=o['layer'],vertices=len(o.data.vertices),faces=len(o.data.polygons),qcb_addition=o.get('art_addition')=='QCB visual kit'))
assert not any(o.get('pose','').startswith('ammo_') or o.get('layer')=='mount' for o in donors)
bpy.context.view_layer.update()
# Match the retained flexible chute to the actual donor cartridge envelope and
# receiver-side cover edge. The box end remains anchored to the station.
assert feed_reference
cover=next(o for o in donors if o.get('source_object')==ARTICULATION['feed_cover_source_object'])
cover_native=[mount_transform.inverted()@(cover.matrix_world@v.co) for v in cover.data.vertices]
target_native=Vector((min(v.x for v in cover_native)+ARTICULATION['feed_overlap_donor_units'],
                      (min(v.y for v in feed_reference)+max(v.y for v in feed_reference))/2,
                      (min(v.z for v in feed_reference)+max(v.z for v in feed_reference))/2))
target=mount_transform@target_native;width=(max(v.y for v in feed_reference)-min(v.y for v in feed_reference))*donor_scale
station=next(o for o in meshes if o.get('source_object')=='Cylinder.003' and o.get('layer')=='frame')
feed_report=feed_alignment.align(station,palette['feed'],target,width,ARTICULATION['feed_blend_rings'])
feed_report['donor_reference_poses']=ARTICULATION['feed_reference_poses'];feed_report['target_donor_coordinates']=list(target_native)
(OUT/'feed_validation.json').write_text(json.dumps(feed_report,indent=2)+'\n')
meshes=[o for o in scene.objects if o.type=='MESH']
donor_report=dict(passed=True,donor=str(DONOR),donor_sha256=hashlib.sha256(DONOR.read_bytes()).hexdigest(),variant='M2HB QCB',donor_scale=donor_scale,mount_transform=[list(row) for row in mount_transform],muzzle_world=list(old_tip),objects=donor_summary,station_except_feed_geometry_and_paint_preserved=True,donor_ammunition_and_tripod_excluded=True)
(OUT/'donor_validation.json').write_text(json.dumps(donor_report,indent=2)+'\n')

# One swivel for the station and an independent local -Y recoil controller.
yaw=bpy.data.objects.new('CROWS_YAW',None);scene.collection.objects.link(yaw)
recoil=bpy.data.objects.new('BARREL_RECOIL',None);scene.collection.objects.link(recoil);recoil.parent=yaw
for o in meshes:
    if o['layer']=='base':continue
    world=o.matrix_world.copy();o.parent=recoil if o['layer']=='barrel' else yaw;o.matrix_world=world
scene.render.fps=ARTICULATION['recoil_fps'];scene.frame_start=ARTICULATION['recoil_frames'][0];scene.frame_end=ARTICULATION['recoil_frames'][-1]
for frame,travel in zip(ARTICULATION['recoil_frames'],[0,ARTICULATION['recoil_tiles'],0]):
    recoil.location=(0,-travel,0);recoil.keyframe_insert(data_path='location',frame=frame)
scene.frame_set(scene.frame_start);bpy.context.view_layer.update()
top=m.camera('Camera_Top',0,90,3.2);top.location=(0,0,10);top.rotation_euler=(0,0,0)
for part in ['base','frame','body','barrel','gun','top','full']:
    for o in meshes:o.hide_render=not (part=='full' or part=='gun' and o['layer'] in ['body','barrel'] or part=='top' and o['layer']!='base' or o['layer']==part)
    m.render(OUT/(part+'_raw.png'),top,2048)
# Retain only the actually visible chute in an overlay. It restores the small
# lip that passes in front of the receiver while the rest of the frame stays
# behind the moving barrel; the gun is never duplicated in this layer.
holdout=bpy.data.materials.new('Feed overlay occlusion mask');holdout.use_nodes=True;holdout.node_tree.nodes.clear()
node=holdout.node_tree.nodes.new('ShaderNodeHoldout');output=holdout.node_tree.nodes.new('ShaderNodeOutputMaterial');holdout.node_tree.links.new(node.outputs[0],output.inputs['Surface'])
backup={o:list(o.data.materials) for o in meshes}
for o in meshes:
    o.hide_render=False
    for index,material in enumerate(backup[o]):
        if material!=palette['feed']:o.data.materials[index]=holdout
m.render(OUT/'feed_overlay_raw.png',top,2048)
for o,materials in backup.items():
    for index,material in enumerate(materials):o.data.materials[index]=material
for o in meshes:o.hide_render=False
for direction,azimuth in [('north',-90),('east',0),('south',90),('west',180)]:
    cam=m.camera('Camera_'+direction,azimuth,64,3.6);m.render(OUT/(direction+'_raw.png'),cam,2048)
scene.frame_set(ARTICULATION['recoil_frames'][1])
for o in meshes:o.hide_render=o['layer']=='base'
m.render(OUT/'recoil_raw.png',top,2048)
scene.frame_set(scene.frame_start)
for o in meshes:o.hide_render=False
scene.camera=top;scene['muzzle_direction']='+Y';scene['swivel_origin']=[0,0,0];m.save(OUT/'crows_ii_m2.blend')
meta=dict(source=str(SOURCE),source_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),source_objects=[dict(name=r['name'],vertices=len(r['vertices']),faces=len(r['faces'])) for r in records],source_faces_preserved=sum(len(v) for v in seen.values()),source_uv_loops_preserved=loops,source_pivot=list(pivot),source_to_world_scale=scale,m2_visible_length_tiles=2.1421875,top_draw_size=[3.2,3.2],directional_draw_size=[3.6,3.6],pivot=[0,0,0],master_resolution=2048,native_resolution=1024,outline_world_tiles=.026,projection_top=90,projection_directions=64,paint='FDE hue; authored vehicle-style support levels, directional planes and desaturated dark paint',weapon_variant='M2HB QCB',weapon_donor=str(DONOR),weapon_donor_sha256=donor_report['donor_sha256'],weapon_mount_transform=donor_report['mount_transform'],replaced_source_weapon_meshes=['.50CAL','.50CAL SLEEVE','.50CAL BARREL'],base_source_component=4541,feed_chute_source_component=6342,optical_housing_source_component=917,added_optical_window_meshes=5,gameplay_definition_added=False)
(OUT/'metadata.json').write_text(json.dumps(meta,indent=2)+'\n');print('CROWS_MODEL_PASS: original M153 station retained; canonical M2HB QCB mounted with fixed-emission materials',flush=True)
meta['articulation']=ARTICULATION;meta['feed_alignment']=feed_report
(OUT/'metadata.json').write_text(json.dumps(meta,indent=2)+'\n')
report=surfaces.report();report['planar_source_components']=PLANAR_COMPONENTS
(OUT/'paint_validation.json').write_text(json.dumps(report,indent=2)+'\n')
report=json.loads(DONOR.with_name('detail_paint.json').read_text());report['note']='Canonical M2HB QCB donor mesh/materials copied unchanged; only one uniform scale and mount transform applied';report['mount_transform']=donor_report['mount_transform']
(OUT/'detail_paint.json').write_text(json.dumps(report,indent=2)+'\n')
