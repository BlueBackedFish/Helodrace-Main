"""Build a separate constant-Emission color-block trial; production stays intact."""
import bpy,json,sys,hashlib,shutil,tempfile,os
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
from height_palette import COLOR_ATTRIBUTE,LUMINANCE_ATTRIBUTE,WEIGHTS,display,final_paint,top_luminance
from cel_patches import BROAD_ATTRIBUTE
cfg=json.loads((OUT/'settings.json').read_text(encoding='utf-8-sig'))
paint=json.loads((ROOT/'part_contrast.json').read_text())['height_first_paint']
source=ROOT/cfg['baseline_blend']
bpy.ops.wm.open_mainfile(filepath=str(source));s=bpy.context.scene;s.frame_set(1)
meshes=[o for o in s.objects if o.type=='MESH']
def geometry_hash():
    h=hashlib.sha256()
    for o in sorted(meshes,key=lambda o:o.name):
        h.update(o.name.encode())
        h.update(str([tuple(v.co) for v in o.data.vertices]).encode())
        h.update(str([list(p.vertices) for p in o.data.polygons]).encode())
        h.update(o.parent.name.encode());h.update(str([tuple(row) for row in o.matrix_world]).encode())
    return h.hexdigest()
before=geometry_hash()
sources={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (source,ROOT/'RimWorld/bradley_rimworld.glb',ROOT/'part_contrast.json')}
assert {name.replace('\\','/'):digest for name,digest in sources.items()}==cfg['baseline_sha256'],'Trial baseline changed; review settings before rebuilding'
for src,dst in [('Audit/ContactPaint/after_front.png','before_front.png'),('RimWorld/VF_East/256/preview_east.png','before_east_256.png'),('RimWorld/VF_East/512/preview_east.png','before_east_512.png')]:
    shutil.copyfile(ROOT/src,OUT/dst)
old_mats={m.name:m for o in meshes for m in o.data.materials if m}
baseline_nodes={name:[n.type for n in m.node_tree.nodes] for name,m in old_mats.items() if m.use_nodes}
assert all('EMISSION' in types and not any(t.startswith('BSDF_') for t in types) for types in baseline_nodes.values())
materials={};plate_faces=[];broad_faces=0;changed_faces=0;curved_before_faces=0
def fixed(role,y):
    key=('metal' if role=='main_barrel' else 'tan',round(y,3))
    if key in materials:return materials[key]
    rgb,actual,saturation=final_paint(Vector((0,0,1)),role,1,paint,final_value=y)
    m=bpy.data.materials.new('Fixed Emission %s Y%.0f'%key);m.use_nodes=True;m.diffuse_color=(*rgb,1)
    m['fixed_emission']=True;m['display_luminance']=actual;m['pigment']=key[0]
    nodes=m.node_tree.nodes;nodes.clear();emission=nodes.new('ShaderNodeEmission')
    emission.inputs['Color'].default_value=(*rgb,1);emission.inputs['Strength'].default_value=1
    output=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(emission.outputs['Emission'],output.inputs['Surface'])
    materials[key]=m;return m
for obj in meshes:
    me=obj.data;colors=me.color_attributes[COLOR_ATTRIBUTE];broad=me.attributes[BROAD_ATTRIBUTE];desired=[]
    for p in me.polygons:
        old=me.materials[p.material_index];role=old.get('paint_role')
        if not role:desired.append(old);continue
        old_rgb=[tuple(colors.data[i].color[:3]) for i in p.loop_indices]
        if len({tuple(round(c,5) for c in rgb) for rgb in old_rgb})>1:curved_before_faces+=1
        y=sum(sum(display(c)*w for c,w in zip(rgb,WEIGHTS)) for rgb in old_rgb)/len(old_rgb)
        if broad.data[p.index].value:
            y=top_luminance(role,paint)-(0 if p.normal.z>=cfg['broad_lid_top_normal'] else cfg['broad_lid_rim_drop'])
            broad_faces+=1
        y=round(y/cfg['palette_step'])*cfg['palette_step']
        target=cfg['brighter_plate']
        if obj.name==target['object'] and p.normal.z>=target['minimum_normal_z']:
            y=target['display_luminance'];plate_faces.append(p.index)
        y=max(paint['minimum_luminance'],min(paint['maximum_luminance'],y))
        m=fixed(role,y);desired.append(m)
        rgb=m.diffuse_color[:3]
        changed_faces+=int(any(max(abs(a-b) for a,b in zip(original,rgb))>1e-6 for original in old_rgb))
        for i in p.loop_indices:
            colors.data[i].color=(*rgb,1);me.attributes[LUMINANCE_ATTRIBUTE].data[i].value=m['display_luminance']
    slots={};me.materials.clear()
    for m in desired:
        if m.name not in slots:slots[m.name]=len(slots);me.materials.append(m)
    for p,m in zip(me.polygons,desired):p.material_index=slots[m.name]
assert plate_faces and before==geometry_hash()
assert not any(o.type=='LIGHT' for o in s.objects)
for m in materials.values():
    assert {n.type for n in m.node_tree.nodes}=={'EMISSION','OUTPUT_MATERIAL'}
    assert not m.node_tree.animation_data
s['emission_trial']='Constant material emission colors; no view input, interpolated paint or render-time recoloring.'
s.render.image_settings.color_depth='16';s.eevee.taa_render_samples=cfg['samples'];s.render.film_transparent=True
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'bradley_emission_trial.blend'),compress=True)
# Export constant unlit colors through glTF's recognized RGB -> output graph.
for m in {m.name:m for o in meshes for m in o.data.materials if m}.values():
    nodes=m.node_tree.nodes;nodes.clear();color=nodes.new('ShaderNodeRGB');color.outputs[0].default_value=m.diffuse_color
    output=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(color.outputs[0],output.inputs['Surface'])
for obj in meshes:
    # Constant glTF material factors must not be multiplied by the old active
    # vertex colors. This export-only cleanup occurs after saving the BLEND.
    for color in list(obj.data.color_attributes):obj.data.color_attributes.remove(color)
with tempfile.TemporaryDirectory(prefix='.export-',dir=OUT) as staging:
    path=Path(staging)/'trial.glb'
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',export_animations=True,export_cameras=False,export_lights=False)
    os.replace(path,OUT/'bradley_emission_trial.glb')
report={'baseline_commit':cfg['baseline_commit'],'baseline_already_emission':True,'baseline_material_nodes':baseline_nodes,
'trial_fixed_paint_materials':len(materials),'flat_broad_lid_faces':broad_faces,'formerly_interpolated_faces':curved_before_faces,
'changed_painted_faces':changed_faces,'brighter_plate':dict(cfg['brighter_plate'],faces=plate_faces),
'geometry_sha256_including_parent_and_world_matrix':before,'geometry_unchanged':True,'faces':sum(len(o.data.polygons) for o in meshes),'meshes':len(meshes),
'production_source_sha256':sources,'trial_only':True,'grille':'Existing position-based emission stripe pattern preserved in BLEND; solid fallback in GLB.',
'no_camera_or_normal_shader_input':True,'no_render_time_recolor':True,'in_game_validation':'not_performed'}
(OUT/'manifest.json').write_text(json.dumps(report,indent=2)+'\n')
print('FIXED EMISSION TRIAL BUILT',len(materials),'fixed paint colors,',len(plate_faces),'bright plate faces')
