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
# Two separate tabletop cabinets. Stylized sloped panels expose identity at 64 degrees.
def radio(name,x,width,height):
    y0=-.045; z0=.535; rear=.245
    # A shallow wedge replaces the large empty lid. Broad dark rim frames the panel.
    verts=[(x+dx,y,z) for dx in [-width/2,width/2]
           for y,z in [(y0,.525),(rear,.525),(rear,z0+height),(y0+height,z0+height)]]
    faces=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(3,7,6,2),(0,4,7,3)]
    mesh=bpy.data.meshes.new(name+'_wedge');mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name+'_cabinet',mesh);scene.collection.objects.link(obj)
    # The raised panel rim is painted casework; reserve black for real recesses.
    for m in [case[0],case[2],case[2]]:mesh.materials.append(m)
    for f in mesh.polygons:f.material_index=0 if f.index==4 else (2 if f.index==5 else 1)
    rotation=Matrix.Rotation(-math.pi/4,3,'X')
    origin=Vector((x,y0,z0));length=height*math.sqrt(2)
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
    # A few broad slots give the shallow case a readable mechanical surface.
    for i in range(3):
        cube(name+'_lid_vent',(x,y0+height+.023+i*.027,z0+height+.002),(width*.60,.009,.004),black)
        cube(name+'_rear_vent',(x,rear+.002,z0+height*.39+i*.031),(width-.080,.008,.011),black)
radio('Collins_32V3',-.238,.44,.19)
radio('Collins_75A3',.233,.43,.165)
for obj in list(scene.objects):
    if obj.type=='MESH' and obj.name.startswith('Collins_'):
        obj.location.y+=.16
scene['radio_style']='shallow_sloped_sprite_panels'
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
with open(os.path.join(OUT,'validation.json'),'w') as f:json.dump({'footprint':[1,1],'wood_rgb':[133,97,67],'color_mask':False,'cameras':checks},f,indent=2)
