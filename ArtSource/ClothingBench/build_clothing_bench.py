"""Simple 3x1 tailoring sprite proxy. Blender --background --python this_file.py"""
import bpy
import math
import os
import json
import numpy as np
from mathutils import Vector, Matrix

OUT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.world=bpy.data.worlds.new('Unlit_sprite_world')
groups={}

# Materials: flat emission, with broad baked face values instead of lighting.
def linear(c):
    c/=255
    return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4

def palette(name,rgb):
    materials=[]
    for label,factor in [('top',1),('front',.83),('side',.68)]:
        material=bpy.data.materials.new(name+'_'+label)
        material.diffuse_color=tuple(linear(c*factor) for c in rgb)+(1,)
        material.use_nodes=True
        material.node_tree.nodes.clear()
        emission=material.node_tree.nodes.new('ShaderNodeEmission')
        emission.inputs['Color'].default_value=material.diffuse_color
        output=material.node_tree.nodes.new('ShaderNodeOutputMaterial')
        material.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
        materials.append(material)
    return materials

gray=palette('Cast_gray',(143,148,148))
light=palette('Worktop_gray',(184,188,187))
dark=palette('Charcoal',(63,69,70))
cyan=palette('Dark_cyan',(44,90,101))
tan=palette('Muted_canvas',(174,155,123))
sage=palette('Muted_fabric',(113,130,116))

def register(obj,group,materials):
    for material in materials:obj.data.materials.append(material)
    for face in obj.data.polygons:
        face.material_index=0 if face.normal.z>.5 else (1 if abs(face.normal.y)>.5 else 2)
    groups.setdefault(group,[]).append(obj)
    return obj

def box(name,position,size,materials,group):
    bpy.ops.mesh.primitive_cube_add(size=1,location=position)
    obj=bpy.context.object;obj.name=name;obj.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return register(obj,group,materials)

def mesh(name,verts,faces,materials,group):
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj)
    return register(obj,group,materials)

# Main bench and under-table storage. One unit approximates one tile.
box('Broad_worktop',(0,0,.87),(3,1,.16),light,'01_Worktable')
box('Sewing_end_support',(-1.25,0,.42),(.18,.82,.76),gray,'02_Support_frame')
box('Low_crossbrace',(-.08,.28,.22),(2.25,.13,.16),dark,'02_Support_frame')
box('Supply_cabinet',(1.02,0,.42),(.72,.83,.76),gray,'03_Storage_cabinet')
for i in range(3):
    box('Supply_drawer_'+str(i+1),(1.02,-.431,.22+i*.23),(.60,.035,.17),cyan,'04_Drawer_fronts')

# Sewing machine: a chunky open C profile, bed and oversized handwheel only.
machine_center=Vector((-.94,.035,.99))
turn=Matrix.Rotation(math.radians(25),4,'Z')
machine_parts=[]
machine_parts.append(box('Sewing_machine_bed',(0,0,0),(.84,.45,.07),dark,'05_Sewing_machine'))
profile=[(.30,0),(.30,.56),(-.33,.56),(-.40,.49),(-.40,.27),
         (-.25,.27),(-.25,.40),(.10,.40),(.10,0)]
n=len(profile)
verts=[(x,y,z) for y in [-.12,.12] for x,z in profile]
faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
machine_parts.append(mesh('Open_arm_sewing_machine',verts,faces,cyan,'05_Sewing_machine'))
bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=.145,depth=.09,location=(.345,0,.30))
wheel=bpy.context.object;wheel.name='Large_handwheel';wheel.rotation_euler.y=math.pi/2
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
register(wheel,'05_Sewing_machine',light);machine_parts.append(wheel)
for obj in machine_parts:
    obj.matrix_world=Matrix.Translation(machine_center)@turn@obj.matrix_world

# Large cutting zone. An irregular blank represents generic fabric, not a
# specific garment type. Flat cloth geometry is intentional sprite proxy work.
box('Cutting_mat',(.13,0,.958),(1.08,.79,.014),dark,'06_Cutting_surface')
cloth=[(-.43,-.28),(.39,-.28),(.39,-.04),(.25,-.04),(.25,.27),
       (-.34,.27),(-.34,.14),(-.43,.14)]
mesh('Partly_cut_canvas',[(x+.13,y,.970) for x,y in cloth],[tuple(range(8))],tan,'07_Cut_fabric')

# One fabric roll and a two-layer folded stack. No folds or fine fabric texture.
bpy.ops.mesh.primitive_cylinder_add(vertices=10,radius=.125,depth=.44,location=(1.025,.21,1.079))
roll=bpy.context.object;roll.name='Fabric_roll';roll.rotation_euler.x=math.pi/2
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
register(roll,'09_Fabric_roll',sage)
for y in [-.012,.432]:
    pts=[(1.025+math.cos(i*math.tau/8)*.034,y,1.079+math.sin(i*math.tau/8)*.034) for i in range(8)]
    mesh('Roll_core_end',pts,[tuple(range(8))],dark,'09_Fabric_roll')
box('Folded_canvas',(1.03,-.26,.991),(.59,.32,.072),tan,'10_Folded_fabric')
box('Folded_sage_cloth',(1.05,-.26,1.046),(.49,.28,.038),sage,'10_Folded_fabric')

# Oversized scissors: two open octagonal handles and two simple blade triangles.
scissor_turn=Matrix.Rotation(math.radians(27),4,'Z')
def scissors_point(x,y,z=.981):
    return Vector((.21,-.13,z))+scissor_turn@Vector((x,y,0))
for y in [-.085,.085]:
    verts=[scissors_point(-.13+math.cos(i*math.tau/8)*r,y+math.sin(i*math.tau/8)*r) for r in [.078,.042] for i in range(8)]
    faces=[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
    mesh('Scissor_handle',verts,faces,dark,'11_Large_scissors')
for sign in [-1,1]:
    mesh('Scissor_blade',[scissors_point(-.075,sign*.064),scissors_point(.24,-sign*.075),scissors_point(.015,-sign*.045)],[(0,1,2)],gray,'11_Large_scissors')

# Logical grouping preserves simple editable geometry without modifiers.
for name,objects in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    if len(objects)>1:bpy.ops.object.join()
    bpy.context.object.name=name

# Apply the same in-place revision on a full procedural rebuild.
import runpy
revision=runpy.run_path(os.path.join(OUT,'revise_clothing_bench.py'),run_name='revision_library')
revision['revise_scene']()
revision['improve_front_visibility']()
runpy.run_path(os.path.join(OUT,'correct_pegboard.py'),run_name='pegboard_library')['correct_pegboard']()

# Current render setting: 64-degree downward views.
target=Vector((0,0,.79));scale=3.65
for name,azimuth in [('North',90),('South',270),('East',0)]:
    a=math.radians(azimuth);e=math.radians(64)
    data=bpy.data.cameras.new('Camera_'+name);data.type='ORTHO';data.ortho_scale=scale
    camera=bpy.data.objects.new('Camera_'+name,data);scene.collection.objects.link(camera)
    camera.location=target+8*Vector((math.cos(a)*math.cos(e),math.sin(a)*math.cos(e),math.sin(e)))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    camera['downward_degrees']=64;camera['target_center']=list(target)

# Rendering and export: transparent RGBA, flat emission, no lights or ground.
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=scene.render.resolution_y=512
scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
scene.view_settings.exposure=0;scene.view_settings.gamma=1
for name in ['North','South','East']:
    scene.camera=bpy.data.objects['Camera_'+name]
    scene.render.filepath=os.path.join(OUT,'clothing_bench_'+name.lower()+'.png')
    bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['Camera_South']
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.color_type='MATERIAL'
            area.spaces.active.region_3d.view_perspective='CAMERA'
bpy.context.preferences.filepaths.save_version=0
path=os.path.join(OUT,'clothing_bench.blend')
bpy.ops.wm.save_as_mainfile(filepath=path)

# Check the saved deliverables and prepare a N/S/E preview without resizing views.
bpy.ops.wm.open_mainfile(filepath=path)
checks={};preview=[]
for name in ['North','South','East']:
    camera=bpy.data.objects['Camera_'+name]
    forward=camera.rotation_euler.to_quaternion()@Vector((0,0,-1))
    angle=math.degrees(math.asin(-forward.z))
    assert abs(angle-64)<.001 and camera.data.type=='ORTHO'
    assert abs(camera.data.ortho_scale-scale)<1e-5
    assert (Vector(camera['target_center'])-target).length<1e-5
    image=bpy.data.images.load(os.path.join(OUT,'clothing_bench_'+name.lower()+'.png'))
    assert tuple(image.size)==(512,512)
    image.colorspace_settings.name='Non-Color'
    values=np.empty(512*512*4,dtype=np.float32);image.pixels.foreach_get(values)
    values=values.reshape(512,512,4)
    y,x=np.where(values[:,:,3]>.5)
    margin=min(int(x.min()),int(y.min()),511-int(x.max()),511-int(y.max()))
    assert margin>=24
    checks[name]={'actual_downward_degrees':angle,'minimum_margin_px':margin,'transparent_pixels':int((values[:,:,3]==0).sum())}
    preview.append(values)
image=bpy.data.images.new('Three_view_preview',width=1536,height=512,alpha=True)
image.colorspace_settings.name='Non-Color'
image.pixels.foreach_set(np.concatenate(preview,axis=1).ravel())
image.filepath_raw=os.path.join(OUT,'preview_north_south_east.png');image.file_format='PNG';image.save()
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
count=sum(len(o.data.vertices) for o in meshes)
# The explicitly requested through-hole board adds a small regular mesh grid.
assert len(meshes)<=16 and count<=420
with open(os.path.join(OUT,'manifest.json'),'w') as f:
    json.dump({'blender_version':bpy.app.version_string,'footprint':[3,1],
               'mesh_components':len(meshes),'vertices':count,'resolution':[512,512],
               'ortho_scale':scale,'target_center':list(target),'validation':checks},f,indent=2)
print(json.dumps({'components':len(meshes),'vertices':count,'camera_and_png_checks':checks}),flush=True)
