"""FDE anchored ground plate; canonical CROWS assembly stays a separate asset."""
from pathlib import Path
import bpy,sys,json,math,hashlib
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(ROOT.parents[1]/'PropAssets'));import modeling as m;import surface_paint
scene=m.start();palette=surface_paint.palette()
paint=m.material('Foundation FDE',palette['equipment_top']);side=m.material('Foundation FDE walls',palette['equipment_front']);lower=m.material('Foundation lower steel',palette['support_cap_top']);cap=m.material('Foundation raised FDE',palette['lid_top']);steel=m.material('Foundation fasteners',(102,108,100));dark=m.material('Foundation gasket',(34,40,38))
parts=[]
def tag(o,part):o['layer']='foundation';o['foundation_part']=part;parts.append(o);return o
def plate(name,width,depth,clip,bottom,top,face,wall):
    x,y=width/2,depth/2;c=clip;ring=[(-x+c,-y),(x-c,-y),(x,-y+c),(x,y-c),(x-c,y),(-x+c,y),(-x,y-c),(-x,-y+c)]
    vs=[(a,b,z) for z in [bottom,top] for a,b in ring];fs=[tuple(reversed(range(8))),tuple(range(8,16))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vs,[],fs);mesh.materials.append(wall);mesh.materials.append(face);mesh.polygons[1].material_index=1
    o=bpy.data.objects.new(name,mesh);scene.collection.objects.link(o);return tag(o,'plate')
def capped(name,a,b,r,wall,face,segments=48):
    o=m.cylinder(name,a,b,r,wall,segments);o.data.materials.append(face)
    for p in o.data.polygons:
        if p.normal.z>.9:p.material_index=1
    return o
plate('Clipped lower plinth',1.50,1.36,.17,.02,.10,lower,lower)
plate('Anchored upper steel plate',1.46,1.32,.16,.10,.145,paint,side)
tag(capped('Central mounting flange',(0,0,.145),(0,0,.175),.225,side,cap),'flange')
for i in range(6):
    a=math.tau*i/6;x,y=.193*math.cos(a),.193*math.sin(a)
    tag(m.cylinder('Flange washer '+str(i),(x,y,.175),(x,y,.181),.026,dark,32),'flange_washer')
    tag(m.cylinder('Flange hex bolt '+str(i),(x,y,.181),(x,y,.198),.018,steel,6),'flange_bolt')
for i,(x,y) in enumerate([(-.585,-.495),(.585,-.495),(.585,.495),(-.585,.495)]):
    tag(m.cylinder('Ground anchor washer '+str(i),(x,y,.145),(x,y,.152),.047,dark,40),'anchor_washer')
    tag(m.cylinder('Ground anchor hex nut '+str(i),(x,y,.152),(x,y,.183),.030,steel,6),'anchor_bolt')
    tag(m.cylinder('Ground anchor stud '+str(i),(x,y,.183),(x,y,.192),.012,lower,20),'anchor_stud')
for i in range(4):
    a=math.tau*i/4;u=Vector((math.cos(a),math.sin(a),0));v=Vector((-math.sin(a),math.cos(a),0));vs=[]
    for sign in [-1,1]:
        for r,z in [(.21,.145),(.39,.145),(.21,.177)]:vs.append(tuple(u*r+v*(sign*.026)+Vector((0,0,z))))
    mesh=bpy.data.meshes.new('Radial reinforcement');mesh.from_pydata(vs,[],[(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)]);mesh.materials.append(side);mesh.materials.append(cap);mesh.polygons[3].material_index=1
    o=bpy.data.objects.new('Radial reinforcing rib '+str(i),mesh);scene.collection.objects.link(o);tag(o,'rib')
tag(m.box('Rear power and data junction',(-.47,.35,.195),(.25,.18,.10),paint,.012,side=side),'junction')
tag(m.box('Junction cover',(-.47,.35,.250),(.23,.16,.015),cap,.007),'junction_lid')
for x in [-.54,-.40]:
    for y in [.30,.40]:tag(m.cylinder('Junction lid screw',(x,y,.258),(x,y,.266),.009,steel,16),'junction_screw')
tag(m.cable('Foundation cable to center',[(-.34,.35,.19),(-.28,.34,.16),(-.17,.26,.16),(-.10,.19,.165),(-.05,.145,.18)],.014,dark),'cable')
tag(m.cylinder('Outer cable gland',(-.59,.35,.19),(-.63,.35,.19),.022,dark,24),'connector')
foundation=list(parts);assert len([o for o in parts if o['foundation_part']=='anchor_bolt'])==4
SOURCE=ROOT.parent/'output/crows_ii_m2.blend';source_meta=json.loads(SOURCE.with_name('metadata.json').read_text())
with bpy.data.libraries.load(str(SOURCE),link=False) as (available,loaded):loaded.objects=list(available.objects)
crows=[]
for o in loaded.objects:
    if o.type in ['MESH','EMPTY']:scene.collection.objects.link(o);crows.append(o)
    else:bpy.data.objects.remove(o,do_unlink=True)
bpy.context.view_layer.update();scene.frame_set(1);bpy.context.view_layer.update()
before={o.name:[o.matrix_world@v.co for v in o.data.vertices] for o in crows if o.type=='MESH'}
anchor=bpy.data.objects.new('CROWS_FOUNDATION_ANCHOR',None);scene.collection.objects.link(anchor);anchor.location.z=.175
for o in crows:
    if o.parent is None:o.parent=anchor
bpy.context.view_layer.update()
for o in crows:
    if o.type=='MESH':
        for v,p in zip(o.data.vertices,before[o.name]):assert (o.matrix_world@v.co-p-Vector((0,0,.175))).length<2e-6,(o.name,v.index)
meshes=[o for o in scene.objects if o.type=='MESH'];source_base=next(o for o in crows if o.get('layer')=='base')
assert abs(min((source_base.matrix_world@v.co).z for v in source_base.data.vertices)-.175)<1e-6
top=m.camera('Foundation_Top',0,90,3.2);top.location=(0,0,10);top.rotation_euler=(0,0,0)
for o in meshes:o.hide_render=o not in foundation
m.render(OUT/'plate_top_raw.png',top,2048)
for direction,azimuth in [('north',-90),('east',0),('south',90),('west',180)]:
    cam=m.camera('Foundation_'+direction,azimuth,64,3.6)
    for o in meshes:o.hide_render=o not in foundation
    m.render(OUT/('plate_'+direction+'_raw.png'),cam,2048)
    for o in meshes:o.hide_render=False
    m.render(OUT/('assembled_'+direction+'_raw.png'),cam,2048)
m.render(OUT/'assembled_top_raw.png',top,2048)
scene.camera=top;scene.render.fps=60;scene.frame_start=1;scene.frame_end=6;scene.frame_set(1)
m.save(OUT/'fixed_mount.blend')
metadata=dict(source_crows=str(SOURCE),source_crows_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),foundation_meshes=len(foundation),station_meshes=len(before),source_station_vertices_preserved=True,source_station_materials_preserved=True,top_draw_size=[3.2,3.2],directional_draw_size=[3.6,3.6],pivot=[0,0],plate_dimensions=[1.5,1.36],station_mount_height=.175,plate_top_contact=.175,ground_anchors=4,central_mount_bolts=6,paint_rgb=palette['equipment_top'],master_resolution=2048,native_resolution=1024,world_outline_tiles=.026,ui_outline_pixels=10,stationary_layer='foundation',weapon_recoil_preserved=source_meta['articulation'],gameplay_definition_added=False)
(OUT/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')
print('FIXED_MOUNT_MODEL_PASS: FDE anchored plate, 4 ground anchors and 6 flange bolts; canonical station only translated onto its seat')
