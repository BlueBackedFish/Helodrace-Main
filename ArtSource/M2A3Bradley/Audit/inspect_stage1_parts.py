"""Read-only source/current inventory and isolated views for role mapping."""
import bpy,json,os,collections
from mathutils import Vector
HERE=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.dirname(HERE)
OUT=os.path.join(HERE,'Stage1');os.makedirs(OUT,exist_ok=True)
current=json.load(open(os.path.join(HERE,'current_model_stats.json')))
current={r['name']:r for r in current['all_meshes']}
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'ReferenceFit','m3a3_bradley_refit.blend'))
s=bpy.context.scene;s.frame_set(1);meshes=[o for o in s.objects if o.type=='MESH']
report={};targets=[]
for o in meshes:
    pts=[o.matrix_world@v.co for v in o.data.vertices]
    report[o.name]={'faces':len(o.data.polygons),'current_faces':current.get(o.name,{}).get('faces'),
        'parent':o.parent.name,'bounds':[[min(p[k] for p in pts) for k in range(3)],[max(p[k] for p in pts) for k in range(3)]],
        'used_materials':sorted({o.data.materials[p.material_index].name for p in o.data.polygons})}
    if o.name.startswith(('WT_ex_decor','WT_ex_armor_','WT_roll_','WT_rocket','WT_bone_commander')):targets.append(o)
    if o.name in {'WT_@root','WT_bone_turret'}:
        # Coincident-position connectivity crosses the source UV seams.
        parent={};alias={};positions={}
        def find(i):
            parent.setdefault(i,i)
            while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
            return i
        for v in o.data.vertices:alias[v.index]=positions.setdefault(tuple(round(c,4) for c in v.co),v.index)
        for p in o.data.polygons:
            a=find(alias[p.vertices[0]])
            for i in p.vertices[1:]:parent[find(alias[i])]=a
        groups=collections.defaultdict(list)
        for p in o.data.polygons:groups[find(alias[p.vertices[0]])].append(p.index)
        components=[]
        for indices in groups.values():
            ids={i for pi in indices for i in o.data.polygons[pi].vertices};ps=[o.matrix_world@o.data.vertices[i].co for i in ids]
            lo=[min(p[k] for p in ps) for k in range(3)];hi=[max(p[k] for p in ps) for k in range(3)]
            components.append({'faces':len(indices),'polygon_indices':indices,'bounds':[lo,hi]})
        components.sort(key=lambda c:c['faces'],reverse=True)
        report[o.name]['components']=components
with open(os.path.join(OUT,'source_inventory.json'),'w') as f:json.dump(report,f,indent=2)
# Small diagnostic renders, keeping source geometry and original saved files intact.
mats=[]
for value in (.82,.58,.35,.18):
    m=bpy.data.materials.new('Audit gray');m.use_nodes=True;nodes=m.node_tree.nodes;nodes.clear()
    e=nodes.new('ShaderNodeEmission');e.inputs['Color'].default_value=(value,value,value,1)
    out=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);mats.append(m)
for o in meshes:
    o.data.materials.clear()
    for m in mats:o.data.materials.append(m)
    for p in o.data.polygons:p.material_index=0 if p.normal.z>.6 else 1 if p.normal.z>.1 else 2 if abs(p.normal.y)>.6 else 3
s.render.resolution_x=s.render.resolution_y=256;s.render.resolution_percentage=100
s.render.film_transparent=True;s.render.image_settings.color_mode='RGBA';s.render.image_settings.file_format='PNG'
s.eevee.taa_render_samples=8
camera=bpy.data.objects['Camera_iso_front'];s.camera=camera
for o in targets:
    for q in meshes:q.hide_render=q!=o
    lo,hi=map(Vector,report[o.name]['bounds']);center=(lo+hi)/2;extent=hi-lo
    camera.location=center+Vector((-6,-9,7));camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=max(extent)*1.55
    s.render.filepath=os.path.join(OUT,o.name+'.png');bpy.ops.render.render(write_still=True)
print('STAGE1 SOURCE INVENTORY COMPLETE',len(report),'groups',len(targets),'isolated views')
