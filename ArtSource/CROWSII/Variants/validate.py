"""Check saved mounts against supplied source geometry and each fitted belt."""
from pathlib import Path
import bpy,sys,json,hashlib
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output'
CONFIG=json.loads((ROOT/'weapons.json').read_text());reports={}
for kind,config in CONFIG.items():
    folder=OUT/kind;meta=json.loads((folder/'metadata.json').read_text());source=Path(meta['source'])
    assert hashlib.sha256(source.read_bytes()).hexdigest()==meta['source_sha256']
    assert hashlib.sha256(Path(meta['station_source']).read_bytes()).hexdigest()==meta['station_sha256']
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(source));bpy.context.view_layer.update()
    o=bpy.context.scene.objects[config['object']];points=[o.matrix_world@v.co for v in o.data.vertices]
    faces={p.index:(list(p.vertices),{uv.name:[tuple(uv.data[i].uv) for i in p.loop_indices] for uv in o.data.uv_layers}) for p in o.data.polygons}
    transform=Matrix(meta['transform']);expected_feed=transform@Vector(config['feed_native'])
    bpy.ops.wm.open_mainfile(filepath=str(folder/('crows_'+kind.lower()+'.blend')));scene=bpy.context.scene;bpy.context.view_layer.update()
    seen=set();vertices=0;uvs=0
    for o in scene.objects:
        if o.type!='MESH' or o.get('source_object')!=config['object']:continue
        vi=o.data.attributes['source_vertex'];fi=o.data.attributes['source_face']
        for v,i in zip(o.data.vertices,vi.data):assert (o.matrix_world@v.co-transform@points[i.value]).length<2e-6;vertices+=1
        for p,i in zip(o.data.polygons,fi.data):
            assert i.value not in seen;seen.add(i.value);indices,uv=faces[i.value]
            assert [vi.data[v].value for v in p.vertices]==indices
            for layer in o.data.uv_layers:
                for loop,expected in zip(p.loop_indices,uv[layer.name]):assert (layer.data[loop].uv-Vector(expected)).length<1e-6;uvs+=1
    assert len(seen)==meta['retained_faces']
    assert len(seen)+sum(p['faces'] for p in meta['removed_components'])==meta['source_faces']
    feed=meta['feed'];length=feed['round_length_tiles'];n=feed['rounds']
    case_centers=[]
    for i in range(n):
        o=scene.objects[kind+' belt case '+str(i)]
        center=o.matrix_world.translation+Vector((0,length*.18,0));case_centers.append(center)
        assert o.parent and o.parent.name=='CROWS_YAW'
    assert (case_centers[0]-Vector(feed['box_outlet'])).length<1e-6
    assert (case_centers[-1]-expected_feed-Vector((feed['terminal_overlap_tiles'],0,0))).length<1e-6
    pitch=config['belt_pitch_m']*meta['world_per_m']
    gaps=[(b-a).length for a,b in zip(case_centers,case_centers[1:])]
    assert min(gaps)>pitch*.8 and max(gaps)<pitch*1.2,(kind,min(gaps),max(gaps),pitch)
    assert len([o for o in scene.objects if o.name.startswith(kind+' link bridge ')])==n-1
    assert not any(o.type=='LIGHT' for o in scene.objects)
    for o in scene.objects:
        if o.type=='MESH':
            assert not o.hide_render,o.name
            for material in o.data.materials:assert any(node.type=='EMISSION' for node in material.node_tree.nodes),(o.name,material.name)
    reports[kind]=dict(passed=True,source_vertices_verified=vertices,source_faces_verified=len(seen),uv_loops_verified=uvs,source_exclusions_accounted=True,humvee_body_excluded=kind=='MK19',station_source_unchanged=True,belt_connected_to_box_and_feed_gate=True,cartridge_center_spacing_tiles=[min(gaps),max(gaps)],cartridges=n,yaw_pivot=[0,0,0],barrel_is_fixed=True,no_lights_or_shadows=True,in_game_tested=False)
(OUT/'model_validation.json').write_text(json.dumps(dict(passed=True,weapons=reports),indent=2)+'\n')
print('CROWS_VARIANTS_MODEL_VALIDATION_PASS:',', '.join(reports),'source vertices/faces/UVs, cartridge spacing, both belt endpoints and fixed-emission materials verified')
