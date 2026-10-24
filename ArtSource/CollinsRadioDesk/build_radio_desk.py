"""Blender background build: 1x1 Collins radio desk, baked colors, no mask."""
import bpy, math, os, json
import numpy as np
from mathutils import Vector, Matrix

OUT=os.path.dirname(os.path.abspath(__file__))
TEX=os.path.abspath(os.path.join(OUT,'../../Textures/Buildings'))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
def mat(name,rgb):
    def linear(c):
        c/=255
        return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    m=bpy.data.materials.new(name);m.use_nodes=True
    m.diffuse_color=tuple(linear(c) for c in rgb)+(1,)
    m.node_tree.nodes.clear()
    e=m.node_tree.nodes.new('ShaderNodeEmission');e.inputs[0].default_value=m.diffuse_color
    o=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],o.inputs['Surface'])
    return m
def palette(name,colors): return [mat(name+str(i),c) for i,c in enumerate(colors)]
# Telegraph desk reference: white top 255, both edge and legs 217 (~85%).
# Bake the same relationship into vanilla wood; no material color mask.
wood=palette('Vanilla_wood',[(133,97,67),(113,83,57),(113,83,57)])
case=palette('Charcoal_case',[(85,90,91),(81,86,87),(77,82,83)])
panel=palette('Gray_panel',[(123,126,122),(119,122,118),(115,118,114)])
black=palette('Dark',[(34,37,37)]*3)
cream=palette('Warm_meter',[(219,207,164)]*3)
steel=palette('Mic_metal',[(165,172,167),(157,164,159),(149,156,151)])
def cube(name,pos,size,mats):
    bpy.ops.mesh.primitive_cube_add(size=1,location=pos)
    o=bpy.context.object;o.name=name;o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for m in mats:o.data.materials.append(m)
    for f in o.data.polygons:f.material_index=0 if f.normal.z>.5 else (1 if abs(f.normal.y)>.5 else 2)
    return o
def cyl(name,pos,r,depth,mats,front=False):
    bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=r,depth=depth,location=pos)
    o=bpy.context.object;o.name=name
    if front:o.rotation_euler.x=math.pi/2
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=False)
    for m in mats:o.data.materials.append(m)
    # Vehicle-style small cylinders: one continuous barrel color, separate caps.
    for f in o.data.polygons:
        cap=abs(f.normal.y)>.9 if front else abs(f.normal.z)>.9
        f.material_index=0 if cap else 1
    return o
# Square footprint with broad untextured wooden masses.
cube('Desk_top',(0,0,.48),(1,1,.085),wood)
for x in [-.40,.40]:
    for y in [-.39,.39]:cube('Desk_leg',(x,y,.225),(.10,.10,.45),wood)
cube('Front_apron',(0,-.40,.395),(.88,.065,.12),wood)
cube('Rear_apron',(0,.40,.395),(.88,.065,.12),wood)
# Two separate tabletop cabinets. Vertical front panels on rectangular cases.
def radio(name,x,width,height):
    y0=-.045; z0=.535; rear=.245
    length=height*math.sqrt(2);top=z0+length
    left=x-width/2;right=x+width/2
    verts=[];faces=[];colors=[]
    def quad(points,color):
        start=len(verts);verts.extend(points)
        faces.append(tuple(range(start,start+4)));colors.append(color)
    quad([(left,y0,.525),(right,y0,.525),(right,y0,top),(left,y0,top)],1)
    quad([(left,y0,.525),(left,rear,.525),(right,rear,.525),(right,y0,.525)],2)
    quad([(left,y0,.525),(left,y0,top),(left,rear,top),(left,rear,.525)],2)
    quad([(right,y0,.525),(right,rear,.525),(right,rear,top),(right,y0,top)],2)
    # Partition the actual case surfaces: dark vent faces share the exact lid/rear
    # planes. No raised bars, decal overlays, thickness, or coplanar duplicates.
    slots=[(y0+.065+i*.055,y0+.077+i*.055) for i in range(3)]
    bounds=sorted([y0,rear]+[v for pair in slots for v in pair])
    xs=[left,x-width*.30,x+width*.30,right]
    for ya,yb in zip(bounds,bounds[1:]):
        vent=any(a<(ya+yb)/2<b for a,b in slots)
        for j,(xa,xb) in enumerate(zip(xs,xs[1:])):
            quad([(xa,ya,top),(xb,ya,top),(xb,yb,top),(xa,yb,top)],3 if vent and j==1 else 0)
    slots=[(z0+.065+i*.043,z0+.076+i*.043) for i in range(3)]
    bounds=sorted([.525,top]+[v for pair in slots for v in pair])
    xs=[left,left+.040,right-.040,right]
    for za,zb in zip(bounds,bounds[1:]):
        vent=any(a<(za+zb)/2<b for a,b in slots)
        for j,(xa,xb) in enumerate(zip(xs,xs[1:])):
            quad([(xb,rear,za),(xa,rear,za),(xa,rear,zb),(xb,rear,zb)],3 if vent and j==1 else 1)
    mesh=bpy.data.meshes.new(name+'_rectangular_case');mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name+'_cabinet',mesh);scene.collection.objects.link(obj)
    # The raised panel rim is painted casework; reserve black for real recesses.
    for m in [case[0],case[1],case[2],black[0]]:mesh.materials.append(m)
    for f,c in zip(mesh.polygons,colors):f.material_index=c
    rotation=Matrix.Identity(3)
    origin=Vector((x,y0,z0))
    def block(label,u,v,w,size,mats):
        o=cube(name+'_'+label,origin+rotation@Vector((u,v,w)),size,mats)
        o.rotation_euler=rotation.to_euler();return o
    block('panel',0,-.009,length/2,(width-.032,.014,length-.025),panel)
    block('lower_shadow',0,-.018,.023,(width-.03,.008,.025),black)
    def window(label,u,w,ww,hh):
        block(label+'_rim',u,-.021,w,(ww+.014,.012,hh+.014),black)
        block(label+'_glass',u,-.029,w,(ww,.006,hh),cream)
        block(label+'_needle',u+.012,-.034,w,(.006,.004,hh*.65),black)
        for i in [-1,0,1]:
            block(label+'_tick',u+i*ww*.28,-.034,w+hh*.32,(.004,.004,hh*.20),black)
    def knob(label,u,w,r):
        o=cyl(name+'_'+label,origin+rotation@Vector((u,-.037,w)),r,.022,black,True)
        o.rotation_euler=(rotation@o.rotation_euler.to_matrix()).to_euler()
        if r>.04:
            # Broad concentric dial face and pointer instead of a solid black dot.
            cap=cyl(name+'_dial_hub',origin+rotation@Vector((u,-.051,w)),r*.69,.007,panel,True)
            cap.rotation_euler=(rotation@cap.rotation_euler.to_matrix()).to_euler()
            block('dial_pointer',u,-.057,w+r*.35,(.007,.005,r*.54),cream)
    if '32V3' in name:
        window('left_meter',-.103,length-.061,.101,.060)
        window('right_meter',.103,length-.061,.101,.060)
        window('tuning_scale',0,length-.129,.110,.025)
    else:
        window('tuning_scale',-.065,length-.060,.157,.058)
        window('signal_meter',.123,length-.060,.066,.058)
    knob('main_tuning',0,.066,.049)
    for u in [-.137,.137]:knob('control',u,.059,.025)
    for u in [-.075,.075]:
        block('switch_socket',u,-.024,.065,(.024,.012,.032),black)
        block('switch',u,-.035,.070,(.009,.012,.027),steel)
    for u in [-width/2+.026,width/2-.026]:
        block('panel_grip',u,-.030,length*.57,(.012,.016,length*.33),steel)
radio('Collins_32V3',-.238,.44,.19)
radio('Collins_75A3',.233,.43,.165)
for obj in list(scene.objects):
    if obj.type=='MESH' and obj.name.startswith('Collins_'):
        obj.location.y+=.16
scene['radio_style']='rectangular_cases_vertical_panels_flush_vents'
# Single prominent desk microphone: rounded head, three grille bars and oval foot.
base=cyl('Microphone_base',(.325,-.326,.539),.077,.028,black)
base.scale.x=1.16;base.scale.y=.83
cyl('Microphone_stem',(.325,-.326,.605),.014,.095,steel)
mic=cube('Microphone_capsule',(.325,-.326,.737),(.125,.079,.180),steel)
bevel=mic.modifiers.new('Rounded_metal_head','BEVEL');bevel.width=.030;bevel.segments=2
for z,w in [(.705,.083),(.737,.099),(.769,.083)]:
    cube('Microphone_grille',(.325,-.367,z),(w,.006,.012),black)
tilt=Matrix.Rotation(math.radians(-30),3,'X');pivot=Vector((.325,-.326,.699))
for obj in list(scene.objects):
    if obj.name=='Microphone_capsule' or obj.name.startswith('Microphone_grille'):
        obj.location=pivot+tilt@(obj.location-pivot)
        obj.rotation_euler=(tilt@obj.rotation_euler.to_matrix()).to_euler()
# Twin paddle telegraph key: broad base, two metal arms and paired finger pieces.
cube('TelegraphKey_foot',(-.22,-.295,.536),(.255,.255,.027),black)
cube('TelegraphKey_base',(-.22,-.295,.553),(.230,.230,.009),case)
cube('TelegraphKey_pivot_block',(-.22,-.225,.580),(.140,.055,.046),case)
for x in [-.260,-.180]:
    cube('TelegraphKey_lever',(x,-.285,.593),(.016,.140,.018),steel)
    cyl('TelegraphKey_pivot',(x,-.225,.610),.021,.018,steel)
    paddle=cube('TelegraphKey_paddle',(x,-.370,.596),(.027,.065,.072),black)
    bevel=paddle.modifiers.new('Simple_finger_edges','BEVEL');bevel.width=.009;bevel.segments=1
# Cameras retain the existing presentation and framing.
target=Vector((0,0,.42));scale=1.47
for name,az in [('North',90),('East',0),('South',270)]:
    a=math.radians(az);e=math.radians(64)
    d=bpy.data.cameras.new('Camera_'+name);d.type='ORTHO';d.ortho_scale=scale
    c=bpy.data.objects.new('Camera_'+name,d);scene.collection.objects.link(c)
    c.location=target+8*Vector((math.cos(a)*math.cos(e),math.sin(a)*math.cos(e),math.sin(e)))
    c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler()
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=scene.render.resolution_y=512;scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
scene.view_settings.exposure=0;scene.view_settings.gamma=1
for name in ['North','East','South']:
    scene.camera=bpy.data.objects['Camera_'+name]
    scene.render.filepath=os.path.join(TEX,'HD_CollinsRadioDesk_'+name.lower()+'.png')
    bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['Camera_South']
bpy.context.preferences.filepaths.save_version=0
blend=os.path.join(OUT,'collins_radio_desk.blend');bpy.ops.wm.save_as_mainfile(filepath=blend)
bpy.ops.wm.open_mainfile(filepath=blend)
vent_faces=0
for name in ['Collins_32V3','Collins_75A3']:
    cabinet=bpy.data.objects[name+'_cabinet']
    top=max(v.co.z for v in cabinet.data.vertices);rear=max(v.co.y for v in cabinet.data.vertices)
    assert max(abs(a) for a in bpy.data.objects[name+'_panel'].rotation_euler)<1e-6
    for face in cabinet.data.polygons:
        if face.material_index==3:
            points=[cabinet.data.vertices[i].co for i in face.vertices]
            assert all(abs(v.z-top)<1e-6 for v in points) or all(abs(v.y-rear)<1e-6 for v in points)
            vent_faces+=1
assert vent_faces==12
assert not any('_lid_vent' in o.name or '_rear_vent' in o.name for o in bpy.context.scene.objects)
checks={};images=[]
for name in ['North','East','South']:
    c=bpy.data.objects['Camera_'+name];f=c.rotation_euler.to_quaternion()@Vector((0,0,-1))
    angle=math.degrees(math.asin(-f.z));assert abs(angle-64)<.001
    im=bpy.data.images.load(os.path.join(TEX,'HD_CollinsRadioDesk_'+name.lower()+'.png'))
    assert tuple(im.size)==(512,512)
    im.colorspace_settings.name='Non-Color'
    data=np.empty(512*512*4,np.float32);im.pixels.foreach_get(data);data=data.reshape(512,512,4)
    y,x=np.where(data[:,:,3]>.5);margin=min(int(x.min()),int(y.min()),511-int(x.max()),511-int(y.max()))
    assert margin>=24
    checks[name]={'downward_degrees':angle,'margin_px':margin};images.append(data)
im=bpy.data.images.new('Preview_North_East_South',width=1536,height=512,alpha=True)
im.colorspace_settings.name='Non-Color';im.pixels.foreach_set(np.concatenate(images,axis=1).ravel())
im.filepath_raw=os.path.join(OUT,'preview_north_east_south.png');im.file_format='PNG';im.save()
im.scale(384,128)
im.filepath_raw=os.path.join(OUT,'preview_128px.png');im.save()
with open(os.path.join(OUT,'validation.json'),'w') as f:json.dump({'footprint':[1,1],'wood_rgb':[133,97,67],'color_mask':False,'vertical_front_panels':True,'coplanar_vent_faces':vent_faces,'cameras':checks},f,indent=2)
