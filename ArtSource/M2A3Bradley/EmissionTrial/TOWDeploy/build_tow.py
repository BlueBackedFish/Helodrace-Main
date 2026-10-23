"""Split only the fixed launcher bracket and add a local TOW deployment hinge."""
import bpy,json,sys,hashlib,os,tempfile
from math import radians
from mathutils import Matrix,Vector
from pathlib import Path
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent;ROOT=TRIAL.parent;sys.path.insert(0,str(ROOT))
from mesh_regions import regions
cfg=json.loads((HERE/'settings.json').read_text(encoding='utf-8-sig'))
source=TRIAL/cfg['source'];source_sha=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source));s=bpy.context.scene;s.frame_set(1)
original_meshes=[o for o in s.objects if o.type=='MESH']
source_faces=sum(len(o.data.polygons) for o in original_meshes)
full_meshes=len(original_meshes)+1
def faces_world(obj):
    return [[tuple(obj.matrix_world@obj.data.vertices[v].co) for v in p.vertices] for p in obj.data.polygons]
baseline={o.name:{'faces':faces_world(o),'colors':[tuple(o.data.materials[p.material_index].diffuse_color) for p in o.data.polygons],
    'parent':o.parent.name,'matrix':[list(row) for row in o.matrix_world]} for o in original_meshes}
original=bpy.data.objects[cfg['split_source']];groups=regions(original.data)
fixed=set()
for seed in cfg['fixed_component_seed_faces']:
    group=next(g for g in groups if seed in g);fixed.update(group)
assert len(fixed)==142,len(fixed)
keep=sorted(set(range(len(original.data.polygons)))-fixed);fixed=sorted(fixed)
def subset(obj,ids,label):
    old=obj.data;used=sorted({v for i in ids for v in old.polygons[i].vertices});mapping={v:i for i,v in enumerate(used)}
    mesh=bpy.data.meshes.new(label);mesh.from_pydata([old.vertices[v].co[:] for v in used],[],[[mapping[v] for v in old.polygons[i].vertices] for i in ids]);mesh.update()
    for m in old.materials:mesh.materials.append(m)
    for p,i in zip(mesh.polygons,ids):p.material_index=old.polygons[i].material_index;p.use_smooth=old.polygons[i].use_smooth
    return mesh
bracket_mesh=subset(original,fixed,'TOW fixed bracket extracted')
bracket=bpy.data.objects.new(cfg['fixed_bracket_object'],bracket_mesh);s.collection.objects.link(bracket)
bracket.parent=original.parent;bracket.matrix_parent_inverse=original.matrix_parent_inverse.copy();bracket.matrix_world=original.matrix_world.copy();bracket['art_role']='tow_fixed_bracket'
original.data=subset(original,keep,'TOW moving box without fixed bracket')
parent=bpy.data.objects['TURRET_YAW'];pivot=bpy.data.objects.new(cfg['control'],None);s.collection.objects.link(pivot)
pivot.empty_display_type='ARROWS';pivot.empty_display_size=.18
pivot.parent=parent;pivot.matrix_parent_inverse=parent.matrix_world.inverted();pivot.matrix_world=Matrix.Translation(Vector(cfg['pivot_world']));bpy.context.view_layer.update()
for name in cfg['moving_objects']:
    obj=bpy.data.objects[name];world=obj.matrix_world.copy();obj.parent=pivot;obj.matrix_parent_inverse=pivot.matrix_world.inverted();obj.matrix_world=world
pivot.rotation_mode='XYZ'
for frame,angle in cfg['keyframes']:
    pivot.rotation_euler[1]=radians(angle);pivot.keyframe_insert(data_path='rotation_euler',index=1,frame=frame,group='TOW deploy')
pivot.animation_data.action.name='TOW_DEPLOY_OPEN_CLOSE'
pivot['usage']='Local Y: 0 degrees stowed; -90 degrees deployed. Only WT_roll_05 moves.'
pivot['front_cover']='WT_roll_04 front protective plate stays fixed to the turret.'
s.frame_set(1);bpy.context.view_layer.update()
# Save exact polygon provenance, not just a face count assertion.
for name,record in baseline.items():
    obj=bpy.data.objects[name]
    if name==cfg['split_source']:continue
    assert faces_world(obj)==record['faces'],('Stowed geometry moved',name)
for obj,ids in [(original,keep),(bracket,fixed)]:
    assert faces_world(obj)==[baseline[cfg['split_source']]['faces'][i] for i in ids]
    assert [tuple(obj.data.materials[p.material_index].diffuse_color) for p in obj.data.polygons]==[baseline[cfg['split_source']]['colors'][i] for i in ids]
assert sum(len(o.data.polygons) for o in s.objects if o.type=='MESH')==source_faces
members=cfg['moving_objects']+cfg['static_objects']+[bracket.name,pivot.name]
collection=bpy.data.collections.new('TOW launcher independent assembly');s.collection.children.link(collection)
for name in members:
    obj=bpy.data.objects[name]
    for old in list(obj.users_collection):old.objects.unlink(obj)
    collection.objects.link(obj)
s['tow_deployment']='Independent simplified hinge; WT_roll_04 front protective plate fixed.'
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'bradley_tow_deploy.blend'),compress=True)
def export_unlit(path):
    for m in {m.name:m for o in bpy.context.scene.objects if o.type=='MESH' for m in o.data.materials if m}.values():
        nodes=m.node_tree.nodes;nodes.clear();color=nodes.new('ShaderNodeRGB');color.outputs[0].default_value=m.diffuse_color
        output=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(color.outputs[0],output.inputs['Surface'])
    for obj in bpy.context.scene.objects:
        if obj.type=='MESH':
            for color in list(obj.data.color_attributes):obj.data.color_attributes.remove(color)
    with tempfile.TemporaryDirectory(prefix='.export-',dir=HERE) as staging:
        tmp=Path(staging)/path.name;bpy.ops.export_scene.gltf(filepath=str(tmp),export_format='GLB',export_animations=True,export_cameras=False,export_lights=False)
        os.replace(tmp,path)
export_unlit(HERE/'bradley_tow_deploy.glb')
# A second editable file contains only the launcher, with the hinge at the origin.
bpy.ops.wm.open_mainfile(filepath=str(HERE/'bradley_tow_deploy.blend'));s=bpy.context.scene;s.frame_set(1)
root=bpy.data.objects.new('TOW_ROOT',None);s.collection.objects.link(root);root.matrix_world=Matrix.Translation(Vector(cfg['pivot_world']));bpy.context.view_layer.update()
for name in cfg['static_objects']+[cfg['fixed_bracket_object'],cfg['control']]:
    obj=bpy.data.objects[name];world=obj.matrix_world.copy();obj.parent=root;obj.matrix_parent_inverse=root.matrix_world.inverted();obj.matrix_world=world
for obj in list(s.objects):
    if obj.name not in set(members)|{'TOW_ROOT'}:bpy.data.objects.remove(obj,do_unlink=True)
root.location=(0,0,0);bpy.context.view_layer.update()
for action in list(bpy.data.actions):
    if action.users==0:bpy.data.actions.remove(action)
data=bpy.data.cameras.new('TOW inspection camera');data.type='ORTHO';data.ortho_scale=1.8
cam=bpy.data.objects.new('TOW inspection camera',data);s.collection.objects.link(cam);center=Vector((.1,.15,0))
cam.location=center+Vector((3,-4,2.8));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();s.camera=cam
s.render.resolution_x=s.render.resolution_y=768;s.render.film_transparent=True
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'tow_launcher.blend'),compress=True)
export_unlit(HERE/'tow_launcher.glb')
manifest={'source_sha256':source_sha,'source':cfg['source'],'config':cfg,'source_faces':source_faces,'full_meshes':full_meshes,
    'moving_faces':len(keep),'fixed_bracket_faces':len(fixed),'split_face_provenance':{'WT_roll_05':keep,cfg['fixed_bracket_object']:fixed},
    'stowed_world_geometry_and_materials_unchanged':True,'other_parenting_unchanged':True,'full_animations':5,'standalone_animations':1,
    'full_model':'bradley_tow_deploy.blend','standalone_model':'tow_launcher.blend','standalone_hinge_at_origin':True,'in_game_validation':'not_performed'}
(HERE/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('TOW DEPLOYMENT MODELS BUILT',len(keep),'moving faces,',len(fixed),'fixed bracket faces')
