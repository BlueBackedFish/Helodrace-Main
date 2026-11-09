"""Build the supplied F-22 with real control joints and render an unlit top sprite."""
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
paint_path=OUT/'fuselage_main_paint.png'
assert paint_path.exists(),'Run prepare_paint.py before rendering'
image=bpy.data.images.load(str(paint_path),check_existing=True);image.colorspace_settings.name='sRGB'
camo=bpy.data.materials.new('F22 photo-referenced native camouflage');camo.use_nodes=True;camo.node_tree.nodes.clear()
nodes=camo.node_tree.nodes;tex=nodes.new('ShaderNodeTexImage');tex.image=image;tex.interpolation='Linear'
emit=nodes.new('ShaderNodeEmission');output=nodes.new('ShaderNodeOutputMaterial')
links=camo.node_tree.links;coordinates=nodes.new('ShaderNodeAttribute');coordinates.attribute_name='F22 native paint'
links.new(coordinates.outputs['Vector'],tex.inputs['Vector'])
links.new(tex.outputs['Color'],emit.inputs[0]);links.new(emit.outputs[0],output.inputs['Surface'])
camo_variants={}
for role in ['tail','fin']:
    material=camo.copy();material.name='F22 '+role+' photo-referenced camouflage'
    variant_image=bpy.data.images.load(str(OUT/('fuselage_'+role+'_paint.png')),check_existing=True)
    variant_image.colorspace_settings.name='sRGB';variant_image.pack()
    next(n for n in material.node_tree.nodes if n.type=='TEX_IMAGE').image=variant_image
    camo_variants[role]=material


def paint(obj,original):
    if obj.name.startswith('Nozzle'):
        if obj.name=='Nozzle.001':return colors['nozzle_inside']
        return colors['nozzle_lip'] if obj.name in ['Nozzle.003','Nozzle.004','Nozzle.007','Nozzle.008'] else colors['nozzle']
    if obj.name=='Cockpit.001':return colors['glass'] if original=='F22 Cockpit' else colors['glass_frame']
    if original=='F22 Main Fuselage':
        if obj.name in ['Fuselage.013','Fuselage.015']:return camo_variants['tail']
        if obj.name in ['Fuselage.005','Fuselage.007']:return camo_variants['fin']
        return camo
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
sys.path.insert(0,str(ROOT))
from airframe import paint_coordinates,prepare as prepare_airframe
paint_plan=json.loads((ROOT/'paint.json').read_text())
for obj in objects:
    if any(mat in obj.data.materials[:] for mat in [camo]+list(camo_variants.values())):
        paint_coordinates(obj,paint_plan['bounds_xy'],origin.matrix_world.inverted()@obj.matrix_world)
source_faces=sum(len(o.data.polygons) for o in objects)
span=max(hi.x-lo.x,hi.y-lo.y);size=STYLE['delivery_resolution'];padding=STYLE['outline_pixels']+STYLE['transparent_margin_pixels']
scale=span/(1-2*padding/size)
sys.path.insert(0,str(ROOT))
from articulation import prepare
joint=m.material('F22 physical joint',json.loads((ROOT/'articulation.json').read_text())['joint_rgb'])
articulation=prepare(scene,origin,scale,joint)
from canopy import prepare as prepare_canopy
canopy=prepare_canopy(scene,STYLE['canopy_subdivision_levels'])
airframe=prepare_airframe(scene,origin,dict(colors,camo=camo,joint=joint),paint_plan['bounds_xy'])
bpy.context.view_layer.update()
# Capture paint after neutralizing the source control deflections. It then
# aligns across the fixed wing/control seam and follows each moving mesh.
for obj in scene.objects:
    if obj.type=='MESH' and obj.data.attributes.get('F22 native paint') is not None:
        paint_coordinates(obj,paint_plan['bounds_xy'],origin.matrix_world.inverted()@obj.matrix_world)
edited={item['object'] for item in articulation['edited_source_objects']}
edited.add(canopy['object'])
edited.update(airframe['edited_source_objects'])
assert all(geometry_signature(scene.objects[name])==signature for name,signature in before.items() if name not in edited)
objects=[o for o in scene.objects if o.type=='MESH']
image.pack()
bpy.data.orphans_purge(do_recursive=True)
data=bpy.data.cameras.new('F22 exact top');data.type='ORTHO';data.ortho_scale=scale
camera=bpy.data.objects.new('F22 exact top',data);scene.collection.objects.link(camera)
camera.location=(0,0,hi.z-lo.z+10);camera.rotation_euler=(0,0,0);scene.camera=camera
m.save(OUT/'f22_raptor.blend');m.render(OUT/'top_plain_2048.png',camera,STYLE['master_resolution'])
assert not any(o.type=='LIGHT' for o in scene.objects)
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==source_hash
report=dict(passed=True,source=str(SOURCE),source_sha256=source_hash,pose_frame=STYLE['pose_frame'],
    source_meshes=len(before),source_faces=source_faces,omitted_meshes=0,
    final_meshes=len(objects),final_faces=sum(len(o.data.polygons) for o in objects),
    untouched_source_meshes_preserved=len(before)-len(edited),
    source_rig_retained_and_actions_archived=True,articulation=articulation,canopy=canopy,airframe=airframe,
    pose_note='Gear/canopy/bays frozen from source frame 650; controls neutralized; named hinge demo at frames 1..60',
    source_forward='+X',source_up='+Z',source_pose_bounds=[list(lo),list(hi)],root_transform=[list(r) for r in transform],
    orthographic_camera_rotation=[0,0,0],ortho_scale=scale,pivot_normalized=[.5,.5],
    master_resolution=STYLE['master_resolution'],delivery_resolution=size,draw_size_tiles=None,
    source_albedo_sha256=hashlib.sha256(image_path.read_bytes()).hexdigest(),
    paint_profile='paint.json',paint_coordinates='Native XY captured in neutral frame 1; source UV layers retained',
    source_albedo_pattern_replaced=True,
    lighting='Emission only; photo-referenced two-tone gray camouflage and RAM edge bands, no lights/AO/normal/reflection',
    material_roles=dict(Counter(mat.name for o in objects for mat in o.data.materials)),objects=inventory,
    game_definition_added=False,in_game_tested=False,blueprint_validation_performed=False)
(OUT/'model_validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
(OUT/'articulation.json').write_text(json.dumps(articulation,indent=2)+'\n',encoding='utf-8')
print('F22_TOP_MODEL_PASS:',len(objects),'meshes;',len(articulation['controllers']),'working controls; model joints, brighter fins and exact top projection')
