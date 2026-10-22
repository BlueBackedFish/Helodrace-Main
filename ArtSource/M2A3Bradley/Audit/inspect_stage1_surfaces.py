"""Bind source material/plane subsets and inspect embedded rear launcher modules."""
import bpy,json,os,collections
from mathutils import Vector
HERE=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.dirname(HERE);OUT=os.path.join(HERE,'Stage1')
inventory=json.load(open(os.path.join(OUT,'source_inventory.json')))
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'ReferenceFit','m3a3_bradley_refit.blend'))
s=bpy.context.scene;s.frame_set(1);meshes=[o for o in s.objects if o.type=='MESH'];sets={}
for name in ['WT_@root','WT_bone_turret']:
    o=bpy.data.objects[name];groups=collections.defaultdict(list)
    main=max(inventory[name]['components'],key=lambda c:c['faces'])
    for p in o.data.polygons:
        mat=o.data.materials[p.material_index].name
        if 'net_f' in mat:role='grille'
        elif 'Optics' in mat:role='optics'
        elif 'Headlamp' in mat:role='lamp_lens'
        elif 'recesses' in mat:role='recess'
        else:role='assembly_fitting'
        groups[role].append(p.index)
    armor=collections.defaultdict(list)
    for i in main['polygon_indices']:
        p=o.data.polygons[i];n=p.normal
        role='roof' if n.z>.88 else 'underside' if n.z<-.35 else 'front' if abs(n.y)>=abs(n.x) and n.y<0 else 'rear' if abs(n.y)>=abs(n.x) else 'side'
        armor[role].append(i)
    sets[name]={'material_subsets':dict(groups),'main_shell_subsets':dict(armor)}
with open(os.path.join(OUT,'source_face_sets.json'),'w') as f:json.dump(sets,f,indent=2)
source=bpy.data.objects['WT_bone_turret'];clusters=[c for c in inventory[source.name]['components'] if c['bounds'][0][1]>1 and c['bounds'][0][2]>2.5 and c['faces']>=500]
clusters.sort(key=lambda c:c['bounds'][0][0]);targets=[]
for i,c in enumerate(clusters,1):
    used=sorted({v for pi in c['polygon_indices'] for v in source.data.polygons[pi].vertices});lookup={v:j for j,v in enumerate(used)}
    me=bpy.data.meshes.new('source_cluster');me.from_pydata([source.matrix_world@source.data.vertices[v].co for v in used],[],[[lookup[v] for v in source.data.polygons[pi].vertices] for pi in c['polygon_indices']]);me.update()
    o=bpy.data.objects.new('WT_rear_cluster_'+str(i).zfill(2),me);s.collection.objects.link(o);targets.append(o)
    c['inspection_name']=o.name
with open(os.path.join(OUT,'rear_launcher_candidates.json'),'w') as f:json.dump(clusters,f,indent=2)
allmeshes=meshes+targets;mats=[]
for value in (.75,.5,.27):
    m=bpy.data.materials.new('Inspection');m.use_nodes=True;nodes=m.node_tree.nodes;nodes.clear();e=nodes.new('ShaderNodeEmission');e.inputs['Color'].default_value=(value,value,value,1);out=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);mats.append(m)
for o in targets:
    for m in mats:o.data.materials.append(m)
    for p in o.data.polygons:p.material_index=0 if p.normal.z>.5 else 1 if p.normal.y>.5 else 2
s.render.resolution_x=s.render.resolution_y=256;s.render.resolution_percentage=100;s.eevee.taa_render_samples=8
s.render.film_transparent=True;s.render.image_settings.color_mode='RGBA';camera=bpy.data.objects['Camera_iso_front'];s.camera=camera
for o,c in zip(targets,clusters):
    for q in allmeshes:q.hide_render=q!=o
    lo,hi=map(Vector,c['bounds']);center=(lo+hi)/2;camera.location=center+Vector((5,8,7));camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=max(hi-lo)*1.6
    s.render.filepath=os.path.join(OUT,o.name+'.png');bpy.ops.render.render(write_still=True)
print('SURFACE SETS AND REAR CANDIDATES',len(clusters))
