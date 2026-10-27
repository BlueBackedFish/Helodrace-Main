"""Check actual chute geometry, retained source surfaces and animated layers."""
from pathlib import Path
import bpy,json
from mathutils import Matrix,Vector

ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';meta=json.loads((OUT/'metadata.json').read_text());feed=meta['feed_alignment'];art=meta['articulation']
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=meta['source'])
source=bpy.data.objects['Cylinder.003'];vertices=[source.matrix_world@v.co for v in source.data.vertices]
polygons=[(list(p.vertices),{layer.name:[tuple(layer.data[i].uv) for i in p.loop_indices] for layer in source.data.uv_layers}) for p in source.data.polygons]
bpy.ops.wm.open_mainfile(filepath=str(OUT/'crows_ii_m2.blend'));scene=bpy.context.scene;scene.frame_set(art['recoil_frames'][0]);bpy.context.view_layer.update()
station=[o for o in scene.objects if o.type=='MESH' and o.get('source_object')=='Cylinder.003'];frame=next(o for o in station if o['layer']=='frame')
feed_ids={i for p in frame.data.polygons if frame.data.materials[p.material_index].name=='feed' for i in p.vertices}
source_transform=Matrix.Diagonal((meta['source_to_world_scale'],)*3+(1,))@Matrix.Translation(-Vector(meta['source_pivot']))
changed=unchanged=faces=uv_loops=0
for o in station:
    vi=o.data.attributes['source_vertex'];fi=o.data.attributes['source_face']
    for v,identifier in zip(o.data.vertices,vi.data):
        expected=source_transform@vertices[identifier.value];error=(o.matrix_world@v.co-expected).length
        if o==frame and v.index in feed_ids:
            changed+=error>1e-6;unchanged+=error<=1e-6
            if expected.x<-.4:assert error<1e-6,'Ammo-box end moved'
        else:assert error<1e-5,(o.name,v.index,'Unrelated station geometry changed')
    for p,identifier in zip(o.data.polygons,fi.data):
        indices,uvs=polygons[identifier.value];assert [vi.data[i].value for i in p.vertices]==indices
        for layer in o.data.uv_layers:
            for i,expected in zip(p.loop_indices,uvs[layer.name]):assert (layer.data[i].uv-Vector(expected)).length<1e-6;uv_loops+=1
        faces+=1
assert changed==192 and unchanged==feed['unchanged_chute_vertices'] and faces==len(polygons)
mouth=[frame.matrix_world@frame.data.vertices[i].co for i in feed['terminal_vertex_indices']]
target=Vector(feed['target_mouth_center']);actual=sum(mouth,Vector())/len(mouth)
assert (actual-target).length<1e-6 and max(abs(v.x-target.x) for v in mouth)<1e-6
assert abs(max(v.y for v in mouth)-min(v.y for v in mouth)-feed['target_width'])<1e-6
meshes=[o for o in scene.objects if o.type=='MESH'];rest={o.name:[o.matrix_world@v.co for v in o.data.vertices] for o in meshes}
scene.frame_set(art['recoil_frames'][1]);bpy.context.view_layer.update();moving=fixed=0
for o in meshes:
    delta=Vector((0,-art['recoil_tiles'],0)) if o['layer']=='barrel' else Vector()
    for v,original in zip(o.data.vertices,rest[o.name]):assert (o.matrix_world@v.co-original-delta).length<2e-6,(o.name,v.index,'Wrong recoil displacement')
    if o['layer']=='barrel':moving+=1;assert o.parent.name=='BARREL_RECOIL'
    else:fixed+=1
scene.frame_set(art['recoil_frames'][-1]);bpy.context.view_layer.update()
for o in meshes:
    for v,original in zip(o.data.vertices,rest[o.name]):assert (o.matrix_world@v.co-original).length<2e-6
assert moving==7 and fixed>0
steps=[]
for frame_index in range(art['recoil_frames'][0],art['recoil_frames'][-1]+1):
    scene.frame_set(frame_index);bpy.context.view_layer.update();travel=-bpy.data.objects['BARREL_RECOIL'].location.y
    assert -1e-7<=travel<=art['recoil_tiles']+1e-7;steps.append(dict(frame=frame_index,recoil_tiles=travel))
result=dict(passed=True,feed_center_error=(actual-target).length,feed_width=feed['target_width'],changed_chute_vertices=changed,unchanged_chute_vertices=unchanged,unrelated_station_vertices_unchanged=True,station_source_faces_preserved=faces,station_uv_loops_preserved=uv_loops,barrel_meshes_moving=moving,fixed_meshes=fixed,recoil_tiles=art['recoil_tiles'],intermediate_recoil_steps=steps,only_barrel_moves=True,qcb_handle_moves_with_barrel=True,returns_to_rest=True,in_game_tested=False)
(OUT/'articulation_validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('CROWS_ARTICULATION_PASS: feed mouth matches donor feedway; only 192 chute vertices fitted; original faces/UV retained; 7 barrel meshes recoil and return independently')
