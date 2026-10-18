"""Blender background build: 1x1 Collins radio desk, baked colors, no mask."""
import bpy, math, os, json
import numpy as np
from mathutils import Vector

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
wood=palette('Vanilla_wood',[(133,97,67),(108,77,51),(94,66,44)])
case=palette('Charcoal_case',[(85,90,91),(59,63,65),(45,49,51)])
panel=palette('Gray_panel',[(123,126,122),(107,110,106),(85,89,87)])
black=palette('Dark',[(34,37,37)]*3)
cream=palette('Warm_meter',[(219,207,164)]*3)
steel=palette('Mic_metal',[(165,172,167),(125,134,131),(103,111,108)])
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
    for f in o.data.polygons:f.material_index=0 if f.normal.z>.4 else (1 if f.normal.y<-.4 else 2)
    return o
# Square footprint with broad untextured wooden masses.
cube('Desk_top',(0,0,.48),(1,1,.085),wood)
for x in [-.40,.40]:
    for y in [-.39,.39]:cube('Desk_leg',(x,y,.225),(.10,.10,.45),wood)
cube('Front_apron',(0,-.40,.395),(.88,.065,.12),wood)
cube('Rear_apron',(0,.40,.395),(.88,.065,.12),wood)
# Two separate tabletop cabinets. Front is -Y, feed/work side of desk.
def radio(name,x,width,height):
    z=.525+height/2
    cube(name+'_cabinet',(x,.19,z),(width,.43,height),case)
    cube(name+'_front',(x,-.034,z),(width-.028,.022,height-.025),panel)
    return z
z=radio('Collins_32V3',-.238,.44,.35)
def window(name,x,z,w,h):
    cube(name+'_rim',(x,-.049,z),(w+.012,.012,h+.012),black)
    cube(name+'_glass',(x,-.057,z),(w,.005,h),cream)
    cube(name+'_needle',(x+.008,-.061,z),(.004,.004,h*.58),black)
window('32V3_left_meter',-.365,.795,.074,.047)
window('32V3_right_meter',-.111,.795,.074,.047)
window('32V3_tuning_window',-.238,.755,.084,.05)
cyl('32V3_main_tuning',(-.238,-.071,.657),.043,.034,black,True)
for x in [-.385,-.31,-.166,-.091]:cyl('32V3_control',(x,-.066,.597),.017,.022,black,True)
z=radio('Collins_75A3',.233,.43,.275)
window('75A3_signal_meter',.355,.748,.055,.038)
window('75A3_tuning_scale',.196,.735,.125,.051)
cyl('75A3_main_tuning',(.196,-.071,.625),.043,.034,black,True)
for x in [.072,.325,.4]:cyl('75A3_control',(x,-.065,.593),.018,.022,black,True)
# Flatten only the two radio assemblies around their front edge and tabletop.
for obj in list(scene.objects):
    if obj.type=='MESH' and obj.name.startswith(('Collins_', '32V3_', '75A3_')):
        obj.location.y=-.035+(obj.location.y+.035)*.72
        obj.location.z=.525+(obj.location.z-.525)*.75
        obj.scale.y*=.72
        obj.scale.z*=.75
scene['radios_flattened']=True
# Single prominent desk microphone. Broad grille stripes survive sprite reduction.
cyl('Microphone_base',(.325,-.326,.54),.081,.035,black)
cyl('Microphone_stem',(.325,-.326,.61),.016,.13,steel)
mic=cube('Microphone_capsule',(.325,-.326,.726),(.09,.06,.135),steel)
bevel=mic.modifiers.new('Simple_rounded_capsule','BEVEL');bevel.width=.02;bevel.segments=1
for z in [.692,.719,.746,.773]:cube('Microphone_grille',(.325,-.359,z),(.063,.004,.009),black)
# One broad tuning note, deliberately without microtext.
cube('Operating_card',(-.22,-.31,.527),(.20,.16,.006),cream)
for y in [-.27,-.30,-.33]:cube('Card_mark',(-.23,y,.531),(.12,.006,.002),panel)
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
with open(os.path.join(OUT,'validation.json'),'w') as f:json.dump({'footprint':[1,1],'wood_rgb':[133,97,67],'color_mask':False,'cameras':checks},f,indent=2)
