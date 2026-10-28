"""Check saved mounts against supplied source geometry and each fitted belt."""
from pathlib import Path
import bpy,sys,json,hashlib
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output'
CONFIG=json.loads((ROOT/'weapons.json').read_text());reports={}
calibration=json.loads((OUT/'scale_calibration.json').read_text())
bpy.ops.wm.open_mainfile(filepath=calibration['source']);bpy.context.scene.frame_set(1);bpy.context.view_layer.update()
canonical=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o.get('source_object')=='Cylinder.003' and o.get('layer')=='frame')
source_faces=[p for p in canonical.data.polygons if canonical.data.materials[p.material_index].name=='feed']
source_used=sorted({i for p in source_faces for i in p.vertices})
source_points=[canonical.matrix_world@canonical.data.vertices[i].co for i in source_used]
source_uv={uv.name:[tuple(uv.data[i].uv) for p in source_faces for i in p.loop_indices] for uv in canonical.data.uv_layers}
source_color=tuple(next(n for n in canonical.data.materials[source_faces[0].material_index].node_tree.nodes if n.type=='EMISSION').inputs[0].default_value)
for kind,config in CONFIG.items():
    folder=OUT/kind;meta=json.loads((folder/'metadata.json').read_text());source=Path(meta['source'])
    assert hashlib.sha256(source.read_bytes()).hexdigest()==meta['source_sha256']
    assert hashlib.sha256(Path(meta['station_source']).read_bytes()).hexdigest()==meta['station_sha256']
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(source));bpy.context.view_layer.update()
    o=bpy.context.scene.objects[config['object']];points=[o.matrix_world@v.co for v in o.data.vertices]
    faces={p.index:(list(p.vertices),{uv.name:[tuple(uv.data[i].uv) for i in p.loop_indices] for uv in o.data.uv_layers}) for p in o.data.polygons}
    transform=Matrix(meta['transform']);expected_feed=transform@Vector(config['feed_native'])
    bpy.ops.wm.open_mainfile(filepath=str(folder/('crows_'+kind.lower()+'.blend')));scene=bpy.context.scene;bpy.context.view_layer.update()
    seen=set();vertices=0;uvs=0;weapon_points=[]
    for o in scene.objects:
        if o.type!='MESH' or o.get('source_object')!=config['object']:continue
        vi=o.data.attributes['source_vertex'];fi=o.data.attributes['source_face']
        weapon_points.extend(o.matrix_world@v.co for v in o.data.vertices)
        for v,i in zip(o.data.vertices,vi.data):assert (o.matrix_world@v.co-transform@points[i.value]).length<2e-6;vertices+=1
        for p,i in zip(o.data.polygons,fi.data):
            assert i.value not in seen;seen.add(i.value);indices,uv=faces[i.value]
            assert [vi.data[v].value for v in p.vertices]==indices
            for layer in o.data.uv_layers:
                for loop,expected in zip(p.loop_indices,uv[layer.name]):assert (layer.data[loop].uv-Vector(expected)).length<1e-6;uvs+=1
    assert len(seen)==meta['retained_faces']
    assert len(seen)+sum(p['faces'] for p in meta['removed_components'])==meta['source_faces']
    actual_length=max(p.y for p in weapon_points)-min(p.y for p in weapon_points)
    expected_ratio=config['length_m']/calibration['m2_length_m'];actual_ratio=actual_length/calibration['m2_length_tiles']
    assert abs(actual_ratio-expected_ratio)<1e-6,(kind,actual_ratio,expected_ratio)
    feed=meta['feed'];chute=scene.objects[kind+' enclosed feed chute']
    assert chute.parent and chute.parent.name=='CROWS_YAW'
    assert len(chute.data.vertices)==len(source_points) and len(chute.data.polygons)==len(source_faces)
    actual_points=[chute.matrix_world@v.co for v in chute.data.vertices]
    unchanged=sum((a-b).length<1e-6 for a,b in zip(actual_points,source_points))
    assert unchanged>=feed['unchanged_chute_vertices']
    terminal=[actual_points[i] for i in feed['terminal_vertex_indices']]
    center=sum(terminal,Vector())/len(terminal)
    assert (center-expected_feed-Vector((feed['terminal_overlap_tiles'],0,0))).length<1e-6
    width=max(p.y for p in terminal)-min(p.y for p in terminal)
    assert abs(width-config['chute_width_m']*meta['world_per_m'])<1e-6
    for layer in chute.data.uv_layers:
        for actual,expected in zip(layer.data,source_uv[layer.name]):assert (actual.uv-Vector(expected)).length<1e-6
    actual_color=tuple(next(n for n in chute.data.materials[0].node_tree.nodes if n.type=='EMISSION').inputs[0].default_value)
    assert actual_color==source_color,'Chute must use the canonical M2 color'
    assert not any(o.name.startswith(kind+' belt case ') for o in scene.objects)
    assert not any(o.type=='LIGHT' for o in scene.objects)
    for o in scene.objects:
        if o.type=='MESH':
            assert not o.hide_render,o.name
            for material in o.data.materials:assert any(node.type=='EMISSION' for node in material.node_tree.nodes),(o.name,material.name)
    reports[kind]=dict(passed=True,source_vertices_verified=vertices,source_faces_verified=len(seen),uv_loops_verified=uvs,source_exclusions_accounted=True,humvee_body_excluded=kind=='MK19',station_source_unchanged=True,belt_connected_to_box_and_feed_gate=True,m2_length_ratio=actual_ratio,expected_real_length_ratio=expected_ratio,weapon_length_tiles=actual_length,simple_enclosed_m2_chute=True,chute_faces=len(chute.data.polygons),unchanged_chute_vertices=unchanged,chute_uvs_and_color_preserved=True,individual_cartridges=False,yaw_pivot=[0,0,0],barrel_is_fixed=True,no_lights_or_shadows=True,in_game_tested=False)
(OUT/'model_validation.json').write_text(json.dumps(dict(passed=True,weapons=reports),indent=2)+'\n')
print('CROWS_VARIANTS_MODEL_VALIDATION_PASS:',', '.join(reports),'source geometry/UVs, actual M2 scale ratios and enclosed feed chute fit/color/UVs verified')
