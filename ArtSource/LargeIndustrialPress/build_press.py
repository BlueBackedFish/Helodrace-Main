"""New press variant. Run Blender --background --python build_press.py."""
import bpy
import math
import os
import json
import numpy as np
from mathutils import Vector

OUT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.world=bpy.data.worlds.new('Unlit')

def linear(c):
    c/=255
    return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4

def colors(name,values):
    mats=[]
    for suffix,value in zip(['top','front','side'],values):
        mat=bpy.data.materials.new(name+'_'+suffix)
        mat.diffuse_color=(linear(value),)*3+(1,)
        mat.use_nodes=True
        mat.node_tree.nodes.clear()
        emission=mat.node_tree.nodes.new('ShaderNodeEmission')
        emission.inputs['Color'].default_value=mat.diffuse_color
        output=mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
        mat.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
        mats.append(mat)
    return mats

body=colors('Cast_gray',[143,108,93])
base=colors('Base_gray',[174,133,113])
dark=colors('Working_charcoal',[64,49,43])
steel=colors('Ram_steel',[218,184,160])

def cube(name,center,size,mats):
    bpy.ops.mesh.primitive_cube_add(size=1,location=center)
    obj=bpy.context.object;obj.name=name;obj.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for mat in mats:obj.data.materials.append(mat)
    for face in obj.data.polygons:
        face.material_index=0 if face.normal.z>.5 else (1 if abs(face.normal.y)>.5 else 2)
    return obj

def cylinder(name,z,height,radius,mats):
    bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=radius,depth=height,location=(0,-.22,z))
    obj=bpy.context.object;obj.name=name
    for mat in mats:obj.data.materials.append(mat)
    for face in obj.data.polygons:
        face.material_index=0 if face.normal.z>.5 else 1
    return obj

# Entirely new geometry: compact H frame with an open, forward working zone.
cube('01_Heavy_foundation',(0,0,.15),(2.85,1.95,.30),base)
for x in [-1.01,1.01]:
    cube('02_Column_shoe',(x,.16,.40),(.69,1.37,.22),body)
    cube('03_Tall_side_column',(x,.30,1.81),(.48,.50,2.82),body)
cube('04_Upper_crosshead',(0,.10,3.10),(2.51,.46,.44),body)
cube('05_Central_ram_housing',(0,-.22,2.82),(.82,.42,.36),body)
cylinder('06_Upper_drive_cap',3.48,.32,.31,body)
cylinder('07_Exposed_bright_ram',2.32,.72,.24,steel)
cube('08_Moving_press_head',(0,-.22,1.88),(1.30,.86,.24),steel)
cube('09_Upper_dark_die',(0,-.22,1.72),(.91,.65,.10),dark)
cube('10_Lower_bolster',(0,-.22,.52),(1.64,1.34,.44),body)
cube('11_Working_platen',(0,-.22,.82),(1.83,1.49,.16),dark)
cube('12_Lower_die',(0,-.22,.97),(.78,.59,.14),steel)

# Same geometric asset is rendered in all directions: no per-view visibility.
bpy.context.view_layer.update()
target=Vector((0,0,1.78))
scale=4.9
for name,azimuth in [('North',90),('East',0),('South',270)]:
    angle=math.radians(azimuth);down=math.radians(64)
    data=bpy.data.cameras.new('Camera_'+name)
    data.type='ORTHO';data.ortho_scale=scale
    camera=bpy.data.objects.new('Camera_'+name,data);scene.collection.objects.link(camera)
    camera.location=target+10*Vector((math.cos(angle)*math.cos(down),math.sin(angle)*math.cos(down),math.sin(down)))
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
    scene.render.filepath=os.path.join(OUT,'large_industrial_press_'+name.lower()+'.png')
    bpy.ops.render.render(write_still=True)

scene.camera=bpy.data.objects['Camera_South']
scene['design']='New single-bay press variant inspired by LineShaftPress; no reused or traced geometry.'
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.color_type='MATERIAL'
            area.spaces.active.region_3d.view_perspective='CAMERA'
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'large_industrial_press.blend'))

# Verify the saved scene and the actual RGBA files, not only configuration.
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'large_industrial_press.blend'))
checks={}
for name in ['North','East','South']:
    camera=bpy.data.objects['Camera_'+name]
    direction=camera.rotation_euler.to_quaternion()@Vector((0,0,-1))
    actual=math.degrees(math.asin(-direction.z))
    assert abs(actual-64)<.001 and camera.data.type=='ORTHO'
    assert abs(camera.data.ortho_scale-scale)<1e-5
    img=bpy.data.images.load(os.path.join(OUT,'large_industrial_press_'+name.lower()+'.png'))
    buf=np.empty(512*512*4,dtype=np.float32);img.pixels.foreach_get(buf)
    alpha=buf.reshape(512,512,4)[:,:,3]
    y,x=np.where(alpha>.5)
    margin=min(int(x.min()),int(y.min()),511-int(x.max()),511-int(y.max()))
    assert margin>=20
    checks[name]={'actual_downward_degrees':actual,'minimum_margin_px':margin,'transparent_pixels':int((alpha==0).sum())}
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
with open(os.path.join(OUT,'manifest.json'),'w') as f:
    json.dump({'new_design':True,'reference_folder':'ArtSource/Reference/LineShaftPress',
               'mesh_objects':len(meshes),'vertices':sum(len(o.data.vertices) for o in meshes),
               'ortho_scale':scale,'target_center':list(target),'resolution':[512,512],
               'camera_positions':'North +Y; East +X; South -Y','validation':checks},f,indent=2)
