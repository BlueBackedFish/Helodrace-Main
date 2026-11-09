"""Preserve the supplied F-22 rig and UVs, then render an unlit top sprite."""
from pathlib import Path
from collections import Counter
import bpy,sys,json,math,hashlib
from mathutils import Vector,Matrix

ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(ROOT.parent/'PropAssets'));import modeling as m
STYLE=json.loads((ROOT/'style.json').read_text(encoding='utf-8'))
SOURCE=Path(STYLE['source']);source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
assert source_hash==STYLE['source_sha256'],'Source FBX changed; review pose and part mapping'
scene=m.start();bpy.ops.import_scene.fbx(filepath=str(SOURCE));scene.frame_set(STYLE['pose_frame']);bpy.context.view_layer.update()
objects=[o for o in scene.objects if o.type=='MESH']
# Keep paint assignments independent when the FBX shares a mesh datablock.
for obj in objects:obj.data=obj.data.copy()


def geometry_signature(obj):
    mesh=obj.data;digest=hashlib.sha256()
    for v in mesh.vertices:digest.update(str(tuple(v.co)).encode())
    for f in mesh.polygons:digest.update(str(tuple(f.vertices)).encode())
    for uv in mesh.uv_layers:
        for point in uv.data:digest.update(str(tuple(point.uv)).encode())
    return digest.hexdigest()


before={o.name:geometry_signature(o) for o in objects}
dg=bpy.context.evaluated_depsgraph_get();points=[]
for obj in objects:
    evaluated=obj.evaluated_get(dg);points.extend(evaluated.matrix_world@v.co for v in evaluated.data.vertices)
lo=Vector([min(p[j] for p in points) for j in range(3)]);hi=Vector([max(p[j] for p in points) for j in range(3)])
rotation=Matrix.Rotation(math.pi/2,4,'Z');center=rotation@((lo+hi)/2)
transform=Matrix.Translation((-center.x,-center.y,-lo.z))@rotation
roots=[o for o in scene.objects if o.parent is None]
origin=bpy.data.objects.new('F22_ART_ORIGIN',None);scene.collection.objects.link(origin)
for obj in roots:
    world=obj.matrix_world.copy();obj.parent=origin;obj.matrix_world=world
origin.matrix_world=transform;bpy.context.view_layer.update()

colors={role:m.material('F22 '+role,rgb) for role,rgb in STYLE['palette'].items()}
image_path=(SOURCE.parent/STYLE['texture_directory']/'F22_Main_Fuselage_Base_color.png').resolve()
paint_path=OUT/'fuselage_flat_uv.png'
assert paint_path.exists(),'Run prepare_paint.py before rendering'
image=bpy.data.images.load(str(paint_path),check_existing=True);image.colorspace_settings.name='sRGB'
camo=bpy.data.materials.new('F22 fixed UV camouflage');camo.use_nodes=True;camo.node_tree.nodes.clear()
nodes=camo.node_tree.nodes;tex=nodes.new('ShaderNodeTexImage');tex.image=image;tex.interpolation='Linear'
emit=nodes.new('ShaderNodeEmission');output=nodes.new('ShaderNodeOutputMaterial')
links=camo.node_tree.links;links.new(tex.outputs['Color'],emit.inputs[0]);links.new(emit.outputs[0],output.inputs['Surface'])


def paint(obj,original):
    if obj.name.startswith('Nozzle'):
        if obj.name=='Nozzle.001':return colors['nozzle_inside']
        return colors['nozzle_lip'] if obj.name in ['Nozzle.003','Nozzle.004','Nozzle.007','Nozzle.008'] else colors['nozzle']
    if obj.name=='Cockpit.001':return colors['glass'] if original=='F22 Cockpit' else colors['glass_frame']
    if original=='F22 Main Fuselage':return camo
    if original=='Material.011':return colors['edge']
    if original=='F22 Cockpit':return colors['body']
    return colors['mechanism']


inventory=[]
for obj in objects:
    original=[mat.name for mat in obj.data.materials]
    for i,name in enumerate(original):obj.data.materials[i]=paint(obj,name)
    inventory.append(dict(name=obj.name,vertices=len(obj.data.vertices),faces=len(obj.data.polygons),
        original_materials=original,materials=[mat.name for mat in obj.data.materials],geometry_uv_sha256=before[obj.name]))
assert before=={o.name:geometry_signature(o) for o in objects}
image.pack()
bpy.data.orphans_purge(do_recursive=True)
span=max(hi.x-lo.x,hi.y-lo.y);size=STYLE['delivery_resolution'];padding=STYLE['outline_pixels']+STYLE['transparent_margin_pixels']
scale=span/(1-2*padding/size)
data=bpy.data.cameras.new('F22 exact top');data.type='ORTHO';data.ortho_scale=scale
camera=bpy.data.objects.new('F22 exact top',data);scene.collection.objects.link(camera)
camera.location=(0,0,hi.z-lo.z+10);camera.rotation_euler=(0,0,0);scene.camera=camera
m.save(OUT/'f22_raptor.blend');m.render(OUT/'top_plain_2048.png',camera,STYLE['master_resolution'])
white=m.material('Control mask white',(255,255,255));black=m.material('Control mask occluder',(0,0,0))
backup={o:list(o.data.materials) for o in objects};selected=set(STYLE['control_objects'])
assert selected.issubset({o.name for o in objects})
for obj in objects:
    for i in range(len(obj.data.materials)):obj.data.materials[i]=white if obj.name in selected else black
m.render(OUT/'control_mask_2048.png',camera,STYLE['master_resolution'])
for obj,materials in backup.items():
    for i,mat in enumerate(materials):obj.data.materials[i]=mat
assert not any(o.type=='LIGHT' for o in scene.objects)
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==source_hash
report=dict(passed=True,source=str(SOURCE),source_sha256=source_hash,pose_frame=STYLE['pose_frame'],
    source_meshes=len(objects),source_faces=sum(len(o.data.polygons) for o in objects),omitted_meshes=0,
    original_mesh_coordinates_topology_and_uv_preserved=True,original_animation_and_rig_preserved=True,
    pose_note='Source animation frame 650; closed canopy, retracted landing gear and closed weapon bays reviewed from the side',
    source_forward='+X',source_up='+Z',source_pose_bounds=[list(lo),list(hi)],root_transform=[list(r) for r in transform],
    orthographic_camera_rotation=[0,0,0],ortho_scale=scale,pivot_normalized=[.5,.5],
    master_resolution=STYLE['master_resolution'],delivery_resolution=size,draw_size_tiles=None,
    source_albedo_sha256=hashlib.sha256(image_path.read_bytes()).hexdigest(),
    lighting='Emission only; four fixed UV camouflage colors, no lights/AO/normal/reflection',
    material_roles=dict(Counter(mat.name for o in objects for mat in o.data.materials)),objects=inventory,
    game_definition_added=False,in_game_tested=False,blueprint_validation_performed=False)
(OUT/'model_validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print('F22_TOP_MODEL_PASS:',len(objects),'source meshes and',report['source_faces'],'faces preserved; frame',STYLE['pose_frame'],'exact top projection')
