"""Read-only OBJ/current hull audit and matching renders before restoration."""
import bpy,json,sys,hashlib
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent;ROOT=TRIAL.parent
sys.path[:0]=[str(ROOT),str(TRIAL/'DetailRepair')]
from mesh_regions import regions,position_key
from source_details import read_parts,source_mesh
assert not (HERE/'source_audit.json').exists(),'Do not overwrite the hull baseline'
bpy.ops.wm.open_mainfile(filepath=str(TRIAL/'bradley_emission_trial.blend'));s=bpy.context.scene;s.frame_set(1)
def under(o,parent):
    while o:
        if o.name==parent:return True
        o=o.parent
    return False
hull=[o for o in s.objects if o.type=='MESH' and under(o,'HULL') and not under(o,'TURRET_YAW')]
groups={o.name for o in hull}|{'WT_ex_decor_04'};groups.discard('WT_rear_ramp')
parts,source=read_parts(groups)
existing={position_key(o.matrix_world@v.co) for o in hull for v in o.data.vertices}
baseline={}
for o in s.objects:
    if o.type!='MESH':continue
    record={'parent':o.parent.name,'world':[[round(v,6) for v in row] for row in o.matrix_world],
        'vertices':[tuple(round(c,6) for c in v.co) for v in o.data.vertices],
        'faces':[list(p.vertices) for p in o.data.polygons],
        'colors':[tuple(round(c,6) for c in o.data.materials[p.material_index].diffuse_color) for p in o.data.polygons]}
    baseline[o.name]=hashlib.sha256(json.dumps(record,sort_keys=True).encode()).hexdigest()
(HERE/'before_objects.json').write_text(json.dumps({'commit':'cbf17877','objects':baseline},indent=2)+'\n')
audit={}
for name,items in parts.items():
    # Wheels/tracks/skirt silhouette are retained and inspected in the full views;
    # component restoration focuses on exterior hull fittings and hatches.
    if name.startswith(('WT_wheel_','WT_track_','WT_ex_armor_l_','WT_ex_armor_r_')):continue
    mesh=source_mesh(items,'hull source audit');components=[]
    for index,ids in enumerate(regions(mesh)):
        coords=[Vector(items[i][0][j]) for i in ids for j in range(len(items[i][0]))]
        lo=[min(p[k] for p in coords) for k in range(3)];hi=[max(p[k] for p in coords) for k in range(3)]
        keys={position_key(p) for p in coords}
        components.append({'component':index,'faces':len(ids),'seed':ids[0],'bounds':[lo,hi],
            'source_vertices_present':len(keys&existing),'source_vertices':len(keys),'source_face_ids':ids})
    bpy.data.meshes.remove(mesh)
    current=bpy.data.objects.get(name)
    audit[name]={'source_faces':len(items),'current_faces':len(current.data.polygons) if current else 0,
                 'components':components}
    print(name,len(items),'->',audit[name]['current_faces'],'components',len(components))
(HERE/'source_audit.json').write_text(json.dumps({'source_sha256':source['source_sha256'],'groups':audit},indent=2)+'\n')
for version,path in [('before',TRIAL/'bradley_emission_trial.blend'),('source',ROOT/'ReferenceFit/m3a3_bradley_refit.blend')]:
    bpy.ops.wm.open_mainfile(filepath=str(path));s=bpy.context.scene;s.frame_set(1)
    for o in s.objects:
        if o.type=='MESH':o.hide_render=under(o,'TURRET_YAW')
    if version=='source':
        palette=[]
        for i,rgb in enumerate([(.65,.62,.51,1),(.42,.40,.34,1),(.28,.28,.25,1),(.07,.10,.11,1)]):
            m=bpy.data.materials.new('Hull audit '+str(i));m.use_nodes=True;n=m.node_tree.nodes;n.clear();e=n.new('ShaderNodeEmission');e.inputs[0].default_value=rgb;out=n.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);palette.append(m)
        for o in s.objects:
            if o.type!='MESH' or o.hide_render:continue
            slots=[]
            for p in o.data.polygons:
                m=o.data.materials[p.material_index];n=o.matrix_world.to_3x3()@p.normal
                slots.append(3 if any(k in m.name.lower() for k in ['optic','glass','rubber','track','recess']) else 0 if n.z>.55 else 1 if abs(n.y)>.55 else 2)
            o.data.materials.clear()
            for m in palette:o.data.materials.append(m)
            for p,i in zip(o.data.polygons,slots):p.material_index=i
    s.render.resolution_x=s.render.resolution_y=1024;s.render.resolution_percentage=100;s.eevee.taa_render_samples=64;s.render.film_transparent=True
    cam=bpy.data.objects['Camera_iso_front'];cam.data.type='ORTHO';cam.data.ortho_scale=7.8;s.camera=cam
    center=Vector((0,-.15,1.25))
    for name,delta in [('front',(7,-9,8)),('rear',(-7,9,8)),('top',(0,0,16))]:
        cam.location=center+Vector(delta);cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
        s.render.filepath=str(HERE/(version+'_'+name+'.png'));bpy.ops.render.render(write_still=True)
print('HULL SOURCE AUDIT COMPLETE')
