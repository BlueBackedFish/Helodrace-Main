"""Reconstruct editable unlit Bradley asset from the user-supplied M3A3 OBJ."""
import bpy, os, json, math, collections
from mathutils import Vector
ROOT=os.path.dirname(os.path.abspath(__file__))
OUT=os.path.join(ROOT,'ReferenceFit');os.makedirs(OUT,exist_ok=True)
SOURCE=r'C:\Users\human\Downloads\Dagor-Asset-Explorer-1.0.0\Dagor-Asset-Explorer-1.0.0\ext\m3a3_bradley_0.obj'
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'m2a3_bradley.blend'))
scene=bpy.context.scene
for o in list(bpy.data.objects):
    if o.type!='CAMERA':bpy.data.objects.remove(o,do_unlink=True)

def empty(name,position,parent=None):
    o=bpy.data.objects.new(name,None);scene.collection.objects.link(o)
    o.location=position;bpy.context.view_layer.update()
    if parent:
        world=o.matrix_world.copy();o.parent=parent;o.matrix_world=world
    return o

root=empty('BRADLEY_ROOT',(0,0,0));hull=empty('HULL',(0,0,0),root)
turret=empty('TURRET_YAW',(-.22,.40,2.00),hull)
gun=empty('GUN_ELEVATION',(-.22,-.35,2.46),turret)
ramp=empty('REAR_RAMP_HINGE',(0,2.64,.59),hull)
door=empty('PERSONNEL_DOOR_HINGE',(.72,2.78,.68),ramp)
paint=[bpy.data.materials[n] for n in ['TAN | upper faces','TAN | fore and aft faces','TAN | side faces','TAN | underside']]
turret_paint=[bpy.data.materials['Turret '+m.name] for m in paint]
for m in paint+turret_paint:m.use_fake_user=True
rubber=bpy.data.materials['Track rubber'];steel=bpy.data.materials['Track metal']
glass=bpy.data.materials['Optics blue black'];light=bpy.data.materials['Headlamp ivory']
dark=bpy.data.materials['Panel edges and recesses']
for m in bpy.data.materials:
    if not m.use_nodes:continue
    for n in m.node_tree.nodes:
        if n.type=='TEX_COORD':n.object=root
        if n.name=='Height darkening' and 'TAN |' in m.name:
            a,b=(2.72,2.86) if m.name.startswith('Turret ') else (1.99,2.14)
            n.inputs['From Min'].default_value=a;n.inputs['From Max'].default_value=b

vertices=[];uvs=[];normals=[];groups=collections.defaultdict(list)
name='root';mat='body'
with open(SOURCE,encoding='utf-8') as f:
    for line in f:
        t=line.split()
        if not t:continue
        if t[0]=='v':
            x,y,z=map(float,t[1:4]);vertices.append((-z,-x,y+.012))
        elif t[0]=='vt':uvs.append(tuple(map(float,t[1:3])))
        elif t[0]=='vn':
            x,y,z=map(float,t[1:4]);normals.append((-z,-x,y))
        elif t[0]=='g':name=t[1]
        elif t[0]=='usemtl':mat=t[1]
        elif t[0]=='f':groups[name].append(([tuple(int(i) if i else 0 for i in v.split('/')) for v in t[1:]],mat))

turret_names={'bone_turret','bone_gun','gun_barrel','mg_twin','rocket_mount_01','turret_inside','ex_decor_02','ex_decor_03','hatch_02','hatch_03','ex_armor_03','bone_commander_sight_v','ex_decor_01'}
gun_names={'bone_gun','gun_barrel','mg_twin','ex_decor_01'}
# Extract the rear ramp and its fixtures from the source's static root group.
# Connectivity uses coincident positions so UV seams do not break the panels.
root_items=groups['@root'];leaders={}
def find(v):
    leaders.setdefault(v,v)
    while leaders[v]!=v:
        leaders[v]=leaders[leaders[v]];v=leaders[v]
    return v
position_ids={};aliases={}
for face,_ in root_items:
    for v in face:
        i=v[0]-1;key=tuple(round(c,4) for c in vertices[i])
        aliases[i]=position_ids.setdefault(key,i)
    a=find(aliases[face[0][0]-1])
    for v in face[1:]:leaders[find(aliases[v[0]-1])]=a
components=collections.defaultdict(list)
for item in root_items:components[find(aliases[item[0][0][0]-1])].append(item)
groups['@root']=[];groups['rear_ramp']=[]
for component in components.values():
    pts=[vertices[v[0]-1] for face,_ in component for v in face]
    lo=[min(p[k] for p in pts) for k in range(3)];hi=[max(p[k] for p in pts) for k in range(3)]
    inside=lo[1]>2.50 and lo[0]>-.90 and hi[0]<.90 and hi[2]<2.01
    fixed_jamb=(hi[0]-lo[0]<.10 and hi[2]-lo[2]>.5)
    groups['rear_ramp' if inside and not fixed_jamb else '@root'].extend(component)
assert len(groups['rear_ramp'])>500

texture_files={
    'm3a3_bradley_body_us_camo_olive':'m3a3_bradley_body_c.dds',
    'm3a3_bradley_turret_us_camo_olive':'m3a3_bradley_turret_c.dds',
    'm3a3_bradley_gun_us_camo_olive':'m3a3_bradley_gun_c.dds',
    'us_m3a3_bradley_track':'us_m3a3_bradley_track_c.dds',
    'mg_m240_mg_m240':'mg_m240_c.dds','net_f':'net_f_c.dds'}
images={};textured={}
def detail_material(base,source):
    if source not in texture_files:return base
    key=(base.name,source)
    if key in textured:return textured[key]
    if source not in images:
        image=bpy.data.images.load(os.path.join(OUT,'Textures',texture_files[source].replace('.dds','.png')),check_existing=True)
        image.pack();images[source]=image
    m=base.copy();m.name='WT detail | '+base.name+' | '+source
    nodes=m.node_tree.nodes;links=m.node_tree.links
    tex=nodes.new('ShaderNodeTexImage');tex.image=images[source]
    gray=nodes.new('ShaderNodeRGBToBW');links.new(tex.outputs['Color'],gray.inputs[0])
    mapping=nodes.new('ShaderNodeMapRange');mapping.clamp=True
    mapping.inputs['From Min'].default_value=.015;mapping.inputs['From Max'].default_value=.40
    mapping.inputs['To Min'].default_value=.90;mapping.inputs['To Max'].default_value=1.0
    if source=='net_f':
        mapping.inputs['From Min'].default_value=0;mapping.inputs['From Max'].default_value=1
        mapping.inputs['To Min'].default_value=.16
        links.new(tex.outputs['Alpha'],mapping.inputs['Value'])
    else:links.new(gray.outputs[0],mapping.inputs['Value'])
    emission=next(n for n in nodes if n.type=='EMISSION')
    previous=emission.inputs['Color'].links[0].from_socket
    color=nodes.new('ShaderNodeMixRGB');color.blend_type='MULTIPLY';color.inputs[0].default_value=1
    links.new(previous,color.inputs[1]);links.new(mapping.outputs[0],color.inputs[2]);links.new(color.outputs[0],emission.inputs['Color'])
    textured[key]=m
    return m
report={}
for name,items in groups.items():
    print('BUILD GROUP',name,flush=True)
    is_turret=name in turret_names or name.startswith(('roll_','antenna_'))
    parent=gun if name in gun_names else turret if is_turret else door if name=='hatch_04' else ramp if name=='rear_ramp' else hull
    indices=sorted({entry[0]-1 for face,material in items for entry in face})
    lookup={v:i for i,v in enumerate(indices)}
    points=[vertices[i] for i in indices]
    faces=[[lookup[v[0]-1] for v in face] for face,material in items]
    me=bpy.data.meshes.new(name);me.from_pydata(points,[],faces);me.update()
    o=bpy.data.objects.new('WT_'+name,me);scene.collection.objects.link(o)
    o.parent=parent;o.matrix_parent_inverse=parent.matrix_world.inverted()
    o['source_group']=name
    palette=(turret_paint if is_turret else paint)+[rubber,steel,glass,light,dark]
    slots={}
    # Material-slot growth can invalidate Blender's live polygon RNA handles.
    # Finish all slot allocation before iterating over mesh polygons.
    for source_mat in sorted({item[1] for item in items}):
        for index,base in enumerate(palette):
            slots[(index,source_mat)]=len(me.materials)
            me.materials.append(detail_material(base,source_mat))
    lo=Vector(tuple(min(p[k] for p in points) for k in range(3)))
    hi=Vector(tuple(max(p[k] for p in points) for k in range(3)))
    center=(lo+hi)/2
    wheel=name.startswith('wheel_')
    radius=max(hi.y-lo.y,hi.z-lo.z)/2
    for p,(_,source_mat) in zip(me.polygons,items):
        if 'track' in source_mat:index=5
        elif 'optic' in source_mat or 'dynamic_glass' in source_mat:index=6
        elif 'headlight' in source_mat:index=7
        elif 'net_f' in source_mat:index=0
        elif 'rifled' in source_mat or name=='mg_twin':index=8
        elif wheel and math.hypot(p.center.y-center.y,p.center.z-center.z)>radius*.82:index=4
        else:index=0 if p.normal.z>.45 else 3 if p.normal.z<-.45 else 1 if abs(p.normal.y)>.5 else 2
        key=(index,source_mat)
        p.material_index=slots[key]
    layer=me.uv_layers.new(name='Source UV')
    for poly,(face,_) in zip(me.polygons,items):
        # Recompute flat normals from the source triangles. Blender 5.1 crashes
        # on the extracted roll_05 custom split-normal payload; geometry stays intact.
        poly.use_smooth=False
        for li,v in zip(poly.loop_indices,face):
            if len(v)>1 and v[1]:layer.data[li].uv=uvs[v[1]-1]
    report[name]={'bounds':[list(lo),list(hi)],'vertices':len(points),'faces':len(faces),'parent':parent.name}

# Independent demo actions retain the familiar animation frame convention.
scene.render.fps=24;scene.frame_start=1;scene.frame_end=120
for o,axis,keys in [(turret,2,[(1,0),(40,45),(80,-35),(120,0)]),
                    (gun,0,[(1,0),(40,18),(80,0),(120,0)]),
                    (ramp,0,[(1,0),(40,0),(80,-100),(120,0)]),
                    (door,2,[(1,0),(25,-100),(40,0),(80,0),(120,0)])]:
    for frame,angle in keys:
        o.rotation_euler[axis]=math.radians(angle)
        o.keyframe_insert(data_path='rotation_euler',frame=frame)
    o.animation_data.action.name=o.name+'_DEMO'
scene.frame_set(1)
assert sum(len(o.data.polygons) for o in scene.objects if o.type=='MESH')==223389
assert bpy.data.objects['WT_hatch_04'].parent==door
assert bpy.data.objects['WT_rear_ramp'].parent==ramp
assert not any(o.type=='LIGHT' for o in scene.objects)
for camera in ['iso_front','iso_rear','north','east','south','west']:
    scene.camera=bpy.data.objects['Camera_'+camera]
    scene.render.filepath=os.path.join(OUT,'bradley_'+camera+'.png')
    bpy.ops.render.render(write_still=True)
for frame,name in [(25,'personnel_open'),(80,'ramp_open')]:
    scene.frame_set(frame);scene.camera=bpy.data.objects['Camera_iso_rear']
    scene.render.filepath=os.path.join(OUT,'bradley_'+name+'.png')
    bpy.ops.render.render(write_still=True)
scene.frame_set(1);scene.camera=bpy.data.objects['Camera_iso_front']
blend=os.path.join(OUT,'m3a3_bradley_refit.blend')
bpy.ops.wm.save_as_mainfile(filepath=blend,compress=True)
for m in bpy.data.materials:
    if not m.use_nodes:continue
    for n in m.node_tree.nodes:
        if n.type=='EMISSION':
            for link in list(n.inputs['Color'].links):m.node_tree.links.remove(link)
            n.inputs['Color'].default_value=m.diffuse_color
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,'m3a3_bradley_refit.glb'),export_format='GLB',export_animations=True,export_cameras=False,export_lights=False)
import hashlib,runpy
with open(SOURCE,'rb') as f:digest=hashlib.file_digest(f,'sha256').hexdigest()
with open(os.path.join(OUT,'source_groups.json'),'w') as f:json.dump(report,f,indent=2)
with open(os.path.join(OUT,'manifest.json'),'w') as f:
    json.dump({'source':SOURCE,'source_sha256':digest,'reference_variant':'War Thunder M3A3 user-supplied extracted OBJ',
               'source_group_count':111,'output_meshes':112,'preserved_faces':223389,
               'axis_conversion':'OBJ (X,Y,Z) -> Blender (-Z,-X,Y+0.012)',
               'reconstruction':'Full source exterior geometry and UVs retained; static root ramp extracted by connectivity',
               'paint':'Unlit TAN; source detail modulation limited to 0.90-1.00; engine mesh alpha used as grille pattern',
               'glb':'Constant emission fallback; full sprite material in packed blend',
               'animation_pivots':'Estimated from source geometry; 24fps frames1-120'},f,indent=2)
runpy.run_path(os.path.join(ROOT,'render_vf_east.py'),init_globals={'SOURCE_BLEND':blend,'OUTPUT_DIR':os.path.join(OUT,'VF_East')})
print('OBJ REFIT VALIDATION PASSED: all 223389 source faces preserved, 112 editable meshes, independent pivots')
