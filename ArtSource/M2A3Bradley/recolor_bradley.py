"""Preserve source proportions, repair requested parts and apply height-led paint."""
import bpy,json,math,hashlib,collections,sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'RimWorld'
cfg=json.loads((ROOT/'part_contrast.json').read_text());roles=json.loads((ROOT/cfg['role_registry']).read_text(encoding='utf-8'))
assert hashlib.sha256((ROOT/cfg['geometry_source']).read_bytes()).hexdigest()==cfg['geometry_sha256']
bpy.ops.wm.open_mainfile(filepath=str(ROOT/cfg['geometry_source']));s=bpy.context.scene;s.frame_set(1)
entries={e['object']:e for e in roles['entries']};base=cfg['paint_srgb_base'];factor=cfg['part_difference_multiplier']
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
repaired_hash=geometry_hash()
def srgb(c):return 255*(12.92*c if c<=.0031308 else 1.055*c**(1/2.4)-.055)
def linear(c):
    v=c/255;return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
def paint_gain(m):
    if 'Game grille' in m.name:return None
    rgb=[srgb(v) for v in m.diffuse_color[:3]];gain=sum(c/b for c,b in zip(rgb,base))/3
    return gain if max(abs(c-b*gain) for c,b in zip(rgb,base))<1.2 else None
def anchor(o,indices=None):
    weights=collections.defaultdict(float)
    for p in o.data.polygons:
        if indices is not None and p.index not in indices:continue
        if p.normal.z<.75:continue
        g=paint_gain(o.data.materials[p.material_index])
        if g is not None:weights[round(g,3)]+=p.area
    return max(weights,key=weights.get) if weights else None
assembly={'hull':anchor(bpy.data.objects['WT_@root']),'turret':anchor(bpy.data.objects['WT_bone_turret'])}
cache={};measurements=[];clamped=0
def new_material(original,gain,palette=None):
    palette=palette or base
    key=(original.name,round(gain,5),tuple(palette))
    if key in cache:return cache[key]
    m=bpy.data.materials.new(('Dark TAN ' if palette!=base else 'Part TAN ')+str(round(gain,4)));m.use_nodes=True
    color=tuple(linear(min(255,b*gain)) for b in palette)+(1,);m.diffuse_color=color
    nodes=m.node_tree.nodes;nodes.clear();e=nodes.new('ShaderNodeEmission');e.inputs['Color'].default_value=color
    out=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);cache[key]=m
    return m
for o in meshes:
    assert o.name in entries,o.name
    role=entries[o.name]['role'];ref=assembly['turret' if entries[o.name]['parent'] in {'TURRET_YAW','GUN_ELEVATION'} else 'hull']
    grouped=collections.defaultdict(list)
    if role=='turret_assembly':
        # Read-only connectivity supplies separate colors for existing roof
        # fixtures and the five rear bins; no vertices or faces are changed.
        leaders=list(range(len(o.data.vertices)))
        def find(i):
            while leaders[i]!=i:leaders[i]=leaders[leaders[i]];i=leaders[i]
            return i
        for edge in o.data.edges:leaders[find(edge.vertices[0])]=find(edge.vertices[1])
        for p in o.data.polygons:grouped[find(p.vertices[0])].append(p.index)
    else:grouped[0]=list(range(len(o.data.polygons)))
    desired=[None]*len(o.data.polygons)
    for indices in grouped.values():
        part_role=role
        if role=='turret_assembly':
            ids={v for pi in indices for v in o.data.polygons[pi].vertices};pts=[o.matrix_world@o.data.vertices[v].co for v in ids]
            lo=Vector(tuple(min(p[k] for p in pts) for k in range(3)));hi=Vector(tuple(max(p[k] for p in pts) for k in range(3)));center=(lo+hi)/2
            for part in roles['protected_components']:
                a,b=map(Vector,part['source_bounds'])
                if all(a[k]-.015<=center[k]<=b[k]+.015 for k in range(3)) and max(hi-lo)<.60:
                    part_role='rear_stowage_bin';break
            if part_role==role and lo.z>2.69 and max(hi-lo)<.90:part_role='turret_roof_equipment'
        minimum=cfg['minimum_role_differences'].get(part_role,0)
        dark_palette=cfg['dark_role_palettes'].get(part_role)
        top_delta=cfg['role_top_differences'].get(part_role)
        measured=anchor(o,set(indices));old_delta=(measured-ref) if measured is not None else 0
        delta=old_delta
        if minimum:
            # A direction-specific minimum also separates formerly identical tops.
            delta=min(old_delta,minimum) if minimum<0 else max(old_delta,minimum)
            offset=factor*delta-old_delta
        else:offset=0
        if offset:measurements.append({'object':o.name,'role':part_role,'reference_gain':ref,'old_top_gain':measured,'old_top_delta':old_delta,'target_delta':factor*delta,'offset':offset})
        for pi in indices:
            p=o.data.polygons[pi];old=o.data.materials[p.material_index];g=paint_gain(old)
            if g is None:desired[pi]=old;continue
            if top_delta is not None and measured is not None:
                # Do not inherit the old uniformly dark hatch tops. Curvature and
                # actual vertex height now supply their separated tones.
                target=ref+top_delta+.45*(g-measured)
            elif dark_palette:
                # A separate brown TAN pigment remains dark even when high up.
                target=1+.55*(g-(measured or ref))
            else:target=g+offset
            floor=min(g,cfg['paint_gain_floor']);new=max(floor,min(cfg['paint_gain_ceiling'],target))
            if abs(new-target)>1e-5:clamped+=1
            desired[pi]=new_material(old,new,dark_palette)
    assert all(m is not None for m in desired)
    o.data.materials.clear();slots={}
    for m in desired:
        if m.name not in slots:slots[m.name]=len(slots);o.data.materials.append(m)
    for p,m in zip(o.data.polygons,desired):p.material_index=slots[m.name]
assert geometry_hash()==repaired_hash
sys.path.insert(0,str(ROOT));from face_depth_tone import apply_face_depth_tone
s.camera=bpy.data.objects['Camera_iso_front'];apply_face_depth_tone(s)
for collection in (bpy.data.meshes,bpy.data.materials,bpy.data.images):
    for item in list(collection):
        if item.users==0:collection.remove(item)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'bradley_rimworld.blend'),compress=True)
for m in bpy.data.materials:
    if not m.use_nodes:continue
    for n in m.node_tree.nodes:
        if n.type=='EMISSION':
            for link in list(n.inputs['Color'].links):m.node_tree.links.remove(link)
            n.inputs['Color'].default_value=m.diffuse_color
bpy.ops.export_scene.gltf(filepath=str(OUT/'bradley_rimworld.glb'),export_format='GLB',export_animations=True,export_cameras=False,export_lights=False)
report={'mode':'targeted_turret_repairs_height_paint','geometry_source':cfg['geometry_source'],'baseline_geometry_sha256':before,'geometry_sha256':repaired_hash,
 'geometry_changes_limited_to':sorted(repairs),'geometry_repairs':repairs,'height_paint':cfg['height_paint'],
 'dark_role_palettes':cfg['dark_role_palettes'],'role_top_differences':cfg['role_top_differences'],
 'meshes':len(meshes),'faces':sum(len(o.data.polygons) for o in meshes),
 'part_difference_multiplier':factor,'clamped_faces':clamped,'assembly_reference_gains':assembly,'part_adjustments':measurements,
 'stage2_geometry':'reverted_at_user_request','in_game_validation':'not_performed'}
(OUT/'manifest.json').write_text(json.dumps(report,indent=2)+'\n')
roles['stage_status']='targeted_turret_repairs_height_paint'
roles['policy']['production_registry_scope']='targeted_repairs_and_paint'
roles['targeted_geometry_repairs']=repairs
roles['runtime_bindings']=[{'object':o.name,'role':entries[o.name]['role'],'parent':o.parent.name} for o in meshes]
roles['restored_stage1_required']=[]
roles['additional_roles']=[]
roles['deferred_roles']=[{'role':'smoke_launcher','reason':'stage2 new geometry reverted; user requests original geometry and recolor only'}]
(ROOT/cfg['role_registry']).write_text(json.dumps(roles,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('TARGETED REPAIR AND HEIGHT PAINT BUILD PASSED',report['faces'],'faces,',len(repairs),'repairs')
