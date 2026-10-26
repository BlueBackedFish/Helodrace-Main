"""Compare the saved layers against the actual ext models, polygon by polygon."""
from pathlib import Path
from collections import defaultdict
import bpy,json,hashlib,math
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';results={}
for kind in ['M2HB','M1917']:
    meta=json.loads((OUT/kind/'metadata.json').read_text());source=Path(meta['source'])
    assert hashlib.sha256(source.read_bytes()).hexdigest()==meta['source_sha256']
    if source.suffix=='.blend':bpy.ops.wm.open_mainfile(filepath=str(source))
    else:
        bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(source))
    original={}
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        original[o.name]=dict(vertices=[o.matrix_world@v.co for v in o.data.vertices],faces=[dict(indices=list(p.vertices),uv={uv.name:[tuple(uv.data[i].uv) for i in p.loop_indices] for uv in o.data.uv_layers}) for p in o.data.polygons])
    bpy.ops.wm.open_mainfile(filepath=str(OUT/kind/'model.blend'));scene=bpy.context.scene
    assert scene.camera.data.type=='ORTHO' and (scene.camera.rotation_euler.to_quaternion()@Vector((0,0,-1))-Vector((0,0,-1))).length<1e-6
    assert scene.render.film_transparent and not any(o.type=='LIGHT' for o in scene.objects)
    assert scene.render.resolution_x==scene.render.resolution_y==2048
    seen=defaultdict(set);vertex_ids=defaultdict(set);uv_loops=0;flat_colors=defaultdict(set);paint_counts=defaultdict(int)
    meshes=[o for o in scene.objects if o.type=='MESH']
    for o in meshes:
        assert o['source_object'] in original
        src=original[o['source_object']];flat=o['source_transform'];matrix=Matrix([flat[i:i+4] for i in range(0,16,4)])
        assert abs(matrix.to_3x3().determinant()-1)<1e-6
        vi=o.data.attributes['source_vertex'];fi=o.data.attributes['source_face']
        for v,identifier in zip(o.data.vertices,vi.data):
            assert (o.matrix_world@v.co-matrix@src['vertices'][identifier.value]).length<2e-6,(kind,o.name,v.index)
            vertex_ids[o['source_object']].add(identifier.value)
        for p,identifier in zip(o.data.polygons,fi.data):
            assert identifier.value not in seen[o['source_object']]
            seen[o['source_object']].add(identifier.value);face=src['faces'][identifier.value]
            assert [vi.data[i].value for i in p.vertices]==face['indices']
            assert set(face['uv'])=={uv.name for uv in o.data.uv_layers}
            material=o.data.materials[p.material_index];paint_counts[material.name]+=1
            color=tuple(round(c,6) for c in next(n for n in material.node_tree.nodes if n.type=='EMISSION').inputs[0].default_value)
            target='tripod' if o['layer']=='mount' else 'barrel' if kind=='M2HB' and o['layer']=='barrel' else 'water_jacket' if material.name.startswith('olive_') else None
            if target:flat_colors[target].add(color)
            for uv in o.data.uv_layers:
                for index,expected in zip(p.loop_indices,face['uv'][uv.name]):
                    assert (uv.data[index].uv-Vector(expected)).length<1e-6
                    uv_loops+=1
        for material in o.data.materials:
            assert material and material.use_nodes and any(n.type=='EMISSION' for n in material.node_tree.nodes)
            assert not any(n.type=='TEX_IMAGE' for n in material.node_tree.nodes)
    for name,src in original.items():
        assert seen[name]==set(range(len(src['faces']))),(kind,name,'missing polygons')
        assert vertex_ids[name]=={i for f in src['faces'] for i in f['indices']},(kind,name,'missing used vertices')
    assert set(seen)==set(original)
    assert all(len(colors)==1 for colors in flat_colors.values()),('Unwanted surface shading',flat_colors)
    assert paint_counts['brass_top']>0 and paint_counts['copper_top']>0
    pose_check={}
    if kind=='M2HB':
        assert paint_counts['link_top']>0
        box=next(o for o in meshes if o['pose']=='ammo_box');lid=next(o for o in meshes if o['pose']=='ammo_lid')
        flat=box['source_transform'];matrix=Matrix([flat[i:i+4] for i in range(0,16,4)])
        assert abs(matrix[0][1])+abs(matrix[1][0])+abs(matrix[2][0])+abs(matrix[2][1])<1e-6,'Ammo box is tilted'
        normal=max(lid.data.polygons,key=lambda p:p.area).normal
        lid_tilt=math.degrees(math.acos(min(1,abs(normal.z))))
        assert lid_tilt<1,'Lid is still open'
        box_points=[v.co for v in box.data.vertices];lid_points=[v.co for v in lid.data.vertices]
        midpoint=lambda points,i:(min(v[i] for v in points)+max(v[i] for v in points))/2
        assert abs(midpoint(box_points,0)-midpoint(lid_points,0))<.04
        assert abs(midpoint(box_points,1)-midpoint(lid_points,1))<.01
        pose_check=dict(ammo_box_level=True,lid_closed=True,lid_surface_tilt_degrees=lid_tilt,cartridge_cases='brass',projectiles='copper',links='dark steel')
    results[kind]=dict(source=meta['source_relative_to_ext'],source_sha256=meta['source_sha256'],projection='90 degree vertical orthographic',transparent=True,lights=0,
        materials='fixed RGB emission; no image textures',source_objects=len(original),polygons_preserved=sum(len(v) for v in seen.values()),used_source_vertices_preserved=sum(len(v) for v in vertex_ids.values()),uv_loops_preserved=uv_loops,
        added_geometry=False,gun_draw_tiles=meta['gun_draw_tiles'],mount_draw_tiles=meta['mount_draw_tiles'],uniform_surface_color_counts={name:len(colors) for name,colors in flat_colors.items()},**pose_check)
(OUT/'model_validation.json').write_text(json.dumps(dict(passed=True,models=results),indent=2)+'\n')
print('HMG_EXT_VALIDATION_PASS: both provided models retain every polygon, used vertex and UV loop; rigid transforms; vertical cameras; fixed emission',flush=True)
