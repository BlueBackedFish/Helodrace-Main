"""Preserve source proportions, repair requested parts and apply height-led paint."""
import bpy,json,hashlib,sys,tempfile,os
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'RimWorld'
cfg=json.loads((ROOT/'part_contrast.json').read_text());roles=json.loads((ROOT/cfg['role_registry']).read_text(encoding='utf-8'))
assert hashlib.sha256((ROOT/cfg['geometry_source']).read_bytes()).hexdigest()==cfg['geometry_sha256']
bpy.ops.wm.open_mainfile(filepath=str(ROOT/cfg['geometry_source']));s=bpy.context.scene;s.frame_set(1)
entries={e['object']:e for e in roles['entries']};base=cfg['source_paint_srgb_base']
meshes=[o for o in s.objects if o.type=='MESH']
def geometry_hash():
    h=hashlib.sha256()
    for o in sorted(meshes,key=lambda q:q.name):
        h.update(o.name.encode());h.update(str([tuple(v.co) for v in o.data.vertices]).encode())
        h.update(str([list(p.vertices) for p in o.data.polygons]).encode());h.update(o.parent.name.encode())
    return h.hexdigest()
before=geometry_hash()
sys.path.insert(0,str(ROOT))
from turret_repairs import apply_repairs
repairs=apply_repairs()
meshes=[o for o in s.objects if o.type=='MESH']
for o in meshes:
    if o.name not in entries:entries[o.name]={'role':o['art_role'],'parent':o.parent.name}
repaired_hash=geometry_hash()
from surface_accents import part_regions,region_role,apply_recess_paint
from height_palette import final_paint,COLOR_ATTRIBUTE
def srgb(c):return 255*(12.92*c if c<=.0031308 else 1.055*c**(1/2.4)-.055)
def paint_gain(m):
    if 'Game grille' in m.name:return None
    rgb=[srgb(v) for v in m.diffuse_color[:3]];gain=sum(c/b for c,b in zip(rgb,base))/3
    return gain if max(abs(c-b*gain) for c,b in zip(rgb,base))<1.2 else None
cache={};accent_regions=[]
def new_material(part_role,z,normal):
    key=part_role
    if key in cache:return cache[key]
    label='RT '+part_role+' '
    m=bpy.data.materials.new(label.strip());m.use_nodes=True
    m['paint_role']=part_role;m['final_height_color']=True
    rgb,_,_=final_paint(z,normal,part_role,1,cfg['height_first_paint'])
    color=(*rgb,1);m.diffuse_color=color
    nodes=m.node_tree.nodes;nodes.clear();e=nodes.new('ShaderNodeEmission');e.inputs['Color'].default_value=color
    out=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);cache[key]=m
    return m
for o in meshes:
    assert o.name in entries,o.name
    role=entries[o.name]['role']
    desired=[None]*len(o.data.polygons)
    for indices in part_regions(o,role):
        part_role=region_role(o,role,indices,roles)
        if part_role in {'rear_basket_floor','hatch_lid_handle'}:
            accent_regions.append({'object':o.name,'role':part_role,'polygon_indices':indices})
        for pi in indices:
            p=o.data.polygons[pi];old=o.data.materials[p.material_index];g=paint_gain(old)
            if g is None:
                desired[pi]=old;continue
            normal=Vector(tuple(round(v,3) for v in p.normal))
            desired[pi]=new_material(part_role,(o.matrix_world@p.center).z,normal)
    assert all(m is not None for m in desired)
    o.data.materials.clear();slots={}
    for m in desired:
        if m.name not in slots:slots[m.name]=len(slots);o.data.materials.append(m)
    for p,m in zip(o.data.polygons,desired):p.material_index=slots[m.name]
assert geometry_hash()==repaired_hash
recess_report={o.name:apply_recess_paint(o,cfg['recess_paint'],entries[o.name]['role']) for o in meshes}
sys.path.insert(0,str(ROOT));from face_depth_tone import apply_face_depth_tone
s.camera=bpy.data.objects['Camera_iso_front'];cel_report=apply_face_depth_tone(s)
quality=cfg['render_quality']
s.eevee.taa_render_samples=quality['samples'];s.render.filter_size=quality['filter_size']
s.render.image_settings.color_depth=quality['color_depth']
for collection in (bpy.data.meshes,bpy.data.materials,bpy.data.images):
    for item in list(collection):
        if item.users==0:collection.remove(item)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'bradley_rimworld.blend'),compress=True)
# Blender finalizes mesh topology on save. Count that saved scene before the
# exchange exporter evaluates its own temporary mesh data.
saved_faces=sum(len(o.data.polygons) for o in bpy.context.scene.objects if o.type=='MESH')
# After saving the editable scene, use the exporter's recognized unlit color
# graph. Emission alone exports an emissive white factor, which loses COLOR_0.
for m in bpy.data.materials:
    if not m.use_nodes:continue
    nodes=m.node_tree.nodes;nodes.clear();out=nodes.new('ShaderNodeOutputMaterial')
    if m.get('paint_role'):
        color=nodes.new('ShaderNodeVertexColor');color.layer_name=COLOR_ATTRIBUTE
    else:
        color=nodes.new('ShaderNodeRGB');color.outputs[0].default_value=m.diffuse_color
    m.node_tree.links.new(color.outputs[0],out.inputs['Surface'])
with tempfile.TemporaryDirectory(prefix='.export-',dir=OUT) as staging:
    path=Path(staging)/'bradley_rimworld.glb'
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',export_animations=True,export_cameras=False,export_lights=False)
    os.replace(path,OUT/'bradley_rimworld.glb')
report={'mode':'targeted_turret_repairs_height_paint','geometry_source':cfg['geometry_source'],'baseline_geometry_sha256':before,'geometry_sha256':repaired_hash,
 'geometry_changes_limited_to':sorted(repairs),'geometry_repairs':repairs,'height_first_paint':cfg['height_first_paint'],
 'render_quality':quality,'recess_paint':recess_report,'cel_paint':cel_report,
 'accent_regions':accent_regions,
 'paint_pipeline':'height -> final linear RGB corner attribute -> emission, no later gains',
 'meshes':len(meshes),'faces':saved_faces,
 'shared_paint_materials':len(cache),
 'stage2_geometry':'reverted_at_user_request','in_game_validation':'not_performed'}
(OUT/'manifest.json').write_text(json.dumps(report,indent=2)+'\n')
roles['stage_status']='targeted_turret_repairs_height_paint'
roles['policy']['production_registry_scope']='targeted_repairs_and_paint'
roles['targeted_geometry_repairs']=repairs
roles['runtime_bindings']=[{'object':o.name,'role':entries[o.name]['role'],'parent':o.parent.name} for o in meshes]
roles['restored_stage1_required']=['WT_roll_03','WT_ex_decor_02']
roles['additional_roles']=[{'role':role,'source_binding':'exact OBJ bone_turret face sets','objects':[o.name for o in meshes if o.get('art_role')==role]} for role in ('smoke_launcher','smoke_mount','turret_linkage_support')]
roles['deferred_roles']=[]
(ROOT/cfg['role_registry']).write_text(json.dumps(roles,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('TARGETED REPAIR AND HEIGHT PAINT BUILD PASSED',report['faces'],'faces,',len(repairs),'repairs')
