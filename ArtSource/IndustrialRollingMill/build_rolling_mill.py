"""Build a new four-roll sheet-metal mill; Blender 5.1 background compatible."""
import bpy
import math
import os
import json
import numpy as np
from mathutils import Vector

OUT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.world=bpy.data.worlds.new('Unlit_sprite_world')

# Match the press family's neutral cast metal, with lighting-independent values.
def linear(value):
    value/=255
    return value/12.92 if value<=.04045 else ((value+.055)/1.055)**2.4

def mat(name,rgb):
    if isinstance(rgb,int):rgb=(rgb,)*3
    material=bpy.data.materials.new(name)
    material.diffuse_color=tuple(linear(c) for c in rgb)+(1,)
    material.use_nodes=True
    material.node_tree.nodes.clear()
    emission=material.node_tree.nodes.new('ShaderNodeEmission')
    emission.inputs['Color'].default_value=material.diffuse_color
    output=material.node_tree.nodes.new('ShaderNodeOutputMaterial')
    material.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
    return material

body=[mat('Cast_top',143),mat('Cast_front',108),mat('Cast_side',93)]
base=[mat('Base_top',174),mat('Base_front',133),mat('Base_side',113)]
dark=[mat('Working_top',64),mat('Working_front',49),mat('Working_side',43)]
roll_mats=[mat('Roll_crown',218),mat('Roll_flank',184),mat('Roll_bottom',145),mat('Roll_end',132)]
sheet_mats=[mat('Sheet_top',(156,163,166)),mat('Sheet_edge',112),mat('Sheet_side',101)]

def cube(name,position,size,materials):
    bpy.ops.mesh.primitive_cube_add(size=1,location=position)
    obj=bpy.context.object;obj.name=name;obj.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for material in materials:obj.data.materials.append(material)
    for face in obj.data.polygons:
        face.material_index=0 if face.normal.z>.5 else (1 if abs(face.normal.y)>.5 else 2)
    return obj

def roller(name,y,z,radius):
    bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=radius,depth=2.38,location=(0,y,z))
    obj=bpy.context.object;obj.name=name
    obj.rotation_euler.y=math.pi/2
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=False)
    for material in roll_mats:obj.data.materials.append(material)
    for face in obj.data.polygons:
        face.material_index=3 if abs(face.normal.x)>.9 else (0 if face.normal.z>.65 else (1 if face.normal.z>-.2 else 2))
    obj['role']='Prominent cylindrical roller, shaft axis X; sheet feed axis Y'
    return obj

# Heavy low bed and two side castings leave the rollers exposed from above.
cube('01_Heavy_foundation',(0,0,.145),(3.25,3.15,.29),base)
for x,label in [(-1.025,'Left'),(1.025,'Right')]:
    cube('02_'+label+'_side_casting',(x,0,.565),(.30,2.48,.55),body)
    for y in [-.69,.69]:
        cube('03_'+label+'_frame_post',(x,y,1.245),(.19,.18,1.01),body)
    cube('04_'+label+'_upper_rail',(x,0,1.795),(.19,1.56,.16),body)
cube('05_Rear_upper_tie',(0,.69,1.795),(2.24,.16,.16),body)

# Two large upper working rolls and two smaller lower entry/exit guide rolls.
# Roll ends project past the side frames, preserving circular faces in East.
roller('06_Entry_guide_roller',-1.06,.87,.22)
roller('07_First_working_roller',-.36,1.395,.30)
roller('08_Second_working_roller',.36,1.395,.30)
roller('09_Exit_guide_roller',1.06,.87,.22)

for y,label in [(-1.45,'Entry'),(1.45,'Exit')]:
    cube('10_'+label+'_feed_lip',(0,y,1.015),(1.68,.42,.14),dark)
    cube('11_'+label+'_table_pedestal',(0,y,.65),(1.18,.30,.67),body)
# One broad sheet is enough to establish a feed direction without surface detail.
cube('12_Sheet_steel_workpiece',(0,-.55,1.092),(1.32,2.20,.018),sheet_mats)
cube('13_Enclosed_side_drive',(1.48,0,.69),(.45,.91,.78),body)

# Three fixed camera positions share a single target and projection scale.
target=Vector((0,0,1.00));scale=4.65
for name,azimuth in [('North',90),('East',0),('South',270)]:
    angle=math.radians(azimuth);elevation=math.radians(64)
    data=bpy.data.cameras.new('Camera_'+name);data.type='ORTHO';data.ortho_scale=scale
    camera=bpy.data.objects.new('Camera_'+name,data);scene.collection.objects.link(camera)
    camera.location=target+10*Vector((math.cos(angle)*math.cos(elevation),math.sin(angle)*math.cos(elevation),math.sin(elevation)))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    camera['downward_degrees']=64;camera['target_center']=list(target)

scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=scene.render.resolution_y=512
scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
scene.view_settings.exposure=0;scene.view_settings.gamma=1
for name in ['North','East','South']:
    scene.camera=bpy.data.objects['Camera_'+name]
    scene.render.filepath=os.path.join(OUT,'industrial_rolling_mill_'+name.lower()+'.png')
    bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['Camera_South']
scene['design']='New four-roll steel plate mill: two working rolls plus entry/exit guides.'
scene['feed_direction']='Along Y, from -Y entry to +Y exit'
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.color_type='MATERIAL'
            area.spaces.active.region_3d.view_perspective='CAMERA'
bpy.context.preferences.filepaths.save_version=0
blend=os.path.join(OUT,'industrial_rolling_mill.blend')
bpy.ops.wm.save_as_mainfile(filepath=blend)

# Reopen the actual deliverable and verify cameras, framing and RGBA output.
bpy.ops.wm.open_mainfile(filepath=blend)
checks={};preview=[]
for name in ['North','East','South']:
    camera=bpy.data.objects['Camera_'+name]
    forward=camera.rotation_euler.to_quaternion()@Vector((0,0,-1))
    angle=math.degrees(math.asin(-forward.z))
    assert abs(angle-64)<.001 and camera.data.type=='ORTHO'
    assert abs(camera.data.ortho_scale-scale)<1e-5
    assert (Vector(camera['target_center'])-target).length<1e-5
    image=bpy.data.images.load(os.path.join(OUT,'industrial_rolling_mill_'+name.lower()+'.png'))
    assert tuple(image.size)==(512,512)
    image.colorspace_settings.name='Non-Color'
    data=np.empty(512*512*4,dtype=np.float32);image.pixels.foreach_get(data)
    data=data.reshape(512,512,4)
    y,x=np.where(data[:,:,3]>.5)
    margin=min(int(x.min()),int(y.min()),511-int(x.max()),511-int(y.max()))
    assert margin>=24
    checks[name]={'actual_downward_degrees':angle,'minimum_margin_px':margin,'transparent_pixels':int((data[:,:,3]==0).sum())}
    preview.append(data)
combined=np.concatenate(preview,axis=1)
image=bpy.data.images.new('All_three_views',width=1536,height=512,alpha=True)
image.colorspace_settings.name='Non-Color';image.pixels.foreach_set(combined.ravel())
image.filepath_raw=os.path.join(OUT,'preview_north_east_south.png');image.file_format='PNG';image.save()
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
with open(os.path.join(OUT,'manifest.json'),'w') as f:
    json.dump({'new_asset':True,'prominent_rollers':4,'mesh_objects':len(meshes),
               'vertices':sum(len(o.data.vertices) for o in meshes),'orthographic_scale':scale,
               'target_center':list(target),'resolution':[512,512],'validation':checks},f,indent=2)
